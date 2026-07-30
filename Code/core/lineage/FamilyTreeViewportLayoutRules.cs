using System;

namespace AncientWarfare3.core.lineage
{
    internal static class FamilyTreeViewportLayoutRules
    {
        public static float CenterPanX(float targetCenterX,
            float measuredViewportWidth, float fallbackViewportWidth)
        {
            return ResolveExtent(measuredViewportWidth,
                       fallbackViewportWidth) * 0.5f - targetCenterX;
        }

        public static float CenterPanY(float targetTopY, float nodeHeight,
            float measuredViewportHeight, float fallbackViewportHeight)
        {
            return targetTopY + nodeHeight * 0.5f -
                   ResolveExtent(measuredViewportHeight,
                       fallbackViewportHeight) * 0.5f;
        }

        public static float CenteredTreeStartX(float totalTreeWidth,
            float padding, float measuredViewportWidth,
            float fallbackViewportWidth)
        {
            float viewportWidth = ResolveExtent(measuredViewportWidth,
                fallbackViewportWidth);
            return padding + Math.Max(0f,
                (viewportWidth - totalTreeWidth) * 0.5f);
        }

        public static float CanvasWidth(float totalTreeWidth, float padding,
            float measuredViewportWidth, float fallbackViewportWidth)
        {
            return Math.Max(
                ResolveExtent(measuredViewportWidth, fallbackViewportWidth),
                totalTreeWidth + padding * 2f);
        }

        public static float SizeDeltaForDesiredExtent(float desiredExtent,
            float parentExtent, float anchorSpan)
        {
            float span = Math.Max(0f, Math.Min(1f, anchorSpan));
            return Math.Max(1f, desiredExtent) -
                   Math.Max(0f, parentExtent) * span;
        }

        private static float ResolveExtent(float measuredExtent,
            float fallbackExtent)
        {
            if (!float.IsNaN(measuredExtent) &&
                !float.IsInfinity(measuredExtent) && measuredExtent > 0f)
                return measuredExtent;
            return Math.Max(1f, fallbackExtent);
        }
    }
}
