using System;
using UnityEngine;

public static class GeoUtils
{
    public static Vector3 GeoToWorld(Vector2 geo, bool debugCube = false)
    {
        var userGeo = new Vector2(
            (float)PlayerLocator.instance.latitude,
            (float)PlayerLocator.instance.longitude
        );

        Vector2 delta = geo - userGeo;

        // Approximate scale factor: 1 degree = ~111,000 meters
        float scale = 111000f;

        float dx = delta.y * scale; // Lon → X (east-west)
        float dz = delta.x * scale; // Lat → Z (north-south)

        Vector3 worldPos = new Vector3(dx, 0, dz);

        if (debugCube)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.transform.position = worldPos;
            marker.transform.localScale = Vector3.one * 10;
            marker.name = $"TargetCube_{geo.x:F4}_{geo.y:F4}";
            marker.GetComponent<Renderer>().material.color = Color.red;
        }

        return worldPos;
    }


    public const double Rad2Deg = 180 / Math.PI;
    public const double Deg2Rad = Math.PI / 180;
    public const double earthRadius = 6371;


    /// <summary>
    /// The distance between two geographical coordinates.
    /// </summary>
    /// <param name="point1">Coordinate (X - Lng, Y - Lat)</param>
    /// <param name="point2">Coordinate (X - Lng, Y - Lat)</param>
    /// <returns>Distance (km).</returns>
    public static Vector2 DistanceBetweenPoints(Vector2 point1, Vector2 point2)
    {
        double scfY = Math.Sin(point1.y * Deg2Rad);
        double sctY = Math.Sin(point2.y * Deg2Rad);
        double ccfY = Math.Cos(point1.y * Deg2Rad);
        double cctY = Math.Cos(point2.y * Deg2Rad);
        double cX = Math.Cos((point1.x - point2.x) * Deg2Rad);
        double sizeX1 = Math.Abs(earthRadius * Math.Acos(scfY * scfY + ccfY * ccfY * cX));
        double sizeX2 = Math.Abs(earthRadius * Math.Acos(sctY * sctY + cctY * cctY * cX));
        float sizeX = (float)((sizeX1 + sizeX2) / 2.0);
        float sizeY = (float)(earthRadius * Math.Acos(scfY * sctY + ccfY * cctY));
        if (float.IsNaN(sizeX)) sizeX = 0;
        if (float.IsNaN(sizeY)) sizeY = 0;
        return new Vector2(sizeX, sizeY);
    }

    public static float HaversineDistanceKm(Vector2 point1, Vector2 point2)
    {
        double lat1 = point1.y * Deg2Rad;
        double lon1 = point1.x * Deg2Rad;
        double lat2 = point2.y * Deg2Rad;
        double lon2 = point2.x * Deg2Rad;

        double dLat = lat2 - lat1;
        double dLon = lon2 - lon1;

        double a = Math.Pow(Math.Sin(dLat / 2), 2) +
                   Math.Cos(lat1) * Math.Cos(lat2) *
                   Math.Pow(Math.Sin(dLon / 2), 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double distanceKm = earthRadius * c;

        return (float)distanceKm;
    }

}
