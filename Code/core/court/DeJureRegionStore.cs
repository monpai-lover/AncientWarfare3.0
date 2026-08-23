using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
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
        private static bool _emptyRegionRepairCompleted;
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
                _emptyRegionRepairCompleted = false;
                _nextChangeId = 1L;
                RepairEmptyRegionsLocked();
                EnsureAllKingdomCapitalSeatsLocked();
            }
            DeJureNewCityAssignmentService.ClearRuntime();
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
                _emptyRegionRepairCompleted = false;
                _nextChangeId = 1L;
            }
            RegionalGovernmentAggregationService.Clear();
            DeJureNewCityAssignmentService.ClearRuntime();
        }

        internal static void ClearRuntime()
        {
            lock (Gate)
            {
                _store = null;
                _directory = null;
                _readAttempted = false;
                _migrationCompleted = false;
                _emptyRegionRepairCompleted = false;
                _nextChangeId = 1L;
            }
            DeJureNewCityAssignmentService.ClearRuntime();
        }

        internal static void RepairAfterWorldLoaded()
        {
            try
            {
                EnsureInitialized();
                DeJureNewCityAssignmentService.RepairUnassignedCities();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("De jure post-load repair failed: " +
                    error.Message);
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
            City requested = World.world?.cities?.get(pCityId);
            if (requested?.data != null &&
                PeasantRebelBanditStrongholdService.IsStrongholdCity(
                    requested)) return false;
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

        internal static bool SyncSeatName(City pCity, string pCommittedName)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                string.IsNullOrWhiteSpace(pCommittedName)) return false;
            string regionName = RegionalGovernmentRules.RegionName(pCommittedName,
                "\u5dde");
            if (string.IsNullOrWhiteSpace(regionName)) return false;

            EnsureInitialized();
            lock (Gate)
            {
                DeJureRegion region = _store?.Regions?.FirstOrDefault(p =>
                    p != null && p.Active &&
                    p.SeatCityId == pCity.data.id);
                if (region == null || string.Equals(region.RegionName,
                        regionName, StringComparison.Ordinal)) return false;
                region.RegionName = regionName;
                region.Version++;
                AddChange(region.RegionId, pCity.data.id, region.RegionId,
                    region.RegionId, "DeJureRegionRenamedFromSeat");
                _store.StoreRevision++;
            }
            RegionalGovernmentAggregationService.Clear();
            return true;
        }

        internal static bool HasExplicitDeJureRemoval(long pCityId)
        {
            if (pCityId < 0L) return false;
            EnsureInitialized();
            lock (Gate) return HasExplicitDeJureRemovalLocked(pCityId);
        }

        internal static bool HasExplicitRegionRetirement(long pKingdomId)
        {
            if (pKingdomId < 0L) return false;
            EnsureInitialized();
            lock (Gate)
            {
                if (_store?.Regions == null || _store.ChangeHistory == null)
                    return false;
                return _store.Regions.Any(region =>
                    region != null && !region.Active &&
                    region.CreatedByKingdomId == pKingdomId &&
                    _store.ChangeHistory.Any(change =>
                        change != null && change.RegionId == region.RegionId &&
                        change.Reason == "DeJureRegionRetired"));
            }
        }

        internal static bool IsEligibleCityId(long pCityId)
        {
            if (pCityId < 0L) return false;
            City city = World.world?.cities?.get(pCityId);
            return IsDeJureEligibleCity(city);
        }

        internal static bool CreateState(City pCity, string pReason,
            out DeJureRegion pCreated, out string pError)
        {
            pCreated = null;
            pError = string.Empty;
            if (!IsDeJureEligibleCity(pCity)) { pError = "invalid_city"; return false; }
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
            if (!IsDeJureEligibleCity(pCity)) { pError = "invalid_city"; return false; }
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
                    if (pCity.kingdom?.capital == pCity)
                        target.SeatCityId = pCity.data.id;
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

        internal static bool AssignCityAutomatically(long pTargetRegionId,
            City pCity, string pReason, out string pError)
        {
            pError = string.Empty;
            if (!IsDeJureEligibleCity(pCity))
            {
                pError = "invalid_city";
                return false;
            }
            EnsureInitialized();
            lock (Gate)
            {
                DeJureAdministrationStore snapshot = CloneStore(_store);
                try
                {
                    DeJureRegion target = _store.Regions.FirstOrDefault(p =>
                        p != null && p.Active && p.RegionId == pTargetRegionId);
                    if (target == null)
                    {
                        pError = "invalid_target";
                        return false;
                    }
                    if (target.MemberCityIds.Contains(pCity.data.id)) return true;
                    if (_store.Regions.Any(p => p != null && p.Active &&
                        p.MemberCityIds != null &&
                        p.MemberCityIds.Contains(pCity.data.id)))
                    {
                        pError = "already_assigned";
                        return false;
                    }
                    target.MemberCityIds.Add(pCity.data.id);
                    target.MemberCityIds = target.MemberCityIds.Distinct().ToList();
                    if (pCity.kingdom?.capital == pCity)
                        target.SeatCityId = pCity.data.id;
                    target.Version++;
                    AddChange(target.RegionId, pCity.data.id, -1L,
                        target.RegionId, pReason ?? "city_created_auto_assign");
                    _store.StoreRevision++;
                    RegionalGovernmentAggregationService.Clear();
                    return true;
                }
                catch (Exception error)
                {
                    _store = snapshot;
                    pError = error.Message;
                    ModClass.LogError("Automatic de jure city assignment failed: " +
                                      error.Message);
                    return false;
                }
            }
        }

        internal static bool UnassignCity(City pCity, out string pError)
        {
            pError = string.Empty;
            if (!IsDeJureEligibleCity(pCity))
            {
                pError = "invalid_city";
                return false;
            }
            EnsureInitialized();
            lock (Gate)
            {
                DeJureAdministrationStore snapshot = CloneStore(_store);
                try
                {
                    DeJureRegion region = _store.Regions.FirstOrDefault(p =>
                        p != null && p.Active && p.MemberCityIds != null &&
                        p.MemberCityIds.Contains(pCity.data.id));
                    if (region == null)
                    {
                        pError = "region_missing";
                        return false;
                    }
                    if (region.SeatCityId == pCity.data.id)
                    {
                        pError = "region_capital";
                        return false;
                    }
                    region.MemberCityIds.Remove(pCity.data.id);
                    region.Version++;
                    AddChange(region.RegionId, pCity.data.id,
                        region.RegionId, -1L, "DeJureCityUnassigned");
                    _store.StoreRevision++;
                    RegionalGovernmentAggregationService.Clear();
                    return true;
                }
                catch (Exception error)
                {
                    _store = snapshot;
                    pError = error.Message;
                    ModClass.LogError(
                        "De jure city unassignment failed: " +
                        error.Message);
                    return false;
                }
            }
        }

        internal static bool TryMergeSingleCityRegions(Kingdom pKingdom,
            long pPrimaryRegionId, long pSecondaryRegionId,
            out string pError)
        {
            pError = string.Empty;
            if (pKingdom?.data == null || pKingdom.isRekt())
            {
                pError = "invalid_kingdom";
                return false;
            }

            EnsureInitialized();
            lock (Gate)
            {
                DeJureAdministrationStore snapshot = CloneStore(_store);
                long changeId = _nextChangeId;
                try
                {
                    DeJureRegion primary = _store?.Regions?.FirstOrDefault(
                        p => p != null && p.Active &&
                             p.RegionId == pPrimaryRegionId);
                    DeJureRegion secondary = _store?.Regions?.FirstOrDefault(
                        p => p != null && p.Active &&
                             p.RegionId == pSecondaryRegionId);
                    City primaryCity = SingleMemberCity(primary);
                    City secondaryCity = SingleMemberCity(secondary);
                    if (primary == null || secondary == null ||
                        primary == secondary || primaryCity == null ||
                        secondaryCity == null || primaryCity.kingdom != pKingdom ||
                        secondaryCity.kingdom != pKingdom ||
                        !AreAdjacent(primaryCity, secondaryCity) ||
                        !IsDeJureEligibleCity(primaryCity) ||
                        !IsDeJureEligibleCity(secondaryCity))
                    {
                        pError = "invalid_target";
                        return false;
                    }

                    primary.MemberCityIds.Add(secondaryCity.data.id);
                    primary.MemberCityIds = primary.MemberCityIds
                        .Distinct().ToList();
                    primary.Version++;
                    secondary.MemberCityIds.Clear();
                    secondary.Active = false;
                    secondary.Version++;
                    AddChange(primary.RegionId, secondaryCity.data.id,
                        secondary.RegionId, primary.RegionId,
                        "DeJureRegionMerged");
                    AddChange(secondary.RegionId, secondaryCity.data.id,
                        secondary.RegionId, -1L, "DeJureRegionRetired");
                    _store.StoreRevision++;
                    RegionalGovernmentAggregationService.Clear();
                    WarGoalPersistence.InvalidateOpenDeJureRegionGoals(
                        LineageArchiveManager.Instance?.OperatingDB,
                        primary.RegionId);
                    WarGoalPersistence.InvalidateOpenDeJureRegionGoals(
                        LineageArchiveManager.Instance?.OperatingDB,
                        secondary.RegionId);
                    HierarchicalVassalMapModeService.MarkHierarchyDirty(pKingdom);
                    HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
                    ModClass.LogInfo("De jure single-city region merge committed: " +
                        "kingdom=" + pKingdom.data.id +
                        ", primaryRegion=" + primary.RegionId +
                        ", retiredRegion=" + secondary.RegionId +
                        ", secondaryCity=" + secondaryCity.data.id +
                        ", revision=" + _store.StoreRevision);
                    return true;
                }
                catch (Exception error)
                {
                    _store = snapshot;
                    _nextChangeId = changeId;
                    pError = error.Message;
                    ModClass.LogError("De jure single-city region merge failed: " +
                                      error.Message);
                    return false;
                }
            }
        }

        internal static bool RetireState(City pSelectedCity,
            out string pError)
        {
            pError = string.Empty;
            if (!IsDeJureEligibleCity(pSelectedCity))
            {
                pError = "invalid_city";
                return false;
            }
            EnsureInitialized();
            lock (Gate)
            {
                DeJureAdministrationStore snapshot = CloneStore(_store);
                try
                {
                    DeJureRegion region = _store.Regions.FirstOrDefault(p =>
                        p != null && p.Active && p.MemberCityIds != null &&
                        p.MemberCityIds.Contains(pSelectedCity.data.id));
                    if (region == null)
                    {
                        pError = "region_missing";
                        return false;
                    }
                    if (region.SeatCityId != pSelectedCity.data.id)
                    {
                        pError = "region_capital_required";
                        return false;
                    }
                    List<long> members = region.MemberCityIds.Distinct().ToList();
                    region.MemberCityIds.Clear();
                    region.Active = false;
                    region.Version++;
                    foreach (long cityId in members)
                        AddChange(region.RegionId, cityId, region.RegionId, -1L,
                            "DeJureRegionRetired");
                    _store.StoreRevision++;
                    RegionalGovernmentAggregationService.Clear();
                    WarGoalPersistence.InvalidateOpenDeJureRegionGoals(
                        LineageArchiveManager.Instance?.OperatingDB,
                        region.RegionId);
                    return true;
                }
                catch (Exception error)
                {
                    _store = snapshot;
                    pError = error.Message;
                    ModClass.LogError("De jure state retirement failed: " +
                                      error.Message);
                    return false;
                }
            }
        }

        internal static void EnsureKingdomCapitalSeat(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            EnsureInitialized();
            lock (Gate) EnsureKingdomCapitalSeatLocked(pKingdom,
                "capital_region_repaired");
            RegionalGovernmentAggregationService.Clear();
        }

        private static void EnsureAllKingdomCapitalSeatsLocked()
        {
            try
            {
                List<Kingdom> kingdoms = World.world?.kingdoms?.list;
                if (kingdoms == null) return;
                foreach (Kingdom kingdom in kingdoms)
                    EnsureKingdomCapitalSeatLocked(kingdom,
                        "capital_region_repaired");
            }
            catch { }
        }

        // Old saves can contain active region shells without any live member.
        // Non-empty player-planned regions are deliberately preserved.
        private static void RepairEmptyRegionsLocked()
        {
            if (_emptyRegionRepairCompleted || _store?.Regions == null ||
                !HasLiveWorld()) return;
            _emptyRegionRepairCompleted = true;
            bool changed = false;
            foreach (DeJureRegion region in _store.Regions)
            {
                if (region == null || region.MemberCityIds == null) continue;
                bool hasLiveMember = region.MemberCityIds.Any(id =>
                    IsDeJureEligibleCity(World.world?.cities?.get(id)));
                if (!DeJureRegionRetirementRules.ShouldRepairEmptyRegion(
                        region.Active, hasLiveMember)) continue;
                region.Active = false;
                region.Version++;
                AddChange(region.RegionId, -1L, region.RegionId, -1L,
                    "DeJureEmptyRegionRepaired");
                changed = true;
            }
            if (!changed) return;
            _store.StoreRevision++;
            RegionalGovernmentAggregationService.Clear();
        }

        private static void EnsureKingdomCapitalSeatLocked(Kingdom pKingdom,
            string pReason)
        {
            City capital = pKingdom?.capital;
            if (!IsDeJureEligibleCity(capital)) return;
            DeJureRegion current = _store.Regions.FirstOrDefault(p =>
                p != null && p.Active && p.MemberCityIds != null &&
                p.MemberCityIds.Contains(capital.data.id));
            if (current != null)
            {
                if (current.SeatCityId != capital.data.id)
                {
                    current.SeatCityId = capital.data.id;
                    current.Version++;
                    AddChange(current.RegionId, capital.data.id,
                        current.RegionId, current.RegionId,
                        "DeJureSeatChanged");
                    _store.StoreRevision++;
                }
                return;
            }
            bool explicitlyRemoved = HasExplicitDeJureRemovalLocked(
                capital.data.id);
            if (!DeJureRegionRetirementRules.ShouldAutoCreateCapitalSeat(
                    hasCurrentRegion: false, explicitlyRemoved)) return;
            long id = Math.Max(1L, _store.NextRegionId++);
            var region = new DeJureRegion
            {
                RegionId = id,
                RegionName = RegionalGovernmentRules.RegionName(
                    capital.data.name ?? string.Empty, "州"),
                SeatCityId = capital.data.id,
                CreatedYear = SafeYear(),
                CreatedByKind = pReason ?? "capital_region_repaired",
                CreatedByKingdomId = pKingdom.id,
                MemberCityIds = new List<long> { capital.data.id }
            };
            _store.Regions.Add(region);
            AddChange(id, capital.data.id, -1L, id,
                pReason ?? "capital_region_repaired");
            _store.StoreRevision++;
        }

        private static bool HasExplicitDeJureRemovalLocked(long pCityId)
        {
            DeJureRegionChange latest = _store?.ChangeHistory?
                .LastOrDefault(p => p != null && p.CityId == pCityId);
            if (latest == null || latest.ToRegionId >= 0L) return false;
            return latest.Reason == "DeJureCityUnassigned" ||
                   latest.Reason == "DeJureRegionRetired";
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
                    _emptyRegionRepairCompleted = false;
                    RepairEmptyRegionsLocked();
                }
                if (_store != null && !_migrationCompleted &&
                    _store.Regions.Count == 0 && HasLiveWorld() &&
                    Config.game_loaded && !SmoothLoader.isLoading())
                {
                    Migrate(_store);
                    _migrationCompleted = true;
                }
                if (_store != null && HasLiveWorld() && Config.game_loaded &&
                    !SmoothLoader.isLoading())
                {
                    RepairEmptyRegionsLocked();
                    EnsureAllKingdomCapitalSeatsLocked();
                }
            }
            if (_store != null && HasLiveWorld() && Config.game_loaded &&
                !SmoothLoader.isLoading())
                DeJureNewCityAssignmentService.RepairUnassignedCities();
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
                if (!IsDeJureEligibleCity(city)) continue;
                cities.Add(city);
                var neighbors = new List<long>();
                if (city.neighbours_cities_kingdom != null)
                    foreach (City neighbor in city.neighbours_cities_kingdom)
                        if (IsDeJureEligibleCity(neighbor) &&
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

        private static City SingleMemberCity(DeJureRegion pRegion)
        {
            if (pRegion?.MemberCityIds == null ||
                pRegion.MemberCityIds.Count != 1) return null;
            try { return World.world?.cities?.get(pRegion.MemberCityIds[0]); }
            catch { return null; }
        }

        private static bool AreAdjacent(City pLeft, City pRight)
        {
            if (pLeft?.data == null || pRight?.data == null) return false;
            try
            {
                return pLeft.neighbours_cities != null &&
                       pRight.neighbours_cities != null &&
                       pLeft.neighbours_cities.Contains(pRight) &&
                       pRight.neighbours_cities.Contains(pLeft);
            }
            catch { return false; }
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

        private static bool IsDeJureEligibleCity(City pCity)
        {
            if (!IsLiveCity(pCity)) return false;
            try
            {
                return DeJureRegionEligibilityRules.CanParticipate(
                    liveCity: true,
                    banditStronghold:
                    PeasantRebelBanditStrongholdService.IsStrongholdCity(
                        pCity));
            }
            catch { return true; }
        }

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
