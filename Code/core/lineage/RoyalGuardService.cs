using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.content;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class RoyalGuardService
    {
        private const int MAX_GUARDS_PER_KINGDOM = 20;
        private const int REFILL_SEARCH_THRESHOLD = 12;
        private const int RECRUITMENT_BATCH_LIMIT = 4;
        private const int RUNTIME_REFRESH_BATCH_LIMIT = 4;
        private const int GFX_REBUILD_BUDGET = 2;
        private const int DISMISS_BATCH_LIMIT = 2;
        private const int STALE_GUARD_ARMY_CLEANUP_LIMIT = 4;
        private const int DISMISS_SCAN_LIMIT = 64;
        private const int CANDIDATE_POOL_LIMIT = 16;
        private const int NOBLE_CANDIDATE_POOL_LIMIT = 8;
        private const int CANDIDATE_SCAN_LIMIT = 64;
        private const int AVERAGE_SCORE_SCAN_LIMIT = 48;
        private const float MIN_NOBLE_RATIO = 0.2f;
        private const int CHECK_INTERVAL = 60;
        private const int PROTECT_RADIUS = 10;
        private const int FOLLOW_RADIUS = 4;
        private const int PATROL_MIN_RADIUS = 2;
        private const int PATROL_MAX_RADIUS = 4;
        private const int PATROL_PHASE_INTERVAL = 8;
        private const int MAP_CHUNK_SIZE = 16;
        private const int SEARCH_COOLDOWN_PRUNE_THRESHOLD = 256;
        private const double THREAT_SEARCH_MISS_COOLDOWN = 2.0;
        private static readonly MethodInfo NewArmyObjectMethod = ResolveNewArmyObjectMethod();
        private static readonly Dictionary<long, double> ThreatSearchNextAllowed = new Dictionary<long, double>();
        private static readonly System.Random Rng = new System.Random();
        private static bool _creatingGuardArmy;

        private sealed class GuardCandidate
        {
            public Actor actor;
            public float score;
            public bool noble;
        }

        private sealed class GuardStateSnapshot
        {
            public long actorId;
            public string actorName;
            public long kingdomId;
            public string kingdomName;
            public long cityId;
            public string cityName;
            public string guardName;
            public bool active;
            public bool captain;
            public bool noble;
            public double appointedTime;
            public double dismissedTime;
            public string dismissReason;
        }

        internal static void ClearRuntimeCaches()
        {
            ThreatSearchNextAllowed.Clear();
        }

        public static bool IsRoyalGuard(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.ROYAL_GUARD, out bool flag, false);
            pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID, out long guardKingdomId, -1L);
            bool hasTrait = pActor.hasTrait(LineageKeys.TRAIT_GUARD);

            if (!flag && guardKingdomId < 0)
            {
                if (hasTrait || IsKingGuardJob(pActor))
                    ClearStaleGuardIdentity(pActor);
                return false;
            }

            if (flag && !hasTrait)
                pActor.addTrait(LineageKeys.TRAIT_GUARD);

            return flag || hasTrait;
        }

        public static bool ShouldBlockNormalArmy(Actor pActor)
        {
            return IsRoyalGuard(pActor);
        }

        public static bool CanAssignArmy(Actor pActor, Army pTargetArmy)
        {
            return RoyalGuardArmyAssignmentRules.CanAssign(
                actorIsRoyalGuard: IsRoyalGuard(pActor),
                targetArmyExists: pTargetArmy != null,
                targetIsRoyalGuardArmy: IsRoyalGuardArmy(pTargetArmy));
        }

        public static void EnsureKingdomGuard(Kingdom pKingdom, bool pForce = false)
        {
            if (pKingdom?.data == null) return;

            int now = (int)LineageService.CurTime();
            bool isRepublic = RepublicGovernmentService.IsRepublic(pKingdom);
            bool isRebel = MandateRebelService.IsRebelKingdom(pKingdom);
            bool kingdomExtinct = pKingdom.isRekt();
            bool successionPending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
            if (RoyalGuardMaintenanceRules.ShouldPreserveGuards(
                    successionPending, isRepublic, isRebel, kingdomExtinct))
                return;
            if (kingdomExtinct)
            {
                pKingdom.data.set(LineageKeys.ROYAL_GUARD_LAST_CHECK, now);
                bool complete = DismissKingdomGuards(pKingdom, "kingdom_extinct");
                if (RoyalGuardMaintenanceRules.ShouldClearDismissState(complete))
                    ClearKingdomGuardStateHints(pKingdom);
                return;
            }
            if (isRepublic || isRebel) return;

            if (!pForce)
            {
                pKingdom.data.get(LineageKeys.ROYAL_GUARD_LAST_CHECK, out int lastCheck, -1);
                if (!RoyalGuardMaintenanceRules.ShouldRunScheduledCheck(now, lastCheck, CHECK_INTERVAL, pKingdom.id))
                    return;
            }
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_LAST_CHECK, now);

            Actor king = pKingdom.king;
            if (king?.data == null || king.isRekt())
            {
                return;
            }

            bool kingIsXia = LineageService.IsXia(king);
            if (!kingIsXia)
            {
                return;
            }

            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardValidate, CityMaintenanceBenchmarkRules.Group);
            List<Actor> active = CollectActiveGuards(pKingdom);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardValidate, CityMaintenanceBenchmarkRules.Group);

            bool militaryEmergency = MilitaryEmergencyService.HasAny(pKingdom);
            bool standingCoreReady = KingdomMilitaryReadinessService.
                HasReadyStandingCore(pKingdom);
            if (!StandingArmyRules.ShouldAllowGuardMaintenance(
                    active.Count > 0, standingCoreReady, militaryEmergency))
                return;
            bool allowRecruitment = StandingArmyRules.ShouldAllowGuardRecruitment(
                standingCoreReady, militaryEmergency);

            string guardName = BuildGuardName(pKingdom);
            int dismissalsThisPass = TrimExcessGuards(active, MAX_GUARDS_PER_KINGDOM,
                DISMISS_BATCH_LIMIT);
            var newlyAppointed = new List<Actor>(RECRUITMENT_BATCH_LIMIT);

            int targetNobles = Math.Max(1, (int)Math.Ceiling(MAX_GUARDS_PER_KINGDOM * MIN_NOBLE_RATIO));
            int activeNobles = CountNobles(active);
            Actor captain = PickCaptain(active);
            if (allowRecruitment && RoyalGuardMaintenanceRules.ShouldSearchCandidates(active.Count, activeNobles, targetNobles,
                    captain != null, REFILL_SEARCH_THRESHOLD))
            {
                Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardAverage, CityMaintenanceBenchmarkRules.Group);
                float averageWarriorScore = AverageWarriorScore(pKingdom);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardAverage, CityMaintenanceBenchmarkRules.Group);

                Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardCandidates, CityMaintenanceBenchmarkRules.Group);
                List<GuardCandidate> candidates = CollectCandidates(pKingdom, active, averageWarriorScore);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardCandidates, CityMaintenanceBenchmarkRules.Group);

                int availableNobles = activeNobles + CountNobleCandidates(candidates);
                if (availableNobles <= 0)
                {
                    if (RoyalGuardMaintenanceRules.ShouldDeferGuardMaintenanceForCaptainShortage(
                            active.Count, availableNobles))
                        return;
                    ClearKingdomGuardStateHints(pKingdom);
                    return;
                }

                int desired = Math.Min(MAX_GUARDS_PER_KINGDOM, active.Count + candidates.Count);
                desired = Math.Min(desired, availableNobles * 5);
                desired = Math.Max(1, desired);
                desired = RoyalGuardMaintenanceRules.ClampDesiredGuardCountForBatch(
                    active.Count, desired, RECRUITMENT_BATCH_LIMIT);
                dismissalsThisPass += TrimExcessGuards(active, desired,
                    DISMISS_BATCH_LIMIT - dismissalsThisPass);

                targetNobles = Math.Max(1, (int)Math.Ceiling(desired * MIN_NOBLE_RATIO));
                Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardFill, CityMaintenanceBenchmarkRules.Group);
                FillNobleQuota(pKingdom, active, candidates, targetNobles, guardName, newlyAppointed);
                FillGuardSlots(pKingdom, active, candidates, desired, guardName, newlyAppointed);
                captain = PickCaptain(active);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardFill, CityMaintenanceBenchmarkRules.Group);
            }

            if (captain == null)
            {
                int availableNobles = CountNobles(active);
                if (RoyalGuardMaintenanceRules.ShouldDeferGuardMaintenanceForCaptainShortage(
                        active.Count, availableNobles))
                    return;
                ClearKingdomGuardStateHints(pKingdom);
                return;
            }

            EnsureFormationRecorded(pKingdom, guardName);
            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardArmy, CityMaintenanceBenchmarkRules.Group);
            Army guardArmy = EnsureGuardArmy(pKingdom, captain, guardName);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardArmy, CityMaintenanceBenchmarkRules.Group);
            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardRefresh, CityMaintenanceBenchmarkRules.Group);
            int runtimeRefreshesApplied = 0;
            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardRefreshCaptain, CityMaintenanceBenchmarkRules.Group);
            RefreshGuardIdentity(captain, pKingdom, guardName, pCaptain: true, guardArmy,
                ref runtimeRefreshesApplied, RUNTIME_REFRESH_BATCH_LIMIT);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardRefreshCaptain, CityMaintenanceBenchmarkRules.Group);
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_REFRESH_CURSOR, out int refreshCursor, 0);
            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardRefreshBatch, CityMaintenanceBenchmarkRules.Group);
            for (int i = 0; i < active.Count; i++)
            {
                Actor guard = active[i];
                if (guard == captain) continue;
                if (!RoyalGuardMaintenanceRules.ShouldRefreshGuardInMaintenancePass(
                        pIsCaptain: false,
                        pIsNewlyAppointed: ContainsActor(newlyAppointed, guard?.data?.id ?? -1L),
                        pActorIndex: i,
                        pCursor: refreshCursor,
                        pBatchLimit: RUNTIME_REFRESH_BATCH_LIMIT,
                        pActiveCount: active.Count))
                    continue;
                RefreshGuardIdentity(guard, pKingdom, guardName, pCaptain: false, guardArmy,
                    ref runtimeRefreshesApplied, RUNTIME_REFRESH_BATCH_LIMIT);
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardRefreshBatch, CityMaintenanceBenchmarkRules.Group);
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_REFRESH_CURSOR,
                RoyalGuardMaintenanceRules.NextRefreshCursor(refreshCursor, active.Count, RUNTIME_REFRESH_BATCH_LIMIT));
            FlushGuardGraphics(active, GFX_REBUILD_BUDGET);
            WriteGuardRoster(pKingdom, active);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardRefresh, CityMaintenanceBenchmarkRules.Group);
        }

        // 每趟只重建 pBudget 个标脏禁卫的头像,其余留脏到后续趟,避免编队/改制瞬时把整队头像一次性重建。
        private static void FlushGuardGraphics(List<Actor> pActive, int pBudget)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                if (pActive == null) return;
                int rebuilt = 0;
                foreach (Actor guard in pActive)
                {
                    if (rebuilt >= pBudget) break;
                    if (guard?.data == null || guard.isRekt()) continue;
                    guard.data.get(LineageKeys.ROYAL_GUARD_GFX_DIRTY,
                        out bool dirty, false);
                    if (!RoyalGuardMaintenanceRules.
                            ShouldRebuildGuardGraphicsNow(dirty, rebuilt,
                                pBudget)) continue;
                    guard.clearGraphicsFully();
                    guard.data.set(LineageKeys.ROYAL_GUARD_GFX_DIRTY,
                        false);
                    long actorId = guard.data.id;
                    DeferredRuntimeWorkService.EnqueueCoalesced(
                        DeferredRuntimeWorkRules.CoalescingKey(
                            "guard_archive", actorId),
                        DeferredWorkClass.Persistent,
                        () => ArchiveLivingActor(actorId));
                    rebuilt++;
                }
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.RoyalGuardGraphicsIndex,
                    benchmark);
            }
        }

        public static void OnKingChanged(Kingdom pKingdom, Actor pNewKing)
        {
            if (pKingdom?.data == null) return;
            if (pNewKing == null) return;
            EnsureKingdomGuard(pKingdom, pForce: true);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            List<Actor> guards = CollectActiveGuards(pKingdom);
            for (int i = 0; i < guards.Count; i++)
                DismissGuard(guards[i], "kingdom_fell", pRecord: true,
                    pKeepTrait: false, pUpdateRoster: false);
            ClearKingdomGuardStateHints(pKingdom);
        }

        internal static List<Actor> CaptureForVassalAbsorption(
            Kingdom pKingdom)
        {
            var result = new List<Actor>();
            if (pKingdom?.data == null) return result;
            try
            {
                foreach (Actor actor in pKingdom.units)
                    if (IsRoyalGuard(actor)) result.Add(actor);
            }
            catch { }
            return result;
        }

        internal static void ReleaseAfterVassalAbsorption(
            Kingdom pFormerKingdom, Actor pActor)
        {
            if (pActor?.data == null) return;
            bool formerGuard;
            try
            {
                pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID,
                    out long guardKingdomId, -1L);
                formerGuard = IsRoyalGuard(pActor) ||
                              guardKingdomId == pFormerKingdom?.id;
            }
            catch { return; }
            if (!formerGuard) return;
            RemoveGuardFromRoster(pFormerKingdom, pActor);
            RemoveFromGuardArmy(pActor);
            ClearStaleGuardIdentity(pActor);
        }

        internal static void ClearKingdomGuardStateForAbsorption(
            Kingdom pKingdom)
        {
            ClearKingdomGuardStateHints(pKingdom);
        }

        public static void OnGuardDeath(Actor pActor)
        {
            if (!IsRoyalGuard(pActor)) return;
            DismissGuard(pActor, "died", pRecord: true, pKeepTrait: false);
        }

        public static void DismissGuard(Actor pActor, string pReason)
        {
            DismissGuard(pActor, pReason, pRecord: true, pKeepTrait: false);
        }

        public static bool ReleaseForRegisteredHeir(Kingdom pKingdom,
            Actor pActor, string pReason)
        {
            if (pKingdom?.data == null || pActor?.data == null) return false;
            bool validReason = string.Equals(pReason, "became_heir",
                                   StringComparison.Ordinal) ||
                               string.Equals(pReason, "became_king",
                                   StringComparison.Ordinal);
            if (!validReason) return false;
            if (!HasGuardResidue(pKingdom, pActor)) return true;

            if (IsRoyalGuard(pActor)) DismissGuard(pActor, pReason);
            RemoveGuardFromRoster(pKingdom, pActor);
            if (pActor.kingdom?.data != null &&
                pActor.kingdom != pKingdom)
                RemoveGuardFromRoster(pActor.kingdom, pActor);
            ClearStaleGuardIdentity(pActor);
            RemoveFromAnyArmy(pActor);
            return !HasGuardResidue(pKingdom, pActor);
        }

        public static void StripGuardsFromNormalArmy(Army pArmy)
        {
            if (pArmy == null) return;
            bool guardArmy = IsRoyalGuardArmy(pArmy);
            Kingdom kingdom = null;
            try { kingdom = pArmy.getKingdom(); }
            catch { }
            if (kingdom?.data == null)
            {
                try { kingdom = pArmy.getCity()?.kingdom; }
                catch { }
            }
            if (!RoyalGuardMaintenanceRules.ShouldInspectNormalArmyForGuards(
                    guardArmy, HasKingdomGuardStateHint(kingdom))) return;

            List<long> rosterIds = ReadGuardRosterIds(kingdom);
            int rosterCount = Math.Min(rosterIds.Count, MAX_GUARDS_PER_KINGDOM);
            for (int i = 0; i < rosterCount; i++)
            {
                Actor unit = GetActorById(rosterIds[i]);
                if (unit?.army != pArmy) continue;
                if (!IsRoyalGuard(unit)) continue;
                RemoveFromNormalArmy(unit);
            }

            Actor captain = pArmy.getCaptain();
            if (IsRoyalGuard(captain))
            {
                Actor replacement = PickArmyCaptainReplacement(pArmy);
                if (replacement != null) AWArmyService.SetCaptainIfChanged(pArmy, replacement);
            }
        }

        public static void StripActorFromNormalArmy(Actor pActor)
        {
            if (!IsRoyalGuard(pActor)) return;
            RemoveFromNormalArmy(pActor);
        }

        public static void PrepareArmyCaptain(ref Actor pActor, City pCity)
        {
            if (!IsRoyalGuard(pActor)) return;
            Actor replacement = PickArmyCaptainReplacement(pCity);
            if (replacement != null) pActor = replacement;
        }

        public static bool TryReplaceGuardCaptain(Army pArmy, ref Actor pActor)
        {
            if (_creatingGuardArmy ||
                AWArmyService.IsSpecialArmyCreationInProgress(pArmy) ||
                IsRoyalGuardArmy(pArmy)) return true;
            if (!IsRoyalGuard(pActor)) return true;
            Actor replacement = PickArmyCaptainReplacement(pArmy);
            if (replacement == null) replacement = PickArmyCaptainReplacement(pArmy?.getCity());
            if (replacement == null) return false;
            pActor = replacement;
            return true;
        }

        public static WorldTile GetFollowTile(Actor pGuard)
        {
            if (!IsRoyalGuard(pGuard)) return null;
            Actor king = pGuard.kingdom?.king;
            if (king?.current_tile == null || king.isRekt()) return null;

            RemoveFromNormalArmy(pGuard);
            WorldTile patrolTile = PickPatrolTileAroundKing(pGuard, king.current_tile);
            if (patrolTile == null) return king.current_tile;

            if (pGuard.current_tile != null &&
                Toolbox.SquaredDistTile(pGuard.current_tile, king.current_tile) <= PROTECT_RADIUS * PROTECT_RADIUS &&
                Toolbox.SquaredDistTile(pGuard.current_tile, patrolTile) <= 1)
                return pGuard.current_tile;

            return patrolTile;
        }

        public static Actor FindThreatNearKing(Actor pGuard)
        {
            if (!IsRoyalGuard(pGuard)) return null;
            Kingdom kingdom = pGuard.kingdom;
            Actor king = kingdom?.king;
            if (king?.current_tile == null || king.isRekt()) return null;
            Actor directThreat = GetDirectAttackThreat(pGuard, kingdom, king);
            if (!ShouldSearchThreatsNow(pGuard, kingdom, king, directThreat != null)) return null;
            if (directThreat != null) return directThreat;
            if (!ShouldRunThreatSearch(pGuard)) return null;

            Actor best = null;
            int bestDist = int.MaxValue;
            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardThreatScan,
                CityMaintenanceBenchmarkRules.Group);
            try
            {
                int kingChunkRadius = ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(
                    PROTECT_RADIUS, MAP_CHUNK_SIZE);
                foreach (Actor unit in Finder.getUnitsFromChunk(
                             king.current_tile, kingChunkRadius, PROTECT_RADIUS))
                {
                    if (!IsValidThreatForGuardCore(pGuard, kingdom, king, unit)) continue;
                    int dist = Toolbox.SquaredDistTile(king.current_tile, unit.current_tile);
                    if (dist >= bestDist) continue;
                    bestDist = dist;
                    best = unit;
                }

                int guardChunkRadius = ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(
                    FOLLOW_RADIUS, MAP_CHUNK_SIZE);
                foreach (Actor unit in Finder.getUnitsFromChunk(
                             pGuard.current_tile, guardChunkRadius, FOLLOW_RADIUS))
                {
                    if (!IsValidThreatForGuardCore(pGuard, kingdom, king, unit)) continue;
                    int dist = Toolbox.SquaredDistTile(king.current_tile, unit.current_tile);
                    if (dist >= bestDist) continue;
                    bestDist = dist;
                    best = unit;
                }
            }
            finally
            {
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardThreatScan,
                    CityMaintenanceBenchmarkRules.Group);
            }
            MarkThreatSearchResult(pGuard, best);
            return best;
        }

        public static void WaitAfterGuardFollowIdle(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(RoyalGuardActionRules.FollowIdleWaitMin,
                RoyalGuardActionRules.FollowIdleWaitMax));
        }

        public static void WaitAfterGuardNoThreat(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(RoyalGuardActionRules.NoThreatWaitMin,
                RoyalGuardActionRules.NoThreatWaitMax));
        }

        public static bool IsValidThreatForGuard(Actor pGuard, Actor pTarget)
        {
            if (!IsRoyalGuard(pGuard)) return false;
            if (pGuard?.kingdom == null || pGuard.current_tile == null) return false;
            if (pTarget?.data == null || pTarget.current_tile == null) return false;
            if (pTarget.isRekt() || pTarget.kingdom == null) return false;
            if (!pGuard.kingdom.isEnemy(pTarget.kingdom)) return false;

            Actor king = pGuard.kingdom.king;
            if (king?.current_tile == null || king.isRekt()) return false;
            return IsValidThreatForGuardCore(pGuard, pGuard.kingdom, king, pTarget);
        }

        private static bool IsValidThreatForGuardCore(Actor pGuard, Kingdom pGuardKingdom, Actor pKing, Actor pTarget)
        {
            if (pGuard?.current_tile == null || pGuardKingdom == null || pKing?.current_tile == null) return false;
            if (pTarget?.data == null || pTarget.current_tile == null) return false;
            if (pTarget.isRekt() || pTarget.kingdom == null) return false;
            if (!pGuardKingdom.isEnemy(pTarget.kingdom)) return false;

            if (!pKing.current_tile.isSameIsland(pTarget.current_tile)) return false;

            int distToKing = Toolbox.SquaredDistTile(pKing.current_tile, pTarget.current_tile);
            if (distToKing <= PROTECT_RADIUS * PROTECT_RADIUS) return true;

            int distToGuard = Toolbox.SquaredDistTile(pGuard.current_tile, pTarget.current_tile);
            if (distToGuard <= FOLLOW_RADIUS * FOLLOW_RADIUS) return true;

            return IsAttackerOf(pTarget, pKing) || IsAttackerOf(pTarget, pGuard);
        }

        private static bool ShouldRunThreatSearch(Actor pGuard)
        {
            if (pGuard?.data == null) return false;
            double now = LineageService.CurTime();
            if (ThreatSearchNextAllowed.TryGetValue(pGuard.data.id, out double nextAllowed))
            {
                if (!ActorAiSearchThrottleRules.ShouldSearch(now, nextAllowed)) return false;
                ThreatSearchNextAllowed.Remove(pGuard.data.id);
            }
            if (ThreatSearchNextAllowed.Count > SEARCH_COOLDOWN_PRUNE_THRESHOLD)
                PruneExpiredThreatSearchCooldowns(now);
            return true;
        }

        private static void PruneExpiredThreatSearchCooldowns(double pNow)
        {
            var expired = new List<long>();
            foreach (KeyValuePair<long, double> entry in ThreatSearchNextAllowed)
                if (entry.Value <= pNow)
                    expired.Add(entry.Key);
            foreach (long id in expired)
                ThreatSearchNextAllowed.Remove(id);
        }

        private static bool ShouldSearchThreatsNow(Actor pGuard, Kingdom pKingdom, Actor pKing, bool pDirectAttack)
        {
            bool hasEnemyWar = false;
            try { hasEnemyWar = pKingdom?.data != null && pKingdom.hasEnemies(); }
            catch { hasEnemyWar = false; }

            return RoyalGuardActionRules.ShouldSearchThreats(hasEnemyWar, pDirectAttack);
        }

        private static Actor GetDirectAttackThreat(Actor pGuard, Kingdom pKingdom, Actor pKing)
        {
            Actor threat = GetDirectAttackThreatFromVictim(pKingdom, pKing);
            if (IsValidThreatForGuardCore(pGuard, pKingdom, pKing, threat)) return threat;

            threat = GetDirectAttackThreatFromVictim(pKingdom, pGuard);
            return IsValidThreatForGuardCore(pGuard, pKingdom, pKing, threat) ? threat : null;
        }

        private static Actor GetDirectAttackThreatFromVictim(Kingdom pKingdom, Actor pVictim)
        {
            try
            {
                BaseSimObject source = pVictim?.attackedBy;
                if (source == null || !source.isActor()) return null;
                Actor attacker = source.a;
                if (attacker?.kingdom == null || pKingdom == null) return null;
                return pKingdom.isEnemy(attacker.kingdom) ? attacker : null;
            }
            catch { return null; }
        }

        private static void MarkThreatSearchResult(Actor pGuard, Actor pTarget)
        {
            if (pGuard?.data == null) return;
            long id = pGuard.data.id;
            if (pTarget?.data != null)
            {
                ThreatSearchNextAllowed.Remove(id);
                return;
            }

            ThreatSearchNextAllowed[id] =
                ActorAiSearchThrottleRules.NextAllowedAfterMiss(LineageService.CurTime(), THREAT_SEARCH_MISS_COOLDOWN);
        }

        private static List<Actor> CollectActiveGuards(Kingdom pKingdom)
        {
            var active = new List<Actor>();
            bool hasHint = HasKingdomGuardStateHint(pKingdom);
            Army guardArmy = hasHint ? FindGuardArmy(pKingdom) : null;
            if (RoyalGuardMaintenanceRules.ShouldUseArmyFastPathForActiveGuards(
                    pGuardArmyFound: guardArmy != null,
                    pGuardArmyUnitCount: guardArmy?.countUnits() ?? 0))
            {
                Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardActiveArmyFastPath,
                    CityMaintenanceBenchmarkRules.Group);
                CollectActiveGuardsFromArmy(guardArmy, pKingdom, active);
                WriteGuardRoster(pKingdom, active);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardActiveArmyFastPath,
                    CityMaintenanceBenchmarkRules.Group);
                return active;
            }

            List<long> rosterIds = ReadGuardRosterIds(pKingdom);
            if (RoyalGuardMaintenanceRules.ShouldUseRosterFastPathForActiveGuards(
                    pGuardArmyFound: guardArmy != null,
                    pHasRoster: rosterIds.Count > 0))
            {
                CollectActiveGuardsFromRoster(pKingdom, rosterIds, active);
                return active;
            }

            if (RoyalGuardMaintenanceRules.ShouldClearStaleGuardStateWithoutRoster(
                    pGuardArmyFound: guardArmy != null,
                    pHasRoster: rosterIds.Count > 0,
                    pHasGuardStateHint: hasHint))
                ClearKingdomGuardStateHints(pKingdom);
            return active;
        }

        private static void CollectActiveGuardsFromRoster(Kingdom pKingdom, List<long> pRosterIds, List<Actor> pActive)
        {
            if (pKingdom?.data == null || pRosterIds == null || pActive == null) return;

            var clean = new List<Actor>(Math.Min(pRosterIds.Count, MAX_GUARDS_PER_KINGDOM));
            foreach (long actorId in pRosterIds)
            {
                if (clean.Count >= MAX_GUARDS_PER_KINGDOM) break;
                Actor actor = GetActorById(actorId);
                if (actor?.data == null) continue;
                if (ContainsActor(clean, actor.data.id)) continue;
                if (!HasGuardDataForKingdom(actor, pKingdom)) continue;
                if (!IsStillValidGuard(actor, pKingdom))
                {
                    DismissGuard(actor, "invalid", pRecord: true, pKeepTrait: false, pUpdateRoster: false);
                    continue;
                }

                pActive.Add(actor);
                clean.Add(actor);
                RemoveFromNormalArmy(actor);
            }

            WriteGuardRoster(pKingdom, clean);
        }

        private static void CollectActiveGuardsFromArmy(Army pGuardArmy, Kingdom pKingdom, List<Actor> pActive)
        {
            if (pGuardArmy == null || pActive == null) return;

            var staleActors = new List<Actor>(STALE_GUARD_ARMY_CLEANUP_LIMIT);
            int invalidDismisses = 0;
            AddActiveGuardFromArmy(pGuardArmy.getCaptain(), pKingdom, pActive, pGuardArmy,
                staleActors, ref invalidDismisses);

            int maxScan = RoyalGuardMaintenanceRules.MaxFastPathGuardArmyScan(
                MAX_GUARDS_PER_KINGDOM, RUNTIME_REFRESH_BATCH_LIMIT);
            int scanned = 0;
            List<Actor> units = pGuardArmy.units;
            for (int i = 0; i < units.Count; i++)
            {
                if (RoyalGuardMaintenanceRules.ShouldStopFastPathGuardArmyScan(
                        scanned, pActive.Count, MAX_GUARDS_PER_KINGDOM, maxScan))
                    break;

                scanned++;
                AddActiveGuardFromArmy(units[i], pKingdom, pActive, pGuardArmy,
                    staleActors, ref invalidDismisses);
            }

            foreach (Actor stale in staleActors)
                RemoveStaleActorFromGuardArmy(stale, pGuardArmy);
        }

        private static void AddActiveGuardFromArmy(Actor pActor, Kingdom pKingdom,
            List<Actor> pActive, Army pGuardArmy, List<Actor> pStaleActors, ref int pInvalidDismisses)
        {
            if (pActor?.data == null) return;
            if (ContainsActor(pActive, pActor.data.id)) return;
            if (!HasGuardDataForKingdom(pActor, pKingdom))
            {
                if (RoyalGuardMaintenanceRules.ShouldRemoveStaleActorFromGuardArmy(
                        pHasGuardDataForKingdom: false,
                        pActorArmyIsGuardArmy: pActor.army == pGuardArmy,
                        pRemovedCount: pStaleActors?.Count ?? 0,
                        pRemovalLimit: STALE_GUARD_ARMY_CLEANUP_LIMIT))
                    pStaleActors.Add(pActor);
                return;
            }

            if (!IsStillValidGuard(pActor, pKingdom))
            {
                if (pInvalidDismisses < STALE_GUARD_ARMY_CLEANUP_LIMIT)
                {
                    pInvalidDismisses++;
                    DismissGuard(pActor, "invalid", pRecord: true, pKeepTrait: false, pUpdateRoster: false);
                }
                return;
            }
            pActive.Add(pActor);
        }

        private static bool HasGuardDataForKingdom(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            pActor.data.get(LineageKeys.ROYAL_GUARD, out bool guardFlag, false);
            pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID, out long guardKingdomId, -1L);
            return RoyalGuardMaintenanceRules.HasGuardDataForKingdom(
                guardFlag,
                guardKingdomId,
                pActor.kingdom?.id ?? -1L,
                pKingdom.id);
        }

        private static bool ContainsActor(List<Actor> pActors, long pActorId)
        {
            if (pActors == null || pActorId < 0) return false;
            foreach (Actor actor in pActors)
                if (actor?.data != null && actor.data.id == pActorId)
                    return true;
            return false;
        }

        private static List<long> ReadGuardRosterIds(Kingdom pKingdom)
        {
            var result = new List<long>(MAX_GUARDS_PER_KINGDOM);
            if (pKingdom?.data == null) return result;
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_ROSTER_IDS, out string raw, "");
            if (string.IsNullOrEmpty(raw)) return result;

            string[] parts = raw.Split(',');
            foreach (string part in parts)
            {
                if (result.Count >= MAX_GUARDS_PER_KINGDOM * 2) break;
                if (!long.TryParse(part, out long id)) continue;
                if (id < 0 || result.Contains(id)) continue;
                result.Add(id);
            }
            return result;
        }

        private static void WriteGuardRoster(Kingdom pKingdom, List<Actor> pActors)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                if (pKingdom?.data == null) return;
                if (pActors == null || pActors.Count == 0)
                {
                    pKingdom.data.get(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                        out string currentEmpty, "");
                    if (RoyalGuardMaintenanceRules.ShouldWriteGuardRoster(
                            currentEmpty, ""))
                        pKingdom.data.set(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                            "");
                    return;
                }

                var ids = new List<string>(Math.Min(pActors.Count,
                    MAX_GUARDS_PER_KINGDOM));
                foreach (Actor actor in pActors)
                {
                    if (actor?.data == null || actor.data.id < 0) continue;
                    if (ids.Count >= MAX_GUARDS_PER_KINGDOM) break;
                    string id = actor.data.id.ToString();
                    if (ids.Contains(id)) continue;
                    ids.Add(id);
                }
                string next = string.Join(",", ids.ToArray());
                pKingdom.data.get(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                    out string current, "");
                if (RoyalGuardMaintenanceRules.ShouldWriteGuardRoster(
                        current, next))
                    pKingdom.data.set(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                        next);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.RoyalGuardRosterIndex,
                    benchmark);
            }
        }

        private static void AddGuardToRoster(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            List<long> ids = ReadGuardRosterIds(pKingdom);
            if (!ids.Contains(pActor.data.id))
                ids.Add(pActor.data.id);
            WriteGuardRosterIds(pKingdom, ids);
        }

        private static void RemoveGuardFromRoster(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            List<long> ids = ReadGuardRosterIds(pKingdom);
            if (!ids.Remove(pActor.data.id)) return;
            WriteGuardRosterIds(pKingdom, ids);
        }

        private static void WriteGuardRosterIds(Kingdom pKingdom, List<long> pIds)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                if (pKingdom?.data == null) return;
                if (pIds == null || pIds.Count == 0)
                {
                    pKingdom.data.get(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                        out string currentEmpty, "");
                    if (RoyalGuardMaintenanceRules.ShouldWriteGuardRoster(
                            currentEmpty, ""))
                        pKingdom.data.set(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                            "");
                    return;
                }

                var ids = new List<string>(Math.Min(pIds.Count,
                    MAX_GUARDS_PER_KINGDOM));
                foreach (long id in pIds)
                {
                    if (id < 0) continue;
                    if (ids.Count >= MAX_GUARDS_PER_KINGDOM) break;
                    string text = id.ToString();
                    if (ids.Contains(text)) continue;
                    ids.Add(text);
                }
                string next = string.Join(",", ids.ToArray());
                pKingdom.data.get(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                    out string current, "");
                if (RoyalGuardMaintenanceRules.ShouldWriteGuardRoster(
                        current, next))
                    pKingdom.data.set(LineageKeys.ROYAL_GUARD_ROSTER_IDS,
                        next);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.RoyalGuardRosterIndex,
                    benchmark);
            }
        }

        private static void RemoveStaleActorFromGuardArmy(Actor pActor, Army pGuardArmy)
        {
            if (pActor?.data == null || pGuardArmy == null) return;
            try { pGuardArmy.units.Remove(pActor); }
            catch { }
            if (pActor.army != pGuardArmy) return;
            try { pActor.setArmy(null); }
            catch { }
        }

        private static bool IsStillValidGuard(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom) return false;
            pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID, out long guardKingdomId, -1L);
            if (guardKingdomId >= 0 && guardKingdomId != pKingdom.id) return false;
            if (pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (DynasticReproductionService
                .ShouldProtectFromOrdinaryMilitaryService(pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            return true;
        }

        private static WorldTile PickPatrolTileAroundKing(Actor pGuard, WorldTile pKingTile)
        {
            if (pGuard?.data == null || pKingTile == null) return null;

            int width = PATROL_MAX_RADIUS * 2 + 1;
            int total = width * width;
            int phase = (int)(LineageService.CurTime() / PATROL_PHASE_INTERVAL);
            int seed = unchecked((int)(pGuard.data.id * 31L + phase * 17L)) & int.MaxValue;

            for (int i = 0; i < total; i++)
            {
                int idx = (seed + i) % total;
                int dx = idx % width - PATROL_MAX_RADIUS;
                int dy = idx / width - PATROL_MAX_RADIUS;
                int distSq = dx * dx + dy * dy;
                if (distSq < PATROL_MIN_RADIUS * PATROL_MIN_RADIUS ||
                    distSq > PATROL_MAX_RADIUS * PATROL_MAX_RADIUS)
                    continue;

                WorldTile tile = World.world.GetTile(pKingTile.x + dx, pKingTile.y + dy);
                if (IsGoodGuardPatrolTile(tile, pKingTile)) return tile;
            }

            return pKingTile.getNeighbourTileSameIsland();
        }

        private static bool IsGoodGuardPatrolTile(WorldTile pTile, WorldTile pKingTile)
        {
            if (pTile == null || pKingTile == null) return false;
            if (!pTile.isSameIsland(pKingTile)) return false;
            if (pTile.Type == null) return false;
            if (!pTile.Type.ground || pTile.Type.liquid || pTile.Type.lava || pTile.Type.block) return false;
            return true;
        }

        private static List<GuardCandidate> CollectCandidates(Kingdom pKingdom, List<Actor> pActive, float pAverageWarriorScore)
        {
            var nobles = new List<GuardCandidate>(NOBLE_CANDIDATE_POOL_LIMIT);
            var common = new List<GuardCandidate>(CANDIDATE_POOL_LIMIT);
            HashSet<long> activeIds = BuildActiveIdSet(pActive);
            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardCandidateScan, CityMaintenanceBenchmarkRules.Group);
            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardCandidateScore, CityMaintenanceBenchmarkRules.Group);
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_CANDIDATE_SCAN_CURSOR, out int cursor, 0);
            if (cursor < 0) cursor = 0;
            List<Actor> units = pKingdom.units;
            int unitCount = units.Count;
            if (cursor >= unitCount) cursor = 0;
            int scanned = Math.Min(CANDIDATE_SCAN_LIMIT, Math.Max(0, unitCount - cursor));
            for (int i = 0; i < scanned; i++)
            {
                Actor unit = units[cursor + i];
                if (unit?.data == null || activeIds.Contains(unit.data.id)) continue;
                if (!IsGuardCandidate(unit, pKingdom)) continue;
                float score = CombatScore(unit);
                if (pAverageWarriorScore > 0f && score <= pAverageWarriorScore) continue;
                var candidate = new GuardCandidate
                {
                    actor = unit,
                    score = score,
                    noble = ChronicleGate.IsNobleActor(unit)
                };
                AddBoundedCandidate(candidate.noble ? nobles : common, candidate,
                    candidate.noble ? NOBLE_CANDIDATE_POOL_LIMIT : CANDIDATE_POOL_LIMIT);
            }
            bool complete = cursor + scanned >= unitCount;
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_CANDIDATE_SCAN_CURSOR,
                RoyalGuardMaintenanceRules.NextBoundedScanCursor(cursor, scanned, complete));
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardCandidateScore, CityMaintenanceBenchmarkRules.Group);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardCandidateScan, CityMaintenanceBenchmarkRules.Group);

            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardCandidateSort, CityMaintenanceBenchmarkRules.Group);
            var result = new List<GuardCandidate>(nobles.Count + common.Count);
            result.AddRange(nobles);
            result.AddRange(common);
            result.Sort((a, b) => b.score.CompareTo(a.score));
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardCandidateSort, CityMaintenanceBenchmarkRules.Group);
            return result;
        }

        private static HashSet<long> BuildActiveIdSet(List<Actor> pActive)
        {
            var result = new HashSet<long>();
            if (pActive == null) return result;
            foreach (Actor actor in pActive)
            {
                if (actor?.data == null) continue;
                result.Add(actor.data.id);
            }
            return result;
        }

        private static void AddBoundedCandidate(List<GuardCandidate> pPool, GuardCandidate pCandidate, int pLimit)
        {
            if (pPool == null || pCandidate?.actor == null) return;
            int limit = pLimit <= 0 ? 1 : pLimit;
            if (pPool.Count < limit)
            {
                pPool.Add(pCandidate);
                return;
            }

            int weakestIndex = FindWeakestCandidateIndex(pPool);
            if (weakestIndex < 0) return;
            float weakestScore = pPool[weakestIndex].score;
            if (!RoyalGuardMaintenanceRules.ShouldKeepBoundedCandidate(
                    pPool.Count, limit, weakestScore, pCandidate.score))
                return;
            pPool[weakestIndex] = pCandidate;
        }

        private static int FindWeakestCandidateIndex(List<GuardCandidate> pPool)
        {
            if (pPool == null || pPool.Count == 0) return -1;
            int weakestIndex = 0;
            float weakestScore = pPool[0].score;
            for (int i = 1; i < pPool.Count; i++)
            {
                if (pPool[i].score >= weakestScore) continue;
                weakestScore = pPool[i].score;
                weakestIndex = i;
            }
            return weakestIndex;
        }

        private static bool IsGuardCandidate(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (!RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pActor))) return false;
            if (!HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.RoyalGuard)) return false;
            if (DynasticReproductionService
                .ShouldProtectFromOrdinaryMilitaryService(pActor)) return false;
            return RoyalGuardSelectionRules.IsEligibleCore(
                isXia: LineageService.IsXia(pActor),
                sameKingdom: pActor.kingdom == pKingdom,
                isMale: pActor.isSexMale(),
                isBoat: pActor.asset.is_boat,
                isRekt: pActor.isRekt(),
                isAdult: pActor.isAdult(),
                isKing: pActor.isKing(),
                isCityLeader: pActor.isCityLeader(),
                isSlave: SlaveService.IsSlave(pActor),
                isRetiredSoldier: SlaveService.IsRetiredSoldier(pActor),
                isCurrentHeir: HeirService.IsCurrentHeir(pKingdom, pActor),
                hasMadness: pActor.hasTrait("madness"),
                isHistoricalFigure: pActor.hasTrait("figure") || pActor.hasTrait("first"));
        }

        private static void FillNobleQuota(Kingdom pKingdom, List<Actor> pActive,
            List<GuardCandidate> pCandidates, int pTargetNobles, string pGuardName, List<Actor> pNewlyAppointed)
        {
            while (CountNobles(pActive) < pTargetNobles)
            {
                GuardCandidate next = TakeBestCandidate(pCandidates, pNobleOnly: true);
                if (next == null) return;
                AppointGuard(next.actor, pKingdom, pGuardName, pCaptain: false);
                pNewlyAppointed?.Add(next.actor);
                pActive.Add(next.actor);
            }
        }

        private static void FillGuardSlots(Kingdom pKingdom, List<Actor> pActive,
            List<GuardCandidate> pCandidates, int pDesired, string pGuardName, List<Actor> pNewlyAppointed)
        {
            while (pActive.Count < pDesired)
            {
                GuardCandidate next = TakeBestCandidate(pCandidates, pNobleOnly: false);
                if (next == null) return;
                AppointGuard(next.actor, pKingdom, pGuardName, pCaptain: false);
                pNewlyAppointed?.Add(next.actor);
                pActive.Add(next.actor);
            }
        }

        private static GuardCandidate TakeBestCandidate(List<GuardCandidate> pCandidates, bool pNobleOnly)
        {
            for (int i = 0; i < pCandidates.Count; i++)
            {
                GuardCandidate candidate = pCandidates[i];
                if (pNobleOnly && !candidate.noble) continue;
                pCandidates.RemoveAt(i);
                return candidate;
            }
            return null;
        }

        private static int TrimExcessGuards(List<Actor> pActive, int pDesired,
            int pRemainingBudget)
        {
            if (!RoyalGuardOfficeRules.CanTrimLifetimeGuard()) return 0;
            int limit = Math.Min(pDesired, MAX_GUARDS_PER_KINGDOM);
            int trimCount = RoyalGuardMaintenanceRules.TrimExcessCountForPass(
                pActive.Count, pDesired, MAX_GUARDS_PER_KINGDOM, pRemainingBudget);
            if (trimCount <= 0) return 0;

            pActive.Sort((a, b) => CombatScore(b).CompareTo(CombatScore(a)));
            int dismissed = 0;
            for (int i = pActive.Count - 1; i >= limit && dismissed < trimCount; i--)
            {
                Actor guard = pActive[i];
                pActive.RemoveAt(i);
                DismissGuard(guard, "over_limit");
                dismissed++;
            }
            return dismissed;
        }

        private static Actor PickCaptain(List<Actor> pActive)
        {
            Actor existing = PickExistingCaptain(pActive);
            if (existing != null) return existing;

            Actor best = null;
            float bestScore = float.MinValue;
            foreach (Actor guard in pActive)
            {
                if (!ChronicleGate.IsNobleActor(guard)) continue;
                float score = CombatScore(guard);
                if (score <= bestScore) continue;
                bestScore = score;
                best = guard;
            }
            return best;
        }

        private static Actor PickExistingCaptain(List<Actor> pActive)
        {
            Actor best = null;
            float bestScore = float.MinValue;
            foreach (Actor guard in pActive)
            {
                if (guard?.data == null) continue;
                guard.data.get(LineageKeys.ROYAL_GUARD_CAPTAIN, out bool captain, false);
                bool noble = ChronicleGate.IsNobleActor(guard);
                if (!RoyalGuardMaintenanceRules.ShouldKeepExistingCaptain(captain, noble)) continue;
                float score = CombatScore(guard);
                if (score <= bestScore) continue;
                bestScore = score;
                best = guard;
            }
            return best;
        }

        private static void AppointGuard(Actor pActor, Kingdom pKingdom, string pGuardName, bool pCaptain)
        {
            if (!HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.RoyalGuard)) return;
            if (!RoyalGuardMaintenanceRules.ShouldPersistNewGuardDuringFill(pFinalRefreshWillRun: true)) return;
            int runtimeRefreshesApplied = 0;
            RefreshGuardIdentity(pActor, pKingdom, pGuardName, pCaptain, null,
                ref runtimeRefreshesApplied, RUNTIME_REFRESH_BATCH_LIMIT);
        }

        private static void RefreshGuardIdentity(Actor pActor, Kingdom pKingdom, string pGuardName, bool pCaptain,
            Army pGuardArmy, ref int pRuntimeRefreshesApplied, int pRuntimeRefreshLimit)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;

            pActor.data.get(LineageKeys.ROYAL_GUARD, out bool guardFlag, false);
            pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID, out long guardKingdomId, -1L);
            pActor.data.get(LineageKeys.ROYAL_GUARD_CAPTAIN, out bool wasCaptain, false);
            pActor.data.get(LineageKeys.ROYAL_GUARD_NAME, out string previousGuardName, "");
            bool hasTrait = pActor.hasTrait(LineageKeys.TRAIT_GUARD);
            bool wasGuard = guardFlag || guardKingdomId >= 0 || hasTrait;
            bool armyChanged = pGuardArmy != null
                ? pActor.army != pGuardArmy
                : pActor.hasArmy() && !IsRoyalGuardArmy(pActor.army);
            bool professionChanged = !pActor.isWarrior();
            bool jobChanged = GuardContent.KingGuardJob != null && !IsKingGuardJob(pActor);
            bool persistRefresh = RoyalGuardMaintenanceRules.ShouldPersistGuardIdentityRefresh(
                pWasGuard: wasGuard,
                pWasCaptain: wasCaptain,
                pCaptain: pCaptain,
                pKingdomChanged: guardKingdomId != pKingdom.id,
                pNameChanged: (previousGuardName ?? "") != (pGuardName ?? ""),
                pMissingTrait: !hasTrait,
                pArmyChanged: armyChanged,
                pProfessionChanged: professionChanged,
                pJobChanged: jobChanged);
            bool runtimeRefresh = RoyalGuardMaintenanceRules.ShouldApplyGuardRuntimeRefresh(
                pArmyChanged: armyChanged,
                pProfessionChanged: professionChanged,
                pJobChanged: jobChanged);
            if (!persistRefresh && !runtimeRefresh) return;
            if (!RoyalGuardMaintenanceRules.ShouldApplyRuntimeRefreshNow(
                    persistRefresh, runtimeRefresh, pRuntimeRefreshesApplied, pRuntimeRefreshLimit))
                return;

            if (persistRefresh)
            {
                Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardRefreshPersist,
                    CityMaintenanceBenchmarkRules.Group);
                pActor.data.set(LineageKeys.ROYAL_GUARD, true);
                pActor.data.set(LineageKeys.ROYAL_GUARD_CAPTAIN, pCaptain);
                pActor.data.set(LineageKeys.ROYAL_GUARD_KINGDOM_ID, pKingdom.id);
                pActor.data.set(LineageKeys.ROYAL_GUARD_NAME, pGuardName ?? "");

                if (!pActor.hasTrait(LineageKeys.TRAIT_GUARD))
                    pActor.addTrait(LineageKeys.TRAIT_GUARD);
                AddGuardToRoster(pKingdom, pActor);
            }

            if (runtimeRefresh)
            {
                Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardRefreshRuntime,
                    CityMaintenanceBenchmarkRules.Group);
                if (armyChanged)
                    AssignToGuardArmy(pActor, pGuardArmy);
                if (professionChanged && !pActor.isWarrior())
                {
                    try { pActor.setProfession(UnitProfession.Warrior); } catch { }
                }
                if (jobChanged && GuardContent.KingGuardJob != null)
                    pActor.setCitizenJob(GuardContent.KingGuardJob);
                pRuntimeRefreshesApplied++;
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardRefreshRuntime,
                    CityMaintenanceBenchmarkRules.Group);
            }

            if (!persistRefresh) return;

            bool expensiveRefresh = RoyalGuardMaintenanceRules.ShouldRecordExpensiveIdentityRefresh(
                pWasGuard: wasGuard,
                pWasCaptain: wasCaptain,
                pCaptain: pCaptain,
                pMissingTrait: !hasTrait,
                pKingdomChanged: guardKingdomId != pKingdom.id,
                pNameChanged: (previousGuardName ?? "") != (pGuardName ?? ""));
            GuardStateSnapshot stateSnapshot = CaptureGuardState(pActor, pActive: true, pCaptain,
                ChronicleGate.IsNobleActor(pActor), pGuardName, "");
            ChronicleActorSnapshot chronicleSnapshot = ChronicleActorSnapshot.Capture(
                pActor, pKingdom, pActor.city);
            EnqueueGuardState(stateSnapshot);
            if (expensiveRefresh)
                // 极贵的头像重建(clearGraphicsFully)+ 归档改到有预算的 FlushGuardGraphics 里做,先标脏。
                pActor.data.set(LineageKeys.ROYAL_GUARD_GFX_DIRTY, true);

            if (!wasGuard || wasCaptain != pCaptain)
                DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent,
                    () => ChronicleEvents.OnRoyalGuardAppointed(chronicleSnapshot, pGuardName, pCaptain));

            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardRefreshPersist,
                CityMaintenanceBenchmarkRules.Group);
        }

        private static bool DismissKingdomGuards(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null) return true;
            if (!RoyalGuardMaintenanceRules.ShouldScanKingdomForDismiss(
                    pHasCollectedActiveList: false,
                    pActiveGuardCount: 0,
                    pHasGuardStateHint: HasKingdomGuardStateHint(pKingdom)))
                return true;

            Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuardDismiss, CityMaintenanceBenchmarkRules.Group);
            Army guardArmy = FindGuardArmy(pKingdom);
            if (guardArmy != null)
            {
                var active = new List<Actor>();
                CollectActiveGuardsFromArmy(guardArmy, pKingdom, active);
                int dismissCount = RoyalGuardMaintenanceRules.DismissCountForPass(
                    active.Count, DISMISS_BATCH_LIMIT);
                for (int i = 0; i < dismissCount; i++)
                    DismissGuard(active[i], pReason, pRecord: true, pKeepTrait: false, pUpdateRoster: false);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardDismiss, CityMaintenanceBenchmarkRules.Group);
                return active.Count <= dismissCount;
            }

            List<long> rosterIds = ReadGuardRosterIds(pKingdom);
            if (RoyalGuardMaintenanceRules.ShouldUseRosterForDismiss(rosterIds.Count > 0))
            {
                var active = new List<Actor>(rosterIds.Count);
                foreach (long actorId in rosterIds)
                {
                    Actor guard = GetActorById(actorId);
                    if (IsRoyalGuard(guard)) active.Add(guard);
                }
                int dismissCount = RoyalGuardMaintenanceRules.DismissCountForPass(
                    active.Count, DISMISS_BATCH_LIMIT);
                for (int i = 0; i < dismissCount; i++)
                    DismissGuard(active[i], pReason, pRecord: true, pKeepTrait: false, pUpdateRoster: false);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardDismiss, CityMaintenanceBenchmarkRules.Group);
                return active.Count <= dismissCount;
            }

            bool complete = DismissKingdomGuardsBounded(pKingdom, pReason);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuardDismiss, CityMaintenanceBenchmarkRules.Group);
            return complete;
        }

        private static bool DismissKingdomGuardsBounded(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null) return true;
            if (!RoyalGuardMaintenanceRules.ShouldUseBoundedDismissScan(
                    pGuardArmyFound: false,
                    pHasGuardStateHint: HasKingdomGuardStateHint(pKingdom)))
                return true;

            pKingdom.data.get(LineageKeys.ROYAL_GUARD_DISMISS_CURSOR, out int cursor, 0);
            if (cursor < 0) cursor = 0;

            var toDismiss = new List<Actor>(DISMISS_BATCH_LIMIT);
            List<Actor> units = pKingdom.units;
            int unitCount = units.Count;
            if (cursor >= unitCount) cursor = 0;
            int end = Math.Min(unitCount, cursor + DISMISS_SCAN_LIMIT);
            for (int i = cursor; i < end; i++)
            {
                Actor unit = units[i];
                if (IsRoyalGuard(unit))
                {
                    toDismiss.Add(unit);
                    if (toDismiss.Count >= DISMISS_BATCH_LIMIT)
                    {
                        pKingdom.data.set(LineageKeys.ROYAL_GUARD_DISMISS_CURSOR, i + 1);
                        foreach (Actor guard in toDismiss)
                            DismissGuard(guard, pReason, pRecord: true, pKeepTrait: false,
                                pUpdateRoster: false);
                        return false;
                    }
                }
            }

            foreach (Actor guard in toDismiss)
                DismissGuard(guard, pReason, pRecord: true, pKeepTrait: false, pUpdateRoster: false);

            if (end < unitCount)
            {
                pKingdom.data.set(LineageKeys.ROYAL_GUARD_DISMISS_CURSOR, end);
                return false;
            }
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_DISMISS_CURSOR, 0);
            return true;
        }

        internal static bool HasKingdomGuardStateHint(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_RECORDED, out bool recorded, false);
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_ARMY_ID, out long armyId, -1L);
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_ROSTER_IDS, out string roster, "");
            return recorded || armyId >= 0 || !string.IsNullOrEmpty(roster);
        }

        private static void ClearKingdomGuardStateHints(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_RECORDED, false);
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_ARMY_ID, -1L);
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_ROSTER_IDS, "");
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_DISMISS_CURSOR, 0);
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_CANDIDATE_SCAN_CURSOR, 0);
        }

        private static void DismissGuard(Actor pActor, string pReason, bool pRecord, bool pKeepTrait)
        {
            DismissGuard(pActor, pReason, pRecord, pKeepTrait, pUpdateRoster: true);
        }

        private static void DismissGuard(Actor pActor, string pReason, bool pRecord, bool pKeepTrait,
            bool pUpdateRoster)
        {
            if (pActor?.data == null || !IsRoyalGuard(pActor)) return;
            if (!RoyalGuardOfficeRules.CanEndLifetimeGuardService(pReason))
                return;

            ThreatSearchNextAllowed.Remove(pActor.data.id);

            Kingdom kingdom = pActor.kingdom;
            City city = pActor.city;
            pActor.data.get(LineageKeys.ROYAL_GUARD_NAME, out string guardName, BuildGuardName(kingdom));
            pActor.data.get(LineageKeys.ROYAL_GUARD_CAPTAIN, out bool wasCaptain, false);
            ChronicleActorSnapshot chronicleSnapshot = ChronicleActorSnapshot.Capture(pActor, kingdom, city);

            pActor.data.set(LineageKeys.ROYAL_GUARD, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_CAPTAIN, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_GUARD_NAME, "");
            if (pUpdateRoster)
                RemoveGuardFromRoster(kingdom, pActor);
            if (!pKeepTrait && pActor.hasTrait(LineageKeys.TRAIT_GUARD))
                pActor.removeTrait(LineageKeys.TRAIT_GUARD);
            ClearGuardCitizenJob(pActor);
            RemoveFromGuardArmy(pActor);

            GuardStateSnapshot stateSnapshot = CaptureGuardState(pActor, pActive: false, wasCaptain,
                ChronicleGate.IsNobleActor(pActor), guardName, pReason);
            EnqueueGuardState(stateSnapshot);
            if (pRecord)
                DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent,
                    () => ChronicleEvents.OnRoyalGuardDismissed(chronicleSnapshot, pReason));

            if (!pActor.isRekt())
            {
                long actorId = pActor.data.id;
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    DeferredRuntimeWorkRules.CoalescingKey("guard_gfx", actorId),
                    DeferredWorkClass.Runtime, () => RefreshActorGraphics(actorId));
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    DeferredRuntimeWorkRules.CoalescingKey("guard_archive", actorId),
                    DeferredWorkClass.Persistent, () => ArchiveLivingActor(actorId));
            }
        }

        private static void ClearStaleGuardIdentity(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.ROYAL_GUARD, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_CAPTAIN, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_GUARD_NAME, "");
            if (pActor.hasTrait(LineageKeys.TRAIT_GUARD))
                pActor.removeTrait(LineageKeys.TRAIT_GUARD);
            ClearGuardCitizenJob(pActor);
            if (!pActor.isRekt())
                pActor.clearGraphicsFully();
        }

        private static void ClearGuardCitizenJob(Actor pActor)
        {
            if (!IsKingGuardJob(pActor)) return;
            try { pActor.endJob(); }
            catch { pActor.citizen_job = null; }
        }

        private static bool IsKingGuardJob(Actor pActor)
        {
            return pActor?.citizen_job != null &&
                   pActor.citizen_job.id == GuardContent.CITIZEN_JOB_KING_GUARD;
        }

        private static bool HasGuardResidue(Kingdom pKingdom,
            Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.ROYAL_GUARD,
                out bool guard, false);
            pActor.data.get(LineageKeys.ROYAL_GUARD_CAPTAIN,
                out bool captain, false);
            pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID,
                out long guardKingdomId, -1L);
            return guard || captain || guardKingdomId >= 0L ||
                   pActor.hasTrait(LineageKeys.TRAIT_GUARD) ||
                   IsKingGuardJob(pActor) ||
                   IsInGuardRoster(pKingdom, pActor) ||
                   IsAssignedToGuardArmy(pActor);
        }

        private static bool IsInGuardRoster(Kingdom pKingdom,
            Actor pActor)
        {
            return pKingdom?.data != null && pActor?.data != null &&
                   ReadGuardRosterIds(pKingdom).Contains(pActor.data.id);
        }

        private static bool IsAssignedToGuardArmy(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.hasArmy() &&
                       IsRoyalGuardArmy(pActor.army);
            }
            catch { return false; }
        }

        private static void RemoveFromAnyArmy(Actor pActor)
        {
            if (pActor?.data == null || !pActor.hasArmy()) return;
            Army army = pActor.army;
            try { pActor.removeFromArmy(); }
            catch { pActor.setArmy(null); }
            AWArmyService.TryRemoveEmptyArmy(army);
        }

        private static void RemoveFromNormalArmy(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (!pActor.hasArmy()) return;
            if (IsRoyalGuardArmy(pActor.army)) return;
            try { pActor.removeFromArmy(); }
            catch { pActor.setArmy(null); }
        }

        private static void AssignToGuardArmy(Actor pActor, Army pGuardArmy)
        {
            if (pActor?.data == null) return;
            if (pGuardArmy == null)
            {
                RemoveFromNormalArmy(pActor);
                return;
            }

            if (pActor.army == pGuardArmy) return;
            if (pActor.hasArmy())
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
            }
            AWArmyService.AddToArmy(pActor, pGuardArmy);
        }

        private static void RemoveFromGuardArmy(Actor pActor)
        {
            if (pActor?.data == null || !pActor.hasArmy()) return;
            Army army = pActor.army;
            if (!IsRoyalGuardArmy(army)) return;
            try { pActor.removeFromArmy(); }
            catch { pActor.setArmy(null); }
            AWArmyService.TryRemoveEmptyArmy(army);
        }

        private static Army EnsureGuardArmy(Kingdom pKingdom, Actor pCaptain, string pGuardName)
        {
            if (pKingdom?.data == null || pCaptain?.data == null) return null;

            Army army = AWArmyService.EnsureArmy(pKingdom, pCaptain.city ?? pKingdom.capital, pCaptain,
                AWArmyRole.RoyalGuard, pGuardName, pDetached: true);
            if (army == null) return null;

            AWArmyService.DetachArmyFromCity(army,
                pCaptain.city ?? pKingdom.capital);
            if (army.data != null)
            {
                army.data.custom_name = true;
                if (army.data.name != pGuardName)
                    army.setName(pGuardName);
            }
            if (!pCaptain.isRekt())
                AWArmyService.SetCaptainIfChanged(army, pCaptain);

            pKingdom.data.set(LineageKeys.ROYAL_GUARD_ARMY_ID, army.id);
            return army;
        }

        private static Army FindGuardArmy(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || World.world?.armies == null) return null;

            pKingdom.data.get(LineageKeys.ROYAL_GUARD_ARMY_ID, out long armyId, -1L);
            Army stored = GetArmyById(armyId);
            if (IsRoyalGuardArmyForKingdom(stored, pKingdom)) return stored;

            Army army = AWArmyService.FindArmy(pKingdom, pKingdom.capital, AWArmyRole.RoyalGuard);
            if (IsRoyalGuardArmyForKingdom(army, pKingdom))
            {
                pKingdom.data.set(LineageKeys.ROYAL_GUARD_ARMY_ID, army.id);
                return army;
            }

            return null;
        }

        private static bool IsRoyalGuardArmyForKingdom(Army pArmy, Kingdom pKingdom)
        {
            if (!IsRoyalGuardArmy(pArmy)) return false;
            try
            {
                if (pArmy.getKingdom() == pKingdom) return true;
            }
            catch { }

            try
            {
                if (pArmy.data?.id_kingdom == pKingdom?.id) return true;
            }
            catch { }

            City anchor = AWArmyService.FindAnchorCity(pArmy);
            return anchor?.kingdom == pKingdom;
        }

        private static Army GetArmyById(long pArmyId)
        {
            if (pArmyId < 0 || World.world?.armies == null) return null;
            try { return World.world.armies.get(pArmyId); }
            catch { return null; }
        }

        private static Actor GetActorById(long pActorId)
        {
            if (pActorId < 0 || World.world?.units == null) return null;
            try { return World.world.units.get(pActorId); }
            catch { return null; }
        }

        private static bool IsRoyalGuardArmy(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            if (AWArmyService.IsRoleArmy(pArmy, AWArmyRole.RoyalGuard)) return true;
            if (pArmy.hasCity()) return false;
            string name = pArmy.data.name ?? "";
            if (name.Contains("\u7981\u536B\u519B")) return true;
            Actor captain = pArmy.getCaptain();
            return IsRoyalGuard(captain);
        }

        private static Army CreateDetachedGuardArmy(Kingdom pKingdom, Actor pCaptain, string pGuardName)
        {
            if (NewArmyObjectMethod == null || World.world?.armies == null) return null;
            City city = pCaptain?.city ?? pKingdom?.capital;
            if (city?.data == null) return null;

            Army army = null;
            bool initialized = false;
            try
            {
                army = NewArmyObjectMethod.Invoke(World.world.armies, null) as Army;
                if (army == null) return null;

                _creatingGuardArmy = true;
                try { army.createArmy(pCaptain, city); }
                finally { _creatingGuardArmy = false; }

                initialized = army.data != null;
                if (!initialized) return null;

                AWArmyService.DetachArmyFromCity(army, city);
                army.data.custom_name = true;
                army.setName(pGuardName);
                return army;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Create royal guard army failed: " + e.Message);
                _creatingGuardArmy = false;
                return null;
            }
            finally
            {
                if (ArmyCreationSafetyRules.ShouldCleanupFailedCreation(
                        army != null, initialized))
                    ArmyInvalidCleanupQueue.RemoveFailedCreation(army,
                        pCaptain, city);
            }
        }

        private static void TryRemoveEmptyGuardArmy(Army pArmy)
        {
            if (!IsRoyalGuardArmy(pArmy)) return;
            if (pArmy.countUnits() > 0 || pArmy.hasCaptain()) return;
            try { World.world?.armies?.removeObject(pArmy); }
            catch { }
        }

        private static MethodInfo ResolveNewArmyObjectMethod()
        {
            Type type = typeof(ArmyManager);
            while (type != null)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (method.Name != "newObject") continue;
                    if (method.GetParameters().Length == 0) return method;
                }
                type = type.BaseType;
            }
            return null;
        }

        private static Actor PickArmyCaptainReplacement(Army pArmy)
        {
            if (pArmy == null) return null;
            foreach (Actor unit in pArmy.getUnits())
            {
                if (CanBeNormalArmyCaptain(unit)) return unit;
            }
            return null;
        }

        private static Actor PickArmyCaptainReplacement(City pCity)
        {
            if (pCity?.data == null) return null;
            foreach (Actor unit in pCity.getUnits())
            {
                if (CanBeNormalArmyCaptain(unit)) return unit;
            }
            return null;
        }

        private static bool CanBeNormalArmyCaptain(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            if (!pActor.isWarrior()) return false;
            if (IsRoyalGuard(pActor)) return false;
            if (SlaveService.IsSlave(pActor)) return false;
            return true;
        }

        private static void EnsureFormationRecorded(Kingdom pKingdom, string pGuardName)
        {
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_RECORDED, out bool recorded, false);
            if (recorded) return;
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_RECORDED, true);
            ChronicleEvents.OnRoyalGuardFormed(pKingdom, pGuardName);
        }

        private static string BuildGuardName(Kingdom pKingdom)
        {
            string kingdomName = pKingdom?.name ?? "";
            return kingdomName + " " + "\u7981\u536B\u519B";
        }

        private static int CountNobles(List<Actor> pActors)
        {
            int count = 0;
            foreach (Actor actor in pActors)
                if (ChronicleGate.IsNobleActor(actor)) count++;
            return count;
        }

        private static int CountNobleCandidates(List<GuardCandidate> pCandidates)
        {
            int count = 0;
            foreach (GuardCandidate candidate in pCandidates)
                if (candidate.noble) count++;
            return count;
        }

        private static float AverageWarriorScore(Kingdom pKingdom)
        {
            float total = 0f;
            int count = 0;
            int scanned = 0;
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (RoyalGuardMaintenanceRules.ShouldStopCandidateScan(scanned, AVERAGE_SCORE_SCAN_LIMIT)) break;
                scanned++;
                if (unit?.data == null || unit.isRekt()) continue;
                if (!unit.isWarrior()) continue;
                if (IsRoyalGuard(unit) || SlaveService.IsSlave(unit)) continue;
                total += CombatScore(unit);
                count++;
            }
            return count == 0 ? 0f : total / count;
        }

        private static float CombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return SafeStat(pActor, "damage")
                   + SafeStat(pActor, "warfare") * 2f
                   + SafeStat(pActor, "health") * 0.1f
                   + SafeStat(pActor, "armor") * 2f
                   + SafeStat(pActor, "speed") * 0.25f;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }

        private static bool IsAttackerOf(Actor pAttacker, Actor pVictim)
        {
            try
            {
                BaseSimObject source = pVictim?.attackedBy;
                return source != null && source.isActor() && source.a == pAttacker;
            }
            catch
            {
                return false;
            }
        }

        private static float RandomWait(float pMin, float pMax)
        {
            if (pMax < pMin) return pMax;
            return (float)(pMin + Rng.NextDouble() * (pMax - pMin));
        }

        private static GuardStateSnapshot CaptureGuardState(Actor pActor, bool pActive, bool pCaptain,
            bool pNoble, string pGuardName, string pDismissReason)
        {
            Kingdom kingdom = pActor?.kingdom;
            City city = pActor?.city;
            double now = LineageService.CurTime();
            return new GuardStateSnapshot
            {
                actorId = pActor?.data?.id ?? -1L,
                actorName = pActor?.getName() ?? "",
                kingdomId = kingdom?.id ?? -1L,
                kingdomName = kingdom?.name ?? "",
                cityId = city?.id ?? -1L,
                cityName = city?.data?.name ?? "",
                guardName = pGuardName ?? "",
                active = pActive,
                captain = pCaptain,
                noble = pNoble,
                appointedTime = now,
                dismissedTime = pActive ? -1.0 : now,
                dismissReason = pDismissReason ?? ""
            };
        }

        private static void EnqueueGuardState(GuardStateSnapshot pSnapshot)
        {
            if (pSnapshot == null || pSnapshot.actorId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("guard_state", pSnapshot.actorId),
                DeferredWorkClass.Persistent, () => UpsertGuardState(pSnapshot));
        }

        private static void RefreshActorGraphics(long pActorId)
        {
            Actor actor = GetActorById(pActorId);
            if (actor?.data == null || actor.isRekt()) return;
            actor.clearGraphicsFully();
        }

        private static void ArchiveLivingActor(long pActorId)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                Actor actor = GetActorById(pActorId);
                if (actor?.data == null || actor.isRekt()) return;
                LineageService.ArchiveActor(actor, pAlive: true);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.RoyalGuardArchiveIndex,
                    benchmark);
            }
        }

        private static void UpsertGuardState(GuardStateSnapshot pSnapshot)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) throw new InvalidOperationException("Guard archive is unavailable.");
            if (pSnapshot == null || pSnapshot.actorId < 0) return;

            string table = RoyalGuardStateTableItem.GetTableName();
            var values = new[]
            {
                ColumnVal.Create("ACTOR_NAME", pSnapshot.actorName),
                ColumnVal.Create("KINGDOM_ID", pSnapshot.kingdomId),
                ColumnVal.Create("KINGDOM_NAME", pSnapshot.kingdomName),
                ColumnVal.Create("CITY_ID", pSnapshot.cityId),
                ColumnVal.Create("CITY_NAME", pSnapshot.cityName),
                ColumnVal.Create("GUARD_NAME", pSnapshot.guardName),
                ColumnVal.Create("ACTIVE", pSnapshot.active ? 1 : 0),
                ColumnVal.Create("CAPTAIN", pSnapshot.captain ? 1 : 0),
                ColumnVal.Create("NOBLE", pSnapshot.noble ? 1 : 0),
                ColumnVal.Create("APPOINTED_TIME", pSnapshot.appointedTime),
                ColumnVal.Create("DISMISSED_TIME", pSnapshot.dismissedTime),
                ColumnVal.Create("DISMISS_REASON", pSnapshot.dismissReason)
            };

            try
            {
                if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ACTOR_ID", pSnapshot.actorId)))
                {
                    db.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pSnapshot.actorId) },
                        values);
                    return;
                }

                var insertValues = new List<ColumnVal> { ColumnVal.Create("ACTOR_ID", pSnapshot.actorId) };
                insertValues.AddRange(values);
                db.Insert(table, insertValues.ToArray());
            }
            catch (Exception e)
            {
                ModClass.LogWarning("RoyalGuardState upsert failed: " + e.Message);
                throw;
            }
        }
    }
}
