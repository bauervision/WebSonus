using UnityEngine;
using System;

/// Optional: keep using the same reinit interface
public interface IGeoMapperReinitOL
{
    void ForceReinit();
}

/// Mapper that uses OnlineMaps Tileset to convert between
/// lat/lon <-> world position.
public class OnlineMapsGeoMapper : MonoBehaviour, IGeoMapperReinitOL
{
    [Header("Online Maps")]
    public OnlineMaps map;                      // assign in Inspector (or will auto-grab)
    public OnlineMapsTileSetControl tileset;    // assign in Inspector (or will auto-grab)

    [Header("Altitude")]
    [Tooltip("Extra Y offset when placing the player on the tileset (meters).")]
    public float yOffset = 1.8f;

    bool _init;

    void Awake()
    {
        Init();
    }

    void Init()
    {
        if (_init) return;

        if (map == null) map = OnlineMaps.instance;
        if (tileset == null) tileset = OnlineMapsTileSetControl.instance;

        if (map == null)
            Debug.LogWarning("OnlineMapsGeoMapper: OnlineMaps instance not found.");

        if (tileset == null)
            Debug.LogWarning("OnlineMapsGeoMapper: OnlineMapsTileSetControl instance not found.");

        _init = true;
    }

    public void ForceReinit()
    {
        // For tileset we don't really have cached ranges to rebuild, but we
        // leave this hook so PlayerLocator can call it safely.
        _init = false;
        Init();
    }

    /// Lat/Lon (degrees) -> world position on the OnlineMaps tileset.
    public Vector3 LatLonToWorld(double lat, double lon, float extraYOffset = 0f)
    {
        Init();
        if (tileset == null)
        {
            Debug.LogWarning("OnlineMapsGeoMapper.LatLonToWorld: tileset is null.");
            return Vector3.zero;
        }

        // NOTE: OnlineMaps uses (lng, lat) order.
        Vector3 world = tileset.GetWorldPosition(lon, lat);
        world.y += yOffset + extraYOffset;
        return world;
    }

    /// World position -> (lat, lon) using OnlineMaps tileset.
    public (double lat, double lon) WorldToLatLon(Vector3 worldPos)
    {
        Init();
        if (tileset == null)
        {
            Debug.LogWarning("OnlineMapsGeoMapper.WorldToLatLon: tileset is null.");
            return (0.0, 0.0);
        }

        double lng, lat;
        if (tileset.GetCoordsByWorldPosition(worldPos, out lng, out lat))
        {
            // Keep your convention: (lat, lon)
            return (lat, lng);
        }

        // Fallback: map center, so we never throw
        if (map != null)
        {
            map.GetPosition(out lng, out lat);
            return (lat, lng);
        }

        return (0.0, 0.0);
    }
}
