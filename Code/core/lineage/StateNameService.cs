using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    public readonly struct StateNameCommitResult
    {
        public readonly bool Success;
        public readonly bool AlreadyBound;
        public readonly long ShiId;
        public readonly long DynastyId;
        public readonly string StateName;

        public StateNameCommitResult(bool pSuccess, bool pAlreadyBound,
            long pShiId, long pDynastyId, string pStateName)
        {
            Success = pSuccess;
            AlreadyBound = pAlreadyBound;
            ShiId = pShiId;
            DynastyId = pDynastyId;
            StateName = pStateName ?? "";
        }

        public static StateNameCommitResult Failed =>
            new StateNameCommitResult(false, false, -1, -1, "");
    }

    internal static class StateNameService
    {
        private readonly struct BranchSeed
        {
            public readonly string StateName;
            public readonly long FounderActorId;
            public readonly long OriginKingdomId;

            public BranchSeed(string pStateName, long pFounderActorId,
                long pOriginKingdomId)
            {
                StateName = pStateName ?? "";
                FounderActorId = pFounderActorId;
                OriginKingdomId = pOriginKingdomId;
            }
        }

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static StateNameCommitResult EnsureBoundStateName(Kingdom pKingdom,
            Actor pFounder, long pShiId, long pDynastyId, long pOriginKingdomId,
            string pPreferredStateName = "")
        {
            if (!Ready || pKingdom?.data == null || pShiId < 0)
                return StateNameCommitResult.Failed;
            if (!TryReadBranchSeed(pShiId, out BranchSeed seed))
                return StateNameCommitResult.Failed;
            string resolved = StateNameRules.ResolveInitialBoundName(
                seed.StateName, pPreferredStateName, pKingdom.name);
            bool hasPreferred = StateNameRules.IsValid(pPreferredStateName);
            bool hasCurrentKingdomName = StateNameRules.IsValid(pKingdom.name);
            if (StateNameRules.IsValid(seed.StateName) &&
                (!hasPreferred || string.Equals(seed.StateName, resolved,
                    StringComparison.Ordinal)))
            {
                return new StateNameCommitResult(true, true,
                    pShiId, pDynastyId, seed.StateName);
            }

            long founderActorId = seed.FounderActorId >= 0
                ? seed.FounderActorId
                : pFounder?.data?.id ?? -1L;
            long originKingdomId = seed.OriginKingdomId >= 0
                ? seed.OriginKingdomId
                : pOriginKingdomId;
            string stateName;
            if (StateNameRules.IsValid(resolved))
            {
                stateName = resolved;
            }
            else
            {
                HashSet<string> activeNames = ReadActiveStateNames(pShiId);
                stateName = StateNameRules.SelectFirstAvailable(
                    XiaPreQinKingdomNameRules.All(), activeNames,
                    pShiId, founderActorId, originKingdomId);
            }
            if (!StateNameRules.IsValid(stateName)) return StateNameCommitResult.Failed;

            double now = LineageService.CurTime();
            string[] historyTables = pFounder?.data?.id >= 0
                ? new[]
                {
                    KingdomHistoryTableItem.GetTableName(),
                    PersonBiographyTableItem.GetTableName()
                }
                : new[] { KingdomHistoryTableItem.GetTableName() };
            if (HistoricalSynchronousWriteCoordinator.TryExecute(
                    TimeSpan.FromSeconds(5), historyTables,
                    HistoricalWriteService.FlushForSynchronousFallback,
                    HistoricalWriteService.TryReserveEventId,
                    eventIds => CommitReserved(pKingdom, pFounder, pShiId,
                        pDynastyId, stateName, hasPreferred,
                        hasCurrentKingdomName, now, eventIds[0],
                        eventIds.Count > 1 ? eventIds[1] : -1L),
                    out StateNameCommitResult result, out string error))
                return result;
            ModClass.LogWarning("State-name binding transaction failed: " +
                                error);
            return StateNameCommitResult.Failed;
        }

        private static StateNameCommitResult CommitReserved(Kingdom pKingdom,
            Actor pFounder, long pShiId, long pDynastyId, string pStateName,
            bool pHasPreferred, bool pHasCurrentKingdomName, double pNow,
            long pKingdomEventId, long pPersonEventId)
        {
            using SQLiteTransaction transaction = DB.BeginTransaction();
            using (var updateShi = new SQLiteCommand(DB)
                   {
                       Transaction = transaction
                   })
            {
                updateShi.CommandText = "UPDATE " +
                    ShiBranchTableItem.GetTableName() +
                    " SET STATE_NAME=@name,STATE_NAME_SOURCE=@source," +
                    "STATE_NAME_DECIDED_TIME=@time WHERE SHI_ID=@shi" +
                    (pHasPreferred
                        ? ""
                        : " AND IFNULL(STATE_NAME,'')=''");
                updateShi.Parameters.AddWithValue("@name", pStateName);
                updateShi.Parameters.AddWithValue("@source",
                    pHasPreferred
                        ? "historical_figure"
                        : pHasCurrentKingdomName
                            ? "existing_kingdom"
                            : "random");
                updateShi.Parameters.AddWithValue("@time", pNow);
                updateShi.Parameters.AddWithValue("@shi", pShiId);
                if (updateShi.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    string committed = ReadBoundName(pShiId);
                    return StateNameRules.IsValid(committed)
                        ? new StateNameCommitResult(true, true,
                            pShiId, pDynastyId, committed)
                        : StateNameCommitResult.Failed;
                }
            }

            if (pDynastyId >= 0)
            {
                using var updateDynasty = new SQLiteCommand(DB)
                {
                    Transaction = transaction
                };
                updateDynasty.CommandText = "UPDATE " +
                    DynastyPeriodTableItem.GetTableName() +
                    " SET STATE_NAME=@name WHERE DYNASTY_ID=@dynasty " +
                    "AND SHI_ID=@shi AND END_TIME=-1";
                updateDynasty.Parameters.AddWithValue("@name", pStateName);
                updateDynasty.Parameters.AddWithValue("@dynasty", pDynastyId);
                updateDynasty.Parameters.AddWithValue("@shi", pShiId);
                updateDynasty.ExecuteNonQuery();
            }

            InsertHistory(transaction, pKingdom, pFounder, pShiId,
                pStateName, pNow, pKingdomEventId, pPersonEventId);
            HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite(
                transaction.Commit);
            return new StateNameCommitResult(true, false,
                pShiId, pDynastyId, pStateName);
        }

        public static string GetBoundOrCurrentName(Kingdom pKingdom, long pShiId = -1)
        {
            long shiId = pShiId;
            if (shiId < 0 && pKingdom?.king?.data != null)
                pKingdom.king.data.get(LineageKeys.SHI_ID, out shiId, -1L);
            string bound = ReadBoundName(shiId);
            if (StateNameRules.IsValid(bound)) return bound;
            string current = pKingdom?.name ?? "";
            return StateNameRules.IsValid(current) ? current.Trim() : "";
        }

        public static string GetBoundStateName(long pShiId)
        {
            string bound = ReadBoundName(pShiId);
            return StateNameRules.IsValid(bound) ? bound.Trim() : "";
        }

        public static bool ProjectCommittedStateName(Kingdom pKingdom,
            StateNameCommitResult pCommitted)
        {
            if (!pCommitted.Success || pKingdom?.data == null ||
                !StateNameRules.IsValid(pCommitted.StateName)) return false;
            try
            {
                ApplyCommittedProjection(pKingdom, pCommitted.StateName);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("State-name projection failed, retrying committed value: " +
                                    error.Message);
                return RetryCommittedProjection(pKingdom, pCommitted.ShiId);
            }
        }

        public static bool ProjectExistingStateName(Kingdom pKingdom,
            long pShiId, string pStateName)
        {
            if (pKingdom?.data == null || pShiId < 0 ||
                !StateNameRules.IsValid(pStateName)) return false;
            string committed = ReadBoundName(pShiId);
            if (!string.Equals(committed, pStateName,
                    StringComparison.Ordinal)) return false;
            return ProjectCommittedStateName(pKingdom,
                new StateNameCommitResult(true, true, pShiId, -1L,
                    committed));
        }

        private static bool RetryCommittedProjection(Kingdom pKingdom, long pShiId)
        {
            string committed = ReadBoundName(pShiId);
            if (pKingdom?.data == null || !StateNameRules.IsValid(committed)) return false;
            try
            {
                ApplyCommittedProjection(pKingdom, committed.Trim());
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("State-name projection retry failed: " + error.Message);
                return false;
            }
        }

        private static void ApplyCommittedProjection(Kingdom pKingdom, string pStateName)
        {
            bool changed = !string.Equals(pKingdom.data.name, pStateName,
                StringComparison.Ordinal);
            if (changed)
                pKingdom.setName(pStateName, pTrack: false);
            if (!changed) KingdomRenameProjectionService.Refresh(pKingdom);
        }

        private static bool TryReadBranchSeed(long pShiId, out BranchSeed pSeed)
        {
            pSeed = default;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(STATE_NAME,'')," +
                                      "IFNULL(FOUNDER_ACTOR_ID,-1)," +
                                      "IFNULL(ORIGIN_KINGDOM_ID,-1) FROM " +
                                      ShiBranchTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi LIMIT 1";
                command.Parameters.AddWithValue("@shi", pShiId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pSeed = new BranchSeed(ValueString(reader, 0),
                    ValueLong(reader, 1, -1), ValueLong(reader, 2, -1));
                return true;
            }
            catch { return false; }
        }

        private static string ReadBoundName(long pShiId)
        {
            if (!Ready || pShiId < 0) return "";
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(STATE_NAME,'') FROM " +
                                      ShiBranchTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi LIMIT 1";
                command.Parameters.AddWithValue("@shi", pShiId);
                return Convert.ToString(command.ExecuteScalar()) ?? "";
            }
            catch { return ""; }
        }

        private static HashSet<string> ReadActiveStateNames(long pExcludedShiId)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT DISTINCT branch.STATE_NAME FROM " +
                                      ShiBranchTableItem.GetTableName() + " branch JOIN " +
                                      DynastyPeriodTableItem.GetTableName() +
                                      " dynasty ON dynasty.SHI_ID=branch.SHI_ID " +
                                      "WHERE dynasty.END_TIME=-1 AND branch.SHI_ID<>@shi " +
                                      "AND IFNULL(branch.STATE_NAME,'')<>'' UNION " +
                                      "SELECT archive.KINGDOM_NAME FROM " +
                                      KingdomArchiveTableItem.GetTableName() + " archive " +
                                      "WHERE archive.IS_ALIVE=1 " +
                                      "AND IFNULL(archive.KINGDOM_NAME,'')<>''";
                command.Parameters.AddWithValue("@shi", pExcludedShiId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string value = ValueString(reader, 0);
                    if (StateNameRules.IsValid(value)) result.Add(value);
                }
            }
            catch { }
            return result;
        }

        private static void InsertHistory(SQLiteTransaction pTransaction,
            Kingdom pKingdom, Actor pFounder, long pShiId, string pStateName,
            double pTime, long pKingdomEventId, long pPersonEventId)
        {
            string color = HistoryColors.FromKingdom(pKingdom);
            string founderName = pFounder?.getName() ?? "";
            long founderId = pFounder?.data?.id ?? -1L;
            string subject = string.IsNullOrEmpty(founderName) ? "氏支" : founderName;
            string content = string.Format(
                HistoryLocalizationRules.Text("aw_hist_title_state_name"),
                subject, pStateName);
            string template = HistoryLocalizationRules.Text(
                "aw_hist_title_state_name");
            HistoryText founderText = string.IsNullOrEmpty(founderName)
                ? HistoryText.PlainText(subject)
                : HistoryText.Actor(pFounder, founderName);
            string rich = string.Format(template, founderText.Rich,
                HistoryText.Colored(pStateName, color).Rich);
            HistoryText richContent = new HistoryText(content, rich,
                founderText.TargetType, founderText.TargetId);
            string year = HistoryWriter.BuildYearPrefix(pTime, pKingdom);
            string yearRich = HistoryWriter.BuildYearPrefixRich(pTime, pKingdom);

            using (var kingdom = new SQLiteCommand(DB) { Transaction = pTransaction })
            {
                kingdom.CommandText = "INSERT INTO " + KingdomHistoryTableItem.GetTableName() +
                    " (EVENT_ID,KINGDOM_ID,WORLD_TIME,YEAR_PREFIX,YEAR_PREFIX_RICH," +
                    "SUBJECT_NAME,SUBJECT_COLOR,CONTENT,CONTENT_RICH,EVENT_TYPE," +
                    "CONTEXT_KINGDOM_ID,CONTEXT_KINGDOM_NAME,CONTEXT_KINGDOM_COLOR," +
                    "TARGET_TYPE,TARGET_ID) VALUES (@id,@kingdom,@time,@year,@yearRich," +
                    "@state,@color,@content,@rich,@type,@kingdom,@state,@color,'actor',@actor)";
                AddHistoryParameters(kingdom, pKingdomEventId, pKingdom.id, founderId,
                    pTime, year, yearRich, pStateName, color,
                    content, richContent.Rich);
                kingdom.ExecuteNonQuery();
            }

            if (founderId < 0) return;
            using var person = new SQLiteCommand(DB) { Transaction = pTransaction };
            person.CommandText = "INSERT INTO " + PersonBiographyTableItem.GetTableName() +
                " (EVENT_ID,ACTOR_ID,WORLD_TIME,YEAR_PREFIX,YEAR_PREFIX_RICH,SUBJECT_NAME," +
                "SUBJECT_COLOR,CONTENT,CONTENT_RICH,EVENT_TYPE,CATEGORY,AGE_AT_EVENT," +
                "IS_KING_AT_EVENT,ROLE_SNAPSHOT,ROLE_LABEL,CONTEXT_KINGDOM_ID," +
                "CONTEXT_KINGDOM_NAME,CONTEXT_KINGDOM_COLOR,TARGET_TYPE,TARGET_ID)" +
                " VALUES (@id,@actor,@time,@year,@yearRich,@actorName,@color,@content," +
                "@rich,@type,@category,@age,1,'king','',@kingdom,@state,@color," +
                "'kingdom',@kingdom)";
            person.Parameters.AddWithValue("@actorName", founderName);
            person.Parameters.AddWithValue("@category", ChronicleCategory.HONOR);
            person.Parameters.AddWithValue("@age", SafeAge(pFounder));
            AddHistoryParameters(person, pPersonEventId, pKingdom.id, founderId,
                pTime, year, yearRich, pStateName, color,
                content, richContent.Rich);
            person.ExecuteNonQuery();
        }

        private static void AddHistoryParameters(SQLiteCommand pCommand, long pEventId,
            long pKingdomId, long pActorId, double pTime, string pYear,
            string pYearRich, string pStateName, string pColor,
            string pContent, string pRich)
        {
            pCommand.Parameters.AddWithValue("@id", pEventId);
            pCommand.Parameters.AddWithValue("@kingdom", pKingdomId);
            pCommand.Parameters.AddWithValue("@actor", pActorId);
            pCommand.Parameters.AddWithValue("@time", pTime);
            pCommand.Parameters.AddWithValue("@year", pYear ?? "");
            pCommand.Parameters.AddWithValue("@yearRich", pYearRich ?? "");
            pCommand.Parameters.AddWithValue("@state", pStateName ?? "");
            pCommand.Parameters.AddWithValue("@color", pColor ?? "");
            pCommand.Parameters.AddWithValue("@content", pContent ?? "");
            pCommand.Parameters.AddWithValue("@rich", pRich ?? pContent ?? "");
            pCommand.Parameters.AddWithValue("@type", "state_name_bound");
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor?.data == null ? -1 : Math.Max(0, pActor.getAge()); }
            catch { return -1; }
        }

        private static string ValueString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static long ValueLong(SQLiteDataReader pReader, int pIndex, long pDefault)
        {
            return pReader.IsDBNull(pIndex) ? pDefault : Convert.ToInt64(pReader.GetValue(pIndex));
        }
    }
}
