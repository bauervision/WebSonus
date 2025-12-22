
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

