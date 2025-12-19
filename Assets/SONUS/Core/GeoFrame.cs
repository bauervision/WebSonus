using UnityEngine;

namespace Sonus.Core
{
    /// <summary>
    /// Canonical heading + rotation unit helpers for Sonus.
    ///
    /// Key rule:
    /// - We do ALL heading math in DEGREES (0..360) in Core.
    /// - Only at the last step do we convert to the consumer’s unit system:
    ///   - OnlineMaps Marker2D.rotation uses "TURNS" (0..1) in our setup.
    /// </summary>
    public static class GeoFrame
    {
        /// <summary>Normalize degrees to [0,360).</summary>
        public static float Norm360(float deg) => Mathf.Repeat(deg, 360f);

        /// <summary>Normalize degrees to (-180,180].</summary>
        public static float Norm180(float deg) => Mathf.Repeat(deg + 180f, 360f) - 180f;

        /// <summary>
        /// Convert degrees to turns (0..1), where:
        /// 0.0=0°, 0.25=90°, 0.5=180°, 0.75=270°, 1.0=360°.
        /// </summary>
        public static float DegreesToTurns(float deg)
        {
            return Norm360(deg) / 360f;
        }

        /// <summary>
        /// Convert turns (0..1) back to degrees (0..360).
        /// </summary>
        public static float TurnsToDegrees(float turns)
        {
            return Norm360(turns * 360f);
        }

        // ----------------------------
        // Calibrated "world north" support
        // ----------------------------

        private static bool _hasCalibration;
        private static Vector3 _worldNorth = Vector3.forward;

        /// <summary>
        /// Calibrate what "north" means in Unity world space using two placed world points:
        /// center and a point slightly north (lat+Δ).
        /// This absorbs OnlineMaps tileset basis flips/rotations.
        /// </summary>
        public static void CalibrateWorldNorth(Vector3 worldCenter, Vector3 worldNorthPoint)
        {
            Vector3 dir = worldNorthPoint - worldCenter;
            dir.y = 0f;

            if (dir.sqrMagnitude < 1e-6f) return;

            _worldNorth = dir.normalized;
            _hasCalibration = true;
        }

        public static void ClearCalibration()
        {
            _worldNorth = Vector3.forward;
            _hasCalibration = false;
        }

        /// <summary>
        /// Bearing in DEGREES (0..360) from a world-forward vector, relative to calibrated north.
        /// </summary>
        public static float BearingDegFromWorldForward(Vector3 worldForward)
        {
            Vector3 f = worldForward;
            f.y = 0f;

            if (f.sqrMagnitude < 1e-6f) return 0f;
            f.Normalize();

            Vector3 n = _hasCalibration ? _worldNorth : Vector3.forward;

            float deg = Vector3.SignedAngle(n, f, Vector3.up);
            return Norm360(deg);
        }

        /// <summary>
        /// Apply an icon offset (in DEGREES) to a bearing (DEGREES).
        /// </summary>
        public static float ApplyIconOffsetDeg(float bearingDeg, float iconOffsetDeg)
        {
            return Norm360(bearingDeg + iconOffsetDeg);
        }

        /// <summary>
        /// Final value to assign to OnlineMaps Marker2D.rotation (TURNS),
        /// given a bearing in degrees and an icon offset in degrees.
        /// </summary>
        public static float Marker2DRotationTurns(float bearingDeg, float iconOffsetDeg = 0f)
        {
            float finalDeg = ApplyIconOffsetDeg(bearingDeg, iconOffsetDeg);
            return DegreesToTurns(finalDeg);
        }

        /// <summary>
        /// Signed delta from current bearing to target bearing (DEGREES). +left, -right.
        /// </summary>
        public static float BearingDeltaDeg(float currentBearingDeg, float targetBearingDeg)
        {
            return Mathf.DeltaAngle(Norm360(currentBearingDeg), Norm360(targetBearingDeg));
        }
    }
}
