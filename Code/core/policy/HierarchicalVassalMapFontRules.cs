using System;

namespace AncientWarfare3.core.policy
{
    public enum HierarchicalVassalMapFontMode
    {
        Bundled,
        GameLocalized
    }

    public static class HierarchicalVassalMapFontRules
    {
        public static HierarchicalVassalMapFontMode ResolveMode(bool pUseBundledFont)
        {
            return pUseBundledFont
                ? HierarchicalVassalMapFontMode.Bundled
                : HierarchicalVassalMapFontMode.GameLocalized;
        }

        public static bool ShouldUseBundledFont(HierarchicalVassalMapFontMode pMode)
        {
            return pMode == HierarchicalVassalMapFontMode.Bundled;
        }

        public static HierarchicalVassalMapFontMode Toggle(HierarchicalVassalMapFontMode pMode)
        {
            return pMode == HierarchicalVassalMapFontMode.Bundled
                ? HierarchicalVassalMapFontMode.GameLocalized
                : HierarchicalVassalMapFontMode.Bundled;
        }

        public static int NextIndex(int pCurrentIndex, int pItemCount)
        {
            if (pItemCount <= 0) return -1;
            int normalized = pCurrentIndex < 0 ? -1 : pCurrentIndex;
            return (normalized + 1) % pItemCount;
        }

        public static int ClampIndex(int pIndex, int pItemCount)
        {
            if (pItemCount <= 0) return -1;
            return Math.Max(0, Math.Min(pIndex, pItemCount - 1));
        }
    }
}
