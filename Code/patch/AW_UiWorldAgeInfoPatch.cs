using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    internal static class AW_UiWorldAgeInfoPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UiWorldAgeInfo), "Awake")]
        private static void AttachWorldTimeTooltip(UiWorldAgeInfo __instance)
        {
            AWWorldAgeClockTooltipAdapter.Attach(__instance);
        }

        public static void SpecialPatch()
        {
            UiWorldAgeInfo worldAgeInfo = Object
                .FindFirstObjectByType<UiWorldAgeInfo>(FindObjectsInactive.Include);
            if (worldAgeInfo != null)
                AWWorldAgeClockTooltipAdapter.Attach(worldAgeInfo);
        }
    }
}
