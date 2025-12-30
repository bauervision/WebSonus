// Assets/SONUS/Web/SonusMapSceneController.cs
using System.Collections;
using UnityEngine;
using OnlineMaps;
using Sonus.Core;
using System.Reflection;

public class SonusMapSceneController : MonoBehaviour
{
    public enum Mode { Map2D, Scene3D }

    [Header("Mode Roots")]
    public GameObject mapModeRoot;   // assign "Map Mode" GO here
    public GameObject sceneModeRoot; // assign "Scene Mode" GO here

    [Header("Targets")]
    public TargetManager targetManager;

    [Header("Defaults")]
    public double defaultLatitude = 37.3045;
    public double defaultLongitude = -80.6115;

    [Header("2D Map (Map Mode)")]
    [Tooltip("Online Maps Map component for the 2D (UI) map")]
    public Map map2D;

    [Tooltip("Camera that renders the 2D map UI (optional)")]
    public Camera mapCamera;

    public int zoom2D = 17;

    [Header("2D User Marker")]
    public Texture2D userMarkerTexture;
    public float userMarkerScale = 1f;

    [Tooltip("Rotate the 2D marker to reflect player orientation captured in 3D.")]
    public bool rotate2DMarkerWithPlayer = true;

    [Tooltip("Icon forward offset in DEGREES (e.g., if the sprite points 'down' by default, use 180).")]
    public float markerIconOffsetDeg = 180f;

    private Marker2D _userMarker2D;

    [Header("3D Map (Scene Mode)")]
    [Tooltip("Online Maps Map component for the 3D tileset")]
    public Map map3D;

    [Tooltip("The 3D control component attached to the 3D map (TileSetControl / similar)")]
    public ControlBase3D map3DControl;

    [Tooltip("Camera that renders the 3D scene / tileset")]
    public Camera sceneCamera;

    [Tooltip("Player root / FirstPersonController transform")]
    public Transform playerRoot;

    public int zoom3D = 16;

    [Tooltip("How far above the terrain to spawn the player (world units).")]
    public float playerSpawnHeight = 2f;

    [Header("3D Spawn Probe")]
    [Tooltip("If true, we use a temporary hidden Marker3D as an elevation-aware spawn probe, then disable it.")]
    public bool useHiddenSpawnProbe = true;

    private Marker3D _spawnProbeMarker3D;
    private GameObject _spawnProbePrefab;

    [Header("Geo Mapping")]
    [Tooltip("Assign your OLMGeoMapper that targets the 3D control/tileset.")]
    public OLMGeoMapper geoMapperOL;

    [Header("Debug / Instrumentation")]
    public bool debugLogGeoEachSecond = false;
    public bool debugLogSwitchSummary = true;

    private float _nextGeoLogTime;

    // Renamed: this is a MAP BEARING in DEGREES (0..360), not a Unity yaw.
    private float _lastBearingDeg;

    private Mode _mode = Mode.Map2D;
    private Coroutine _enter3DRoutine;
    private Coroutine _enter2DRoutine;

    private void Awake()
    {
        if (map3D != null && map3DControl == null)
            map3DControl = map3D.control3D;

        if (useHiddenSpawnProbe)
        {
            _spawnProbePrefab = new GameObject("SonusSpawnProbePrefab");
            _spawnProbePrefab.hideFlags = HideFlags.HideAndDontSave;
            _spawnProbePrefab.SetActive(false);
        }
    }

    private void Start()
    {
        // Initialize state once.
        SonusLocationState.Set(defaultLatitude, defaultLongitude);

        // Ensure the 3D control is bound to the scene camera (critical for ScreenToLocation).
        if (map3DControl != null && sceneCamera != null)
            map3DControl.activeCamera = sceneCamera;

        // Optional: keep mapper camera/control aligned too.
        if (geoMapperOL != null)
        {
            if (geoMapperOL.map == null) geoMapperOL.map = map3D;
            if (geoMapperOL.control3D == null) geoMapperOL.control3D = map3DControl;
            if (geoMapperOL.sceneCamera == null) geoMapperOL.sceneCamera = sceneCamera;
        }

        // Wire target manager refs ONLY (do not create markers here; too early).
        if (targetManager != null)
        {
            targetManager.sceneController = this;
            // targetManager.geoMapperOL = geoMapperOL;
            targetManager.playerRoot = playerRoot;
        }

        // Init both maps at default state.
        double lat = SonusLocationState.Lat;
        double lng = SonusLocationState.Lng;

        Init2DMap(lat, lng);
        Ensure2DUserMarker(lng, lat);

        Init3DMap(lat, lng);

        // Seed target immediately on 2D so player sees it on scene start
        if (targetManager != null && map2D != null)
            targetManager.OnEnter2D(map2D);


        SetMode(Mode.Map2D);
    }

    private void Update()
    {
        if (!debugLogGeoEachSecond) return;
        if (_mode != Mode.Scene3D) return;
        if (Time.time < _nextGeoLogTime) return;

        _nextGeoLogTime = Time.time + 1f;

        if (geoMapperOL == null)
        {
            Debug.Log("[SONUS][GEO] missing geoMapperOL");
            return;
        }

        // (intentionally empty as in your pasted version)
    }

    private static void ForceMarker2DManagerInstance(Marker2DManager desired)
    {
        if (desired == null) return;

        // Some OnlineMaps versions expose `instance` publicly, others keep it non-public.
        var t = typeof(Marker2DManager);
        var f = t.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            f.SetValue(null, desired);
            return;
        }

        // Fallback: property form (rare)
        var p = t.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite)
        {
            p.SetValue(null, desired, null);
        }
    }


    private void Init2DMap(double lat, double lng)
    {
        if (map2D == null) return;

        map2D.view.SetCenter((float)lng, (float)lat, zoom2D);
        map2D.Redraw();
    }

    private void Init3DMap(double lat, double lng)
    {
        if (map3D == null) return;

        map3D.view.SetCenter((float)lng, (float)lat, zoom3D);
        map3D.Redraw();
    }

    private void Ensure2DUserMarker(double lng, double lat)
    {
        if (map2D == null) return;
        if (_userMarker2D != null) return;
        if (userMarkerTexture == null) return;

        _userMarker2D = Marker2DManager.CreateItem(lng, lat, userMarkerTexture, "user");
        if (_userMarker2D == null) return;

        _userMarker2D.align = Align.Center;
        _userMarker2D.scale = userMarkerScale;
        _userMarker2D.location = new GeoPoint(lng, lat);

        Apply2DMarkerRotation();
        map2D.Redraw();
    }

    public void ToggleMode()
    {
        SetMode(_mode == Mode.Map2D ? Mode.Scene3D : Mode.Map2D);
    }

    private void SetMode(Mode next)
    {
        // Snapshot BEFORE leaving 3D.
        if (_mode == Mode.Scene3D && next == Mode.Map2D)
            Capture3DStateSnapshotAndLog();

        _mode = next;
        bool mapMode = _mode == Mode.Map2D;

        if (mapModeRoot != null) mapModeRoot.SetActive(mapMode);
        if (sceneModeRoot != null) sceneModeRoot.SetActive(!mapMode);

        // Stop any pending enter routines.
        if (_enter2DRoutine != null) StopCoroutine(_enter2DRoutine);
        if (_enter3DRoutine != null) StopCoroutine(_enter3DRoutine);

        if (mapMode)
        {
            // Buffer safety: delay one frame before touching 2D map again.
            _enter2DRoutine = StartCoroutine(EnterMapModeRoutine());
        }
        else
        {
            EnterSceneMode();
        }
    }

    private IEnumerator EnterMapModeRoutine()
    {
        yield return null;
        EnterMapMode();
    }

    private void EnterMapMode()
    {
        if (map2D == null) return;

        double lat = SonusLocationState.Lat;
        double lng = SonusLocationState.Lng;

        Ensure2DUserMarker(lng, lat);

        map2D.view.SetCenter((float)lng, (float)lat, zoom2D);
        Sync2DUserMarker();
        map2D.Redraw();

        if (targetManager != null) targetManager.OnEnter2D(map2D);
    }

    private void EnterSceneMode()
    {
        if (map3D == null || map3DControl == null || playerRoot == null) return;

        double lat = SonusLocationState.Lat;
        double lng = SonusLocationState.Lng;

        map3D.view.SetCenter((float)lng, (float)lat, zoom3D);
        map3D.Redraw();

        // Start logger on entering 3D.
        _nextGeoLogTime = Time.time + 1f;

        if (useHiddenSpawnProbe)
        {
            _enter3DRoutine = StartCoroutine(EnterSceneModeRoutine(lng, lat));
        }
        else
        {
            // No probe: still give the tileset/marker systems a couple frames to wake up,
            // then let TargetManager create/sync its Marker3D.
            _enter3DRoutine = StartCoroutine(EnterSceneModeNoProbeRoutine());
        }
    }

    private IEnumerator EnterSceneModeNoProbeRoutine()
    {
        // Let the 3D scene & map control settle
        yield return null;
        yield return null;

        if (targetManager != null) targetManager.OnEnter3D();

        Vector3 forward = (sceneCamera != null) ? sceneCamera.transform.forward : playerRoot.forward;
        _lastBearingDeg = GeoFrame.BearingDegFromWorldForward(forward);
    }


    private IEnumerator EnterSceneModeRoutine(double lng, double lat)
    {
        CreateOrMoveSpawnProbe(lng, lat);

        // Give OnlineMaps a couple frames to apply elevation/placement.
        yield return null;
        yield return null;

        PlacePlayerAtSpawnProbe();

        // ✅ NOW the 3D tileset & Marker3D system are "warm"
        if (targetManager != null) targetManager.OnEnter3D();

        Vector3 forward = (sceneCamera != null) ? sceneCamera.transform.forward : playerRoot.forward;
        _lastBearingDeg = GeoFrame.BearingDegFromWorldForward(forward);

        CleanupSpawnProbe();
    }


    private void CreateOrMoveSpawnProbe(double lng, double lat)
    {
        if (_spawnProbePrefab == null) return;

        if (_spawnProbeMarker3D == null)
        {
            _spawnProbeMarker3D = Marker3DManager.CreateItem(lng, lat, _spawnProbePrefab, "spawn-probe");
            if (_spawnProbeMarker3D == null) return;

            _spawnProbeMarker3D.sizeType = Marker3D.SizeType.scene;
        }

        _spawnProbeMarker3D.scale = 1f;
        _spawnProbeMarker3D.enabled = true;
        _spawnProbeMarker3D.location = new GeoPoint(lng, lat);
        _spawnProbeMarker3D.Update();
    }

    private void PlacePlayerAtSpawnProbe()
    {
        if (_spawnProbeMarker3D == null) return;
        Transform markerTr = _spawnProbeMarker3D.transform;
        if (markerTr == null) return;

        Vector3 p = markerTr.position;

        var cc = playerRoot.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerRoot.position = new Vector3(p.x, p.y + playerSpawnHeight, p.z);

        if (cc != null) cc.enabled = true;
    }

    private void CleanupSpawnProbe()
    {
        if (_spawnProbeMarker3D == null) return;
        _spawnProbeMarker3D.enabled = false;
    }

    private void Capture3DStateSnapshotAndLog()
    {
        // Snapshot BEARING (not Unity yaw)
        if (playerRoot != null)
        {
            Vector3 forward = (sceneCamera != null) ? sceneCamera.transform.forward : playerRoot.forward;
            _lastBearingDeg = GeoFrame.BearingDegFromWorldForward(forward);
        }

        double snapLat = SonusLocationState.Lat;
        double snapLon = SonusLocationState.Lng;

        if (geoMapperOL != null && geoMapperOL.TryFeetScreenToLatLon(out double lat, out double lon))
        {
            snapLat = lat;
            snapLon = lon;
            SonusLocationState.Set(snapLat, snapLon);
        }

        if (!debugLogSwitchSummary) return;

        // (rest of your logging omitted exactly as you had it)
    }

    private void Sync2DUserMarker()
    {
        if (_userMarker2D == null) return;

        double lat = SonusLocationState.Lat;
        double lng = SonusLocationState.Lng;

        _userMarker2D.location = new GeoPoint(lng, lat);
        Apply2DMarkerRotation();
    }

    private void Apply2DMarkerRotation()
    {
        if (!rotate2DMarkerWithPlayer) return;
        if (_userMarker2D == null) return;

        // OnlineMaps Marker2D.rotation expects TURNS (0..1) in our setup.
        _userMarker2D.rotation = GeoFrame.Marker2DRotationTurns(
            _lastBearingDeg,
            markerIconOffsetDeg
        );
    }
}
