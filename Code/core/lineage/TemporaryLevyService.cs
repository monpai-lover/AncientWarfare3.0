using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class TemporaryLevyService
    {
        private sealed class LevyPool
        {
            public readonly HashSet<long> ActorIds = new HashSet<long>();
            public readonly long[] DemobilizationBuffer = new long[TemporaryLevyRules.DemobilizationBatchSize];
            public long ActiveWarId = -1L;
            public string NoticeSignature = "";
        }

        private sealed class RecruitmentYearPlan
        {
            public readonly long KingdomId;
            public readonly int Year;
            public int CompletedWorkItems;
            public int ScannedCandidates;
            public int RecruitedActors;
            public int PreferredCityCursor;

            public RecruitmentYearPlan(long pKingdomId, int pYear)
            {
                KingdomId = pKingdomId;
                Year = pYear;
            }
        }

        private static readonly Dictionary<long, LevyPool> Pools = new Dictionary<long, LevyPool>();
        private static readonly Dictionary<long, RecruitmentYearPlan> RecruitmentPlans =
            new Dictionary<long, RecruitmentYearPlan>();
        private static readonly HashSet<long> ActiveActorIds = new HashSet<long>();

        public static bool IsTemporaryLevy(Actor pActor)
        {
            return pActor?.data != null && ActiveActorIds.Contains(pActor.data.id);
        }

        public static void OnActorInvalidated(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (!ActiveActorIds.Remove(pActor.data.id)) return;
            pActor.data.get(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, out long kingdomId, -1L);
            if (kingdomId >= 0 && Pools.TryGetValue(kingdomId, out LevyPool pool))
            {
                pool.ActorIds.Remove(pActor.data.id);
                if (pool.ActorIds.Count == 0) Pools.Remove(kingdomId);
            }
            ClearFields(pActor);
        }

        public static bool HasActivePool(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   Pools.TryGetValue(pKingdom.id, out LevyPool pool) &&
                   pool.ActorIds.Count > 0;
        }

        public static int ActiveLevyCount(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   Pools.TryGetValue(pKingdom.id, out LevyPool pool)
                ? pool.ActorIds.Count
                : 0;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return;
            if (AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom))
            {
                RecruitmentPlans.Remove(pKingdom.id);
                ScheduleDemobilization(pKingdom.id);
                return;
            }
            if (!MilitaryEmergencyService.HasAny(pKingdom))
            {
                ScheduleDemobilization(pKingdom.id);
                return;
            }

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year)
            {
                ResumeRecruitmentYear(pKingdom, year);
                return;
            }
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, year);
            ScheduleRecruitmentYear(pKingdom, year);
        }

        public static void OnWarStarted(War pWar, string pNoticeSignature)
        {
            if (pWar?.data == null) return;
            foreach (Kingdom kingdom in pWar.getAttackers())
            {
                ActivateWar(kingdom, pWar.data.id, pNoticeSignature);
                OnEmergencyChanged(kingdom);
            }
            foreach (Kingdom kingdom in pWar.getDefenders())
            {
                ActivateWar(kingdom, pWar.data.id, pNoticeSignature);
                OnEmergencyChanged(kingdom);
            }
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            foreach (Kingdom kingdom in pWar.getAttackers()) OnEmergencyChanged(kingdom);
            foreach (Kingdom kingdom in pWar.getDefenders()) OnEmergencyChanged(kingdom);
        }

        public static void OnNoticeClosed(long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data != null) OnEmergencyChanged(kingdom);
        }

        public static void OnEmergencyChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom))
            {
                RecruitmentPlans.Remove(pKingdom.id);
                ScheduleDemobilization(pKingdom.id);
                return;
            }
            long kingdomId = pKingdom.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("levy_emergency", kingdomId),
                DeferredWorkClass.Runtime, () => ProcessEmergencyChanged(kingdomId));
        }

        private static void ProcessEmergencyChanged(long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null) return;
            if (MilitaryEmergencyService.HasAny(kingdom))
                OnKingdomYear(kingdom);
            else
                ScheduleIfSafe(kingdom);
        }

        public static void RebuildRuntime()
        {
            Pools.Clear();
            RecruitmentPlans.Clear();
            ActiveActorIds.Clear();
            if (World.world?.units != null)
            {
                foreach (Actor actor in World.world.units)
                {
                    if (!HasPersistedFlag(actor)) continue;
                    actor.data.get(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, out long kingdomId, -1L);
                    if (kingdomId < 0)
                    {
                        ClearFields(actor);
                        continue;
                    }
                    ActiveActorIds.Add(actor.data.id);
                    Pool(kingdomId).ActorIds.Add(actor.data.id);
                }
            }

            foreach (long kingdomId in new List<long>(Pools.Keys))
            {
                Kingdom kingdom = ResolveKingdom(kingdomId);
                if (kingdom?.data == null || !MilitaryEmergencyService.HasAny(kingdom))
                    ScheduleDemobilization(kingdomId);
            }
            ResumeActiveRecruitmentPlans();
        }

        public static void ClearRuntime()
        {
            Pools.Clear();
            RecruitmentPlans.Clear();
            ActiveActorIds.Clear();
        }

        private static void ScheduleRecruitmentYear(Kingdom pKingdom, int pYear)
        {
            if (pKingdom?.data == null || pYear < 0) return;
            if (!RecruitmentPlans.TryGetValue(pKingdom.id, out RecruitmentYearPlan plan) ||
                plan.Year != pYear)
            {
                plan = new RecruitmentYearPlan(pKingdom.id, pYear);
                RecruitmentPlans[pKingdom.id] = plan;
                PersistRecruitmentPlan(pKingdom, plan);
            }
            ScheduleRecruitmentBatch(plan);
        }

        private static void ResumeRecruitmentYear(Kingdom pKingdom, int pYear)
        {
            if (pKingdom?.data == null) return;
            if (!RecruitmentPlans.TryGetValue(pKingdom.id, out RecruitmentYearPlan plan) ||
                plan.Year != pYear)
                plan = RestoreRecruitmentPlan(pKingdom, pYear);
            if (plan == null ||
                !TemporaryLevyRules.ShouldRunRecruitmentWorkItem(true,
                    plan.CompletedWorkItems, plan.ScannedCandidates,
                    plan.RecruitedActors)) return;
            ScheduleRecruitmentBatch(plan);
        }

        private static RecruitmentYearPlan RestoreRecruitmentPlan(Kingdom pKingdom, int pYear)
        {
            if (pKingdom?.data == null || pYear < 0) return null;
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, out int recordedYear, int.MinValue);
            if (recordedYear != pYear) return null;
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_WORK_ITEMS, out int workItems, 0);
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_SCANNED, out int scanned, 0);
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_RECRUITED, out int recruited, 0);
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_FRONTIER_CURSOR, out int frontierCursor, 0);
            var plan = new RecruitmentYearPlan(pKingdom.id, pYear)
            {
                CompletedWorkItems = TemporaryLevyRules.ClampRestoredCounter(
                    workItems, TemporaryLevyRules.MaxWorkItemsPerKingdomYear),
                ScannedCandidates = TemporaryLevyRules.ClampRestoredCounter(
                    scanned, TemporaryLevyRules.MaxCandidatesPerKingdomYear),
                RecruitedActors = TemporaryLevyRules.ClampRestoredCounter(
                    recruited, TemporaryLevyRules.MaxRecruitsPerKingdomYear),
                PreferredCityCursor = TemporaryLevyRules.ClampRestoredCounter(
                    frontierCursor, TemporaryLevyRules.MaxWorkItemsPerKingdomYear)
            };
            RecruitmentPlans[pKingdom.id] = plan;
            return plan;
        }

        private static void PersistRecruitmentPlan(Kingdom pKingdom, RecruitmentYearPlan pPlan)
        {
            if (pKingdom?.data == null || pPlan == null || pKingdom.id != pPlan.KingdomId) return;
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, pPlan.Year);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_WORK_ITEMS, pPlan.CompletedWorkItems);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_SCANNED, pPlan.ScannedCandidates);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_RECRUITED, pPlan.RecruitedActors);
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_FRONTIER_CURSOR, pPlan.PreferredCityCursor);
        }

        private static void ResumeActiveRecruitmentPlans()
        {
            if (World.world?.kingdoms == null) return;
            int year = Date.getCurrentYear();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() ||
                    !MilitaryEmergencyService.HasAny(kingdom)) continue;
                kingdom.data.get(LineageKeys.TEMPORARY_LEVY_LAST_YEAR,
                    out int lastYear, int.MinValue);
                if (lastYear == year)
                    ResumeRecruitmentYear(kingdom, year);
                else
                {
                    kingdom.data.set(LineageKeys.TEMPORARY_LEVY_LAST_YEAR, year);
                    ScheduleRecruitmentYear(kingdom, year);
                }
            }
        }

        private static void ScheduleRecruitmentBatch(RecruitmentYearPlan pPlan)
        {
            if (pPlan == null) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("levy_recruit", pPlan.KingdomId),
                DeferredWorkClass.Runtime,
                () => ProcessRecruitmentBatch(pPlan.KingdomId, pPlan.Year));
        }

        private static void ProcessRecruitmentBatch(long pKingdomId, int pYear)
        {
            if (!RecruitmentPlans.TryGetValue(pKingdomId, out RecruitmentYearPlan plan) ||
                plan.Year != pYear) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                RecruitmentPlans.Remove(pKingdomId);
                ScheduleDemobilization(pKingdomId);
                return;
            }
            if (AutonomousRestorationService.IsActiveCampaignKingdom(kingdom))
            {
                RecruitmentPlans.Remove(pKingdomId);
                ScheduleDemobilization(pKingdomId);
                return;
            }
            bool emergencyActive = MilitaryEmergencyService.HasAny(kingdom);
            if (!emergencyActive)
            {
                ScheduleDemobilization(pKingdomId);
                return;
            }
            if (!TemporaryLevyRules.ShouldRunRecruitmentWorkItem(true,
                    plan.CompletedWorkItems, plan.ScannedCandidates, plan.RecruitedActors))
                return;

            City city;
            if (!ArmyDeploymentService.TryGetPreferredLevyCity(
                    kingdom, plan.PreferredCityCursor, out city))
                city = NextCursorCity(kingdom);
            else
                plan.PreferredCityCursor++;

            int scanned = 0;
            int recruited = 0;
            ScanCity(kingdom, city, ref scanned, ref recruited);
            plan.CompletedWorkItems++;
            plan.ScannedCandidates += scanned;
            plan.RecruitedActors += recruited;
            PersistRecruitmentPlan(kingdom, plan);

            if (TemporaryLevyRules.ShouldRunRecruitmentWorkItem(
                    MilitaryEmergencyService.HasAny(kingdom), plan.CompletedWorkItems,
                    plan.ScannedCandidates, plan.RecruitedActors))
            {
                ScheduleRecruitmentBatch(plan);
            }
        }

        private static City NextCursorCity(Kingdom pKingdom)
        {
            int cityCount = pKingdom?.cities?.Count ?? 0;
            if (cityCount <= 0) return null;
            pKingdom.data.get(LineageKeys.TEMPORARY_LEVY_CITY_CURSOR, out int cursor, 0);
            cursor = PositiveModulo(cursor, cityCount);
            City city = pKingdom.cities[cursor];
            pKingdom.data.set(LineageKeys.TEMPORARY_LEVY_CITY_CURSOR,
                PositiveModulo(cursor + 1, cityCount));
            return city;
        }

        private static void ScanCity(Kingdom pKingdom, City pCity,
            ref int pScanned, ref int pRecruited)
        {
            if (pCity?.data == null || pCity.isRekt() || pCity.kingdom != pKingdom) return;
            int ordinaryMilitary = StandingArmyService.CountOrdinaryMilitary(pCity);
            if (pCity.status.warrior_slots <= 0 || ordinaryMilitary >= pCity.status.warrior_slots) return;
            pCity.data.get(LineageKeys.TEMPORARY_LEVY_ACTOR_CURSOR, out int cursor, 0);
            if (cursor < 0) cursor = 0;

            int unitCount = pCity.units.Count;
            if (cursor >= unitCount) cursor = 0;
            int available = Math.Min(unitCount - cursor,
                TemporaryLevyRules.MaxCandidatesPerWorkItem);
            int localLimit = Math.Max(0, available);
            int localScanned = 0;
            for (int i = 0; i < localLimit; i++)
            {
                if (pScanned >= TemporaryLevyRules.MaxCandidatesPerWorkItem ||
                    pRecruited >= TemporaryLevyRules.MaxRecruitsPerWorkItem ||
                    ordinaryMilitary >= pCity.status.warrior_slots) break;

                Actor actor = pCity.units[cursor + i];
                pScanned++;
                localScanned++;
                if (!CanEnlist(pKingdom, pCity, actor, ordinaryMilitary)) continue;
                if (!Enlist(pKingdom, pCity, actor)) continue;
                pRecruited++;
                ordinaryMilitary++;
            }
            bool complete = cursor + localScanned >= unitCount;
            pCity.data.set(LineageKeys.TEMPORARY_LEVY_ACTOR_CURSOR,
                complete ? 0 : cursor + localScanned);
        }

        private static bool CanEnlist(Kingdom pKingdom, City pCity, Actor pActor, int pOrdinaryMilitary)
        {
            if (pActor?.data == null || pActor.city != pCity || pActor.kingdom != pKingdom) return false;
            if (pActor.isRekt() || !pActor.isAlive() || !pActor.isAdult() ||
                pActor.asset?.is_boat == true || !pActor.isProfession(UnitProfession.Unit)) return false;
            bool protectedIdentity = IsProtectedIdentity(pKingdom, pActor);
            bool originalEligible;
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.TemporaryLevy))
                originalEligible = pCity.checkCanMakeWarrior(pActor);
            return TemporaryLevyRules.CanEnlist(originalEligible, protectedIdentity, pActor.getAge(),
                pOrdinaryMilitary, pCity.status.warrior_slots);
        }

        private static bool IsProtectedIdentity(Kingdom pKingdom, Actor pActor)
        {
            if (pActor.isKing() || pActor.isCityLeader() || HeirService.IsCurrentHeir(pKingdom, pActor))
                return true;
            if (GeneralService.IsActiveGeneralFast(pActor) || RoyalGuardService.IsRoyalGuard(pActor) ||
                RoyalAsylumService.IsActive(pActor) || SlaveService.IsSlave(pActor) ||
                SlaveService.IsRetiredSoldier(pActor)) return true;
            if (pActor.army != null && AWArmyService.IsSpecialArmy(pActor.army)) return true;
            if (!HistoricalMasterVocationService.CanEnter(pActor, HistoricalMasterMilitaryContext.OrdinaryWarrior))
                return true;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            return !string.IsNullOrEmpty(office) && layer != CourtOfficeLayer.Military;
        }

        private static bool Enlist(Kingdom pKingdom, City pCity, Actor pActor)
        {
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.TemporaryLevy))
            {
                if (!pCity.checkCanMakeWarrior(pActor)) return false;
                pCity.makeWarrior(pActor);
            }
            if (!pActor.isWarrior()) return false;
            if (!EnsureArmyMembership(pCity, pActor))
            {
                pActor.stopBeingWarrior();
                return false;
            }

            LevyPool pool = Pool(pKingdom.id);
            string noticeSignature = string.IsNullOrEmpty(pool.NoticeSignature)
                ? WarNoticeService.IncomingNoticeSignature(pKingdom)
                : pool.NoticeSignature;
            long warId = pool.ActiveWarId;
            if (warId < 0) MilitaryEmergencyService.TryGetActiveWarId(pKingdom, out warId);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY, true);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_NOTICE_SIGNATURE, noticeSignature ?? "");
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_ORIGINAL_CITY_ID, pCity.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_WAR_ID, warId);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            pool.ActorIds.Add(pActor.data.id);
            ActiveActorIds.Add(pActor.data.id);
            WarNoticeService.QueueArmyChanged(pKingdom, pActor.army, pRosterExpanded: true);
            RecordEnlistedDeferred(pActor.data.id, pKingdom.id, pCity.id);
            return true;
        }

        private static bool EnsureArmyMembership(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null || !pActor.isWarrior()) return false;
            Army army = null;
            try
            {
                if (pCity.hasArmy())
                    army = pCity.getArmy();
                else
                    army = World.world?.armies?.newArmy(pActor, pCity);
            }
            catch { }
            if (army?.data == null) return false;
            if (pActor.army != army) AWArmyService.AddToArmy(pActor, army);
            return pActor.army == army;
        }

        private static void ActivateWar(Kingdom pKingdom, long pWarId, string pNoticeSignature)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            LevyPool pool = Pool(pKingdom.id);
            pool.ActiveWarId = pWarId;
            if (!string.IsNullOrEmpty(pNoticeSignature)) pool.NoticeSignature = pNoticeSignature;
        }

        private static void ScheduleIfSafe(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || MilitaryEmergencyService.HasAny(pKingdom)) return;
            ScheduleDemobilization(pKingdom.id);
        }

        private static void ScheduleDemobilization(long pKingdomId)
        {
            if (pKingdomId < 0 || !Pools.TryGetValue(pKingdomId, out LevyPool pool)) return;
            if (pool.ActorIds.Count == 0)
            {
                Pools.Remove(pKingdomId);
                return;
            }
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("levy_demobilize", pKingdomId),
                DeferredWorkClass.Runtime,
                () => DemobilizeBatch(pKingdomId));
        }

        private static void DemobilizeBatch(long pKingdomId)
        {
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data != null && MilitaryEmergencyService.HasAny(kingdom) &&
                !AutonomousRestorationService.IsActiveCampaignKingdom(kingdom)) return;
            if (!Pools.TryGetValue(pKingdomId, out LevyPool pool)) return;

            long[] batch = pool.DemobilizationBuffer;
            int count = 0;
            foreach (long actorId in pool.ActorIds)
            {
                batch[count++] = actorId;
                if (count >= batch.Length) break;
            }

            for (int i = 0; i < count; i++)
            {
                long actorId = batch[i];
                Actor actor = ResolveActor(actorId);
                if (actor?.data != null) DemobilizeActor(actor, pKingdomId);
                pool.ActorIds.Remove(actorId);
            }

            if (pool.ActorIds.Count == 0)
            {
                Pools.Remove(pKingdomId);
                return;
            }
            ScheduleDemobilization(pKingdomId);
        }

        private static void DemobilizeActor(Actor pActor, long pMobilizingKingdomId)
        {
            pActor.data.get(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, out long recordedKingdomId, -1L);
            bool sameKingdom = pActor.kingdom?.id == pMobilizingKingdomId && recordedKingdomId == pMobilizingKingdomId;
            bool living = !pActor.isRekt() && pActor.isAlive();
            if (sameKingdom && living)
            {
                if (pActor.isWarrior()) pActor.stopBeingWarrior();
                City destination = ResolveDemobilizationCity(pActor, pMobilizingKingdomId);
                if (destination?.data != null && pActor.city != destination)
                {
                    try { pActor.joinCity(destination); } catch { }
                }
                try { pActor.ai?.setJob(pActor.getNextJob()); } catch { }
                RecordDemobilizedDeferred(pActor.data.id, pMobilizingKingdomId,
                    destination?.id ?? pActor.city?.id ?? -1L);
            }
            ClearFields(pActor);
        }

        private static City ResolveDemobilizationCity(Actor pActor, long pKingdomId)
        {
            pActor.data.get(LineageKeys.TEMPORARY_LEVY_ORIGINAL_CITY_ID, out long originalCityId, -1L);
            City original = ResolveCity(originalCityId);
            if (original?.data != null && !original.isRekt() && original.kingdom?.id == pKingdomId) return original;
            if (pActor.city?.data != null && !pActor.city.isRekt() && pActor.city.kingdom?.id == pKingdomId)
                return pActor.city;
            return ResolveKingdom(pKingdomId)?.capital;
        }

        private static void ClearFields(Actor pActor)
        {
            if (pActor?.data == null) return;
            ActiveActorIds.Remove(pActor.data.id);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY, false);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_NOTICE_SIGNATURE, "");
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_ORIGINAL_CITY_ID, -1L);
            pActor.data.set(LineageKeys.TEMPORARY_LEVY_WAR_ID, -1L);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
        }

        private static bool HasPersistedFlag(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.TEMPORARY_LEVY, out bool active, false);
            return active;
        }

        private static LevyPool Pool(long pKingdomId)
        {
            if (!Pools.TryGetValue(pKingdomId, out LevyPool pool))
            {
                pool = new LevyPool();
                Pools[pKingdomId] = pool;
            }
            return pool;
        }

        private static void RecordEnlistedDeferred(long pActorId, long pKingdomId, long pCityId)
        {
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () =>
            {
                Actor actor = ResolveActor(pActorId);
                Kingdom kingdom = ResolveKingdom(pKingdomId);
                City city = ResolveCity(pCityId);
                if (actor?.data == null || kingdom?.data == null) return;
                HistoryWriter.RecordPerson(actor.data.id, kingdom, actor.getName(), "temporary_levy_enlisted",
                    HistoryText.Actor(actor) + HistoryLocalizationRules.H("aw_hist_temporary_levy_enlisted"),
                    ChronicleCategory.WAR, HistoryTarget.City(city));
            });
        }

        private static void RecordDemobilizedDeferred(long pActorId, long pKingdomId, long pCityId)
        {
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () =>
            {
                Actor actor = ResolveActor(pActorId);
                Kingdom kingdom = ResolveKingdom(pKingdomId);
                City city = ResolveCity(pCityId);
                if (actor?.data == null || kingdom?.data == null) return;
                HistoryWriter.RecordPerson(actor.data.id, kingdom, actor.getName(), "temporary_levy_demobilized",
                    HistoryText.Actor(actor) + HistoryLocalizationRules.H("aw_hist_temporary_levy_demobilized"),
                    ChronicleCategory.WAR, HistoryTarget.City(city));
            });
        }

        private static int PositiveModulo(int pValue, int pModulo)
        {
            if (pModulo <= 0) return 0;
            int result = pValue % pModulo;
            return result < 0 ? result + pModulo : result;
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Actor ResolveActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }
    }
}
