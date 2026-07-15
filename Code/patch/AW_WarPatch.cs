using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     战争编年史:
    ///     Postfix WarManager.newWar  → WarRecordWriter.OnWarStart + 双国写 war_start 国家史。
    ///     Postfix WarManager.endWar  → WarRecordWriter.OnWarEnd   + 双国写 war_end   国家史。
    ///     两方法均在 WarManager 自身声明,typeof 正确。
    /// </summary>
    [HarmonyPatch]
    public static class AW_WarPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DiplomacyManager), "startWar",
            new[] { typeof(Kingdom), typeof(Kingdom), typeof(WarTypeAsset), typeof(bool) })]
        public static bool DiplomacyStartWar_Prefix(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset,
            ref War __result)
        {
            if (!WarDecisionService.ShouldBlockWarStart(pAttacker, pDefender, pAsset)) return true;
            __result = null;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.newWar))]
        public static bool NewWar_Prefix(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pType, ref War __result)
        {
            if (!WarDecisionService.ShouldBlockWarStart(pAttacker, pDefender, pType)) return true;
            __result = null;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.newWar))]
        public static void NewWar_Postfix(War __result)
        {
            if (__result?.data == null) return;
            WarRecordWriter.OnWarStart(__result);
            VassalService.OnWarStarted(__result);
            MandateService.OnWarStarted(__result);
            RoyalAsylumService.OnWarStarted(__result);

            Kingdom atk = __result.getMainAttacker();
            Kingdom def = __result.getMainDefender();
            string warTypeName = GetWarTypeName(__result);
            if (atk?.data != null)
                ChronicleEvents.OnWarStart(atk, def, def?.name ?? "未知", warTypeName);
            if (def?.data != null)
                ChronicleEvents.OnWarStart(def, atk, atk?.name ?? "未知", warTypeName);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
        public static void EndWar_Postfix(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            CityOccupationAccelerationService.OnWarEnded(pWar);
            RoyalAsylumService.OnWarEnded(pWar);
            WarRecordWriter.OnWarEnd(pWar, pWinner);
            WarTerritoryService.OnWarEnded(pWar, pWinner);
            ApplyDiplomacyWarResult(pWar, pWinner);
            MandateService.OnWarEnded(pWar, pWinner);
            MandateRebelService.OnWarEnded(pWar, pWinner);
            GeneralService.OnWarEnded(pWar, pWinner);

            Kingdom atk = pWar.getMainAttacker();
            Kingdom def = pWar.getMainDefender();
            var result = WarRecordWriter.WinnerLabelRich(pWinner, atk, def);
            if (atk?.data != null)
                ChronicleEvents.OnWarEnd(atk, def, def?.name ?? "未知", result);
            if (def?.data != null)
                ChronicleEvents.OnWarEnd(def, atk, atk?.name ?? "未知", result);
        }

        private static void ApplyDiplomacyWarResult(War pWar, WarWinner pWinner)
        {
            VassalService.OnWarEnded(pWar, pWinner);
        }

        private static string GetWarTypeName(War pWar)
        {
            try { return pWar.getAsset()?.id ?? ""; } catch { return ""; }
        }
    }
}
