using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    public sealed class SuccessionDisputeIndexSpec
    {
        public string Name = "";
        public string Table = "";
        public string Columns = "";
        public string Where = "";
        public bool Unique;

        public string BuildSql()
        {
            string sql = "CREATE " + (Unique ? "UNIQUE " : "") +
                         "INDEX IF NOT EXISTS " + Name + " ON " + Table +
                         " (" + Columns + ")";
            return string.IsNullOrEmpty(Where)
                ? sql
                : sql + " WHERE " + Where;
        }
    }

    public static class SuccessionDisputeIndexRules
    {
        public static IReadOnlyList<SuccessionDisputeIndexSpec>
            GetRequiredIndexes()
        {
            return new[]
            {
                Index("uq_SuccessionDispute_original_active",
                    "SuccessionDispute", "ORIGINAL_KINGDOM_ID",
                    "STATUS<>6 AND END_TIME<0", true),
                Index("uq_SuccessionDispute_rival_active",
                    "SuccessionDispute", "RIVAL_KINGDOM_ID",
                    "RIVAL_KINGDOM_ID>=0 AND STATUS<>6 AND END_TIME<0",
                    true),
                Index("idx_SuccessionDispute_due", "SuccessionDispute",
                    "STATUS, DEADLINE_YEAR, DISPUTE_ID"),
                Index("uq_SuccessionDisputeCity_city_active",
                    "SuccessionDisputeCity", "CITY_ID", "ACTIVE=1", true),
                Index("idx_SuccessionDisputeCity_dispute_active",
                    "SuccessionDisputeCity",
                    "DISPUTE_ID, ACTIVE, ORDINAL, CITY_ID")
            };
        }

        private static SuccessionDisputeIndexSpec Index(string pName,
            string pTable, string pColumns, string pWhere = "",
            bool pUnique = false)
        {
            return new SuccessionDisputeIndexSpec
            {
                Name = pName,
                Table = pTable,
                Columns = pColumns,
                Where = pWhere,
                Unique = pUnique
            };
        }
    }
}
