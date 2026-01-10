using UnityEngine;
using Sonus.Core;

/// Draws the "cone" + lockline + heading line used by the audio guidance.
/// Green = To-target lock line
/// Yellow = cone edges (allowed heading band)
/// Red = current heading when outside cone (white when inside)
/// Cyan = correction arrow (points to turn direction)
public class AudioGuidanceLineViz : MonoBehaviour
{
    [Header("Refs")]
    public AudioManager audioManager;
    public TargetManager targetManager;

    [Tooltip("Optional override for heading source. If null, uses AudioManager.headingSource -> camera.")]
    public Transform headingSourceOverride;

    [Header("Draw")]
    public bool onlyWhenIn3D = true;
    public float startHeight = 0.2f;
    public float drawDistance = 25f;

    [Header("Cone tuning")]
    [Tooltip("When far, keep cone tighter (degrees).")]
    public float farConeDeg = 10f;

    [Tooltip("When near, widen a bit so small jitter doesn't spam counsel (degrees).")]
    public float nearConeDeg = 24f;

    [Tooltip("Distance at/above this uses farConeDeg.")]
    public float farMeters = 120f;

    [Tooltip("Distance at/below this uses nearConeDeg.")]
    public float nearMeters = 50f;

    [Header("Turn arrow")]
    public float arrowLen = 8f;
    public float arrowSideLen = 2.5f;

    void LateUpdate()
    {
        if (audioManager == null || targetManager == null) return;

        if (onlyWhenIn3D && targetManager.sceneController != null && targetManager.sceneController.IsIn2DMode)
            return;

        var actor = targetManager.currentTarget;
        if (actor == null) return;

        Transform h =
            headingSourceOverride != null ? headingSourceOverride :
            (audioManager.headingSource != null ? audioManager.headingSource :
            (audioManager.sceneCamera != null ? audioManager.sceneCamera.transform :
            (Camera.main != null ? Camera.main.transform : null)));

        Transform camT =
            (audioManager.sceneCamera != null ? audioManager.sceneCamera.transform :
            (Camera.main != null ? Camera.main.transform : h));

        if (h == null || camT == null) return;

        float meters = targetManager.DistanceToTargetMeters();
        if (!float.IsFinite(meters)) return;

        float allowedDeg = GetAllowedConeDeg(meters);

        Vector3 origin = camT.position;
        origin.y += startHeight;

        // --- Heading (what "forward" means for left/right)
        Vector3 heading = h.forward; heading.y = 0f;
        if (heading.sqrMagnitude < 1e-6f) return;
        heading.Normalize();

        // Apply the same yaw offset audio uses
        if (Mathf.Abs(audioManager.headingOffsetDeg) > 0.001f)
            heading = Quaternion.AngleAxis(audioManager.headingOffsetDeg, Vector3.up) * heading;

        // --- To-target direction (lock line)
        Vector3 toTDir = GetToTargetDir(origin, actor);
        if (toTDir.sqrMagnitude < 1e-6f) return;
        toTDir.Normalize();

        // relDeg: +RIGHT, -LEFT (matches AudioManager.ComputeRelativeAngleDeg)
        float relDeg = Vector3.SignedAngle(heading, toTDir, Vector3.up);
        if (audioManager.invertLeftRight) relDeg = -relDeg;

        float abs = Mathf.Abs(relDeg);
        bool inside = abs <= allowedDeg;

        // GREEN lock line
        Debug.DrawLine(origin, origin + toTDir * drawDistance, Color.green);

        // YELLOW cone edges around lockline (not around heading)
        Vector3 leftEdge = Quaternion.AngleAxis(-allowedDeg, Vector3.up) * toTDir;
        Vector3 rightEdge = Quaternion.AngleAxis(+allowedDeg, Vector3.up) * toTDir;
        Debug.DrawLine(origin, origin + leftEdge * drawDistance, Color.yellow);
        Debug.DrawLine(origin, origin + rightEdge * drawDistance, Color.yellow);

        // RED/WHITE heading line
        Debug.DrawLine(origin, origin + heading * drawDistance, inside ? Color.white : Color.red);

        // CYAN correction arrow: points the direction you should rotate
        // If relDeg > 0 => target is to the RIGHT => you need to turn RIGHT (arrow points right)
        // If relDeg < 0 => target is to the LEFT => you need to turn LEFT
        if (!inside)
        {
            float sign = Mathf.Sign(relDeg); // + right, - left
            Vector3 side = Vector3.Cross(Vector3.up, heading).normalized; // right of heading
            Vector3 arrowDir = (sign > 0f) ? side : -side;

            Vector3 a0 = origin + heading * (drawDistance * 0.35f);
            Vector3 a1 = a0 + arrowDir * arrowLen;
            Debug.DrawLine(a0, a1, Color.cyan);

            // Arrow head
            Vector3 back = -arrowDir;
            Vector3 headL = (a1 + (back + Vector3.Cross(Vector3.up, back)).normalized * arrowSideLen);
            Vector3 headR = (a1 + (back - Vector3.Cross(Vector3.up, back)).normalized * arrowSideLen);
            Debug.DrawLine(a1, headL, Color.cyan);
            Debug.DrawLine(a1, headR, Color.cyan);
        }

        // Optional: quick HUD text in console (disable if noisy)
        // Debug.Log($"[Viz] d={meters:F0} allowed={allowedDeg:F1} rel={relDeg:F1} inside={inside}");
    }

    float GetAllowedConeDeg(float meters)
    {
        // Farther than farMeters => farConeDeg
        if (meters >= farMeters) return farConeDeg;

        // Closer than nearMeters => nearConeDeg
        if (meters <= nearMeters) return nearConeDeg;

        // In-between => interpolate
        float t = Mathf.InverseLerp(farMeters, nearMeters, meters); // far->near
        return Mathf.Lerp(farConeDeg, nearConeDeg, t);
    }

    Vector3 GetToTargetDir(Vector3 origin, TargetActor actor)
    {
        // Prefer world mapping (matches reticle)
        if (audioManager.geoMapper != null)
        {
            Vector3 targetW = audioManager.geoMapper.LatLonToWorld(actor._Lat, actor._Lon, 0f);
            targetW.y = origin.y;

            Vector3 toT = targetW - origin; toT.y = 0f;
            return toT.sqrMagnitude < 1e-6f ? Vector3.zero : toT.normalized;
        }

        // Fallback: geo bearing (0=N, 90=E)
        if (!TryGetPlayerGeo(out double pLat, out double pLon)) return Vector3.zero;

        float bearing = (float)GeoBearingDeg(pLat, pLon, actor._Lat, actor._Lon);
        float br = bearing * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(br), 0f, Mathf.Cos(br)).normalized;
    }

    bool TryGetPlayerGeo(out double lat, out double lon)
    {
        lat = 0; lon = 0;

        if (SonusPlayerGeoState.HasValue)
        {
            lat = SonusPlayerGeoState.Lat;
            lon = SonusPlayerGeoState.Lng;
            if (lat != 0.0 && lon != 0.0) return true;
        }

        if (audioManager.geoMapper != null && audioManager.geoMapper.TryFeetScreenToLatLon(out lat, out lon))
            return true;

        if (SonusLocationState.Lat != 0.0 && SonusLocationState.Lng != 0.0)
        {
            lat = SonusLocationState.Lat;
            lon = SonusLocationState.Lng;
            return true;
        }

        return false;
    }

    static double GeoBearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double phi1 = lat1 * Mathf.Deg2Rad;
        double phi2 = lat2 * Mathf.Deg2Rad;
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;

        double y = System.Math.Sin(dLon) * System.Math.Cos(phi2);
        double x = System.Math.Cos(phi1) * System.Math.Sin(phi2) -
                   System.Math.Sin(phi1) * System.Math.Cos(phi2) * System.Math.Cos(dLon);

        double brng = System.Math.Atan2(y, x) * Mathf.Rad2Deg;
        brng = (brng + 360.0) % 360.0;
        return brng;
    }
}
