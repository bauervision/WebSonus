// Assets/SONUS/Web/TargetManager.cs
using System.Collections;
using System.Reflection;
using UnityEngine;
using OnlineMaps;
using Sonus.Core;

[System.Serializable]
public struct PresetGeoPoint
{
    public double lat;
    public double lon;

    public PresetGeoPoint(double lat, double lon)
    {
        this.lat = lat;
        this.lon = lon;
    }

    public override string ToString() => $"({lat:F6},{lon:F6})";
}

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

    [Header("Preset Targets")]
    public bool usePresetTargets = true;

    public PresetGeoPoint[] presetTargets = new PresetGeoPoint[]
    {
        new(37.305458, -80.612394),
        new(37.306329, -80.610859),
        new(37.306515, -80.613209),
        new(37.303756, -80.612456),
        new(37.304914, -80.611766),
        new(37.304215, -80.610174),
        new(37.306193, -80.609963),
        new(37.305381, -80.610454),
        new(37.307287, -80.610910),
        new(37.306878, -80.614156),
        new(37.305340, -80.614054),
        new(37.306148, -80.608554),
    };

    [Header("Preset Selection")]
    public bool firstTargetRandomThenClosest = true;

    [Tooltip("If true, we won't immediately re-use the exact last preset index.")]
    public bool avoidImmediateRepeat = true;

    private bool _hasPickedFirstTarget;
    private int _lastPresetIndex = -1;

    [Header("Local Presets (auto-generate)")]
    public bool autoGeneratePresetsNearPlayer = true;
    public int autoPresetCount = 12;
    public float autoPresetRadiusMeters = 450f; // how far out the cloud spreads
    public float autoPresetMinRadiusMeters = 120f; // keep them from clustering on top of you
    public int autoPresetSeed = 0; // 0 = random each run; set a number for repeatable



    private int[] _presetBag;
    private int _presetBagIndex;

    [Header("Debug")]
    public bool debugLogs = true;

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

        // We want patrol to run even in 2D so the icon moves on the 2D map.
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

        float d = Vector3.Distance(playerRoot.position, measure);

        if (Time.time >= _nextFoundAllowedTime && d <= foundRadiusMeters)
        {
            _nextFoundAllowedTime = Time.time + foundCooldownSeconds;

            if (debugLogs)
                Debug.Log($"[TargetHunt] FOUND (d={d:F1}m) -> respawn");

            RequestRespawn();
        }
    }

    // ---------------------------
    // Mode hooks
    // ---------------------------

    public void OnEnter2D(Map map2D)
    {
        _in2DMode = true;

        EnsureLocalPresetTargets(map2D);
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
    // Target
    // ---------------------------


    private void EnsureLocalPresetTargets(Map map2D)
    {
        if (!autoGeneratePresetsNearPlayer) return;
        if (presetTargets != null && presetTargets.Length > 0 && _hasPickedFirstTarget) return;
        // (optional) don’t regenerate mid-run once you’ve started picking.

        if (!TryGetPlayerLatLon(out double lat0, out double lon0))
        {
            // Fallback to map center if needed
            if (map2D != null)
            {
                lon0 = map2D.view.center.x;
                lat0 = map2D.view.center.y;
            }
        }

        if (System.Math.Abs(lat0) < 1e-9 && System.Math.Abs(lon0) < 1e-9) return;

        var rng = (autoPresetSeed == 0) ? new System.Random() : new System.Random(autoPresetSeed);

        presetTargets = new PresetGeoPoint[autoPresetCount];

        for (int i = 0; i < autoPresetCount; i++)
        {
            // uniform-ish ring distribution
            double t = rng.NextDouble() * System.Math.PI * 2.0;
            double u = rng.NextDouble();
            double r = autoPresetMinRadiusMeters + (autoPresetRadiusMeters - autoPresetMinRadiusMeters) * System.Math.Sqrt(u);

            double northM = System.Math.Cos(t) * r;
            double eastM = System.Math.Sin(t) * r;

            (double lat, double lon) = OffsetLatLonMeters(lat0, lon0, northM, eastM);
            presetTargets[i] = new PresetGeoPoint(lat, lon);
        }

        // Reset bag so selection uses new list
        _presetBag = null;
        _presetBagIndex = 0;
        _hasPickedFirstTarget = false;
        _lastPresetIndex = -1;

        if (debugLogs)
            Debug.Log($"[TargetHunt] Auto-generated {autoPresetCount} local presets around ({lat0:F6},{lon0:F6}) r≈{autoPresetRadiusMeters}m");
    }

    private static (double lat, double lon) OffsetLatLonMeters(double latDeg, double lonDeg, double northM, double eastM)
    {
        const double R = 6378137.0; // WGS84-ish
        double rad = System.Math.PI / 180.0;

        double dLat = northM / R;
        double dLon = eastM / (R * System.Math.Cos(latDeg * rad));

        double lat2 = latDeg + dLat / rad;
        double lon2 = lonDeg + dLon / rad;

        return (lat2, lon2);
    }


    private void EnsureTarget(Map map2D)
    {
        if (HasValidTarget()) return;

        // Base from state / center / defaults
        double baseLat = SonusLocationState.Lat;
        double baseLon = SonusLocationState.Lng;

        if ((baseLat == 0 && baseLon == 0) && map2D != null)
        {
            baseLon = map2D.view.center.x;
            baseLat = map2D.view.center.y;
        }

        if ((baseLat == 0 && baseLon == 0) && sceneController != null)
        {
            baseLat = sceneController.defaultLatitude;
            baseLon = sceneController.defaultLongitude;
        }

        double tLat, tLon;

        if (TryPickNextPreset(out tLat, out tLon))
        {
            if (debugLogs)
                Debug.Log($"[TargetHunt] Using preset target=({tLat:F6},{tLon:F6})");
        }
        else
        {
            // Fallback: deterministic offset so we ALWAYS get a visible target nearby.
            tLat = baseLat + 0.001;
            tLon = baseLon + 0.001;

            if (debugLogs)
                Debug.Log($"[TargetHunt] Using fallback target=({tLat:F6},{tLon:F6}) from base=({baseLat:F6},{baseLon:F6})");
        }

        currentTarget = new TargetActor(TargetType.STATIONARY, tLat, tLon)
        {
            _Name = "Target"
        };

        // Seed patrol route immediately so 2D shows motion from the start.
        if (enablePatrol && patrolManager != null)
        {
            patrolManager.legMeters = patrolLegMeters;
            patrolManager.speedMps = patrolSpeedMps;
            patrolManager.AssignRoute(currentTarget._Lat, currentTarget._Lon);
        }

        if (debugLogs)
            Debug.Log($"[TargetHunt] Seed target=({currentTarget._Lat:F6},{currentTarget._Lon:F6}) from base=({baseLat:F6},{baseLon:F6})");
    }

    private bool HasValidTarget()
    {
        if (currentTarget == null) return false;
        return !(System.Math.Abs(currentTarget._Lat) < 1e-9 && System.Math.Abs(currentTarget._Lon) < 1e-9);
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
    // Preset bag
    // ---------------------------

    private void ResetPresetBag()
    {
        if (presetTargets == null || presetTargets.Length == 0)
        {
            _presetBag = null;
            _presetBagIndex = 0;
            return;
        }

        _presetBag = new int[presetTargets.Length];
        for (int i = 0; i < _presetBag.Length; i++) _presetBag[i] = i;

        // Fisher–Yates shuffle
        for (int i = _presetBag.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_presetBag[i], _presetBag[j]) = (_presetBag[j], _presetBag[i]);
        }

        _presetBagIndex = 0;

        if (debugLogs)
            Debug.Log($"[TargetHunt] Preset bag reset ({presetTargets.Length} targets).");
    }

    private bool TryPickNextPreset(out double tLat, out double tLon)
    {
        tLat = tLon = 0;

        if (!usePresetTargets) return false;
        if (presetTargets == null || presetTargets.Length == 0) return false;

        // If we're not doing the new logic, keep old bag behavior
        if (!firstTargetRandomThenClosest)
        {
            if (_presetBag == null || _presetBag.Length != presetTargets.Length) ResetPresetBag();
            if (_presetBag == null || _presetBag.Length == 0) return false;

            if (_presetBagIndex >= _presetBag.Length) ResetPresetBag();

            int idx = _presetBag[_presetBagIndex++];
            var p = presetTargets[idx];
            tLat = p.lat;
            tLon = p.lon;
            _lastPresetIndex = idx;
            return true;
        }

        // --- New logic ---
        // First pick: random from bag (so you still get variety)
        if (!_hasPickedFirstTarget)
        {
            if (_presetBag == null || _presetBag.Length != presetTargets.Length) ResetPresetBag();
            if (_presetBag == null || _presetBag.Length == 0) return false;

            if (_presetBagIndex >= _presetBag.Length) ResetPresetBag();

            int idx = _presetBag[_presetBagIndex++];
            var p = presetTargets[idx];

            tLat = p.lat;
            tLon = p.lon;

            _hasPickedFirstTarget = true;
            _lastPresetIndex = idx;
            return true;
        }

        // After first: choose closest preset to player's current position (if known)
        if (!TryGetPlayerLatLon(out double pLat, out double pLon))
        {
            // If we can't get player lat/lon, fall back to bag behavior
            if (_presetBag == null || _presetBag.Length != presetTargets.Length) ResetPresetBag();
            if (_presetBag == null || _presetBag.Length == 0) return false;

            if (_presetBagIndex >= _presetBag.Length) ResetPresetBag();

            int idx = _presetBag[_presetBagIndex++];
            var p = presetTargets[idx];
            tLat = p.lat;
            tLon = p.lon;
            _lastPresetIndex = idx;
            return true;
        }

        int bestIdx = -1;
        double bestM = double.MaxValue;

        for (int i = 0; i < presetTargets.Length; i++)
        {
            if (avoidImmediateRepeat && presetTargets.Length > 1 && i == _lastPresetIndex)
                continue;

            var pt = presetTargets[i];
            double m = ApproxMetersBetween(pLat, pLon, pt.lat, pt.lon);
            if (m < bestM)
            {
                bestM = m;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) return false;

        var best = presetTargets[bestIdx];
        tLat = best.lat;
        tLon = best.lon;
        _lastPresetIndex = bestIdx;

        if (debugLogs)
            Debug.Log($"[TargetHunt] Closest preset chosen idx={bestIdx} dist≈{bestM:F0}m from player=({pLat:F6},{pLon:F6})");

        return true;
    }


    private bool TryGetPlayerLatLon(out double lat, out double lon)
    {
        // Best source: SonusLocationState (your system snapshot)
        lat = SonusLocationState.Lat;
        lon = SonusLocationState.Lng;

        if (System.Math.Abs(lat) > 1e-9 || System.Math.Abs(lon) > 1e-9)
            return true;

        // Fallback: use 2D map center if available (better than nothing)
        if (sceneController != null && sceneController.map2D != null)
        {
            lon = sceneController.map2D.view.center.x;
            lat = sceneController.map2D.view.center.y;
            if (System.Math.Abs(lat) > 1e-9 || System.Math.Abs(lon) > 1e-9)
                return true;
        }

        return false;
    }

    // Good enough for ~km distances: equirectangular approximation in meters
    private static double ApproxMetersBetween(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0; // meters
        double rad = System.Math.PI / 180.0;

        double phi1 = lat1 * rad;
        double phi2 = lat2 * rad;

        double dPhi = (lat2 - lat1) * rad;
        double dLam = (lon2 - lon1) * rad;

        double x = dLam * System.Math.Cos((phi1 + phi2) * 0.5);
        double y = dPhi;

        return System.Math.Sqrt(x * x + y * y) * R;
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
