using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal sealed class DiplomaticOperationPreview
    {
        public bool Available;
        public string Reason = "invalid";
        public DiplomaticOperationType Type;
        public long SourceKingdomId = -1L;
        public long TargetKingdomId = -1L;
        public long TargetCityId = -1L;
        public string ProjectType = "";
        public bool StrongForgery;
        public int DurationYears;
        public int SuccessChance;
        public int DiscoveryChance;
        public int NetworkStrength;
        public int NetworkUntilYear;
        public int SpyPoints;
        public int SpyPointsPerYear;
        public int PointCost;
    }

    internal static class DiplomaticOperationService
    {
        private const int StatusPending = 0;
        private const int StatusProcessing = 1;
        private const int StatusSucceeded = 2;
        private const int StatusFailed = 3;
        private const int StatusCancelled = 4;
        private const double WorldTimePerYear = 365d / 6d;
        private const int EspionagePenaltyYears = 5;
        private const int AiScheduleYears = 8;

        private static double _nextDueTime = double.PositiveInfinity;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static bool HasActiveSpyNetwork(Kingdom pSource,
            Kingdom pTarget, out int pStrength, out int pUntilYear)
        {
            pStrength = 0;
            pUntilYear = -1;
            return pSource?.data != null && pTarget?.data != null &&
                   TryReadActiveNetwork(pSource.id, pTarget.id,
                       SafeYear(), out pStrength, out pUntilYear);
        }

        public static bool ConsumeActiveSpyNetwork(long pSourceKingdomId,
            long pTargetKingdomId)
        {
            if (!Ready || pSourceKingdomId < 0 || pTargetKingdomId < 0)
                return false;
            try
            {
                using var command = new SQLiteCommand(
                    "UPDATE SpyNetwork SET ACTIVE=0 WHERE " +
                    "SOURCE_KINGDOM_ID=@source AND TARGET_KINGDOM_ID=@target " +
                    "AND ACTIVE=1", DB);
                command.Parameters.AddWithValue("@source",
                    pSourceKingdomId);
                command.Parameters.AddWithValue("@target",
                    pTargetKingdomId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Spy network annexation consume failed: " +
                    exception.Message);
                return false;
            }
        }

        public static DiplomaticOperationPreview PrepareSpyNetwork(
            Kingdom pSource, Kingdom pTarget)
        {
            DiplomaticOperationPreview preview = BasePreview(pSource,
                pTarget, DiplomaticOperationType.SpyNetwork);
            if (!string.IsNullOrEmpty(preview.Reason)) return preview;
            if (IsTargetingSuzerain(pSource, pTarget))
            {
                preview.Reason = "cannot_spy_on_suzerain";
                return preview;
            }
            if (HasPendingPairOperation(pSource.id, pTarget.id, -1L))
            {
                preview.Reason = "covert_operation_pending";
                return preview;
            }
            if (TryReadActiveNetwork(pSource.id, pTarget.id,
                    SafeYear(), out int activeStrength,
                    out int activeUntilYear))
            {
                preview.NetworkStrength = activeStrength;
                preview.NetworkUntilYear = activeUntilYear;
                preview.SpyPoints = activeStrength;
                preview.SpyPointsPerYear = SpyNetworkPointRules.PointsPerYear;
                preview.Reason = "spy_network_active";
                return preview;
            }
            FillTimingAndChances(preview, pSource, pTarget,
                forgery: false, strongForgery: false);
            preview.Available = true;
            preview.Reason = "";
            return preview;
        }

        public static DiplomaticOperationPreview PrepareForgeDocuments(
            Kingdom pSource, Kingdom pTarget, City pTargetCity,
            string pProjectType)
        {
            return PrepareForgeDocuments(pSource, pTarget, pTargetCity,
                pProjectType, -1L);
        }

        private static DiplomaticOperationPreview PrepareForgeDocuments(
            Kingdom pSource, Kingdom pTarget, City pTargetCity,
            string pProjectType, long pIgnoreOperationId)
        {
            DiplomaticOperationPreview preview = BasePreview(pSource,
                pTarget, DiplomaticOperationType.ForgeDocuments);
            preview.TargetCityId = pTargetCity?.data?.id ?? -1L;
            preview.ProjectType = pProjectType ?? "";
            preview.StrongForgery = pProjectType ==
                                     WarTerritoryService.PROJECT_STRONG_CLAIM;
            if (!string.IsNullOrEmpty(preview.Reason)) return preview;
            if (IsTargetingSuzerain(pSource, pTarget))
            {
                preview.Reason = "cannot_spy_on_suzerain";
                return preview;
            }
            if (HasPendingPairOperation(pSource.id, pTarget.id,
                    pIgnoreOperationId))
            {
                preview.Reason = "covert_operation_pending";
                return preview;
            }
            if (pProjectType != WarTerritoryService.PROJECT_WEAK_CLAIM &&
                pProjectType != WarTerritoryService.PROJECT_STRONG_CLAIM)
            {
                preview.Reason = "fabrication_unavailable";
                return preview;
            }
            bool activeNetwork = TryReadActiveNetwork(pSource.id,
                pTarget.id, SafeYear(), out int networkStrength,
                out int networkUntilYear);
            preview.NetworkStrength = networkStrength;
            preview.NetworkUntilYear = networkUntilYear;
            preview.SpyPoints = networkStrength;
            preview.SpyPointsPerYear = SpyNetworkPointRules.PointsPerYear;
            preview.PointCost = SpyNetworkPointRules.Cost(
                preview.StrongForgery
                    ? SpyClaimKind.Strong
                    : SpyClaimKind.Weak);
            bool cityOwned = pTargetCity?.data != null &&
                             !pTargetCity.isRekt() &&
                             pTargetCity.kingdom == pTarget;
            bool canFabricate = cityOwned &&
                                WarTerritoryService.CanFabricateAgainst(
                                    pSource, pTarget, pTargetCity, out _);
            preview.Reason = WarDecisionService.HasActiveNormalClaim(
                pSource, pTarget)
                ? "claim_already_purchased"
                : SpyNetworkPointRules.PurchaseReason(activeNetwork,
                    cityOwned, canFabricate, networkStrength,
                    preview.StrongForgery
                        ? SpyClaimKind.Strong
                        : SpyClaimKind.Weak);
            if (!string.IsNullOrEmpty(preview.Reason)) return preview;
            preview.Available = true;
            return preview;
        }

        public static bool TryStartSpyNetwork(Kingdom pSource,
            Kingdom pTarget, bool pPlayerInitiated,
            out long pOperationId, out string pReason)
        {
            DiplomaticOperationPreview preview = PrepareSpyNetwork(
                pSource, pTarget);
            return TryInsert(preview, pPlayerInitiated,
                out pOperationId, out pReason);
        }

        public static bool TryStartForgeDocuments(Kingdom pSource,
            Kingdom pTarget, City pTargetCity, string pProjectType,
            bool pPlayerInitiated, out long pOperationId,
            out string pReason)
        {
            DiplomaticOperationPreview preview = PrepareForgeDocuments(
                pSource, pTarget, pTargetCity, pProjectType);
            pOperationId = -1L;
            pReason = preview?.Reason ?? "invalid";
            if (!Ready || preview == null || !preview.Available) return false;
            SpyClaimKind kind = preview.StrongForgery
                ? SpyClaimKind.Strong
                : SpyClaimKind.Weak;
            string claimType = preview.StrongForgery
                ? WarTerritoryService.CLAIM_STRONG
                : WarTerritoryService.CLAIM_WEAK;
            string reasonKey = preview.StrongForgery
                ? "strong_claim"
                : "weak_claim";
            int yearsValid = preview.StrongForgery ? 45 : 20;
            int purchaseYear = SafeYear();
            string purchaseKey = SpyNetworkPointRules.PurchaseKey(kind,
                preview.TargetCityId, purchaseYear);
            long claimId = -1L;
            SpyClaimPurchaseResult result = SpyNetworkPointLedger.TryPurchase(
                DB, preview.SourceKingdomId, preview.TargetKingdomId,
                purchaseYear, LineageService.CurTime(), kind, purchaseKey,
                (connection, transaction) =>
                    WarDecisionService.TryCreateClaimInTransaction(pSource,
                        pTarget, pTargetCity, claimType,
                        WarDecisionService.WAR_NORMAL, reasonKey,
                        yearsValid, transaction, out claimId));
            pReason = result.Reason;
            if (!result.Success) return false;
            pOperationId = claimId;
            WarDecisionService.RecordClaimCreated(pSource, pTarget,
                reasonKey, WarDecisionService.WAR_NORMAL);
            return true;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!Ready || !IsLiveRealm(pKingdom) || !pKingdom.hasKing()) return;
            int year = SafeYear();
            if (Math.Abs((year + pKingdom.id) % AiScheduleYears) != 0)
                return;
            Kingdom target = ReadOneBorderContact(pKingdom);
            if (!IsLiveRealm(target)) return;
            if (TryReadActiveNetwork(pKingdom.id, target.id, year,
                    out _, out _))
            {
                City city = ReadOneFabricationCity(pKingdom, target);
                if (city?.data != null)
                    TryStartForgeDocuments(pKingdom, target, city,
                        WarTerritoryService.PROJECT_WEAK_CLAIM,
                        pPlayerInitiated: false, out _, out _);
                return;
            }
            TryStartSpyNetwork(pKingdom, target,
                pPlayerInitiated: false, out _, out _);
        }

        public static void ProcessFrame()
        {
            if (!Ready) return;
            double now = LineageService.CurTime();
            if (now < _nextDueTime) return;
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            OperationRow row;
            try { row = ClaimOneDue(now); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "diplomatic_operation_claim", diagnostic);
            }
            if (row == null)
            {
                diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                try { _nextDueTime = ReadNextDueTime(); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "diplomatic_operation_next_due", diagnostic);
                }
                return;
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { Resolve(row); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "diplomatic_operation_resolve", diagnostic);
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { _nextDueTime = ReadNextDueTime(); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "diplomatic_operation_next_due", diagnostic);
            }
        }

        public static void ResetRuntime()
        {
            _nextDueTime = double.PositiveInfinity;
            if (!Ready) return;
            try
            {
                using var command = new SQLiteCommand(
                    "UPDATE DiplomaticOperation SET STATUS=0 " +
                    "WHERE STATUS=1", DB);
                command.ExecuteNonQuery();
                _nextDueTime = ReadNextDueTime();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Covert operation recovery failed: " +
                                    exception.Message);
            }
        }

        private static bool TryInsert(DiplomaticOperationPreview pPreview,
            bool pPlayerInitiated, out long pOperationId,
            out string pReason)
        {
            pOperationId = -1L;
            pReason = pPreview?.Reason ?? "invalid";
            if (!Ready || pPreview == null || !pPreview.Available)
                return false;
            try
            {
                int year = SafeYear();
                double now = LineageService.CurTime();
                double dueTime = now +
                                 pPreview.DurationYears * WorldTimePerYear;
                pOperationId = TableIdAllocator.Next(DB,
                    DiplomaticOperationTableItem.GetTableName(),
                    "OPERATION_ID");
                DB.Insert(DiplomaticOperationTableItem.GetTableName(),
                    ColumnVal.Create("OPERATION_ID", pOperationId),
                    ColumnVal.Create("SOURCE_KINGDOM_ID",
                        pPreview.SourceKingdomId),
                    ColumnVal.Create("TARGET_KINGDOM_ID",
                        pPreview.TargetKingdomId),
                    ColumnVal.Create("OPERATION_TYPE",
                        TypeId(pPreview.Type)),
                    ColumnVal.Create("STATUS", StatusPending),
                    ColumnVal.Create("TARGET_CITY_ID", pPreview.TargetCityId),
                    ColumnVal.Create("PROJECT_TYPE", pPreview.ProjectType),
                    ColumnVal.Create("STRONG_FORGERY",
                        pPreview.StrongForgery ? 1 : 0),
                    ColumnVal.Create("START_YEAR", year),
                    ColumnVal.Create("DUE_YEAR",
                        year + pPreview.DurationYears),
                    ColumnVal.Create("START_TIME", now),
                    ColumnVal.Create("DUE_TIME", dueTime),
                    ColumnVal.Create("NETWORK_STRENGTH",
                        pPreview.NetworkStrength),
                    ColumnVal.Create("SUCCESS_CHANCE",
                        pPreview.SuccessChance),
                    ColumnVal.Create("DISCOVERY_CHANCE",
                        pPreview.DiscoveryChance),
                    ColumnVal.Create("DISCOVERED", 0),
                    ColumnVal.Create("RESULT", ""),
                    ColumnVal.Create("PLAYER_INITIATED",
                        pPlayerInitiated ? 1 : 0));
                if (dueTime < _nextDueTime) _nextDueTime = dueTime;
                pReason = "";
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Covert operation start failed: " +
                                    exception.Message);
                pOperationId = -1L;
                pReason = "covert_operation_write_failed";
                return false;
            }
        }

        private static OperationRow ClaimOneDue(double pNow)
        {
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                OperationRow row;
                using (var select = new SQLiteCommand(
                           "SELECT OPERATION_ID,SOURCE_KINGDOM_ID," +
                           "TARGET_KINGDOM_ID,OPERATION_TYPE,TARGET_CITY_ID," +
                           "PROJECT_TYPE,STRONG_FORGERY,START_YEAR,DUE_YEAR," +
                           "NETWORK_STRENGTH,SUCCESS_CHANCE," +
                           "DISCOVERY_CHANCE,PLAYER_INITIATED FROM " +
                           "DiplomaticOperation WHERE STATUS=0 AND DUE_TIME<=@now ORDER BY DUE_TIME,OPERATION_ID LIMIT 1",
                           DB, transaction))
                {
                    select.Parameters.AddWithValue("@now", pNow);
                    using SQLiteDataReader reader = select.ExecuteReader();
                    row = reader.Read() ? ReadRow(reader) : null;
                }
                if (row == null)
                {
                    transaction.Commit();
                    return null;
                }
                using var claim = new SQLiteCommand(
                    "UPDATE DiplomaticOperation SET STATUS=1 WHERE OPERATION_ID=@id AND STATUS=0",
                    DB, transaction);
                claim.Parameters.AddWithValue("@id", row.OperationId);
                if (claim.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return null;
                }
                transaction.Commit();
                return row;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Covert operation dequeue failed: " +
                                    exception.Message);
                return null;
            }
        }

        private static void Resolve(OperationRow pRow)
        {
            Kingdom source = FindKingdom(pRow.SourceKingdomId);
            Kingdom target = FindKingdom(pRow.TargetKingdomId);
            if (!IsLiveRealm(source) || !IsLiveRealm(target))
            {
                Finish(pRow, StatusCancelled, "realm_invalid", 0,
                    pRow.DueYear);
                return;
            }
            int successRoll = DiplomacyActionExpansionRules.StablePercentRoll(
                pRow.OperationId, pRow.SourceKingdomId,
                pRow.TargetKingdomId, pRow.StartYear, 17);
            int discoveryRoll = DiplomacyActionExpansionRules.StablePercentRoll(
                pRow.OperationId, pRow.SourceKingdomId,
                pRow.TargetKingdomId, pRow.StartYear, 53);
            DiplomaticOperationOutcome outcome =
                DiplomacyActionExpansionRules.ResolveOperationOutcome(
                    successRoll, discoveryRoll, pRow.SuccessChance,
                    pRow.DiscoveryChance);
            bool succeeded = false;
            string result;
            int strength = pRow.NetworkStrength;
            int dueYear = pRow.DueYear;

            if (pRow.Type == DiplomaticOperationType.SpyNetwork)
            {
                succeeded = outcome.Succeeded;
                if (succeeded)
                {
                    strength = DiplomacyActionExpansionRules
                        .NetworkStrengthForSuccess(pRow.SuccessChance);
                    dueYear = DiplomacyActionExpansionRules
                        .NetworkExpiryYear(SafeYear());
                    result = "network_active";
                    SpyNetworkPointLedger.UpsertNetwork(DB,
                        pRow.SourceKingdomId, pRow.TargetKingdomId, 0,
                        SafeYear(), LineageService.CurTime());
                }
                else result = "spy_network_failed";
            }
            else if (pRow.Type == DiplomaticOperationType.ForgeDocuments)
            {
                Finish(pRow, StatusCancelled, "legacy_forgery_removed",
                    strength, dueYear, outcome.Discovered);
                return;
            }
            else
            {
                Finish(pRow, StatusCancelled, "unknown_operation", 0,
                    dueYear);
                return;
            }

            Finish(pRow, succeeded ? StatusSucceeded : StatusFailed,
                result, strength, dueYear, outcome.Discovered);
            ApplyDiscovery(pRow, source, target, outcome.Discovered);
            DiplomacyConversationService.RecordCovertResult(source, target,
                TypeId(pRow.Type), result, outcome.Discovered);
        }

        private static void RecordCancelledOperationResult(OperationRow pRow,
            Kingdom pSource, Kingdom pTarget, string pReason,
            bool pDiscovered)
        {
            DiplomacyConversationService.RecordCovertResult(
                pSource, pTarget, TypeId(pRow.Type),
                string.IsNullOrEmpty(pReason) ? "invalid" : pReason,
                pDiscovered);
        }

        private static void ApplyDiscovery(OperationRow pRow,
            Kingdom pSource, Kingdom pTarget, bool pDiscovered)
        {
            if (!pDiscovered) return;
            int year = SafeYear();
            DiplomaticRelationModifierService.Upsert(pSource.id,
                pTarget.id, "discovered_espionage", pRow.OperationId,
                -30, year, year + EspionagePenaltyYears);
        }

        private static void Finish(OperationRow pRow, int pStatus,
            string pResult, int pNetworkStrength, int pDueYear,
            bool pDiscovered = false)
        {
            try
            {
                using var command = new SQLiteCommand(
                    "UPDATE DiplomaticOperation SET STATUS=@status," +
                    "RESULT=@result,NETWORK_STRENGTH=@strength," +
                    "DUE_YEAR=@due,DISCOVERED=@discovered WHERE " +
                    "OPERATION_ID=@id AND STATUS=1", DB);
                command.Parameters.AddWithValue("@status", pStatus);
                command.Parameters.AddWithValue("@result", pResult ?? "");
                command.Parameters.AddWithValue("@strength", pNetworkStrength);
                command.Parameters.AddWithValue("@due", pDueYear);
                command.Parameters.AddWithValue("@discovered",
                    pDiscovered ? 1 : 0);
                command.Parameters.AddWithValue("@id", pRow.OperationId);
                if (command.ExecuteNonQuery() != 1)
                    ModClass.LogWarning("Covert operation finish lost claim: " +
                                        pRow.OperationId);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Covert operation finish failed: " +
                                    exception.Message);
            }
        }

        private static bool TryReadActiveNetwork(long pSourceId,
            long pTargetId, int pYear, out int pStrength,
            out int pUntilYear)
        {
            pStrength = 0;
            pUntilYear = int.MaxValue;
            if (!Ready) return false;
            try
            {
                SpyNetworkPointSnapshot snapshot = SpyNetworkPointLedger.Read(
                    DB, pSourceId, pTargetId, pYear,
                    LineageService.CurTime());
                pStrength = snapshot.Points;
                return snapshot.Active;
            }
            catch { return false; }
        }

        private static double ReadNextDueTime()
        {
            if (!Ready) return double.PositiveInfinity;
            try
            {
                using var command = new SQLiteCommand(
                    "SELECT MIN(DUE_TIME) FROM DiplomaticOperation " +
                    "WHERE STATUS=0 AND DUE_TIME>=0", DB);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? double.PositiveInfinity
                    : Convert.ToDouble(value);
            }
            catch { return double.PositiveInfinity; }
        }

        private static bool HasPendingPairOperation(long pSourceId,
            long pTargetId, long pIgnoreOperationId)
        {
            using var command = new SQLiteCommand(
                "SELECT 1 FROM DiplomaticOperation WHERE ((" +
                "SOURCE_KINGDOM_ID=@source AND TARGET_KINGDOM_ID=@target) " +
                "OR (SOURCE_KINGDOM_ID=@target AND " +
                "TARGET_KINGDOM_ID=@source)) " +
                "AND STATUS IN (0,1) AND OPERATION_ID<>@ignore LIMIT 1", DB);
            command.Parameters.AddWithValue("@source", pSourceId);
            command.Parameters.AddWithValue("@target", pTargetId);
            command.Parameters.AddWithValue("@ignore", pIgnoreOperationId);
            return command.ExecuteScalar() != null;
        }

        private static DiplomaticOperationPreview BasePreview(
            Kingdom pSource, Kingdom pTarget, DiplomaticOperationType pType)
        {
            var preview = new DiplomaticOperationPreview
            {
                Type = pType,
                SourceKingdomId = pSource?.id ?? -1L,
                TargetKingdomId = pTarget?.id ?? -1L
            };
            if (!Ready || !IsLiveRealm(pSource) || !IsLiveRealm(pTarget) ||
                pSource == pTarget)
            {
                preview.Reason = "invalid";
                return preview;
            }
            if (SafeEnemy(pSource, pTarget))
            {
                preview.Reason = "at_war";
                return preview;
            }
            preview.Reason = "";
            return preview;
        }

        private static void FillTimingAndChances(
            DiplomaticOperationPreview pPreview, Kingdom pSource,
            Kingdom pTarget, bool forgery, bool strongForgery)
        {
            Actor sourceKing = pSource.king;
            Actor targetKing = pTarget.king;
            int sourceDiplomacy = Math.Max(0, sourceKing?.diplomacy ?? 0);
            int sourceIntelligence = Math.Max(0,
                sourceKing?.intelligence ?? 0);
            int targetDiplomacy = Math.Max(0, targetKing?.diplomacy ?? 0);
            int targetIntelligence = Math.Max(0,
                targetKing?.intelligence ?? 0);
            pPreview.DurationYears = DiplomacyActionExpansionRules
                .OperationDurationYears(CapitalDistance(pSource, pTarget),
                    sourceDiplomacy, sourceIntelligence, strongForgery);
            DiplomaticOperationChances chances =
                DiplomacyActionExpansionRules.OperationChances(
                    sourceDiplomacy, sourceIntelligence, targetDiplomacy,
                    targetIntelligence, forgery, strongForgery);
            pPreview.SuccessChance = chances.SuccessChance;
            pPreview.DiscoveryChance = chances.DiscoveryChance;
        }

        private static Kingdom ReadOneBorderContact(Kingdom pKingdom)
        {
            int count = pKingdom?.cities?.Count ?? 0;
            if (count == 0) return null;
            pKingdom.data.get(LineageKeys.DIPLOMACY_COVERT_CITY_CURSOR,
                out int cursor, 0);
            cursor = Math.Max(0, cursor) % count;
            City city = pKingdom.cities[cursor];
            pKingdom.data.set(LineageKeys.DIPLOMACY_COVERT_CITY_CURSOR,
                (cursor + 1) % count);
            if (city?.neighbours_kingdoms == null) return null;
            foreach (Kingdom target in city.neighbours_kingdoms)
                if (IsLiveRealm(target) && target != pKingdom) return target;
            return null;
        }

        private static City ReadOneFabricationCity(Kingdom pSource,
            Kingdom pTarget)
        {
            try
            {
                int scanned = 0;
                foreach (City city in pTarget.getCities())
                {
                    if (scanned++ >= DiplomacyActionExpansionRules
                            .MaximumAiForgeryCitiesScanned) break;
                    if (city?.data != null &&
                        WarTerritoryService.CanFabricateAgainst(pSource,
                            pTarget, city, out _)) return city;
                }
            }
            catch { }
            return null;
        }

        private static bool IsTargetingSuzerain(Kingdom pSource,
            Kingdom pTarget)
        {
            Kingdom current = pSource;
            for (int i = 0; i < 16 && current?.data != null; i++)
            {
                current = VassalService.GetSuzerain(current) ??
                          VassalService.GetTributarySuzerain(current);
                if (current == pTarget) return true;
            }
            return false;
        }

        private static OperationRow ReadRow(SQLiteDataReader pReader)
        {
            return new OperationRow
            {
                OperationId = pReader.GetInt64(0),
                SourceKingdomId = pReader.GetInt64(1),
                TargetKingdomId = pReader.GetInt64(2),
                Type = ParseType(ReadString(pReader, 3)),
                TargetCityId = pReader.GetInt64(4),
                ProjectType = ReadString(pReader, 5),
                StrongForgery = pReader.GetInt32(6) != 0,
                StartYear = pReader.GetInt32(7),
                DueYear = pReader.GetInt32(8),
                NetworkStrength = pReader.GetInt32(9),
                SuccessChance = pReader.GetInt32(10),
                DiscoveryChance = pReader.GetInt32(11),
                PlayerInitiated = pReader.GetInt32(12) != 0
            };
        }

        private static string TypeId(DiplomaticOperationType pType)
        {
            return pType == DiplomaticOperationType.SpyNetwork
                ? "spy_network"
                : pType == DiplomaticOperationType.ForgeDocuments
                    ? "forge_documents"
                    : "none";
        }

        private static DiplomaticOperationType ParseType(string pType)
        {
            return pType == "spy_network"
                ? DiplomaticOperationType.SpyNetwork
                : pType == "forge_documents"
                    ? DiplomaticOperationType.ForgeDocuments
                    : DiplomaticOperationType.None;
        }

        private static string ReadString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : pReader.GetString(pIndex);
        }

        private static float CapitalDistance(Kingdom pSource,
            Kingdom pTarget)
        {
            try
            {
                WorldTile first = pSource?.capital?.getTile();
                WorldTile second = pTarget?.capital?.getTile();
                return first != null && second != null
                    ? Toolbox.DistTile(first, second)
                    : 60f;
            }
            catch { return 60f; }
        }

        private static bool SafeEnemy(Kingdom pSource, Kingdom pTarget)
        {
            try { return pSource.isEnemy(pTarget); }
            catch { return true; }
        }

        private static bool IsLiveRealm(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   !pKingdom.isNeutral() && pKingdom.isCiv();
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private sealed class OperationRow
        {
            public long OperationId;
            public long SourceKingdomId;
            public long TargetKingdomId;
            public DiplomaticOperationType Type;
            public long TargetCityId;
            public string ProjectType = "";
            public bool StrongForgery;
            public int StartYear;
            public int DueYear;
            public int NetworkStrength;
            public int SuccessChance;
            public int DiscoveryChance;
            public bool PlayerInitiated;
        }
    }
}
