using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.performance;
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
        private static bool _worldLoadRepairCompleted;
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
                _worldLoadRepairCompleted = false;
                _nextChangeId = 1L;
            }
            DeJureRegionMaintenanceService.ClearRuntime();
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
                _worldLoadRepairCompleted = false;
                _nextChangeId = 1L;
            }
            DeJureRegionMaintenanceService.ClearRuntime();
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
                _worldLoadRepairCompleted = false;
                _nextChangeId = 1L;
            }
            DeJureRegionMaintenanceService.ClearRuntime();
            DeJureNewCityAssignmentService.ClearRuntime();
        }

        internal static void RepairAfterWorldLoaded()
        {
            bool continuityChanged = false;
            try
            {
                EnsureInitialized();
                lock (Gate)
                {
                    if (_worldLoadRepairCompleted || !HasLiveWorld() ||
                        !Config.game_loaded || SmoothLoader.isLoading())
                        return;
                    RepairEmptyRegionsLocked();
                    EnsureAllKingdomCapitalSeatsLocked();
                    if (!_migrationCompleted)
                    {
                        Migrate(_store);
                        _migrationCompleted = true;
                    }
                    continuityChanged = RepairDisconnectedRegionsLocked();
                    if (continuityChanged) _store.StoreRevision++;
                    MigrateHistoricalMetadataLocked();
                    _worldLoadRepairCompleted = true;
                }
                if (continuityChanged)
                {
                    RegionalGovernmentAggregationService.Clear();
                    HierarchicalVassalMapModeService.MarkHierarchyDirty();
                    HierarchicalVassalMapModeService.
                        RefreshAfterDeJureMutation();
                }
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

        internal static string ResolveDisplayName(DeJureRegion pRegion)
        {
            return pRegion?.RegionName ?? string.Empty;
        }

        internal static bool SyncSeatName(City pCity, string pCommittedName,
            bool pTrackedRename)
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
                if (region == null) return false;
                if (!CityStateRenameRules.ShouldSyncStateName(
                        pIsSeat: true, pTrackedRename, region.SeatLocked))
                    return false;
                if (string.Equals(region.RegionName, regionName,
                        StringComparison.Ordinal)) return false;
                region.RegionName = regionName;
                region.RegionNameSource =
                    DeJureHistoricalProfileRules.ManualSeatRename;
                region.Version++;
                AddChange(region.RegionId, pCity.data.id, region.RegionId,
                    region.RegionId, "DeJureRegionRenamedFromSeat");
                _store.StoreRevision++;
                DeJureRegionMaintenanceService.MarkRegionDirty(
                    region.RegionId, DeJureDirtyReason.Name);
            }
            RegionalGovernmentAggregationService.Clear();
            return true;
        }

        internal static bool TryRenameRegion(long pRegionId, long pCityId,
            string pRequestedName, out string pError)
        {
            pError = string.Empty;
            string regionName = CityStateRenameRules.Normalize(
                pRequestedName);
            if (pRegionId < 0L || pCityId < 0L || regionName.Length == 0)
            {
                pError = "invalid_region_name";
                return false;
            }

            EnsureInitialized();
            bool changed = false;
            lock (Gate)
            {
                DeJureRegion region = _store?.Regions?.FirstOrDefault(p =>
                    p != null && p.Active && p.RegionId == pRegionId);
                if (region == null || region.MemberCityIds == null ||
                    !region.MemberCityIds.Contains(pCityId))
                {
                    pError = "invalid_region";
                    return false;
                }
                if (string.Equals(region.RegionName, regionName,
                        StringComparison.Ordinal)) return true;
                region.RegionName = regionName;
                region.RegionNameSource =
                    DeJureHistoricalProfileRules.ManualSeatRename;
                region.Version++;
                AddChange(region.RegionId, pCityId, region.RegionId,
                    region.RegionId, "DeJureRegionRenamedByPlayer");
                _store.StoreRevision++;
                DeJureRegionMaintenanceService.MarkRegionDirty(
                    region.RegionId, DeJureDirtyReason.Name);
                changed = true;
            }
            if (changed)
            {
                RegionalGovernmentAggregationService.Clear();
                HierarchicalVassalMapModeService.MarkHierarchyDirty();
                HierarchicalVassalMapModeService.
                    RefreshAfterDeJureMutation();
            }
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
                    string regionName = ResolveCreatedRegionNameLocked(pCity,
                        out string stateId, out string commanderyId);
                    var region = new DeJureRegion
                    {
                        RegionId = id,
                        RegionName = regionName,
                        HistoricalStateId = stateId,
                        HistoricalCommanderyId = commanderyId,
                        SeatCityId = pCity.data.id,
                        SeatLocked = true,
                        RegionNameSource = stateId.Length > 0
                            ? DeJureHistoricalProfileRules.HistoricalDefault
                            : DeJureHistoricalProfileRules.LegacyPreserved,
                        CreatedYear = year,
                        CreatedByKind = pReason ?? "power_create",
                        CreatedByKingdomId = pCity.kingdom?.data?.id ?? -1L,
                        MemberCityIds = new List<long> { pCity.data.id }
                    };
                    if (string.IsNullOrWhiteSpace(region.RegionName))
                        region.RegionName = ResolveCountyNameForPresentation(
                            pCity) ?? "州";
                    _store.Regions.Add(region);
                    AddChange(id, pCity.data.id, fromRegionId, id,
                        "DeJureRegionCreated");
                    _store.StoreRevision++;
                    DeJureRegionMaintenanceService.MarkRegionDirty(id,
                        DeJureDirtyReason.Membership);
                    if (pCity.kingdom?.data != null)
                        DeJureRegionMaintenanceService.MarkKingdomDirty(
                            pCity.kingdom.data.id, DeJureDirtyReason.CityRoster);
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
                    DeJureRegionMaintenanceService.MarkRegionDirty(
                        target.RegionId, DeJureDirtyReason.Membership);
                    if (fromRegionId >= 0L)
                        DeJureRegionMaintenanceService.MarkRegionDirty(
                            fromRegionId, DeJureDirtyReason.Membership);
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
                    // Automatic assignment may only join a region that still
                    // has a live member owned by the new city's kingdom.  A
                    // global nearest-region scan must never merge countries.
                    bool sameKingdomMember = target.MemberCityIds != null &&
                        target.MemberCityIds.Any(id =>
                        {
                            City member = World.world?.cities?.get(id);
                            return member?.data != null && !member.isRekt() &&
                                member.kingdom == pCity.kingdom;
                        });
                    if (!sameKingdomMember)
                    {
                        pError = "invalid_target_kingdom";
                        return false;
                    }
                    if (target.MemberCityIds.Contains(pCity.data.id)) return true;
                    int liveMemberCount = (target.MemberCityIds ??
                        new List<long>()).Count(id =>
                        IsDeJureEligibleCity(World.world?.cities?.get(id)));
                    if (liveMemberCount >= RegionalGovernmentRules.
                        MaximumRegionCityCount)
                    {
                        pError = "region_capacity";
                        return false;
                    }
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
                    DeJureRegionMaintenanceService.MarkRegionDirty(
                        target.RegionId, DeJureDirtyReason.Membership);
                    if (pCity.kingdom?.data != null)
                        DeJureRegionMaintenanceService.MarkKingdomDirty(
                            pCity.kingdom.data.id, DeJureDirtyReason.CityRoster);
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
                    DeJureRegionMaintenanceService.MarkRegionDirty(
                        region.RegionId, DeJureDirtyReason.Membership);
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
                    DeJureRegionMaintenanceService.MarkRegionDirty(
                        primary.RegionId, DeJureDirtyReason.Merge);
                    DeJureRegionMaintenanceService.MarkRegionDirty(
                        secondary.RegionId, DeJureDirtyReason.Merge |
                        DeJureDirtyReason.Retirement);
                    DeJureRegionMaintenanceService.MarkKingdomDirty(
                        pKingdom.data.id, DeJureDirtyReason.CityRoster);
                    RegionalGovernmentAggregationService.Clear();
                    WarGoalPersistence.InvalidateOpenDeJureRegionGoals(
                        LineageArchiveManager.Instance?.OperatingDB,
                        primary.RegionId, LineageService.CurTime());
                    WarGoalPersistence.InvalidateOpenDeJureRegionGoals(
                        LineageArchiveManager.Instance?.OperatingDB,
                        secondary.RegionId, LineageService.CurTime());
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
                    DeJureRegionMaintenanceService.MarkRegionDirty(
                        region.RegionId, DeJureDirtyReason.Retirement);
                    if (pSelectedCity.kingdom?.data != null)
                        DeJureRegionMaintenanceService.MarkKingdomDirty(
                            pSelectedCity.kingdom.data.id,
                            DeJureDirtyReason.CityRoster);
                    RegionalGovernmentAggregationService.Clear();
                    WarGoalPersistence.InvalidateOpenDeJureRegionGoals(
                        LineageArchiveManager.Instance?.OperatingDB,
                        region.RegionId, LineageService.CurTime());
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
            DeJureRegionMaintenanceService.MarkKingdomDirty(
                pKingdom.data.id, DeJureDirtyReason.Capital);
            RegionalGovernmentAggregationService.Clear();
        }

        internal static bool ProcessDirtyKingdom(long pKingdomId,
            DeJureDirtyReason pReason)
        {
            if (pKingdomId < 0L) return true;
            Kingdom kingdom = World.world?.kingdoms?.get(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt()) return false;
            EnsureInitialized();
            bool changed = false;
            lock (Gate)
            {
                if (_store == null) return false;
                DeJureAdministrationStore snapshot = CloneStore(_store);
                long changeId = _nextChangeId;
                try
                {
                    long before = _store.StoreRevision;
                    EnsureKingdomCapitalSeatLocked(kingdom,
                        "dirty_kingdom_" + pReason);
                    changed = _store.StoreRevision != before;
                }
                catch
                {
                    _store = snapshot;
                    _nextChangeId = changeId;
                    throw;
                }
            }
            if (changed)
            {
                RegionalGovernmentAggregationService.Clear();
                HierarchicalVassalMapModeService.MarkHierarchyDirty(kingdom);
                HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
            }
            return true;
        }

        internal static bool ProcessDirtyRegion(long pRegionId,
            DeJureDirtyReason pReason)
        {
            if (pRegionId < 0L) return true;
            EnsureInitialized();
            bool changed = false;
            lock (Gate)
            {
                DeJureAdministrationStore snapshot = CloneStore(_store);
                long changeId = _nextChangeId;
                try
                {
                DeJureRegion region = _store?.Regions?.FirstOrDefault(p =>
                    p != null && p.RegionId == pRegionId);
                if (region == null) return false;
                if (!region.Active) return true;
                if (!HasLiveWorld()) return false;
                if (RepairDisconnectedRegionsLocked(pRegionId))
                    changed = true;
                region.MemberCityIds ??= new List<long>();
                region.MemberCityIds = region.MemberCityIds.Distinct().ToList();
                bool hasLiveMember = region.MemberCityIds.Any(id =>
                    IsDeJureEligibleCity(World.world?.cities?.get(id)));
                if (!hasLiveMember)
                {
                    region.Active = false;
                    region.Version++;
                    AddChange(region.RegionId, -1L, region.RegionId, -1L,
                        "DeJureEmptyRegionRepaired");
                    changed = true;
                }
                else if (!region.MemberCityIds.Contains(region.SeatCityId))
                {
                    long seat = ChooseSeat(region.MemberCityIds);
                    if (seat >= 0L && seat != region.SeatCityId)
                    {
                        region.SeatCityId = seat;
                        region.Version++;
                        AddChange(region.RegionId, seat, region.RegionId,
                            region.RegionId, "DeJureSeatChanged");
                        changed = true;
                    }
                }
                if (changed) _store.StoreRevision++;
                }
                catch
                {
                    _store = snapshot;
                    _nextChangeId = changeId;
                    throw;
                }
            }
            if (changed)
            {
                RegionalGovernmentAggregationService.Clear();
                HierarchicalVassalMapModeService.MarkHierarchyDirty();
                HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
                WarGoalPersistence.InvalidateOpenDeJureRegionGoals(
                    LineageArchiveManager.Instance?.OperatingDB,
                    pRegionId, LineageService.CurTime());
            }
            return true;
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
            City capital = SelectHighestDevelopmentAnchor(pKingdom);
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
            string capitalRegionName = ResolveCreatedRegionNameLocked(capital,
                out string capitalStateId, out string capitalCommanderyId);
            var region = new DeJureRegion
            {
                RegionId = id,
                RegionName = capitalRegionName,
                HistoricalStateId = capitalStateId,
                HistoricalCommanderyId = capitalCommanderyId,
                SeatCityId = capital.data.id,
                SeatLocked = true,
                RegionNameSource = capitalStateId.Length > 0
                    ? DeJureHistoricalProfileRules.HistoricalDefault
                    : DeJureHistoricalProfileRules.LegacyPreserved,
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

        private static City SelectHighestDevelopmentAnchor(Kingdom pKingdom)
        {
            bool hasKingdomRegion = _store?.Regions?.Any(region =>
                region != null && region.Active &&
                (region.MemberCityIds ?? new List<long>()).Any(id =>
                {
                    City member = World.world?.cities?.get(id);
                    return IsDeJureEligibleCity(member) &&
                        member.kingdom == pKingdom;
                })) == true;
            if (hasKingdomRegion && pKingdom?.capital != null &&
                IsDeJureEligibleCity(pKingdom.capital))
                return pKingdom.capital;
            try
            {
                IEnumerable<City> cities = pKingdom?.getCities();
                return (cities ?? Enumerable.Empty<City>())
                    .Where(IsDeJureEligibleCity)
                    .OrderByDescending(DevelopmentMapModeService.GetCityScore)
                    .ThenByDescending(SafePopulation)
                    .ThenBy(city => city.data.id)
                    .FirstOrDefault();
            }
            catch { return null; }
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
            if (pStore?.Regions == null || World.world?.cities == null) return;

            // At migration time the capital regions have already been created
            // by EnsureAllKingdomCapitalSeatsLocked. Keep the legacy grouping
            // shape for multi-city states, but never use a city as an implicit
            // state seat merely because it was left over from that grouping.
            var assigned = new HashSet<long>(pStore.Regions
                .Where(region => region != null && region.Active)
                .SelectMany(region => region.MemberCityIds ??
                    new List<long>()));
            var cities = new Dictionary<long, City>();
            var facts = new List<RegionalGovernmentCityFact>();
            foreach (City city in World.world.cities)
            {
                if (!IsDeJureEligibleCity(city) || city.kingdom?.data == null)
                    continue;
                cities[city.data.id] = city;
                if (assigned.Contains(city.data.id)) continue;
                var neighbors = new List<long>();
                if (city.neighbours_cities_kingdom != null)
                    foreach (City neighbor in city.neighbours_cities_kingdom)
                        if (IsDeJureEligibleCity(neighbor) &&
                            neighbor.kingdom == city.kingdom)
                            neighbors.Add(neighbor.data.id);
                facts.Add(new RegionalGovernmentCityFact
                {
                    KingdomId = city.kingdom.data.id,
                    CityId = city.data.id,
                    CityName = ResolveCountyNameForPresentation(city),
                    Development = DevelopmentMapModeService.GetCityScore(city),
                    Population = SafePopulation(city),
                    NeighborCityIds = neighbors.Distinct().ToArray()
                });
            }

            var deferredSingles = new List<RegionalGovernmentFact>();
            foreach (IGrouping<long, RegionalGovernmentCityFact> kingdomFacts in
                     facts.GroupBy(fact => fact.KingdomId))
            {
                foreach (RegionalGovernmentFact group in RegionalGovernmentRules
                         .Build(kingdomFacts, "州"))
                {
                    if (group.MemberCityIds == null ||
                        group.MemberCityIds.Count < 2)
                    {
                        deferredSingles.Add(group);
                        continue;
                    }
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
                    assigned.UnionWith(region.MemberCityIds);
                }
            }

            // Prefer attaching one-city clusters to an adjacent-seat region,
            // then the nearest same-kingdom region with free capacity. Only
            // an isolated cluster with no compatible capacity becomes a new
            // one-city region, preventing cities from being dropped entirely.
            foreach (RegionalGovernmentFact singleton in deferredSingles)
            {
                if (singleton?.MemberCityIds == null ||
                    singleton.MemberCityIds.Count == 0) continue;
                City city = cities.TryGetValue(singleton.MemberCityIds[0],
                    out City resolved) ? resolved : null;
                if (city == null) continue;
                DeJureRegion target = FindMigrationTargetLocked(pStore, city);
                if (target == null)
                {
                    target = new DeJureRegion
                    {
                        RegionId = pStore.NextRegionId++,
                        RegionName = RegionalGovernmentRules.RegionName(
                            ResolveCountyNameForPresentation(city), "州"),
                        SeatCityId = city.data.id,
                        CreatedYear = SafeYear(),
                        CreatedByKind = "legacy_migration_isolated",
                        CreatedByKingdomId = city.kingdom.data.id,
                        MemberCityIds = new List<long> { city.data.id }
                    };
                    pStore.Regions.Add(target);
                    AddChange(target.RegionId, city.data.id, -1L,
                        target.RegionId, "legacy_migration_isolated");
                    assigned.Add(city.data.id);
                    continue;
                }
                target.MemberCityIds ??= new List<long>();
                target.MemberCityIds.Add(city.data.id);
                target.MemberCityIds = target.MemberCityIds.Distinct().ToList();
                target.Version++;
                assigned.Add(city.data.id);
            }
            Normalize(pStore);
        }

        private static DeJureRegion FindMigrationTargetLocked(
            DeJureAdministrationStore pStore, City pCity)
        {
            if (pStore?.Regions == null || pCity?.kingdom?.data == null)
                return null;
            City capital = pCity.kingdom.capital;
            return pStore.Regions.Where(region => region != null &&
                    region.Active && region.MemberCityIds != null &&
                    region.MemberCityIds.Count < RegionalGovernmentRules.
                        MaximumRegionCityCount &&
                    region.MemberCityIds.Any(id =>
                    {
                        City member = World.world?.cities?.get(id);
                        return IsDeJureEligibleCity(member) &&
                            member.kingdom == pCity.kingdom;
                    }))
                .OrderByDescending(region => AreAdjacent(pCity,
                    World.world?.cities?.get(region.SeatCityId)))
                .ThenBy(region => DistanceSquared(pCity,
                    World.world?.cities?.get(region.SeatCityId)))
                .ThenByDescending(region => region.SeatCityId ==
                    capital?.data?.id)
                .ThenBy(region => region.RegionId)
                .FirstOrDefault();
        }

        private static bool RepairDisconnectedRegionsLocked()
        {
            return RepairDisconnectedRegionsLocked(-1L);
        }

        private static bool RepairDisconnectedRegionsLocked(long pRegionId)
        {
            if (_store?.Regions == null || !HasLiveWorld()) return false;
            var detached = new List<(City City, long FromRegionId)>();
            bool changed = false;
            foreach (DeJureRegion region in _store.Regions
                         .Where(item => item != null && item.Active &&
                             (pRegionId < 0L || item.RegionId == pRegionId))
                         .OrderBy(item => item.RegionId).ToArray())
            {
                List<City> members = (region.MemberCityIds ?? new List<long>())
                    .Select(id => World.world?.cities?.get(id))
                    .Where(IsDeJureEligibleCity)
                    .OrderBy(city => city.data.id).ToList();
                if (members.Count <= 1) continue;
                if (!PrepareRegionNeighbours(members)) continue;
                long seatId = members.Any(city =>
                        city.data.id == region.SeatCityId)
                    ? region.SeatCityId
                    : ChooseSeat(members.Select(city => city.data.id));
                var adjacency = members.ToDictionary(
                    city => city.data.id,
                    city => (IReadOnlyCollection<long>)members
                        .Where(other => other != city &&
                            AreAdjacent(city, other))
                        .Select(other => other.data.id).OrderBy(id => id)
                        .ToArray());
                var retained = new HashSet<long>(
                    DeJureRegionContinuityRules.SelectConnectedMembers(
                        seatId, members.Select(city => city.data.id),
                        adjacency,
                        RegionalGovernmentRules.MaximumRegionCityCount));
                List<City> removed = members.Where(city =>
                    !retained.Contains(city.data.id)).ToList();
                if (removed.Count == 0) continue;
                foreach (City city in removed)
                {
                    region.MemberCityIds.Remove(city.data.id);
                    detached.Add((city, region.RegionId));
                    AddChange(region.RegionId, city.data.id, region.RegionId,
                        -1L, "DeJureDisconnectedMemberDetached");
                }
                region.MemberCityIds = region.MemberCityIds.Distinct().ToList();
                region.Version++;
                changed = true;
            }

            foreach ((City city, long fromRegionId) in detached
                         .OrderBy(item => item.City.data.id))
            {
                DeJureRegion target = FindContinuityRepairTargetLocked(city);
                if (target == null)
                {
                    long id = Math.Max(1L, _store.NextRegionId++);
                    target = new DeJureRegion
                    {
                        RegionId = id,
                        RegionName = RegionalGovernmentRules.RegionName(
                            ResolveCountyNameForPresentation(city), "州"),
                        SeatCityId = city.data.id,
                        CreatedYear = SafeYear(),
                        CreatedByKind = "contiguity_repair_isolated",
                        CreatedByKingdomId = city.kingdom?.data?.id ?? -1L,
                        MemberCityIds = new List<long> { city.data.id }
                    };
                    _store.Regions.Add(target);
                    AddChange(id, city.data.id, fromRegionId, id,
                        "DeJureContiguityRegionCreated");
                    continue;
                }
                target.MemberCityIds.Add(city.data.id);
                target.MemberCityIds = target.MemberCityIds.Distinct().ToList();
                target.Version++;
                AddChange(target.RegionId, city.data.id, fromRegionId,
                    target.RegionId, "DeJureContiguityMemberReassigned");
            }
            return changed;
        }

        private static DeJureRegion FindContinuityRepairTargetLocked(City pCity)
        {
            if (pCity?.data == null) return null;
            var candidates = new List<DeJureNewCityRegionCandidate>();
            foreach (DeJureRegion region in _store.Regions.Where(item =>
                         item != null && item.Active &&
                         item.MemberCityIds != null &&
                         !item.MemberCityIds.Contains(pCity.data.id)))
            {
                List<City> members = region.MemberCityIds.Select(id =>
                        World.world?.cities?.get(id))
                    .Where(IsDeJureEligibleCity).ToList();
                if (members.Count >= RegionalGovernmentRules.
                        MaximumRegionCityCount) continue;
                int adjacentCount = members.Count(member =>
                    AreAdjacent(pCity, member));
                if (adjacentCount <= 0) continue;
                City seat = members.FirstOrDefault(member =>
                    member.data.id == region.SeatCityId);
                candidates.Add(new DeJureNewCityRegionCandidate(
                    region.RegionId, seat != null && AreAdjacent(pCity, seat),
                    adjacentCount, 0L,
                    DistanceSquared(pCity, seat), true));
            }
            long targetId = DeJureNewCityAssignmentRules.Select(candidates);
            return _store.Regions.FirstOrDefault(region =>
                region != null && region.Active &&
                region.RegionId == targetId);
        }

        private static bool PrepareRegionNeighbours(IEnumerable<City> pCities)
        {
            try
            {
                foreach (City city in pCities)
                {
                    city.recalculateNeighbourZones();
                    city.recalculateNeighbourCities();
                }
                return true;
            }
            catch { return false; }
        }

        private static long DistanceSquared(City pFirst, City pSecond)
        {
            if (pFirst?.getTile()?.pos == null || pSecond?.getTile()?.pos == null)
                return long.MaxValue;
            try
            {
                double dx = pFirst.getTile().pos.x - pSecond.getTile().pos.x;
                double dy = pFirst.getTile().pos.y - pSecond.getTile().pos.y;
                double value = dx * dx + dy * dy;
                return value >= long.MaxValue ? long.MaxValue : (long)value;
            }
            catch { return long.MaxValue; }
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

        private static XiaHistoricalDeJureProfile ResolveHistoricalProfileLocked(
            City pCity)
        {
            return XiaHistoricalDeJureRules.SelectProfile(
                XiaHistoricalDeJureCatalogService.Current,
                new[] { pCity?.data?.name ?? string.Empty },
                StableHistoricalSelector(pCity?.data?.id ?? 0L));
        }

        /// <summary>
        ///     新建 region 的州名。开启「历史州郡县」时优先按成员城市名去
        ///     历史目录里对，对上就用真正的州名（如「扬州」），而不是把城市名
        ///     拼个「州」字（「晋江」→「晋江州」）。
        ///
        ///     对不上时**不能留空**:城市名多半是随机生成器产的（「晋江」就是），
        ///     几乎撞不上历史县名。留空会让 HistoricalStateId 一直是空,而
        ///     DeJureNewCityAssignmentService.ApplyHistoricalCityName 又要靠
        ///     这个 id 才能给城市取历史名 —— 两边互相等,谁也起不来。
        ///     所以这里按稳定种子直接分配一个尚未被占用的历史州,把链条接上。
        /// </summary>
        private static string ResolveCreatedRegionNameLocked(City pCity,
            out string pStateId, out string pCommanderyId)
        {
            pStateId = string.Empty;
            pCommanderyId = string.Empty;
            string fallback = RegionalGovernmentRules.RegionName(
                ResolveCountyNameForPresentation(pCity), "州");
            if (!AWPerformanceSettings.EnableHistoricalDeJureCityNames)
                return fallback;
            try
            {
                XiaHistoricalDeJureProfile profile =
                    ResolveHistoricalProfileLocked(pCity);
                if (!string.IsNullOrWhiteSpace(profile.StateName))
                {
                    pStateId = profile.StateId;
                    pCommanderyId = profile.CommanderyId;
                    return profile.StateName;
                }

                XiaHistoricalStateDefinition assigned =
                    SelectUnusedHistoricalStateLocked(pCity);
                if (assigned == null) return fallback;
                pStateId = assigned.Id;
                return assigned.Name;
            }
            catch { return fallback; }
        }

        /// <summary>
        ///     挑一个当前没有任何 region 占用的历史州。全被占完就返回 null,
        ///     调用方退回拼接名 —— 重复的州名比拼接名更糟。
        /// </summary>
        private static XiaHistoricalStateDefinition
            SelectUnusedHistoricalStateLocked(City pCity)
        {
            XiaHistoricalDeJureCatalog catalog =
                XiaHistoricalDeJureCatalogService.Current;
            if (catalog == null) return null;
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (DeJureRegion existing in _store?.Regions ??
                         new List<DeJureRegion>())
            {
                if (existing == null || !existing.Active) continue;
                if (!string.IsNullOrWhiteSpace(existing.HistoricalStateId))
                    used.Add(existing.HistoricalStateId.Trim());
            }

            XiaHistoricalStateDefinition[] available = catalog.States
                .Where(p => p != null && p.Id.Length > 0 &&
                            p.Name.Length > 0 && !used.Contains(p.Id))
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            if (available.Length == 0) return null;
            int selector = StableHistoricalSelector(pCity?.data?.id ?? 0L);
            int index = selector == int.MinValue
                ? 0
                : (int)((uint)selector % (uint)available.Length);
            return available[index];
        }

        private static void MigrateHistoricalMetadataLocked()
        {
            if (_store?.Regions == null) return;
            bool changed = false;
            foreach (DeJureRegion region in _store.Regions)
            {
                if (region == null || !region.Active) continue;
                if (region.SeatCityId >= 0L && !region.SeatLocked)
                {
                    region.SeatLocked = true;
                    changed = true;
                }
                City seat = World.world?.cities?.get(region.SeatCityId);
                if (string.IsNullOrWhiteSpace(region.HistoricalStateId) &&
                    seat?.data != null)
                {
                    XiaHistoricalDeJureProfile profile =
                        ResolveHistoricalProfileLocked(seat);
                    if (!string.IsNullOrWhiteSpace(profile.StateId))
                    {
                        region.HistoricalStateId = profile.StateId;
                        region.HistoricalCommanderyId = profile.CommanderyId;
                        if (string.IsNullOrWhiteSpace(region.RegionName))
                        {
                            region.RegionName = profile.StateName;
                            region.RegionNameSource =
                                DeJureHistoricalProfileRules.HistoricalDefault;
                        }
                        changed = true;
                    }
                }
                if (string.IsNullOrWhiteSpace(region.RegionNameSource))
                {
                    region.RegionNameSource =
                        DeJureHistoricalProfileRules.LegacyPreserved;
                    changed = true;
                }
            }
            if (!changed) return;
            _store.StoreRevision++;
            RegionalGovernmentAggregationService.Clear();
        }

        private static int StableHistoricalSelector(long pCityId)
        {
            unchecked
            {
                ulong value = (ulong)pCityId ^
                    ((ulong)(uint)MapBox.current_world_seed_id << 32);
                value ^= value >> 33;
                value *= 0xff51afd7ed558ccdUL;
                value ^= value >> 33;
                return (int)(value ^ (value >> 32));
            }
        }

        /// <summary>
        /// Resolves a county/lowest-level administrative name for display.
        /// Chinese presentation uses the persisted historical JSON name;
        /// other languages keep the city's current projected name.
        /// </summary>
        internal static string ResolveCountyNameForPresentation(City pCity)
        {
            if (pCity?.data == null) return string.Empty;
            try
            {
                pCity.data.get(AWNameDataKeys.ChineseName,
                    out string historicalChineseName, string.Empty);
                // Keep non-Chinese county labels on the exact same projection
                // path as the city itself.  Reading data.name directly can
                // retain a stale Chinese projection after a language switch.
                string projectedCityName = AWLocalizedNameService.ProjectStored(
                    pCity.data);
                // A player-authored city name is authoritative in every
                // locale and must not be replaced by a historical catalog
                // entry that happens to match its Chinese identity.
                if (pCity.data.custom_name) return projectedCityName;
                return XiaHistoricalDeJureRules.ResolveCountyName(
                    XiaHistoricalDeJureCatalogService.Current,
                    historicalChineseName, projectedCityName,
                    AWLocalizedNameService.CurrentLanguage());
            }
            catch
            {
                return pCity.data.name ?? string.Empty;
            }
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
