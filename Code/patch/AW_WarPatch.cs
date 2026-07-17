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
            CentralizationBorderDeploymentService.OnWarStarted(__result);
            MandateService.OnWarStarted(__result);
            RoyalAsylumService.OnWarStarted(__result);
            MilitaryEmergencyService.OnWarStarted(__result);
            TemporaryLevyService.OnWarStarted(__result, WarNoticeService.FindSignatureForWar(__result));
            TemporarySlaveVanguardService.OnWarStarted(__result);
            WarNoticeService.OnWarStarted(__result);

            Kingdom atk = __result.getMainAttacker();
            Kingdom def = __result.getMainDefender();
            string warTypeName = GetWarTypeName(__result);
            if (atk?.data != null)
                ChronicleEvents.OnWarStart(atk, def, def?.name ?? "未知", warTypeName);
            if (def?.data != null)
                ChronicleEvents.OnWarStart(def, atk, atk?.name ?? "未知", warTypeName);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.joinAttackers))]
        public static void JoinAttackers_Prefix(War __instance, Kingdom pKingdom, out bool __state)
        {
            __state = __instance?.data != null && pKingdom?.data != null &&
                      !__instance.isAttacker(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.joinAttackers))]
        public static void JoinAttackers_Postfix(War __instance, Kingdom pKingdom, bool __state)
        {
            if (!__state || __instance?.data == null || pKingdom?.data == null ||
                !__instance.isAttacker(pKingdom)) return;
            OnKingdomJoinedWar(__instance, pKingdom, pDefender: false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.joinDefenders))]
        public static void JoinDefenders_Prefix(War __instance, Kingdom pKingdom, out bool __state)
        {
            __state = __instance?.data != null && pKingdom?.data != null &&
                      !__instance.isDefender(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.joinDefenders))]
        public static void JoinDefenders_Postfix(War __instance, Kingdom pKingdom, bool __state)
        {
            if (!__state || __instance?.data == null || pKingdom?.data == null ||
                !__instance.isDefender(pKingdom)) return;
            OnKingdomJoinedWar(__instance, pKingdom, pDefender: true);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.removeAttacker))]
        public static void RemoveAttacker_Prefix(War __instance, Kingdom pKingdom, out bool __state)
        {
            __state = __instance?.data != null && pKingdom?.data != null &&
                      __instance.isAttacker(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.removeAttacker))]
        public static void RemoveAttacker_Postfix(War __instance, Kingdom pKingdom, bool __state)
        {
            if (!__state || __instance?.data == null || pKingdom?.data == null ||
                __instance.isAttacker(pKingdom)) return;
            OnKingdomLeftWar(__instance, pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.removeDefender))]
        public static void RemoveDefender_Prefix(War __instance, Kingdom pKingdom, out bool __state)
        {
            __state = __instance?.data != null && pKingdom?.data != null &&
                      __instance.isDefender(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.removeDefender))]
        public static void RemoveDefender_Postfix(War __instance, Kingdom pKingdom, bool __state)
        {
            if (!__state || __instance?.data == null || pKingdom?.data == null ||
                __instance.isDefender(pKingdom)) return;
            OnKingdomLeftWar(__instance, pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
        public static void EndWar_Postfix(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            CityOccupationAccelerationService.OnWarEnded(pWar);
            RoyalAsylumService.OnWarEnded(pWar);
            MilitaryEmergencyService.OnWarEnded(pWar);
            TemporaryLevyService.OnWarEnded(pWar);
            TemporarySlaveVanguardService.OnWarEnded(pWar);
            WarRecordWriter.OnWarEnd(pWar, pWinner);
            WarTerritoryService.OnWarEnded(pWar, pWinner);
            AutonomousRestorationService.OnWarEnded(pWar);
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

        private static void OnKingdomJoinedWar(War pWar, Kingdom pKingdom, bool pDefender)
        {
            MilitaryEmergencyService.OnKingdomJoinedWar(pWar, pKingdom, pDefender);
            TemporaryLevyService.OnEmergencyChanged(pKingdom);
            TemporarySlaveVanguardService.OnEmergencyChanged(pKingdom);
        }

        private static void OnKingdomLeftWar(War pWar, Kingdom pKingdom)
        {
            MilitaryEmergencyService.OnKingdomLeftWar(pWar, pKingdom);
            TemporaryLevyService.OnEmergencyChanged(pKingdom);
            TemporarySlaveVanguardService.OnEmergencyChanged(pKingdom);
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
