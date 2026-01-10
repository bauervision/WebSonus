// Assets/SONUS/Web/TargetPatrolManager.cs
using UnityEngine;

[System.Serializable]
public struct GeoLL
{
    public double lat;
    public double lon;

    public GeoLL(double lat, double lon)
    {
        this.lat = lat;
        this.lon = lon;
    }

    public override string ToString() => $"({lat:F6},{lon:F6})";
}

public class TargetPatrolManager : MonoBehaviour
{
    [Header("Path")]
    [Tooltip("How far from the start point to pick A and B (meters).")]
    public float legMeters = 100f;

    [Tooltip("Meters per second along the path.")]
    public float speedMps = 1.25f;

    [Tooltip("Optional pause at each node (Start/A/B).")]
    public float dwellSeconds = 0f;

    [Header("Debug")]
    public bool debugLogs = false;

    GeoLL _start, _a, _b;
    int _seg;     // 0: S->A, 1: A->B, 2: B->S
    float _t;     // 0..1 progress on segment
    float _dwellUntil;
    bool _hasRoute;

    public bool HasRoute => _hasRoute;

    public void AssignRoute(double startLat, double startLon)
    {
        _start = new GeoLL(startLat, startLon);

        // Pick two random bearings; keep distances fixed (simple + stable)
        float brgA = Random.Range(0f, 360f);
        float brgB = (brgA + 180f + Random.Range(-45f, 45f)) % 360f;

        _a = OffsetMeters(_start, legMeters, brgA);
        _b = OffsetMeters(_start, legMeters, brgB);

        _seg = 0;
        _t = 0f;
        _dwellUntil = 0f;
        _hasRoute = true;

        if (debugLogs)
            Debug.Log($"[TargetPatrol] start={_start} A={_a} B={_b} leg={legMeters}m speed={speedMps}m/s");
    }

    public bool Tick(float dt, out double lat, out double lon)
    {
        lat = lon = 0;
        if (!_hasRoute) return false;

        if (dwellSeconds > 0f && Time.unscaledTime < _dwellUntil)
        {
            var pHold = GetPoint(_seg, _t);
            lat = pHold.lat; lon = pHold.lon;
            return true;
        }

        GeoLL p0, p1;
        GetEndpoints(_seg, out p0, out p1);

        // Our segment length is ~legMeters (or legMeters*sqrt(2) for A->B-ish),
        // but compute actual meters for consistent speed.
        float segMeters = (float)HaversineMeters(p0.lat, p0.lon, p1.lat, p1.lon);
        if (segMeters < 0.01f) segMeters = 0.01f;

        _t += (speedMps * dt) / segMeters;

        if (_t >= 1f)
        {
            _t = 0f;
            _seg = (_seg + 1) % 3;
            if (dwellSeconds > 0f) _dwellUntil = Time.unscaledTime + dwellSeconds;
        }

        var p = GetPoint(_seg, _t);
        lat = p.lat; lon = p.lon;
        return true;
    }

    void GetEndpoints(int seg, out GeoLL p0, out GeoLL p1)
    {
        switch (seg)
        {
            case 0: p0 = _start; p1 = _a; break;
            case 1: p0 = _a; p1 = _b; break;
            default: p0 = _b; p1 = _start; break;
        }
    }

    GeoLL GetPoint(int seg, float t)
    {
        GeoLL p0, p1;
        GetEndpoints(seg, out p0, out p1);

        // Lerp in lat/lon is fine at ~100m scale.
        double lat = Mathf.Lerp((float)p0.lat, (float)p1.lat, t);
        double lon = Mathf.Lerp((float)p0.lon, (float)p1.lon, t);
        return new GeoLL(lat, lon);
    }

    static GeoLL OffsetMeters(GeoLL origin, float meters, float bearingDeg)
    {
        // Small-distance local tangent approximation
        double dLat = (meters * Mathf.Cos(bearingDeg * Mathf.Deg2Rad)) / 111320.0;

        double metersPerDegLon = 111320.0 * System.Math.Cos(origin.lat * System.Math.PI / 180.0);
        if (System.Math.Abs(metersPerDegLon) < 1e-6) metersPerDegLon = 1e-6;

        double dLon = (meters * Mathf.Sin(bearingDeg * Mathf.Deg2Rad)) / metersPerDegLon;

        return new GeoLL(origin.lat + dLat, origin.lon + dLon);
    }

    static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = (lat2 - lat1) * System.Math.PI / 180.0;
        double dLon = (lon2 - lon1) * System.Math.PI / 180.0;

        double a =
            System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
            System.Math.Cos(lat1 * System.Math.PI / 180.0) * System.Math.Cos(lat2 * System.Math.PI / 180.0) *
            System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);

        double c = 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a));
        return R * c;
    }
}
