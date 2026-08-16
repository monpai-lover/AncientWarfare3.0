using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content;
using AncientWarfare3.core.performance;
using life.taxi;

namespace AncientWarfare3.core.lineage
{
    internal static class WarArmyReturnService
    {
        private const int MaximumArmiesPerFrame = 4;
        private const int MaximumQueueScansPerFrame = 64;
        private const int MaximumMemberJobChecksPerArmy = 128;

        private static readonly WarArmyReturnQueueCore Queue =
            new WarArmyReturnQueueCore();
        private static bool _rebuildPending;

        public static bool TryBegin(Army pArmy)
        {
            if (!IsLiveArmy(pArmy)) return false;
            if (ArmyRtsControllerService.HasValidMission(pArmy)) return false;
            Kingdom kingdom = AWArmyService.GetIntendedKingdom(pArmy);
            if (!IsLiveKingdom(kingdom)) return false;
            Actor captain = SafeCaptain(pArmy);
            if (!IsAlive(captain) || captain.kingdom != kingdom) return false;
            City target = ResolveTargetCity(pArmy, kingdom);
            if (!IsFriendlySafeCity(target, kingdom)) return false;
            if (!Queue.Begin(pArmy.id, kingdom.id, target.id)) return false;
            Persist(pArmy, kingdom.id, target.id);
            EnsureReturnCaptainJob(pArmy);
            ModClass.LogInfo("[AW3 RTS return] stage=admitted" +
                             " army=" + pArmy.id +
                             " kingdom=" + kingdom.id +
                             " target_city=" + target.id);
            return true;
        }

        public static void ProcessFrame()
        {
            if (_rebuildPending) RebuildRuntime();
            IReadOnlyList<WarArmyReturnQueueOrder> frame = Queue.TakeFrame(
                MaximumArmiesPerFrame, MaximumQueueScansPerFrame);
            for (int i = 0; i < frame.Count; i++)
            {
                WarArmyReturnQueueOrder order = frame[i];
                long armyId = order.ArmyId;
                Army army = ResolveArmy(order.ArmyId);
                Kingdom kingdom = ResolveKingdom(order.KingdomId);
                Actor captain = SafeCaptain(army);
                if (!IsLiveArmy(army) || !IsLiveKingdom(kingdom) ||
                    !IsAlive(captain) || captain.kingdom != kingdom ||
                    AWArmyService.GetIntendedKingdom(army) != kingdom)
                {
                    Discard(armyId, army);
                    continue;
                }
                bool hasValidMission = ArmyRtsControllerService.
                    HasValidMission(army);
                bool captainInside = IsInsideFriendlySafeCity(captain,
                    kingdom);
                bool arrivalSweepAdvanced = !hasValidMission &&
                    captainInside;
                bool armyArrived = arrivalSweepAdvanced &&
                    AdvanceArrivalSweep(army, kingdom, order);
                if (!captainInside)
                    ResetArrivalConfirmation(order);
                WarArmyReturnOrderDecision decision = WarArmyReturnRules.
                    ResolveOrder(armyAlive: true,
                        insideFriendlySafeCity: armyArrived,
                        hasValidMission: hasValidMission);
                if (decision == WarArmyReturnOrderDecision.CancelForMission)
                {
                    Cancel(armyId);
                    continue;
                }
                if (decision == WarArmyReturnOrderDecision.Complete)
                {
                    CompleteArrival(armyId, army);
                    continue;
                }

                City target = ResolveCity(order.TargetCityId);
                if (!IsFriendlySafeCity(target, kingdom))
                {
                    target = ResolveTargetCity(army, kingdom);
                    if (!IsFriendlySafeCity(target, kingdom))
                    {
                        Discard(armyId, army);
                        continue;
                    }
                    Queue.UpdateTarget(armyId, target.id);
                    Persist(army, kingdom.id, target.id);
                }
                if (!arrivalSweepAdvanced)
                    EnsureReturnJobs(army, order);
                Queue.Requeue(armyId);
            }
        }

        public static void Cancel(long pArmyId)
        {
            if (pArmyId < 0L) return;
            Queue.Cancel(pArmyId);
            ClearPersisted(ResolveArmy(pArmyId));
        }

        public static bool IsActive(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            pArmy.data.get(LineageKeys.AW_ARMY_RETURN_ACTIVE,
                out bool active, false);
            return active;
        }

        public static void OnArmyRosterChanged(Army pArmy)
        {
            if (pArmy?.data == null) return;
            Queue.OnRosterChanged(pArmy.id);
        }

        public static string GetJob(Actor pActor)
        {
            Army army = pActor?.army;
            if (!IsAlive(pActor) || army?.data == null ||
                !IsActive(army)) return "";
            return pActor == SafeCaptain(army)
                ? ArmyRtsContent.ReturnCaptainJobId
                : ArmyRtsContent.ReturnFollowerJobId;
        }

        internal static bool TryPrepareMilitaryP0Actor(Actor pActor)
        {
            Army army = pActor?.army;
            if (!IsAlive(pActor) || army?.data == null ||
                !IsActive(army)) return false;
            EnsureReturnActorJob(pActor, army, SafeCaptain(army));
            ArmyRtsMovementDiagnostic.Log("return", "return_prepare",
                pActor, "captain=" + (pActor == SafeCaptain(army)));
            return true;
        }

        internal static bool TryGetTarget(Actor pActor,
            out WorldTile pTarget)
        {
            pTarget = null;
            Army army = pActor?.army;
            if (!IsAlive(pActor) || army?.data == null ||
                !IsActive(army)) return false;
            WarArmyReturnStoredIntent stored = ReadPersisted(army);
            Kingdom kingdom = ResolveKingdom(stored?.KingdomId ?? -1L);
            City city = ResolveCity(stored?.TargetCityId ?? -1L);
            if (pActor.kingdom != kingdom ||
                AWArmyService.GetIntendedKingdom(army) != kingdom ||
                !IsFriendlySafeCity(city, kingdom)) return false;
            pTarget = SafeCityTile(city);
            bool resolved = pTarget?.data != null;
            if (resolved)
                ArmyRtsMovementDiagnostic.Log("return",
                    "return_target_resolved", pActor,
                    "city=" + city.id +
                    " target_tile=" + pTarget.data.tile_id);
            return resolved;
        }

        internal static bool TryHandleTransport(Actor pActor,
            WorldTile pTarget)
        {
            if (!TryGetTarget(pActor, out WorldTile activeTarget) ||
                activeTarget != pTarget ||
                SameIsland(pActor.current_tile, pTarget)) return false;
            if (HasExactTaxiRequest(pActor, pTarget)) return true;
            if (ArmyRtsTransportService.TryHandleActor(pActor, pTarget,
                    pMayBegin: true)) return true;
            ArmyRtsTransportService.EnsureNativeTaxiRequest(pActor, pTarget);
            return true;
        }

        private static bool HasExactTaxiRequest(Actor pActor,
            WorldTile pTarget)
        {
            if (pActor?.data == null || pTarget?.data == null) return false;
            try
            {
                TaxiRequest request = TaxiManager.getRequestForActor(pActor);
                return request?.getTileTarget()?.data?.tile_id ==
                       pTarget.data.tile_id;
            }
            catch { return false; }
        }

        internal static bool ShouldSuppressCombatPreemption(Actor pActor)
        {
            return TryGetTarget(pActor, out _);
        }

        internal static void SuppressCombatForReturn(Actor pActor)
        {
            if (!ShouldSuppressCombatPreemption(pActor)) return;
            ClearAttackTarget(pActor);
            EnsureReturnActorJob(pActor, pActor.army,
                SafeCaptain(pActor.army));
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            if (pArmy == null) return;
            Queue.RemoveDisposed(pArmy.id);
            ClearPersisted(pArmy);
        }

        public static void ClearRuntime()
        {
            Queue.Clear();
            _rebuildPending = true;
        }

        public static void RebuildRuntime()
        {
            Queue.Clear();
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                _rebuildPending = true;
                return;
            }
            _rebuildPending = false;
            if (World.world?.armies == null)
            {
                _rebuildPending = true;
                return;
            }
            foreach (Army army in World.world.armies)
                TryRestore(army);
        }

        private static void TryRestore(Army pArmy)
        {
            WarArmyReturnStoredIntent stored = ReadPersisted(pArmy);
            if (stored == null || !stored.Active) return;
            Kingdom kingdom = ResolveKingdom(stored.KingdomId);
            Actor captain = SafeCaptain(pArmy);
            City storedTarget = ResolveCity(stored.TargetCityId);
            City replacement = IsFriendlySafeCity(storedTarget, kingdom)
                ? storedTarget
                : ResolveTargetCity(pArmy, kingdom);
            var facts = new WarArmyReturnRestoreFacts
            {
                ArmyAlive = IsLiveArmy(pArmy) && IsAlive(captain),
                ArmyKingdomMatches = IsLiveKingdom(kingdom) &&
                    captain?.kingdom == kingdom &&
                    AWArmyService.GetIntendedKingdom(pArmy) == kingdom,
                InsideFriendlySafeCity = IsInsideFriendlySafeCity(captain,
                    kingdom),
                HasValidMission = ArmyRtsControllerService.
                    HasValidMission(pArmy),
                StoredTargetFriendlySafe = IsFriendlySafeCity(storedTarget,
                    kingdom),
                ReplacementTargetCityId = replacement?.id ?? -1L
            };
            if (!WarArmyReturnPersistenceRules.TryRestore(stored, facts,
                    out WarArmyReturnStoredIntent restored))
            {
                if (facts.HasValidMission)
                {
                    Cancel(stored.ArmyId);
                    ModClass.LogInfo(
                        "[AW3 RTS return] stage=cancelled" +
                        " reason=restore_valid_mission" +
                        " army=" + stored.ArmyId);
                }
                else
                {
                    ModClass.LogInfo(
                        "[AW3 RTS return] stage=restore_discarded" +
                        " reason=restore_invalid_or_complete" +
                        " army=" + stored.ArmyId);
                    Discard(stored.ArmyId, pArmy);
                }
                return;
            }
            if (!Queue.Begin(restored.ArmyId, restored.KingdomId,
                    restored.TargetCityId))
            {
                ModClass.LogInfo(
                    "[AW3 RTS return] stage=restore_discarded" +
                    " reason=restore_queue_rejected" +
                    " army=" + stored.ArmyId);
                Discard(stored.ArmyId, pArmy);
                return;
            }
            Persist(pArmy, restored.KingdomId, restored.TargetCityId);
            EnsureReturnCaptainJob(pArmy);
        }

        private static void CompleteArrival(long pArmyId, Army pArmy)
        {
            Queue.Complete(pArmyId);
            ClearPersisted(pArmy);
            ArmyRtsControllerService.ReleaseAfterReturn(pArmy);
            ModClass.LogInfo("[AW3 RTS return] stage=completed" +
                             " army=" + pArmyId +
                             " rts_active=" +
                             ArmyRtsControllerService.HasValidMission(pArmy));
        }

        private static void Discard(long pArmyId, Army pArmy)
        {
            Queue.Complete(pArmyId);
            ClearPersisted(pArmy);
            ReleaseReturnJobs(pArmy);
            ModClass.LogInfo("[AW3 RTS return] stage=discarded" +
                             " army=" + pArmyId);
        }

        private static void Persist(Army pArmy, long pKingdomId,
            long pTargetCityId)
        {
            if (pArmy?.data == null) return;
            pArmy.data.set(LineageKeys.AW_ARMY_RETURN_ACTIVE, true);
            pArmy.data.set(LineageKeys.AW_ARMY_RETURN_KINGDOM_ID,
                pKingdomId);
            pArmy.data.set(LineageKeys.AW_ARMY_RETURN_TARGET_CITY_ID,
                pTargetCityId);
        }

        private static WarArmyReturnStoredIntent ReadPersisted(Army pArmy)
        {
            if (pArmy?.data == null) return null;
            pArmy.data.get(LineageKeys.AW_ARMY_RETURN_ACTIVE,
                out bool active, false);
            if (!active) return null;
            pArmy.data.get(LineageKeys.AW_ARMY_RETURN_KINGDOM_ID,
                out long kingdomId, -1L);
            pArmy.data.get(LineageKeys.AW_ARMY_RETURN_TARGET_CITY_ID,
                out long targetCityId, -1L);
            return new WarArmyReturnStoredIntent
            {
                Active = true,
                ArmyId = pArmy.id,
                KingdomId = kingdomId,
                TargetCityId = targetCityId
            };
        }

        private static void ClearPersisted(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.removeBool(LineageKeys.AW_ARMY_RETURN_ACTIVE);
            pArmy.data.removeLong(LineageKeys.AW_ARMY_RETURN_KINGDOM_ID);
            pArmy.data.removeLong(LineageKeys.AW_ARMY_RETURN_TARGET_CITY_ID);
        }

        private static void EnsureReturnCaptainJob(Army pArmy)
        {
            if (pArmy?.data == null || !IsActive(pArmy)) return;
            Actor captain = SafeCaptain(pArmy);
            EnsureReturnActorJob(captain, pArmy, captain);
        }

        private static void EnsureReturnJobs(Army pArmy,
            WarArmyReturnQueueOrder pOrder)
        {
            if (pArmy?.data == null || pOrder == null ||
                !IsActive(pArmy)) return;
            Actor captain = SafeCaptain(pArmy);
            EnsureReturnActorJob(captain, pArmy, captain);
            int count;
            try { count = pArmy.units?.Count ?? 0; }
            catch { count = 0; }
            int start = Math.Min(count, Math.Max(0, pOrder.MemberCursor));
            int end = Math.Min(count,
                start + MaximumMemberJobChecksPerArmy);
            for (int i = start; i < end; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (actor == captain) continue;
                EnsureReturnActorJob(actor, pArmy, captain);
            }
            pOrder.MemberCursor = end >= count ? 0 : end;
        }

        private static bool AdvanceArrivalSweep(Army pArmy,
            Kingdom pKingdom, WarArmyReturnQueueOrder pOrder)
        {
            if (pArmy?.data == null || pKingdom?.data == null ||
                pOrder == null) return false;
            int count;
            try { count = pArmy.units?.Count ?? 0; }
            catch { return false; }
            if (pOrder.ArrivalExpectedMemberCount != count)
            {
                ResetArrivalConfirmation(pOrder);
                pOrder.ArrivalExpectedMemberCount = count;
            }
            Actor captain = SafeCaptain(pArmy);
            int start = Math.Min(count,
                Math.Max(0, pOrder.ArrivalCursor));
            int end = Math.Min(count,
                start + MaximumMemberJobChecksPerArmy);
            for (int i = start; i < end; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch
                {
                    pOrder.ArrivalSweepClear = false;
                    continue;
                }
                if (!IsAlive(actor) || actor.army != pArmy)
                {
                    pOrder.ArrivalSweepClear = false;
                    continue;
                }
                long actorId = actor.data.id;
                if (!pOrder.ArrivalSweepActorIds.Add(actorId))
                    pOrder.ArrivalSweepClear = false;
                if (actor.kingdom != pKingdom ||
                    !IsInsideFriendlySafeCity(actor, pKingdom))
                {
                    pOrder.ArrivalSweepClear = false;
                    pOrder.ArrivalVerifiedActorIds.Remove(actorId);
                }
                else if (!pOrder.ArrivalConfirmationPass)
                {
                    pOrder.ArrivalVerifiedActorIds.Add(actorId);
                }
                else if (!pOrder.ArrivalVerifiedActorIds.Contains(actorId))
                {
                    pOrder.ArrivalSweepClear = false;
                }
                EnsureReturnActorJob(actor, pArmy, captain);
            }
            pOrder.ArrivalCursor = end >= count ? 0 : end;
            if (end < count) return false;
            bool completeRosterObserved =
                pOrder.ArrivalSweepActorIds.Count == count;
            if (!pOrder.ArrivalConfirmationPass)
            {
                bool freezeComplete = pOrder.ArrivalSweepClear &&
                    completeRosterObserved &&
                    pOrder.ArrivalVerifiedActorIds.Count == count;
                if (!freezeComplete)
                {
                    ResetArrivalConfirmation(pOrder);
                    return false;
                }
                pOrder.ArrivalConfirmationPass = true;
                ResetArrivalSweep(pOrder);
                return false;
            }
            bool arrived = pOrder.ArrivalSweepClear &&
                completeRosterObserved &&
                pOrder.ArrivalVerifiedActorIds.Count == count &&
                pOrder.ArrivalVerifiedActorIds.SetEquals(
                    pOrder.ArrivalSweepActorIds);
            if (!arrived) ResetArrivalConfirmation(pOrder);
            return arrived;
        }

        private static void ResetArrivalSweep(WarArmyReturnQueueOrder pOrder)
        {
            if (pOrder == null) return;
            pOrder.ArrivalCursor = 0;
            pOrder.ArrivalSweepClear = true;
            pOrder.ArrivalSweepActorIds.Clear();
        }

        private static void ResetArrivalConfirmation(
            WarArmyReturnQueueOrder pOrder)
        {
            if (pOrder == null) return;
            ResetArrivalSweep(pOrder);
            pOrder.ArrivalExpectedMemberCount = -1;
            pOrder.ArrivalConfirmationPass = false;
            pOrder.ArrivalVerifiedActorIds.Clear();
        }

        private static void EnsureReturnActorJob(Actor pActor, Army pArmy,
            Actor pCaptain)
        {
            if (!IsAlive(pActor) || pActor.ai == null ||
                pActor.army != pArmy) return;
            SyntheticLevyService.ResetReturnArrival(pActor);
            bool captain = pActor == pCaptain;
            bool arrivalVerified = Queue.IsArrivalVerified(pArmy.id,
                pActor.data.id);
            if (arrivalVerified)
            {
                WarArmyReturnStoredIntent stored = ReadPersisted(pArmy);
                Kingdom returnKingdom = ResolveKingdom(
                    stored?.KingdomId ?? -1L);
                if (!IsInsideFriendlySafeCity(pActor, returnKingdom))
                {
                    Queue.OnRosterChanged(pArmy.id);
                    arrivalVerified = false;
                }
            }
            string jobId = captain
                ? ArmyRtsContent.ReturnCaptainJobId
                : ArmyRtsContent.ReturnFollowerJobId;
            bool captainAtTargetCenter = captain &&
                IsAtPersistedTargetCityCenter(pActor, pArmy);
            bool waitAtArrival = WarArmyReturnRules.
                ShouldWaitAtReturnArrival(arrivalVerified, captain,
                    captainAtTargetCenter);
            string taskId = waitAtArrival
                ? "wait"
                : captain
                ? ArmyRtsContent.ReturnTaskId
                : "warrior_army_follow_leader";
            bool expectedJob = pActor.ai.job?.id == jobId;
            bool expectedTask = pActor.isTask(taskId);
            bool transportOwned = pActor.is_inside_boat ||
                ArmyRtsTransportService.OwnsActorTask(pActor);
            bool repair = WarArmyReturnRules.ShouldRepairReturnTask(
                IsActive(pArmy), actorAlive: true, expectedJob,
                expectedTask, pActor.is_moving, transportOwned);
            if (repair)
            {
                ClearAttackTarget(pActor);
                pActor.cancelAllBeh();
                pActor.stopMovement();
                pActor.clearOldPath();
                pActor.clearTileTarget();
                pActor.beh_tile_target = null;
                pActor.ai.setJob(jobId);
                pActor.ai.setTask(taskId);
                ArmyRtsMovementDiagnostic.Log("return",
                    "task_repaired", pActor, "task=" + taskId);
            }
            ArmyMilitaryMovementPriorityIndex.Register(pActor.data.id,
                ArmyMilitaryMovementPriorityKind.RtsMember);
        }

        private static bool IsAtPersistedTargetCityCenter(Actor pActor,
            Army pArmy)
        {
            if (pActor?.current_tile?.data == null || pArmy?.data == null)
                return false;
            WarArmyReturnStoredIntent stored = ReadPersisted(pArmy);
            City city = ResolveCity(stored?.TargetCityId ?? -1L);
            WorldTile center = SafeCityTile(city);
            if (center?.data == null) return false;
            return pActor.current_tile.data.tile_id == center.data.tile_id;
        }

        private static void ReleaseReturnJobs(Army pArmy)
        {
            Actor captain = SafeCaptain(pArmy);
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                ReleaseReturnActor(actor);
            }
            if (captain?.data != null) ReleaseReturnActor(captain);
        }

        private static void ReleaseReturnActor(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            SyntheticLevyService.ConfirmReturnArrivalIfSafe(pActor);
            ArmyMilitaryMovementPriorityIndex.Unregister(pActor.data.id);
            string jobId = pActor.ai.job?.id ?? "";
            bool returnOwnedJob =
                jobId == ArmyRtsContent.ReturnCaptainJobId ||
                jobId == ArmyRtsContent.ReturnFollowerJobId;
            if (returnOwnedJob)
            {
                pActor.cancelAllBeh();
                pActor.ai.clearJob();
                StandingArmyPeacetimeService.RefreshJob(pActor);
            }
        }

        private static void ClearAttackTarget(Actor pActor)
        {
            if (pActor?.data == null) return;
            try { pActor.clearAttackTarget(); }
            catch { }
            try { pActor.beh_actor_target = null; }
            catch { }
        }

        private static City ResolveTargetCity(Army pArmy, Kingdom pKingdom)
        {
            City anchor = AWArmyService.FindAnchorCity(pArmy);
            if (IsFriendlySafeCity(anchor, pKingdom)) return anchor;
            if (IsFriendlySafeCity(pKingdom?.capital, pKingdom))
                return pKingdom.capital;
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (IsFriendlySafeCity(city, pKingdom)) return city;
            }
            catch { }
            return null;
        }

        private static bool IsInsideFriendlySafeCity(Actor pActor,
            Kingdom pKingdom)
        {
            City city;
            try { city = pActor?.current_tile?.zone?.city; }
            catch { city = null; }
            if (!IsFriendlySafeCity(city, pKingdom)) return false;
            WorldTile center = SafeCityTile(city);
            WorldTile current = pActor?.current_tile;
            if (center?.data == null || current?.data == null) return false;
            return WarArmyReturnRules.IsInsideReturnArrivalRadius(
                insideFriendlySafeCity: true,
                deltaX: (long)current.x - center.x,
                deltaY: (long)current.y - center.y);
        }

        internal static bool IsInsideFriendlySafeCity(Actor pActor)
        {
            return IsInsideFriendlySafeCity(pActor, pActor?.kingdom);
        }

        private static bool IsFriendlySafeCity(City pCity,
            Kingdom pKingdom)
        {
            if (!IsLiveCity(pCity) || !IsLiveKingdom(pKingdom) ||
                pCity.kingdom != pKingdom) return false;
            try
            {
                return !pCity.isGettingCaptured() &&
                       OccupiedCitySupplyService.CanProvideToRealm(
                           pCity, pKingdom);
            }
            catch { return false; }
        }

        private static WorldTile SafeCityTile(City pCity)
        {
            try { return pCity?.getTile(); }
            catch { return null; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static bool SameIsland(WorldTile pFirst, WorldTile pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null) return false;
            try { return pFirst.isSameIsland(pSecond); }
            catch { return false; }
        }

        private static bool IsLiveArmy(Army pArmy)
        {
            try
            {
                return pArmy?.data != null && pArmy.isAlive() &&
                       pArmy.countUnits() > 0;
            }
            catch { return false; }
        }

        private static bool IsAlive(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && pKingdom.isAlive() &&
                       !pKingdom.isRekt();
            }
            catch { return false; }
        }

        private static bool IsLiveCity(City pCity)
        {
            try
            {
                return pCity?.data != null && pCity.isAlive() &&
                       !pCity.isRekt();
            }
            catch { return false; }
        }

        private static Army ResolveArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City ResolveCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
