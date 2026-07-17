using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    public sealed class RulerTitleDecision
    {
        public RulerTitleFacts Facts;
        public string PosthumousName = "";
        public string TempleName = "";
        public string DisplayTitle = "";
        public string PosthumousQualificationKey = "";
        public string TempleQualificationKey = "";
        public int PosthumousCycleNo;
        public int TempleCycleNo;
        public string TitleKind = "posthumous";
        public string TitleSuffix = "";
        public string Evaluation = "";
        public string Grade = "";
        public string DominantDimension = "";
        public int CivilScore;
        public int TerritoryScore;
        public int WarScore;
        public int OrderScore;
        public int EndingScore;
        public int TotalScore;
        public string Reason = "";
        public string HistoryPlain = "";
        public string HistoryRich = "";
        public string YearPrefix = "";
        public string YearPrefixRich = "";
        public string BiographyCategory = ChronicleCategory.HONOR;
        public string BiographyRole = "former_king";
        public string BiographyRoleLabel = "";
        public int AgeAtEvent = -1;
        public bool IsRetrospective;
        public string RetrospectiveRelation = "";

        public static RulerTitleDecision ForPosthumous(RulerTitleFacts pFacts,
            PosthumousTitleDecision pPosthumous, string pTitleKind = "posthumous")
        {
            string grade = pPosthumous.GradeKey ?? "";
            string evaluation = grade.StartsWith("praise", StringComparison.Ordinal)
                ? "good"
                : grade.StartsWith("blame", StringComparison.Ordinal) ? "bad" : "neutral";
            int highestTitle = pFacts?.HighestTitle ?? 0;
            string suffix = highestTitle switch
            {
                0 => "伯",
                1 => "侯",
                2 => "公",
                3 => "王",
                4 => "帝",
                _ => "君"
            };
            return new RulerTitleDecision
            {
                Facts = pFacts,
                PosthumousName = pPosthumous.Name,
                DisplayTitle = PosthumousTitleRules.BuildRankedAppellation(
                    pFacts?.StateName, pPosthumous.Name, highestTitle),
                PosthumousQualificationKey = pPosthumous.QualificationKey,
                PosthumousCycleNo = pPosthumous.CycleNo,
                TitleKind = string.IsNullOrWhiteSpace(pTitleKind)
                    ? "posthumous"
                    : pTitleKind.Trim(),
                TitleSuffix = suffix,
                Evaluation = evaluation,
                Grade = grade,
                DominantDimension = pPosthumous.DominantKey,
                CivilScore = pPosthumous.Civil,
                TerritoryScore = pPosthumous.Territory,
                WarScore = pPosthumous.War,
                OrderScore = pPosthumous.Order,
                EndingScore = pPosthumous.Ending,
                TotalScore = pPosthumous.Total,
                Reason = pPosthumous.Reason,
                AgeAtEvent = pFacts?.Age ?? -1
            };
        }
    }

    public readonly struct RulerTitleCommitResult
    {
        public readonly bool Success;
        public readonly long RecordId;
        public readonly string PosthumousName;
        public readonly string TempleName;
        public readonly string DisplayTitle;

        public RulerTitleCommitResult(bool pSuccess, long pRecordId,
            string pPosthumousName, string pTempleName, string pDisplayTitle)
        {
            Success = pSuccess;
            RecordId = pRecordId;
            PosthumousName = pPosthumousName ?? "";
            TempleName = pTempleName ?? "";
            DisplayTitle = pDisplayTitle ?? "";
        }

        public static RulerTitleCommitResult Failed =>
            new RulerTitleCommitResult(false, -1, "", "", "");
    }

    internal static class RulerTitleCommitService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static RulerTitleCommitResult Commit(RulerTitleDecision pDecision)
        {
            RulerTitleFacts facts = pDecision?.Facts;
            if (!Ready || facts == null || facts.ActorId < 0 || facts.ShiId < 0 ||
                string.IsNullOrWhiteSpace(pDecision.PosthumousName) ||
                string.IsNullOrWhiteSpace(pDecision.DisplayTitle))
                return RulerTitleCommitResult.Failed;

            if (facts.ReignId >= 0 && TryReadExisting(facts.ReignId, out RulerTitleCommitResult existing))
                return existing;

            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                double time = LineageService.CurTime();
                if (!DynastyTitleRegistryService.TryReserve(DB, transaction, facts.ShiId,
                        "posthumous", pDecision.PosthumousName,
                        pDecision.PosthumousCycleNo, facts.ActorId, facts.ReignId, time))
                    return RulerTitleCommitResult.Failed;
                if (!string.IsNullOrWhiteSpace(pDecision.TempleName) &&
                    !DynastyTitleRegistryService.TryReserve(DB, transaction, facts.ShiId,
                        "temple", pDecision.TempleName, pDecision.TempleCycleNo,
                        facts.ActorId, facts.ReignId, time))
                    return RulerTitleCommitResult.Failed;

                long recordId = NextId(transaction,
                    PosthumousTitleTableItem.GetTableName(), "RECORD_ID");
                InsertTitle(transaction, recordId, pDecision, time);
                if (facts.ReignId >= 0)
                    UpdateReign(transaction, facts.ReignId, pDecision.DisplayTitle,
                        facts.KingdomColor, facts.HighestTitle);
                if (!string.IsNullOrEmpty(pDecision.HistoryPlain))
                    InsertHistory(transaction, pDecision, time);
                transaction.Commit();
                return new RulerTitleCommitResult(true, recordId,
                    pDecision.PosthumousName, pDecision.TempleName,
                    pDecision.DisplayTitle);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Ruler title transaction failed: " + error.Message);
                return RulerTitleCommitResult.Failed;
            }
        }

        private static void InsertTitle(SQLiteTransaction pTransaction, long pRecordId,
            RulerTitleDecision pDecision, double pTime)
        {
            RulerTitleFacts facts = pDecision.Facts;
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + PosthumousTitleTableItem.GetTableName() +
                " (RECORD_ID,ACTOR_ID,KINGDOM_ID,REIGN_ID,SHI_ID,DYNASTY_ID," +
                "MANDATE_PERIOD_ID,HIGHEST_TITLE,KING_NAME,KING_COLOR,TITLE_CHAR," +
                "TITLE_SUFFIX,FULL_TITLE,FULL_TITLE_COLOR,TITLE_KIND,TEMPLE_NAME," +
                "POSTHUMOUS_NAME,IS_MANDATE,IS_RETROSPECTIVE,RETROSPECTIVE_RELATION," +
                "QUALIFICATION_KEY,FACT_SNAPSHOT,EVAL,SCORE_DETAIL,GRADE," +
                "DOMINANT_DIMENSION,SCORE_CIVIL,SCORE_TERRITORY,SCORE_WAR," +
                "SCORE_ORDER,SCORE_ENDING,TOTAL_SCORE,REASON_TEXT,DECIDED_TIME)" +
                " VALUES (@record,@actor,@kingdom,@reign,@shi,@dynasty,@mandate," +
                "@highest,@kingName,@color,@posthumous,@suffix,@display,@color," +
                "@kind,@temple,@posthumous,@isMandate,@retrospective,@relation," +
                "@qualification,@facts,@eval,@scoreDetail,@grade,@dominant,@civil," +
                "@territory,@war,@order,@ending,@total,@reason,@time)";
            command.Parameters.AddWithValue("@record", pRecordId);
            command.Parameters.AddWithValue("@actor", facts.ActorId);
            command.Parameters.AddWithValue("@kingdom", facts.KingdomId);
            command.Parameters.AddWithValue("@reign", facts.ReignId);
            command.Parameters.AddWithValue("@shi", facts.ShiId);
            command.Parameters.AddWithValue("@dynasty", facts.DynastyId);
            command.Parameters.AddWithValue("@mandate", facts.MandatePeriodId);
            command.Parameters.AddWithValue("@highest", facts.HighestTitle);
            command.Parameters.AddWithValue("@kingName", facts.ActorName ?? "");
            command.Parameters.AddWithValue("@color", facts.KingdomColor ?? "");
            command.Parameters.AddWithValue("@posthumous", pDecision.PosthumousName ?? "");
            command.Parameters.AddWithValue("@suffix", pDecision.TitleSuffix ?? "");
            command.Parameters.AddWithValue("@display", pDecision.DisplayTitle ?? "");
            command.Parameters.AddWithValue("@kind", pDecision.TitleKind ?? "posthumous");
            command.Parameters.AddWithValue("@temple", pDecision.TempleName ?? "");
            command.Parameters.AddWithValue("@isMandate", facts.IsMandate ? 1 : 0);
            command.Parameters.AddWithValue("@retrospective", pDecision.IsRetrospective ? 1 : 0);
            command.Parameters.AddWithValue("@relation", pDecision.RetrospectiveRelation ?? "");
            command.Parameters.AddWithValue("@qualification",
                pDecision.PosthumousQualificationKey ?? "");
            command.Parameters.AddWithValue("@facts", JsonConvert.SerializeObject(facts));
            command.Parameters.AddWithValue("@eval", pDecision.Evaluation ?? "");
            command.Parameters.AddWithValue("@scoreDetail", pDecision.Reason ?? "");
            command.Parameters.AddWithValue("@grade", pDecision.Grade ?? "");
            command.Parameters.AddWithValue("@dominant", pDecision.DominantDimension ?? "");
            command.Parameters.AddWithValue("@civil", pDecision.CivilScore);
            command.Parameters.AddWithValue("@territory", pDecision.TerritoryScore);
            command.Parameters.AddWithValue("@war", pDecision.WarScore);
            command.Parameters.AddWithValue("@order", pDecision.OrderScore);
            command.Parameters.AddWithValue("@ending", pDecision.EndingScore);
            command.Parameters.AddWithValue("@total", pDecision.TotalScore);
            command.Parameters.AddWithValue("@reason", pDecision.Reason ?? "");
            command.Parameters.AddWithValue("@time", pTime);
            command.ExecuteNonQuery();
        }

        private static void UpdateReign(SQLiteTransaction pTransaction, long pReignId,
            string pDisplayTitle, string pColor, int pHighestTitle)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + KingdomReignTableItem.GetTableName() +
                                  " SET POSTHUMOUS_TITLE=@title,POSTHUMOUS_COLOR=@color," +
                                  "HIGHEST_TITLE=CASE WHEN HIGHEST_TITLE<@highest " +
                                  "THEN @highest ELSE HIGHEST_TITLE END WHERE REIGN_ID=@reign";
            command.Parameters.AddWithValue("@title", pDisplayTitle ?? "");
            command.Parameters.AddWithValue("@color", pColor ?? "");
            command.Parameters.AddWithValue("@highest", pHighestTitle);
            command.Parameters.AddWithValue("@reign", pReignId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The title reign row is missing.");
        }

        private static void InsertHistory(SQLiteTransaction pTransaction,
            RulerTitleDecision pDecision, double pTime)
        {
            RulerTitleFacts facts = pDecision.Facts;
            long kingdomEventId = NextId(pTransaction,
                KingdomHistoryTableItem.GetTableName(), "EVENT_ID");
            using (var kingdom = new SQLiteCommand(DB) { Transaction = pTransaction })
            {
                kingdom.CommandText = "INSERT INTO " + KingdomHistoryTableItem.GetTableName() +
                    " (EVENT_ID,KINGDOM_ID,WORLD_TIME,YEAR_PREFIX,YEAR_PREFIX_RICH," +
                    "SUBJECT_NAME,SUBJECT_COLOR,CONTENT,CONTENT_RICH,EVENT_TYPE," +
                    "CONTEXT_KINGDOM_ID,CONTEXT_KINGDOM_NAME,CONTEXT_KINGDOM_COLOR," +
                    "TARGET_TYPE,TARGET_ID) VALUES (@id,@kingdom,@time,@year,@yearRich," +
                    "@subject,@color,@content,@rich,@type,@kingdom,@subject,@color,'actor',@actor)";
                AddHistoryParameters(kingdom, kingdomEventId, pDecision, pTime);
                kingdom.ExecuteNonQuery();
            }

            long personEventId = NextId(pTransaction,
                PersonBiographyTableItem.GetTableName(), "EVENT_ID");
            using var person = new SQLiteCommand(DB) { Transaction = pTransaction };
            person.CommandText = "INSERT INTO " + PersonBiographyTableItem.GetTableName() +
                " (EVENT_ID,ACTOR_ID,WORLD_TIME,YEAR_PREFIX,YEAR_PREFIX_RICH,SUBJECT_NAME," +
                "SUBJECT_COLOR,CONTENT,CONTENT_RICH,EVENT_TYPE,CATEGORY,AGE_AT_EVENT," +
                "IS_KING_AT_EVENT,ROLE_SNAPSHOT,ROLE_LABEL,CONTEXT_KINGDOM_ID," +
                "CONTEXT_KINGDOM_NAME,CONTEXT_KINGDOM_COLOR,TARGET_TYPE,TARGET_ID)" +
                " VALUES (@id,@actor,@time,@year,@yearRich,@actorName,@color,@content," +
                "@rich,@type,@category,@age,0,@role,@roleLabel,@kingdom,@subject,@color," +
                "'kingdom',@kingdom)";
            person.Parameters.AddWithValue("@id", personEventId);
            person.Parameters.AddWithValue("@actorName", facts.ActorName ?? "");
            person.Parameters.AddWithValue("@category", pDecision.BiographyCategory ?? "");
            person.Parameters.AddWithValue("@age", pDecision.AgeAtEvent);
            person.Parameters.AddWithValue("@role", pDecision.BiographyRole ?? "");
            person.Parameters.AddWithValue("@roleLabel", pDecision.BiographyRoleLabel ?? "");
            AddSharedHistoryParameters(person, pDecision, pTime);
            person.ExecuteNonQuery();
        }

        private static void AddHistoryParameters(SQLiteCommand pCommand, long pEventId,
            RulerTitleDecision pDecision, double pTime)
        {
            pCommand.Parameters.AddWithValue("@id", pEventId);
            AddSharedHistoryParameters(pCommand, pDecision, pTime);
        }

        private static void AddSharedHistoryParameters(SQLiteCommand pCommand,
            RulerTitleDecision pDecision, double pTime)
        {
            RulerTitleFacts facts = pDecision.Facts;
            pCommand.Parameters.AddWithValue("@kingdom", facts.KingdomId);
            pCommand.Parameters.AddWithValue("@actor", facts.ActorId);
            pCommand.Parameters.AddWithValue("@time", pTime);
            pCommand.Parameters.AddWithValue("@year", pDecision.YearPrefix ?? "");
            pCommand.Parameters.AddWithValue("@yearRich", pDecision.YearPrefixRich ?? "");
            pCommand.Parameters.AddWithValue("@subject", facts.StateName ?? "");
            pCommand.Parameters.AddWithValue("@color", facts.KingdomColor ?? "");
            pCommand.Parameters.AddWithValue("@content", pDecision.HistoryPlain ?? "");
            pCommand.Parameters.AddWithValue("@rich", string.IsNullOrEmpty(pDecision.HistoryRich)
                ? pDecision.HistoryPlain ?? ""
                : pDecision.HistoryRich);
            pCommand.Parameters.AddWithValue("@type", KingdomEvent.POSTHUMOUS);
        }

        private static long NextId(SQLiteTransaction pTransaction, string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn + "),-1)+1 FROM " + pTable;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static bool TryReadExisting(long pReignId,
            out RulerTitleCommitResult pResult)
        {
            pResult = RulerTitleCommitResult.Failed;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT RECORD_ID,POSTHUMOUS_NAME,TEMPLE_NAME,FULL_TITLE " +
                                      "FROM " + PosthumousTitleTableItem.GetTableName() +
                                      " WHERE REIGN_ID=@reign LIMIT 1";
                command.Parameters.AddWithValue("@reign", pReignId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pResult = new RulerTitleCommitResult(true,
                    reader.GetInt64(0), ValueString(reader, 1),
                    ValueString(reader, 2), ValueString(reader, 3));
                return true;
            }
            catch { return false; }
        }

        private static string ValueString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }
    }
}
