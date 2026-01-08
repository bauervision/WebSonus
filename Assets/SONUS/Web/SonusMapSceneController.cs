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

    [Header("3D Map")]
    public Map map3D;
    public ControlBase3D map3DControl;
    public Camera sceneCamera;
    public Transform playerRoot;
    public int zoom3D = 16;

    [Header("Geo Mapping (3D)")]
    [Tooltip("Assign the OLMGeoMapper wired to the SAME 3D Map + 3D Control.")]
    public OLMGeoMapper geoMapperOL;

    [Header("Targets")]
    public TargetManager targetManager;

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

    public void ToggleMode()
    {
        SetMode(_mode == Mode.Map2D ? Mode.Scene3D : Mode.Map2D);
    }

    public void SetMode(Mode next, bool immediate = false)
    {
        if (_modeRoutine != null) StopCoroutine(_modeRoutine);
        _modeRoutine = StartCoroutine(SetModeRoutine(next, immediate));
    }

    private IEnumerator SetModeRoutine(Mode next, bool immediate)
    {
        _mode = next;

        bool is2D = _mode == Mode.Map2D;

        // Flip roots first
        if (mapModeRoot != null) mapModeRoot.SetActive(is2D);
        if (sceneModeRoot != null) sceneModeRoot.SetActive(!is2D);

        // Give Unity one frame to activate/deactivate OnlineMaps objects cleanly
        if (!immediate)
            yield return null;

        if (is2D) Enter2D();
        else yield return Enter3D();
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

        // Place player using mapper (elevation-aware when available)
        if (playerRoot != null && geoMapperOL != null)
        {
            Vector3 w = geoMapperOL.LatLonToWorld(lat, lng, extraYOffset: 0f);
            if (w != Vector3.zero)
            {
                playerRoot.position = w;
            }
            else if (debugLogs)
            {
                Debug.LogWarning("[SONUS] geoMapperOL.LatLonToWorld returned Vector3.zero (mapper not ready?)");
            }
        }

        // Now allow targets to create/sync their 3D marker
        if (targetManager != null)
            targetManager.OnEnter3D();

        if (debugLogs)
            Debug.Log($"[SONUS] Enter3D @ ({lat:F6},{lng:F6}) z={zoom3D}");
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
