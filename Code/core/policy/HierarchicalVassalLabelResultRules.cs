using System;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalLabelResultRules
    {
        internal const float PositionThreshold = 0.1f;
        internal const float SizeThreshold = 0.01f;
        internal const float AngleThreshold = 0.5f;

        internal static bool AreEquivalent(
            HierarchicalVassalLabelBuildResult pLeft,
            HierarchicalVassalLabelBuildResult pRight)
        {
            if (!string.Equals(pLeft.DisplayText, pRight.DisplayText,
                    StringComparison.Ordinal) ||
                pLeft.CountryLabelGap != pRight.CountryLabelGap)
                return false;

            HierarchicalVassalMapModeLabelPlacement left = pLeft.Placement;
            HierarchicalVassalMapModeLabelPlacement right = pRight.Placement;
            return Math.Abs(left.Centroid.x - right.Centroid.x) <
                       PositionThreshold &&
                   Math.Abs(left.Centroid.y - right.Centroid.y) <
                       PositionThreshold &&
                   Math.Abs(left.Size - right.Size) < SizeThreshold &&
                   CircularAngleDistance(left.Angle, right.Angle) <
                       AngleThreshold;
        }

        private static float CircularAngleDistance(float pLeft,
            float pRight)
        {
            float distance = Math.Abs(pLeft - pRight) % 360f;
            return distance > 180f ? 360f - distance : distance;
        }
    }
}
