using System;

namespace AncientWarfare3.core.performance
{
    public static class AWActorPresentationDirtyRules
    {
        public static bool ShouldStop(int processed, int maximumItems,
            long elapsedTicks, long budgetTicks)
        {
            return maximumItems <= 0 || processed >= maximumItems ||
                   budgetTicks <= 0L || elapsedTicks >= budgetTicks;
        }

        public static int AdvanceRepairCursor(int cursor, int visited,
            int total)
        {
            if (total <= 0) return 0;
            int next = Math.Max(0, cursor) + Math.Max(0, visited);
            return next >= total ? next % total : next;
        }

        public static bool InitialCaptureComplete(int visited, int total)
        {
            return total <= 0 || visited >= total;
        }

    }

    public static class AWActorSpriteInitializationRules
    {
        public static bool ShouldDeferHeadCheck(bool dirtyHead,
            bool frameDataReady, bool animationHeadsReady, bool textureReady)
        {
            return dirtyHead &&
                   (!frameDataReady || !animationHeadsReady ||
                    !textureReady);
        }
    }
}
