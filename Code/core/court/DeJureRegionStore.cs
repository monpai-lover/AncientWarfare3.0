using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionStore
    {
        private const string FileName = "aw3_de_jure_regions.json";
        private static readonly object Gate = new object();
        private static DeJureAdministrationStore _store;
        private static string _directory;
        private static bool _readAttempted;
        private static bool _migrationCompleted;
        private static long _nextChangeId = 1L;

        internal static long Revision
        {
            get { EnsureInitialized(); return _store?.StoreRevision ?? 0L; }
        }

        internal static void ObserveLoadDirectory(string pDirectory)
        {
            lock (Gate)
            {
                _directory = Normalize(pDirectory);
                _store = ReadFile(_directory) ??
                         new DeJureAdministrationStore();
                Normalize(_store);
                _readAttempted = true;
                _migrationCompleted = _store.Regions.Count > 0;
                _nextChangeId = 1L;
            }
        }

        internal static void PublishToSave(string pDirectory)
        {
            string directory = Normalize(pDirectory);
            if (string.IsNullOrEmpty(directory)) return;
            EnsureInitialized();
            lock (Gate) _directory = directory;
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, FileName);
                File.WriteAllText(path, JsonConvert.SerializeObject(
                    _store, Formatting.Indented));
            }
            catch (Exception error)
            {
                ModClass.LogError("De jure region save failed: " +
                                  error.Message);
            }
        }

        internal static void ClearForNewWorld()
        {
            lock (Gate)
            {
                _directory = null;
                _store = new DeJureAdministrationStore();
                _readAttempted = true;
                _migrationCompleted = false;
                _nextChangeId = 1L;
            }
            RegionalGovernmentAggregationService.Clear();
        }

        internal static void ClearRuntime()
        {
            lock (Gate)
            {
                _store = null;
                _directory = null;
                _readAttempted = false;
                _migrationCompleted = false;
                _nextChangeId = 1L;
            }
        }

        internal static IReadOnlyList<DeJureRegion> ActiveRegions()
        {
            EnsureInitialized();
            lock (Gate)
            {
                return (_store?.Regions ?? new List<DeJureRegion>())
                    .Where(p => p != null && p.Active &&
                                p.MemberCityIds != null &&
                                p.MemberCityIds.Count > 0)
                    .Select(CloneRegion).ToArray();
            }
        }

        internal static bool TryGetForCity(long pCityId,
            out DeJureRegion pRegion)
        {
            pRegion = null;
            if (pCityId < 0L) return false;
            EnsureInitialized();
            lock (Gate)
            {
                DeJureRegion found = _store?.Regions?.FirstOrDefault(p =>
                    p != null && p.Active && p.MemberCityIds != null &&
                    p.MemberCityIds.Contains(pCityId));
                if (found == null) return false;
                pRegion = CloneRegion(found);
                return true;
            }
        }

        internal static bool TryGetBySeat(long pSeatCityId,
            out DeJureRegion pRegion)
        {
            pRegion = null;
            EnsureInitialized();
            lock (Gate)
            {
                DeJureRegion found = _store?.Regions?.FirstOrDefault(p =>
                    p != null && p.Active && p.SeatCityId == pSeatCityId);
                if (found == null) return false;
                pRegion = CloneRegion(found);
                return true;
            }
        }

        internal static bool CreateState(City pCity, string pReason,
            out DeJureRegion pCreated, out string pError)
        {
            pCreated = null;
            pError = string.Empty;
            if (!IsLiveCity(pCity)) { pError = "invalid_city"; return false; }
            EnsureInitialized();
            lock (Gate)
            {
                DeJureAdministrationStore snapshot = CloneStore(_store);
                try
                {
                    RemoveFromCurrent(pCity.data.id, pReason ??
                        "power_create", out long fromRegionId);
                    int year = SafeYear();
                    long id = Math.Max(1L, _store.NextRegionId++);
                    var region = new DeJureRegion
                    {
                        RegionId = id,
                        RegionName = RegionalGovernmentRules.RegionName(
                            pCity.data.name ?? string.Empty, "州"),
                        SeatCityId = pCity.data.id,
                        CreatedYear = year,
                        CreatedByKind = pReason ?? "power_create",
                        CreatedByKingdomId = pCity.kingdom?.data?.id ?? -1L,
                        MemberCityIds = new List<long> { pCity.data.id }
                    };
                    if (string.IsNullOrWhiteSpace(region.RegionName))
                        region.RegionName = pCity.data.name ?? "州";
                    _store.Regions.Add(region);
                    AddChange(id, pCity.data.id, fromRegionId, id,
                        "DeJureRegionCreated");
                    _store.StoreRevision++;
                    RegionalGovernmentAggregationService.Clear();
                    pCreated = CloneRegion(region);
                    return true;
                }
                catch (Exception error)
                {
                    _store = snapshot;
                    pError = error.Message;
                    ModClass.LogError("De jure state creation failed: " +
                                      error.Message);
                    return false;
                }
            }
        }

        internal static bool AssignCity(long pTargetRegionId, City pCity,
            out string pError)
        {
            pError = string.Empty;
            if (!IsLiveCity(pCity)) { pError = "invalid_city"; return false; }
            EnsureInitialized();
            lock (Gate)
            {
                DeJureAdministrationStore snapshot = CloneStore(_store);
                try
                {
                    DeJureRegion target = _store.Regions.FirstOrDefault(p =>
                        p != null && p.Active && p.RegionId == pTargetRegionId);
                    if (target == null) { pError = "invalid_target"; return false; }
                    if (target.MemberCityIds.Contains(pCity.data.id)) return true;
                    RemoveFromCurrent(pCity.data.id, "power_assign",
                        out long fromRegionId);
                    target.MemberCityIds.Add(pCity.data.id);
                    target.MemberCityIds = target.MemberCityIds.Distinct().ToList();
                    AddChange(target.RegionId, pCity.data.id, fromRegionId,
                        target.RegionId, "DeJureCityTransferred");
                    _store.StoreRevision++;
                    RegionalGovernmentAggregationService.Clear();
                    return true;
                }
                catch (Exception error)
                {
                    _store = snapshot;
                    pError = error.Message;
                    ModClass.LogError("De jure city assignment failed: " +
                                      error.Message);
                    return false;
                }
            }
        }

        private static void EnsureInitialized()
        {
            lock (Gate)
            {
                if (_store == null && !_readAttempted)
                {
                    _readAttempted = true;
                    _store = ReadFile(_directory) ??
                             new DeJureAdministrationStore();
                    Normalize(_store);
                    _migrationCompleted = _store.Regions.Count > 0;
                }
                if (_store != null && !_migrationCompleted &&
                    _store.Regions.Count == 0 && HasLiveWorld() &&
                    Config.game_loaded && !SmoothLoader.isLoading())
                {
                    Migrate(_store);
                    _migrationCompleted = true;
                }
            }
        }

        private static DeJureAdministrationStore ReadFile(string pDirectory)
        {
            if (string.IsNullOrEmpty(pDirectory)) return null;
            try
            {
                string path = Path.Combine(pDirectory, FileName);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<
                    DeJureAdministrationStore>(File.ReadAllText(path));
            }
            catch (Exception error)
            {
                ModClass.LogError("De jure region load failed: " +
                                  error.Message);
                return null;
            }
        }

        private static void Migrate(DeJureAdministrationStore pStore)
        {
            var cities = new List<City>();
            var facts = new List<RegionalGovernmentCityFact>();
            if (World.world?.cities == null) return;
            foreach (City city in World.world.cities)
            {
                if (!IsLiveCity(city)) continue;
                cities.Add(city);
                var neighbors = new List<long>();
                if (city.neighbours_cities_kingdom != null)
                    foreach (City neighbor in city.neighbours_cities_kingdom)
                        if (IsLiveCity(neighbor) &&
                            neighbor.kingdom == city.kingdom)
                            neighbors.Add(neighbor.data.id);
                facts.Add(new RegionalGovernmentCityFact
                {
                    KingdomId = city.kingdom?.data?.id ?? -1L,
                    CityId = city.data.id,
                    CityName = city.data.name ?? string.Empty,
                    Development = DevelopmentMapModeService.GetCityScore(city),
                    Population = SafePopulation(city),
                    NeighborCityIds = neighbors.Distinct().ToArray()
                });
            }
            foreach (RegionalGovernmentFact group in RegionalGovernmentRules.
                     Build(facts, "州"))
            {
                var region = new DeJureRegion
                {
                    RegionId = pStore.NextRegionId++,
                    RegionName = RegionalGovernmentRules.RegionName(
                        group.SeatCityName, "州"),
                    SeatCityId = group.SeatCityId,
                    CreatedYear = SafeYear(),
                    CreatedByKind = "legacy_migration",
                    CreatedByKingdomId = group.KingdomId,
                    MemberCityIds = group.MemberCityIds.Distinct().ToList()
                };
                pStore.Regions.Add(region);
            }
            var assigned = new HashSet<long>(pStore.Regions.SelectMany(p =>
                p.MemberCityIds ?? new List<long>()));
            foreach (City city in cities)
            {
                if (assigned.Contains(city.data.id)) continue;
                pStore.Regions.Add(new DeJureRegion
                {
                    RegionId = pStore.NextRegionId++,
                    RegionName = RegionalGovernmentRules.RegionName(
                        city.data.name ?? string.Empty, "州"),
                    SeatCityId = city.data.id,
                    CreatedYear = SafeYear(),
                    CreatedByKind = "legacy_single_city",
                    CreatedByKingdomId = city.kingdom?.data?.id ?? -1L,
                    MemberCityIds = new List<long> { city.data.id }
                });
            }
            Normalize(pStore);
        }

        private static void RemoveFromCurrent(long pCityId, string pReason,
            out long pFromRegionId)
        {
            pFromRegionId = -1L;
            DeJureRegion current = _store.Regions.FirstOrDefault(p =>
                p != null && p.Active && p.MemberCityIds.Contains(pCityId));
            if (current == null) return;
            pFromRegionId = current.RegionId;
            current.MemberCityIds.Remove(pCityId);
            if (current.SeatCityId == pCityId && current.MemberCityIds.Count > 0)
            {
                current.SeatCityId = ChooseSeat(current.MemberCityIds);
                current.Version++;
                AddChange(current.RegionId, pCityId, current.RegionId,
                    current.RegionId, "DeJureSeatChanged");
            }
            if (current.MemberCityIds.Count == 0)
            {
                current.Active = false;
                current.Version++;
                AddChange(current.RegionId, pCityId, current.RegionId, -1L,
                    "DeJureRegionRetired");
            }
        }

        private static long ChooseSeat(IEnumerable<long> pCityIds)
        {
            return pCityIds.Select(id => World.world?.cities?.get(id))
                .Where(IsLiveCity)
                .OrderByDescending(DevelopmentMapModeService.GetCityScore)
                .ThenByDescending(SafePopulation)
                .ThenBy(p => p.data.id)
                .Select(p => p.data.id).DefaultIfEmpty(-1L).First();
        }

        private static void AddChange(long pRegionId, long pCityId,
            long pFrom, long pTo, string pReason)
        {
            _store.ChangeHistory.Add(new DeJureRegionChange
            {
                ChangeId = _nextChangeId++, RegionId = pRegionId,
                CityId = pCityId, FromRegionId = pFrom, ToRegionId = pTo,
                Reason = pReason ?? string.Empty, Year = SafeYear()
            });
        }

        private static void Normalize(DeJureAdministrationStore pStore)
        {
            if (pStore == null) return;
            pStore.Regions ??= new List<DeJureRegion>();
            pStore.ChangeHistory ??= new List<DeJureRegionChange>();
            pStore.OrphanedRecords ??= new List<string>();
            var seen = new HashSet<long>();
            foreach (DeJureRegion region in pStore.Regions)
            {
                if (region == null) continue;
                region.MemberCityIds ??= new List<long>();
                region.MemberCityIds = region.MemberCityIds.Distinct().ToList();
                if (!seen.Add(region.RegionId)) region.Active = false;
                if (region.RegionId >= pStore.NextRegionId)
                    pStore.NextRegionId = region.RegionId + 1L;
                if (region.MemberCityIds.Count == 0) region.Active = false;
                if (region.Active && !region.MemberCityIds.Contains(region.SeatCityId))
                    region.SeatCityId = region.MemberCityIds[0];
            }
            _nextChangeId = Math.Max(1L, pStore.ChangeHistory.Count == 0
                ? 1L : pStore.ChangeHistory.Max(p => p.ChangeId) + 1L);
        }

        private static DeJureAdministrationStore CloneStore(
            DeJureAdministrationStore pStore)
        {
            return JsonConvert.DeserializeObject<DeJureAdministrationStore>(
                JsonConvert.SerializeObject(pStore)) ??
                new DeJureAdministrationStore();
        }

        private static DeJureRegion CloneRegion(DeJureRegion pRegion)
        {
            return JsonConvert.DeserializeObject<DeJureRegion>(
                JsonConvert.SerializeObject(pRegion));
        }

        private static string Normalize(string pDirectory)
        {
            if (string.IsNullOrWhiteSpace(pDirectory)) return null;
            try { return Path.GetFullPath(pDirectory); }
            catch { return null; }
        }

        private static bool HasLiveWorld() => World.world?.cities != null &&
            World.world.cities.Count > 0;

        private static bool IsLiveCity(City pCity) => pCity?.data != null &&
            !pCity.isRekt() && pCity.data.id >= 0L;

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }
    }
}
