using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class FeudatoryService
    {
        private const int MaximumAnnualRepairs = 4;
        private const int MaximumSuccessionKinNodes = 128;
        private const int MaximumSuccessionKinDistance = 6;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<long, int> AnnualCursorByKingdom =
            new Dictionary<long, int>();
        private static FeudatoryCache _cache = FeudatoryCache.Empty;
        [ThreadStatic] private static int _intentionalJingnanTransferDepth;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance.InitializeSuccessful;

        public static void ResetRuntime()
        {
            lock (CacheLock)
                _cache = FeudatoryCache.Empty;
            AnnualCursorByKingdom.Clear();
            _intentionalJingnanTransferDepth = 0;
            FeudatoryGarrisonService.ClearRuntime();
        }

        public static IReadOnlyList<FeudatorySnapshot> GetByKingdom(long pKingdomId)
        {
            FeudatoryCache cache = _cache;
            return cache.ByKingdom.TryGetValue(pKingdomId, out FeudatorySnapshot[] rows)
                ? rows
                : Array.Empty<FeudatorySnapshot>();
        }

        public static bool IsFavorOrderEnabled(Kingdom pEmpire)
        {
            if (pEmpire?.data == null) return false;
            pEmpire.data.get(LineageKeys.FAVOR_ORDER_ENABLED,
                out bool enabled, false);
            return enabled;
        }

        public static bool CanEnableFavorOrder(Kingdom pEmpire,
            out string pReason)
        {
            pReason = "";
            bool isMandate = pEmpire?.data != null &&
                             MandateService.IsMandateKingdom(pEmpire);
            bool enabled = IsFavorOrderEnabled(pEmpire);
            CentralizationSnapshot centralization =
                CentralizationService.ReadSnapshot(pEmpire);
            bool allowed = FeudatoryFavorRules.CanEnable(isMandate, enabled,
                centralization.can_reform,
                GetByKingdom(pEmpire?.id ?? -1L).Count);
            if (allowed) return true;
            if (!isMandate) pReason = "not_mandate";
            else if (enabled) pReason = "already_enabled";
            else if (GetByKingdom(pEmpire.id).Count == 0)
                pReason = "no_feudatories";
            else pReason = centralization.block_reason ?? "centralization";
            return false;
        }

        public static bool EnableFavorOrder(Kingdom pEmpire,
            out string pReason)
        {
            if (!CanEnableFavorOrder(pEmpire, out pReason)) return false;
            CentralizationSnapshot snapshot =
                CentralizationService.ReadSnapshot(pEmpire);
            if (!CentralizationService.TryCompleteMandateReform(pEmpire,
                    snapshot.next_target_level, out pReason)) return false;
            pEmpire.data.set(LineageKeys.FAVOR_ORDER_ENABLED, true);
            pEmpire.data.set(LineageKeys.POLICY_ENFEOFFMENT_STATE,
                KingdomPolicyDefs.EnfeoffmentLimit);
            HistoryWriter.RecordKingdom(pEmpire, "favor_order_proclaimed",
                HistoryLocalizationRules.Text(
                    "aw_hist_favor_order_proclaimed"));
            return true;
        }

        public static int ApplyAutonomyCap(Kingdom pEmpire,
            int pAutonomyCap)
        {
            if (!Ready || pEmpire?.data == null) return 0;
            IReadOnlyList<FeudatorySnapshot> rows =
                GetByKingdom(pEmpire.id);
            var changed = new List<FeudatorySnapshot>();
            for (int i = 0; i < rows.Count; i++)
            {
                FeudatorySnapshot snapshot = rows[i];
                int autonomy = FeudatoryAutonomyRules.ApplyCap(
                    snapshot.Autonomy, pAutonomyCap);
                if (autonomy != snapshot.Autonomy)
                    changed.Add(snapshot.WithAutonomyLoyalty(
                        autonomy, snapshot.Loyalty));
            }
            if (changed.Count == 0) return 0;

            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                for (int i = 0; i < changed.Count; i++)
                {
                    using var command = new SQLiteCommand(DB)
                        { Transaction = transaction };
                    command.CommandText = "UPDATE " +
                        FeudatoryTableItem.GetTableName() +
                        " SET AUTONOMY=@autonomy WHERE FEUDATORY_ID=@id " +
                        "AND STATUS=0 AND END_TIME<0";
                    command.Parameters.AddWithValue("@autonomy",
                        changed[i].Autonomy);
                    command.Parameters.AddWithValue("@id",
                        changed[i].FeudatoryId);
                    if (command.ExecuteNonQuery() != 1) return 0;
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory autonomy cap failed: " +
                                    exception.Message);
                return 0;
            }

            for (int i = 0; i < changed.Count; i++)
                PublishReplaced(changed[i]);
            return changed.Count;
        }

        public static bool TryGet(long pFeudatoryId, out FeudatorySnapshot pSnapshot)
        {
            return _cache.ById.TryGetValue(pFeudatoryId, out pSnapshot);
        }

        public static bool TryGetByCity(long pCityId, out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return _cache.FeudatoryByCity.TryGetValue(pCityId, out long feudatoryId) &&
                   _cache.ById.TryGetValue(feudatoryId, out pSnapshot);
        }

        public static bool TryGetByPrince(long pActorId, out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return _cache.FeudatoryByPrince.TryGetValue(pActorId, out long feudatoryId) &&
                   _cache.ById.TryGetValue(feudatoryId, out pSnapshot);
        }

        public static bool TryGetByShiBranch(long pShiBranchId,
            out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return _cache.FeudatoryByShiBranch.TryGetValue(pShiBranchId,
                       out long feudatoryId) &&
                   _cache.ById.TryGetValue(feudatoryId, out pSnapshot);
        }

        public static bool TryGetBySuccessor(long pActorId,
            out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return _cache.FeudatoryBySuccessor.TryGetValue(pActorId,
                       out long feudatoryId) &&
                   _cache.ById.TryGetValue(feudatoryId, out pSnapshot);
        }

        public static bool IsActivePrince(Actor pActor)
        {
            return pActor?.data != null && !pActor.isRekt() &&
                   TryGetByPrince(pActor.data.id, out FeudatorySnapshot snapshot) &&
                   snapshot.PrinceActorId == pActor.data.id;
        }

        public static bool TryGetRoamTile(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            if (!IsActivePrince(pActor) ||
                !TryGetByPrince(pActor.data.id, out FeudatorySnapshot snapshot))
                return false;
            City seat;
            try { seat = World.world?.cities?.get(snapshot.SeatCityId); }
            catch { seat = null; }
            if (seat?.data == null || seat.isRekt() || seat.zones == null ||
                seat.zones.Count == 0)
                return false;

            TileZone zone = seat.zones[UnityEngine.Random.Range(0, seat.zones.Count)];
            pTarget = zone?.getRandomTile() ?? zone?.centerTile ?? seat.getTile();
            return pTarget != null;
        }

        public static bool TryEstablish(Kingdom pEmpire, Actor pPrince,
            IReadOnlyList<City> pCities, string pReason, out long pFeudatoryId)
        {
            pFeudatoryId = -1;
            if (!Ready || !ValidateEstablishment(pEmpire, pPrince, pCities))
                return false;

            if (!NobleRankService.EnsureFeudatoryPrinceTitle(pEmpire,
                    pPrince, out string titleName))
                return false;
            string feudatoryName =
                FeudatoryRules.BuildFeudatoryName(titleName);
            long shiBranchId = LineageService.EnsureFeudatoryShiBranch(
                pPrince, titleName, pCities[0]);
            if (shiBranchId < 0 || string.IsNullOrEmpty(feudatoryName))
                return false;

            double now = LineageService.CurTime();
            int year = SafeCurrentYear();
            long feudatoryId;
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                feudatoryId = NextId(transaction, FeudatoryTableItem.GetTableName(),
                    "FEUDATORY_ID");
                long cityEntryId = NextId(transaction,
                    FeudatoryCityTableItem.GetTableName(), "ENTRY_ID");
                InsertHeader(transaction, feudatoryId, pEmpire, pPrince,
                    pCities[0], feudatoryName, shiBranchId, year, now);
                for (int i = 0; i < pCities.Count; i++)
                    InsertCity(transaction, cityEntryId + i, feudatoryId,
                        pCities[i], now);
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory establishment failed: " +
                                    exception.Message);
                return false;
            }

            var cityIds = new long[pCities.Count];
            for (int i = 0; i < pCities.Count; i++) cityIds[i] = pCities[i].id;
            Actor successor = FindFeudatorySuccessor(pPrince);
            string seatName = pCities[0].data.name ?? "";
            var snapshot = new FeudatorySnapshot(feudatoryId, pEmpire.id,
                pPrince.data.id, pCities[0].id, 40, 60, cityIds,
                pEmpireName: pEmpire.name ?? "",
                pPrinceName: pPrince.getName() ?? "",
                pSeatName: seatName,
                pFeudatoryName: feudatoryName,
                pParentColor: HistoryColors.FromKingdom(pEmpire),
                pPrinceShiLabel: BuildPrinceShiLabel(pPrince.data.id,
                    feudatoryName),
                pSuccessorActorId: successor?.data?.id ?? -1L,
                pSuccessorName: successor?.getName() ?? "",
                pCityRows: BuildCityRows(cityIds),
                pShiBranchId: shiBranchId);
            ProjectHotIds(snapshot, pPrince, pCities);
            PublishAdded(snapshot);
            SlaveService.ReleaseForFeudatoryAppointment(pPrince);
            AssignPrinceIdentity(pPrince, pCities[0]);
            MarkPrinceChildren(pPrince, snapshot.FeudatoryId);
            DynasticMaleLineContinuityService.RequestContinuation(pPrince);
            RefreshSuccessor(snapshot.FeudatoryId);
            FeudatoryMapModeService.DirtyMapIfActive();
            for (int i = 0; i < pCities.Count; i++)
                MandateService.OnKingdomCoreCreated(pEmpire, pCities[i], "feudatory");
            ChronicleEvents.OnFeudatoryEstablished(pEmpire, pPrince, pCities[0],
                pCities.Count);
            FeudatoryGarrisonService.EnsureFor(snapshot);
            FeudatoryOfficeService.ScheduleMaintenance(snapshot);
            pFeudatoryId = feudatoryId;
            return true;
        }

        public static bool CanRelocateFeudatory(Kingdom pEmpire,
            long pFeudatoryId)
        {
            return TryGetGovernable(pEmpire, pFeudatoryId,
                       out FeudatorySnapshot snapshot) &&
                   FeudatorySelectionService.TrySelectRelocationCities(
                       pEmpire, snapshot, out _);
        }

        public static CourtDispositionResistanceResult
            TryStartDispositionResistance(Kingdom pEmpire,
                long pFeudatoryId, int pIntensity, string pReason)
        {
            if (!TryGetGovernable(pEmpire, pFeudatoryId,
                    out FeudatorySnapshot snapshot))
                return CourtDispositionResistanceResult.FailedToStart;
            bool activated = CheckRevoltOnRevocation(pEmpire, snapshot,
                Math.Max(0, pIntensity), pReason ?? "court_disposition",
                out bool triggered);
            if (!triggered)
                return CourtDispositionResistanceResult.Accepted;
            return activated
                ? CourtDispositionResistanceResult.Rebelled
                : CourtDispositionResistanceResult.FailedToStart;
        }

        public static bool TryRelocateFeudatory(Kingdom pEmpire,
            long pFeudatoryId, out int pIntensity)
        {
            CourtDispositionResistanceResult result =
                TryRelocateFeudatoryDisposition(pEmpire, pFeudatoryId,
                    out pIntensity);
            return result != CourtDispositionResistanceResult.FailedToStart;
        }

        public static CourtDispositionResistanceResult
            TryRelocateFeudatoryDisposition(Kingdom pEmpire,
                long pFeudatoryId, out int pIntensity)
        {
            pIntensity = 0;
            if (!TryGetGovernable(pEmpire, pFeudatoryId,
                    out FeudatorySnapshot snapshot) ||
                !FeudatorySelectionService.TrySelectRelocationCities(
                    pEmpire, snapshot, out List<City> targetCities))
                return CourtDispositionResistanceResult.FailedToStart;
            pIntensity = FeudatoryRevocationRules.IntensityFor(
                FeudatoryRevocationAction.Relocate);
            bool activated = CheckRevoltOnRevocation(pEmpire, snapshot,
                pIntensity, "revocation_relocate", out bool triggered);
            if (triggered)
                return activated
                    ? CourtDispositionResistanceResult.Rebelled
                    : CourtDispositionResistanceResult.FailedToStart;
            double now = LineageService.CurTime();
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                long entryId = NextId(transaction,
                    FeudatoryCityTableItem.GetTableName(), "ENTRY_ID");
                using (var close = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    close.CommandText = "UPDATE " +
                        FeudatoryCityTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time," +
                        "END_REASON='relocation' WHERE FEUDATORY_ID=@id " +
                        "AND ACTIVE=1";
                    close.Parameters.AddWithValue("@time", now);
                    close.Parameters.AddWithValue("@id", pFeudatoryId);
                    if (close.ExecuteNonQuery() != snapshot.CityIds.Count)
                        return CourtDispositionResistanceResult.FailedToStart;
                }
                using (var header = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    header.CommandText = "UPDATE " +
                        FeudatoryTableItem.GetTableName() +
                        " SET SEAT_CITY_ID=@seat WHERE FEUDATORY_ID=@id " +
                        "AND STATUS=0 AND END_TIME<0";
                    header.Parameters.AddWithValue("@seat", targetCities[0].id);
                    header.Parameters.AddWithValue("@id", pFeudatoryId);
                    if (header.ExecuteNonQuery() != 1)
                        return CourtDispositionResistanceResult.FailedToStart;
                }
                for (int i = 0; i < targetCities.Count; i++)
                    InsertCity(transaction, entryId + i, pFeudatoryId,
                        targetCities[i], now);
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory relocation failed: " +
                                    exception.Message);
                return CourtDispositionResistanceResult.FailedToStart;
            }

            for (int i = 0; i < snapshot.CityIds.Count; i++)
                ClearCityProjection(snapshot.CityIds[i]);
            var targetIds = new long[targetCities.Count];
            for (int i = 0; i < targetCities.Count; i++)
            {
                targetIds[i] = targetCities[i].id;
                targetCities[i].data.set(LineageKeys.CITY_FEUDATORY_ID,
                    pFeudatoryId);
            }
            City oldSeat = FindCity(snapshot.SeatCityId);
            City newSeat = targetCities[0];
            FeudatorySnapshot updated = snapshot.WithCitiesAndSeat(targetIds,
                newSeat.id, newSeat.data.name, BuildCityRows(targetIds),
                snapshot.FeudatoryName);
            PublishReplaced(updated);
            FeudatoryMapModeService.DirtyMapIfActive();
            MovePrinceToSeat(updated);
            FeudatoryGarrisonService.EnsureFor(updated);
            FeudatoryOfficeService.OnSeatChanged(snapshot, updated);
            for (int i = 0; i < targetCities.Count; i++)
                MandateService.OnKingdomCoreCreated(pEmpire, targetCities[i],
                    "feudatory_relocation");
            ChronicleEvents.OnFeudatoryRelocated(pEmpire,
                FindActor(snapshot.PrinceActorId), oldSeat, newSeat,
                targetCities.Count, pIntensity);
            return CourtDispositionResistanceResult.Accepted;
        }

        public static bool CanReclaimFeudatoryCity(Kingdom pEmpire,
            long pFeudatoryId, long pCityId)
        {
            if (!TryGetGovernable(pEmpire, pFeudatoryId,
                    out FeudatorySnapshot snapshot))
                return false;
            bool member = false;
            for (int i = 0; i < snapshot.CityIds.Count; i++)
                if (snapshot.CityIds[i] == pCityId)
                {
                    member = true;
                    break;
                }
            return FeudatoryRevocationRules.CanReclaimCity(member,
                snapshot.CityIds.Count);
        }

        public static bool TryReclaimFeudatoryCity(Kingdom pEmpire,
            long pFeudatoryId, long pCityId, out int pIntensity)
        {
            CourtDispositionResistanceResult result =
                TryReclaimFeudatoryCityDisposition(pEmpire, pFeudatoryId,
                    pCityId, out pIntensity);
            return result != CourtDispositionResistanceResult.FailedToStart;
        }

        public static CourtDispositionResistanceResult
            TryReclaimFeudatoryCityDisposition(Kingdom pEmpire,
                long pFeudatoryId, long pCityId, out int pIntensity)
        {
            pIntensity = 0;
            if (!CanReclaimFeudatoryCity(pEmpire, pFeudatoryId, pCityId) ||
                !TryGet(pFeudatoryId, out FeudatorySnapshot snapshot))
                return CourtDispositionResistanceResult.FailedToStart;
            City city = FindCity(pCityId);
            Actor prince = FindActor(snapshot.PrinceActorId);
            pIntensity = FeudatoryRevocationRules.IntensityFor(
                FeudatoryRevocationAction.ReclaimCity);
            bool activated = CheckRevoltOnRevocation(pEmpire, snapshot,
                pIntensity, "revocation_reclaim", out bool triggered);
            if (triggered)
                return activated
                    ? CourtDispositionResistanceResult.Rebelled
                    : CourtDispositionResistanceResult.FailedToStart;
            if (!RemoveCity(snapshot, pCityId, "revocation_reclaim"))
                return CourtDispositionResistanceResult.FailedToStart;
            ChronicleEvents.OnFeudatoryCityReclaimed(pEmpire, prince, city,
                pIntensity);
            return CourtDispositionResistanceResult.Accepted;
        }

        public static bool CanAbolishFeudatory(Kingdom pEmpire,
            long pFeudatoryId)
        {
            return TryGetGovernable(pEmpire, pFeudatoryId, out _);
        }

        public static bool TryAbolishFeudatory(Kingdom pEmpire,
            long pFeudatoryId, out int pIntensity)
        {
            pIntensity = 0;
            if (!TryGetGovernable(pEmpire, pFeudatoryId,
                    out FeudatorySnapshot snapshot))
                return false;
            pIntensity = FeudatoryRevocationRules.IntensityFor(
                FeudatoryRevocationAction.Abolish);
            bool activated = CheckRevoltOnRevocation(pEmpire, snapshot,
                pIntensity, "revocation_abolish", out bool triggered);
            if (triggered) return activated;
            Actor prince = FindActor(snapshot.PrinceActorId);
            if (!AbolishFeudatoryInternal(snapshot, prince, pEmpire,
                    "revocation_abolish", pOldPrinceDying: false))
                return false;
            if (prince?.data != null && !prince.isRekt())
            {
                prince.data.set(LineageKeys.NOBLE_DISTANCE, 99);
                LineageService.RefreshNobleStatus(prince);
                try { LineageService.ArchiveActor(prince, pAlive: true); }
                catch { }
            }
            return true;
        }

        public static bool UpdateGarrison(long pFeudatoryId, long pArmyId,
            long pCaptainActorId)
        {
            if (!Ready || pArmyId < 0 || pCaptainActorId < 0 ||
                !TryGet(pFeudatoryId, out FeudatorySnapshot current))
                return false;
            int armySize = GetArmySize(pArmyId);
            if (current.GarrisonArmyId == pArmyId &&
                current.GarrisonCaptainActorId == pCaptainActorId)
            {
                if (current.GarrisonSize != armySize)
                    PublishReplaced(current.WithGarrison(pArmyId,
                        pCaptainActorId, armySize));
                return true;
            }

            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + FeudatoryTableItem.GetTableName() +
                    " SET GARRISON_ARMY_ID=@army," +
                    "GARRISON_CAPTAIN_ACTOR_ID=@captain WHERE FEUDATORY_ID=@id " +
                    "AND STATUS=0 AND END_TIME<0";
                command.Parameters.AddWithValue("@army", pArmyId);
                command.Parameters.AddWithValue("@captain", pCaptainActorId);
                command.Parameters.AddWithValue("@id", pFeudatoryId);
                if (command.ExecuteNonQuery() != 1) return false;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory garrison update failed: " +
                                    exception.Message);
                return false;
            }

            PublishReplaced(current.WithGarrison(pArmyId, pCaptainActorId,
                armySize));
            return true;
        }

        public static void LoadActiveCache()
        {
            if (!Ready) return;
            var cityIds = new Dictionary<long, List<long>>();
            var rows = new List<FeudatoryTableItem>();
            try
            {
                using (var cityCommand = new SQLiteCommand(DB))
                {
                    cityCommand.CommandText = "SELECT FEUDATORY_ID,CITY_ID FROM " +
                        FeudatoryCityTableItem.GetTableName() +
                        " WHERE ACTIVE=1 ORDER BY FEUDATORY_ID,ENTRY_ID";
                    using SQLiteDataReader reader = cityCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        long feudatoryId = reader.GetInt64(0);
                        if (!cityIds.TryGetValue(feudatoryId, out List<long> list))
                        {
                            list = new List<long>(FeudatoryRules.MaximumCities);
                            cityIds[feudatoryId] = list;
                        }
                        if (list.Count < FeudatoryRules.MaximumCities)
                            list.Add(reader.GetInt64(1));
                    }
                }

                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT * FROM " +
                    FeudatoryTableItem.GetTableName() +
                    " WHERE STATUS=0 AND END_TIME<0 ORDER BY FEUDATORY_ID";
                using SQLiteDataReader headerReader = command.ExecuteReader();
                while (headerReader.Read())
                {
                    var row = new FeudatoryTableItem();
                    row.ReadFromReader(headerReader);
                    rows.Add(row);
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory cache load failed: " +
                                    exception.Message);
                return;
            }

            var snapshots = new List<FeudatorySnapshot>(rows.Count);
            foreach (FeudatoryTableItem row in rows)
            {
                cityIds.TryGetValue(row.feudatory_id, out List<long> memberIds);
                Kingdom empire = FindKingdom(row.empire_kingdom_id);
                Actor prince = FindActor(row.prince_actor_id);
                string seatName = FindCity(row.seat_city_id)?.data?.name ?? "";
                RepairLoadedIdentity(row, prince, empire,
                    FindCity(row.seat_city_id));
                Actor successor = FindFeudatorySuccessor(prince,
                    row.shi_branch_id, row.feudatory_id, empire);
                snapshots.Add(new FeudatorySnapshot(row.feudatory_id,
                    row.empire_kingdom_id, row.prince_actor_id, row.seat_city_id,
                    row.autonomy, row.loyalty, memberIds,
                    row.garrison_army_id, row.garrison_captain_actor_id,
                    empire?.name ?? "",
                    row.prince_name ?? "",
                    seatName, row.feudatory_name ?? "",
                    HistoryColors.FromKingdom(empire),
                    BuildPrinceShiLabel(row.prince_actor_id,
                        row.feudatory_name),
                    GetArmySize(row.garrison_army_id),
                    successor?.data?.id ?? -1L,
                    successor?.getName() ?? "", BuildCityRows(memberIds),
                    row.shi_branch_id));
            }
            Publish(snapshots);
            for (int i = 0; i < snapshots.Count; i++)
            {
                FeudatorySnapshot snapshot = snapshots[i];
                Actor prince = FindActor(snapshot.PrinceActorId);
                if (prince?.data == null) continue;
                prince.data.set(LineageKeys.FEUDATORY_ID,
                    snapshot.FeudatoryId);
                prince.data.set(LineageKeys.FEUDATORY_LINE_ID,
                    snapshot.FeudatoryId);
                prince.data.set(LineageKeys.FEUDATORY_BRANCH_SHI_ID,
                    snapshot.ShiBranchId);
                SlaveService.ReleaseForFeudatoryAppointment(prince);
                City seat = FindCity(snapshot.SeatCityId);
                if (seat?.data != null) AssignPrinceIdentity(prince, seat);
                MarkPrinceChildren(prince, snapshot.FeudatoryId);
                DynasticMaleLineContinuityService.RequestContinuation(prince);
            }
            FeudatoryMapModeService.DirtyMapIfActive();
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom,
            Kingdom pNewKingdom)
        {
            if (IsIntentionalJingnanTransfer) return;
            if (!Ready || pCity?.data == null || pOldKingdom == pNewKingdom ||
                !TryGetByCity(pCity.id, out FeudatorySnapshot snapshot))
                return;
            RemoveCity(snapshot, pCity.id, "owner_changed");
        }

        internal static bool IsIntentionalJingnanTransfer =>
            _intentionalJingnanTransferDepth > 0;

        internal static void BeginIntentionalJingnanTransfer()
        {
            _intentionalJingnanTransferDepth++;
        }

        internal static void EndIntentionalJingnanTransfer()
        {
            _intentionalJingnanTransferDepth = Math.Max(0,
                _intentionalJingnanTransferDepth - 1);
        }

        internal static bool TryPersistJingnanStatus(
            FeudatorySnapshot pSnapshot, int pExpectedStatus,
            int pNextStatus, string pReason)
        {
            if (!Ready || pSnapshot == null) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    FeudatoryTableItem.GetTableName() +
                    " SET STATUS=@next,END_REASON=@reason," +
                    "ACTIVE_WAR_ID=CASE WHEN @clear=1 THEN -1 " +
                    "ELSE ACTIVE_WAR_ID END," +
                    "REBEL_KINGDOM_ID=CASE WHEN @clear=1 THEN -1 " +
                    "ELSE REBEL_KINGDOM_ID END " +
                    "WHERE FEUDATORY_ID=@id AND STATUS=@expected " +
                    "AND END_TIME<0";
                command.Parameters.AddWithValue("@next", pNextStatus);
                command.Parameters.AddWithValue("@reason", pReason ?? "");
                command.Parameters.AddWithValue("@id", pSnapshot.FeudatoryId);
                command.Parameters.AddWithValue("@expected", pExpectedStatus);
                command.Parameters.AddWithValue("@clear",
                    pNextStatus == FeudatoryRules.StatusActive ? 1 : 0);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory Jingnan status failed: " +
                                    exception.Message);
                return false;
            }
        }

        internal static bool TryBindJingnanWar(FeudatorySnapshot pSnapshot,
            long pWarId, long pRebelKingdomId)
        {
            if (!Ready || pSnapshot == null || pWarId < 0 ||
                pRebelKingdomId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    FeudatoryTableItem.GetTableName() +
                    " SET ACTIVE_WAR_ID=@war,REBEL_KINGDOM_ID=@rebel " +
                    "WHERE FEUDATORY_ID=@id AND STATUS=@status " +
                    "AND END_TIME<0";
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@rebel", pRebelKingdomId);
                command.Parameters.AddWithValue("@id", pSnapshot.FeudatoryId);
                command.Parameters.AddWithValue("@status",
                    FeudatoryRules.StatusRebelling);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory Jingnan binding failed: " +
                                    exception.Message);
                return false;
            }
        }

        internal static List<FeudatoryJingnanState> ReadJingnanStates(
            long pWarId)
        {
            var result = new List<FeudatoryJingnanState>();
            if (!Ready || pWarId < 0) return result;
            var rows = new List<FeudatoryTableItem>();
            try
            {
                int offset = 0;
                while (true)
                {
                    int rowsRead = 0;
                    using var command = new SQLiteCommand(DB);
                    command.CommandText = "SELECT * FROM " +
                        FeudatoryTableItem.GetTableName() +
                        " WHERE STATUS=@status AND ACTIVE_WAR_ID=@war " +
                        "ORDER BY FEUDATORY_ID LIMIT @limit OFFSET @offset";
                    command.Parameters.AddWithValue("@status",
                        FeudatoryRules.StatusRebelling);
                    command.Parameters.AddWithValue("@war", pWarId);
                    command.Parameters.AddWithValue("@limit",
                        FeudatoryJingnanRules.SettlementReadBatchSize);
                    command.Parameters.AddWithValue("@offset", offset);
                    using SQLiteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = new FeudatoryTableItem();
                        row.ReadFromReader(reader);
                        rows.Add(row);
                        rowsRead++;
                    }
                    if (!FeudatoryJingnanRules.
                            ShouldReadNextSettlementBatch(rowsRead)) break;
                    offset += rowsRead;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    FeudatoryTableItem row = rows[i];
                    List<long> cityIds = ReadActiveJingnanCityIds(
                        row.feudatory_id);
                    Kingdom empire = FindKingdom(row.empire_kingdom_id);
                    Actor prince = FindActor(row.prince_actor_id);
                    string seatName = FindCity(row.seat_city_id)?.data?.name ?? "";
                    var snapshot = new FeudatorySnapshot(row.feudatory_id,
                        row.empire_kingdom_id, row.prince_actor_id,
                        row.seat_city_id, row.autonomy, row.loyalty, cityIds,
                        row.garrison_army_id,
                        row.garrison_captain_actor_id,
                        empire?.name ?? "", row.prince_name ?? "", seatName,
                        string.IsNullOrWhiteSpace(row.feudatory_name)
                            ? FeudatoryRules.BuildFeudatoryName(
                                NobleRankService.ReadHot(prince).TitleName)
                            : row.feudatory_name,
                        HistoryColors.FromKingdom(empire),
                        BuildPrinceShiLabel(row.prince_actor_id,
                            row.feudatory_name),
                        GetArmySize(row.garrison_army_id),
                        pCityRows: BuildCityRows(cityIds),
                        pShiBranchId: row.shi_branch_id);
                    result.Add(new FeudatoryJingnanState(snapshot,
                        row.rebel_kingdom_id, row.active_war_id,
                        prince?.getName() ?? row.prince_name ?? ""));
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Read Jingnan feudatories failed: " +
                                    exception.Message);
            }
            return result;
        }

        private static List<long> ReadActiveJingnanCityIds(long pFeudatoryId)
        {
            var result = new List<long>(FeudatoryRules.MaximumCities);
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT CITY_ID FROM " +
                FeudatoryCityTableItem.GetTableName() +
                " WHERE FEUDATORY_ID=@id AND ACTIVE=1 " +
                "ORDER BY ENTRY_ID LIMIT 5";
            command.Parameters.AddWithValue("@id", pFeudatoryId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read() && result.Count < FeudatoryRules.MaximumCities)
                result.Add(reader.GetInt64(0));
            return result;
        }

        internal static bool RestoreJingnanFeudatory(
            FeudatoryJingnanState pState)
        {
            FeudatorySnapshot snapshot = pState?.Snapshot;
            Kingdom empire = FindKingdom(snapshot?.EmpireKingdomId ?? -1L);
            if (snapshot == null || empire?.data == null ||
                !TryPersistJingnanStatus(snapshot,
                    FeudatoryRules.StatusRebelling,
                    FeudatoryRules.StatusActive, "")) return false;
            var cities = new List<City>(snapshot.CityIds.Count);
            for (int i = 0; i < snapshot.CityIds.Count; i++)
            {
                City city = FindCity(snapshot.CityIds[i]);
                if (city?.data == null || city.kingdom != empire) continue;
                city.data.set(LineageKeys.CITY_FEUDATORY_ID,
                    snapshot.FeudatoryId);
                cities.Add(city);
            }
            Actor prince = FindActor(snapshot.PrinceActorId);
            if (prince?.data != null)
            {
                prince.data.set(LineageKeys.FEUDATORY_ID,
                    snapshot.FeudatoryId);
                City seat = FindCity(snapshot.SeatCityId);
                if (seat?.kingdom == empire) AssignPrinceIdentity(prince, seat);
            }
            PublishAdded(snapshot);
            FeudatoryGarrisonService.ReassignForJingnan(snapshot, empire);
            FeudatoryMapModeService.DirtyMapIfActive();
            return true;
        }

        internal static bool AbolishJingnanFeudatory(
            FeudatoryJingnanState pState, string pReason, bool pDemotePrince)
        {
            FeudatorySnapshot snapshot = pState?.Snapshot;
            Kingdom empire = FindKingdom(snapshot?.EmpireKingdomId ?? -1L);
            Actor prince = FindActor(snapshot?.PrinceActorId ?? -1L);
            if (snapshot == null || empire?.data == null ||
                !AbolishFeudatoryInternal(snapshot, prince, empire,
                    pReason, pOldPrinceDying: false,
                    pExpectedStatus: FeudatoryRules.StatusRebelling))
                return false;
            if (pDemotePrince && prince?.data != null && !prince.isRekt())
            {
                prince.data.set(LineageKeys.NOBLE_DISTANCE, 99);
                LineageService.RefreshNobleStatus(prince);
            }
            return true;
        }

        internal static bool CloseJingnanStalemate(
            FeudatoryJingnanState pState)
        {
            FeudatorySnapshot snapshot = pState?.Snapshot;
            Kingdom empire = FindKingdom(snapshot?.EmpireKingdomId ?? -1L);
            Actor prince = FindActor(snapshot?.PrinceActorId ?? -1L);
            return snapshot != null && empire?.data != null &&
                   AbolishFeudatoryInternal(snapshot, prince, empire,
                       "jingnan_stalemate", pOldPrinceDying: true,
                       pExpectedStatus: FeudatoryRules.StatusRebelling,
                       pRecordHistory: false);
        }

        internal static void FinalizeJingnanActivation(
            FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot == null) return;
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
                ClearCityProjection(pSnapshot.CityIds[i]);
            Actor prince = FindActor(pSnapshot.PrinceActorId);
            prince?.data?.set(LineageKeys.FEUDATORY_ID, -1L);
            FeudatoryOfficeService.OnFeudatoryEnded(pSnapshot,
                "jingnan_activated");
            PublishRemoved(pSnapshot.FeudatoryId);
            FeudatoryMapModeService.DirtyMapIfActive();
        }

        public static void OnActorDying(Actor pActor)
        {
            if (pActor?.data == null) return;
            bool activePrince = IsActivePrince(pActor);
            if (FeudatorySuccessionRules.ShouldRefreshAfterDeath(
                    activePrince,
                    dyingActorIsDesignatedSuccessor: false))
            {
                OnPrinceDying(pActor);
                return;
            }
            bool designatedSuccessor = TryGetBySuccessor(pActor.data.id,
                out FeudatorySnapshot successorSnapshot);
            if (!FeudatorySuccessionRules.ShouldRefreshAfterDeath(
                    activePrince, designatedSuccessor)) return;
            RefreshSuccessor(successorSnapshot.FeudatoryId,
                pActor.data.id);
        }

        public static void OnChildBorn(Actor pChild, Actor pParent1,
            Actor pParent2)
        {
            if (pChild?.data == null) return;
            Actor prince = IsActivePrince(pParent1)
                ? pParent1
                : IsActivePrince(pParent2)
                    ? pParent2
                    : null;
            if (prince?.data == null ||
                !TryGetByPrince(prince.data.id, out FeudatorySnapshot snapshot))
                return;
            pChild.data.set(LineageKeys.FEUDATORY_PARENT_ACTOR_ID,
                prince.data.id);
            pChild.data.set(LineageKeys.FEUDATORY_LINE_ID,
                snapshot.FeudatoryId);
            RefreshSuccessor(snapshot.FeudatoryId);
        }

        public static void OnActorAdult(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.FEUDATORY_ADULT_REFRESHED,
                out bool refreshed, false);
            if (refreshed || !pActor.isAdult()) return;
            pActor.data.set(LineageKeys.FEUDATORY_ADULT_REFRESHED, true);
            long shiId = LineageQuery.GetActorShiId(pActor.data.id);
            if (TryGetByShiBranch(shiId, out FeudatorySnapshot snapshot))
                RefreshSuccessor(snapshot.FeudatoryId);
        }

        public static void OnPrinceDying(Actor pActor)
        {
            if (!Ready || pActor?.data == null ||
                !TryGetByPrince(pActor.data.id, out FeudatorySnapshot snapshot) ||
                snapshot.PrinceActorId != pActor.data.id)
                return;
            Kingdom empire = FindKingdom(snapshot.EmpireKingdomId);
            ResolveInvalidPrince(snapshot, empire, "prince_died", pActor);
        }

        public static bool OnPrinceAccededToEmpire(Kingdom pEmpire,
            Actor pEmperor)
        {
            if (!Ready || pEmpire?.data == null || pEmperor?.data == null ||
                pEmpire.king != pEmperor ||
                !TryGetByPrince(pEmperor.data.id,
                    out FeudatorySnapshot snapshot) ||
                snapshot.EmpireKingdomId != pEmpire.id) return false;
            Actor successor = FindFeudatorySuccessor(pEmperor,
                snapshot.ShiBranchId, snapshot.FeudatoryId, pEmpire,
                pEmperor.data.id);
            FeudatoryAccessionDisposition disposition =
                FeudatorySuccessionRules.ResolveAccessionDisposition(
                    successor?.data?.id ?? -1L);
            if (disposition ==
                    FeudatoryAccessionDisposition.TransferToSuccessor)
                return TransferPrince(snapshot, pEmperor, successor,
                    pEmpire, "prince_acceded_to_empire", false,
                    pPreserveOldPrinceJob: true);
            bool reverted = AbolishFeudatoryInternal(snapshot, pEmperor,
                pEmpire,
                "prince_acceded_to_empire", false,
                pPreserveOldPrinceJob: true);
            if (reverted)
                NobleRankService.TryRevoke(pEmperor,
                    "prince_acceded_to_empire", out _);
            return reverted;
        }

        public static void OnKingdomDestroying(Kingdom pEmpire)
        {
            if (!Ready || pEmpire?.data == null) return;
            var rows = new List<FeudatorySnapshot>(
                GetByKingdom(pEmpire.id));
            for (int i = 0; i < rows.Count; i++)
            {
                FeudatorySnapshot snapshot = rows[i];
                Actor prince = FindActor(snapshot.PrinceActorId);
                if (AbolishFeudatoryInternal(snapshot, prince, pEmpire,
                        "empire_fell", pOldPrinceDying: false,
                        pExpectedStatus: FeudatoryRules.StatusActive))
                    continue;
                AbolishFeudatoryInternal(snapshot, prince, pEmpire,
                    "empire_fell", pOldPrinceDying: false,
                    pExpectedStatus: FeudatoryRules.StatusRebelling);
            }
            AnnualCursorByKingdom.Remove(pEmpire.id);
        }

        public static void OnKingdomYear(Kingdom pEmpire)
        {
            if (!Ready || pEmpire?.data == null || pEmpire.isRekt() ||
                !MandateService.IsMandateKingdom(pEmpire))
                return;
            int year = SafeCurrentYear();
            if (!FeudatoryRules.ShouldRunAnnualWork(year, pEmpire.id)) return;
            IReadOnlyList<FeudatorySnapshot> rows = GetByKingdom(pEmpire.id);
            if (rows.Count == 0) return;

            var capitalCoreIds = new List<long>();
            City capital = pEmpire.capital;
            if (capital?.data != null)
            {
                capitalCoreIds.Add(capital.id);
                if (capital.neighbours_cities != null)
                    foreach (City adjacent in capital.neighbours_cities)
                        if (adjacent?.data != null)
                            capitalCoreIds.Add(adjacent.id);
            }

            AnnualCursorByKingdom.TryGetValue(pEmpire.id, out int cursor);
            if (cursor < 0 || cursor >= rows.Count) cursor = 0;
            int count = Math.Min(MaximumAnnualRepairs, rows.Count);
            int centralWarriors =
                FeudatoryJingnanRiskService.CountCentralWarriors(pEmpire);
            bool reclaimedCoreCity = false;
            bool repairedGarrison = false;
            FeudatorySnapshot aiRevocationTarget = null;
            int aiThreatScore = int.MinValue;
            for (int offset = 0; offset < count; offset++)
            {
                FeudatorySnapshot snapshot = rows[(cursor + offset) % rows.Count];
                long invalidCityId = FindFirstInvalidCity(snapshot, pEmpire);
                bool removedInvalidCity = invalidCityId >= 0 &&
                    RemoveCity(snapshot, invalidCityId, "annual_invalid_city");
                if (removedInvalidCity &&
                    !TryGet(snapshot.FeudatoryId, out snapshot))
                    continue;

                if (!removedInvalidCity && !reclaimedCoreCity)
                {
                    long cityId = FeudatoryRules.SelectOneCapitalCoreRepair(
                        snapshot.CityIds, capitalCoreIds);
                    if (cityId >= 0)
                    {
                        RemoveCity(snapshot, cityId, "capital_core_repair");
                        reclaimedCoreCity = true;
                        if (!TryGet(snapshot.FeudatoryId, out snapshot)) continue;
                    }
                }

                if (!IsValidPrince(snapshot, pEmpire))
                {
                    ResolveInvalidPrince(snapshot, pEmpire,
                        "annual_prince_invalid", null);
                    continue;
                }

                if (!repairedGarrison &&
                    FeudatoryGarrisonService.NeedsRepair(snapshot))
                {
                    FeudatoryGarrisonService.EnsureFor(snapshot);
                    repairedGarrison = true;
                }

                FeudatoryJingnanRiskReport risk =
                    FeudatoryJingnanRiskService.Evaluate(pEmpire, snapshot,
                        pRevocationIntensity: 0, centralWarriors);
                if (FeudatoryJingnanRiskRules.ShouldProactivelyRevolt(
                        risk.Risk, risk.RulerIsDirectAgnaticAncestor))
                {
                    FeudatoryJingnanService.TryActivate(
                        snapshot.FeudatoryId, "proactive_jingnan",
                        risk.Risk, out _);
                    continue;
                }
                int threatScore = snapshot.Autonomy - snapshot.Loyalty;
                if (threatScore > aiThreatScore)
                {
                    aiThreatScore = threatScore;
                    aiRevocationTarget = snapshot;
                }
            }
            AnnualCursorByKingdom[pEmpire.id] = (cursor + count) % rows.Count;
            TryAiRevocation(pEmpire, aiRevocationTarget, centralWarriors,
                year);
            int mandateValue = MandateService.ReadReport().mandate_value;
            IReadOnlyList<FeudatorySnapshot> currentRows =
                GetByKingdom(pEmpire.id);
            int institutionLoyaltyBonus =
                CourtInstitutionEffectService.Read(pEmpire).
                    FeudatoryMaintenanceLoyaltyBonus;
            ApplyMaintenanceEvolution(currentRows, mandateValue,
                institutionLoyaltyBonus,
                out int unstableFeudatories);
            MandatePhaseService.AdjustCatalyst(
                MandateFeudatoryCompletionRules.
                    FeudatoryInstabilityCatalystDelta(currentRows.Count,
                        unstableFeudatories),
                "feudatory_instability");
            FeudatoryOfficeService.MaintainBatch(pEmpire, currentRows,
                cursor, count);
            for (int i = 0; i < currentRows.Count; i++)
                FeudatoryGarrisonService.ScheduleMaintenance(
                    currentRows[i].FeudatoryId);
        }

        private static void TryAiRevocation(Kingdom pEmpire,
            FeudatorySnapshot pTarget, int pCentralWarriors, int pYear)
        {
            if (pTarget == null) return;
            pEmpire.data.get(LineageKeys.FEUDATORY_AI_LAST_REVOCATION_YEAR,
                out int lastActionYear, -1);
            bool atWar = FeudatoryJingnanRiskService.HasActiveWar(pEmpire);
            if (!FeudatoryJingnanRiskRules.ShouldAiConsiderRevocation(
                    pYear, lastActionYear, atWar, pTarget.Autonomy,
                    pTarget.Loyalty)) return;

            bool relocationAvailable = CanRelocateFeudatory(pEmpire,
                pTarget.FeudatoryId);
            FeudatoryRevocationAction action =
                FeudatoryJingnanRiskRules.SelectAiRevocationAction(
                    pTarget.Autonomy, pTarget.Loyalty,
                    pTarget.CityIds.Count, relocationAvailable);
            if (action == FeudatoryRevocationAction.None) return;
            int intensity = FeudatoryRevocationRules.IntensityFor(action);
            FeudatoryJingnanRiskReport projected =
                FeudatoryJingnanRiskService.Evaluate(pEmpire, pTarget,
                    intensity, pCentralWarriors);
            if (!FeudatoryJingnanRiskRules.CanAiAttemptRevocation(
                    projected.Risk,
                    projected.RulerIsDirectAgnaticAncestor)) return;

            bool changed = action switch
            {
                FeudatoryRevocationAction.Relocate =>
                    TryRelocateFeudatory(pEmpire, pTarget.FeudatoryId,
                        out _),
                FeudatoryRevocationAction.ReclaimCity =>
                    TryReclaimFeudatoryCity(pEmpire, pTarget.FeudatoryId,
                        SelectAiReclaimCity(pTarget), out _),
                FeudatoryRevocationAction.Abolish =>
                    TryAbolishFeudatory(pEmpire, pTarget.FeudatoryId,
                        out _),
                _ => false
            };
            if (changed)
                pEmpire.data.set(
                    LineageKeys.FEUDATORY_AI_LAST_REVOCATION_YEAR, pYear);
        }

        private static long SelectAiReclaimCity(
            FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot == null) return -1L;
            for (int i = pSnapshot.CityIds.Count - 1; i >= 0; i--)
                if (pSnapshot.CityIds[i] != pSnapshot.SeatCityId)
                    return pSnapshot.CityIds[i];
            return -1L;
        }

        private static bool CheckRevoltOnRevocation(Kingdom pEmpire,
            FeudatorySnapshot pSnapshot, int pIntensity, string pReason,
            out bool pTriggered)
        {
            pTriggered = false;
            int centralWarriors =
                FeudatoryJingnanRiskService.CountCentralWarriors(pEmpire);
            FeudatoryJingnanRiskReport risk =
                FeudatoryJingnanRiskService.Evaluate(pEmpire, pSnapshot,
                    pIntensity, centralWarriors);
            pTriggered =
                FeudatoryJingnanRiskRules.ShouldRevoltOnRevocation(
                    risk.Risk, risk.RulerIsDirectAgnaticAncestor);
            return pTriggered && FeudatoryJingnanService.TryActivate(
                pSnapshot.FeudatoryId, pReason, risk.Risk, out _);
        }

        private static int ApplyMaintenanceEvolution(
            IReadOnlyList<FeudatorySnapshot> pRows, int pMandateValue,
            int pInstitutionLoyaltyBonus,
            out int pUnstableCount)
        {
            pUnstableCount = 0;
            int count = pRows?.Count ?? 0;
            if (!Ready || count == 0) return 0;
            var changed = new List<FeudatorySnapshot>(count);
            for (int i = 0; i < count; i++)
            {
                FeudatorySnapshot snapshot = pRows[i];
                if (snapshot.Autonomy >= 70 && snapshot.Loyalty <= 30)
                    pUnstableCount++;
                int loyalty =
                    FeudatoryAutonomyRules.ApplyMaintenanceLoyalty(
                        snapshot.Loyalty, snapshot.Autonomy, pMandateValue,
                        pInstitutionLoyaltyBonus);
                if (loyalty != snapshot.Loyalty)
                    changed.Add(snapshot.WithAutonomyLoyalty(snapshot.Autonomy, loyalty));
            }
            if (changed.Count == 0) return 0;

            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                for (int i = 0; i < changed.Count; i++)
                {
                    FeudatorySnapshot snapshot = changed[i];
                    using var command = new SQLiteCommand(DB)
                        { Transaction = transaction };
                    command.CommandText = "UPDATE " +
                        FeudatoryTableItem.GetTableName() +
                        " SET LOYALTY=@loyalty WHERE FEUDATORY_ID=@id " +
                        "AND STATUS=0 AND END_TIME<0";
                    command.Parameters.AddWithValue("@loyalty",
                        snapshot.Loyalty);
                    command.Parameters.AddWithValue("@id",
                        snapshot.FeudatoryId);
                    if (command.ExecuteNonQuery() != 1) return 0;
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory loyalty evolution failed: " +
                                    exception.Message);
                return 0;
            }

            for (int i = 0; i < changed.Count; i++)
                PublishReplaced(changed[i]);
            return changed.Count;
        }

        private static bool ValidateEstablishment(Kingdom pEmpire, Actor pPrince,
            IReadOnlyList<City> pCities)
        {
            if (pEmpire?.data == null || pEmpire.isRekt() ||
                pPrince?.data == null || pPrince.isRekt() ||
                pCities == null || pCities.Count == 0 ||
                pCities.Count > FeudatoryRules.MaximumCities)
                return false;
            if (MandateService.GetCurrentMandateKingdom() != pEmpire ||
                pPrince.kingdom != pEmpire ||
                TryGetByPrince(pPrince.data.id, out _))
                return false;

            var capitalAdjacent = new HashSet<long>();
            City capital = pEmpire.capital;
            if (capital?.neighbours_cities != null)
                foreach (City adjacent in capital.neighbours_cities)
                    if (adjacent?.data != null)
                        capitalAdjacent.Add(adjacent.id);

            var selected = new HashSet<long>();
            for (int i = 0; i < pCities.Count; i++)
            {
                City city = pCities[i];
                bool connected = i == 0 || IsConnected(city, selected);
                bool assigned = city?.data != null &&
                                TryGetByCity(city.id, out _);
                bool allowed = city?.data != null && FeudatoryRules.CanAssignCity(
                    city.kingdom == pEmpire, !city.isRekt() && city.isAlive(),
                    city == capital, capitalAdjacent.Contains(city.id), assigned,
                    connected, selected.Count);
                if (!allowed || !selected.Add(city.id)) return false;
            }
            return true;
        }

        private static bool RemoveCity(FeudatorySnapshot pSnapshot,
            long pCityId, string pReason)
        {
            if (pSnapshot == null) return false;
            var remaining = new List<long>(pSnapshot.CityIds.Count - 1);
            bool member = false;
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
            {
                long cityId = pSnapshot.CityIds[i];
                if (cityId == pCityId)
                {
                    member = true;
                    continue;
                }
                remaining.Add(cityId);
            }
            FeudatoryRepairDecision decision = FeudatoryRules.ResolveCityTransfer(
                member, pSameOwner: false, pCityId, pSnapshot.SeatCityId,
                remaining);
            if (decision.Action == FeudatoryRepairAction.Ignore) return false;

            double now = LineageService.CurTime();
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using (var cityCommand = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    cityCommand.CommandText = "UPDATE " +
                        FeudatoryCityTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time,END_REASON=@reason " +
                        "WHERE FEUDATORY_ID=@feudatory AND CITY_ID=@city " +
                        "AND ACTIVE=1";
                    cityCommand.Parameters.AddWithValue("@time", now);
                    cityCommand.Parameters.AddWithValue("@reason", pReason ?? "");
                    cityCommand.Parameters.AddWithValue("@feudatory",
                        pSnapshot.FeudatoryId);
                    cityCommand.Parameters.AddWithValue("@city", pCityId);
                    if (cityCommand.ExecuteNonQuery() != 1) return false;
                }

                using (var headerCommand = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    if (decision.Action == FeudatoryRepairAction.Abolish)
                    {
                        headerCommand.CommandText = "UPDATE " +
                            FeudatoryTableItem.GetTableName() +
                            " SET STATUS=4,END_TIME=@time,END_REASON=@reason " +
                            "WHERE FEUDATORY_ID=@id AND STATUS=0 AND END_TIME<0";
                    }
                    else if (decision.Action == FeudatoryRepairAction.MoveSeat)
                    {
                        headerCommand.CommandText = "UPDATE " +
                            FeudatoryTableItem.GetTableName() +
                            " SET SEAT_CITY_ID=@seat WHERE FEUDATORY_ID=@id " +
                            "AND STATUS=0 AND END_TIME<0";
                        headerCommand.Parameters.AddWithValue("@seat",
                            decision.NewSeatCityId);
                    }
                    if (headerCommand.CommandText.Length > 0)
                    {
                        headerCommand.Parameters.AddWithValue("@time", now);
                        headerCommand.Parameters.AddWithValue("@reason",
                            pReason ?? "");
                        headerCommand.Parameters.AddWithValue("@id",
                            pSnapshot.FeudatoryId);
                        if (headerCommand.ExecuteNonQuery() != 1) return false;
                    }
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory city repair failed: " +
                                    exception.Message);
                return false;
            }

            ClearCityProjection(pCityId);
            if (decision.Action == FeudatoryRepairAction.Abolish)
            {
                FeudatoryOfficeService.OnFeudatoryEnded(pSnapshot,
                    pReason);
                PublishRemoved(pSnapshot.FeudatoryId);
                FeudatoryMapModeService.DirtyMapIfActive();
                ClearPrinceIdentity(pSnapshot);
                RemoveGarrison(pSnapshot);
                return true;
            }

            long seatId = decision.Action == FeudatoryRepairAction.MoveSeat
                ? decision.NewSeatCityId
                : pSnapshot.SeatCityId;
            string seatName = FindCity(seatId)?.data?.name;
            FeudatorySnapshot updated = pSnapshot.WithCitiesAndSeat(remaining,
                seatId, seatName, BuildCityRows(remaining),
                pSnapshot.FeudatoryName);
            PublishReplaced(updated);
            FeudatoryMapModeService.DirtyMapIfActive();
            if (decision.Action == FeudatoryRepairAction.MoveSeat)
            {
                FeudatoryOfficeService.OnSeatChanged(pSnapshot, updated);
                MovePrinceToSeat(updated);
                FeudatoryGarrisonService.EnsureFor(updated);
            }
            return true;
        }

        private static long FindFirstInvalidCity(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire)
        {
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
            {
                long cityId = pSnapshot.CityIds[i];
                City city = FindCity(cityId);
                if (city?.data == null || city.isRekt() || !city.isAlive() ||
                    city.kingdom != pEmpire)
                    return cityId;
            }
            return -1L;
        }

        private static bool IsValidPrince(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire)
        {
            Actor prince = FindActor(pSnapshot.PrinceActorId);
            return prince?.data != null &&
                   FeudatorySuccessionRules.CanRemainPrince(
                       !prince.isRekt() && prince.isAlive(),
                       prince.kingdom == pEmpire, prince.isKing());
        }

        private static bool ResolveInvalidPrince(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire, string pReason, Actor pDyingPrince)
        {
            if (pSnapshot == null) return false;
            Actor oldPrince = pDyingPrince ?? FindActor(pSnapshot.PrinceActorId);
            Actor successor = FindFeudatorySuccessor(oldPrince,
                pSnapshot.ShiBranchId, pSnapshot.FeudatoryId, pEmpire);
            if (successor?.data != null)
                return TransferPrince(pSnapshot, oldPrince, successor,
                    pEmpire, pReason, pDyingPrince != null);
            return AbolishFeudatoryInternal(pSnapshot, oldPrince, pEmpire,
                pReason == "prince_died" ? "line_extinct" : pReason,
                pDyingPrince != null);
        }

        private static bool TransferPrince(FeudatorySnapshot pSnapshot,
            Actor pOldPrince, Actor pSuccessor, Kingdom pEmpire,
            string pReason, bool pOldPrinceDying,
            bool pPreserveOldPrinceJob = false)
        {
            if (pSnapshot == null || pSuccessor?.data == null ||
                pEmpire?.data == null || pSuccessor.kingdom != pEmpire)
                return false;
            long shiBranchId = pSnapshot.ShiBranchId >= 0
                ? pSnapshot.ShiBranchId
                : LineageQuery.GetActorShiId(pSuccessor.data.id);
            FeudatoryFavorAction favorAction =
                FeudatoryFavorRules.ResolveSuccessionEffect(
                    IsFavorOrderEnabled(pEmpire), pSnapshot.CityIds.Count);
            long reclaimedCityId = favorAction ==
                                   FeudatoryFavorAction.ReclaimCity
                ? SelectFavorReclaimedCity(pSnapshot, pEmpire)
                : -1L;
            if (favorAction == FeudatoryFavorAction.ReclaimCity &&
                reclaimedCityId < 0)
                favorAction = FeudatoryFavorAction.None;
            int nextAutonomy = favorAction ==
                               FeudatoryFavorAction.ReduceAutonomy
                ? FeudatoryFavorRules.ReduceAutonomy(pSnapshot.Autonomy)
                : pSnapshot.Autonomy;
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using (var command = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    command.CommandText = "UPDATE " +
                        FeudatoryTableItem.GetTableName() +
                        " SET PRINCE_ACTOR_ID=@prince,PRINCE_NAME=@name," +
                        "SHI_BRANCH_ID=@shi,AUTONOMY=@autonomy " +
                        "WHERE FEUDATORY_ID=@id AND STATUS=0 AND END_TIME<0";
                    command.Parameters.AddWithValue("@prince",
                        pSuccessor.data.id);
                    command.Parameters.AddWithValue("@name",
                        pSuccessor.getName() ?? "");
                    command.Parameters.AddWithValue("@shi", shiBranchId);
                    command.Parameters.AddWithValue("@autonomy", nextAutonomy);
                    command.Parameters.AddWithValue("@id", pSnapshot.FeudatoryId);
                    if (command.ExecuteNonQuery() != 1) return false;
                }
                if (reclaimedCityId >= 0)
                {
                    using var city = new SQLiteCommand(DB)
                        { Transaction = transaction };
                    city.CommandText = "UPDATE " +
                        FeudatoryCityTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time,END_REASON='favor_order' " +
                        "WHERE FEUDATORY_ID=@id AND CITY_ID=@city AND ACTIVE=1";
                    city.Parameters.AddWithValue("@time", LineageService.CurTime());
                    city.Parameters.AddWithValue("@id", pSnapshot.FeudatoryId);
                    city.Parameters.AddWithValue("@city", reclaimedCityId);
                    if (city.ExecuteNonQuery() != 1) return false;
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory succession failed: " +
                                    exception.Message);
                return false;
            }

            NobleRankService.TryInheritFeudatoryPrinceTitle(pOldPrince,
                pSuccessor, pEmpire, pSnapshot.FeudatoryName);

            ClearPrinceIdentity(pSnapshot,
                pResetJob: !pOldPrinceDying && !pPreserveOldPrinceJob);
            City seat = FindCity(pSnapshot.SeatCityId);
            pSuccessor.data.set(LineageKeys.FEUDATORY_ID,
                pSnapshot.FeudatoryId);
            pSuccessor.data.set(LineageKeys.FEUDATORY_LINE_ID,
                pSnapshot.FeudatoryId);
            pSuccessor.data.set(LineageKeys.FEUDATORY_BRANCH_SHI_ID,
                shiBranchId);
            AssignPrinceIdentity(pSuccessor, seat);
            Actor next = FindFeudatorySuccessor(pSuccessor, shiBranchId,
                pSnapshot.FeudatoryId, pEmpire);
            FeudatorySnapshot updated = pSnapshot.WithPrince(
                pSuccessor.data.id, pSuccessor.getName() ?? "", shiBranchId,
                BuildPrinceShiLabel(pSuccessor.data.id,
                    pSnapshot.FeudatoryName),
                next?.data?.id ?? -1L, next?.getName() ?? "");
            if (reclaimedCityId >= 0)
            {
                var remaining = new List<long>(updated.CityIds.Count - 1);
                for (int i = 0; i < updated.CityIds.Count; i++)
                    if (updated.CityIds[i] != reclaimedCityId)
                        remaining.Add(updated.CityIds[i]);
                ClearCityProjection(reclaimedCityId);
                updated = updated.WithCitiesAndSeat(remaining,
                    updated.SeatCityId, updated.SeatName,
                    BuildCityRows(remaining), updated.FeudatoryName);
                FeudatoryMapModeService.DirtyMapIfActive();
            }
            if (nextAutonomy != updated.Autonomy)
                updated = updated.WithAutonomyLoyalty(nextAutonomy,
                    updated.Loyalty);
            PublishReplaced(updated);
            MarkPrinceChildren(pSuccessor, updated.FeudatoryId);
            try { LineageService.ArchiveActor(pSuccessor, pAlive: true); }
            catch { }
            ChronicleEvents.OnFeudatoryInherited(pEmpire, pOldPrince,
                pSuccessor, seat, pReason);
            if (favorAction != FeudatoryFavorAction.None)
                ChronicleEvents.OnFavorOrderSuccession(pEmpire, pSuccessor,
                    reclaimedCityId >= 0 ? FindCity(reclaimedCityId) : seat,
                    favorAction, nextAutonomy);
            return true;
        }

        private static long SelectFavorReclaimedCity(
            FeudatorySnapshot pSnapshot, Kingdom pEmpire)
        {
            if (pSnapshot == null || pEmpire?.capital?.data == null) return -1L;
            var candidates = new List<FeudatoryFavorCityCandidate>(
                pSnapshot.CityIds.Count);
            WorldTile capitalTile = pEmpire.capital.getTile();
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
            {
                City city = FindCity(pSnapshot.CityIds[i]);
                WorldTile tile = city?.getTile();
                if (city?.data == null || tile == null || capitalTile == null)
                    continue;
                int distance = Math.Abs(tile.x - capitalTile.x) +
                               Math.Abs(tile.y - capitalTile.y);
                candidates.Add(new FeudatoryFavorCityCandidate(city.id,
                    distance));
            }
            return FeudatoryFavorRules.SelectReclaimedCity(
                pSnapshot.SeatCityId, candidates);
        }

        private static bool AbolishFeudatoryInternal(
            FeudatorySnapshot pSnapshot,
            Actor pOldPrince, Kingdom pEmpire, string pReason,
            bool pOldPrinceDying,
            int pExpectedStatus = FeudatoryRules.StatusActive,
            bool pRecordHistory = true,
            bool pPreserveOldPrinceJob = false)
        {
            if (pSnapshot == null) return false;
            double now = LineageService.CurTime();
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using (var cities = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    cities.CommandText = "UPDATE " +
                        FeudatoryCityTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time,END_REASON=@reason " +
                        "WHERE FEUDATORY_ID=@id AND ACTIVE=1";
                    cities.Parameters.AddWithValue("@time", now);
                    cities.Parameters.AddWithValue("@reason", pReason ?? "");
                    cities.Parameters.AddWithValue("@id", pSnapshot.FeudatoryId);
                    cities.ExecuteNonQuery();
                }
                using (var header = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    header.CommandText = "UPDATE " +
                        FeudatoryTableItem.GetTableName() +
                        " SET STATUS=@status,END_TIME=@time,END_REASON=@reason " +
                        "WHERE FEUDATORY_ID=@id AND STATUS=@expected " +
                        "AND END_TIME<0";
                    header.Parameters.AddWithValue("@status",
                        FeudatoryRules.StatusAbolished);
                    header.Parameters.AddWithValue("@time", now);
                    header.Parameters.AddWithValue("@reason", pReason ?? "");
                    header.Parameters.AddWithValue("@id", pSnapshot.FeudatoryId);
                    header.Parameters.AddWithValue("@expected", pExpectedStatus);
                    if (header.ExecuteNonQuery() != 1) return false;
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory abolition failed: " +
                                    exception.Message);
                return false;
            }

            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
                ClearCityProjection(pSnapshot.CityIds[i]);
            ClearPrinceIdentity(pSnapshot,
                pResetJob: !pOldPrinceDying && !pPreserveOldPrinceJob);
            RemoveGarrison(pSnapshot);
            FeudatoryOfficeService.OnFeudatoryEnded(pSnapshot, pReason);
            PublishRemoved(pSnapshot.FeudatoryId);
            FeudatoryMapModeService.DirtyMapIfActive();
            if (pRecordHistory)
                ChronicleEvents.OnFeudatoryAbolished(pEmpire, pOldPrince,
                    FindCity(pSnapshot.SeatCityId), pReason);
            return true;
        }

        private static bool TryGetGovernable(Kingdom pEmpire,
            long pFeudatoryId, out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return Ready && pEmpire?.data != null && !pEmpire.isRekt() &&
                   MandateService.IsMandateKingdom(pEmpire) &&
                   TryGet(pFeudatoryId, out pSnapshot) &&
                   pSnapshot.EmpireKingdomId == pEmpire.id;
        }

        private static bool IsConnected(City pCity, HashSet<long> pSelected)
        {
            if (pCity?.neighbours_cities == null) return false;
            foreach (City adjacent in pCity.neighbours_cities)
                if (adjacent?.data != null && pSelected.Contains(adjacent.id))
                    return true;
            return false;
        }

        private static void InsertHeader(SQLiteTransaction pTransaction,
            long pFeudatoryId, Kingdom pEmpire, Actor pPrince, City pSeat,
            string pFeudatoryName, long pShiBranchId, int pYear, double pNow)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + FeudatoryTableItem.GetTableName() +
                " (FEUDATORY_ID,EMPIRE_KINGDOM_ID,PRINCE_ACTOR_ID,PRINCE_NAME," +
                "FEUDATORY_NAME,SHI_BRANCH_ID,SEAT_CITY_ID,AUTONOMY,LOYALTY,GARRISON_ARMY_ID," +
                "GARRISON_CAPTAIN_ACTOR_ID,ESTABLISHED_YEAR,STATUS," +
                "ACTIVE_WAR_ID,REBEL_KINGDOM_ID,START_TIME,END_TIME,END_REASON) " +
                "VALUES (@id,@kingdom,@prince,@name,@feudatoryName,@shi,@seat,40,60,-1,-1," +
                "@year,0,-1,-1,@start,-1,'')";
            command.Parameters.AddWithValue("@id", pFeudatoryId);
            command.Parameters.AddWithValue("@kingdom", pEmpire.id);
            command.Parameters.AddWithValue("@prince", pPrince.data.id);
            command.Parameters.AddWithValue("@name", pPrince.getName() ?? "");
            command.Parameters.AddWithValue("@feudatoryName",
                pFeudatoryName ?? "");
            command.Parameters.AddWithValue("@shi", pShiBranchId);
            command.Parameters.AddWithValue("@seat", pSeat.id);
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@start", pNow);
            command.ExecuteNonQuery();
        }

        private static void InsertCity(SQLiteTransaction pTransaction,
            long pEntryId, long pFeudatoryId, City pCity, double pNow)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " +
                FeudatoryCityTableItem.GetTableName() +
                " (ENTRY_ID,FEUDATORY_ID,CITY_ID,ACTIVE,ASSIGNED_TIME,END_TIME," +
                "END_REASON) VALUES (@entry,@feudatory,@city,1,@time,-1,'')";
            command.Parameters.AddWithValue("@entry", pEntryId);
            command.Parameters.AddWithValue("@feudatory", pFeudatoryId);
            command.Parameters.AddWithValue("@city", pCity.id);
            command.Parameters.AddWithValue("@time", pNow);
            command.ExecuteNonQuery();
        }

        private static long NextId(SQLiteTransaction pTransaction,
            string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn + "),0)+1 FROM " +
                                  pTable;
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 1L
                : Convert.ToInt64(value);
        }

        private static void ProjectHotIds(FeudatorySnapshot pSnapshot,
            Actor pPrince, IReadOnlyList<City> pCities)
        {
            pPrince.data.set(LineageKeys.FEUDATORY_ID, pSnapshot.FeudatoryId);
            pPrince.data.set(LineageKeys.FEUDATORY_LINE_ID,
                pSnapshot.FeudatoryId);
            pPrince.data.set(LineageKeys.FEUDATORY_BRANCH_SHI_ID,
                pSnapshot.ShiBranchId);
            for (int i = 0; i < pCities.Count; i++)
                pCities[i].data.set(LineageKeys.CITY_FEUDATORY_ID,
                    pSnapshot.FeudatoryId);
            AssignPrinceIdentity(pPrince, pCities[0]);
        }

        private static void AssignPrinceIdentity(Actor pPrince, City pSeat)
        {
            if (pPrince?.data == null || pSeat?.data == null) return;
            pPrince.data.get(LineageKeys.FEUDATORY_AMBITION,
                out int baseAmbition, -1);
            if (baseAmbition < 0)
                pPrince.data.set(LineageKeys.FEUDATORY_AMBITION,
                    Math.Max(0, Math.Min(100,
                        GeneralService.GetAmbition(pPrince))));
            pPrince.joinCity(pSeat);
            if (!pPrince.hasTrait(FeudatoryContent.TraitId))
                pPrince.addTrait(FeudatoryContent.TraitId);

            pPrince.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            if (!pPrince.isWarrior() &&
                layer != CourtOfficeLayer.Military)
                CourtService.ClearOfficeForReignTransition(pPrince,
                    "became_feudatory_prince");
            if (pPrince.isWarrior()) return;
            try { pPrince.ai?.setJob(FeudatoryContent.ActorJobId); }
            catch { }
        }

        private static void PublishAdded(FeudatorySnapshot pSnapshot)
        {
            lock (CacheLock)
            {
                var all = new List<FeudatorySnapshot>(_cache.ById.Count + 1);
                foreach (FeudatorySnapshot existing in _cache.ById.Values)
                    all.Add(existing);
                all.Add(pSnapshot);
                _cache = FeudatoryCache.Build(all);
            }
        }

        private static void PublishReplaced(FeudatorySnapshot pSnapshot)
        {
            lock (CacheLock)
            {
                var all = new List<FeudatorySnapshot>(_cache.ById.Count);
                foreach (FeudatorySnapshot existing in _cache.ById.Values)
                    all.Add(existing.FeudatoryId == pSnapshot.FeudatoryId
                        ? pSnapshot
                        : existing);
                _cache = FeudatoryCache.Build(all);
            }
        }

        private static void PublishRemoved(long pFeudatoryId)
        {
            lock (CacheLock)
            {
                var all = new List<FeudatorySnapshot>(
                    Math.Max(0, _cache.ById.Count - 1));
                foreach (FeudatorySnapshot existing in _cache.ById.Values)
                    if (existing.FeudatoryId != pFeudatoryId)
                        all.Add(existing);
                _cache = FeudatoryCache.Build(all);
            }
        }

        private static void ClearCityProjection(long pCityId)
        {
            City city = FindCity(pCityId);
            city?.data?.set(LineageKeys.CITY_FEUDATORY_ID, -1L);
        }

        private static void ClearPrinceIdentity(FeudatorySnapshot pSnapshot,
            bool pResetJob = true)
        {
            Actor prince = FindActor(pSnapshot.PrinceActorId);
            if (prince?.data == null) return;
            prince.data.set(LineageKeys.FEUDATORY_ID, -1L);
            if (prince.hasTrait(FeudatoryContent.TraitId))
                prince.removeTrait(FeudatoryContent.TraitId);
            if (pResetJob && !prince.isRekt())
            {
                try { prince.ai?.setJob(prince.getNextJob()); }
                catch { }
                prince.clearGraphicsFully();
            }
        }

        private static void MovePrinceToSeat(FeudatorySnapshot pSnapshot)
        {
            Actor prince = FindActor(pSnapshot.PrinceActorId);
            City seat = FindCity(pSnapshot.SeatCityId);
            if (prince?.data == null || prince.isRekt() || seat?.data == null ||
                seat.kingdom != prince.kingdom)
                return;
            prince.joinCity(seat);
        }

        private static void RemoveGarrison(FeudatorySnapshot pSnapshot)
        {
            FeudatoryGarrisonService.Disband(pSnapshot);
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try
            {
                ActorManager units = World.world?.units;
                return units?.get(pActorId);
            }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try
            {
                CityManager cities = World.world?.cities;
                return cities?.get(pCityId);
            }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try
            {
                KingdomManager kingdoms = World.world?.kingdoms;
                return kingdoms?.get(pKingdomId);
            }
            catch { return null; }
        }

        private static FeudatoryCityDisplayRow[] BuildCityRows(
            IReadOnlyList<long> pCityIds)
        {
            int count = Math.Min(FeudatoryRules.MaximumCities,
                pCityIds?.Count ?? 0);
            var rows = new FeudatoryCityDisplayRow[count];
            for (int i = 0; i < count; i++)
            {
                long cityId = pCityIds[i];
                City city = FindCity(cityId);
                Actor governor = city?.leader;
                rows[i] = new FeudatoryCityDisplayRow(cityId,
                    city?.data?.name ?? "", governor?.data?.id ?? -1L,
                    governor?.getName() ?? "");
            }
            return rows;
        }

        private static void RepairLoadedIdentity(FeudatoryTableItem pRow,
            Actor pPrince, Kingdom pEmpire, City pSeat)
        {
            if (pRow == null || pPrince?.data == null || pEmpire?.data == null)
                return;
            string titleName = NobleRankService.ReadHot(pPrince).TitleName;
            if (string.IsNullOrWhiteSpace(titleName))
                NobleRankService.EnsureFeudatoryPrinceTitle(pEmpire,
                    pPrince, out titleName);
            string feudatoryName = FeudatoryRules.BuildFeudatoryName(
                titleName);
            if (string.IsNullOrWhiteSpace(feudatoryName))
                feudatoryName = pRow.feudatory_name ?? "";
            long shiBranchId = LineageService.EnsureFeudatoryShiBranch(
                pPrince, titleName, pSeat,
                pReuseInheritedFeudatoryBranch: true);
            if (shiBranchId < 0) shiBranchId = pRow.shi_branch_id;
            bool changed = pRow.feudatory_name != feudatoryName ||
                           pRow.shi_branch_id != shiBranchId;
            pRow.feudatory_name = feudatoryName;
            pRow.shi_branch_id = shiBranchId;
            if (!changed) return;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    FeudatoryTableItem.GetTableName() +
                    " SET FEUDATORY_NAME=@name,SHI_BRANCH_ID=@shi " +
                    "WHERE FEUDATORY_ID=@id AND STATUS=0 AND END_TIME<0";
                command.Parameters.AddWithValue("@name", feudatoryName);
                command.Parameters.AddWithValue("@shi", shiBranchId);
                command.Parameters.AddWithValue("@id", pRow.feudatory_id);
                command.ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory identity repair failed: " +
                                    exception.Message);
            }
        }

        private static string BuildPrinceShiLabel(long pActorId,
            string pFeudatoryName = "")
        {
            long shiId = LineageQuery.GetActorShiId(pActorId);
            ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(shiId);
            if (branch == null) return "";
            if (branch.source_type == ShiSourceType.FEUDATORY)
            {
                string titleName = string.IsNullOrWhiteSpace(branch.state_name)
                    ? pFeudatoryName
                    : branch.state_name;
                return ShiBranchRules.BuildFeudatoryDisplayName(titleName,
                    branch.clan_name);
            }
            return ShiBranchRules.BuildDisplayName(branch.origin_city_name,
                branch.clan_name);
        }

        private static void MarkPrinceChildren(Actor pPrince,
            long pFeudatoryId)
        {
            if (pPrince?.data == null || pFeudatoryId < 0) return;
            try
            {
                foreach (Actor child in pPrince.getChildren(false))
                {
                    if (child?.data == null) continue;
                    if (child.data.parent_id_1 != pPrince.data.id &&
                        child.data.parent_id_2 != pPrince.data.id)
                        continue;
                    child.data.set(LineageKeys.FEUDATORY_PARENT_ACTOR_ID,
                        pPrince.data.id);
                    child.data.set(LineageKeys.FEUDATORY_LINE_ID,
                        pFeudatoryId);
                }
            }
            catch { }
        }

        private static bool RefreshSuccessor(long pFeudatoryId,
            long pExcludedActorId = -1L)
        {
            if (!TryGet(pFeudatoryId, out FeudatorySnapshot snapshot))
                return false;
            Actor prince = FindActor(snapshot.PrinceActorId);
            Kingdom empire = FindKingdom(snapshot.EmpireKingdomId);
            Actor successor = FindFeudatorySuccessor(prince,
                snapshot.ShiBranchId, snapshot.FeudatoryId, empire,
                pExcludedActorId);
            long successorId = successor?.data?.id ?? -1L;
            string successorName = successor?.getName() ?? "";
            if (snapshot.SuccessorActorId == successorId &&
                snapshot.SuccessorName == successorName)
                return false;
            PublishReplaced(snapshot.WithPrince(snapshot.PrinceActorId,
                snapshot.PrinceName, snapshot.ShiBranchId,
                snapshot.PrinceShiLabel, successorId, successorName));
            DynasticMaleLineContinuityService.RequestContinuation(prince);
            return true;
        }

        private static Actor FindFeudatorySuccessor(Actor pPrince,
            long pShiBranchId = -1L, long pFeudatoryId = -1L,
            Kingdom pEmpire = null, long pExcludedActorId = -1L)
        {
            if (pPrince?.data != null)
            {
                try { LineageService.SyncExistingChildrenAfterLineageChange(pPrince); }
                catch { }
                if (pShiBranchId < 0)
                    pShiBranchId = LineageQuery.GetActorShiId(pPrince.data.id);
                pEmpire ??= pPrince.kingdom;
            }
            if (pShiBranchId < 0 || pEmpire?.data == null) return null;

            ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(
                pShiBranchId);
            long founderActorId = branch?.founder_actor_id ??
                                  pPrince?.data?.id ?? -1L;
            var candidates = new List<FeudatorySuccessionCandidate>();
            var seen = new HashSet<long>();
            if (pPrince?.data != null)
            {
                foreach (Actor child in pPrince.getChildren(false))
                    AddSuccessionCandidate(candidates, seen, child, pPrince,
                        pEmpire, pShiBranchId, pFeudatoryId,
                        founderActorId, pExcludedActorId,
                        directSon: true);
            }
            foreach (long actorId in LineageQuery.GetLivingShiMemberIds(
                         pShiBranchId, MaximumSuccessionKinNodes))
            {
                AddSuccessionCandidate(candidates, seen,
                    FindActor(actorId), pPrince, pEmpire, pShiBranchId,
                    pFeudatoryId, founderActorId, pExcludedActorId,
                    directSon: false);
            }

            return FindActor(FeudatorySuccessionRules.SelectSuccessor(candidates));
        }

        private static void AddSuccessionCandidate(
            List<FeudatorySuccessionCandidate> pCandidates,
            HashSet<long> pSeen, Actor pCandidate, Actor pPrince,
            Kingdom pEmpire, long pShiBranchId, long pFeudatoryId,
            long pFounderActorId, long pExcludedActorId, bool directSon)
        {
            if (pCandidate?.data == null || !pSeen.Add(pCandidate.data.id))
                return;
            bool alreadyOtherPrince = TryGetByPrince(pCandidate.data.id,
                out FeudatorySnapshot existing) &&
                existing.FeudatoryId != pFeudatoryId;
            bool alreadyOtherSuccessor = TryGetBySuccessor(
                pCandidate.data.id, out FeudatorySnapshot otherSuccession) &&
                otherSuccession.FeudatoryId != pFeudatoryId;
            bool imperialHeir = HeirService.IsCurrentHeir(pEmpire,
                pCandidate);
            bool biologicalDirectSon = directSon &&
                FeudatorySuccessionRules.IsDirectBiologicalSon(
                    pCandidate.data.parent_id_1,
                    pCandidate.data.parent_id_2,
                    pPrince?.data?.id ?? -1L,
                    pCandidate.isSexMale());
            bool adult = pCandidate.isAdult();
            bool ageEligible = adult || biologicalDirectSon;
            bool eligible = pCandidate.kingdom == pEmpire &&
                pCandidate.data.id != pExcludedActorId &&
                !alreadyOtherPrince && !alreadyOtherSuccessor &&
                !imperialHeir && !pCandidate.isRekt() &&
                pCandidate.isAlive() && pCandidate != pPrince &&
                !pCandidate.isKing() && pCandidate.isSexMale() &&
                ageEligible && !pCandidate.hasTrait("madness") &&
                !SlaveService.IsSlave(pCandidate);
            bool sameShi = LineageQuery.GetActorShiId(pCandidate.data.id) ==
                           pShiBranchId;
            bool directTreeDescendant = pFounderActorId >= 0 &&
                LineageQuery.IsAgnaticDescendantOf(pCandidate.data.id,
                    pFounderActorId);
            int kinDistance = directTreeDescendant
                ? LineageQuery.GetAgnaticDepth(pCandidate.data.id,
                    pFounderActorId)
                : MaximumSuccessionKinDistance + 1;
            pCandidate.data.get(LineageKeys.BIRTH_LEGITIMACY,
                out bool legitimateBirth, true);
            pCandidates.Add(new FeudatorySuccessionCandidate(
                pCandidate.data.id, eligible, biologicalDirectSon, sameShi,
                kinDistance, pCandidate.data.created_time,
                directTreeDescendant, legitimateBirth, adult));
        }

        private static int GetArmySize(long pArmyId)
        {
            if (pArmyId < 0) return 0;
            try
            {
                ArmyManager armies = World.world?.armies;
                return Math.Max(0, armies?.get(pArmyId)?.countUnits() ?? 0);
            }
            catch { return 0; }
        }

        private static void Publish(IReadOnlyList<FeudatorySnapshot> pSnapshots)
        {
            lock (CacheLock) _cache = FeudatoryCache.Build(pSnapshots);
        }

        private static int SafeCurrentYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private sealed class FeudatoryCache
        {
            public static readonly FeudatoryCache Empty = Build(
                Array.Empty<FeudatorySnapshot>());

            public readonly Dictionary<long, FeudatorySnapshot> ById;
            public readonly Dictionary<long, FeudatorySnapshot[]> ByKingdom;
            public readonly Dictionary<long, long> FeudatoryByCity;
            public readonly Dictionary<long, long> FeudatoryByPrince;
            public readonly Dictionary<long, long> FeudatoryByShiBranch;
            public readonly Dictionary<long, long> FeudatoryBySuccessor;

            private FeudatoryCache(
                Dictionary<long, FeudatorySnapshot> pById,
                Dictionary<long, FeudatorySnapshot[]> pByKingdom,
                Dictionary<long, long> pFeudatoryByCity,
                Dictionary<long, long> pFeudatoryByPrince,
                Dictionary<long, long> pFeudatoryByShiBranch,
                Dictionary<long, long> pFeudatoryBySuccessor)
            {
                ById = pById;
                ByKingdom = pByKingdom;
                FeudatoryByCity = pFeudatoryByCity;
                FeudatoryByPrince = pFeudatoryByPrince;
                FeudatoryByShiBranch = pFeudatoryByShiBranch;
                FeudatoryBySuccessor = pFeudatoryBySuccessor;
            }

            public static FeudatoryCache Build(
                IReadOnlyList<FeudatorySnapshot> pSnapshots)
            {
                var byId = new Dictionary<long, FeudatorySnapshot>();
                var kingdomLists = new Dictionary<long, List<FeudatorySnapshot>>();
                var byCity = new Dictionary<long, long>();
                var byPrince = new Dictionary<long, long>();
                var byShiBranch = new Dictionary<long, long>();
                var bySuccessor = new Dictionary<long, long>();
                var ordered = new List<FeudatorySnapshot>();
                int count = pSnapshots?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    FeudatorySnapshot snapshot = pSnapshots[i];
                    if (snapshot == null) continue;
                    ordered.Add(snapshot);
                }
                ordered.Sort((left, right) =>
                    left.FeudatoryId.CompareTo(right.FeudatoryId));
                for (int i = 0; i < ordered.Count; i++)
                {
                    FeudatorySnapshot snapshot = ordered[i];
                    byId[snapshot.FeudatoryId] = snapshot;
                    byPrince[snapshot.PrinceActorId] = snapshot.FeudatoryId;
                    if (snapshot.ShiBranchId >= 0 &&
                        !byShiBranch.ContainsKey(snapshot.ShiBranchId))
                        byShiBranch[snapshot.ShiBranchId] =
                            snapshot.FeudatoryId;
                    if (snapshot.SuccessorActorId >= 0 &&
                        !bySuccessor.ContainsKey(snapshot.SuccessorActorId))
                        bySuccessor[snapshot.SuccessorActorId] =
                            snapshot.FeudatoryId;
                    for (int cityIndex = 0; cityIndex < snapshot.CityIds.Count;
                         cityIndex++)
                        byCity[snapshot.CityIds[cityIndex]] = snapshot.FeudatoryId;
                    if (!kingdomLists.TryGetValue(snapshot.EmpireKingdomId,
                            out List<FeudatorySnapshot> rows))
                    {
                        rows = new List<FeudatorySnapshot>();
                        kingdomLists[snapshot.EmpireKingdomId] = rows;
                    }
                    rows.Add(snapshot);
                }

                var byKingdom = new Dictionary<long, FeudatorySnapshot[]>();
                foreach (KeyValuePair<long, List<FeudatorySnapshot>> pair in
                         kingdomLists)
                {
                    pair.Value.Sort((left, right) =>
                        left.FeudatoryId.CompareTo(right.FeudatoryId));
                    byKingdom[pair.Key] = pair.Value.ToArray();
                }
                return new FeudatoryCache(byId, byKingdom, byCity, byPrince,
                    byShiBranch, bySuccessor);
            }
        }
    }
}
