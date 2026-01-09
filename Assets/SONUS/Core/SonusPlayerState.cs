namespace Sonus.Core
{
    /// <summary>Authoritative player geo + heading (3D scene).</summary>
    public static class SonusPlayerGeoState
    {
        public static double Lat { get; private set; } = double.NaN;
        public static double Lng { get; private set; } = double.NaN;

        // NEW: 0=N, 90=E, NaN = unknown
        public static double HeadingDeg { get; private set; } = double.NaN;

        public static bool HasValue =>
            !(double.IsNaN(Lat) || double.IsNaN(Lng));

        public static bool HasHeading =>
            !double.IsNaN(HeadingDeg);

        public static void Set(double lat, double lng)
        {
            Lat = lat;
            Lng = lng;
        }

        // NEW
        public static void Set(double lat, double lng, double headingDeg)
        {
            Lat = lat;
            Lng = lng;
            HeadingDeg = headingDeg;
        }

        // NEW
        public static void SetHeading(double headingDeg)
        {
            HeadingDeg = headingDeg;
        }

        public static void Clear()
        {
            Lat = double.NaN;
            Lng = double.NaN;
            HeadingDeg = double.NaN;
        }
    }
}
