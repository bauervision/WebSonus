// Assets/Sonus/Core/Scripts/SonusLocationState.cs
namespace Sonus.Core
{
    public static class SonusLocationState
    {
        public static double Lat { get; private set; }
        public static double Lng { get; private set; }

        public static void Set(double lat, double lng)
        {
            Lat = lat;
            Lng = lng;
        }
    }
}
