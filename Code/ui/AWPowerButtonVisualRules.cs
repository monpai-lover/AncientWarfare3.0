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

        public static bool ShouldClearOwnedCancelIconOverride<T>(
            string pPowerId, bool pHasValidSourceIcon,
            T pCurrentOverride, T pOwnedOverride,
            bool pOwnsCancelButton)
            where T : class
        {
            return pHasValidSourceIcon && pOwnsCancelButton &&
                   !string.IsNullOrEmpty(pPowerId) &&
                   !ShouldPatchCancelIcon(pPowerId) &&
                   ReferenceEquals(pCurrentOverride, pOwnedOverride);
        }
    }
}
