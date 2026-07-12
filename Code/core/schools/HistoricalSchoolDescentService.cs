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
            City city = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor.city;
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string cause, "death");
            HistoricalSchoolStore.MarkMasterDead(master.Id, pActor.data.id,
                Date.getCurrentYear(), city?.data?.id ?? -1L, cause, WorldTime());
            HistoricalSchoolTravelService.OnDeath(pActor);
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
            bool membershipOpened = false;
            bool descentRecorded = false;
            try
            {
                actor = World.world?.units?.createNewUnit(pMaster.ActorAssetId, tile,
                    pMiracleSpawn: false, 0f, FindXiaSubspecies(pHome), null,
                    pSpawnWithItems: true, pAdultAge: true);
                if (actor?.data == null || actor.isRekt())
                    throw new InvalidOperationException("actor creation failed");
                actor.joinCity(pHome);
                ApplyCanonicalIdentity(actor, pMaster);
                membershipOpened = SchoolMembershipService.TryJoin(actor, pMaster.SchoolId,
                    SchoolMembershipSource.HistoricalDescent, pMaster.Id, -1,
                    pHome.data.id, 0);
                if (!membershipOpened) throw new InvalidOperationException("membership rejected");
                if (!HistoricalSchoolStore.TryRecordDescent(pMaster, actor.data.id,
                        pHome.kingdom.id, pHome.kingdom.name, pHome.data.id,
                        Date.getCurrentYear(), WorldTime()))
                    throw new InvalidOperationException("TryRecordDescent failed");
                descentRecorded = true;
                HistoricalAffiliationService.RegisterDescent(actor.data.id, pHome.kingdom.id,
                    pHome.kingdom.name, pHome.data.id, Date.getCurrentYear());
                if (!_ledger.MarkSpawned(pMaster, pEligibleYear))
                    throw new InvalidOperationException("duplicate descent ledger state");

                MasterByActor[actor.data.id] = pMaster.Id;
                HomeCounts[pHome.kingdom.id] = HomeCount(pHome.kingdom.id) + 1;
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
                if (descentRecorded && actor?.data != null)
                {
                    HistoricalSchoolStore.RollbackDescent(pMaster.Id, actor.data.id);
                    HistoricalAffiliationService.RollbackDescent(actor.data.id);
                }
                if (membershipOpened && actor?.data != null)
                    SchoolMembershipService.RollbackJoin(actor, pMaster.Id);
                try { actor?.Dispose(); } catch { }
                ModClass.LogWarning("Historical school descent failed: " + pMaster.Id + " - " +
                                    error.Message);
                return false;
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
