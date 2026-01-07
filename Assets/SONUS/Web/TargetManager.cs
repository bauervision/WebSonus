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

        // Patrol should run even in 2D so the icon moves on the 2D map.
        if (enablePatrol && patrolManager != null && HasValidTarget())
        {
            // Keep patrol settings live-tunable
            patrolManager.legMeters = patrolLegMeters;
            patrolManager.speedMps = patrolSpeedMps;

            if (patrolManager.Tick(Time.deltaTime, out double lat, out double lon))
            {
                currentTarget._Lat = lat;
                currentTarget._Lon = lon;

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
                        // OM can throw if 3D stack isn't fully alive yet; self-heal.
                        _marker3DReady = false;

                        if (Time.time >= _next3DResyncAllowedTime)
                        {
                            _next3DResyncAllowedTime = Time.time + 1.0f;
                            if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
                            _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
                        }
                    }
                }

                // Push into 2D marker if it exists/ready
                Update2DMarkerLocationSafe(lon, lat);
            }
        }

        // Arrival / found should be based on 3D world position (elevation-aware).
        // If 3D marker isn't active, do nothing (we don't want 2D-only "found").
        if (!enableArrivalRespawn) return;
        if (playerRoot == null) return;
        if (_marker3D == null || !_marker3D.enabled || _marker3D.transform == null) return;

        Vector3 p = _marker3D.transform.position;
        if (p == Vector3.zero) return;

        // NOTE: do NOT write to marker transform each frame here (OM may also move it).
        // Instead, apply offsets only for the measurement.
        float yVisual = targetVisualYOffset;
        float yExtra = targetExtraYOffset;

        Vector3 measure = new Vector3(p.x, p.y + yVisual + yExtra, p.z);

        // Unity world distance (units)
        float dWorld = Vector3.Distance(playerRoot.position, measure);

        // Geo distance (meters) — this is what we expose to UI
        _lastDistanceMeters = ComputeDistanceMeters();

        if (Time.time >= _nextFoundAllowedTime && _lastDistanceMeters > 0f && _lastDistanceMeters <= foundRadiusMeters)
        {
            _nextFoundAllowedTime = Time.time + foundCooldownSeconds;

            if (debugLogs)
                Debug.Log($"[TargetHunt] FOUND (d={_lastDistanceMeters:F1}m) -> respawn");

            RequestRespawn();
        }

    }

    // ---------------------------
    // Mode hooks
    // ---------------------------

    public void OnEnter2D(Map map2D)
    {
        _in2DMode = true;

        EnsureTarget(map2D);

        // Never create 2D markers immediately on mode switch; OnlineMaps may not be initialized yet.
        Kick2DMarkerCreateIf2DActive();

        // Do NOT disable Marker3D via OM here (can NRE during teardown). Just hide GO safely.
        Set3DEnabled(false);

        map2D?.Redraw();

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

        EnsureTarget(sceneController != null ? sceneController.map2D : null);

        // Make sure the marker GO is allowed to render before we sync
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
        // If run is over, don't apply
        if (!_runActive || _targetsSpawnedThisRun >= targetsPerRun) return;

        currentTarget = new TargetActor(TargetType.STATIONARY, tLat, tLon)
        {
            _Name = $"Target {_targetsSpawnedThisRun + 1}"
        };

        _targetsSpawnedThisRun++;
        OnRunProgressChanged?.Invoke(_targetsSpawnedThisRun, targetsPerRun);

        // Seed patrol
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

        // If we're in 3D, kick a proper sync (now that currentTarget is committed)
        if (!_in2DMode)
        {
            if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
            _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
        }
    }

    private (double lat, double lon) GenerateRandomGeoOffset(double lat0, double lon0)
    {
        // If we have bounds, prefer directions that keep us inside the box.
        // Try a bunch of candidates; accept first that lands inside bounds.
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

        // Fallback: if we're boxed-in somehow, clamp the player's location into the bounds
        if (_hasBounds)
            return ClampIntoBounds(lat0, lon0);

        // Final fallback (unbounded)
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
        // If no bounds, just pick any bin, avoid immediate repeat.
        if (!_hasBounds)
        {
            int b = Random.Range(0, 8);
            if (_lastDirBin >= 0 && b == _lastDirBin) b = (b + Random.Range(1, 8)) % 8;
            return b;
        }

        // Margin near edge where we start “pushing inward”
        const double marginM = 70.0;

        double toNorthM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, _maxLat, pLon);
        double toSouthM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, _minLat, pLon);
        double toEastM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, pLat, _maxLon);
        double toWestM = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, pLat, _minLon);

        bool banN = toNorthM < marginM;
        bool banS = toSouthM < marginM;
        bool banE = toEastM < marginM;
        bool banW = toWestM < marginM;

        // Candidate bins, filtered by edge bans
        // bins: 0=N,1=NE,2=E,3=SE,4=S,5=SW,6=W,7=NW
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

        // If filtered everything, relax bans (still avoid repeat if possible)
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
        // 0=N,2=E,4=S,6=W
        // Using: north=cos(bearing)*d, east=sin(bearing)*d
        return (System.Math.PI / 4.0) * bin;
    }

    private bool HasValidTarget()
    {
        if (currentTarget == null) return false;
        return !(System.Math.Abs(currentTarget._Lat) < 1e-9 && System.Math.Abs(currentTarget._Lon) < 1e-9);
    }

    private bool TryGetPlayerLatLon(Map map2D, out double lat, out double lon)
    {
        // Best source: SonusLocationState (if your system is updating it correctly)
        lat = SonusLocationState.Lat;
        lon = SonusLocationState.Lng;
        if (System.Math.Abs(lat) > 1e-9 || System.Math.Abs(lon) > 1e-9)
            return true;

        // Fallback: 2D map center
        if (map2D != null)
        {
            lon = map2D.view.center.x;
            lat = map2D.view.center.y;
            if (System.Math.Abs(lat) > 1e-9 || System.Math.Abs(lon) > 1e-9)
                return true;
        }

        // Last resort: defaults
        if (sceneController != null)
        {
            lat = sceneController.defaultLatitude;
            lon = sceneController.defaultLongitude;
            if (System.Math.Abs(lat) > 1e-9 || System.Math.Abs(lon) > 1e-9)
                return true;
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
        if (!_in2DMode) return; // only redraw when user is looking at 2D
        if (!Is2DReady()) return;

        Ensure2DManagerSingleton();

        _marker2D.location = new GeoPoint(lon, lat);

        if (markerManager2D != null && markerManager2D.map != null && Time.time >= _next2DRedrawTime)
        {
            _next2DRedrawTime = Time.time + (1f / Mathf.Max(1f, map2DRedrawHz));
            markerManager2D.map.Redraw();
        }
    }

    private bool Is2DReady()
    {
        if (markerManager2D == null) return false;
        if (!markerManager2D.gameObject.activeInHierarchy) return false;

        var map = markerManager2D.map;
        if (map == null) return false;
        if (!map.gameObject.activeInHierarchy) return false;

        // Must have a 2D control and it must be active/enabled
        var ctrl = map.control;
        if (ctrl == null) return false;
        if (!ctrl.enabled) return false;
        if (!ctrl.gameObject.activeInHierarchy) return false;

        return true;
    }

    private IEnumerator Recreate2DMarkerWhenReady()
    {
        // If we aren't actually in 2D mode anymore, don't create 2D markers.
        if (!_in2DMode) yield break;

        // OnlineMaps often needs EndOfFrame to initialize marker buffers after re-activation
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return new WaitForEndOfFrame();

        float timeout = 3.0f;
        float t0 = Time.realtimeSinceStartup;

        while (_in2DMode && !Is2DReady() && (Time.realtimeSinceStartup - t0) < timeout)
            yield return null;

        if (!_in2DMode) yield break;

        if (!Is2DReady())
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] 2D not ready; skipping marker create.");
            yield break;
        }

        if (targetMarkerTexture == null || currentTarget == null)
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] Missing texture or target; cannot create 2D marker.");
            yield break;
        }

        Ensure2DManagerSingleton();

        // Remove prior marker
        SafeRemove2DMarker();

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
                yield break;
            }

            _marker2D.align = Align.Center;
            _marker2D.scale = targetMarkerScale;
            _marker2D.enabled = true;
            _marker2D["data"] = currentTarget;

            markerManager2D.map?.Redraw();

            if (debugLogs)
                Debug.Log($"[TargetHunt] 2D marker created @ ({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TargetHunt] 2D CreateItem exception: {ex.GetType().Name}: {ex.Message}");
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

            _marker3D = Marker3DManager.CreateItem(0, 0, targetPrefab, "target-3d");
            if (_marker3D == null)
            {
                Debug.LogWarning("[TargetHunt] 3D CreateItem returned null.");
                yield break;
            }

            _marker3D.sizeType = Marker3D.SizeType.scene;
        }

        // Ensure the marker GO is visible (safe) before update
        if (_marker3D.transform != null)
            _marker3D.transform.gameObject.SetActive(true);

        _marker3D.location = new GeoPoint(currentTarget._Lon, currentTarget._Lat);

        // Only set enabled when turning ON (disabling via enabled can throw in some OM lifecycles)
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

        // Let OM resolve transform
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
            // Prefer toggling the marker instance GO over Marker3D.enabled, because enabled can NRE during mode teardown.
            if (_marker3D.transform != null)
                _marker3D.transform.gameObject.SetActive(enabled);

            // Only toggle enabled when turning ON. Turning OFF via enabled has NRE'd for you.
            if (enabled)
                _marker3D.enabled = true;
        }
        catch
        {
            // OM may be mid-teardown; ignore and let next Enter3D resync.
        }

        if (!enabled) _marker3DReady = false;
    }

    // ---------------------------
    // Singleton forcing (2D)
    // ---------------------------

    private void Ensure2DManagerSingleton()
    {
        // NOTE: Do not require Is2DReady here; we call this only when we already validated readiness.
        if (markerManager2D == null) return;

        var t = typeof(Marker2DManager);
        var f = t.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(null, markerManager2D);
    }

    // ---------------------------
    // Respawn
    // ---------------------------

    private IEnumerator RespawnFlow()
    {
        // Hide 3D marker safely (don't disable via OM)
        Set3DEnabled(false);

        currentTarget = null;

        // If we've already spawned the full run, complete it now.
        if (_targetsSpawnedThisRun >= targetsPerRun)
        {
            _runActive = false;

            if (debugLogs)
                Debug.Log("[TargetHunt] Run complete. Waiting for user to start another run.");

            OnRunComplete?.Invoke();
            _isRespawning = false;
            yield break;
        }

        EnsureTarget(sceneController != null ? sceneController.map2D : null);

        // Re-arm patrol route for the new target
        if (enablePatrol && patrolManager != null && HasValidTarget())
            patrolManager.AssignRoute(currentTarget._Lat, currentTarget._Lon);

        // If we're in 2D, re-create marker safely (deferred). If we're in 3D, leave 2D alone.
        if (_in2DMode)
            Kick2DMarkerCreateIf2DActive();

        // If we're in 3D, resync the 3D marker
        if (!_in2DMode)
        {
            if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
            _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
        }

        // Give marker sync a moment (non-blocking) — avoids reticle flicker on instant respawn
        yield return null;

        _isRespawning = false;
    }

    public bool TryGetTargetWorldPos(out Vector3 pos)
    {
        pos = default;

        if (!_marker3DReady) return false;
        if (_marker3D == null || !_marker3D.enabled || _marker3D.transform == null) return false;

        pos = _marker3D.transform.position;
        return pos != Vector3.zero;
    }

    public void RequestRespawn()
    {
        if (_isRespawning) return;
        _isRespawning = true;
        StartCoroutine(RespawnFlow());
    }

    // ---------------------------
    // Helpers
    // ---------------------------
    private float _lastDistanceMeters = -1f;
    /// <summary>
    /// Returns the last computed target distance in meters.
    /// Falls back to computing from lat/lon if needed.
    /// </summary>
    public float DistanceToTargetMeters()
    {
        if (currentTarget == null) return float.PositiveInfinity;

        if (!TryGetPlayerLatLon(sceneController != null ? sceneController.map2D : null, out double pLat, out double pLon))
            return float.PositiveInfinity;

        double tLat = currentTarget._Lat;
        double tLon = currentTarget._Lon;

        return (float)TargetGeoUtil.ApproxMetersBetween(pLat, pLon, tLat, tLon);
    }

    public bool TryGetPlayerLatLonForUI(out double lat, out double lon)
    {
        return TryGetPlayerLatLon(sceneController != null ? sceneController.map2D : null, out lat, out lon);
    }
    /// <summary>
    /// Compute distance in meters using lat/lon (player vs target).
    /// This avoids Unity world scale issues with TileSet size.
    /// </summary>
    private float ComputeDistanceMeters()
    {
        if (currentTarget == null) return -1f;

        if (!TryGetPlayerLatLon(sceneController != null ? sceneController.map2D : null, out double pLat, out double pLon))
            return -1f;

        double m = TargetGeoUtil.ApproxMetersBetween(pLat, pLon, currentTarget._Lat, currentTarget._Lon);
        return (float)m;
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
