using System;
using System.Reflection;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ArmySafetyPatch
    {
        private static readonly FieldInfo ArmyKingdomField = AccessTools.Field(typeof(Army), "_kingdom");
        private static readonly FieldInfo ArmyCityField =
            AccessTools.Field(typeof(Army), "_city");
        private static readonly FieldInfo ArmyCaptainField =
            AccessTools.Field(typeof(Army), "_captain");
        private static readonly FieldInfo CityArmyField =
            AccessTools.Field(typeof(City), "army");

        private readonly struct CaptainAssignmentDiagnosticState
        {
            public CaptainAssignmentDiagnosticState(long pArmyId,
                long pPreviousCaptainId, long pRequestedCaptainId,
                bool pPreviousAlive, bool pPreviousIsMember,
                bool pDisposalScope, bool pReplicaApply,
                bool pPreviousIsAuthority, bool pMutationAllowed)
            {
                ArmyId = pArmyId;
                PreviousCaptainId = pPreviousCaptainId;
                RequestedCaptainId = pRequestedCaptainId;
                PreviousAlive = pPreviousAlive;
                PreviousIsMember = pPreviousIsMember;
                DisposalScope = pDisposalScope;
                ReplicaApply = pReplicaApply;
                PreviousIsAuthority = pPreviousIsAuthority;
                MutationAllowed = pMutationAllowed;
            }

            public long ArmyId { get; }
            public long PreviousCaptainId { get; }
            public long RequestedCaptainId { get; }
            public bool PreviousAlive { get; }
            public bool PreviousIsMember { get; }
            public bool DisposalScope { get; }
            public bool ReplicaApply { get; }
            public bool PreviousIsAuthority { get; }
            public bool MutationAllowed { get; }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.newArmy))]
        private static bool ArmyManagerNewArmyCaptainLease_Prefix(
            Actor pActor, ref Army __result)
        {
            if (AW3MultiplayerReplicaScope.IsApplying ||
                !HasLiveCaptainLeaseOutside(pActor,
                    pRequestedArmy: null)) return true;
            __result = null;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.newArmy))]
        private static void ArmyManagerNewArmyNativeName_Postfix(
            Actor pActor, City pCity, Army __result)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            AWArmyService.EnsureOrdinaryNativeName(__result,
                pActor?.kingdom ?? pCity?.kingdom, pCity);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Army), nameof(Army.setCaptain))]
        private static bool ArmySetCaptainLease_Prefix(Army __instance,
            Actor pActor, out CaptainAssignmentDiagnosticState __state)
        {
            bool liveArmy = IsLiveArmy(__instance);
            Actor current = RawCaptain(__instance);
            bool disposalActive = ArmyCaptainDisposalScope.IsActive(
                __instance);
            if (ArmyCaptainContinuityRules.ShouldReleaseForeignCaptainLease(
                    current?.data != null, IsLiveCaptainActor(current),
                    CaptainMatchesArmyKingdom(__instance, current),
                    disposalActive))
            {
                ReleaseForeignCaptainLease(__instance, current);
                current = RawCaptain(__instance);
            }
            bool currentExists = current?.data != null;
            bool currentAlive = IsLiveCaptainActor(current);
            bool currentAuthority = IsCivilAuthority(current);
            bool disposalScope = ArmyCaptainDisposalScope.IsActive(
                __instance);
            bool replicaApply = AW3MultiplayerReplicaScope.IsApplying;
            bool replicaSession = AW3MultiplayerReplicaScope.
                IsReplicaSession;
            bool requestedAuthority = pActor != null &&
                                      IsCivilAuthority(pActor);
            bool royalGuardHandoff =
                RoyalGuardCaptainHandoffScope.IsActive(__instance);
            bool reject = ArmyCaptainContinuityRules.
                ShouldRejectCaptainMutation(
                    ArmyRtsRuntimeMode.Current,
                    replicaSession || replicaApply || disposalScope,
                    liveArmy,
                    currentExists,
                    currentAlive,
                    IsArmyMember(__instance, current),
                    ReferenceEquals(current, pActor),
                    currentCaptainIsCivilAuthority: currentAuthority,
                    royalGuardRoleOwnsCaptain: royalGuardHandoff);
            bool mutationAllowed = !requestedAuthority && !reject;
            __state = new CaptainAssignmentDiagnosticState(
                __instance?.id ?? -1L, ActorId(current), ActorId(pActor),
                currentAlive, IsArmyMember(__instance, current),
                disposalScope, replicaApply,
                currentAuthority, mutationAllowed);
            return mutationAllowed;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Army), nameof(Army.setCaptain))]
        private static bool ArmySetCaptainRequestedLease_Prefix(
            Army __instance, Actor pActor)
        {
            return !HasLiveCaptainLeaseOutside(pActor, __instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Army), nameof(Army.setCaptain))]
        private static void ArmySetCaptainDiagnostic_Postfix(Army __instance,
            CaptainAssignmentDiagnosticState __state)
        {
            long currentCaptainId = ActorId(RawCaptain(__instance));
            if (currentCaptainId == __state.PreviousCaptainId) return;
            ArmyRtsControllerService.OnCaptainChanged(__instance);
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            if (ArmyRtsRuntimeMode.Current != ArmyRtsMode.On) return;
            ModClass.LogWarning("[Army captain change] army=" +
                __state.ArmyId + " previous=" +
                __state.PreviousCaptainId + " requested=" +
                __state.RequestedCaptainId + " current=" +
                currentCaptainId + " previous_alive=" +
                __state.PreviousAlive + " previous_is_member=" +
                __state.PreviousIsMember + " disposal_scope=" +
                __state.DisposalScope + " replica_apply=" +
                __state.ReplicaApply + " previous_is_authority=" +
                __state.PreviousIsAuthority + " mutation_allowed=" +
                __state.MutationAllowed);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Army), nameof(Army.checkCaptainExistence))]
        private static bool ArmyCheckCaptainExistence_Prefix(
            Army __instance)
        {
            bool liveArmy = false;
            try
            {
                liveArmy = __instance?.data != null &&
                           __instance.isAlive();
            }
            catch { }
            bool replica = AW3MultiplayerReplicaScope.IsReplicaSession ||
                           AW3MultiplayerReplicaScope.IsApplying ||
                           ArmyCaptainDisposalScope.IsActive(__instance);
            if (!ArmyCaptainContinuityRules.ShouldOwnMaintenance(
                    ArmyRtsRuntimeMode.Current, replica, liveArmy))
                return true;

            Actor current = RawCaptain(__instance);
            bool disposalActive = ArmyCaptainDisposalScope.IsActive(
                __instance);
            if (ArmyCaptainContinuityRules.ShouldReleaseForeignCaptainLease(
                    current?.data != null, IsLiveCaptainActor(current),
                    CaptainMatchesArmyKingdom(__instance, current),
                    disposalActive))
            {
                ReleaseForeignCaptainLease(__instance, current);
                current = RawCaptain(__instance);
            }
            bool currentExists = current?.data != null;
            bool currentAlive = IsLiveCaptainActor(current);
            bool currentAuthority = IsCivilAuthority(current);
            bool currentIsMember = IsArmyMember(__instance, current);
            if (ArmyCaptainContinuityRules.
                    ShouldRepairCaptainMembership(
                        currentExists, currentAlive, currentIsMember,
                        disposalActive))
            {
                AWArmyService.AddToArmy(current, __instance);
                currentIsMember = IsArmyMember(__instance, current);
            }
            bool currentLeaseEligible = currentExists &&
                IsEligibleCaptain(__instance, current);
            if (currentExists && currentAlive && currentIsMember &&
                !currentLeaseEligible)
            {
                ReleaseIneligibleCaptainLease(__instance, current);
                current = RawCaptain(__instance);
                currentExists = current?.data != null;
                currentAlive = IsLiveCaptainActor(current);
                currentAuthority = IsCivilAuthority(current);
                currentIsMember = IsArmyMember(__instance, current);
                currentLeaseEligible = currentExists &&
                    IsEligibleCaptain(__instance, current);
            }
            if (ArmyCaptainContinuityRules.ShouldRetainCaptain(
                    ArmyCaptainContinuityRules.IsCurrentCaptainStable(
                        currentExists,
                        currentAlive,
                        currentIsMember,
                        captainIsCivilAuthority: currentAuthority),
                    currentLeaseEligible))
            {
                if (__instance.data.id_captain != current.data.id)
                    __instance.data.id_captain = current.data.id;
                return false;
            }
            if (ArmyCaptainContinuityRules.ShouldRetainCaptain(
                    ArmyCaptainContinuityRules.ShouldPreserveAssignedCaptain(
                        currentExists, currentAlive, currentIsMember),
                    currentLeaseEligible))
            {
                if (__instance.data.id_captain != current.data.id)
                    __instance.data.id_captain = current.data.id;
                return false;
            }

            if (TrySelectStableCaptain(__instance, out Actor replacement))
                AWArmyService.SetCaptainIfChanged(__instance, replacement);
            else if (!TemporaryLevyService.TryPromoteExistingLevyCaptain(__instance) && currentExists)
                __instance.setCaptain(null);
            DetachCivilAuthorityCaptain(__instance, current);
            return false;
        }

        private static void ReleaseIneligibleCaptainLease(Army pArmy,
            Actor pActor)
        {
            if (pArmy?.data == null || pActor?.data == null) return;
            using (ArmyCaptainDisposalScope.Open(pArmy))
            {
                try { pArmy.setCaptain(null); }
                catch { }
            }
            RoyalGuardService.StripActorFromNormalArmy(pActor);
            ArmyRtsControllerService.ReleaseActor(pActor);
            ArmyDeploymentService.ReleaseActor(pActor, restoreJob: true);
            ArmyStrategicIndexService.OnArmyRosterChanged(pArmy);
        }

        private static bool TrySelectStableCaptain(Army pArmy,
            out Actor pCaptain)
        {
            pCaptain = null;
            long bestActorId = -1L;
            try
            {
                foreach (Actor candidate in pArmy.getUnits())
                {
                    if (!IsEligibleCaptain(pArmy, candidate)) continue;
                    long candidateId = ActorId(candidate);
                    if (!ArmyCaptainContinuityRules.ShouldPreferReplacement(
                            bestActorId, candidateId)) continue;
                    bestActorId = candidateId;
                    pCaptain = candidate;
                }
            }
            catch
            {
                pCaptain = null;
            }
            return pCaptain != null;
        }

        private static bool IsEligibleCaptain(Army pArmy, Actor pActor)
        {
            return CaptainMatchesArmyKingdom(pArmy, pActor) &&
                   AWArmyService.IsCaptainLeaseEligible(pArmy, pActor,
                requireMembership: true);
        }

        private static bool CaptainMatchesArmyKingdom(Army pArmy,
            Actor pActor)
        {
            return AWArmyService.CaptainMatchesArmyKingdom(pArmy, pActor);
        }

        private static void ReleaseForeignCaptainLease(Army pArmy,
            Actor pActor)
        {
            if (pArmy?.data == null || pActor?.data == null) return;
            using (ArmyCaptainDisposalScope.Open(pArmy))
            {
                try
                {
                    if (pActor.army == pArmy) pActor.removeFromArmy();
                }
                catch
                {
                    try { pActor.setArmy(null); }
                    catch { }
                }
                try { pArmy.setCaptain(null); }
                catch { }
            }
            ArmyRtsControllerService.ReleaseActor(pActor);
            ArmyDeploymentService.ReleaseActor(pActor, restoreJob: true);
            TemporaryLevyService.OnActorInvalidated(pActor);
            WartimeGarrisonService.OnActorInvalidated(pActor);
            TemporarySlaveVanguardService.OnMemberInvalidated(pActor);
            MandateMilitaryPhaseService.Clear(pActor);
            ArmyStrategicIndexService.OnArmyRosterChanged(pArmy);
        }

        private static bool IsLiveCaptainActor(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        public static bool ActorSetArmy_Prefix(Actor __instance,
            Army pObject, out Army __state)
        {
            __state = __instance?.army;
            bool king = false;
            bool leader = false;
            try
            {
                king = __instance?.isProfession(UnitProfession.King) == true ||
                       __instance?.isKing() == true;
                leader = __instance?.isProfession(UnitProfession.Leader) == true ||
                         __instance?.isCityLeader() == true;
            }
            catch { }
            if (!ArmyLifecycleRules.CanAssignArmyToAuthorityRole(
                    pObject != null, king, leader)) return false;
            if (ShouldRejectCaptainDetachment(__instance, __state,
                    pObject))
                return false;
            try
            {
                if (pObject?.data != null &&
                    ReferenceEquals(pObject.getCaptain(), __instance))
                    return true;
            }
            catch { }
            return ArmyLifecycleRules.CanAssignArmyToAuthorityRole(
                pObject != null, king, leader);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "removeFromArmy")]
        public static bool ActorRemoveFromArmy_Prefix(Actor __instance)
        {
            Army current = __instance?.army;
            return !ShouldRejectCaptainDetachment(__instance, current,
                pNextArmy: null);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Army), nameof(Army.checkCaptainRemoval))]
        public static bool ArmyCheckCaptainRemoval_Prefix(
            Army __instance, Actor pActor)
        {
            if (!ReferenceEquals(RawCaptain(__instance), pActor))
                return true;
            bool currentExists = pActor?.data != null;
            bool currentAlive = IsLiveCaptainActor(pActor);
            bool royalGuardHandoff =
                RoyalGuardCaptainHandoffScope.IsActive(__instance);
            return !ArmyCaptainContinuityRules.
                ShouldRejectCaptainMutation(
                    ArmyRtsRuntimeMode.Current,
                    AW3MultiplayerReplicaScope.IsReplicaSession ||
                    AW3MultiplayerReplicaScope.IsApplying ||
                    ArmyCaptainDisposalScope.IsActive(__instance),
                    IsLiveArmy(__instance),
                    currentExists,
                    currentAlive,
                    IsArmyMember(__instance, pActor),
                    requestedSameCaptain: false,
                    currentCaptainIsCivilAuthority:
                        IsCivilAuthority(pActor),
                    royalGuardRoleOwnsCaptain: royalGuardHandoff);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        public static void ActorSetArmy_Postfix(Actor __instance,
            Army __state, bool __runOriginal)
        {
            if (!__runOriginal) return;
            if (__state == __instance?.army) return;
            ArmyFormationService.OnActorArmyChanged(__instance, __state,
                __instance?.army);
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__state == null || __state == __instance?.army) return;
            ArmyInvalidCleanupQueue.ScheduleIfEmpty(__state,
                SafeCity(__state), SafeKingdom(__state, null, null));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die", new[] { typeof(bool),
            typeof(AttackType), typeof(bool), typeof(bool) })]
        public static void ActorDieFormation_Prefix(Actor __instance)
        {
            ArmyFormationService.OnActorDying(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.checkArmyExistence))]
        public static void CityCheckArmyExistence_Prefix(City __instance)
        {
            Army army = null;
            try { army = __instance?.getArmy(); }
            catch { }
            if (!ArmyLifecycleRules.ShouldDetachInvalidCityArmy(
                    army != null, army?.data != null)) return;
            try { CityArmyField?.SetValue(__instance, null); }
            catch { }
            ArmyInvalidCleanupQueue.Schedule(army);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehCheckArmy), nameof(CityBehCheckArmy.execute))]
        public static void CityCheckArmy_Prefix(City pCity,
            out Army __state)
        {
            __state = null;
            try
            {
                if (pCity?.hasArmy() == true) __state = pCity.getArmy();
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityBehCheckArmy), nameof(CityBehCheckArmy.execute))]
        public static void CityCheckArmy_Postfix(City pCity,
            Army __state)
        {
            if (__state == null) return;
            Army current = null;
            try
            {
                if (pCity?.hasArmy() == true) current = pCity.getArmy();
            }
            catch { }
            if (current == __state)
            {
                int count = 0;
                try { count = __state.countUnits(); }
                catch { }
                if (count > 0) return;
            }
            ArmyInvalidCleanupQueue.ScheduleIfEmpty(__state, pCity,
                pCity?.kingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehCityActorCheckAttack), nameof(BehCityActorCheckAttack.execute))]
        public static bool CityActorCheckAttack_Prefix(Actor pActor, ref BehResult __result)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            int benchmarkIndex =
                RecentFeatureBenchmarkRules.ArmyAttackMissingStateIndex;
            try
            {
                if (IsCivilAuthority(pActor))
                {
                    __result = BehResult.Stop;
                    return false;
                }
                bool skip = ArmyAiSafetyRules.ShouldSkipCityAttackAction(
                    pHasActor: pActor?.data != null && !pActor.isRekt(),
                    pHasCity: pActor?.city?.data != null,
                    pHasAttackZone: pActor?.city?.target_attack_zone != null,
                    pHasArmy: pActor?.army?.data != null,
                    pHasCurrentTile: pActor?.current_tile != null,
                    pHasCurrentZone: pActor?.current_tile?.zone != null);

                if (skip)
                {
                    __result = BehResult.Stop;
                    return false;
                }

                bool actorIsCaptain = IsArmyCaptain(pActor);
                if (ArmyRetreatRules.ShouldEvaluateLegacyRetreat(
                        ArmyRtsRuntimeMode.Current, actorIsCaptain) &&
                    ArmyRetreatService.ShouldStopAttack(pActor))
                {
                    benchmarkIndex = RecentFeatureBenchmarkRules
                        .ArmyAttackRetreatHoldIndex;
                    __result = BehResult.Stop;
                    return false;
                }

                if (TemporarySlaveVanguardService.ShouldDelayBehindVanguard(
                        pActor))
                {
                    benchmarkIndex = RecentFeatureBenchmarkRules
                        .ArmyAttackVanguardHoldIndex;
                    __result = BehResult.Stop;
                    return false;
                }

                TileZone attackZone = pActor.city.target_attack_zone;
                WorldTile attackCenter = attackZone?.centerTile;
                bool sameIsland = true;
                try
                {
                    sameIsland = attackCenter == null ||
                                 attackCenter.isSameIsland(
                                     pActor.current_tile);
                }
                catch { sameIsland = true; }
                if (ArmyAiSafetyRules.ShouldRouteCrossIslandAttackThroughAw3(
                        attackCenter?.data != null, sameIsland,
                        PathfindingOwnershipService.ShouldIntercept))
                {
                    benchmarkIndex = RecentFeatureBenchmarkRules
                        .ArmyAttackCrossIslandIndex;
                    WorldTile target = StableAttackTarget(attackZone,
                        pActor.data.id);
                    if (target?.data == null)
                    {
                        __result = BehResult.Stop;
                        return false;
                    }
                    pActor.beh_tile_target = target;
                    __result = BehResult.Continue;
                    return false;
                }

                if (pActor.current_tile.zone == null)
                {
                    if (TryRecoverMissingCurrentZone(pActor,
                            out WorldTile recoveryTarget))
                    {
                        pActor.beh_tile_target = recoveryTarget;
                        __result = BehResult.Continue;
                        return false;
                    }
                    __result = BehResult.Stop;
                    return false;
                }

                benchmarkIndex =
                    RecentFeatureBenchmarkRules.ArmyAttackReadyIndex;
                return true;
            }
            finally
            {
                RecentFeatureBenchmark.End(benchmarkIndex, benchmark);
            }
        }

        private static bool TryRecoverMissingCurrentZone(Actor pActor,
            out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.current_tile == null ||
                pActor.current_tile.zone != null) return false;
            TileZone attackZone = pActor.city?.target_attack_zone;
            WorldTile center = attackZone?.centerTile;
            if (center == null) return false;
            try
            {
                if (!center.isSameIsland(pActor.current_tile)) return false;
                pTarget = attackZone.tiles.GetRandom() ?? center;
                return pTarget != null;
            }
            catch
            {
                pTarget = null;
                return false;
            }
        }

        private static WorldTile StableAttackTarget(TileZone pZone,
            long pActorId)
        {
            if (pZone == null) return null;
            try
            {
                int count = pZone.tiles?.Length ?? 0;
                if (count > 0)
                {
                    int start = (int)(unchecked((ulong)pActorId) %
                                      (ulong)count);
                    int checks = System.Math.Min(16, count);
                    for (int offset = 0; offset < checks; offset++)
                    {
                        int index = (start + offset) % count;
                        WorldTile tile = pZone.tiles[index];
                        if (IsStableAttackTile(tile)) return tile;
                    }
                }
            }
            catch { }
            return IsStableAttackTile(pZone.centerTile)
                ? pZone.centerTile
                : null;
        }

        private static bool IsStableAttackTile(WorldTile pTile)
        {
            return pTile?.data != null && pTile.Type != null &&
                   pTile.Type.ground && !pTile.Type.liquid &&
                   !pTile.Type.ocean && !pTile.Type.lava &&
                   !pTile.Type.block;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehCheckAttackZone), nameof(CityBehCheckAttackZone.execute))]
        public static bool CityCheckAttackZone_Prefix(City pCity,
            ref BehResult __result)
        {
            if (ArmyRtsRuntimeModeRules.ShouldUseLegacyStrategicWrites(
                    ArmyRtsRuntimeMode.Current)) return true;
            if (pCity != null)
            {
                pCity.target_attack_city = null;
                pCity.target_attack_zone = null;
            }
            __result = BehResult.Continue;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DecisionHelper),
            nameof(DecisionHelper.makeDecisionFor))]
        public static bool MakeDecisionFor_Prefix(Actor pActor,
            ref string pLastDecisionID, ref bool __result)
        {
            bool rtsOwnsActor =
                ArmyRtsControllerService.OwnsLiveActor(pActor);
            if (ArmyRtsRuntimeModeRules.
                    ShouldAllowVanillaDecisionEvaluation(
                        ArmyRtsRuntimeMode.Current, rtsOwnsActor))
                return true;
            pLastDecisionID = string.Empty;
            __result = false;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityBehCheckAttackZone), nameof(CityBehCheckAttackZone.execute))]
        public static void CityCheckAttackZone_Postfix(City pCity)
        {
            CityAttackZoneService.RepairAfterTargetSelection(pCity);
        }

        private static bool IsArmyCaptain(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.army?.data != null &&
                       pActor.army.getCaptain() == pActor;
            }
            catch { return false; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehFindTileNearbyGroupLeader), nameof(BehFindTileNearbyGroupLeader.execute))]
        public static bool FindTileNearbyGroupLeader_Prefix(Actor pActor, ref BehResult __result)
        {
            if (AWPathMovementBridge.HasOwnership(pActor))
            {
                __result = BehResult.Stop;
                return false;
            }
            bool ownedMarch = AWArmyMarchService.HasOwnedMarch(pActor);
            bool ownedFormation =
                ArmyRtsControllerService.HasFollowerMission(pActor);
            bool ownedMovement = ownedMarch || ownedFormation;
            if (ArmyMarchRules.ShouldRunVanillaFollowerSearch(ownedMovement))
                return true;
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                ArmyFollowerTargetResult targetResult =
                    AWArmyMarchService.ResolveFollowerTarget(
                        pActor, out WorldTile correctionTarget);
                if (targetResult != ArmyFollowerTargetResult.Move)
                {
                    if (ArmyMarchRules.ShouldUseIdleFollowerTarget(
                            ownedMovement, pHasCorrectionTarget: false,
                            pHasCurrentTile:
                                pActor?.current_tile?.data != null))
                    {
                        pActor.beh_tile_target = null;
                        pActor.setNotMoving();
                        pActor.timer_action = 0.1f;
                        __result = BehResult.Stop;
                        return false;
                    }
                    __result = BehResult.Stop;
                    return false;
                }
                ArmyFollowerStepResult stepResult =
                    AWArmyMarchService.TryStepFollowerDirect(pActor,
                        correctionTarget);
                if (ArmySharedPathRules.ShouldUseLocalReconnect(stepResult))
                {
                    pActor.beh_tile_target = correctionTarget;
                    __result = BehResult.Continue;
                    return false;
                }
                if (stepResult != ArmyFollowerStepResult.Stepped)
                {
                    __result = BehResult.Stop;
                    return false;
                }
                __result = BehResult.RepeatStep;
                return false;
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.ArmyMarchIndex, benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updatePathMovement))]
        public static bool UpdatePathMovement_InstalledProviderSwim_Prefix(
            Actor __instance)
        {
            return !AWArmyMarchService.
                TryAdvanceInstalledProviderSwimEntry(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updatePathMovement))]
        public static void UpdatePathMovement_Postfix(Actor __instance)
        {
            if (AWPathfindingRuntimeMode.IsAw3) return;
            AWArmyMarchService.OnVanillaLeaderPathStep(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdomArmyBacklink_Prefix(
            City __instance)
        {
            Army army = null;
            try { army = __instance?.getArmy(); }
            catch { }
            if (army?.data == null) return;

            City armyCity = ReadField<City>(ArmyCityField, army);
            bool detached = ArmyCreationSafetyRules.
                ShouldDetachCityReference(
                    AWArmyService.IsSpecialArmy(army),
                    AWArmyRoleRules.ShouldUseDetachedArmy(
                        AWArmyService.GetRole(army)));
            if (detached || armyCity?.data != null &&
                !ReferenceEquals(armyCity, __instance))
            {
                try { CityArmyField?.SetValue(__instance, null); }
                catch { }
                return;
            }

            if (ReferenceEquals(armyCity, __instance)) return;
            try { ArmyCityField?.SetValue(army, __instance); }
            catch { }
            if (__instance?.data != null)
                army.data.id_city = __instance.id;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Army), nameof(Army.save))]
        public static bool ArmySave_Prefix(Army __instance)
        {
            bool hasData = __instance?.data != null;
            bool alive = false;
            try { alive = __instance != null && __instance.isAlive(); }
            catch { }
            if (ArmyCreationSafetyRules.ShouldSkipSave(hasData, alive))
            {
                ArmyInvalidCleanupQueue.Schedule(__instance);
                return false;
            }
            bool specialArmy = AWArmyService.IsSpecialArmy(__instance);
            City rawCity = ReadField<City>(ArmyCityField, __instance);
            Kingdom rawKingdom = ReadField<Kingdom>(ArmyKingdomField,
                __instance);
            Actor rawCaptain = ReadField<Actor>(ArmyCaptainField,
                __instance);
            bool detachCity = ArmyCreationSafetyRules.
                ShouldDetachCityReference(specialArmy,
                    AWArmyRoleRules.ShouldUseDetachedArmy(
                        AWArmyService.GetRole(__instance)));
            if (detachCity)
                AWArmyService.DetachArmyFromCity(__instance, rawCity);
            bool safeSave = ArmySaveSafetyRules.ShouldUseSafeSave(
                specialArmy, IsReferenceValid(rawCity),
                IsReferenceValid(rawKingdom), IsReferenceValid(rawCaptain));
            if (!safeSave) return true;

            City city = detachCity ? null : SafeCity(__instance);
            Actor captain = SafeCaptain(__instance);
            Kingdom kingdom = SafeKingdom(__instance, city, captain);
            int unitCount = SafeUnitCount(__instance);

            if (ArmySaveSafetyRules.ShouldRemoveUnrecoverableArmy(
                    kingdom?.data != null, city?.data != null,
                    captain?.data != null, unitCount))
            {
                __instance.data.id_city = -1L;
                __instance.data.id_kingdom = -1L;
                __instance.data.id_captain = -1L;
                ArmyInvalidCleanupQueue.Schedule(__instance);
                return false;
            }

            RepairReferences(__instance, city, kingdom, captain);
            try { __instance.data.save(); }
            catch
            {
                ArmyInvalidCleanupQueue.Schedule(__instance);
                return false;
            }
            __instance.data.id_city = city?.data != null ? city.id : -1L;
            __instance.data.id_kingdom = kingdom?.data != null ? kingdom.id : -1L;
            __instance.data.id_captain = captain?.data != null ? captain.data.id : -1L;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Army), nameof(Army.Dispose))]
        public static void ArmyDispose_Prefix(Army __instance,
            out IDisposable __state)
        {
            __state = ArmyCaptainDisposalScope.Open(__instance);
            WarArmyReturnService.OnArmyDisposed(__instance);
            ArmyRtsWarLifecycleService.OnArmyDestroyed(__instance);
            ArmyRtsControllerService.Invalidate(__instance?.id ?? -1L);
            ArmyFormationService.RemoveArmy(__instance?.id ?? -1L);
            ArmyLogisticsService.OnArmyDisposed(__instance);
            ArmyRetreatService.OnArmyDisposed(__instance);
            ArmyMissionPersistence.OnArmyDisposed(__instance);
            ArmyStrategicIndexService.OnArmyDisposed(__instance);
            AWArmyMarchService.ClearArmy(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Army), nameof(Army.Dispose))]
        public static void ArmyDispose_Postfix(IDisposable __state)
        {
            __state?.Dispose();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Army), nameof(Army.Dispose))]
        private static Exception ArmyDispose_Finalizer(Exception __exception,
            IDisposable __state)
        {
            __state?.Dispose();
            return __exception;
        }

        private static City SafeCity(Army pArmy)
        {
            try
            {
                City city = pArmy.getCity();
                if (city?.data != null) return city;
            }
            catch { }

            try
            {
                City anchor = AWArmyService.FindAnchorCity(pArmy);
                return anchor?.data != null ? anchor : null;
            }
            catch { return null; }
        }

        private static Actor RawCaptain(Army pArmy)
        {
            return ReadField<Actor>(ArmyCaptainField, pArmy);
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try
            {
                Actor captain = pArmy.getCaptain();
                if (captain?.data != null && !captain.isRekt()) return captain;
            }
            catch { }

            try
            {
                foreach (Actor unit in pArmy.getUnits())
                    if (unit?.data != null && !unit.isRekt())
                        return unit;
            }
            catch { }

            return null;
        }

        private static long ActorId(Actor pActor)
        {
            try { return pActor?.data?.id ?? -1L; }
            catch { return -1L; }
        }

        private static bool IsArmyMember(Army pArmy, Actor pActor)
        {
            try
            {
                return pArmy?.data != null && pActor?.data != null &&
                       ReferenceEquals(pActor.army, pArmy) &&
                       pArmy.units != null &&
                       pArmy.units.Contains(pActor);
            }
            catch { return false; }
        }

        private static bool ShouldRejectCaptainDetachment(Actor pActor,
            Army pCurrentArmy, Army pNextArmy)
        {
            if (pActor?.data == null || pCurrentArmy?.data == null ||
                ReferenceEquals(pCurrentArmy, pNextArmy)) return false;
            if (!CaptainMatchesArmyKingdom(pCurrentArmy, pActor))
                return false;
            bool actorIsCaptain;
            try
            {
                actorIsCaptain = ReferenceEquals(
                    pCurrentArmy.getCaptain(), pActor);
            }
            catch { actorIsCaptain = false; }
            return ArmyCaptainContinuityRules.
                ShouldRejectCaptainDetachment(
                    ArmyRtsRuntimeMode.Current,
                    AW3MultiplayerReplicaScope.IsReplicaSession ||
                    AW3MultiplayerReplicaScope.IsApplying ||
                    ArmyCaptainDisposalScope.IsActive(pCurrentArmy),
                    IsLiveArmy(pCurrentArmy), actorIsCaptain,
                    IsLiveCaptainActor(pActor),
                    leavingCurrentArmy: true,
                    actorIsCivilAuthority: IsCivilAuthority(pActor));
        }

        private static bool HasLiveCaptainLeaseOutside(Actor pActor,
            Army pRequestedArmy)
        {
            if (pActor?.data == null ||
                AW3MultiplayerReplicaScope.IsApplying) return false;
            Army currentArmy = pActor.army;
            if (currentArmy?.data == null ||
                ReferenceEquals(currentArmy, pRequestedArmy) ||
                ArmyCaptainDisposalScope.IsActive(currentArmy))
                return false;
            bool actorIsCurrentCaptain = false;
            try
            {
                actorIsCurrentCaptain = ReferenceEquals(
                    currentArmy.getCaptain(), pActor);
            }
            catch { }
            return !ArmyCaptainContinuityRules.
                CanCreateNewArmyWithRequestedCaptain(
                    actorExists: true,
                    actorAlive: IsLiveCaptainActor(pActor),
                    currentArmyLive: IsLiveArmy(currentArmy),
                    actorIsCurrentCaptain: actorIsCurrentCaptain);
        }

        private static bool IsCivilAuthority(Actor pActor)
        {
            try
            {
                return pActor?.data != null &&
                       (pActor.isProfession(UnitProfession.King) ||
                        pActor.isProfession(UnitProfession.Leader) ||
                        pActor.isKing() || pActor.isCityLeader());
            }
            catch { return false; }
        }

        private static void DetachCivilAuthorityCaptain(Army pArmy,
            Actor pActor)
        {
            if (!IsCivilAuthority(pActor)) return;
            try
            {
                if (pActor.army == pArmy) pActor.removeFromArmy();
            }
            catch
            {
                try { pActor.setArmy(null); }
                catch { }
            }
            ArmyRtsControllerService.ReleaseActor(pActor);
            ArmyDeploymentService.ReleaseActor(pActor, restoreJob: true);
            TemporaryLevyService.OnActorInvalidated(pActor);
            WartimeGarrisonService.OnActorInvalidated(pActor);
            TemporarySlaveVanguardService.OnMemberInvalidated(pActor);
            MandateMilitaryPhaseService.Clear(pActor);
            if (RoyalGuardService.IsRoyalGuard(pActor))
                RoyalGuardService.DismissGuard(pActor,
                    "became_civil_authority");
        }

        private static bool IsLiveArmy(Army pArmy)
        {
            try { return pArmy?.data != null && pArmy.isAlive(); }
            catch { return false; }
        }

        private static Kingdom SafeKingdom(Army pArmy, City pCity, Actor pCaptain)
        {
            if (pCaptain?.kingdom?.data != null) return pCaptain.kingdom;
            if (pCity?.kingdom?.data != null) return pCity.kingdom;

            Kingdom stored = ReadField<Kingdom>(ArmyKingdomField, pArmy);
            if (stored?.data != null) return stored;

            try
            {
                Kingdom kingdom = pArmy.getKingdom();
                if (kingdom?.data != null) return kingdom;
            }
            catch { }

            return null;
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return pArmy.countUnits(); }
            catch { return 0; }
        }

        private static void TrySetKingdom(Army pArmy, Kingdom pKingdom)
        {
            try { ArmyKingdomField?.SetValue(pArmy, pKingdom); }
            catch { }
        }

        private static T ReadField<T>(FieldInfo pField, Army pArmy)
            where T : class
        {
            try { return pField?.GetValue(pArmy) as T; }
            catch { return null; }
        }

        private static bool IsReferenceValid(City pCity)
        {
            return pCity == null || pCity.data != null;
        }

        private static bool IsReferenceValid(Kingdom pKingdom)
        {
            return pKingdom == null || pKingdom.data != null;
        }

        private static bool IsReferenceValid(Actor pActor)
        {
            return pActor == null || pActor.data != null;
        }

        private static void RepairReferences(Army pArmy, City pCity,
            Kingdom pKingdom, Actor pCaptain)
        {
            try { ArmyCityField?.SetValue(pArmy, pCity); }
            catch { }
            TrySetKingdom(pArmy, pKingdom);
            try { ArmyCaptainField?.SetValue(pArmy, pCaptain); }
            catch { }
        }

    }
}
