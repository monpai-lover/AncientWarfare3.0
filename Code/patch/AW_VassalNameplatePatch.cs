using AncientWarfare3.ui.components;
using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_VassalNameplatePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameplateText), nameof(NameplateText.newNameplate))]
        public static void NameplateTextNewNameplate_Postfix(NameplateText __instance)
        {
            VassalNameplateSuzerainFlag.Attach(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NameplateText), nameof(NameplateText.prepare))]
        public static void NameplateTextPrepare_Prefix(NameplateText __instance)
        {
            VassalNameplateSuzerainFlag.Hide(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameplateText), "showTextKingdom")]
        public static void ShowTextKingdom_Postfix(NameplateText __instance, Kingdom pMetaObject, Vector2 pPosition)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                VassalNameplateSuzerainFlag.Apply(__instance, pMetaObject);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.NameplatesIndex, benchmark);
            }
        }
    }
}
