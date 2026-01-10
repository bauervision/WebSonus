// Assets/SONUS/Web/Debug/RotationProbe.cs
using UnityEngine;
using OnlineMaps;
/// <summary>
/// Attach to any object you want to observe. Logs rotation every frame.
/// Optionally compares against a headingSource (player root or camera rig).
/// </summary>
public class RotationProbe : MonoBehaviour
{
    [Header("Map source (what counts as 'map change')")]
    [Tooltip("If null, will try to find any OnlineMaps in scene.")]
    public Map map;

    [Tooltip("If null, uses OnlineMapsMarkerManager.instance")]
    public Marker2DManager markerManager;

    [Tooltip("0 = first marker in OnlineMaps Marker Manager list.")]
    public int markerIndex = 0;

    [Header("Optional compare")]
    [Tooltip("Set to playerRoot (yaw carrier) to compare yaw to marker rotation.")]
    public Transform headingSource;

    [Header("Map-change thresholds")]
    [Tooltip("Degrees of lat/lon change required to count as a map pan. (~1e-5 ≈ 1m-ish)")]
    public double centerEpsDeg = 1e-5;

    [Tooltip("Zoom delta required to count as a zoom change.")]
    public float zoomEps = 0.01f;

    [Tooltip("Degrees of bearing/rotation change required to count as a bearing change (if available).")]
    public float bearingEpsDeg = 0.5f;

    [Header("Marker-change thresholds (when map changes)")]
    [Tooltip("Only include marker rotation in log if it changed by >= this amount since last log. 0 = always include.")]
    public float markerRotEpsDeg = 0f;

    [Header("Output")]
    public bool showOnScreen = false;

    // last snapshot
    double _lastLat = double.NaN;
    double _lastLon = double.NaN;
    float _lastZoom = float.NaN;
    float _lastBearing = float.NaN; // best-effort; may remain NaN

    float _lastMarkerRot = float.NaN;
    string _lastLine = "";



    void Update()
    {
        if (map == null || map.view == null) return;

        // --- current map state ---
        double lon = map.view.center.x;
        double lat = map.view.center.y;

        // bearing/rotation is not guaranteed across OnlineMaps setups.
        // We'll try a couple common properties via reflection, otherwise NaN.
        float bearing = TryGetMapBearingDeg(map);

        bool first = double.IsNaN(_lastLat);

        bool centerChanged = first ||
            System.Math.Abs(lat - _lastLat) >= centerEpsDeg ||
            System.Math.Abs(lon - _lastLon) >= centerEpsDeg;


        bool bearingChanged = false;
        if (!float.IsNaN(bearing))
        {
            bearingChanged = first ||
                Mathf.Abs(Mathf.DeltaAngle(_lastBearing, bearing)) >= bearingEpsDeg;
        }

        // Only log when the MAP changes
        if (!(centerChanged || bearingChanged))
            return;

        _lastLat = lat;
        _lastLon = lon;

        _lastBearing = bearing;

        // --- marker rotation ---
        float markerRot = float.NaN;
        if (markerManager != null && markerManager.items != null &&
            markerIndex >= 0 && markerIndex < markerManager.items.Count &&
            markerManager.items[markerIndex] != null)
        {
            markerRot = Normalize360(markerManager.items[markerIndex].rotation);
        }

        bool includeMarkerRot = true;
        if (markerRotEpsDeg > 0f && !float.IsNaN(_lastMarkerRot) && !float.IsNaN(markerRot))
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(_lastMarkerRot, markerRot));
            includeMarkerRot = d >= markerRotEpsDeg;
        }
        if (!float.IsNaN(markerRot))
            _lastMarkerRot = markerRot;

        // --- heading source compare ---
        float srcYaw = float.NaN;
        float deltaYaw = float.NaN;
        if (headingSource != null)
        {
            srcYaw = Normalize360(headingSource.eulerAngles.y);
            if (!float.IsNaN(markerRot))
                deltaYaw = Mathf.DeltaAngle(srcYaw, markerRot);
        }

        // Build log line
        string why =
            $"{(centerChanged ? "center " : "")}{(bearingChanged ? "bearing " : "")}".Trim();

        _lastLine =
            $"[OM Probe] MAP CHANGE ({why}) center=({lat:F6},{lon:F6}) " +
            (float.IsNaN(bearing) ? "" : $" bearing={bearing:F1}") +
            (includeMarkerRot && !float.IsNaN(markerRot) ? $" | marker[{markerIndex}].rot={markerRot:F1}" : "") +
            (headingSource != null ? $" | srcYaw={srcYaw:F1} dYaw={deltaYaw:F1}" : "");


    }

    static float Normalize360(float deg)
    {
        deg %= 360f;
        if (deg < 0f) deg += 360f;
        return deg;
    }

    static float TryGetMapBearingDeg(Map m)
    {
        // Best-effort: some OnlineMaps controls have bearing/rotation; many don't.
        // We probe common names to avoid compile-time dependency on a specific control.
        try
        {
            var t = m.GetType();
            // common: "bearing"
            var p1 = t.GetProperty("bearing");
            if (p1 != null && p1.PropertyType == typeof(float))
                return (float)p1.GetValue(m, null);

            // common: "rotation"
            var p2 = t.GetProperty("rotation");
            if (p2 != null && p2.PropertyType == typeof(float))
                return (float)p2.GetValue(m, null);

            // some controls attach bearing on map.control
            if (m.control != null)
            {
                var ct = m.control.GetType();
                var cp1 = ct.GetProperty("bearing");
                if (cp1 != null && cp1.PropertyType == typeof(float))
                    return (float)cp1.GetValue(m.control, null);

                var cp2 = ct.GetProperty("rotation");
                if (cp2 != null && cp2.PropertyType == typeof(float))
                    return (float)cp2.GetValue(m.control, null);
            }
        }
        catch { }

        return float.NaN;
    }

    void OnGUI()
    {
        if (!showOnScreen) return;
        GUI.Label(new Rect(10, 10, 2000, 30), _lastLine);
    }
}