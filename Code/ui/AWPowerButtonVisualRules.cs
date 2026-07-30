using System;

namespace AncientWarfare3.ui
{
    public static class AWPowerButtonVisualRules
    {
        public static T SelectIcon<T>(T pBaseSprite, T pOverrideSprite)
            where T : class
        {
            return pOverrideSprite ?? pBaseSprite;
        }

        public static bool ShouldPatchCancelIcon(string pPowerId)
        {
            return string.Equals(pPowerId, "xia",
                       StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(pPowerId) &&
                    pPowerId.StartsWith("aw_", StringComparison.Ordinal));
        }
    }
}
