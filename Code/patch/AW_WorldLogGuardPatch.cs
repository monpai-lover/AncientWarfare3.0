using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WorldLogGuardPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logNewKing))]
        public static bool LogNewKing_Prefix(Kingdom pKingdom)
        {
            return pKingdom?.king != null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WindowMetaGeneric<War, WarData>), "loadNameInput")]
        public static bool WarLoadNameInput_Prefix(object __instance)
        {
            return MetaWindowSafetyRules.ShouldUseNameInput(GetWarNameInput(__instance) != null);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WindowMetaGeneric<War, WarData>), "OnDisable")]
        public static bool WarOnDisable_Prefix(object __instance)
        {
            return MetaWindowSafetyRules.ShouldUseNameInput(GetWarNameInput(__instance) != null);
        }

        private static NameInput GetWarNameInput(object pInstance)
        {
            if (pInstance == null) return null;
            try
            {
                return AccessTools.Field(typeof(WindowMetaGeneric<War, WarData>), "_name_input")
                    ?.GetValue(pInstance) as NameInput;
            }
            catch
            {
                return null;
            }
        }
    }
}
