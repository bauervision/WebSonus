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
    public const double earthRadius = 6378137.0;


    // ✅ Add this function to convert world pos back to lat/lon
    public static Vector2 WorldToGeo(Vector3 worldPos)
    {
        float lon = worldPos.x / 10000f; // Match your GeoToWorld scale
        float lat = worldPos.z / 10000f;
        return new Vector2(lat, lon);
    }

}
