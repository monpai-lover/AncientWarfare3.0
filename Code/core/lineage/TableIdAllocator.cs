using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     结构化表（KingdomReign/DynastyPeriod/EraPeriod/PosthumousTitle）的主键自增。
    ///     底数取 MAX(PRIMARY_COL)+1，但绝大多数调用方在 BeginTransaction 之前取号、
    ///     之后才插入，MAX 本身无法阻止两次取号拿到同一个值；未提交的行也不在 MAX
    ///     里。因此这里为每张表额外保留一个进程内水位线：发号时取「数据库底数」与
    ///     「水位线」的较大者，并立刻推进水位线，使同一个连接内不会重复发号。
    ///     水位线按连接实例绑定，换库后自然作废。
    /// </summary>
    internal static class TableIdAllocator
    {
        private static readonly object Gate = new object();

        private static readonly Dictionary<string, long> HighWaterMarks =
            new Dictionary<string, long>(StringComparer.Ordinal);

        private static readonly WeakReference ConnectionReference =
            new WeakReference(null);

        public static long Next(SQLiteConnection pDb, string pTable, string pPrimaryCol)
        {
            if (pDb == null) return 1;
            try
            {
                return Next(pDb, null, pTable, pPrimaryCol);
            }
            catch
            {
                // 底数查询失败时不能退回 1：那在非空表上几乎必然撞主键。
                // 改为只按水位线继续发号；没有水位线时才退回 1。
                return Reserve(pDb, pTable, pPrimaryCol, 0L, 1);
            }
        }

        public static long Next(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable,
            string pPrimaryCol)
        {
            return Next(pDb, pTransaction, pTable, pPrimaryCol, 1);
        }

        /// <summary>
        ///     一次预留 <paramref name="pCount" /> 个连续 ID 并返回其中最小的一个，
        ///     调用方按 返回值 + 0..pCount-1 使用。分多次取号会让中间的号被后续
        ///     调用重新发出。
        /// </summary>
        public static long Next(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable,
            string pPrimaryCol, int pCount)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));
            if (pCount < 1) throw new ArgumentOutOfRangeException(nameof(pCount));
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
            return Reserve(pDb, pTable, pPrimaryCol, maximum, pCount);
        }

        private static long Reserve(SQLiteConnection pDb, string pTable,
            string pPrimaryCol, long pDatabaseMaximum, int pCount)
        {
            string key = pTable + "." + pPrimaryCol;
            lock (Gate)
            {
                if (!ReferenceEquals(ConnectionReference.Target, pDb))
                {
                    // 换了连接就是换了库，旧水位线不再适用。
                    HighWaterMarks.Clear();
                    ConnectionReference.Target = pDb;
                }

                long next = pDatabaseMaximum + 1L;
                if (HighWaterMarks.TryGetValue(key, out long reserved) &&
                    reserved > next)
                    next = reserved;
                if (next < 1L) next = 1L;
                if (next > long.MaxValue - pCount) return -1L;
                HighWaterMarks[key] = next + pCount;
                return next;
            }
        }
    }
}
