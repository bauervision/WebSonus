// TargetGeoBinder.cs
using UnityEngine;

[DisallowMultipleComponent]
public class TargetGeoBinder : MonoBehaviour
{
    public TargetProxy proxy;
    public float yOffset = 0.0f;

    Terrain _terrain;
    GeoMapper _mapper;
    double _lastLat, _lastLon;

    void Awake()
    {
        _mapper = PlayerLocator.instance?.mapper;
        _terrain = PlayerLocator.instance?.terrainRef;
        Apply(true); // position immediately if actor is already set
    }

    void LateUpdate()
    {
        if (proxy == null || proxy.actor == null || _mapper == null) return;

        var a = proxy.actor;
        if (!Mathf.Approximately((float)a._Lat, (float)_lastLat) ||
            !Mathf.Approximately((float)a._Lon, (float)_lastLon))
        {
            Apply(false);
        }
    }

    public void Apply(bool forceMarkerRedraw)
    {
        if (proxy == null || proxy.actor == null || _mapper == null) return;

        var a = proxy.actor;

        // Lat/Lon -> world (XZ)
        Vector3 world = _mapper.LatLonToWorld(a._Lat, a._Lon);

        // Height from terrain
        if (_terrain != null)
        {
            float groundY = _terrain.SampleHeight(world) + _terrain.transform.position.y;
            world.y = groundY + yOffset;
        }

        transform.position = world;
        _lastLat = a._Lat; _lastLon = a._Lon;

        // Keep the map marker in sync too (if present)
        var m = a.GetMarker();
        if (m != null) m.position = new Vector2((float)a._Lon, (float)a._Lat); // (lon, lat)

        if (forceMarkerRedraw) OnlineMaps.instance?.Redraw();
    }
}
