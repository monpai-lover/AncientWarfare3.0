using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(CancelButton), nameof(CancelButton.setIconFrom))]
    internal static class AW_PowerButtonVisualPatch
    {
        private static CancelButton _ownedCancelButton;
        private static Sprite _ownedCancelSprite;

        [HarmonyPrefix]
        private static bool SetIconFrom_Prefix(CancelButton __instance,
            PowerButton pButton)
        {
            string powerId = pButton?.godPower?.id;
            bool hasValidSourceIcon = pButton?.icon != null;
            if (AWPowerButtonVisualRules.
                ShouldClearOwnedCancelIconOverride(
                    powerId, hasValidSourceIcon,
                    __instance?.powerIcon?.overrideSprite,
                    _ownedCancelSprite,
                    ReferenceEquals(__instance, _ownedCancelButton)))
            {
                __instance.powerIcon.overrideSprite = null;
                _ownedCancelButton = null;
                _ownedCancelSprite = null;
                return true;
            }
            if (!AWPowerButtonVisualRules.ShouldPatchCancelIcon(powerId))
                return true;
            if (__instance?.powerIcon == null || !hasValidSourceIcon)
                return true;

            Sprite sprite = AWPowerButtonVisualRules.SelectIcon(
                pButton.icon.sprite, pButton.icon.overrideSprite);
            if (sprite == null) return true;

            __instance.powerIcon.sprite = sprite;
            __instance.powerIcon.overrideSprite = sprite;
            __instance.powerIcon.preserveAspect = true;
            __instance.powerIcon.enabled = true;
            _ownedCancelButton = __instance;
            _ownedCancelSprite = sprite;
            return false;
        }
    }
}
