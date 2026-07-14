using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolDescentService
    {
        private sealed class PendingHistoricalDescent
        {
            public PendingHistoricalDescent(Actor pActor,
                HistoricalSchoolMasterDefinition pMaster, SchoolMembershipRecord pMembership,
                long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId,
                int pPersistenceYear, double pPersistenceTime, int pEligibleYear,
                HistoricalMasterLineageCommitIdentity pIdentity)
            {
                Actor = pActor;
                Master = pMaster;
                Membership = pMembership;
                HomeKingdomId = pHomeKingdomId;
                HomeKingdomName = pHomeKingdomName ?? "";
                HometownCityId = pHometownCityId;
                PersistenceYear = pPersistenceYear;
                PersistenceTime = pPersistenceTime;
                EligibleYear = pEligibleYear;
                Identity = pIdentity;
            }

            public Actor Actor { get; }
            public HistoricalSchoolMasterDefinition Master { get; }
            public SchoolMembershipRecord Membership { get; }
            public long HomeKingdomId { get; }
            public string HomeKingdomName { get; }
            public long HometownCityId { get; }
            public int PersistenceYear { get; }
            public double PersistenceTime { get; }
            public int EligibleYear { get; }
            public HistoricalMasterLineageCommitIdentity Identity { get; }
            public int Attempts { get; set; }
            public long ReadyFrame { get; set; }
            public bool DestroyRequested { get; set; }
        }

        private static HistoricalSchoolDescentLedger _ledger =
            new HistoricalSchoolDescentLedger();
        private static readonly Dictionary<long, string> MasterByActor =
            new Dictionary<long, string>();
        private static readonly Dictionary<long, int> HomeCounts =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, PendingHistoricalDescent>
            PendingDescentsByActor = new Dictionary<long, PendingHistoricalDescent>();
        private static readonly Dictionary<string, long> PendingActorByMaster =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Queue<long> PendingDescentRetries = new Queue<long>();
        private const int MaxPendingDescentRetryBackoffFrames = 240;
        private const int MaxPendingDescentRetryQueueScan = 16;
        private static long _pendingDescentRetryFrame;

        internal static bool HasPendingDescents => PendingDescentsByActor.Count > 0;

        public static void LoadState()
        {
            ClearRuntime();
            foreach (HistoricalSchoolMasterStoreRecord row in
                     HistoricalSchoolStore.LoadMasterStates())
            {
                HistoricalSchoolMasterDefinition master =
                    HistoricalSchoolMasterRegistry.Find(row.MasterId);
                if (master == null || !row.Spawned) continue;
                _ledger.MarkSpawned(master, row.SpawnYear);
                if (row.ActorId >= 0) MasterByActor[row.ActorId] = master.Id;
                if (row.HomeKingdomId >= 0)
                    HomeCounts[row.HomeKingdomId] = HomeCount(row.HomeKingdomId) + 1;
                try
                {
                    Actor actor = row.ActorId >= 0
                        ? World.world?.units?.get(row.ActorId)
                        : null;
                    if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                        row.LineageId < 0 || row.ShiId < 0 || row.CreatedTime < 0d)
                        continue;
                    var identity = new HistoricalMasterLineageCommitIdentity(row.ActorId,
                        master.CanonicalName, master.CanonicalShiName,
                        master.CanonicalGivenName, master.CanonicalFamilyName,
                        master.FamilyEvidence,
                        row.HomeKingdomId, row.HometownCityId, row.CreatedTime);
                    identity.FreezeIds(row.LineageId, row.ShiId);
                    if (!HistoricalMasterIdentityProjection.TryApply(actor, master, identity))
                        ModClass.LogWarning("Historical school master identity repair pending: " +
                                            master.Id);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Historical school master identity repair failed: " +
                                        master.Id + " - " + error.Message);
                }
            }
            try
            {
                if (World.world?.units == null) return;
                foreach (Actor actor in World.world.units)
                {
                    if (actor?.data == null || !actor.isAlive() || actor.isRekt()) continue;
                    actor.data.get(LineageKeys.SCHOOL_MASTER_ID, out string masterId, "");
                    HistoricalSchoolMasterDefinition master =
                        HistoricalSchoolMasterRegistry.Find(masterId);
                    if (master == null) continue;
                    bool missingFromLedger = !_ledger.IsSpawned(master.Id);
                    ReservePreservedActor(master, actor, actor.city, Date.getCurrentYear());
                    if (missingFromLedger)
                        ModClass.LogWarning("Preserved unknown historical school actor reserved: " +
                                            master.Id + " actor=" + actor.data.id);
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school preserved actor scan failed: " +
                                    error.Message);
            }
        }

        public static void ClearRuntime()
        {
            _ledger = new HistoricalSchoolDescentLedger();
            MasterByActor.Clear();
            HomeCounts.Clear();
            PendingDescentsByActor.Clear();
            PendingActorByMaster.Clear();
            PendingDescentRetries.Clear();
            _pendingDescentRetryFrame = 0L;
        }

        public static bool IsCanonicalMaster(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.SCHOOL_MASTER_ID, out string masterId, "");
            if (HistoricalSchoolMasterRegistry.Find(masterId) != null) return true;
            return MasterByActor.TryGetValue(pActor.data.id, out masterId) &&
                   HistoricalSchoolMasterRegistry.Find(masterId) != null;
        }

        public static HistoricalSchoolMasterDefinition DefinitionFor(Actor pActor)
        {
            if (pActor?.data == null) return null;
            pActor.data.get(LineageKeys.SCHOOL_MASTER_ID, out string masterId, "");
            if (string.IsNullOrEmpty(masterId)) MasterByActor.TryGetValue(pActor.data.id,
                out masterId);
            return HistoricalSchoolMasterRegistry.Find(masterId);
        }

        public static int ProcessDue(int pEligibleYear, IReadOnlyList<City> pLivingXiaCities)
        {
            if (pLivingXiaCities == null || pLivingXiaCities.Count == 0) return 0;
            IReadOnlyList<HistoricalSchoolMasterDefinition> due =
                HistoricalSchoolRules.SelectDue(pEligibleYear, _ledger,
                    HistoricalSchoolRules.MaxDescentsPerEligibleYear);
            if (due.Count == 0) return 0;
            int spawned = 0;
            foreach (HistoricalSchoolMasterDefinition master in due)
            {
                if (HasPendingDescents) break;
                City home = SelectHome(master, pLivingXiaCities);
                if (home == null || !TryDescend(master, home, pEligibleYear)) continue;
                spawned++;
            }
            return spawned;
        }

        public static void OnCommittedDeath(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster, City pCity)
        {
            if (pActor?.data == null || pMaster == null) return;
            HistoricalSchoolContent.AnnounceDeath(pActor, pCity);
            HistoryWriter.RecordPerson(pActor.data.id,
                HistoricalAffiliationService.HomeKingdom(pActor), pMaster.CanonicalName,
                "school_master_death", pMaster.CanonicalName + "逝世", ChronicleCategory.LIFE);
            if (pCity?.data != null)
                HistoryWriter.RecordCity(pCity, pCity.kingdom, "school_master_death",
                    pMaster.CanonicalName + "逝世");
        }

        private static City SelectHome(HistoricalSchoolMasterDefinition pMaster,
            IReadOnlyList<City> pCities)
        {
            var candidates = new List<HistoricalSchoolHomeCandidate>(pCities.Count);
            var byCity = new Dictionary<long, City>();
            foreach (City city in pCities)
            {
                Kingdom kingdom = city?.kingdom;
                if (city?.data == null || city.isRekt() || kingdom?.data == null ||
                    kingdom.isRekt() || !LineageService.IsXiaKingdom(kingdom)) continue;
                int population = SafePopulation(city);
                float development = population + SafeZones(city) * 8f + SafeBuildings(city) * 3f;
                candidates.Add(new HistoricalSchoolHomeCandidate(kingdom.id, city.data.id,
                    kingdom.name, pLivingXia: true, HomeCount(kingdom.id),
                    kingdom.capital == city, development, population));
                byCity[city.data.id] = city;
            }
            HistoricalSchoolHomeCandidate selected = HistoricalSchoolRules.SelectHome(pMaster,
                candidates);
            return selected != null && byCity.TryGetValue(selected.CityId, out City cityResult)
                ? cityResult
                : null;
        }

        private static bool TryDescend(HistoricalSchoolMasterDefinition pMaster, City pHome,
            int pEligibleYear)
        {
            WorldTile tile = pHome?.getTile();
            if (tile == null || pHome.kingdom?.data == null || pHome.kingdom.isRekt()) return false;
            Actor actor = null;
            SchoolMembershipRecord membership = null;
            HistoricalMasterLineageCommitIdentity identity = null;
            bool persistenceAttempted = false;
            SchoolPersistenceOutcome persistenceOutcome = SchoolPersistenceOutcome.Unknown;
            long homeKingdomId = pHome.kingdom.id;
            string homeKingdomName = pHome.kingdom.name;
            long hometownCityId = pHome.data.id;
            int persistenceYear = Date.getCurrentYear();
            double persistenceTime = WorldTime();
            try
            {
                ActorManager units = World.world?.units;
                using (HistoricalSchoolActorSpawnCapture capture =
                       HistoricalSchoolActorSpawnCapture.Begin(units))
                {
                    try
                    {
                        actor = units?.createNewUnit(pMaster.ActorAssetId, tile,
                            pMiracleSpawn: false, 0f, FindXiaSubspecies(pHome), null,
                            pSpawnWithItems: true, pAdultAge: true);
                    }
                    catch
                    {
                        actor = capture.CapturedActor;
                        throw;
                    }
                }
                if (actor?.data == null || actor.isRekt())
                    throw new InvalidOperationException("actor creation failed");
                actor.joinCity(pHome);
                if (actor.data == null || !actor.isAlive() || actor.isRekt() ||
                    actor.city != pHome || actor.kingdom != pHome.kingdom ||
                    actor.current_tile == null)
                    throw new InvalidOperationException("actor home assignment failed");
                identity = new HistoricalMasterLineageCommitIdentity(actor.data.id,
                    pMaster.CanonicalName, pMaster.CanonicalShiName,
                    pMaster.CanonicalGivenName, pMaster.CanonicalFamilyName,
                    pMaster.FamilyEvidence,
                    homeKingdomId, hometownCityId, persistenceTime);
                membership = SchoolMembershipService.PrepareHistoricalDescent(actor,
                    pMaster.SchoolId, pMaster.Id, hometownCityId, 0);
                if (membership == null)
                    throw new InvalidOperationException("membership prepare rejected");
                persistenceAttempted = true;
                persistenceOutcome = HistoricalSchoolStore.CommitHistoricalDescent(pMaster,
                    membership, homeKingdomId, homeKingdomName, hometownCityId,
                    persistenceYear, persistenceTime, identity);
                if (persistenceOutcome != SchoolPersistenceOutcome.Committed)
                    throw new InvalidOperationException("historical descent persistence " +
                                                        persistenceOutcome);
                if (!SchoolMembershipService.AdoptCommittedHistoricalDescent(actor, membership))
                    throw new InvalidOperationException("committed membership adopt failed");
                if (!ProjectCommittedIdentity(actor, pMaster, identity))
                    throw new InvalidOperationException("committed identity projection failed");
                if (!ReservePreservedActor(pMaster, actor, pHome, pEligibleYear))
                    throw new InvalidOperationException("duplicate descent ledger state");
                AnnounceReconciledDescent(actor, pMaster, pHome);
                return true;
            }
            catch (Exception error)
            {
                if (persistenceAttempted && membership != null &&
                    persistenceOutcome != SchoolPersistenceOutcome.CleanFailure)
                {
                    QueuePendingDescent(actor, pMaster, membership, homeKingdomId,
                        homeKingdomName, hometownCityId, persistenceYear, persistenceTime,
                        pEligibleYear, identity);
                }
                else
                {
                    RemoveFailedActor(actor, pMaster.ActorAssetId);
                }
                ModClass.LogWarning("Historical school descent failed: " + pMaster.Id + " - " +
                                    error.Message + " persistence=" + persistenceOutcome);
                return false;
            }
        }

        private static void QueuePendingDescent(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster, SchoolMembershipRecord pMembership,
            long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId,
            int pPersistenceYear, double pPersistenceTime, int pEligibleYear,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            if (pActor?.data == null || pMaster == null || pMembership == null ||
                pIdentity == null) return;
            long actorId = pActor.data.id;
            if (PendingDescentsByActor.ContainsKey(actorId)) return;
            var pending = new PendingHistoricalDescent(pActor, pMaster, pMembership,
                pHomeKingdomId, pHomeKingdomName, pHometownCityId, pPersistenceYear,
                pPersistenceTime, pEligibleYear, pIdentity);
            PendingDescentsByActor[actorId] = pending;
            PendingActorByMaster[pMaster.Id] = actorId;
            PendingDescentRetries.Enqueue(actorId);
            MasterByActor[actorId] = pMaster.Id;
            if (pHomeKingdomId >= 0)
                HomeCounts[pHomeKingdomId] = HomeCount(pHomeKingdomId) + 1;
        }

        internal static void ProcessPendingDescentReconciliations()
        {
            ProcessPendingDescentReconciliation(pIgnoreBackoff: false);
        }

        internal static bool FlushPendingDescentsForSchoolWrite()
        {
            return FlushPendingDescents(32);
        }

        internal static bool FlushPendingDescentsForSave()
        {
            return FlushPendingDescents(64);
        }

        private static bool FlushPendingDescents(int pMaximumAttempts)
        {
            int budget = Math.Min(pMaximumAttempts,
                Math.Max(8, PendingDescentsByActor.Count * 8));
            while (PendingDescentsByActor.Count > 0 && budget-- > 0)
                ProcessPendingDescentReconciliation(pIgnoreBackoff: true);
            return PendingDescentsByActor.Count == 0;
        }

        private static void ProcessPendingDescentReconciliation(bool pIgnoreBackoff)
        {
            _pendingDescentRetryFrame++;
            int scanBudget = Math.Min(PendingDescentRetries.Count,
                MaxPendingDescentRetryQueueScan);
            while (scanBudget-- > 0)
            {
                long actorId = PendingDescentRetries.Dequeue();
                if (!PendingDescentsByActor.TryGetValue(actorId,
                        out PendingHistoricalDescent pending)) continue;
                if (!pIgnoreBackoff && pending.ReadyFrame > _pendingDescentRetryFrame)
                {
                    PendingDescentRetries.Enqueue(actorId);
                    continue;
                }
                try
                {
                    ReconcilePendingDescent(pending);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Pending historical descent reconciliation failed: " +
                                        error.Message);
                    if (PendingDescentsByActor.TryGetValue(actorId,
                            out PendingHistoricalDescent current) &&
                        ReferenceEquals(current, pending))
                        RequeuePendingDescent(pending);
                }
                return;
            }
        }

        private static void ReconcilePendingDescent(PendingHistoricalDescent pending)
        {
            SchoolPersistenceOutcome outcome =
                HistoricalSchoolStore.CommitHistoricalDescent(pending.Master,
                    pending.Membership, pending.HomeKingdomId, pending.HomeKingdomName,
                    pending.HometownCityId, pending.PersistenceYear,
                    pending.PersistenceTime, pending.Identity);
            if (outcome == SchoolPersistenceOutcome.Unknown)
            {
                RequeuePendingDescent(pending);
                return;
            }
            if (outcome == SchoolPersistenceOutcome.CleanFailure)
            {
                RemovePendingDescent(pending, pReleaseReservation: true);
                RemoveFailedActor(pending.Actor, pending.Master.ActorAssetId);
                return;
            }
            if (outcome != SchoolPersistenceOutcome.Committed ||
                !SchoolMembershipService.AdoptCommittedHistoricalDescent(pending.Actor,
                    pending.Membership) ||
                !ProjectCommittedIdentity(pending.Actor, pending.Master,
                    pending.Identity))
            {
                RequeuePendingDescent(pending);
                return;
            }

            if (!_ledger.IsSpawned(pending.Master.Id) &&
                !_ledger.MarkSpawned(pending.Master, pending.EligibleYear))
            {
                RequeuePendingDescent(pending);
                return;
            }
            HistoricalAffiliationService.RegisterDescent(pending.Membership.ActorId,
                pending.HomeKingdomId, pending.HomeKingdomName, pending.HometownCityId,
                pending.PersistenceYear);
            bool destroyRequested = pending.DestroyRequested;
            RemovePendingDescent(pending, pReleaseReservation: false);
            City home = FindCity(pending.HometownCityId) ?? pending.Actor?.city;
            AnnounceReconciledDescent(pending.Actor, pending.Master, home);

            bool actorDead = pending.Actor?.data != null &&
                (!pending.Actor.isAlive() || pending.Actor.isRekt());
            if (actorDead)
            {
                SchoolDeathOutcome deathOutcome =
                    SchoolMembershipService.OnDeath(pending.Actor, pDestroy: true);
                if (deathOutcome == SchoolDeathOutcome.Committed ||
                    deathOutcome == SchoolDeathOutcome.NotApplicable)
                    HistoricalSchoolActorDestroyQueue.Queue(pending.Actor,
                        "Reconciled historical descent destroy requeue failed");
            }
            else if (destroyRequested)
            {
                HistoricalSchoolActorDestroyQueue.Queue(pending.Actor,
                    "Reconciled historical descent destroy requeue failed");
            }
        }

        private static void RemovePendingDescent(PendingHistoricalDescent pPending,
            bool pReleaseReservation)
        {
            if (pPending == null) return;
            long actorId = pPending.Membership.ActorId;
            PendingDescentsByActor.Remove(actorId);
            if (PendingActorByMaster.TryGetValue(pPending.Master.Id, out long reservedActor) &&
                reservedActor == actorId)
                PendingActorByMaster.Remove(pPending.Master.Id);
            if (!pReleaseReservation) return;
            if (MasterByActor.TryGetValue(actorId, out string masterId) &&
                masterId == pPending.Master.Id)
                MasterByActor.Remove(actorId);
            int count = HomeCount(pPending.HomeKingdomId);
            if (count <= 1) HomeCounts.Remove(pPending.HomeKingdomId);
            else HomeCounts[pPending.HomeKingdomId] = count - 1;
        }

        private static void RequeuePendingDescent(PendingHistoricalDescent pPending)
        {
            pPending.Attempts++;
            int exponent = Math.Max(0, Math.Min(8, pPending.Attempts - 1));
            int delay = Math.Min(MaxPendingDescentRetryBackoffFrames, 1 << exponent);
            pPending.ReadyFrame = _pendingDescentRetryFrame + delay;
            PendingDescentRetries.Enqueue(pPending.Membership.ActorId);
        }

        internal static bool ShouldDeferDestroy(Actor pActor)
        {
            if (pActor?.data == null ||
                !PendingDescentsByActor.TryGetValue(pActor.data.id,
                    out PendingHistoricalDescent pending) ||
                !ReferenceEquals(pending.Actor, pActor)) return false;
            pending.DestroyRequested = true;
            return true;
        }

        private static void AnnounceReconciledDescent(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster, City pHome)
        {
            if (pActor?.data == null || pMaster == null) return;
            try
            {
                if (pActor.isAlive() && !pActor.isRekt())
                    LineageService.ArchiveActor(pActor, pAlive: true);
                HistoricalSchoolContent.AnnounceDescent(pActor, pHome);
                HistoryWriter.RecordPerson(pActor.data.id, pHome?.kingdom,
                    pMaster.CanonicalName, "school_master_descent",
                    pMaster.CanonicalName + "降临" + (pHome?.data?.name ?? ""),
                    ChronicleCategory.LIFE);
                if (pHome?.data != null)
                    HistoryWriter.RecordCity(pHome, pHome.kingdom, "school_master_descent",
                        pMaster.CanonicalName + "降临此城");
                ModClass.LogInfo("Historical school master descended: " + pMaster.Id +
                                 " actor=" + pActor.data.id + " city=" +
                                 (pHome?.data?.id ?? -1L));
            }
            catch (Exception sideEffectError)
            {
                ModClass.LogWarning("Historical school descent announcement failed: " +
                                    sideEffectError.Message);
            }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static bool ReservePreservedActor(HistoricalSchoolMasterDefinition pMaster,
            Actor pActor, City pHome, int pEligibleYear)
        {
            if (pMaster == null || pActor?.data == null) return false;
            try
            {
                bool newlyReserved = !_ledger.IsSpawned(pMaster.Id);
                if (newlyReserved && !_ledger.MarkSpawned(pMaster, pEligibleYear)) return false;
                MasterByActor[pActor.data.id] = pMaster.Id;

                City home = pHome?.data != null && !pHome.isRekt() ? pHome : pActor.city;
                Kingdom kingdom = home?.kingdom?.data != null && !home.kingdom.isRekt()
                    ? home.kingdom
                    : pActor.kingdom;
                if (newlyReserved && kingdom?.data != null && !kingdom.isRekt())
                    HomeCounts[kingdom.id] = HomeCount(kingdom.id) + 1;
                if (HistoricalAffiliationService.Get(pActor.data.id) == null &&
                    home?.data != null && !home.isRekt() && kingdom?.data != null &&
                    !kingdom.isRekt())
                    HistoricalAffiliationService.RegisterDescent(pActor.data.id, kingdom.id,
                        kingdom.name, home.data.id, Date.getCurrentYear());
                return newlyReserved;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school preserved actor reservation failed: " +
                                    error.Message);
                return false;
            }
        }

        private static void RemoveFailedActor(Actor pActor, string pExpectedActorAssetId)
        {
            if (pActor == null) return;
            try
            {
                ActorManager units = World.world?.units;
                HistoricalSchoolFailedActorCleanup.Remove(units, pActor,
                    pExpectedActorAssetId);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school failed actor removal failed: " +
                                    error.Message);
            }
        }

        private static bool ProjectCommittedIdentity(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            return HistoricalMasterIdentityProjection.TryApply(pActor, pMaster, pIdentity);
        }

        private static Subspecies FindXiaSubspecies(City pCity)
        {
            try
            {
                foreach (Actor actor in pCity.units)
                    if (LineageService.IsXia(actor) && actor.subspecies != null &&
                        !actor.subspecies.isRekt()) return actor.subspecies;
            }
            catch { }
            return null;
        }

        private static int HomeCount(long pKingdomId)
        {
            return HomeCounts.TryGetValue(pKingdomId, out int count) ? count : 0;
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeZones(City pCity)
        {
            try { return pCity?.countZones() ?? 0; }
            catch { return 0; }
        }

        private static int SafeBuildings(City pCity)
        {
            try { return pCity?.buildings?.Count ?? 0; }
            catch { return 0; }
        }

        private static double WorldTime()
        {
            return World.world?.getCurWorldTime() ?? 0d;
        }
    }
}
