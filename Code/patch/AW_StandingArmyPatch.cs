using AncientWarfare3.core.lineage;
using AncientWarfare3.api.multiplayer;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_StandingArmyPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.newArmy))]
        private static bool NewArmy_Prefix(Actor pActor, City pCity,
            ref Army __result)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            if (ArmyLifecycleRules.ShouldBlockOrdinaryArmyCreation(
                    WartimeGarrisonService.IsActive(pActor)))
            {
                __result = null;
                return false;
            }
            if (pCity?.data == null)
            {
                __result = null;
                return false;
            }
            if (ArmyFieldIndexService.IsFieldCreationExempt(pActor, pCity))
                return true;
            if (ArmyFieldIndexService.Count(pCity.kingdom) <
                ArmyEstablishmentRules.MaximumFieldArmies) return true;
            if (ArmyFieldIndexService.TryRouteCappedCandidate(
                    pActor, pCity, out Army existing))
            {
                __result = existing;
                return false;
            }
            __result = null;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.newArmy))]
        private static void NewArmy_Postfix(Army __result)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            ArmyStrategicIndexService.OnArmyRegistered(__result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        private static bool SetArmy_Prefix(Actor __instance, Army pObject,
            out Army __state)
        {
            __state = __instance?.army;
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            return !WartimeGarrisonService.ShouldBlockArmyAssignment(
                __instance, pObject);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        private static void SetArmy_Postfix(Actor __instance, Army pObject,
            Army __state, bool __runOriginal)
        {
            if (!__runOriginal) return;
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__state == __instance?.army) return;
            ArmyRtsControllerService.OnArmyRosterChanged(__state);
            ArmyRtsControllerService.OnArmyRosterChanged(__instance?.army);
            KingdomMilitaryReadinessService.MarkArmyCitiesDirty(
                __instance, __state, pObject);
            QueueDeploymentRefresh(__instance, __state,
                pRosterExpanded: false);
            QueueDeploymentRefresh(__instance, __instance?.army,
                pRosterExpanded: true);
            ArmyStrategicIndexService.OnArmyRosterChanged(__state);
            ArmyStrategicIndexService.OnArmyRosterChanged(
                __instance?.army);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.destroyCity))]
        private static void DestroyCity_Prefix(City __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            WartimeGarrisonService.OnCityInvalidated(__instance);
            KingdomMilitaryReadinessService.OnCityDestroyed(__instance);
            ArmyRetreatService.OnCityDestroyed(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "tryToMakeWarrior")]
        private static bool TryToMakeWarrior_Prefix()
        {
            return MilitaryRecruitmentScope.AllowsVanillaTryToMakeWarrior;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.checkCanMakeWarrior))]
        private static bool CheckCanMakeWarrior_Prefix(City __instance, Actor pActor, ref bool __result)
        {
            if (!MilitaryRecruitmentScope.BypassesWarriorCapacity) return true;
            __result = PassesOriginalEligibilityWithoutCapacity(__instance, pActor);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.checkIfWarriorStillOk))]
        private static bool CheckIfWarriorStillOk_Prefix(City __instance, Actor pActor, ref bool __result)
        {
            if (TemporaryLevyService.IsTemporaryLevy(pActor) ||
                WartimeGarrisonService.IsActive(pActor) ||
                TemporarySlaveVanguardService.IsMember(pActor) ||
                StandingArmyService.ShouldKeepWithinOriginalArmyLimit(__instance, pActor))
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.isOkToSendArmy))]
        private static void IsOkToSendArmy_Postfix(City __instance,
            ref bool __result)
        {
            if (__result || __instance?.data == null ||
                __instance.kingdom?.data == null || !__instance.hasArmy())
                return;
            Army army = __instance.getArmy();
            if (army?.data == null || AWArmyService.IsSpecialArmy(army)) return;
            int armyCount;
            float warriorSlots;
            try
            {
                armyCount = army.countUnits();
                warriorSlots = __instance.getMaxWarriors();
            }
            catch { return; }
            int standingCore = StandingArmyRules.PeacetimeCore(
                (int)System.Math.Ceiling(warriorSlots));
            __result = TemporaryLevyRules.CanLaunchEmergencyArmy(
                vanillaReady: false,
                militaryEmergency: MilitaryEmergencyService.HasAny(
                    __instance.kingdom),
                armyCount: armyCount,
                warriorSlots: warriorSlots,
                standingCoreCount: standingCore);
        }

        private static bool PassesOriginalEligibilityWithoutCapacity(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null || pActor.isBaby()) return false;
            if (!pCity.hasCulture()) return true;
            if (pActor.isSexFemale() && pCity.culture.hasTrait("conscription_male_only")) return false;
            if (pActor.isSexMale() && pCity.culture.hasTrait("conscription_female_only")) return false;
            return true;
        }

        private static void QueueDeploymentRefresh(Actor pActor, Army pArmy,
            bool pRosterExpanded)
        {
            if (pArmy?.data == null) return;
            Kingdom kingdom = null;
            try { kingdom = pArmy.getKingdom(); }
            catch { kingdom = pActor?.kingdom; }
            WarNoticeService.QueueArmyChanged(kingdom, pArmy,
                pRosterExpanded);
        }
    }
}
