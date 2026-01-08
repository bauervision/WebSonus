// Assets/Sonus/Core/DirectionMath.cs
using UnityEngine;

namespace Sonus.Core
{
    public static class DirectionMath
    {
        /// <summary>
        /// Returns signed angle (degrees) from forward → targetWorldPos, in [-180, 180].
        /// Positive = left, negative = right.
        /// </summary>
        public static float SignedAngleToTarget(Vector3 origin, Vector3 forward, Vector3 targetWorldPos)
        {
            Vector3 toTarget = (targetWorldPos - origin);
            toTarget.y = 0f;
            forward.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f) return 0f;

            return Vector3.SignedAngle(forward.normalized, toTarget.normalized, Vector3.up);
        }

        /// <summary>
        /// Simple bucket for orientation cues.
        /// </summary>
        public static OrientationKind ClassifyOrientation(float signedAngleDeg, float aheadThresholdDeg = 10f)
        {
            float abs = Mathf.Abs(signedAngleDeg);

            if (abs <= aheadThresholdDeg) return OrientationKind.Ahead;

            if (abs >= 180f - aheadThresholdDeg) return OrientationKind.Behind;

            return signedAngleDeg > 0 ? OrientationKind.Left : OrientationKind.Right;
        }
    }

    public enum OrientationKind
    {
        Ahead,
        Left,
        Right,
        Behind
    }
}
