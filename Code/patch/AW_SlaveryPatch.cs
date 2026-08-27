using AncientWarfare3.core.lineage;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SlaveryPatch
    {
        public readonly struct ActorKingdomArmyState
        {
            public readonly Kingdom Kingdom;
            public readonly Army Army;

            public ActorKingdomArmyState(Actor pActor)
            {
                Kingdom = pActor?.kingdom;
                Army = pActor?.army;
            }
        }

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
        public static void SetKingdom_Prefix(Actor __instance,
            out ActorKingdomArmyState __state)
        {
            __state = new ActorKingdomArmyState(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setKingdom", new[] { typeof(Kingdom) })]
        public static void SetKingdom_Postfix(Actor __instance,
            ActorKingdomArmyState __state)
        {
            if (__instance?.kingdom == __state.Kingdom) return;

            // Vanilla calls setDefaultKingdom from ActorManager.finalizeActor
            // while an actor is still being materialized.  The original
            // kingdom assignment must remain side-effect free at that point;
            // load/army repair has its own post-load queues.
            if (!IsStableRuntime(__instance)) return;

            ArmyMembershipReconciliationService.Enqueue(__state.Army);
            ArmyMembershipReconciliationService.Enqueue(__instance?.army);
            // Do not query profession state synchronously here.  The actor
            // can still be in a vanilla lifecycle boundary; the membership
            // service performs that check from its stable authority queue.
            WarriorArmyMembershipService.Enqueue(__instance);
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            WarNoticeService.QueueArmyChanged(__state.Kingdom,
                __instance.army);
            ArmyDeploymentService.ReleaseActor(__instance, restoreJob: true);
            TemporarySlaveVanguardService.OnActorKingdomChanged(__instance,
                __state.Kingdom);
        }

        private static bool IsStableRuntime(Actor pActor)
        {
            try
            {
                // Actor.setDefaultKingdom is also invoked from
                // ActorManager.finalizeActor.  At that point data/asset can
                // exist while profession and tile state are still being
                // materialized; querying warrior/army state is unsafe.
                return pActor?.data != null && pActor.asset != null &&
                       pActor.profession_asset != null &&
                       pActor.current_tile?.data != null &&
                       Config.game_loaded && !SmoothLoader.isLoading() &&
                       World.world != null;
            }
            catch
            {
                return false;
            }
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
