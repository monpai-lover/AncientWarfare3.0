using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal static class TributarySettlementPersistence
    {
        private const string TableName = "VassalRelation";

        internal static void InitializeNewRelation(SQLiteConnection db,
            long relationId, int currentYear)
        {
            using var command = new SQLiteCommand(db);
            command.CommandText =
                "UPDATE " + TableName + " SET " +
                "LAST_TRIBUTE_ATTEMPT_YEAR=-1," +
                "LAST_TRIBUTE_PAID_YEAR=-1," +
                "NEXT_TRIBUTE_DUE_YEAR=@next," +
                "LAST_TRIBUTE_FACTOR_PERCENT=-1 " +
                "WHERE RELATION_ID=@relation AND ACTIVE=1 AND END_TIME<0 " +
                "AND CONTRACT_TIER=@tier";
            command.Parameters.AddWithValue("@next", currentYear + 1);
            command.Parameters.AddWithValue("@relation", relationId);
            command.Parameters.AddWithValue("@tier",
                VassalContractTierRules.Tributary);
            RequireExactlyOne(command.ExecuteNonQuery(),
                "initialize", relationId);
        }

        internal static bool TryBeginAttempt(SQLiteConnection db,
            long relationId, int currentYear)
        {
            using var command = new SQLiteCommand(db);
            command.CommandText =
                "UPDATE " + TableName + " SET " +
                "LAST_TRIBUTE_ATTEMPT_YEAR=@year," +
                "NEXT_TRIBUTE_DUE_YEAR=CASE " +
                "WHEN NEXT_TRIBUTE_DUE_YEAR<0 THEN @year " +
                "ELSE NEXT_TRIBUTE_DUE_YEAR END " +
                "WHERE RELATION_ID=@relation AND ACTIVE=1 AND END_TIME<0 " +
                "AND CONTRACT_TIER=@tier " +
                "AND LAST_TRIBUTE_ATTEMPT_YEAR<>@year " +
                "AND (NEXT_TRIBUTE_DUE_YEAR<0 OR " +
                "NEXT_TRIBUTE_DUE_YEAR<=@year)";
            command.Parameters.AddWithValue("@year", currentYear);
            command.Parameters.AddWithValue("@relation", relationId);
            command.Parameters.AddWithValue("@tier",
                VassalContractTierRules.Tributary);
            return command.ExecuteNonQuery() == 1;
        }

        internal static void MarkPaid(SQLiteConnection db,
            long relationId, int currentYear, int factorPercent)
        {
            using var command = new SQLiteCommand(db);
            command.CommandText =
                "UPDATE " + TableName + " SET " +
                "LAST_TRIBUTE_PAID_YEAR=@year," +
                "NEXT_TRIBUTE_DUE_YEAR=@next," +
                "LAST_TRIBUTE_FACTOR_PERCENT=@factor " +
                "WHERE RELATION_ID=@relation AND ACTIVE=1 AND END_TIME<0 " +
                "AND CONTRACT_TIER=@tier " +
                "AND LAST_TRIBUTE_ATTEMPT_YEAR=@year";
            command.Parameters.AddWithValue("@year", currentYear);
            command.Parameters.AddWithValue("@next", currentYear + 1);
            command.Parameters.AddWithValue("@factor",
                Math.Max(0, Math.Min(100, factorPercent)));
            command.Parameters.AddWithValue("@relation", relationId);
            command.Parameters.AddWithValue("@tier",
                VassalContractTierRules.Tributary);
            RequireExactlyOne(command.ExecuteNonQuery(),
                "mark paid", relationId);
        }

        private static void RequireExactlyOne(int changedRows,
            string operation, long relationId)
        {
            if (changedRows == 1) return;
            throw new InvalidOperationException(
                "Tributary settlement " + operation +
                " expected one active loose relation, changed=" +
                changedRows + " relation=" + relationId);
        }
    }
}
