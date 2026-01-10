// Assets/SONUS/Web/TargetManager.cs
using System.Collections;
using System.Reflection;
using UnityEngine;
using OnlineMaps;
using Sonus.Core;

public class TargetManager : MonoBehaviour
{
    [Header("Refs (from SonusMapSceneController)")]
    public SonusMapSceneController sceneController;
    public Transform playerRoot;




    [Header("2D Wiring (assign in Inspector)")]
    [Tooltip("Drag the Marker2DManager that belongs to the 2D map (Map Mode).")]
    public Marker2DManager markerManager2D;

    public Texture2D targetMarkerTexture;
    public float targetMarkerScale = 1f;

    [Header("3D Wiring")]
    [Tooltip("Prefab used by OnlineMaps Marker3D. Keep it small/simple.")]
    public GameObject targetPrefab;

    [Header("3D Target Visual")]
    public GameObject targetVisualPrefab;
    public Vector3 targetVisualOffset = new Vector3(0f, 0f, 0f);
    private Transform _targetVisual;

    [Tooltip("Extra Y offset for distance checks (world units).")]
    public float targetExtraYOffset = 0.2f;

    [Header("3D Visual Offset")]
    [Tooltip("Extra Y offset applied to the 3D marker transform AFTER OnlineMaps places it. Can be negative.")]
    public float targetVisualYOffset = 0f;

    [Header("Patrol")]
    public bool enablePatrol = true;
    public TargetPatrolManager patrolManager;

    [Tooltip("Meters for each patrol leg (Start->A, Start->B radius).")]
    public float patrolLegMeters = 100f;

    [Tooltip("Meters/sec along patrol path.")]
    public float patrolSpeedMps = 1.25f;

    [Header("Found / Arrival")]
    public bool enableArrivalRespawn = true;
    public float foundRadiusMeters = 20f;
    public float foundCooldownSeconds = 2f;

    private float _nextFoundAllowedTime;

    [Header("2D Motion / Redraw")]
    [Tooltip("How often to redraw the 2D map while the target is moving.")]
    public float map2DRedrawHz = 12f;

    private float _next2DRedrawTime;

    [Header("Run Settings")]
    [Tooltip("How many targets spawn in one run before prompting the user to run again.")]
    public int targetsPerRun = 3;

    [Header("Spawn Distance (feel)")]
    [Tooltip("Average spawn distance from player (meters).")]
    public float spawnDistanceMeters = 135f;

    [Tooltip("Random +/- jitter applied to spawnDistanceMeters (meters).")]
    public float spawnDistanceJitterMeters = 25f;

    [Tooltip("Clamp: never spawn closer than this distance (meters).")]
    public float spawnMinDistanceMeters = 90f;

    [Header("Spawn Bounds (2D view snapshot)")]
    public bool constrainToStartViewBounds = true;

    [Tooltip("Shrink the captured bounds inward (meters) to keep spawns away from the edge.")]
    public float boundsInsetMeters = 30f;

    [Header("Debug")]
    public bool debugLogs = true;

    // UI hooks (wire these from your UI layer)
    public System.Action OnRunComplete;
    public System.Action<int, int> OnRunProgressChanged; // (spawned, total)

    // ✅ Do NOT let Unity serialize a default TargetActor (0,0) into the component
    [System.NonSerialized] public TargetActor currentTarget;

    private Marker2D _marker2D;
    private Marker3D _marker3D;

    private Coroutine _sync3DRoutine;
    private Coroutine _recreate2DRoutine;

    private bool _isRespawning;
    private bool _in2DMode;

    // Marker3D lifecycle safety
    private bool _marker3DReady;
    private float _next3DResyncAllowedTime;

    // Run state
    private int _targetsSpawnedThisRun = 0;
    private bool _runActive = true;

    // Spawn bounds snapshot (captured at run start)
    private bool _hasBounds;
    private double _minLat, _maxLat, _minLon, _maxLon;

    // Direction variety (8 bins: N,NE,E,SE,S,SW,W,NW)
    private int _lastDirBin = -1;

    private float _lastDistanceMeters = -1f;

    private void Awake()
    {
        if (sceneController == null) sceneController = FindAny<SonusMapSceneController>();



        // ✅ Kill any inspector-serialized ghost target
        currentTarget = null;

        if (patrolManager == null) patrolManager = GetComponent<TargetPatrolManager>();
        if (patrolManager != null)
        {
            patrolManager.legMeters = patrolLegMeters;
            patrolManager.speedMps = patrolSpeedMps;
        }


    }

    private void Update()
    {

        if (_isRespawning) return;



        // 2) Patrol updates target geo (truth). Markers are just renderers.
        if (enablePatrol && patrolManager != null && HasValidTarget())
        {
            patrolManager.legMeters = patrolLegMeters;
            patrolManager.speedMps = patrolSpeedMps;

            if (patrolManager.Tick(Time.deltaTime, out double lat, out double lon))
            {
                currentTarget._Lat = lat;
                currentTarget._Lon = lon;
                // Debug2DSnapshot("PatrolTick", lon, lat);
                // Push into 3D marker if active AND ready (prevents OM NRE)
                if (_marker3DReady && _marker3D != null && _marker3D.enabled)
                {
                    try
                    {
                        _marker3D.location = new GeoPoint(lon, lat);
                        _marker3D.Update();
                    }
                    catch
                    {
                        _marker3DReady = false;

                        if (Time.time >= _next3DResyncAllowedTime)
                        {
                            _next3DResyncAllowedTime = Time.time + 1.0f;
                            if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
                            _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
                        }
                    }
                }

                // Always keep 2D marker location current.
                Update2DMarkerLocationSafe(lon, lat);
                if (debugLogs && _in2DMode && (Time.frameCount % 30 == 0))
                    Debug2DSnapshot("2DTick", lon, lat);

            }
        }

        // 3) Arrival / found must be GEO-only (world units are not meters; tileset scale/elevation can drift).
        if (!enableArrivalRespawn) return;

        _lastDistanceMeters = ComputeDistanceMeters();

        if (Time.time >= _nextFoundAllowedTime &&
            _lastDistanceMeters > 0f &&
            _lastDistanceMeters <= foundRadiusMeters)
        {
            _nextFoundAllowedTime = Time.time + foundCooldownSeconds;

            if (debugLogs)
                Debug.Log($"[TargetHunt] FOUND (d={_lastDistanceMeters:F1}m) -> respawn");

            AudioManager.Instance.PlayArrival(currentTarget);
            AudioManager.Instance.StopSonic();
            RequestRespawn();
        }
    }



    // ---------------------------
    // Mode hooks
    // ---------------------------

    public void OnEnter2D(Map map2D)
    {
        _in2DMode = true;

        // Wire FIRST
        if (markerManager2D == null && sceneController != null && sceneController.markerManager2D != null)
            markerManager2D = sceneController.markerManager2D;

        // 2) Force singleton deterministically on mode entry (prevents wrong-instance marker creation)
        Ensure2DManagerSingleton();

        Debug2DSnapshot("Enter2D");

        // 3) Ensure we have a target before creating marker
        EnsureTarget(map2D);

        // Stop + start exactly one routine
        if (_recreate2DRoutine != null) StopCoroutine(_recreate2DRoutine);
        _recreate2DRoutine = StartCoroutine(Recreate2DMarkerWhenReady());

        // 5) Hide 3D marker visuals safely
        Set3DEnabled(false);

        // 6) Redraw the 2D map (harmless even if not fully ready)
        map2D?.Redraw();

        // Debug snapshot
        Debug2DSnapshot("Enter2D");

        if (debugLogs && map2D != null && currentTarget != null)
        {
            var c = map2D.view.center;
            Debug.Log($"[TargetHunt] Enter2D center=({c.y:F6},{c.x:F6}) target=({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
        }
    }


    public void OnEnter3D()
    {
        _in2DMode = false;

        if (debugLogs) Debug.Log("[TargetHunt] Enter3D");

        // Stop any pending 2D recreate routine so it doesn't fight you
        if (_recreate2DRoutine != null)
        {
            StopCoroutine(_recreate2DRoutine);
            _recreate2DRoutine = null;
        }

        EnsureTarget(sceneController != null ? sceneController.map2D : null);

        // Allow 3D to render before we sync
        Set3DEnabled(true);

        if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
        _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
    }


    // ---------------------------
    // Run control (called by UI)
    // ---------------------------

    public void StartNewRun()
    {
        _runActive = true;
        _targetsSpawnedThisRun = 0;

        CaptureStartBounds();

        currentTarget = null;
        EnsureTarget(sceneController != null ? sceneController.map2D : null);

        if (_in2DMode)
            Kick2DMarkerCreateIf2DActive();
        else
        {
            if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
            _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
        }

        OnRunProgressChanged?.Invoke(_targetsSpawnedThisRun, targetsPerRun);

        if (debugLogs)
            Debug.Log("[TargetHunt] New run started.");
    }

    private void CaptureStartBounds()
    {
        _hasBounds = false;
        _lastDirBin = -1;

        if (!constrainToStartViewBounds) return;

        var map2D = sceneController != null ? sceneController.map2D : null;
        if (map2D == null || map2D.view == null) return;

        // GeoPoint: x=lng, y=lat
        var tl = map2D.view.topLeft;
        var br = map2D.view.bottomRight;

        _minLat = System.Math.Min(tl.y, br.y);
        _maxLat = System.Math.Max(tl.y, br.y);
        _minLon = System.Math.Min(tl.x, br.x);
        _maxLon = System.Math.Max(tl.x, br.x);

        // Inset to keep spawns away from the edge
        if (boundsInsetMeters > 0)
        {
            double midLat = (_minLat + _maxLat) * 0.5;
            TargetGeoUtil.MetersToLatLonDeltas(midLat, boundsInsetMeters, out double dLat, out double dLon);

            _minLat += dLat; _maxLat -= dLat;
            _minLon += dLon; _maxLon -= dLon;
        }

        _hasBounds = true;

        if (debugLogs)
            Debug.Log($"[TargetHunt] Bounds captured lat[{_minLat:F6},{_maxLat:F6}] lon[{_minLon:F6},{_maxLon:F6}] (inset {boundsInsetMeters}m)");
    }

    // ---------------------------
    // Target creation
    // ---------------------------

    private void EnsureTarget(Map map2D)
    {
        if (HasValidTarget()) return;

        if (!_runActive || _targetsSpawnedThisRun >= targetsPerRun)
            return;

        if (!TryGetPlayerLatLon(map2D, out double baseLat, out double baseLon))
            return;

        (double tLat, double tLon) = GenerateRandomGeoOffset(baseLat, baseLon);
        ApplyNewTarget(tLat, tLon, baseLat, baseLon);
    }

    private void ApplyNewTarget(double tLat, double tLon, double baseLat, double baseLon)
    {
        if (!_runActive || _targetsSpawnedThisRun >= targetsPerRun) return;

        currentTarget = new TargetActor(TargetType.STATIONARY, tLat, tLon)
        {
            _Name = $"Target {_targetsSpawnedThisRun + 1}"
        };

        _targetsSpawnedThisRun++;
        OnRunProgressChanged?.Invoke(_targetsSpawnedThisRun, targetsPerRun);

        if (enablePatrol && patrolManager != null)
        {
            patrolManager.legMeters = patrolLegMeters;
            patrolManager.speedMps = patrolSpeedMps;
            patrolManager.AssignRoute(currentTarget._Lat, currentTarget._Lon);
        }

        if (debugLogs)
        {
            double approxGeo = TargetGeoUtil.ApproxMetersBetween(baseLat, baseLon, tLat, tLon);
            Debug.Log($"[TargetHunt] Spawned target #{_targetsSpawnedThisRun}/{targetsPerRun} geoDist≈{approxGeo:F0}m @ ({tLat:F6},{tLon:F6})");
        }

        // NEW: sonic reset + initial callout + periodic loop
        AudioManager.Instance.OnTargetChanged(currentTarget);
        AudioManager.Instance.PlayInitialDirectionForTarget(currentTarget, true);
        AudioManager.Instance.StartSonic();


        if (!_in2DMode)
        {
            if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
            _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
        }
    }

    private (double lat, double lon) GenerateRandomGeoOffset(double lat0, double lon0)
    {
        const int attempts = 24;

        for (int i = 0; i < attempts; i++)
        {
            int bin = PickDirectionBin(lat0, lon0);
            double bearing = BinToBearingRad(bin);

            float distM = spawnDistanceMeters + Random.Range(-spawnDistanceJitterMeters, spawnDistanceJitterMeters);
            distM = Mathf.Max(distM, spawnMinDistanceMeters);

            double northM = System.Math.Cos(bearing) * distM;
            double eastM = System.Math.Sin(bearing) * distM;

            var (tLat, tLon) = TargetGeoUtil.OffsetLatLonMeters(lat0, lon0, northM, eastM);

            if (!_hasBounds || PointInBounds(tLat, tLon))
            {
                _lastDirBin = bin;
                return (tLat, tLon);
            }
        }

        if (_hasBounds)
            return ClampIntoBounds(lat0, lon0);

        double t = Random.value * System.Math.PI * 2.0;
        float d = Mathf.Max(spawnDistanceMeters, spawnMinDistanceMeters);
        double n = System.Math.Cos(t) * d;
        double e = System.Math.Sin(t) * d;
        return TargetGeoUtil.OffsetLatLonMeters(lat0, lon0, n, e);
    }

    private bool PointInBounds(double lat, double lon)
    {
        return lat >= _minLat && lat <= _maxLat && lon >= _minLon && lon <= _maxLon;
    }

    private (double lat, double lon) ClampIntoBounds(double lat, double lon)
    {
        double cLat = System.Math.Max(_minLat, System.Math.Min(_maxLat, lat));
        double cLon = System.Math.Max(_minLon, System.Math.Min(_maxLon, lon));
        return (cLat, cLon);
    }

    private int PickDirectionBin(double pLat, double pLon)
    {
        if (!_hasBounds)
        {
            int b = Random.Range(0, 8);
            if (_lastDirBin >= 0 && b == _lastDirBin) b = (b + Random.Range(1, 8)) % 8;
            return b;
        }

        const double marginM = 70.0;

        double toNorthM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, _maxLat, pLon);
        double toSouthM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, _minLat, pLon);
        double toEastM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, pLat, _maxLon);
        double toWestM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, pLat, _minLon);

        bool banN = toNorthM < marginM;
        bool banS = toSouthM < marginM;
        bool banE = toEastM < marginM;
        bool banW = toWestM < marginM;

        var cand = new System.Collections.Generic.List<int>(8);

        for (int b = 0; b < 8; b++)
        {
            if (_lastDirBin >= 0 && b == _lastDirBin) continue;

            bool usesN = (b == 0 || b == 1 || b == 7);
            bool usesS = (b == 4 || b == 3 || b == 5);
            bool usesE = (b == 2 || b == 1 || b == 3);
            bool usesW = (b == 6 || b == 7 || b == 5);

            if (banN && usesN) continue;
            if (banS && usesS) continue;
            if (banE && usesE) continue;
            if (banW && usesW) continue;

            cand.Add(b);
        }

        if (cand.Count == 0)
        {
            for (int b = 0; b < 8; b++)
            {
                if (_lastDirBin >= 0 && b == _lastDirBin) continue;
                cand.Add(b);
            }
        }

        if (cand.Count == 0) return Random.Range(0, 8);
        return cand[Random.Range(0, cand.Count)];
    }

    private static double BinToBearingRad(int bin)
    {
        return (System.Math.PI / 4.0) * bin;
    }

    private bool HasValidTarget()
    {
        if (currentTarget == null) return false;
        return !(System.Math.Abs(currentTarget._Lat) < 1e-9 && System.Math.Abs(currentTarget._Lon) < 1e-9);
    }

    private static bool IsValidLatLon(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon)) return false;
        if (double.IsInfinity(lat) || double.IsInfinity(lon)) return false;
        if (lat < -90 || lat > 90) return false;
        if (lon < -180 || lon > 180) return false;

        // Treat (0,0) as invalid for our app context.
        if (System.Math.Abs(lat) < 1e-9 && System.Math.Abs(lon) < 1e-9) return false;

        return true;
    }

    private bool TryGetPlayerLatLon(Map map2D, out double lat, out double lon)
    {
        // Prefer authoritative player geo (3D-derived) in ALL modes if available.
        if (SonusPlayerGeoState.HasValue)
        {
            lat = SonusPlayerGeoState.Lat;
            lon = SonusPlayerGeoState.Lng;
            return IsValidLatLon(lat, lon);
        }

        // 2D fallback: map center (only if we truly have no player geo)
        if (map2D != null)
        {
            lon = map2D.view.center.x;
            lat = map2D.view.center.y;
            return IsValidLatLon(lat, lon);
        }

        lat = lon = 0;
        return false;
    }


    // ---------------------------
    // 2D marker (safe create + update)
    // ---------------------------

    private void Kick2DMarkerCreateIf2DActive()
    {
        if (_recreate2DRoutine != null) StopCoroutine(_recreate2DRoutine);
        _recreate2DRoutine = StartCoroutine(Recreate2DMarkerWhenReady());
    }

    private void Update2DMarkerLocationSafe(double lon, double lat)
    {
        if (_marker2D == null) return;

        // ✅ Always keep marker geo current. Do NOT gate on Is2DReady().
        _marker2D.location = new GeoPoint(lon, lat);

        // Only redraw if we’re actually viewing 2D and the map object exists/active.
        if (!_in2DMode) return;
        if (markerManager2D == null) return;

        var map = markerManager2D.map;
        if (map == null) return;
        if (!map.gameObject.activeInHierarchy) return;

        // Keep singleton consistent (but don’t require map.control enabled).
        Ensure2DManagerSingleton();

        if (Time.time >= _next2DRedrawTime)
        {
            _next2DRedrawTime = Time.time + (1f / Mathf.Max(1f, map2DRedrawHz));
            map.Redraw();
        }
    }


    private bool Is2DReady()
    {
        if (markerManager2D == null) return false;
        if (!markerManager2D.gameObject.activeInHierarchy) return false;

        var map = markerManager2D.map;
        if (map == null) return false;
        if (!map.gameObject.activeInHierarchy) return false;

        return true;
    }

    private IEnumerator Recreate2DMarkerWhenReady()
    {
        if (!_in2DMode) yield break;

        // Let OnlineMaps settle after mode toggle
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;

        // Ensure manager is wired
        if (markerManager2D == null && sceneController != null && sceneController.markerManager2D != null)
            markerManager2D = sceneController.markerManager2D;

        const float timeout = 3.0f;
        float t0 = Time.realtimeSinceStartup;

        // Wait until the map exists and is active (do NOT depend on control enabled)
        while (_in2DMode &&
               (markerManager2D == null ||
                !markerManager2D.gameObject.activeInHierarchy ||
                markerManager2D.map == null ||
                !markerManager2D.map.gameObject.activeInHierarchy) &&
               (Time.realtimeSinceStartup - t0) < timeout)
        {
            yield return null;
        }

        if (!_in2DMode) yield break;

        if (markerManager2D == null || markerManager2D.map == null)
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] 2D map not available; skipping marker ensure.");
            Debug2DSnapshot("Recreate2DNoMap");
            yield break;
        }

        if (targetMarkerTexture == null || currentTarget == null)
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] Missing texture or target; cannot ensure 2D marker.");
            Debug2DSnapshot("Recreate2DMissingDeps");
            yield break;
        }

        // Force singleton BEFORE any marker calls
        Ensure2DManagerSingleton();

        // ✅ If we already have a marker, DO NOT delete/recreate it.
        // Just ensure it’s enabled, bound, and redrawn.
        if (_marker2D != null)
        {
            try
            {
                _marker2D.enabled = true;
                _marker2D["data"] = currentTarget;
                _marker2D.location = new GeoPoint(currentTarget._Lon, currentTarget._Lat);

                markerManager2D.map.Redraw();
                Debug2DSnapshot("Recreate2DReusedMarkerDone");

                if (debugLogs)
                    Debug.Log("[TargetHunt] 2D marker reused (no recreate).");

                yield break;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[TargetHunt] 2D reuse exception: {ex.GetType().Name}: {ex.Message}");
                Debug2DSnapshot("Recreate2DReuseException");
                yield break;
            }
        }

        // Otherwise create once (no SafeRemove here — there is nothing to remove)
        try
        {
            _marker2D = Marker2DManager.CreateItem(
                currentTarget._Lon,
                currentTarget._Lat,
                targetMarkerTexture,
                "target"
            );

            if (_marker2D == null)
            {
                Debug.LogWarning("[TargetHunt] 2D CreateItem returned null.");
                Debug2DSnapshot("Recreate2DNullCreate");
                yield break;
            }

            _marker2D.align = Align.Center;
            _marker2D.scale = targetMarkerScale;
            _marker2D.enabled = true;
            _marker2D["data"] = currentTarget;

            _marker2D.location = new GeoPoint(currentTarget._Lon, currentTarget._Lat);

            markerManager2D.map.Redraw();
            Debug2DSnapshot("Recreate2DMarkerDone");

            if (debugLogs)
                Debug.Log($"[TargetHunt] 2D marker created @ ({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TargetHunt] 2D CreateItem exception: {ex.GetType().Name}: {ex.Message}");
            Debug2DSnapshot("Recreate2DException");
            // IMPORTANT: leave _marker2D as null; Update2DMarkerLocationSafe will safely no-op.
        }
    }


    private void SafeRemove2DMarker()
    {
        if (_marker2D == null) return;

        try
        {
            Ensure2DManagerSingleton();
            Marker2DManager.RemoveItem(_marker2D);
        }
        catch
        {
            try { _marker2D.enabled = false; } catch { }
        }

        _marker2D = null;
    }


    // ---------------------------
    // 3D marker
    // ---------------------------

    private void Ensure3DTargetVisual()
    {
        if (targetVisualPrefab == null) return;
        if (_marker3D == null || _marker3D.transform == null) return;

        var existing = _marker3D.transform.Find("TargetVisual");
        if (existing != null) return;

        var vis = Instantiate(targetVisualPrefab, _marker3D.transform);
        vis.name = "TargetVisual";
        vis.transform.localPosition = Vector3.zero;          // or (0,2,0) if needed
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale = Vector3.one;
    }


    private IEnumerator Ensure3DMarkerAndSync()
    {
        if (currentTarget == null) yield break;

        _marker3DReady = false;

        const float timeout = 10f;
        float t0 = Time.realtimeSinceStartup;

        while (Marker3DManager.instance == null && Time.realtimeSinceStartup - t0 < timeout)
            yield return null;

        if (Marker3DManager.instance == null)
        {
            Debug.LogWarning("[TargetHunt] Marker3DManager.instance is null.");
            yield break;
        }

        if (_marker3D == null)
        {
            if (targetPrefab == null)
            {
                Debug.LogWarning("[TargetHunt] targetPrefab is null.");
                yield break;
            }

            _marker3D = Marker3DManager.CreateItem(currentTarget._Lon, currentTarget._Lat, targetPrefab, "target-3d");

            if (_marker3D == null)
            {
                Debug.LogWarning("[TargetHunt] 3D CreateItem returned null.");
                yield break;
            }

            _marker3D.sizeType = Marker3D.SizeType.scene;


        }

        if (_marker3D.transform != null)
            _marker3D.transform.gameObject.SetActive(true);

        // NEW: always ensure the child exists
        Ensure3DTargetVisual();

        _marker3D.location = new GeoPoint(currentTarget._Lon, currentTarget._Lat);

        try { _marker3D.enabled = true; } catch { }

        try
        {
            _marker3D.Update();
        }
        catch
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] Marker3D.Update threw during sync; will retry next Enter3D.");
            yield break;
        }


        for (int i = 0; i < 10; i++) yield return null;

        _marker3DReady = (_marker3D != null && _marker3D.enabled && _marker3D.transform != null);

        if (debugLogs && _marker3D != null && _marker3D.transform != null)
            Debug.Log($"[TargetHunt] 3D marker pos={_marker3D.transform.position} target=({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
    }

    private void Set3DEnabled(bool enabled)
    {
        if (_marker3D == null) return;

        try
        {
            if (_marker3D.transform != null)
                _marker3D.transform.gameObject.SetActive(enabled);

            if (enabled)
                _marker3D.enabled = true;
        }
        catch { }

        if (!enabled) _marker3DReady = false;
    }

    // ---------------------------
    // Singleton forcing (2D)
    // ---------------------------

    private void Ensure2DManagerSingleton()
    {
        if (markerManager2D == null) return;

        var t = typeof(Marker2DManager);
        var f = t.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null) return;
        f.SetValue(null, markerManager2D);
    }


    // ---------------------------
    // Respawn
    // ---------------------------

    private IEnumerator RespawnFlow()
    {
        Set3DEnabled(false);

        currentTarget = null;

        if (_targetsSpawnedThisRun >= targetsPerRun)
        {
            _runActive = false;

            if (debugLogs)
                Debug.Log("[TargetHunt] Run complete. Waiting for user to start another run.");

            AudioManager.Instance.PlayMissionComplete();
            AudioManager.Instance.StopSonic();
            OnRunComplete?.Invoke();
            _isRespawning = false;
            yield break;
        }

        EnsureTarget(sceneController != null ? sceneController.map2D : null);

        if (enablePatrol && patrolManager != null && HasValidTarget())
            patrolManager.AssignRoute(currentTarget._Lat, currentTarget._Lon);

        if (_in2DMode)
            Kick2DMarkerCreateIf2DActive();

        if (!_in2DMode)
        {
            if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
            _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
        }

        yield return null;

        _isRespawning = false;
    }

    public bool TryGetTargetWorldPos(out Vector3 pos)
    {
        pos = default;

        if (!_marker3DReady) return false;
        if (_marker3D == null || !_marker3D.enabled || _marker3D.transform == null) return false;

        pos = _marker3D.transform.position;
        return true;
    }

    public void RequestRespawn()
    {
        if (_isRespawning) return;
        _isRespawning = true;
        StartCoroutine(RespawnFlow());
    }

    // ---------------------------
    // Public helpers
    // ---------------------------

    public float DistanceToTargetMeters()
    {
        if (currentTarget == null) return float.PositiveInfinity;

        if (!Sonus.Core.SonusPlayerGeoState.HasValue)
            return float.PositiveInfinity;

        double pLat = Sonus.Core.SonusPlayerGeoState.Lat;
        double pLon = Sonus.Core.SonusPlayerGeoState.Lng;

        double tLat = currentTarget._Lat;
        double tLon = currentTarget._Lon;

        return (float)TargetGeoUtil.ApproxMetersBetween(pLat, pLon, tLat, tLon);
    }


    public bool TryGetPlayerLatLonForUI(out double lat, out double lon)
    {
        return TryGetPlayerLatLon(sceneController != null ? sceneController.map2D : null, out lat, out lon);
    }

    private float ComputeDistanceMeters()
    {
        if (currentTarget == null) return -1f;

        if (!TryGetPlayerLatLon(sceneController != null ? sceneController.map2D : null, out double pLat, out double pLon))
            return -1f;

        double m = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, currentTarget._Lat, currentTarget._Lon);
        return (float)m;
    }

    void Debug2DSnapshot(string tag, double lon = double.NaN, double lat = double.NaN)
    {
        if (!debugLogs) return;

        var map = markerManager2D != null ? markerManager2D.map : null;
        var ctrl = map != null ? map.control : null;

        string m2d = _marker2D != null
            ? $"m2d#{_marker2D.GetHashCode()} loc=({_marker2D.location.y:F6},{_marker2D.location.x:F6})"
            : "m2d=null";

        string mapStr = map != null
            ? $"map#{map.GetHashCode()} active={map.gameObject.activeInHierarchy}"
            : "map=null";

        string ctrlStr = ctrl != null
            ? $"ctrl#{ctrl.GetHashCode()} enabled={ctrl.enabled} active={ctrl.gameObject.activeInHierarchy}"
            : "ctrl=null";

        Debug.Log($"[TargetHunt][{tag}] in2D={_in2DMode} {m2d} {mapStr} {ctrlStr} " +
                  $"set=({lat:F6},{lon:F6})");
    }


    private static T FindAny<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        var obj = Object.FindFirstObjectByType<T>();
        if (obj != null) return obj;
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
