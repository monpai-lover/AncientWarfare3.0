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
        private static HistoricalSchoolDescentLedger _ledger =
            new HistoricalSchoolDescentLedger();
        private static readonly Dictionary<long, string> MasterByActor =
            new Dictionary<long, string>();
        private static readonly Dictionary<long, int> HomeCounts =
            new Dictionary<long, int>();

        public static void LoadState()
        {
            _ledger = new HistoricalSchoolDescentLedger();
            MasterByActor.Clear();
            HomeCounts.Clear();
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
                City home = SelectHome(master, pLivingXiaCities);
                if (home == null || !TryDescend(master, home, pEligibleYear)) continue;
                spawned++;
            }
            return spawned;
        }

        public static void OnDeath(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive()) return;
            HistoricalSchoolMasterDefinition master = DefinitionFor(pActor);
            if (master == null) return;
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            if (affiliation?.LifecycleState == HistoricalSchoolLifecycleState.Serving)
            {
                Kingdom serviceKingdom = World.world?.kingdoms?.get(
                    affiliation.ServiceKingdomId);
                CourtService.EndGuestOfficer(pActor, serviceKingdom, "death",
                    Date.getCurrentYear());
            }
            City city = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor.city;
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string cause, "death");
            HistoricalSchoolStore.MarkMasterDead(master.Id, pActor.data.id,
                Date.getCurrentYear(), city?.data?.id ?? -1L, cause, WorldTime());
            HistoricalSchoolTravelService.OnDeath(pActor);
            SchoolLineageService.OnTeacherDeath(pActor);
            SchoolMembershipService.OnDeath(pActor);
            HistoricalSchoolContent.AnnounceDeath(pActor, city);
            HistoryWriter.RecordPerson(pActor.data.id,
                HistoricalAffiliationService.HomeKingdom(pActor), master.CanonicalName,
                "school_master_death", master.CanonicalName + "逝世", ChronicleCategory.LIFE);
            if (city?.data != null)
                HistoryWriter.RecordCity(city, city.kingdom, "school_master_death",
                    master.CanonicalName + "逝世");
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
            bool persistenceAttempted = false;
            SchoolPersistenceOutcome persistenceOutcome = SchoolPersistenceOutcome.Unknown;
            try
            {
                actor = World.world?.units?.createNewUnit(pMaster.ActorAssetId, tile,
                    pMiracleSpawn: false, 0f, FindXiaSubspecies(pHome), null,
                    pSpawnWithItems: true, pAdultAge: true);
                if (actor?.data == null || actor.isRekt())
                    throw new InvalidOperationException("actor creation failed");
                actor.joinCity(pHome);
                if (actor.data == null || !actor.isAlive() || actor.isRekt() ||
                    actor.city != pHome || actor.kingdom != pHome.kingdom ||
                    actor.current_tile == null)
                    throw new InvalidOperationException("actor home assignment failed");
                ApplyCanonicalIdentity(actor, pMaster);
                SchoolMembershipRecord membership =
                    SchoolMembershipService.PrepareHistoricalDescent(actor, pMaster.SchoolId,
                        pMaster.Id, pHome.data.id, 0);
                if (membership == null)
                    throw new InvalidOperationException("membership prepare rejected");
                persistenceAttempted = true;
                persistenceOutcome = HistoricalSchoolStore.CommitHistoricalDescent(pMaster,
                    membership, pHome.kingdom.id, pHome.kingdom.name, pHome.data.id,
                    Date.getCurrentYear(), WorldTime());
                if (persistenceOutcome != SchoolPersistenceOutcome.Committed)
                    throw new InvalidOperationException("historical descent persistence " +
                                                        persistenceOutcome);
                if (!SchoolMembershipService.AdoptCommittedHistoricalDescent(actor, membership))
                    throw new InvalidOperationException("committed membership adopt failed");
                if (!ReservePreservedActor(pMaster, actor, pHome, pEligibleYear))
                    throw new InvalidOperationException("duplicate descent ledger state");
                try
                {
                    LineageService.ArchiveActor(actor, pAlive: true);
                    HistoricalSchoolContent.AnnounceDescent(actor, pHome);
                    HistoryWriter.RecordPerson(actor.data.id, pHome.kingdom,
                        pMaster.CanonicalName, "school_master_descent",
                        pMaster.CanonicalName + "降临" + pHome.data.name,
                        ChronicleCategory.LIFE);
                    HistoryWriter.RecordCity(pHome, pHome.kingdom, "school_master_descent",
                        pMaster.CanonicalName + "降临此城");
                    ModClass.LogInfo("Historical school master descended: " + pMaster.Id +
                                     " actor=" + actor.data.id + " city=" + pHome.data.id);
                }
                catch (Exception sideEffectError)
                {
                    ModClass.LogWarning("Historical school descent announcement failed: " +
                                        sideEffectError.Message);
                }
                return true;
            }
            catch (Exception error)
            {
                bool canDestroy = !persistenceAttempted ||
                    HistoricalSchoolPersistenceRules.CanDestroy(persistenceOutcome);
                if (canDestroy)
                    RemoveFailedActor(actor);
                else
                    ReservePreservedActor(pMaster, actor, pHome, pEligibleYear);
                ModClass.LogWarning("Historical school descent failed: " + pMaster.Id + " - " +
                                    error.Message + " persistence=" + persistenceOutcome);
                return false;
            }
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

        private static void RemoveFailedActor(Actor pActor)
        {
            if (pActor == null) return;
            try
            {
                ActorManager units = World.world?.units;
                if (units == null || pActor.data == null) return;
                if (units.get(pActor.data.id) != pActor) return;
                pActor.setAlive(pValue: false);
                pActor.skipUpdates();
                World.world.units.scheduleDestroyOnPlay(pActor);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school failed actor removal failed: " +
                                    error.Message);
            }
        }

        private static void ApplyCanonicalIdentity(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster)
        {
            pActor.data.sex = pMaster.IsMale ? ActorSex.Male : ActorSex.Female;
            pActor.data.age_overgrowth = pMaster.SpawnAge;
            pActor.data.favorite = true;
            pActor.data.set(LineageKeys.SCHOOL_MASTER_ID, pMaster.Id);
            pActor.data.set(LineageKeys.GIVEN_NAME, pMaster.CanonicalName);
            pActor.data.set("aw_school_master_stewardship", pMaster.Abilities.Stewardship);
            pActor.data.set("aw_school_master_diplomacy", pMaster.Abilities.Diplomacy);
            pActor.data.set("aw_school_master_warfare", pMaster.Abilities.Warfare);
            pActor.data.set("aw_school_master_intelligence", pMaster.Abilities.Intelligence);
            pActor.setName(pMaster.CanonicalName);
            if (!pActor.hasTrait(HistoricalSchoolContent.MasterTraitId))
                pActor.addTrait(HistoricalSchoolContent.MasterTraitId);
            pActor.setStatsDirty();
            pActor.updateStats();
            pActor.setHealth(pActor.getMaxHealth());
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
