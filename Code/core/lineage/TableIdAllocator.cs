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
                return Next(pDb, null, pTable, pPrimaryCol);
            }
            catch { return 1; }
        }

        public static long Next(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable,
            string pPrimaryCol)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));
            using var cmd = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = $"SELECT IFNULL(MAX({pPrimaryCol}), 0) " +
                              $"FROM {pTable}"
            };
            object result = cmd.ExecuteScalar();
            long maximum = result == null || result == DBNull.Value
                ? 0L
                : Convert.ToInt64(result);
            return maximum + 1;
        }
    }
}
