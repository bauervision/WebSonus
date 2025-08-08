using System;
using Newtonsoft.Json;
using UnityEngine;

[System.Serializable]
public abstract class SurgeActor
{
    ///<summary> Altitude of this actor </summary>
    public double _Alt;

    ///<summary> Direction this actor is currently facing/heading </summary>
    public float _Dir;
    ///<summary> The database key of this data </summary>
    public string _ID;
    ///<summary> The name of this target </summary>
    public string _Name;
    ///<summary> Has this actor received recent data updates? </summary>
    public bool _isActive;
    ///<summary> Latitude coordinates of this actor </summary>
    public double _Lat;
    ///<summary> Longitiude coordinates of this actor </summary>
    public double _Lon;
    ///<summary> A string containing the most recent timestamp from the server when an update was received for this actor. A comparison will be made to determine if the _isActive state should be changed
    /// based on the current system time and this value. If too much time has passed since the last update, this actor will be set to in-active. Eventually,
    /// the target will be removed completely if too much time has passed since it was updated.
    /// </summary>
    public string _Time;

}

[System.Serializable]
public enum TargetType { STATIONARY, DYNAMIC }

[System.Serializable]
public class TargetActor : SurgeActor
{

    ///<summary> Distinguishes whether the UI will display:  PERSON, VEHICLE, Objective </summary>
    public int _Type;

    [JsonConstructor]
    public TargetActor() { }

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
        _Alt = OnlineMapsElevationManagerBase.GetUnscaledElevationByCoordinate(_Lon, _Lat);
        _Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var marker = GetMarker();
        if (marker != null)
        {
            marker.position = new Vector2((float)_Lon, (float)_Lat);
        }
    }

    public OnlineMapsMarker GetMarker()
    {
        var items = OnlineMapsMarkerManager.instance.items;
        foreach (var marker in items)
        {
            if (marker["data"] is TargetActor actor && actor._ID == this._ID)
                return marker;
        }
        return null;
    }


}

public class TargetSelectorUI : MonoBehaviour
{
    public static TargetType SelectedType = TargetType.STATIONARY;

    public void SelectStationary() => SelectedType = TargetType.STATIONARY;
    public void SelectDynamic() => SelectedType = TargetType.DYNAMIC;
}




