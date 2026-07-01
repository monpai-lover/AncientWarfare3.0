using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     结构化表（KingdomReign/DynastyPeriod/EraPeriod/PosthumousTitle）的主键自增。
    ///     原理与 HistoryWriter.NextEventId 相同：取 MAX(PRIMARY_COL)+1。
    /// </summary>
    internal static class TableIdAllocator
    {
        public static long Next(SQLiteConnection pDb, string pTable, string pPrimaryCol)
        {
            if (pDb == null) return 1;
            try
            {
                using var cmd = new SQLiteCommand(pDb);
                cmd.CommandText = $"SELECT IFNULL(MAX({pPrimaryCol}), 0) FROM {pTable}";
                object r = cmd.ExecuteScalar();
                long max = (r == null || r == DBNull.Value) ? 0L : Convert.ToInt64(r);
                return max + 1;
            }
            catch { return 1; }
        }
    }
}
