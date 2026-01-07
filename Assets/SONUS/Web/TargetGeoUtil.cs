// Assets/SONUS/Web/TargetGeoUtil.cs
using UnityEngine;

public static class TargetGeoUtil
{
    public static (double lat, double lon) OffsetLatLonMeters(double latDeg, double lonDeg, double northM, double eastM)
    {
        const double R = 6378137.0;
        double rad = System.Math.PI / 180.0;

        double dLat = northM / R;
        double dLon = eastM / (R * System.Math.Cos(latDeg * rad));

        double lat2 = latDeg + dLat / rad;
        double lon2 = lonDeg + dLon / rad;

        return (lat2, lon2);
    }

    // Equirectangular approximation (good enough at these distances)
    public static double ApproxMetersBetween(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double rad = System.Math.PI / 180.0;

        double phi1 = lat1 * rad;
        double phi2 = lat2 * rad;

        double dPhi = (lat2 - lat1) * rad;
        double dLam = (lon2 - lon1) * rad;

        double x = dLam * System.Math.Cos((phi1 + phi2) * 0.5);
        double y = dPhi;

        return System.Math.Sqrt(x * x + y * y) * R;
    }

    public static void MetersToLatLonDeltas(double atLatDeg, double meters, out double dLatDeg, out double dLonDeg)
    {
        const double R = 6378137.0;
        double rad = System.Math.PI / 180.0;

        double dLat = meters / R;
        double dLon = meters / (R * System.Math.Cos(atLatDeg * rad));

        dLatDeg = dLat / rad;
        dLonDeg = dLon / rad;
    }

    /// <summary>
    /// Offsets a lat/lon by geo meters in a given bearing (radians).
    /// Bearing: 0 = north, PI/2 = east.
    /// Returns (lat, lon).
    /// </summary>
    public static (double lat, double lon) GeoOffsetByBearing(double latDeg, double lonDeg, float meters, double bearingRad)
    {
        double northM = System.Math.Cos(bearingRad) * meters;
        double eastM = System.Math.Sin(bearingRad) * meters;
        return OffsetLatLonMeters(latDeg, lonDeg, northM, eastM);
    }
}
