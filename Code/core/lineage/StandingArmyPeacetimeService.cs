using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.core.performance;
using life.taxi;

namespace AncientWarfare3.core.lineage
{
    internal static class StandingArmyPeacetimeService
    {
        private sealed class JobRefreshPlan
        {
            public readonly long KingdomId;
            public bool MilitaryEmergency;
            public int CityCursor;
            public int ActorCursor;

            public JobRefreshPlan(long pKingdomId,
                bool pMilitaryEmergency)
            {
                KingdomId = pKingdomId;
                MilitaryEmergency = pMilitaryEmergency;
            }
        }

        private const string PatrolCursorKey =
            "aw_standing_army_peacetime_patrol_cursor";
        private const string ReproductionFirstObservedYearKey =
            "aw_reproduction_task_first_observed_year";
        private const int MaxTileChecks = 16;
        private const int MaxActorsPerWorkItem = 16;
        private const double BoundaryRefreshRetrySeconds = 8d;
        private static readonly Dictionary<long, JobRefreshPlan>
            RefreshPlans = new Dictionary<long, JobRefreshPlan>();
        private static readonly Dictionary<long, double>
            BoundaryRefreshNextAllowedByCity = new Dictionary<long, double>();

        public static string GetJob(Actor pActor)
        {
            ReleaseLegacyPatrolForJobSelection(pActor);
            if (!ShouldAssignCitizenJob(pActor) ||
                !AssetManager.job_actor.has(ArmyRtsContent.CitizenJobId))
                return "";
            return ArmyRtsContent.CitizenJobId;
        }

        public static bool ShouldUsePeacetimeJob(Actor pActor)
        {
            return StandingArmyRules.ShouldUsePeacetimePatrol(
                IsCareerStandingSoldier(pActor),
                HasMilitaryEmergency(pActor),
                IsInCombat(pActor),
                HasCityAttackOrder(pActor));
        }

        public static bool CanYieldToReproduction(Actor pActor)
        {
            if (pActor?.ai == null) return false;
            return StandingArmyRules.ShouldReleaseLegacyPeacetimePatrol(
                pActor.ai.job?.id ?? "", pActor.ai.task?.id ?? "");
        }

        public static bool IsCareerStandingSoldier(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !pActor.isAlive() || !pActor.isWarrior()) return false;
            Army army = pActor.army;
            if (army?.data == null || AWArmyService.IsSpecialArmy(army) ||
                ArmyRtsControllerService.HasActiveMission(army.id) ||
                !WarArmyReturnRules.ShouldAllowPeacetimeJob(
                    WarArmyReturnService.IsActive(army)))
                return false;
            City anchorCity = AWArmyService.FindAnchorCity(army);
            if (anchorCity?.data == null || pActor.kingdom != anchorCity.kingdom)
                return false;
            City city = anchorCity;

            // 原版检查：军队必须是该城市当前驻军
            // 但 RTS 军队执行任务时会离开城市，任务结束后应恢复巡逻
            // 放宽条件：只要军队曾驻扎该城市或仍在该城市领土即可
            bool anchoredToActorCity = anchorCity?.id == city.id;
            if (StandingArmyRules.ShouldKeepPeacetimePatrolForAnchor(
                    actorCityMatchesAnchor: true,
                    armyAnchoredToActorCity: anchoredToActorCity,
                    actorInsideCityCoreZone: pActor.current_tile?.zone?.city == city))
            {
                // 军队当前驻扎在该城 → 合格
            }
            else
            {
                // 军队不在该城，检查是否仍在该城领土内
                // （RTS 任务结束，士兵回到边境时的情况）
                return false;
            }

            pActor.data.get(LineageKeys.TEMPORARY_LEVY,
                out bool temporaryLevy, false);
            pActor.data.get(LineageKeys.WARTIME_GARRISON,
                out bool wartimeGarrison, false);
            pActor.data.get(LineageKeys.TEMPORARY_SLAVE_VANGUARD_MEMBER,
                out bool slaveVanguard, false);
            return !temporaryLevy && !wartimeGarrison && !slaveVanguard &&
                   !SlaveService.IsSlave(pActor);
        }

        public static bool HasMilitaryEmergency(Actor pActor)
        {
            return pActor?.kingdom?.data != null &&
                   MilitaryEmergencyService.HasAny(pActor.kingdom);
        }

        public static bool IsInCombat(Actor pActor)
        {
            if (pActor?.data == null) return false;
            try
            {
                return pActor.has_attack_target ||
                       pActor.ai?.task?.in_combat == true;
            }
            catch { return false; }
        }

        public static bool HasCityAttackOrder(Actor pActor)
        {
            try
            {
                return pActor?.city?.data != null &&
                       pActor.city.hasAttackZoneOrder();
            }
            catch { return false; }
        }

        public static void RefreshJob(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            if (WarArmyReturnService.IsActive(pActor.army))
            {
                WarArmyReturnService.TryPrepareMilitaryP0Actor(pActor);
                return;
            }
            ReleaseLegacyPeacetimePatrol(pActor,
                pRestoreImmediately: true);
            if (ShouldAssignCitizenJob(pActor))
            {
                AssignCitizenJob(pActor);
                return;
            }
            ReleaseCitizenJob(pActor);
            if (pActor.ai.job == null) RestoreOriginalJob(pActor);
        }

        public static void RefreshAfterReturn(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            ReleasePostReturnMovementOwnership(pActor);
            RefreshJob(pActor);
            // Returning units can be released in large batches. Per-actor
            // Info logging here serializes the main thread and turns a normal
            // peacetime return into a multi-second file-I/O spike. Detailed
            // movement diagnostics remain available through the sampled RTS
            // diagnostic gate when explicitly enabled.
            if (AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
                ArmyRtsMovementDiagnostic.Log("peacetime", "return_completed",
                    pActor);
        }

        private static void ReleasePostReturnMovementOwnership(Actor pActor)
        {
            City city = pActor?.city;
            if (city?.data != null)
            {
                City target = null;
                bool hasTargetCity = false;
                bool targetAlive = false;
                bool sourceHasWarriors = false;
                bool targetStillEnemy = false;
                bool targetReachable = false;
                try
                {
                    target = city.target_attack_city;
                    hasTargetCity = target?.data != null;
                    targetAlive = hasTargetCity && target.isAlive();
                    sourceHasWarriors = city.hasAnyWarriors();
                    targetStillEnemy = hasTargetCity &&
                                       target.kingdom?.data != null &&
                                       target.kingdom.isEnemy(city.kingdom);
                    targetReachable = hasTargetCity &&
                                      target.reachableFrom(city);
                }
                catch { }
                if (StandingArmyRules.ShouldClearPostReturnAttackOrder(
                        hasTargetCity, targetAlive, sourceHasWarriors,
                        targetStillEnemy, targetReachable))
                {
                    city.target_attack_city = null;
                    city.target_attack_zone = null;
                }
            }

            try
            {
                TaxiRequest request = TaxiManager.getRequestForActor(pActor);
                if (request == null) return;
                request.embarkToBoat(pActor);
                if (request.countActors() == 0)
                    TaxiManager.cancelRequest(request);
            }
            catch { }
        }

        private static bool ShouldAssignCitizenJob(Actor pActor)
        {
            if (pActor?.data == null) return false;
            Army army = pActor.army;
            City city = pActor.city;
            bool actorValid;
            bool hasValidCity;
            bool ordinaryArmy;
            try
            {
                actorValid = !pActor.isRekt() && pActor.isAlive() &&
                             pActor.isWarrior();
                hasValidCity = city?.data != null && !city.isRekt() &&
                               city.kingdom == pActor.kingdom;
                ordinaryArmy = army?.data != null &&
                               !AWArmyService.IsSpecialArmy(army);
            }
            catch
            {
                return false;
            }
            bool activeMission = ordinaryArmy &&
                                 ArmyRtsControllerService.HasActiveMission(
                                     army.id);
            bool activeReturn = ordinaryArmy &&
                                WarArmyReturnService.IsActive(army);
            return StandingArmyRules.ShouldAssignCitizenJob(
                actorValid, hasValidCity, ordinaryArmy,
                activeMission, activeReturn, HasMilitaryEmergency(pActor),
                IsInCombat(pActor), HasCityAttackOrder(pActor),
                HasSpecialMilitaryState(pActor, army));
        }

        private static bool HasSpecialMilitaryState(Actor pActor, Army pArmy)
        {
            if (pActor?.data == null) return true;
            pActor.data.get(LineageKeys.TEMPORARY_LEVY,
                out bool temporaryLevy, false);
            pActor.data.get(LineageKeys.WARTIME_GARRISON,
                out bool wartimeGarrison, false);
            pActor.data.get(LineageKeys.TEMPORARY_SLAVE_VANGUARD_MEMBER,
                out bool slaveVanguard, false);
            try
            {
                return temporaryLevy || wartimeGarrison || slaveVanguard ||
                       AWArmyService.IsSpecialArmy(pArmy) ||
                       SyntheticLevyService.IsSynthetic(pActor) ||
                       RoyalGuardService.IsRoyalGuard(pActor) ||
                       SlaveService.IsSlave(pActor);
            }
            catch { return true; }
        }

        private static void AssignCitizenJob(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            if (!AssetManager.job_actor.has(ArmyRtsContent.CitizenJobId))
            {
                RestoreOriginalJob(pActor);
                return;
            }
            if (pActor.ai.job?.id == ArmyRtsContent.CitizenJobId &&
                pActor.ai.task != null) return;
            try
            {
                pActor.cancelAllBeh();
                pActor.ai.setJob(ArmyRtsContent.CitizenJobId);
                pActor.timer_action = 0f;
                pActor._beh_skip = false;
                pActor._update_done = false;
                pActor.b6_0_updateDecision(0f);
            }
            catch { RestoreOriginalJob(pActor); }
        }

        private static void ReleaseCitizenJob(Actor pActor)
        {
            if (pActor?.ai?.job?.id != ArmyRtsContent.CitizenJobId) return;
            try { pActor.cancelAllBeh(); }
            catch { }
            RestoreOriginalJob(pActor);
        }

        private static void RestoreOriginalJob(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            string jobId = "";
            try { jobId = pActor.getNextJob(); }
            catch { }
            if (string.IsNullOrEmpty(jobId) ||
                !AssetManager.job_actor.has(jobId))
                jobId = pActor.isWarrior() ? "attacker" : "unit_citizen";
            if (!AssetManager.job_actor.has(jobId)) return;
            try { pActor.ai.setJob(jobId); }
            catch { }
        }

        private static bool HandleActiveReproduction(Actor pActor,
            bool pObserveTimeout, string pTaskId)
        {
            int currentYear = SafeYear();
            pActor.data.get(ReproductionFirstObservedYearKey,
                out int firstObservedYear, -1);
            if (firstObservedYear < 0 || firstObservedYear > currentYear)
            {
                firstObservedYear = currentYear;
                pActor.data.set(ReproductionFirstObservedYearKey,
                    currentYear);
            }

            if (DynasticReproductionRules
                .ShouldPreservePeacetimeReproduction(
                    pObserveTimeout, pTaskId,
                    firstObservedYear, currentYear)) return true;

            ClearReproductionObservation(pActor);
            if (DynasticReproductionRules
                .ShouldRecoverStuckReproduction(
                    pObserveTimeout, pTaskId,
                    firstObservedYear, currentYear))
            {
                ResetReproductionActor(pActor.lover);
                ResetReproductionActor(pActor);
            }
            return false;
        }

        private static void ResetReproductionActor(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null ||
                !DynasticReproductionRules.IsSexualReproductionTask(
                    pActor.ai.task?.id)) return;
            ClearReproductionObservation(pActor);
            pActor.cancelAllBeh();
            try { pActor.ai.setJob(Actor.nextJobActor(pActor)); }
            catch { pActor.ai.clearJob(); }
        }

        private static void ClearReproductionObservation(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(ReproductionFirstObservedYearKey,
                out int firstObservedYear, -1);
            if (!DynasticReproductionRules.ShouldClearReproductionObservation(
                    firstObservedYear)) return;
            pActor.data.set(ReproductionFirstObservedYearKey, -1);
        }

        private static int SafeYear()
        {
            try { return Math.Max(0, Date.getCurrentYear()); }
            catch { return 0; }
        }

        public static void RestoreMilitaryJob(Actor pActor)
        {
            RefreshJob(pActor);
        }

        public static void ReleaseLegacyPatrolForJobSelection(Actor pActor)
        {
            ReleaseLegacyPeacetimePatrol(pActor,
                pRestoreImmediately: false);
        }

        private static void ReleaseLegacyPeacetimePatrol(Actor pActor,
            bool pRestoreImmediately)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            string jobId = pActor.ai.job?.id ?? "";
            string taskId = pActor.ai.task?.id ?? "";
            if (!StandingArmyRules.ShouldReleaseLegacyPeacetimePatrol(
                    jobId, taskId)) return;
            pActor.cancelAllBeh();
            pActor.data.set(PatrolCursorKey, 0);
            if (!pRestoreImmediately)
            {
                pActor.ai.clearJob();
                return;
            }
            try { pActor.ai.setJob(Actor.nextJobActor(pActor)); }
            catch { pActor.ai.clearJob(); }
        }

        public static void OnMilitaryEmergencyChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            bool emergency = MilitaryEmergencyService.HasAny(pKingdom);
            if (!RefreshPlans.TryGetValue(pKingdom.id,
                    out JobRefreshPlan plan) ||
                plan.MilitaryEmergency != emergency)
            {
                plan = new JobRefreshPlan(pKingdom.id, emergency);
                RefreshPlans[pKingdom.id] = plan;
            }
            ScheduleRefresh(plan.KingdomId);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data != null) RefreshPlans.Remove(pKingdom.id);
        }

        public static void ClearRuntime()
        {
            RefreshPlans.Clear();
            BoundaryRefreshNextAllowedByCity.Clear();
            CityBoundaryPatrolService.ClearRuntime();
        }

        private static void ScheduleRefresh(long pKingdomId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "standing_peacetime_jobs", pKingdomId),
                DeferredWorkClass.Runtime,
                () => ProcessRefresh(pKingdomId));
        }

        private static void ProcessRefresh(long pKingdomId)
        {
            if (!RefreshPlans.TryGetValue(pKingdomId,
                    out JobRefreshPlan plan)) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                RefreshPlans.Remove(pKingdomId);
                return;
            }

            bool emergency = MilitaryEmergencyService.HasAny(kingdom);
            if (plan.MilitaryEmergency != emergency)
            {
                plan.MilitaryEmergency = emergency;
                plan.CityCursor = 0;
                plan.ActorCursor = 0;
            }
            if (plan.CityCursor >= kingdom.cities.Count)
            {
                RefreshPlans.Remove(pKingdomId);
                return;
            }

            City city = kingdom.cities[plan.CityCursor];
            Army army = OrdinaryArmy(city, kingdom);
            if (army?.data != null)
            {
                int count = army.units.Count;
                int start = Math.Max(0,
                    Math.Min(plan.ActorCursor, count));
                int end = Math.Min(count,
                    start + MaxActorsPerWorkItem);
                for (int i = start; i < end; i++)
                    RefreshJob(army.units[i]);
                plan.ActorCursor = end;
                if (end < army.units.Count)
                {
                    ScheduleRefresh(pKingdomId);
                    return;
                }
            }

            plan.CityCursor++;
            plan.ActorCursor = 0;
            if (plan.CityCursor < kingdom.cities.Count)
                ScheduleRefresh(pKingdomId);
            else
                RefreshPlans.Remove(pKingdomId);
        }

        private static Army OrdinaryArmy(City pCity, Kingdom pKingdom)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom != pKingdom || !pCity.hasArmy()) return null;
            Army army = pCity.getArmy();
            return army?.data != null && !AWArmyService.IsSpecialArmy(army)
                ? army
                : null;
        }

        public static WorldTile GetPatrolTile(Actor pActor)
        {
            City city = AWArmyService.FindAnchorCity(pActor?.army);
            if (!ShouldUsePeacetimeJob(pActor) || city?.data == null)
                return SafeCityCenter(city);
            TryRefreshBoundaryZones(city);
            if (city.border_zones.Count == 0) return SafeCityCenter(city);

            pActor.data.get(PatrolCursorKey, out int cursor, 0);
            if (cursor < 0) cursor = 0;
            pActor.data.set(PatrolCursorKey,
                cursor == int.MaxValue ? 0 : cursor + 1);
            TileZone boundary = CityBoundaryPatrolService.GetBoundaryZone(
                city, pActor.data.id, cursor);
            WorldTile tile = FindSafeTile(boundary,
                pActor.data.id, cursor, pActor.current_tile);
            if (tile == null) tile = SafeCityCenter(city);
            if (tile != null && pActor.current_tile != null &&
                !pActor.current_tile.isSameIsland(tile))
                return SafeCityCenter(city);
            return tile;
        }

        private static void TryRefreshBoundaryZones(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.border_zones.Count > 0) return;
            double now = LineageService.CurTime();
            if (BoundaryRefreshNextAllowedByCity.TryGetValue(pCity.id,
                    out double nextAllowed) && now < nextAllowed) return;
            BoundaryRefreshNextAllowedByCity[pCity.id] = now +
                BoundaryRefreshRetrySeconds;
            try { pCity.recalculateNeighbourZones(); }
            catch { return; }
            if (pCity.border_zones.Count > 0)
            {
                BoundaryRefreshNextAllowedByCity.Remove(pCity.id);
                CityBoundaryPatrolService.Invalidate(pCity);
            }
        }

        private static WorldTile FindSafeTile(TileZone pZone,
            long pActorId, int pVisit, WorldTile pAvoid)
        {
            WorldTile[] tiles = pZone?.tiles;
            int count = tiles?.Length ?? 0;
            if (count <= 0) return SafeTile(pZone?.centerTile)
                ? pZone.centerTile
                : null;
            int start = WartimeGarrisonRules.PatrolStartIndex(pActorId,
                pVisit, count);
            WorldTile fallback = null;
            int checks = Math.Min(MaxTileChecks, count);
            for (int offset = 0; offset < checks; offset++)
            {
                WorldTile tile = tiles[(start + offset) % count];
                if (!SafeTile(tile)) continue;
                if (tile != pAvoid) return tile;
                fallback = tile;
            }
            WorldTile center = pZone?.centerTile;
            return SafeTile(center) && center != pAvoid ? center : fallback;
        }

        private static WorldTile SafeCityCenter(City pCity)
        {
            WorldTile tile = pCity?.getTile();
            return SafeTile(tile) ? tile : null;
        }

        private static bool SafeTile(WorldTile pTile)
        {
            TileTypeBase type = pTile?.Type;
            return WartimeGarrisonRules.CanUsePatrolCandidate(
                pTile?.data != null, type?.ground == true,
                type?.liquid == true, type?.ocean == true,
                type?.lava == true, type?.block == true);
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try
            {
                return pKingdomId >= 0
                    ? World.world?.kingdoms?.get(pKingdomId)
                    : null;
            }
            catch { return null; }
        }
    }
}
