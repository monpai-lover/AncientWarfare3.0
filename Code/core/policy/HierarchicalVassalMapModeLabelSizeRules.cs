using System;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapModeLabelSizeRules
    {
        public const float BaseScale = 0.325f;
        public const float MinimumSize = 0.7f;
        public const float MaximumSize = 8.0f;

        public static float Calculate(int area)
        {
            double scaled = Math.Sqrt(Math.Max(1, area)) * BaseScale;
            return (float)Math.Max(MinimumSize,
                Math.Min(MaximumSize, scaled));
        }
    }
}
