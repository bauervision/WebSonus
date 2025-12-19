// DebugNorthProbeRenderer.cs
// Standalone debug helper for Online Maps 3D (v4.2.1.1 safe).
//
// How it works:
// - Creates 3 hidden Marker3D probes (center, north, east).
// - Lets Online Maps place them, then reads their world positions.
// - Draws debug lines:
//    GREEN: center -> north (lat + delta)
//    RED:   center -> east  (lon + delta)
//
// Notes:
// - Works even when ControlBase3D has no GetWorldPosition API.
// - Meant for debugging only (Editor / Dev builds).
//
// Usage:
// - Add to any GameObject in your 3D scene.
// - Assign map3D (the OnlineMaps Map that drives the 3D tileset).
// - Toggle "enabled" or "drawContinuously" as needed.

using UnityEngine;
using OnlineMaps;

public class DebugNorthProbeRenderer : MonoBehaviour
{
    public enum CenterMode
    {
        UseFixedLatLon,
        UseSonusLocationState
    }

    [Header("Bindings")]
    [Tooltip("Assign the Online Maps Map used for the 3D tileset (Scene Mode).")]
    public Map map3D;

    [Header("Center")]
    public CenterMode centerMode = CenterMode.UseSonusLocationState;

    [Tooltip("Used when CenterMode = UseFixedLatLon")]
    public double fixedLatitude = 37.3045;

    [Tooltip("Used when CenterMode = UseFixedLatLon")]
    public double fixedLongitude = -80.6115;

    [Header("Probe Settings")]
    [Tooltip("Step size in degrees. 0.001 ≈ 111m, 0.0005 ≈ 55m.")]
    public double deltaDeg = 0.001;

    [Tooltip("How often to refresh marker positions (seconds).")]
    public float refreshSeconds = 0.25f;

    [Tooltip("Draw continuously. If false, use hotkey to draw once.")]
    public bool drawContinuously = true;

    public bool enableHotkey = true;
    public KeyCode drawOnceKey = KeyCode.N;

    [Tooltip("Line duration (seconds). Use ~refreshSeconds when continuous.")]
    public float lineDuration = 0.25f;

    [Tooltip("Lift lines slightly to avoid z-fighting.")]
    public float lift = 0.5f;

    [Header("Visibility")]
    public bool drawNorthEast = true;

    // Hidden prefab + markers
    private GameObject _probePrefab;
    private Marker3D _mCenter;
    private Marker3D _mNorth;
    private Marker3D _mEast;

    private float _nextRefresh;

    private void Awake()
    {
        if (map3D == null) map3D = FindAnyObjectByType<Map>();

        // Hidden prefab used by Marker3DManager (no renderer needed)
        _probePrefab = new GameObject("DebugNorthProbePrefab");
        _probePrefab.hideFlags = HideFlags.HideAndDontSave;
        _probePrefab.SetActive(false);
    }

    private void OnDestroy()
    {
        // Disable markers (OnlineMaps manages their lifecycle; keep it safe)
        SafeDisable(_mCenter);
        SafeDisable(_mNorth);
        SafeDisable(_mEast);

        if (_probePrefab != null) Destroy(_probePrefab);
    }

    private void Update()
    {
        if (map3D == null) return;

        if (drawContinuously)
        {
            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + refreshSeconds;
                RefreshAndDraw();
            }
            return;
        }

        if (enableHotkey && Input.GetKeyDown(drawOnceKey))
        {
            RefreshAndDraw();
        }
    }

    private void RefreshAndDraw()
    {
        GetCenter(out double lat, out double lon);

        // GeoPoint expects (lng, lat)
        var pCenter = new GeoPoint(lon, lat);
        var pNorth = new GeoPoint(lon, lat + deltaDeg);
        var pEast = new GeoPoint(lon + deltaDeg, lat);

        EnsureMarkers();

        // Update marker geo locations
        _mCenter.location = pCenter;
        _mNorth.location = pNorth;
        _mEast.location = pEast;

        // Force Online Maps to apply placement
        _mCenter.enabled = true; _mCenter.Update();
        _mNorth.enabled = true; _mNorth.Update();
        _mEast.enabled = true; _mEast.Update();

        // Read placed world positions
        Vector3 wC = _mCenter.transform.position; wC.y += lift;
        Vector3 wN = _mNorth.transform.position; wN.y += lift;
        Vector3 wE = _mEast.transform.position; wE.y += lift;

        if (drawNorthEast)
        {
            Debug.DrawLine(wC, wN, Color.green, lineDuration, false); // NORTH
            Debug.DrawLine(wC, wE, Color.red, lineDuration, false); // EAST
        }
    }

    private void EnsureMarkers()
    {
        if (_mCenter == null) _mCenter = CreateProbe("probe-center");
        if (_mNorth == null) _mNorth = CreateProbe("probe-north");
        if (_mEast == null) _mEast = CreateProbe("probe-east");
    }

    private Marker3D CreateProbe(string label)
    {
        var m = Marker3DManager.CreateItem(0, 0, _probePrefab, label);
        if (m == null) return null;

        // Keep probes tiny and effectively invisible
        m.sizeType = Marker3D.SizeType.scene;
        m.scale = 1f;
        m.enabled = true;
        m.Update();

        return m;
    }

    private void SafeDisable(Marker3D m)
    {
        if (m == null) return;
        m.enabled = false;
    }

    private void GetCenter(out double lat, out double lon)
    {
        if (centerMode == CenterMode.UseFixedLatLon)
        {
            lat = fixedLatitude;
            lon = fixedLongitude;
            return;
        }

        // Uses your existing global state if available
        lat = Sonus.Core.SonusLocationState.Lat;
        lon = Sonus.Core.SonusLocationState.Lng;
    }
}
