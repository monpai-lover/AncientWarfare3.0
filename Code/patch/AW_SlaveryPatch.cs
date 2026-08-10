using AncientWarfare3.core.lineage;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SlaveryPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        public static void MakeWarrior_Postfix(City __instance, Actor pActor)
        {
            if (pActor?.data == null || !pActor.isWarrior()) return;
            bool initializeTemporaryMilitaryIdentity =
                MilitaryRecruitmentScope.SuppressesPermanentEnlistmentHistory &&
                (SlaveService.IsSlave(pActor) ||
                 SlaveService.IsRetiredSoldier(pActor));
            if (!MilitaryRecruitmentScope.SuppressesPermanentEnlistmentHistory ||
                initializeTemporaryMilitaryIdentity)
                SlaveService.OnMadeWarrior(__instance, pActor);
            RoyalGuardService.StripActorFromNormalArmy(pActor);
            if (__instance != null && __instance.hasArmy())
            {
                SlaveService.RenameArmyIfSlaveArmy(__instance.getArmy());
                FiefMilitaryService.RefreshArmyName(__instance.getArmy());
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "setKingdom", new[] { typeof(Kingdom) })]
        public static void SetKingdom_Prefix(Actor __instance, out Kingdom __state)
        {
            __state = __instance?.kingdom;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setKingdom", new[] { typeof(Kingdom) })]
        public static void SetKingdom_Postfix(Actor __instance, Kingdom __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__instance?.kingdom == __state) return;
            WarNoticeService.QueueArmyChanged(__state, __instance.army);
            ArmyDeploymentService.ReleaseActor(__instance, restoreJob: true);
            TemporarySlaveVanguardService.OnActorKingdomChanged(__instance, __state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        public static void JoinCity_Postfix(Actor __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            SlavePopulationIndexService.OnActorCityChanged(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.stopBeingWarrior))]
        public static void StopBeingWarrior_Postfix(Actor __instance)
        {
            TemporarySlaveVanguardService.OnWarriorStatusLost(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.newKillAction))]
        public static void NewKillAction_Postfix(Actor __instance, Actor pDeadUnit, Kingdom pPrevKingdom, AttackType pAttackType)
        {
            SlaveService.TryPromoteSlaveByMerit(__instance, pDeadUnit);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            SlaveKingAbdicationService.TryForceCurrentSlaveKing(__instance, pActor, "slave_king");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getHit")]
        public static bool GetHit_Prefix(Actor __instance, AttackType pAttackType, BaseSimObject pAttacker)
        {
            return !SlaveService.TryCaptureCombatTarget(__instance, pAttacker, pAttackType);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.updateAge))]
        public static void CityUpdateAge_Postfix(City __instance)
        {
            if (__instance?.data == null) return;
            if (!SlaveService.IsSlaveryEnabled(__instance.kingdom)) return;

            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                SlaveService.ResetSlaveFoodQuota(__instance);
            }
            finally
            {
                UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CitySlaveFoodIndex, benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehTryToEatCityFood), nameof(BehTryToEatCityFood.eatFood))]
        public static bool EatCityFood_Prefix(Actor pActor, City pCity)
        {
            return SlaveService.CanConsumeCityFood(pActor, pCity);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
        public static void JoinAnotherKingdom_Prefix(City __instance, out Kingdom __state)
        {
            __state = __instance?.kingdom;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
        public static void JoinAnotherKingdom_Postfix(City __instance, Kingdom pNewSetKingdom, bool pCaptured, bool pRebellion, Kingdom __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (!pCaptured) return;
            SlaveService.HandleCityCaptured(__instance, __state, pNewSetKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.newArmy))]
        public static bool NewArmy_Prefix(ref Actor pActor, City pCity, ref Army __result)
        {
            RoyalGuardService.PrepareArmyCaptain(ref pActor, pCity);
            if (!RoyalGuardService.ShouldBlockNormalArmy(pActor) && !SlaveService.IsSlave(pActor)) return true;

            __result = null;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Army), nameof(Army.setCaptain))]
        public static bool SetCaptain_Prefix(Army __instance, ref Actor pActor)
        {
            if (!RoyalGuardService.TryReplaceGuardCaptain(__instance, ref pActor))
                return false;
            return SlaveService.TryReplaceSlaveCaptain(__instance, ref pActor);
        }
    }
}
