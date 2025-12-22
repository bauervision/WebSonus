using System;
using OnlineMaps;
using UnityEngine;

[System.Serializable]
public class TargetActor : SurgeActor
{

    ///<summary> Distinguishes whether the UI will display:  PERSON, VEHICLE, Objective </summary>
    public int _Type;

    public TargetActor(TargetActor targetActor)
    {
        this._Alt = targetActor._Alt;
        this._Dir = targetActor._Dir;
        this._Name = targetActor._Name;
        this._ID = targetActor._ID;
        this._isActive = targetActor._isActive;
        this._Lat = targetActor._Lat;
        this._Lon = targetActor._Lon;
        this._Time = targetActor._Time;
        this._Type = (int)targetActor._Type;
    }

    public TargetActor(TargetType type, double lat, double lng)
    {
        this._Alt = 0;
        this._Dir = 0;
        this._ID = "+ New Target";
        this._Name = "";
        this._isActive = true;
        this._Lat = lat;
        this._Lon = lng;
        this._Type = (int)type;// which index is the mesh we chose? 1 = person, 2 = vehicle, 3 = objective
        this._Time = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds().ToString();

    }

    public void MoveTo(Vector2 newLatLon)
    {
        _Lat = newLatLon.x;
        _Lon = newLatLon.y;

        _Alt = ElevationManagerBase.GetUnscaledElevationByCoordinate(_Lon, _Lat);
        _Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var marker = GetMarker();
        if (marker != null)
        {
            marker.location = new GeoPoint(_Lon, _Lat);

            // Optional: if you don’t see it move immediately in your setup
            Map.instance.Redraw();
        }
    }


    public Marker GetMarker()
    {
        // Guard: OnlineMaps may not be initialized in this scene
        var mgr = Marker2DManager.instance;
        if (mgr == null)
        {
            // Optional: log once if you care
            // Debug.LogWarning("[TargetActor] OnlineMapsMarkerManager.instance is null; no marker lookup.");
            return null;
        }

        var items = mgr.items;
        if (items == null)
        {
            // Optional: log once
            // Debug.LogWarning("[TargetActor] OnlineMapsMarkerManager.items is null; no markers to search.");
            return null;
        }

        foreach (var marker in items)
        {
            if (marker == null) continue;

            if (marker["data"] is TargetActor actor && actor._ID == this._ID)
                return marker;
        }

        return null;
    }



}
