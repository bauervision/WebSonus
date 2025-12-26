using System.Reflection;
using UnityEngine;
using OnlineMaps;

public interface IGeoMapperReinitOL
{
    void ForceReinit();
}

/// Mapper that uses Online Maps 3D control to convert between:
/// - Lat/Lon <-> World position
/// - Screen sample -> Lat/Lon (preferred “where am I” on switch)
///
/// Design notes:
/// - Online Maps APIs vary by control/version, so we keep reflection for LatLonToWorld and (optional) WorldToLatLon.
/// - For gameplay “current position”, we prefer ScreenToLocation sampling near the bottom-center of the screen.
/// - We reject (0,0) “no data” reads and reuse the last known good lat/lon.
public class OLMGeoMapper : MonoBehaviour, IGeoMapperReinitOL
{
    [Header("Online Maps (prefer explicit wiring)")]
    [Tooltip("Assign the SAME 3D Map used in Scene Mode (not the 2D UI map).")]
    public Map map;

    [Tooltip("Assign the 3D control used by that map (often map.control3D).")]
    public ControlBase3D control3D;

    [Tooltip("Camera rendering the 3D tileset (fallback if control3D.activeCamera is unset).")]
    public Camera sceneCamera;

    [Header("Altitude")]
    [Tooltip("Extra Y offset when placing the player on the tileset (world units).")]
    public float yOffset = 1.8f;

    [Header("Geo Sampling (preferred)")]
    [Tooltip("0 = bottom of screen (near feet), 0.5 = center (crosshair), 1 = top.")]
    [Range(0f, 1f)]
    public float screenSampleY01 = 0.12f; // tune ~0.08–0.20

    [Header("Debug")]
    public bool debugLogs = false;

    private bool _init;

    // last-good caching for screen sampling
    private bool _hasLastGood;
    private double _lastGoodLat;
    private double _lastGoodLon;

    private void Awake()
    {
        Init();
    }

    private void Init()
    {
        if (_init) return;

        if (map == null) map = FindAny<Map>();
        if (control3D == null && map != null) control3D = map.control3D;

        if (sceneCamera == null) sceneCamera = Camera.main;

        if (map == null) Debug.LogWarning("OLMGeoMapper: Map is null. Assign it in inspector (recommended).");
        if (control3D == null) Debug.LogWarning("OLMGeoMapper: ControlBase3D is null. Assign it in inspector (recommended).");
        if (sceneCamera == null) Debug.LogWarning("OLMGeoMapper: sceneCamera is null. Assign it in inspector (recommended).");

        _init = true;
    }

    public void ForceReinit()
    {
        _init = false;
        Init();
    }

    // ------------------------------------------------------
    // Lat/Lon -> World (spawn / teleport)
    // ------------------------------------------------------

    public Vector3 LatLonToWorld(double lat, double lon, float extraYOffset = 0f)
    {
        Init();
        if (control3D == null) return Vector3.zero;

        // Ensure control has an active camera (some builds need this)
        if (control3D.activeCamera == null && sceneCamera != null)
            control3D.activeCamera = sceneCamera;

        // Online Maps commonly uses (lng, lat) order internally.
        double lng = lon;

        // 1) Prefer elevation-aware method when using tileset / dynamic mesh controls
        if (TryInvokeGetWorldPositionWithElevation(control3D, lng, lat, out Vector3 worldE))
        {
            worldE.y += yOffset + extraYOffset;
            return worldE;
        }

        // 2) Fallback: plain world position
        if (TryInvokeGetWorldPosition(control3D, lng, lat, out Vector3 world))
        {
            world.y += yOffset + extraYOffset;
            return world;
        }

        if (debugLogs)
            Debug.LogWarning("OLMGeoMapper.LatLonToWorld: could not resolve GetWorldPosition(WithElevation) on this control.");

        return Vector3.zero;
    }

    private bool TryInvokeGetWorldPositionWithElevation(ControlBase3D control, double lng, double lat, out Vector3 world)
    {
        world = Vector3.zero;
        var t = control.GetType();

        // Vector3 GetWorldPositionWithElevation(double lng, double lat)
        var m1 = t.GetMethod(
            "GetWorldPositionWithElevation",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(double), typeof(double) },
            null
        );

        if (m1 != null && m1.ReturnType == typeof(Vector3))
        {
            world = (Vector3)m1.Invoke(control, new object[] { lng, lat });
            return true;
        }

        // Vector3 GetWorldPositionWithElevation(float lng, float lat)
        var m2 = t.GetMethod(
            "GetWorldPositionWithElevation",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(float), typeof(float) },
            null
        );

        if (m2 != null && m2.ReturnType == typeof(Vector3))
        {
            world = (Vector3)m2.Invoke(control, new object[] { (float)lng, (float)lat });
            return true;
        }

        return false;
    }


    // ------------------------------------------------------
    // Preferred: feet-ish screen sample -> Lat/Lon
    // ------------------------------------------------------

    public bool TryFeetScreenToLatLon(out double lat, out double lon)
    {
        Init();
        lat = 0.0;
        lon = 0.0;

        if (control3D == null) return false;

        Camera cam = control3D.activeCamera != null ? control3D.activeCamera : sceneCamera;
        if (cam == null) return false;

        // Ensure the control has a camera (some builds need this set explicitly)
        if (control3D.activeCamera == null && cam != null)
            control3D.activeCamera = cam;

        Vector2 sp = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * screenSampleY01);

        GeoPoint g;
        try
        {
            g = control3D.ScreenToLocation(sp);
        }
        catch (System.NullReferenceException)
        {
            // TileSetControl.HitTest can throw early while tileset/control is warming up.
            if (debugLogs)
                Debug.Log("[OLMGeoMapper] ScreenToLocation not ready yet (HitTest null). Will retry.");
            return false;
        }

        lat = g.latitude;
        lon = g.longitude;

        if (!IsValidLatLon(lat, lon) || IsZeroZero(lat, lon))
        {
            if (_hasLastGood)
            {
                lat = _lastGoodLat;
                lon = _lastGoodLon;
                return true;
            }

            if (debugLogs)
                Debug.Log($"[OLMGeoMapper] TryFeetScreenToLatLon invalid raw lat/lon=({g.latitude},{g.longitude}) screen={sp}");

            return false;
        }

        _hasLastGood = true;
        _lastGoodLat = lat;
        _lastGoodLon = lon;
        return true;
    }

    // ------------------------------------------------------
    // Optional: World -> Lat/Lon (best-effort)
    // Use this for diagnostics only; gameplay should use TryFeetScreenToLatLon.
    // ------------------------------------------------------

    public bool TryWorldToLatLon(Vector3 worldPos, out double lat, out double lon)
    {
        Init();
        lat = 0.0;
        lon = 0.0;

        if (control3D == null) return false;

        // Ensure control has the right camera for any internal projections.
        if (control3D.activeCamera == null && sceneCamera != null)
            control3D.activeCamera = sceneCamera;

        // 1) Try native world->coords methods (if present in this control/version).
        //    Some builds expect local-space input, others world-space. Try both.
        Vector3 localPos = control3D.transform.InverseTransformPoint(worldPos);

        if (TryInvokeWorldToCoords(control3D, localPos, out lon, out lat) && IsValidLatLon(lat, lon))
            return true;

        if (TryInvokeWorldToCoords(control3D, worldPos, out lon, out lat) && IsValidLatLon(lat, lon))
            return true;

        // 2) Fallback: project world -> screen -> ScreenToLocation (only works if point is in front of camera).
        Camera cam = control3D.activeCamera != null ? control3D.activeCamera : sceneCamera;
        if (cam != null)
        {
            Vector3 sp3 = cam.WorldToScreenPoint(worldPos);

            if (sp3.z > 0f)
            {
                Vector2 sp2 = new Vector2(sp3.x, sp3.y);
                GeoPoint g = control3D.ScreenToLocation(sp2);

                lat = g.latitude;
                lon = g.longitude;

                if (IsValidLatLon(lat, lon) && !IsZeroZero(lat, lon))
                    return true;
            }
        }

        // 3) Last fallback: map center.
        if (map != null && TryGetMapCenter(map, out lon, out lat) && IsValidLatLon(lat, lon))
            return true;

        return false;
    }

    // ------------------------------------------------------
    // Reflection helpers (tolerate API drift)
    // ------------------------------------------------------

    private bool TryInvokeGetWorldPosition(ControlBase3D control, double lng, double lat, out Vector3 world)
    {
        world = Vector3.zero;
        var t = control.GetType();

        // Vector3 GetWorldPosition(double lng, double lat)
        var m1 = t.GetMethod(
            "GetWorldPosition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(double), typeof(double) },
            null
        );

        if (m1 != null && m1.ReturnType == typeof(Vector3))
        {
            world = (Vector3)m1.Invoke(control, new object[] { lng, lat });
            return true;
        }

        // Vector3 GetWorldPosition(float lng, float lat)
        var m2 = t.GetMethod(
            "GetWorldPosition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(float), typeof(float) },
            null
        );

        if (m2 != null && m2.ReturnType == typeof(Vector3))
        {
            world = (Vector3)m2.Invoke(control, new object[] { (float)lng, (float)lat });
            return true;
        }

        // void GetWorldPosition(double lng, double lat, out Vector3 world)
        var m3 = t.GetMethod(
            "GetWorldPosition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(double), typeof(double), typeof(Vector3).MakeByRefType() },
            null
        );

        if (m3 != null)
        {
            object[] args = new object[] { lng, lat, Vector3.zero };
            m3.Invoke(control, args);
            world = (Vector3)args[2];
            return true;
        }

        return false;
    }

    private bool TryInvokeWorldToCoords(ControlBase3D control, Vector3 pos, out double lng, out double lat)
    {
        lng = 0.0;
        lat = 0.0;

        var t = control.GetType();

        // bool GetCoordsByWorldPosition(Vector3 world, out double lng, out double lat)
        var a = t.GetMethod(
            "GetCoordsByWorldPosition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Vector3), typeof(double).MakeByRefType(), typeof(double).MakeByRefType() },
            null
        );

        if (a != null && a.ReturnType == typeof(bool))
        {
            object[] args = new object[] { pos, 0d, 0d };
            bool ok = (bool)a.Invoke(control, args);
            if (ok)
            {
                lng = (double)args[1];
                lat = (double)args[2];
                return true;
            }
        }

        // GeoPoint GetCoords(Vector3 world)
        var b = t.GetMethod(
            "GetCoords",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Vector3) },
            null
        );

        if (b != null && b.ReturnType == typeof(GeoPoint))
        {
            GeoPoint gp = (GeoPoint)b.Invoke(control, new object[] { pos });
            lat = gp.latitude;
            lng = gp.longitude;
            return true;
        }

        // void GetCoords(Vector3 world, out double lng, out double lat)
        var c = t.GetMethod(
            "GetCoords",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Vector3), typeof(double).MakeByRefType(), typeof(double).MakeByRefType() },
            null
        );

        if (c != null)
        {
            object[] args = new object[] { pos, 0d, 0d };
            c.Invoke(control, args);
            lng = (double)args[1];
            lat = (double)args[2];
            return true;
        }

        return false;
    }

    private bool TryGetMapCenter(Map m, out double lng, out double lat)
    {
        lng = 0.0;
        lat = 0.0;

        var view = m.view;
        if (view == null) return false;

        var t = view.GetType();

        // void GetCenter(out double lng, out double lat)
        var g1 = t.GetMethod(
            "GetCenter",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(double).MakeByRefType(), typeof(double).MakeByRefType() },
            null
        );

        if (g1 != null)
        {
            object[] args = new object[] { 0d, 0d };
            g1.Invoke(view, args);
            lng = (double)args[0];
            lat = (double)args[1];
            return true;
        }

        // void GetCenter(out float lng, out float lat)
        var g2 = t.GetMethod(
            "GetCenter",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(float).MakeByRefType(), typeof(float).MakeByRefType() },
            null
        );

        if (g2 != null)
        {
            object[] args = new object[] { 0f, 0f };
            g2.Invoke(view, args);
            lng = (float)args[0];
            lat = (float)args[1];
            return true;
        }

        return false;
    }

    // ------------------------------------------------------
    // Validation helpers
    // ------------------------------------------------------

    private static bool IsZeroZero(double lat, double lon)
    {
        return Mathf.Abs((float)lat) < 0.000001f && Mathf.Abs((float)lon) < 0.000001f;
    }

    private static bool IsValidLatLon(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon)) return false;
        if (double.IsInfinity(lat) || double.IsInfinity(lon)) return false;
        if (lat < -90 || lat > 90) return false;
        if (lon < -180 || lon > 180) return false;
        return true;
    }

    private static T FindAny<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        var obj = UnityEngine.Object.FindFirstObjectByType<T>();
        if (obj != null) return obj;
        return UnityEngine.Object.FindAnyObjectByType<T>();
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }
}
