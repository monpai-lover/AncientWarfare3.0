using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class StandingArmyService
    {
        private sealed class EstablishmentScan
        {
            public ArmyStrategicIdCursor Cursor;
            public int FieldArmyCount;
            public Army ReplenishmentArmy;
            public int ReplenishmentUnits = int.MaxValue;
            public bool ReplenishmentPreferred;
            public Army ShellArmy;
            public Army ExcessMergeSource;
            public Army ExcessMergeTarget;
            public Army FirstNonEmptyArmy;
            public Army CommandlessMergeSource;
        }

        private sealed class MergeWork
        {
            public long KingdomId;
            public long SourceArmyId;
            public long TargetArmyId;
        }

        private sealed class Candidate
        {
            public Actor Actor;
            public float Score;
        }

        private sealed class StandingSnapshot
        {
            public int Count;
            public readonly List<Actor> Weakest = new List<Actor>(
                System.Math.Max(StandingArmyRules.MaxReductionsPerPass,
                    StandingArmyRules.MaxReplacementsPerPass));
        }

        private static readonly Dictionary<long, EstablishmentScan>
            EstablishmentScans = new Dictionary<long, EstablishmentScan>();
        private static readonly Dictionary<long, MergeWork> MergeByKingdom =
            new Dictionary<long, MergeWork>();
        private static readonly Dictionary<long, ArmyFieldUsabilityScan>
            UsabilityScans = new Dictionary<long, ArmyFieldUsabilityScan>();

        public static void MaintainCity(City pCity)
        {
            if (!IsValidCity(pCity)) return;
            try
            {
                Kingdom kingdom = pCity.kingdom;
                if (!OccupiedCitySupplyService.CanProvideToRealm(
                        pCity, kingdom)) return;
                if (!StandingArmyRules.ShouldMaintainPeacetime(
                        MilitaryEmergencyService.HasAny(kingdom),
                        TemporaryLevyService.HasActivePool(kingdom))) return;

                int effectiveSlots = MandateMilitaryPhaseService.
                    EffectiveWarriorSlots(kingdom, pCity.status.warrior_slots);
                int core = StandingArmyRules.PeacetimeCore(effectiveSlots);
                int standingCount = CountNormalArmyUnits(pCity);

                if (standingCount < core)
                {
                    AppointCandidates(pCity, CollectCandidates(pCity), core - standingCount);
                    return;
                }

                if (standingCount <= 0) return;
                StandingSnapshot standing = CollectOrdinaryStanding(pCity, standingCount);

                if (standing.Count > core)
                {
                    ReduceSurplus(standing.Weakest, standing.Count - core);
                    return;
                }

                ReplaceWeakestIfBetter(pCity, standing.Weakest, CollectCandidates(pCity));
            }
            finally
            {
                KingdomMilitaryReadinessService.ObserveCity(pCity);
            }
        }

        public static int CountOrdinaryStanding(City pCity)
        {
            return CountNormalArmyUnits(pCity);
        }

        public static int CountOrdinaryStandingFast(City pCity)
        {
            return CountNormalArmyUnits(pCity);
        }

        public static int CountOrdinaryMilitary(City pCity)
        {
            return CountNormalArmyUnits(pCity);
        }

        public static bool RequestEstablishment(Kingdom pKingdom,
            City pPreferredCity, out ArmyRecruitmentDisposition pDisposition,
            out Army pArmy)
        {
            pDisposition = ArmyRecruitmentDisposition.Reject;
            pArmy = null;
            if (pKingdom?.data == null || pKingdom.isRekt()) return true;
            if (!EstablishmentScans.TryGetValue(pKingdom.id,
                    out EstablishmentScan scan))
            {
                scan = new EstablishmentScan
                {
                    Cursor = ArmyFieldIndexService.CreateSnapshotCursor(
                        pKingdom)
                };
                EstablishmentScans[pKingdom.id] = scan;
            }

            IReadOnlyList<long> ids = scan.Cursor.Take(
                ArmyEstablishmentRules.MaximumFieldArmies);
            for (int i = 0; i < ids.Count; i++)
            {
                Army candidate = ArmyFieldIndexService.ResolveIndexedArmy(
                    ids[i], pKingdom.id);
                if (IsFieldArmy(candidate, pKingdom))
                    ObserveEstablishment(scan, candidate, pKingdom,
                        pPreferredCity);
            }
            if (!scan.Cursor.IsComplete) return false;

            EstablishmentScans.Remove(pKingdom.id);
            pArmy = scan.ReplenishmentArmy;
            pDisposition = ArmyEstablishmentRules.DecideRecruitment(
                scan.FieldArmyCount, pArmy?.data != null);
            TryScheduleOneMaintenance(pKingdom, scan);
            return true;
        }

        internal static bool TryResolveReplenishmentTarget(
            Kingdom pKingdom, long pArmyId, out Army pArmy)
        {
            pArmy = null;
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pArmyId < 0L) return false;
            Army candidate = ArmyFieldIndexService.ResolveIndexedArmy(
                pArmyId, pKingdom.id);
            if (candidate?.data == null)
            {
                try { candidate = World.world?.armies?.get(pArmyId); }
                catch { candidate = null; }
            }
            if (!IsFieldArmy(candidate, pKingdom)) return false;
            pArmy = candidate;
            return true;
        }

        public static bool TryHasUsableFieldArmy(Kingdom pKingdom,
            out bool pHasUsable)
        {
            pHasUsable = false;
            if (pKingdom?.data == null || pKingdom.isRekt()) return true;
            if (!UsabilityScans.TryGetValue(pKingdom.id,
                    out ArmyFieldUsabilityScan scan))
            {
                scan = new ArmyFieldUsabilityScan(
                    ArmyFieldIndexService.CreateSnapshotCursor(pKingdom),
                    ArmyEstablishmentRules.MaximumFieldArmies);
                UsabilityScans[pKingdom.id] = scan;
            }
            IReadOnlyList<long> ids = scan.TakeNextRawBatch(
                ArmyEstablishmentRules.MaximumFieldArmies);
            for (int i = 0; i < ids.Count; i++)
            {
                Army army = ArmyFieldIndexService.ResolveIndexedArmy(
                    ids[i], pKingdom.id);
                bool valid = IsFieldArmy(army, pKingdom);
                scan.Observe(valid, valid && SafeUnitCount(army) > 1);
            }
            if (!scan.IsComplete) return false;
            pHasUsable = scan.FoundUsable;
            UsabilityScans.Remove(pKingdom.id);
            return true;
        }

        public static void OnFieldArmyChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            EstablishmentScans.Remove(pKingdom.id);
            UsabilityScans.Remove(pKingdom.id);
            ScheduleEstablishmentMaintenance(pKingdom.id);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            EstablishmentScans.Remove(pKingdom.id);
            UsabilityScans.Remove(pKingdom.id);
            MergeByKingdom.Remove(pKingdom.id);
        }

        public static void ClearEstablishmentRuntime()
        {
            EstablishmentScans.Clear();
            MergeByKingdom.Clear();
            UsabilityScans.Clear();
        }

        private static int CountNormalArmyUnits(City pCity)
        {
            if (pCity?.data == null || !pCity.hasArmy()) return 0;
            Army army = pCity.getArmy();
            if (army?.data == null || !army.isAlive() || AWArmyService.IsSpecialArmy(army)) return 0;
            try { return System.Math.Max(0, army.countUnits()); }
            catch { return 0; }
        }

        private static void ObserveEstablishment(EstablishmentScan pScan,
            Army pArmy, Kingdom pKingdom, City pPreferredCity)
        {
            int units = SafeUnitCount(pArmy);
            if (!ArmyEstablishmentRules.ShouldCountTowardsFieldArmyLimit(
                    units))
            {
                ObserveMaintenanceCandidate(pScan, pArmy, units);
                return;
            }
            pScan.FieldArmyCount++;
            int target = TargetStrength(pArmy, pKingdom);
            bool preferred = AWArmyService.GetAnchorCityId(pArmy) ==
                             (pPreferredCity?.id ?? -1L);
            if (ArmyEstablishmentRules.ShouldUseAsReplenishmentTarget(
                    units, target) &&
                (pScan.ReplenishmentArmy == null ||
                 preferred && !pScan.ReplenishmentPreferred ||
                 preferred == pScan.ReplenishmentPreferred &&
                 units < pScan.ReplenishmentUnits))
            {
                pScan.ReplenishmentArmy = pArmy;
                pScan.ReplenishmentUnits = units;
                pScan.ReplenishmentPreferred = preferred;
            }
            ObserveMaintenanceCandidate(pScan, pArmy, units);
        }

        private static void ObserveMaintenanceCandidate(
            EstablishmentScan pScan, Army pArmy, int pUnits)
        {
            if (pUnits <= 0 && pScan.ShellArmy == null)
                pScan.ShellArmy = pArmy;
            if (pUnits <= 0 || pScan.ExcessMergeSource != null) return;
            if (pScan.FirstNonEmptyArmy == null)
                pScan.FirstNonEmptyArmy = pArmy;
            bool sourceEligible = ArmyEstablishmentRules.
                IsMaintenanceMergeSource(pUnits,
                    HasLivingCaptain(pArmy), HasActiveRtsMission(pArmy));
            if (sourceEligible && pScan.FirstNonEmptyArmy != pArmy)
            {
                pScan.ExcessMergeSource = pArmy;
                pScan.ExcessMergeTarget = pScan.FirstNonEmptyArmy;
                return;
            }
            if (sourceEligible && pScan.CommandlessMergeSource == null)
                pScan.CommandlessMergeSource = pArmy;
            if (pScan.CommandlessMergeSource != null &&
                pScan.CommandlessMergeSource != pArmy)
            {
                pScan.ExcessMergeSource = pScan.CommandlessMergeSource;
                pScan.ExcessMergeTarget = pArmy;
            }
        }

        private static void TryScheduleOneMaintenance(Kingdom pKingdom,
            EstablishmentScan pScan)
        {
            if (pKingdom?.data == null || pScan == null ||
                MergeByKingdom.ContainsKey(pKingdom.id)) return;
            bool overCap = ArmyEstablishmentRules.
                ShouldMaintainExcessFieldArmies(pScan.FieldArmyCount);
            if (pScan.ShellArmy?.data != null)
            {
                ArmyInvalidCleanupQueue.ScheduleShell(pScan.ShellArmy,
                    AWArmyService.FindAnchorCity(pScan.ShellArmy), pKingdom);
                return;
            }
            Army source = pScan.ExcessMergeSource;
            Army target = pScan.ExcessMergeTarget;
            if (source?.data == null || target?.data == null) return;
            if (!ArmyEstablishmentRules.ShouldScheduleMaintenanceMerge(
                    overCap, HasActiveWar(pKingdom), SafeUnitCount(source),
                    HasLivingCaptain(source),
                    HasActiveRtsMission(source))) return;
            MergeByKingdom[pKingdom.id] = new MergeWork
            {
                KingdomId = pKingdom.id,
                SourceArmyId = source.id,
                TargetArmyId = target.id
            };
            ScheduleMergeBatch(pKingdom.id);
        }

        private static void ScheduleEstablishmentMaintenance(
            long pKingdomId)
        {
            if (pKingdomId < 0L) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "army_establishment_maintenance", pKingdomId),
                DeferredWorkClass.Runtime,
                () => ProcessEstablishmentMaintenance(pKingdomId));
        }

        private static void ProcessEstablishmentMaintenance(
            long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt() ||
                MergeByKingdom.ContainsKey(pKingdomId)) return;
            if (!RequestEstablishment(kingdom, pPreferredCity: null,
                    out _, out _))
                ScheduleEstablishmentMaintenance(pKingdomId);
        }

        private static void ScheduleMergeBatch(long pKingdomId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "army_establishment_merge", pKingdomId),
                DeferredWorkClass.Runtime,
                () => ProcessMergeBatch(pKingdomId));
        }

        private static void ProcessMergeBatch(long pKingdomId)
        {
            if (!MergeByKingdom.TryGetValue(pKingdomId,
                    out MergeWork work)) return;
            Army source = ArmyFieldIndexService.ResolveIndexedArmy(
                work.SourceArmyId, work.KingdomId);
            Army target = ArmyFieldIndexService.ResolveIndexedArmy(
                work.TargetArmyId, work.KingdomId);
            Kingdom kingdom = ResolveKingdom(work.KingdomId);
            if (!IsFieldArmy(source, kingdom) ||
                !IsFieldArmy(target, kingdom))
            {
                MergeByKingdom.Remove(pKingdomId);
                ScheduleEstablishmentMaintenance(pKingdomId);
                return;
            }
            bool overCap = ArmyEstablishmentRules.
                ShouldMaintainExcessFieldArmies(
                    ArmyFieldIndexService.Count(kingdom));
            if (!ArmyEstablishmentRules.ShouldScheduleMaintenanceMerge(
                    overCap, HasActiveWar(kingdom), SafeUnitCount(source),
                    HasLivingCaptain(source),
                    HasActiveRtsMission(source)) || source == target)
            {
                MergeByKingdom.Remove(pKingdomId);
                ScheduleEstablishmentMaintenance(pKingdomId);
                return;
            }
            var batch = new List<Actor>(
                ArmyEstablishmentRules.MemberAssignmentBatchSize);
            int count = Math.Min(SafeUnitCount(source),
                ArmyEstablishmentRules.MemberAssignmentBatchSize);
            for (int i = 0; i < count; i++)
            {
                Actor actor = null;
                try { actor = source.units[i]; }
                catch { }
                if (actor?.data != null && actor.army == source)
                    batch.Add(actor);
            }
            int moved = 0;
            using (ArmyCaptainDisposalScope.Open(source))
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    AWArmyService.AddToArmy(batch[i], target);
                    if (batch[i].army == target) moved++;
                }
            }

            int remaining = SafeUnitCount(source);
            if (remaining > 0)
            {
                if (ArmyEstablishmentRules.ShouldContinueMergeBatch(
                        remaining, moved))
                    ScheduleMergeBatch(pKingdomId);
                else
                    MergeByKingdom.Remove(pKingdomId);
                return;
            }
            MergeByKingdom.Remove(pKingdomId);
            if (!ArmyLifecycleRules.ShouldQueueArmyShellForCleanup(
                    remaining)) return;
            using (ArmyCaptainDisposalScope.Open(source))
            {
                try { source.setCaptain(null); } catch { }
                ArmyRtsControllerService.Invalidate(source.id);
                ArmyInvalidCleanupQueue.ScheduleShell(source,
                    AWArmyService.FindAnchorCity(source), kingdom);
            }
        }

        private static bool IsFieldArmy(Army pArmy, Kingdom pKingdom)
        {
            if (pArmy?.data == null || pKingdom?.data == null ||
                AWArmyService.IsSpecialArmy(pArmy) ||
                GarrisonSortieService.IsSortieArmy(pArmy)) return false;
            try
            {
                return pArmy.isAlive() && pArmy.getKingdom() == pKingdom;
            }
            catch { return false; }
        }

        private static bool HasActiveRtsMission(Army pArmy)
        {
            return pArmy?.data != null &&
                   ArmyRtsControllerService.HasActiveMission(pArmy.id);
        }

        private static bool HasActiveWar(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   MilitaryEmergencyService.TryGetActiveWarId(pKingdom,
                       out _);
        }

        private static bool HasLivingCaptain(Army pArmy)
        {
            Actor captain = null;
            try { captain = pArmy?.getCaptain(); }
            catch { }
            try
            {
                return captain?.data != null && !captain.isRekt() &&
                       captain.isAlive();
            }
            catch { return false; }
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.units?.Count ?? 0); }
            catch { return 0; }
        }

        internal static int TargetStrength(Army pArmy, Kingdom pKingdom)
        {
            if (pArmy?.data != null && !AWArmyService.IsSpecialArmy(pArmy))
                return CityArmyReinforcementService.ApprovedTarget(pArmy,
                    pKingdom);
            City anchor = AWArmyService.FindAnchorCity(pArmy);
            int slots = 0;
            try
            {
                if (anchor?.data != null)
                    slots = anchor.status.warrior_slots;
            }
            catch { }
            if (anchor?.data != null)
                slots = MandateMilitaryPhaseService.EffectiveWarriorSlots(
                    pKingdom, slots);
            return Math.Max(2, slots);
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        public static bool ShouldKeepWithinOriginalArmyLimit(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null || !pCity.hasArmy()) return false;
            Army army = pCity.getArmy();
            return army?.data != null && !AWArmyService.IsSpecialArmy(army) &&
                   pActor.army == army && pActor.isWarrior() &&
                   !SlaveService.IsSlave(pActor) && pCity.hasEnoughFoodForArmy();
        }

        private static bool IsValidCity(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            return pCity?.data != null && !pCity.isRekt() &&
                   kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral();
        }

        private static StandingSnapshot CollectOrdinaryStanding(City pCity, int pCount)
        {
            var result = new StandingSnapshot { Count = pCount };
            if (pCity?.data == null || !pCity.hasArmy()) return result;
            Army army = pCity.getArmy();
            if (army?.data == null || AWArmyService.IsSpecialArmy(army)) return result;

            pCity.data.get(LineageKeys.STANDING_ARMY_ROSTER_SCAN_CURSOR, out int cursor, 0);
            if (cursor < 0) cursor = 0;
            int unitCount = army.units.Count;
            if (cursor >= unitCount) cursor = 0;
            int scanned = System.Math.Min(StandingArmyRules.MaxStandingScanPerPass,
                System.Math.Max(0, unitCount - cursor));
            for (int i = 0; i < scanned; i++)
            {
                Actor actor = army.units[cursor + i];
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                if (!actor.isWarrior() || actor.army != army) continue;
                actor.data.get(LineageKeys.TEMPORARY_LEVY, out bool levy, false);
                if (levy || RoyalGuardService.IsRoyalGuard(actor) || SlaveService.IsSlave(actor)) continue;
                AddBoundedWeakest(result.Weakest, actor,
                    System.Math.Max(StandingArmyRules.MaxReductionsPerPass,
                        StandingArmyRules.MaxReplacementsPerPass));
            }
            bool complete = cursor + scanned >= unitCount;
            pCity.data.set(LineageKeys.STANDING_ARMY_ROSTER_SCAN_CURSOR,
                complete ? 0 : cursor + scanned);
            result.Weakest.Sort(CompareWeakestFirst);
            return result;
        }

        private static List<Candidate> CollectCandidates(City pCity)
        {
            var result = new List<Candidate>(StandingArmyRules.MaxAppointmentsPerPass + 1);
            pCity.data.get(LineageKeys.STANDING_ARMY_SCAN_CURSOR, out int cursor, 0);
            if (cursor < 0) cursor = 0;

            int unitCount = pCity.units.Count;
            if (cursor >= unitCount) cursor = 0;
            int scanned = System.Math.Min(StandingArmyRules.MaxCandidateScan,
                System.Math.Max(0, unitCount - cursor));
            for (int i = 0; i < scanned; i++)
            {
                Actor actor = pCity.units[cursor + i];
                if (!IsCandidate(pCity, actor)) continue;
                AddBoundedBest(result, new Candidate { Actor = actor, Score = Score(actor) },
                    StandingArmyRules.MaxAppointmentsPerPass + StandingArmyRules.MaxReplacementsPerPass);
            }

            bool complete = cursor + scanned >= unitCount;
            pCity.data.set(LineageKeys.STANDING_ARMY_SCAN_CURSOR, complete ? 0 : cursor + scanned);
            result.Sort(CompareBestFirst);
            return result;
        }

        private static bool IsCandidate(City pCity, Actor pActor)
        {
            if (pActor?.data == null || pActor.city != pCity || pActor.kingdom != pCity.kingdom) return false;
            if (pActor.isRekt() || !pActor.isAlive() || !pActor.isAdult() || pActor.asset?.is_boat == true)
                return false;
            if (!SoldierRetirementRules.IsOrdinaryServiceAgeAllowed(
                    pActor.getAge())) return false;
            if (!pActor.isProfession(UnitProfession.Unit)) return false;
            if (pActor.isKing() || pActor.isCityLeader() || GeneralService.IsActiveGeneralFast(pActor)) return false;
            if (HeirService.IsCurrentHeir(pCity.kingdom, pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor) || SlaveService.IsSlave(pActor) ||
                SlaveService.IsRetiredSoldier(pActor) || RoyalAsylumService.IsActive(pActor)) return false;
            if (DynasticReproductionService
                .ShouldProtectFromOrdinaryMilitaryService(pActor)) return false;
            if (!HistoricalMasterVocationService.CanEnter(pActor, HistoricalMasterMilitaryContext.OrdinaryWarrior))
                return false;

            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            if (!string.IsNullOrEmpty(office) && layer != CourtOfficeLayer.Military) return false;

            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.StandingArmy))
                return pCity.checkCanMakeWarrior(pActor);
        }

        private static void ReduceSurplus(List<Actor> pStanding, int pSurplus)
        {
            int count = System.Math.Min(System.Math.Min(pSurplus, pStanding.Count),
                StandingArmyRules.MaxReductionsPerPass);
            for (int i = 0; i < count; i++) DemoteWithoutRetirement(pStanding[i]);
        }

        private static void AppointCandidates(City pCity, List<Candidate> pCandidates, int pShortage)
        {
            int count = System.Math.Min(System.Math.Min(pShortage, pCandidates.Count),
                StandingArmyRules.MaxAppointmentsPerPass);
            for (int i = 0; i < count; i++) Appoint(pCity, pCandidates[i].Actor);
        }

        private static void ReplaceWeakestIfBetter(City pCity, List<Actor> pStanding,
            List<Candidate> pCandidates)
        {
            if (pStanding.Count == 0 || pCandidates.Count == 0 ||
                StandingArmyRules.MaxReplacementsPerPass <= 0) return;
            Actor weakest = pStanding[0];
            Candidate strongest = pCandidates[0];
            float weakestScore = Score(weakest);
            if (strongest.Score < weakestScore) return;
            if (strongest.Score == weakestScore && strongest.Actor.data.id > weakest.data.id) return;

            DemoteWithoutRetirement(weakest);
            Appoint(pCity, strongest.Actor);
        }

        private static void Appoint(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null || pActor.isWarrior()) return;
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.StandingArmy))
            {
                if (!pCity.checkCanMakeWarrior(pActor)) return;
                pCity.makeWarrior(pActor);
            }
            if (StandingArmyRules.ShouldEnsureArmyMembership(
                    pActor.isWarrior(), pActor.army?.data != null))
                EnsureOrdinaryArmyMembership(pCity, pActor);
        }

        private static bool EnsureOrdinaryArmyMembership(City pCity,
            Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null ||
                pActor.kingdom != pCity.kingdom || !pActor.isWarrior())
                return false;
            if (pActor.army?.data != null) return true;

            Army cityArmy = null;
            try
            {
                if (pCity.hasArmy()) cityArmy = pCity.getArmy();
            }
            catch { }
            if (IsAssignableStandingArmy(cityArmy, pCity))
            {
                AWArmyService.AddToArmy(pActor, cityArmy);
                return pActor.army == cityArmy;
            }
            if (ArmyFieldIndexService.TryRouteStandingCandidate(
                    pActor, pCity, out _)) return true;
            if (ArmyFieldIndexService.Count(pCity.kingdom) >=
                ArmyEstablishmentRules.MaximumFieldArmies) return false;

            Army created = null;
            bool detached = cityArmy != null;
            try
            {
                using (MilitaryRecruitmentScope.Open(
                           MilitaryRecruitmentKind.StandingArmy))
                {
                    created = detached
                        ? AWArmyService.CreateDetachedArmy(
                            pCity.kingdom, pCity, pActor)
                        : World.world?.armies?.newArmy(pActor, pCity);
                }
                if (created?.data == null) return false;
                if (pActor.army != created)
                    AWArmyService.AddToArmy(pActor, created);
                if (pActor.army != created)
                {
                    ArmyInvalidCleanupQueue.ScheduleShell(created, pCity,
                        pCity.kingdom);
                    return false;
                }
                if (detached)
                    ArmyStrategicIndexService.OnArmyRegistered(created);
                return true;
            }
            catch
            {
                if (created?.data != null)
                    ArmyInvalidCleanupQueue.ScheduleShell(created, pCity,
                        pCity.kingdom);
                return false;
            }
        }

        private static bool IsAssignableStandingArmy(Army pArmy,
            City pCity)
        {
            if (pArmy?.data == null || pCity?.kingdom?.data == null ||
                AWArmyService.IsSpecialArmy(pArmy) ||
                GarrisonSortieService.IsSortieArmy(pArmy)) return false;
            try
            {
                return pArmy.isAlive() &&
                       AWArmyService.GetIntendedKingdom(pArmy) ==
                       pCity.kingdom;
            }
            catch { return false; }
        }

        private static void DemoteWithoutRetirement(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isWarrior()) return;
            pActor.stopBeingWarrior();
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
        }

        private static void AddBoundedBest(List<Candidate> pCandidates, Candidate pCandidate, int pLimit)
        {
            if (pCandidate?.Actor?.data == null || pLimit <= 0) return;
            if (pCandidates.Count < pLimit)
            {
                pCandidates.Add(pCandidate);
                return;
            }

            int weakest = 0;
            for (int i = 1; i < pCandidates.Count; i++)
                if (CompareBestFirst(pCandidates[weakest], pCandidates[i]) < 0)
                    weakest = i;
            if (CompareBestFirst(pCandidate, pCandidates[weakest]) < 0)
                pCandidates[weakest] = pCandidate;
        }

        private static void AddBoundedWeakest(List<Actor> pActors, Actor pActor, int pLimit)
        {
            if (pActor?.data == null || pLimit <= 0) return;
            if (pActors.Count < pLimit)
            {
                pActors.Add(pActor);
                return;
            }

            int strongest = 0;
            for (int i = 1; i < pActors.Count; i++)
                if (CompareWeakestFirst(pActors[strongest], pActors[i]) < 0)
                    strongest = i;
            if (CompareWeakestFirst(pActor, pActors[strongest]) < 0)
                pActors[strongest] = pActor;
        }

        private static int CompareBestFirst(Candidate pLeft, Candidate pRight)
        {
            int score = pRight.Score.CompareTo(pLeft.Score);
            return score != 0 ? score : pLeft.Actor.data.id.CompareTo(pRight.Actor.data.id);
        }

        private static int CompareWeakestFirst(Actor pLeft, Actor pRight)
        {
            int score = Score(pLeft).CompareTo(Score(pRight));
            return score != 0 ? score : pRight.data.id.CompareTo(pLeft.data.id);
        }

        private static float Score(Actor pActor)
        {
            return StandingArmyRules.MilitaryScore(
                SafeStat(pActor, "damage"),
                SafeStat(pActor, "warfare"),
                SafeStat(pActor, "health"),
                SafeStat(pActor, "armor"),
                SafeStat(pActor, "speed"));
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }
    }
}
