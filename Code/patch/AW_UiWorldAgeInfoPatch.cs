using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    // 类级 [HarmonyPatch] 必需:PatchClassProcessor 在类上没有这个特性时
    // 直接返回,方法级特性一个都不会被处理(且不报错)。
    [HarmonyPatch]
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
