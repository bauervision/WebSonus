namespace Sonus.Core
{
    /// <summary>
    /// Shared lat/lon for the "current focus" (usually the active target).
    /// </summary>
    public static class SonusLocationState
    {
        public static double Lat { get; private set; }
        public static double Lng { get; private set; }

        public static bool HasValue =>
            !(double.IsNaN(Lat) || double.IsNaN(Lng));

        public static void Set(double lat, double lng)
        {
            Lat = lat;
            Lng = lng;
        }

        public static void Clear()
        {
            Lat = double.NaN;
            Lng = double.NaN;
        }
    }
}
