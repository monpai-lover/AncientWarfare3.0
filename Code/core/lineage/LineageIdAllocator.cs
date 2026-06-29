using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     姓族 / 氏支 全局自增 ID 分配。直接查表 MAX(id)+1,不需要单独计数器表,
    ///     天然随存档持久化(库本身随存档复制)。从 1 起,0 留作"无"。
    /// </summary>
    internal static class LineageIdAllocator
    {
        public static long NextLineageId()
        {
            return NextId(LineageGroupTableItem.GetTableName(), "LINEAGE_ID");
        }

        public static long NextShiId()
        {
            return NextId(ShiBranchTableItem.GetTableName(), "SHI_ID");
        }

        private static long NextId(string pTable, string pIdColumn)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return -1;

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText = $"SELECT IFNULL(MAX({pIdColumn}), 0) FROM {pTable}";
            long max = (long)cmd.ExecuteScalar();
            return max + 1;
        }
    }
}
