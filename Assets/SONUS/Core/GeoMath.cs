using System;
using UnityEngine;

namespace Sonus.Core
{
    /// <summary>
    /// Core geospatial math utilities for Sonus.
    /// No PlayerLocator, no debug cubes, no scene dependencies.
    /// </summary>
    public static class GeoMath
    {
        public const double Rad2Deg = 180.0 / Math.PI;
        public const double Deg2Rad = Math.PI / 180.0;
        public const double EarthRadiusMeters = 6378137.0;

        /// <summary>
        /// The distance between two geographical coordinates on Earth, in meters.
        /// point = (x: Lng, y: Lat)
        /// Returns (x: east-west meters, y: north-south meters).
        /// </summary>
        public static Vector2 DistanceBetweenPoints(Vector2 point1, Vector2 point2)
        {
            double scfY = Math.Sin(point1.y * Deg2Rad);
            double sctY = Math.Sin(point2.y * Deg2Rad);
            double ccfY = Math.Cos(point1.y * Deg2Rad);
            double cctY = Math.Cos(point2.y * Deg2Rad);
            double cX = Math.Cos((point1.x - point2.x) * Deg2Rad);

            double sizeX1 = Math.Abs(EarthRadiusMeters * Math.Acos(scfY * scfY + ccfY * ccfY * cX));
            double sizeX2 = Math.Abs(EarthRadiusMeters * Math.Acos(sctY * sctY + cctY * cctY * cX));
            float sizeX = (float)((sizeX1 + sizeX2) / 2.0);

            float sizeY = (float)(EarthRadiusMeters * Math.Acos(scfY * sctY + ccfY * cctY));

            if (float.IsNaN(sizeX)) sizeX = 0;
            if (float.IsNaN(sizeY)) sizeY = 0;

            return new Vector2(sizeX, sizeY);
        }

        /// <summary>
        /// Great-circle distance between two points (km).
        /// point = (x: Lng, y: Lat)
        /// </summary>
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
            double distanceMeters = EarthRadiusMeters * c;

            return (float)(distanceMeters / 1000.0);
        }

        /// <summary>
        /// Offset a starting lat/lon by heading (deg) and distance (m).
        /// startLatLon = (x: Lat, y: Lng)
        /// </summary>
        public static Vector2 OffsetLocation(Vector2 startLatLon, float headingDegrees, float distanceMeters)
        {
            double lat1 = Mathf.Deg2Rad * startLatLon.x;
            double lon1 = Mathf.Deg2Rad * startLatLon.y;
            double headingRad = Mathf.Deg2Rad * headingDegrees;

            double angularDistance = distanceMeters / EarthRadiusMeters;

            double lat2 = Mathf.Asin(
                Mathf.Sin((float)lat1) * Mathf.Cos((float)angularDistance) +
                Mathf.Cos((float)lat1) * Mathf.Sin((float)angularDistance) * Mathf.Cos((float)headingRad)
            );

            double lon2 = lon1 + Mathf.Atan2(
                Mathf.Sin((float)headingRad) * Mathf.Sin((float)angularDistance) * Mathf.Cos((float)lat1),
                Mathf.Cos((float)angularDistance) - Mathf.Sin((float)lat1) * Mathf.Sin((float)lat2)
            );

            // Back to degrees
            float newLat = Mathf.Rad2Deg * (float)lat2;
            float newLon = Mathf.Rad2Deg * (float)lon2;

            return new Vector2(newLat, newLon);
        }

        /// <summary>
        /// Convert geo to local XZ in meters, relative to an origin lat/lon.
        /// geo & origin = (x: Lat, y: Lng)
        /// </summary>
        public static Vector3 GeoToLocalMeters(Vector2 geo, Vector2 originLatLon, float metersPerDegree = 111000f)
        {
            Vector2 delta = geo - originLatLon;

            float dx = delta.y * metersPerDegree; // Lon → X (east-west)
            float dz = delta.x * metersPerDegree; // Lat → Z (north-south)

            return new Vector3(dx, 0f, dz);
        }

        /// <summary>
        /// Convert local XZ meters back to lat/lon, relative to an origin.
        /// originLatLon = (x: Lat, y: Lng)
        /// </summary>
        public static Vector2 LocalMetersToGeo(Vector3 localMeters, Vector2 originLatLon, float metersPerDegree = 111000f)
        {
            float dLon = localMeters.x / metersPerDegree;
            float dLat = localMeters.z / metersPerDegree;

            float lat = originLatLon.x + dLat;
            float lon = originLatLon.y + dLon;

            return new Vector2(lat, lon);
        }
    }
}
