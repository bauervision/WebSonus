using UnityEngine;

public static class GeoUtil
{
    // Earth radius in meters
    private const double R = 6371000.0;

    /// <summary>
    /// Returns (lat, lon) in degrees, distanceMeters away from origin, at a random bearing.
    /// </summary>
    public static Vector2 RandomPointAround(double originLatDeg, double originLonDeg, float distanceMeters)
    {
        double bearing = Random.Range(0f, 360f) * Mathf.Deg2Rad;

        double lat1 = originLatDeg * Mathf.Deg2Rad;
        double lon1 = originLonDeg * Mathf.Deg2Rad;

        double dByR = distanceMeters / R;

        double sinLat1 = System.Math.Sin(lat1);
        double cosLat1 = System.Math.Cos(lat1);

        double sinD = System.Math.Sin(dByR);
        double cosD = System.Math.Cos(dByR);

        double sinLat2 = sinLat1 * cosD + cosLat1 * sinD * System.Math.Cos(bearing);
        double lat2 = System.Math.Asin(sinLat2);

        double y = System.Math.Sin(bearing) * sinD * cosLat1;
        double x = cosD - sinLat1 * sinLat2;
        double lon2 = lon1 + System.Math.Atan2(y, x);

        // Normalize lon to [-180, 180]
        double lon2Deg = (lon2 * Mathf.Rad2Deg + 540.0) % 360.0 - 180.0;
        double lat2Deg = lat2 * Mathf.Rad2Deg;

        return new Vector2((float)lat2Deg, (float)lon2Deg);
    }
}
