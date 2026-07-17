using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class RestorationUprisingMobilizationService
    {
        private sealed class CampaignState
        {
            public long CampaignId;
            public long KingdomId;
            public long SeedCityId;
            public long ArmyId = -1L;
            public int Year;
            public int WorkItems;
            public int Scanned;
            public int Recruited;
            public int ActorCursor;
            public bool Cleaning;
            public Kingdom KingdomRef;
            public readonly HashSet<long> MemberIds = new HashSet<long>();
            public readonly long[] MutationBuffer =
                new long[RestorationUprisingRules.DemobilizationBatchSize];
            public readonly List<long> StaleIds =
                new List<long>(RestorationUprisingRules.MaxActiveRecruitsPerCampaign);
        }

        private static readonly Dictionary<long, CampaignState> States =
            new Dictionary<long, CampaignState>();

        public static void Start(Kingdom pKingdom, City pSeed, long pCampaignId)
        {
            if (!IsLiveKingdom(pKingdom) || pSeed?.data == null || pSeed.isRekt() ||
                pSeed.kingdom != pKingdom || pCampaignId < 0) return;

            int year = Date.getCurrentYear();
            var state = new CampaignState
            {
                CampaignId = pCampaignId,
                KingdomId = pKingdom.id,
                SeedCityId = pSeed.id,
                Year = year,
                KingdomRef = pKingdom
            };
            States[pCampaignId] = state;
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ACTIVE, true);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID, pCampaignId);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_KINGDOM_ID, pKingdom.id);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_SEED_CITY_ID, pSeed.id);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ARMY_ID, -1L);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ROSTER_IDS, "");
            PersistCounters(pKingdom, state);
            ScheduleRecruitment(state);
        }

        public static void OnCampaignYear(Kingdom pKingdom, long pCampaignId)
        {
            if (!IsLiveKingdom(pKingdom) || pCampaignId < 0) return;
            CampaignState state = GetOrRestoreState(pKingdom, pCampaignId);
            if (state == null || state.Cleaning || !IsCampaignActive(pKingdom, pCampaignId)) return;

            int year = Date.getCurrentYear();
            if (state.Year != year)
            {
                state.Year = year;
                state.WorkItems = 0;
                state.Scanned = 0;
                state.Recruited = 0;
                ReconcileMembers(state, pKingdom);
                PersistCounters(pKingdom, state);
            }
            ScheduleRecruitment(state);
        }

        public static void Complete(Kingdom pKingdom, long pCampaignId)
        {
            BeginCleanup(pKingdom, pCampaignId);
        }

        public static void Fail(Kingdom pKingdom, long pCampaignId)
        {
            BeginCleanup(pKingdom, pCampaignId);
        }

        public static void RebuildRuntime()
        {
            States.Clear();
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null) continue;
                kingdom.data.get(LineageKeys.RESTORATION_UPRISING_ACTIVE,
                    out bool active, false);
                kingdom.data.get(LineageKeys.RESTORATION_UPRISING_ROSTER_IDS,
                    out string roster, "");
                if (!active && string.IsNullOrEmpty(roster)) continue;
                kingdom.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                    out long campaignId, -1L);
                if (campaignId >= 0) RebuildRuntime(kingdom, campaignId);
            }
        }

        public static void RebuildRuntime(Kingdom pKingdom, long pCampaignId)
        {
            if (pKingdom?.data == null || pCampaignId < 0) return;
            CampaignState state = RestoreState(pKingdom, pCampaignId);
            if (state == null) return;
            States[pCampaignId] = state;
            ReconcileMembers(state, pKingdom);
            if (IsCampaignActive(pKingdom, pCampaignId))
                OnCampaignYear(pKingdom, pCampaignId);
            else
                ScheduleCleanup(state);
        }

        public static void ClearRuntime()
        {
            States.Clear();
        }

        private static void ScheduleRecruitment(CampaignState pState)
        {
            if (pState == null || pState.Cleaning ||
                pState.MemberIds.Count >= RestorationUprisingRules.MaxActiveRecruitsPerCampaign ||
                !RestorationUprisingRules.ShouldRunRecruitmentWorkItem(true,
                    pState.WorkItems, pState.Scanned, pState.Recruited)) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "restoration_uprising_recruit", pState.CampaignId),
                DeferredWorkClass.Runtime,
                () => ProcessRecruitmentBatch(pState.CampaignId));
        }

        private static void ProcessRecruitmentBatch(long pCampaignId)
        {
            if (!States.TryGetValue(pCampaignId, out CampaignState state) || state.Cleaning) return;
            Kingdom kingdom = ResolveKingdom(state) ?? state.KingdomRef;
            if (!IsLiveKingdom(kingdom) || !IsCampaignActive(kingdom, pCampaignId))
            {
                BeginCleanup(kingdom, pCampaignId);
                return;
            }

            int year = Date.getCurrentYear();
            if (state.Year != year)
            {
                state.Year = year;
                state.WorkItems = 0;
                state.Scanned = 0;
                state.Recruited = 0;
                ReconcileMembers(state, kingdom);
            }
            if (state.MemberIds.Count >= RestorationUprisingRules.MaxActiveRecruitsPerCampaign ||
                !RestorationUprisingRules.ShouldRunRecruitmentWorkItem(true,
                    state.WorkItems, state.Scanned, state.Recruited)) return;

            City seed = ResolveCity(state.SeedCityId);
            int scanned = 0;
            int recruited = 0;
            if (seed?.data != null && !seed.isRekt() && seed.kingdom == kingdom)
                ScanSeedCity(state, kingdom, seed, ref scanned, ref recruited);
            if (recruited > 0) PublishArmyChanged(state, kingdom);
            state.WorkItems++;
            state.Scanned += scanned;
            state.Recruited += recruited;
            PersistState(kingdom, state);
            ScheduleRecruitment(state);
        }

        private static void ScanSeedCity(CampaignState pState, Kingdom pKingdom,
            City pSeed, ref int pScanned, ref int pRecruited)
        {
            int unitCount = pSeed.units?.Count ?? 0;
            if (unitCount <= 0) return;
            int cursor = PositiveModulo(pState.ActorCursor, unitCount);
            int annualCandidatesRemaining = Math.Max(0,
                RestorationUprisingRules.MaxCandidatesPerCampaignYear - pState.Scanned);
            int inspect = Math.Min(unitCount, Math.Min(
                RestorationUprisingRules.MaxCandidatesPerWorkItem,
                annualCandidatesRemaining));
            int annualRecruitsRemaining = Math.Max(0,
                RestorationUprisingRules.MaxRecruitsPerCampaignYear - pState.Recruited);
            int rosterRemaining = Math.Max(0,
                RestorationUprisingRules.MaxActiveRecruitsPerCampaign - pState.MemberIds.Count);
            int recruitLimit = Math.Min(RestorationUprisingRules.MaxRecruitsPerWorkItem,
                Math.Min(annualRecruitsRemaining, rosterRemaining));

            for (int i = 0; i < inspect && pRecruited < recruitLimit; i++)
            {
                int currentCount = pSeed.units?.Count ?? 0;
                if (currentCount <= 0) break;
                int index = PositiveModulo(cursor + i, currentCount);
                Actor actor = pSeed.units[index];
                pScanned++;
                if (!CanEnlist(pState, pKingdom, pSeed, actor)) continue;
                if (Enlist(pState, pKingdom, pSeed, actor)) pRecruited++;
            }
            pState.ActorCursor = PositiveModulo(cursor + pScanned,
                Math.Max(1, pSeed.units?.Count ?? unitCount));
        }

        private static bool CanEnlist(CampaignState pState, Kingdom pKingdom,
            City pCity, Actor pActor)
        {
            if (pActor?.data == null || pActor.city != pCity || pActor.kingdom != pKingdom ||
                pActor.isRekt() || !pActor.isAlive() || pActor.asset?.is_boat == true ||
                !pActor.isProfession(UnitProfession.Unit) || pState.MemberIds.Contains(pActor.data.id))
                return false;
            bool male;
            try { male = pActor.isSexMale(); }
            catch { male = false; }
            bool protectedIdentity = IsProtectedIdentity(pKingdom, pActor);
            bool originalEligible;
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.RestorationUprising))
                originalEligible = pCity.checkCanMakeWarrior(pActor);
            return RestorationUprisingRules.CanEnlist(originalEligible, protectedIdentity,
                male, pActor.isAdult(), pActor.getAge(), pActor.isWarrior());
        }

        private static bool Enlist(CampaignState pState, Kingdom pKingdom,
            City pCity, Actor pActor)
        {
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.RestorationUprising))
                pCity.makeWarrior(pActor);
            if (!pActor.isWarrior()) return false;
            Army army = EnsureArmy(pState, pKingdom, pCity, pActor);
            if (army?.data == null || pActor.army != army)
            {
                pActor.stopBeingWarrior();
                return false;
            }

            pActor.data.set(LineageKeys.RESTORATION_UPRISING_MEMBER, true);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID, pState.CampaignId);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_KINGDOM_ID, pState.KingdomId);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_ORIGINAL_CITY_ID, pCity.id);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_ARMY_ID, army.id);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            pState.MemberIds.Add(pActor.data.id);
            return true;
        }

        private static void PublishArmyChanged(CampaignState pState, Kingdom pKingdom)
        {
            Army army = ResolveArmy(pState?.ArmyId ?? -1L);
            if (!IsArmyOwnedBy(army, pKingdom)) return;
            WarNoticeService.QueueArmyChanged(pKingdom, army, pRosterExpanded: true);
        }

        private static Army EnsureArmy(CampaignState pState, Kingdom pKingdom,
            City pCity, Actor pRecruit)
        {
            Army army = ResolveArmy(pState.ArmyId);
            if (army?.data != null && !IsArmyOwnedBy(army, pKingdom))
            {
                DiscardForeignArmyReference(pState, pKingdom);
                army = null;
            }
            if (army?.data == null)
            {
                try
                {
                    if (pCity.hasArmy())
                    {
                        Army existing = pCity.getArmy();
                        if (existing?.data != null && !AWArmyService.IsSpecialArmy(existing) &&
                            IsArmyOwnedBy(existing, pKingdom))
                            army = existing;
                    }
                }
                catch { }
            }
            if (army?.data == null)
            {
                try { army = World.world?.armies?.newArmy(pRecruit, pCity); }
                catch { army = null; }
            }
            if (army?.data == null || !IsArmyOwnedBy(army, pKingdom)) return null;

            army.data.set(LineageKeys.RESTORATION_UPRISING_ARMY, true);
            army.data.set(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID, pState.CampaignId);
            army.data.set(LineageKeys.RESTORATION_UPRISING_KINGDOM_ID, pKingdom.id);
            pState.ArmyId = army.id;
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ARMY_ID, army.id);
            if (pRecruit.army != army) AWArmyService.AddToArmy(pRecruit, army);
            return pRecruit.army == army ? army : null;
        }

        private static void DiscardForeignArmyReference(CampaignState pState,
            Kingdom pKingdom)
        {
            ClearArmyFields(pState);
            pState.ArmyId = -1L;
            if (pKingdom?.data != null)
                pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ARMY_ID, -1L);
        }

        private static bool IsArmyOwnedBy(Army pArmy, Kingdom pKingdom)
        {
            if (pArmy?.data == null || pKingdom?.data == null) return false;
            try { return pArmy.getKingdom() == pKingdom; }
            catch { return false; }
        }

        private static bool IsProtectedIdentity(Kingdom pKingdom, Actor pActor)
        {
            if (pActor?.data == null) return true;
            if (pActor.isKing() || pActor.isCityLeader() ||
                HeirService.IsCurrentHeir(pKingdom, pActor)) return true;
            if (GeneralService.IsActiveGeneralFast(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) ||
                RoyalAsylumService.IsActive(pActor) ||
                SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return true;
            if (pActor.army != null && AWArmyService.IsSpecialArmy(pActor.army)) return true;
            if (!HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.OrdinaryWarrior)) return true;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            return !string.IsNullOrEmpty(office);
        }

        private static void BeginCleanup(Kingdom pKingdom, long pCampaignId)
        {
            if (pCampaignId < 0) return;
            CampaignState state = GetOrRestoreState(pKingdom, pCampaignId);
            if (state == null) return;
            state.Cleaning = true;
            if (pKingdom?.data != null)
                pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ACTIVE, false);
            if (state.MemberIds.Count == 0)
            {
                FinishCleanup(state, pKingdom ?? state.KingdomRef);
                return;
            }
            PersistRoster(pKingdom ?? state.KingdomRef, state);
            ScheduleCleanup(state);
        }

        private static void ScheduleCleanup(CampaignState pState)
        {
            if (pState == null) return;
            pState.Cleaning = true;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "restoration_uprising_cleanup", pState.CampaignId),
                DeferredWorkClass.Runtime,
                () => ProcessCleanupBatch(pState.CampaignId));
        }

        private static void ProcessCleanupBatch(long pCampaignId)
        {
            if (!States.TryGetValue(pCampaignId, out CampaignState state)) return;
            Kingdom kingdom = ResolveKingdom(state) ?? state.KingdomRef;
            int count = 0;
            foreach (long actorId in state.MemberIds)
            {
                state.MutationBuffer[count++] = actorId;
                if (count >= RestorationUprisingRules.DemobilizationBatchSize) break;
            }

            for (int i = 0; i < count; i++)
            {
                long actorId = state.MutationBuffer[i];
                Actor actor = ResolveActor(actorId);
                if (actor?.data != null) DemobilizeActor(state, kingdom, actor);
                state.MemberIds.Remove(actorId);
            }
            if (state.MemberIds.Count == 0)
            {
                FinishCleanup(state, kingdom);
                return;
            }
            PersistRoster(kingdom, state);
            ScheduleCleanup(state);
        }

        private static void DemobilizeActor(CampaignState pState, Kingdom pKingdom,
            Actor pActor)
        {
            pActor.data.get(LineageKeys.RESTORATION_UPRISING_MEMBER,
                out bool marked, false);
            pActor.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                out long campaignId, -1L);
            bool ownMarker = marked && campaignId == pState.CampaignId;
            bool living = !pActor.isRekt() && pActor.isAlive();
            bool sameKingdom = pActor.kingdom?.id == pState.KingdomId;
            bool protectedIdentity = living && IsProtectedIdentity(pKingdom, pActor);
            if (RestorationUprisingRules.ShouldDemobilizeActor(
                    ownMarker, living, sameKingdom, protectedIdentity))
            {
                if (pActor.isWarrior()) pActor.stopBeingWarrior();
                City destination = ResolveOriginalCity(pActor, pState.KingdomId);
                if (destination?.data != null && pActor.city != destination)
                {
                    try { pActor.joinCity(destination); }
                    catch { }
                }
                try { pActor.ai?.setJob(pActor.getNextJob()); }
                catch { }
            }
            if (campaignId == pState.CampaignId) ClearActorFields(pActor);
        }

        private static void FinishCleanup(CampaignState pState, Kingdom pKingdom)
        {
            ClearArmyFields(pState);
            ClearKingdomFields(pKingdom, pState.CampaignId);
            States.Remove(pState.CampaignId);
        }

        private static void ReconcileMembers(CampaignState pState, Kingdom pKingdom)
        {
            pState.StaleIds.Clear();
            foreach (long actorId in pState.MemberIds)
            {
                Actor actor = ResolveActor(actorId);
                if (IsCurrentMember(actor, pState)) continue;
                if (actor?.data != null)
                    ClearActorFieldsForCampaign(actor, pState.CampaignId);
                pState.StaleIds.Add(actorId);
            }
            for (int i = 0; i < pState.StaleIds.Count; i++)
                pState.MemberIds.Remove(pState.StaleIds[i]);
            if (pState.StaleIds.Count > 0) PersistRoster(pKingdom, pState);
        }

        private static bool IsCurrentMember(Actor pActor, CampaignState pState)
        {
            if (pActor?.data == null || pActor.isRekt() || !pActor.isAlive() ||
                pActor.kingdom?.id != pState.KingdomId) return false;
            pActor.data.get(LineageKeys.RESTORATION_UPRISING_MEMBER,
                out bool marked, false);
            pActor.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                out long campaignId, -1L);
            return marked && campaignId == pState.CampaignId;
        }

        private static CampaignState GetOrRestoreState(Kingdom pKingdom, long pCampaignId)
        {
            if (States.TryGetValue(pCampaignId, out CampaignState state))
            {
                if (pKingdom?.data != null) state.KingdomRef = pKingdom;
                return state;
            }
            state = RestoreState(pKingdom, pCampaignId);
            if (state != null) States[pCampaignId] = state;
            return state;
        }

        private static CampaignState RestoreState(Kingdom pKingdom, long pCampaignId)
        {
            if (pKingdom?.data == null || pCampaignId < 0) return null;
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                out long recordedCampaignId, -1L);
            if (recordedCampaignId != pCampaignId) return null;
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_SEED_CITY_ID,
                out long seedCityId, -1L);
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_ARMY_ID,
                out long armyId, -1L);
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_LAST_YEAR,
                out int year, Date.getCurrentYear());
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_WORK_ITEMS,
                out int workItems, 0);
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_SCANNED,
                out int scanned, 0);
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_RECRUITED,
                out int recruited, 0);
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_ACTOR_CURSOR,
                out int actorCursor, 0);
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_ROSTER_IDS,
                out string roster, "");
            var state = new CampaignState
            {
                CampaignId = pCampaignId,
                KingdomId = pKingdom.id,
                SeedCityId = seedCityId,
                ArmyId = armyId,
                Year = year,
                WorkItems = Clamp(workItems,
                    RestorationUprisingRules.MaxWorkItemsPerCampaignYear),
                Scanned = Clamp(scanned,
                    RestorationUprisingRules.MaxCandidatesPerCampaignYear),
                Recruited = Clamp(recruited,
                    RestorationUprisingRules.MaxRecruitsPerCampaignYear),
                ActorCursor = Math.Max(0, actorCursor),
                KingdomRef = pKingdom
            };
            DecodeRoster(roster, state.MemberIds);
            return state;
        }

        private static void PersistState(Kingdom pKingdom, CampaignState pState)
        {
            PersistCounters(pKingdom, pState);
            PersistRoster(pKingdom, pState);
        }

        private static void PersistCounters(Kingdom pKingdom, CampaignState pState)
        {
            if (pKingdom?.data == null || pState == null) return;
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_LAST_YEAR, pState.Year);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_WORK_ITEMS, pState.WorkItems);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_SCANNED, pState.Scanned);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_RECRUITED, pState.Recruited);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ACTOR_CURSOR, pState.ActorCursor);
        }

        private static void PersistRoster(Kingdom pKingdom, CampaignState pState)
        {
            if (pKingdom?.data == null || pState == null) return;
            var ordered = new List<long>(pState.MemberIds);
            ordered.Sort();
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ROSTER_IDS,
                string.Join(",", ordered));
        }

        private static void DecodeRoster(string pRaw, HashSet<long> pResult)
        {
            if (pResult == null || string.IsNullOrEmpty(pRaw)) return;
            foreach (string part in pRaw.Split(','))
            {
                if (pResult.Count >= RestorationUprisingRules.MaxActiveRecruitsPerCampaign) break;
                if (long.TryParse(part, out long id) && id >= 0) pResult.Add(id);
            }
        }

        private static bool IsCampaignActive(Kingdom pKingdom, long pCampaignId)
        {
            if (!IsLiveKingdom(pKingdom)) return false;
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_ACTIVE,
                out bool active, false);
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                out long campaignId, -1L);
            return active && campaignId == pCampaignId &&
                   AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom);
        }

        private static void ClearActorFields(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_MEMBER, false);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID, -1L);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_ORIGINAL_CITY_ID, -1L);
            pActor.data.set(LineageKeys.RESTORATION_UPRISING_ARMY_ID, -1L);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
        }

        private static void ClearActorFieldsForCampaign(Actor pActor, long pCampaignId)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                out long campaignId, -1L);
            if (campaignId == pCampaignId) ClearActorFields(pActor);
        }

        private static void ClearArmyFields(CampaignState pState)
        {
            Army army = ResolveArmy(pState?.ArmyId ?? -1L);
            if (army?.data == null || pState == null) return;
            army.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                out long campaignId, -1L);
            if (campaignId != pState.CampaignId) return;
            army.data.set(LineageKeys.RESTORATION_UPRISING_ARMY, false);
            army.data.set(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID, -1L);
            army.data.set(LineageKeys.RESTORATION_UPRISING_KINGDOM_ID, -1L);
        }

        private static void ClearKingdomFields(Kingdom pKingdom, long pCampaignId)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID,
                out long campaignId, -1L);
            if (campaignId != pCampaignId) return;
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ACTIVE, false);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_CAMPAIGN_ID, -1L);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_KINGDOM_ID, -1L);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_SEED_CITY_ID, -1L);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ARMY_ID, -1L);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ROSTER_IDS, "");
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_LAST_YEAR, -1);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_WORK_ITEMS, 0);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_SCANNED, 0);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_RECRUITED, 0);
            pKingdom.data.set(LineageKeys.RESTORATION_UPRISING_ACTOR_CURSOR, 0);
        }

        private static City ResolveOriginalCity(Actor pActor, long pKingdomId)
        {
            pActor.data.get(LineageKeys.RESTORATION_UPRISING_ORIGINAL_CITY_ID,
                out long cityId, -1L);
            City city = ResolveCity(cityId);
            if (city?.data != null && !city.isRekt() && city.kingdom?.id == pKingdomId)
                return city;
            if (pActor.city?.data != null && !pActor.city.isRekt() &&
                pActor.city.kingdom?.id == pKingdomId) return pActor.city;
            return ResolveKingdom(pKingdomId)?.capital;
        }

        private static Kingdom ResolveKingdom(CampaignState pState)
        {
            return ResolveKingdom(pState?.KingdomId ?? -1L);
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return pKingdomId >= 0 ? World.world?.kingdoms?.get(pKingdomId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pCityId)
        {
            try { return pCityId >= 0 ? World.world?.cities?.get(pCityId) : null; }
            catch { return null; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            try { return pActorId >= 0 ? World.world?.units?.get(pActorId) : null; }
            catch { return null; }
        }

        private static Army ResolveArmy(long pArmyId)
        {
            try { return pArmyId >= 0 ? World.world?.armies?.get(pArmyId) : null; }
            catch { return null; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() && !pKingdom.isNeutral();
        }

        private static int Clamp(int pValue, int pMaximum)
        {
            return Math.Max(0, Math.Min(Math.Max(0, pMaximum), pValue));
        }

        private static int PositiveModulo(int pValue, int pModulo)
        {
            if (pModulo <= 0) return 0;
            int result = pValue % pModulo;
            return result < 0 ? result + pModulo : result;
        }
    }
}
