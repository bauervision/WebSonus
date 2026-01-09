// Assets/SONUS/Web/SonusMapSceneController.cs
using System.Collections;
using System.Reflection;
using UnityEngine;
using OnlineMaps;
using Sonus.Core;

public class SonusMapSceneController : MonoBehaviour
{
    public enum Mode { Map2D, Scene3D }

    [Header("Mode Roots")]
    public GameObject mapModeRoot;    // assign "Map Mode"
    public GameObject sceneModeRoot;  // assign "Scene Mode"

    [Header("Defaults (fallback only)")]
    public double defaultLatitude = 37.3045;
    public double defaultLongitude = -80.6115;

    [Header("2D Map")]
    public Map map2D;
    [Tooltip("The Marker2DManager that belongs to your 2D map.")]
    public Marker2DManager markerManager2D;
    public int zoom2D = 17;

    [Header("2D User Marker")]
    public Texture2D userMarkerTexture;
    public float userMarkerScale = 1f;

    private float _next2DFollowT;
    [Header("2D Follow")]
    public bool followUserIn2D = true;
    public float follow2DHz = 15f; // 10–20 feels good
    public bool centerMapOnUserIn2D = false; // leave off if you want manual panning


    [Header("3D Map")]
    public Map map3D;
    public ControlBase3D map3DControl;
    public Camera sceneCamera;
    public Transform playerRoot;
    public int zoom3D = 16;

    [Header("3D Elevation Probe (player)")]
    public GameObject playerProbePrefab;     // tiny empty prefab (can be invisible)
    public float playerGroundOffset = 1.8f;  // how high above surface to place player
    public int probeWarmupFrames = 10;       // frames to wait after Update()
    public float probeTimeoutSeconds = 2.0f; // max wait for tileset resolve

    private Marker3D _playerProbe3D;

    [Header("Geo Mapping (3D)")]
    [Tooltip("Assign the OLMGeoMapper wired to the SAME 3D Map + 3D Control.")]
    public OLMGeoMapper geoMapperOL;

    [Header("Targets")]
    public TargetManager targetManager;

    [Header("Sonic")]
    [SerializeField] private bool sonicEnabled = true; // driven by your single toggle button
    public AudioManager audioManager;

    [Header("Debug")]
    public bool debugLogs = true;

    private Mode _mode = Mode.Map2D;
    private Coroutine _modeRoutine;

    private Marker2D _userMarker2D;

    public bool IsIn2DMode => _mode == Mode.Map2D;

    // UI Button methods (keep these names stable)
    public void EnterMapMode() => SetMode(Mode.Map2D);
    public void EnterSceneMode() => SetMode(Mode.Scene3D);
    public void ToggleModeButton() => ToggleMode();

    // Optional aliases in case your buttons referenced these names:
    public void EnterMapModeButton() => SetMode(Mode.Map2D);
    public void EnterSceneModeButton() => SetMode(Mode.Scene3D);
    private float _lastPlayerYawDeg;



    private void Awake()
    {
        if (map3D != null && map3DControl == null)
            map3DControl = map3D.control3D;

        if (targetManager == null) targetManager = FindFirstObjectByType<TargetManager>();
        if (geoMapperOL == null) geoMapperOL = FindFirstObjectByType<OLMGeoMapper>();
    }

    private static bool IsZeroZero(double lat, double lon)
    {
        return Mathf.Abs((float)lat) < 0.000001f && Mathf.Abs((float)lon) < 0.000001f;
    }

    private void Start()
    {
        // If nothing else has set location yet, seed to defaults (non-zero).
        if (!SonusLocationState.HasValue || IsZeroZero(SonusLocationState.Lat, SonusLocationState.Lng))
            SonusLocationState.Set(defaultLatitude, defaultLongitude);

        // Bind camera to 3D control for ScreenToLocation / hit tests.
        if (map3DControl != null && sceneCamera != null)
            map3DControl.activeCamera = sceneCamera;

        // Ensure mapper is wired
        if (geoMapperOL != null)
        {
            if (geoMapperOL.map == null) geoMapperOL.map = map3D;
            if (geoMapperOL.control3D == null) geoMapperOL.control3D = map3DControl;
            if (geoMapperOL.sceneCamera == null) geoMapperOL.sceneCamera = sceneCamera;
        }

        // Let TargetManager know who owns it
        if (targetManager != null)
        {
            targetManager.sceneController = this;
            targetManager.playerRoot = playerRoot;
        }

        // Start in 2D by default
        SetMode(Mode.Map2D, immediate: true);
    }

    private void Update()
    {
        // Only do continuous follow in 2D mode
        if (!IsIn2DMode) return;
        if (!followUserIn2D) return;
        if (map2D == null || map2D.view == null) return;
        if (_userMarker2D == null) return; // created in Enter2D

        if (Time.time < _next2DFollowT) return;
        _next2DFollowT = Time.time + (1f / Mathf.Max(1f, follow2DHz));

        if (!SonusLocationState.HasValue) return;

        float lat = (float)SonusLocationState.Lat;
        float lng = (float)SonusLocationState.Lng;

        // Update marker position
        Sync2DUserMarker(lng, lat);

        // Optional: keep the map centered on the user
        if (centerMapOnUserIn2D)
            map2D.view.SetCenter(lng, lat, map2D.view.zoom);

        map2D.Redraw();
    }

    public void ToggleMode()
    {
        SetMode(_mode == Mode.Map2D ? Mode.Scene3D : Mode.Map2D);
    }

    public void SetMode(Mode next, bool immediate = false)
    {
        audioManager.StopSonic();

        if (_modeRoutine != null) StopCoroutine(_modeRoutine);
        _modeRoutine = StartCoroutine(SetModeRoutine(next, immediate));
    }

    private IEnumerator SetModeRoutine(Mode next, bool immediate)
    {
        // If we are about to leave 3D, capture current facing.
        if (next == Mode.Map2D && _mode == Mode.Scene3D && playerRoot != null)
        {
            float yaw = playerRoot.eulerAngles.y;
            _lastPlayerYawDeg = Mathf.Repeat(yaw + 180f, 360f);
        }


        _mode = next;

        bool is2D = _mode == Mode.Map2D;

        // Flip roots first
        if (mapModeRoot != null) mapModeRoot.SetActive(is2D);
        if (sceneModeRoot != null) sceneModeRoot.SetActive(!is2D);

        // Give Unity one frame to activate/deactivate OnlineMaps objects cleanly
        if (!immediate)
            yield return null;

        if (is2D)
        {
            Enter2D();
            audioManager.StopSonic(); // always off in Map mode
        }
        else
        {
            yield return Enter3D();    // wait until 3D is ready (player placed, targets synced)

            if (sonicEnabled)
            {
                audioManager.StartSonic();
                audioManager.HearNow(); // <-- force immediate cue (don’t wait 30s)
            }
        }

    }

    // ----------------------------
    // 2D
    // ----------------------------
    private void Enter2D()
    {
        if (map2D == null || map2D.view == null) return;

        // Ensure marker singleton points at the 2D manager before any CreateItem calls.
        ForceMarker2DManagerInstance(markerManager2D);

        // Choose best-known location
        double lat = SonusLocationState.HasValue ? SonusLocationState.Lat : defaultLatitude;
        double lng = SonusLocationState.HasValue ? SonusLocationState.Lng : defaultLongitude;

        // Center map + ensure user marker
        map2D.view.SetCenter((float)lng, (float)lat, zoom2D);
        Ensure2DUserMarker((float)lng, (float)lat);
        Sync2DUserMarker((float)lng, (float)lat);
        Sync2DUserHeading(_lastPlayerYawDeg);

        map2D.Redraw();

        if (targetManager != null)
            targetManager.OnEnter2D(map2D);

        if (debugLogs)
            Debug.Log($"[SONUS] Enter2D @ ({lat:F6},{lng:F6}) z={zoom2D}");
    }

    private void Ensure2DUserMarker(float lng, float lat)
    {
        if (_userMarker2D != null) return;
        if (userMarkerTexture == null) return;

        // Must have correct singleton set (ForceMarker2DManagerInstance)
        _userMarker2D = Marker2DManager.CreateItem(lng, lat, userMarkerTexture, "user");
        if (_userMarker2D == null) return;

        _userMarker2D.align = Align.Center;
        _userMarker2D.scale = userMarkerScale;
        _userMarker2D.location = new GeoPoint(lng, lat);
    }

    private void Sync2DUserMarker(float lng, float lat)
    {
        if (_userMarker2D == null) return;
        _userMarker2D.location = new GeoPoint(lng, lat);
    }

    private void Sync2DUserHeading(float yawDeg)
    {
        if (_userMarker2D == null) return;

        // Prefer the captured yaw from 3D exit (stable).
        float h = Mathf.Repeat(yawDeg, 360f);

        TrySetMarker2DRotation(_userMarker2D, h);
    }


    // ----------------------------
    // 3D
    // ----------------------------
    private IEnumerator Enter3D()
    {
        if (map3D == null || map3D.view == null) yield break;

        // Bind camera (again) in case scene objects were toggled
        if (map3DControl != null && sceneCamera != null)
            map3DControl.activeCamera = sceneCamera;

        // Choose best-known location (must be non-zero)
        double lat = (SonusLocationState.HasValue && !IsZeroZero(SonusLocationState.Lat, SonusLocationState.Lng))
            ? SonusLocationState.Lat
            : defaultLatitude;

        double lng = (SonusLocationState.HasValue && !IsZeroZero(SonusLocationState.Lat, SonusLocationState.Lng))
            ? SonusLocationState.Lng
            : defaultLongitude;

        // Center 3D map
        map3D.view.SetCenter((float)lng, (float)lat, zoom3D);
        map3D.Redraw();

        // Let tiles/control warm up a couple frames
        yield return null;
        yield return null;

        // Place player using 3D probe marker (MOST reliable Y)
        yield return StartCoroutine(PlacePlayerUsing3DProbe(lat, lng));

        // Now allow targets to create/sync their 3D marker
        if (targetManager != null)
            targetManager.OnEnter3D();

        if (debugLogs)
            Debug.Log($"[SONUS] Enter3D @ ({lat:F6},{lng:F6}) z={zoom3D}");
    }

    private IEnumerator PlacePlayerUsing3DProbe(double lat, double lng)
    {
        if (playerRoot == null) yield break;

        // Wait for Marker3DManager
        float t0 = Time.realtimeSinceStartup;
        while (Marker3DManager.instance == null && Time.realtimeSinceStartup - t0 < probeTimeoutSeconds)
            yield return null;

        if (Marker3DManager.instance == null)
        {
            if (debugLogs) Debug.LogWarning("[SONUS] Marker3DManager.instance not ready; cannot place player by probe.");
            yield break;
        }

        // Create probe marker if needed
        if (_playerProbe3D == null)
        {
            GameObject probeGO =
                playerProbePrefab != null
                    ? playerProbePrefab
                    : CreateRuntimeProbeGO();

            _playerProbe3D = Marker3DManager.CreateItem(0, 0, probeGO, "player-probe-3d");
            if (_playerProbe3D == null)
            {
                if (debugLogs) Debug.LogWarning("[SONUS] CreateItem for player probe returned null.");
                yield break;
            }

            _playerProbe3D.sizeType = Marker3D.SizeType.scene;
        }

        // Set geo (GeoPoint expects lon,lat)
        _playerProbe3D.location = new GeoPoint(lng, lat);

        // Ensure visible/active
        if (_playerProbe3D.transform != null)
            _playerProbe3D.transform.gameObject.SetActive(true);

        try { _playerProbe3D.enabled = true; } catch { }

        try { _playerProbe3D.Update(); }
        catch
        {
            if (debugLogs) Debug.LogWarning("[SONUS] player probe Update() threw; tileset/control not ready.");
            yield break;
        }

        // Warmup frames so OM can resolve transform
        for (int i = 0; i < probeWarmupFrames; i++)
            yield return null;

        // Poll until valid pos or timeout
        Vector3 pos = Vector3.zero;
        float t1 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t1 < probeTimeoutSeconds)
        {
            if (_playerProbe3D != null && _playerProbe3D.enabled && _playerProbe3D.transform != null)
            {
                pos = _playerProbe3D.transform.position;
                if (pos != Vector3.zero) break;
            }
            yield return null;
        }

        if (pos == Vector3.zero)
        {
            if (debugLogs) Debug.LogWarning("[SONUS] player probe never resolved a valid world position (pos=0).");
            yield break;
        }

        // Teleport safely (avoid gravity-fall artifacts)
        var cc = playerRoot.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        var rb = playerRoot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        playerRoot.position = pos + Vector3.up * playerGroundOffset;

        if (rb != null) rb.isKinematic = false;
        if (cc != null) cc.enabled = true;

        if (debugLogs)
            Debug.Log($"[SONUS] Player placed by probe @ world={pos} geo=({lat:F6},{lng:F6})");
    }

    private GameObject CreateRuntimeProbeGO()
    {
        var go = new GameObject("PlayerProbeRuntime");

        // Make absolutely sure it renders nothing
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // Defensive: remove anything unexpected
        foreach (var c in go.GetComponents<Component>())
        {
            if (!(c is Transform))
                Destroy(c);
        }

        return go;
    }

    // ----------------------------
    // Audio connections
    // ----------------------------
    public void SetSonicEnabled(bool on)
    {
        sonicEnabled = on;

        if (audioManager == null) audioManager = FindFirstObjectByType<AudioManager>();

        // Only run sonic in Scene mode
        if (_mode == Mode.Scene3D)
        {
            if (sonicEnabled) audioManager?.StartSonic();
            else audioManager?.StopSonic();
        }
        else
        {
            audioManager?.StopSonic();
        }
    }


    private static void TrySetMarker2DRotation(Marker2D m, float deg)
    {
        if (m == null) return;

        deg = Mathf.Repeat(deg, 360f);

        var t = m.GetType();

        // Prefer explicit degree-based APIs if they exist
        var pDeg =
            t.GetProperty("rotationDegree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? t.GetProperty("rotationDegrees", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (pDeg != null && pDeg.PropertyType == typeof(float) && pDeg.CanWrite)
        {
            pDeg.SetValue(m, deg);
            return;
        }

        var fDeg =
            t.GetField("rotationDegree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? t.GetField("rotationDegrees", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (fDeg != null && fDeg.FieldType == typeof(float))
        {
            fDeg.SetValue(m, deg);
            return;
        }

        // Fallback: "rotation" is normalized turns (0..1) in your build.
        float turns = deg / 360f;

        var pRot = t.GetProperty("rotation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (pRot != null && pRot.PropertyType == typeof(float) && pRot.CanWrite)
        {
            pRot.SetValue(m, turns);
            return;
        }

        var fRot = t.GetField("rotation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fRot != null && fRot.FieldType == typeof(float))
        {
            fRot.SetValue(m, turns);
            return;
        }
    }




    // ----------------------------
    // OnlineMaps singleton nudges
    // ----------------------------
    private static void ForceMarker2DManagerInstance(Marker2DManager desired)
    {
        if (desired == null) return;

        var t = typeof(Marker2DManager);
        var f = t.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            f.SetValue(null, desired);
            return;
        }

        var p = t.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite)
            p.SetValue(null, desired, null);
    }
}
