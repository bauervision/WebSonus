using UnityEngine;
using System;

public interface IGeoMapperReinit { void ForceReinit(); }


public class GeoMapper : MonoBehaviour
{
    [Header("Geographic bounds (degrees) — match your RWT area")]
    public double latMin, latMax;
    public double lonMin, lonMax;

    [Header("Terrain mapping (Unity)")]
    public Transform terrainOrigin;          // SW corner (or terrain.transform if that's 0,0)
    public Vector2 worldSizeXZ;

    [Header("Auto")]
    public Terrain terrainRef;               // assign your RWT Terrain here
    public bool autoFromTerrain = true;

    const double R = 6378137.0;
    static double MX(double lonDeg) => R * (lonDeg * Math.PI / 180.0);
    static double MY(double latDeg) => R * Math.Log(Math.Tan(Math.PI / 4.0 + (latDeg * Math.PI / 180.0) / 2.0));

    double _mxMin, _mxMax, _myMin, _myMax;
    bool _init;
    public void ForceReinit() { _init = false; }   // <- add this




    void OnValidate()
    {
        _init = false;
        if (autoFromTerrain && terrainRef != null)
        {
            var sz = terrainRef.terrainData.size;     // X,Z in world units
            worldSizeXZ = new Vector2(sz.x, sz.z);
            if (terrainOrigin == null) terrainOrigin = terrainRef.transform; // SW corner
        }
    }

    void OnDrawGizmosSelected()
    {
        try
        {
            Vector3 SW = LatLonToWorld(latMin, lonMin);
            Vector3 SE = LatLonToWorld(latMin, lonMax);
            Vector3 NE = LatLonToWorld(latMax, lonMax);
            Vector3 NW = LatLonToWorld(latMax, lonMin);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(SW, SE); Gizmos.DrawLine(SE, NE);
            Gizmos.DrawLine(NE, NW); Gizmos.DrawLine(NW, SW);
        }
        catch { /* ignore during edit-time init */ }
    }


    void Init()
    {
        if (_init) return;
        _mxMin = MX(lonMin); _mxMax = MX(lonMax);
        _myMin = MY(latMin); _myMax = MY(latMax);
        _init = true;
    }

    public Vector3 LatLonToWorld(double lat, double lon, float y = 0f)
    {
        Init();
        var mx = MX(lon);
        var my = MY(lat);
        var nx = (mx - _mxMin) / (_mxMax - _mxMin); // 0..1 across lon
        var nz = (my - _myMin) / (_myMax - _myMin); // 0..1 across lat
        var origin = terrainOrigin ? terrainOrigin.position : Vector3.zero;
        return new Vector3(origin.x + (float)(nx * worldSizeXZ.x), y, origin.z + (float)(nz * worldSizeXZ.y));
    }

    public (double lat, double lon) WorldToLatLon(Vector3 worldPos)
    {
        Init();
        var origin = terrainOrigin ? terrainOrigin.position : Vector3.zero;
        double lx = worldPos.x - origin.x;
        double lz = worldPos.z - origin.z;

        // normalized 0..1 across the baked rectangle
        double nx = Math.Clamp(lx / worldSizeXZ.x, 0.0, 1.0);
        double nz = Math.Clamp(lz / worldSizeXZ.y, 0.0, 1.0);

        // back to Mercator meters
        double mx = _mxMin + nx * (_mxMax - _mxMin);
        double my = _myMin + nz * (_myMax - _myMin);

        // inverse Web Mercator:
        // lon = mx/R ; lat = atan(sinh(my/R))
        double lon = (mx / R) * (180.0 / Math.PI);
        double lat = Math.Atan(Math.Sinh(my / R)) * (180.0 / Math.PI);

        return (lat, lon);
    }

}