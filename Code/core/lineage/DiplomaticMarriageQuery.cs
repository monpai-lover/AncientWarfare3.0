using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public sealed class DiplomaticMarriageQuery
    {
        private const string CandidateSql =
            "SELECT ID FROM ActorArchive WHERE LINEAGE_ID=@lineage AND IS_ALIVE=1 " +
            "AND NOT EXISTS (SELECT 1 FROM DiplomaticMarriage marriage " +
            "WHERE marriage.STATUS=0 AND marriage.END_TIME<0 AND " +
            "marriage.ACTOR_A_ID=ActorArchive.ID) " +
            "AND NOT EXISTS (SELECT 1 FROM DiplomaticMarriage marriage " +
            "WHERE marriage.STATUS=0 AND marriage.END_TIME<0 AND " +
            "marriage.ACTOR_B_ID=ActorArchive.ID) " +
            "ORDER BY BIRTH_TIME,ID LIMIT @limit";
        private const string RealmCandidateSql =
            "SELECT ID FROM ActorArchive WHERE LINEAGE_ID=@lineage " +
            "AND KINGDOM_ID=@kingdom AND IS_ALIVE=1 " +
            "AND NOT EXISTS (SELECT 1 FROM DiplomaticMarriage marriage " +
            "WHERE marriage.STATUS=0 AND marriage.END_TIME<0 AND " +
            "marriage.ACTOR_A_ID=ActorArchive.ID) " +
            "AND NOT EXISTS (SELECT 1 FROM DiplomaticMarriage marriage " +
            "WHERE marriage.STATUS=0 AND marriage.END_TIME<0 AND " +
            "marriage.ACTOR_B_ID=ActorArchive.ID) " +
            "ORDER BY BIRTH_TIME,ID LIMIT @limit";

        private readonly SQLiteConnection _db;

        public DiplomaticMarriageQuery(SQLiteConnection pDb)
        {
            _db = pDb ?? throw new ArgumentNullException(nameof(pDb));
        }

        public IReadOnlyList<long> ReadCandidateIds(long pLineageId,
            int pRequestedLimit)
        {
            int limit = Math.Min(
                DiplomacyActionExpansionRules
                    .MaximumRoyalArchiveIdsScannedPerRealm,
                Math.Max(0, pRequestedLimit));
            var result = new List<long>(limit);
            if (pLineageId < 0 || limit == 0) return result;
            using SQLiteCommand command = BuildCommand(CandidateSql,
                pLineageId, limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(Convert.ToInt64(reader.GetValue(0)));
            return result;
        }

        public IReadOnlyList<long> ReadCandidateIds(long pLineageId,
            long pKingdomId, int pRequestedLimit)
        {
            int limit = Math.Min(
                DiplomacyActionExpansionRules
                    .MaximumRoyalArchiveIdsScannedPerRealm,
                Math.Max(0, pRequestedLimit));
            var result = new List<long>(limit);
            if (pLineageId < 0 || pKingdomId < 0 || limit == 0)
                return result;
            using SQLiteCommand command = BuildRealmCommand(
                RealmCandidateSql, pLineageId, pKingdomId, limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(Convert.ToInt64(reader.GetValue(0)));
            return result;
        }

        public string ExplainCandidatePlan(long pLineageId,
            int pRequestedLimit)
        {
            int limit = Math.Min(
                DiplomacyActionExpansionRules
                    .MaximumRoyalArchiveIdsScannedPerRealm,
                Math.Max(1, pRequestedLimit));
            using SQLiteCommand command = BuildCommand(
                "EXPLAIN QUERY PLAN " + CandidateSql, pLineageId, limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read())
                result.Add(Convert.ToString(reader.GetValue(3)) ?? "");
            return string.Join("\n", result.ToArray());
        }

        public string ExplainCandidatePlan(long pLineageId,
            long pKingdomId, int pRequestedLimit)
        {
            int limit = Math.Min(
                DiplomacyActionExpansionRules
                    .MaximumRoyalArchiveIdsScannedPerRealm,
                Math.Max(1, pRequestedLimit));
            using SQLiteCommand command = BuildRealmCommand(
                "EXPLAIN QUERY PLAN " + RealmCandidateSql,
                pLineageId, pKingdomId, limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read())
                result.Add(Convert.ToString(reader.GetValue(3)) ?? "");
            return string.Join("\n", result.ToArray());
        }

        private SQLiteCommand BuildCommand(string pSql, long pLineageId,
            int pLimit)
        {
            var command = new SQLiteCommand(pSql, _db);
            command.Parameters.AddWithValue("@lineage", pLineageId);
            command.Parameters.AddWithValue("@limit", pLimit);
            return command;
        }

        private SQLiteCommand BuildRealmCommand(string pSql, long pLineageId,
            long pKingdomId, int pLimit)
        {
            SQLiteCommand command = BuildCommand(pSql, pLineageId, pLimit);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            return command;
        }
    }
}
