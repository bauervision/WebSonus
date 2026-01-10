using UnityEngine;
using Sonus.Core;

/// LineRenderer-based guidance visualization (visible in GAME view).
/// Green  = to-target lock line
/// Yellow = cone edges
/// Red    = current heading when OUTSIDE cone (white when inside)
/// Cyan   = turn direction arrow when outside
public class AudioGuidanceLineVizLR : MonoBehaviour
{
    [Header("Refs")]
    public AudioManager audioManager;
    public TargetManager targetManager;

    [Tooltip("Optional override for heading source. If null, uses AudioManager.headingSource -> camera.")]
    public Transform headingSourceOverride;

    [Header("Mode")]
    public bool onlyWhenIn3D = true;

    [Header("Draw")]
    public float startHeight = 0.25f;
    public float drawDistance = 30f;
    public float lineWidth = 0.04f;

    [Header("Cone tuning")]
    public float farConeDeg = 10f;
    public float nearConeDeg = 24f;
    public float farMeters = 120f;
    public float nearMeters = 50f;

    [Header("Arrow")]
    public float arrowLen = 8f;
    public float arrowSideLen = 2.0f;

    // Line renderers
    LineRenderer _lockLine;
    LineRenderer _coneLeft;
    LineRenderer _coneRight;
    LineRenderer _headingLine;
    LineRenderer _arrowMain;
    LineRenderer _arrowHeadL;
    LineRenderer _arrowHeadR;

    void Awake()
    {
        _lockLine = MakeLine("LockLine");
        _coneLeft = MakeLine("ConeLeft");
        _coneRight = MakeLine("ConeRight");
        _headingLine = MakeLine("HeadingLine");
        _arrowMain = MakeLine("ArrowMain");
        _arrowHeadL = MakeLine("ArrowHeadL");
        _arrowHeadR = MakeLine("ArrowHeadR");

        // Colors
        SetColor(_lockLine, Color.green);
        SetColor(_coneLeft, Color.yellow);
        SetColor(_coneRight, Color.yellow);
        SetColor(_headingLine, Color.white);
        SetColor(_arrowMain, Color.cyan);
        SetColor(_arrowHeadL, Color.cyan);
        SetColor(_arrowHeadR, Color.cyan);
    }

    void LateUpdate()
    {
        if (audioManager == null || targetManager == null) { HideAll(); return; }

        // If you don't have IsIn2DMode, just leave onlyWhenIn3D=false or delete this block.
        if (onlyWhenIn3D && targetManager.sceneController != null && targetManager.sceneController.IsIn2DMode)
        {
            HideAll();
            return;
        }

        var actor = targetManager.currentTarget;
        if (actor == null) { HideAll(); return; }

        Transform h =
            headingSourceOverride != null ? headingSourceOverride :
            (audioManager.headingSource != null ? audioManager.headingSource :
            (audioManager.sceneCamera != null ? audioManager.sceneCamera.transform :
            (Camera.main != null ? Camera.main.transform : null)));

        if (h == null) { HideAll(); return; }

        float meters = targetManager.DistanceToTargetMeters();
        if (!float.IsFinite(meters)) { HideAll(); return; }

        float allowedDeg = GetAllowedConeDeg(meters);

        // IMPORTANT: origin should match heading source to avoid small angular offsets
        Vector3 origin = h.position;
        origin.y += startHeight;

        // Heading vector (what "forward" means)
        Vector3 heading = h.forward; heading.y = 0f;
        if (heading.sqrMagnitude < 1e-6f) { HideAll(); return; }
        heading.Normalize();

        if (Mathf.Abs(audioManager.headingOffsetDeg) > 0.001f)
            heading = Quaternion.AngleAxis(audioManager.headingOffsetDeg, Vector3.up) * heading;

        // To-target direction (lock) - authoritative world pos from TargetManager (same as reticle)
        Vector3 toTDir = GetToTargetDir(origin);
        if (toTDir.sqrMagnitude < 1e-6f) { HideAll(); return; }
        toTDir.Normalize();

        // relDeg: +RIGHT, -LEFT (after optional invert)
        float relDeg = Vector3.SignedAngle(heading, toTDir, Vector3.up);
        if (audioManager.invertLeftRight) relDeg = -relDeg;

        float abs = Mathf.Abs(relDeg);
        bool inside = abs <= allowedDeg;

        // Draw lock and cone around lock
        Draw2(_lockLine, origin, origin + toTDir * drawDistance);

        Vector3 leftEdge = Quaternion.AngleAxis(-allowedDeg, Vector3.up) * toTDir;
        Vector3 rightEdge = Quaternion.AngleAxis(+allowedDeg, Vector3.up) * toTDir;
        Draw2(_coneLeft, origin, origin + leftEdge * drawDistance);
        Draw2(_coneRight, origin, origin + rightEdge * drawDistance);

        // Heading line color based on inside/outside
        SetColor(_headingLine, inside ? Color.white : Color.red);
        Draw2(_headingLine, origin, origin + heading * drawDistance);

        // Arrow only when outside
        if (!inside)
        {
            float sign = Mathf.Sign(relDeg); // + => target is right => turn right
            Vector3 rightOfHeading = Vector3.Cross(Vector3.up, heading).normalized;
            Vector3 arrowDir = (sign > 0f) ? rightOfHeading : -rightOfHeading;

            Vector3 a0 = origin + heading * (drawDistance * 0.35f);
            Vector3 a1 = a0 + arrowDir * arrowLen;

            Draw2(_arrowMain, a0, a1);

            // arrow head
            Vector3 back = (-arrowDir).normalized;
            Vector3 perp = Vector3.Cross(Vector3.up, back).normalized;

            Vector3 hL = a1 + (back + perp).normalized * arrowSideLen;
            Vector3 hR = a1 + (back - perp).normalized * arrowSideLen;

            Draw2(_arrowHeadL, a1, hL);
            Draw2(_arrowHeadR, a1, hR);

            ShowArrow(true);
        }
        else
        {
            ShowArrow(false);
        }
    }

    // ---------------- Helpers ----------------

    float GetAllowedConeDeg(float meters)
    {
        if (meters >= farMeters) return farConeDeg;
        if (meters <= nearMeters) return nearConeDeg;

        float t = Mathf.InverseLerp(farMeters, nearMeters, meters);
        return Mathf.Lerp(farConeDeg, nearConeDeg, t);
    }

    Vector3 GetToTargetDir(Vector3 origin)
    {
        // 1) BEST: use the same world anchor used by the reticle (authoritative)
        if (targetManager != null && targetManager.TryGetTargetWorldPos(out Vector3 targetWorld))
        {
            targetWorld.y = origin.y; // flatten for yaw-only guidance

            Vector3 toT = targetWorld - origin;
            toT.y = 0f;

            if (toT.sqrMagnitude > 1e-6f)
                return toT.normalized;
        }

        // 2) fallback: geo mapper -> world (if you still want it)
        var actor = (targetManager != null) ? targetManager.currentTarget : null;
        if (actor != null && audioManager != null && audioManager.geoMapper != null)
        {
            Vector3 targetW = audioManager.geoMapper.LatLonToWorld(actor._Lat, actor._Lon, 0f);
            targetW.y = origin.y;

            Vector3 toT = targetW - origin;
            toT.y = 0f;

            if (toT.sqrMagnitude > 1e-6f)
                return toT.normalized;
        }

        // 3) last resort: bearing-only (no mapper)
        if (actor != null && TryGetPlayerGeo(out double pLat, out double pLon))
        {
            float bearing = (float)GeoBearingDeg(pLat, pLon, actor._Lat, actor._Lon);
            float br = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(br), 0f, Mathf.Cos(br)).normalized;
        }

        return Vector3.zero;
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

    LineRenderer MakeLine(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;

        // Unlit material so it shows clearly
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // Render on top a bit
        lr.sortingOrder = 5000;

        lr.enabled = false;
        return lr;
    }

    void SetColor(LineRenderer lr, Color c)
    {
        if (lr == null) return;
        lr.startColor = c;
        lr.endColor = c;
    }

    void Draw2(LineRenderer lr, Vector3 a, Vector3 b)
    {
        if (lr == null) return;
        lr.enabled = true;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    void ShowArrow(bool on)
    {
        if (_arrowMain != null) _arrowMain.enabled = on;
        if (_arrowHeadL != null) _arrowHeadL.enabled = on;
        if (_arrowHeadR != null) _arrowHeadR.enabled = on;
    }

    void HideAll()
    {
        if (_lockLine != null) _lockLine.enabled = false;
        if (_coneLeft != null) _coneLeft.enabled = false;
        if (_coneRight != null) _coneRight.enabled = false;
        if (_headingLine != null) _headingLine.enabled = false;
        ShowArrow(false);
    }
}
