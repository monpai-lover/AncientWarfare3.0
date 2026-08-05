using AncientWarfare3.core.court;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     贵族晋升:Postfix City.setLeader / Kingdom.setKing。
    ///     - 城主(setLeader)走分流 OnCityLeaderAppointed:无谱系→初次贵族建姓族+氏支;
    ///       已有谱系(父系继承)→多余 male 子嗣分封新氏支(长子/继承人留原氏)。
    ///     - 国王(setKing)是大宗,不分封,直接 OnActorPromoted 赋/刷新贵族身份。
    ///
    ///     读档/重复设置安全:已有谱系幂等(EnsureLineageForNoble 直接 return,只刷新身份)。
    ///     Postfix 注入,不接管原方法。
    /// </summary>
    [HarmonyPatch]
    public static class AW_PromotionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static bool SetLeader_HeirGuard_Prefix(City __instance, Actor pActor, bool pNew, out bool __state)
        {
            __state = false;
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            if (pActor == null ||
                (!LineageService.IsXia(pActor) && !LineageService.IsXiaKingdom(__instance?.kingdom) &&
                 !XiaizationService.IsForeignPseudoDynasty(pActor.kingdom)))
                return true;
            if (!XiaAuthorityGenderRules.ShouldAllowSetLeader(
                    pIsXiaActor: true,
                    pIsMale: pActor.isSexMale(),
                    pIsNewAppointment: pNew))
            {
                __state = true;
                return false;
            }
            if (!pNew) return true;

            Kingdom kingdom = __instance?.kingdom ?? pActor.kingdom;
            if (!HeirService.IsCurrentHeir(kingdom, pActor)) return true;

            __state = true;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static void SetLeader_Postfix(Actor pActor, bool pNew, bool __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (GovernorRotationRuntimeScope.IsActive) return;
            if (__state) return;
            if (pActor == null || !ShouldRunLineagePromotionHook(
                    pActor, pActor.kingdom))
                return;
            LineageService.OnCityLeaderAppointed(pActor, CourtOfficeId.Governor);
            LineageService.EnsureOfficialShiAndClan(pActor, CourtOfficeId.Governor);
            if (pNew) ChronicleEvents.OnBecomeLeader(pActor); // 编年史:仅新任命记(pNew=false 是读档/复位,不重复记)
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            NamingProfileId profile = AWCultureNamingTraditionService
                .ResolveForActorReadOnly(pActor).Profile;
            bool actorIsActualKing = pActor != null &&
                                     __instance?.king == pActor;
            if (actorIsActualKing)
                WesternLineageMigrationService.Request();
            if (!WesternLineageAdmissionRules.ShouldRunKingAdmission(
                    pFromLoad, actorIsActualKing,
                    profile)) return;
            RoyalMedicalCareService.ReconcileTargets(__instance);
            if (pActor == null || !ShouldRunLineagePromotionHook(
                    pActor, __instance))
                return;
            LineageService.OnActorPromoted(pActor, NobleTrigger.King);
        }

        private static bool ShouldRunLineagePromotionHook(Actor pActor,
            Kingdom pKingdom)
        {
            NamingProfileId profile = AWCultureNamingTraditionService
                .ResolveForActorReadOnly(pActor).Profile;
            return WesternLineageAdmissionRules.ShouldRunPromotionHook(profile,
                LineageService.IsXia(pActor),
                LineageService.IsXiaKingdom(pKingdom),
                XiaizationService.IsForeignPseudoDynasty(pKingdom));
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static bool SetKing_MaleOnly_Prefix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            if (pActor == null) return true;
            if (RoyalAsylumService.IsActive(pActor) &&
                !RoyalAsylumService.RecallForSuccession(pActor, __instance))
            {
                AccessionIdentityService.DeferInstalledKing(__instance,
                    pActor);
                return true;
            }
            return XiaAuthorityGenderRules.ShouldAllowSetKing(
                pFromLoad,
                pCandidateIsMale: pActor.isSexMale(),
                pCandidateIsXia: LineageService.IsXia(pActor),
                pKingdomIsXia: LineageService.IsXiaKingdom(__instance));
        }
    }

    [HarmonyPatch]
    internal static class AW_CityLeaderCareerPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static void SetLeader_Prefix(City __instance, Actor pActor,
            out Actor __state)
        {
            __state = __instance?.leader;
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (GovernorRotationRuntimeScope.IsActive) return;
            if (pActor?.data != null &&
                CourtService.HasOfficialCourt(__instance?.kingdom))
                OfficialCareerStateService.FreezeNativeCityFast(pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static void SetLeader_Postfix(City __instance, Actor pActor, bool pNew, Actor __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (GovernorRotationRuntimeScope.IsActive) return;
            if (__state?.data != null && __state != __instance?.leader)
                EndLeaderCareer(__state, "replaced");
            CityGovernorProjectionRepairService.OnLeaderAssigned(
                __instance, pActor, pNew);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.removeLeader))]
        public static void RemoveLeader_Prefix(City __instance, out Actor __state)
        {
            __state = __instance?.leader;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.removeLeader))]
        public static void RemoveLeader_Postfix(Actor __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (GovernorRotationRuntimeScope.IsActive) return;
            if (__state?.data != null) EndLeaderCareer(__state, "removed");
        }

        private static void EndLeaderCareer(Actor pActor, string pReason)
        {
            CourtService.ClearCityGovernor(pActor, pReason);
        }
    }
}
