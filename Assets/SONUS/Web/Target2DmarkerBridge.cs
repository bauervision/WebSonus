using UnityEngine;
using OnlineMaps;

public class Target2DMarkerBridge : MonoBehaviour
{
    [Header("Optional visuals")]
    public Texture2D markerIcon; // optional icon
    public bool showLabel = true;

    private Marker2D _marker;

    public void Upsert(TargetActor actor)
    {
        if (actor == null) return;

        var mgr = Marker2DManager.instance;
        if (mgr == null)
        {
            Debug.LogWarning("[Target2DMarkerBridge] Marker2DManager.instance is null (2D map not ready).");
            return;
        }

        // Online Maps uses GeoPoint(lng, lat)
        var gp = new GeoPoint(actor._Lon, actor._Lat);

        if (_marker == null)
        {
            // Create marker (signature depends on your setup; these are the common ones)
            _marker = (markerIcon != null)
                ? mgr.Create(gp, markerIcon)
                : mgr.Create(gp);

            // Attach actor so TargetActor.GetMarker() can find it
            _marker["data"] = actor;

            if (showLabel)
                _marker.label = string.IsNullOrEmpty(actor._Name) ? actor._ID : actor._Name;
        }
        else
        {
            _marker.location = gp;

            // Important: actor instance changes each respawn
            _marker["data"] = actor;

            if (showLabel)
                _marker.label = string.IsNullOrEmpty(actor._Name) ? actor._ID : actor._Name;
        }

        Map.instance?.Redraw();
    }

    public void Remove()
    {
        if (_marker == null) return;

        var mgr = Marker2DManager.instance;
        if (mgr != null) mgr.Remove(_marker);

        _marker = null;
        Map.instance?.Redraw();
    }
}
