using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(CancelButton), nameof(CancelButton.setIconFrom))]
    internal static class AW_PowerButtonVisualPatch
    {
        [HarmonyPrefix]
        private static bool SetIconFrom_Prefix(CancelButton __instance,
            PowerButton pButton)
        {
            string powerId = pButton?.godPower?.id;
            if (AWPowerButtonVisualRules.
                ShouldClearCancelIconOverride(powerId))
            {
                if (__instance?.powerIcon != null)
                    __instance.powerIcon.overrideSprite = null;
                return true;
            }
            if (!AWPowerButtonVisualRules.ShouldPatchCancelIcon(powerId))
                return true;
            if (__instance?.powerIcon == null || pButton?.icon == null)
                return true;

            Sprite sprite = AWPowerButtonVisualRules.SelectIcon(
                pButton.icon.sprite, pButton.icon.overrideSprite);
            if (sprite == null) return true;

            __instance.powerIcon.sprite = sprite;
            __instance.powerIcon.overrideSprite = sprite;
            __instance.powerIcon.preserveAspect = true;
            __instance.powerIcon.enabled = true;
            return false;
        }
    }
}
