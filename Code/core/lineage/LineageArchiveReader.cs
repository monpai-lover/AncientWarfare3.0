using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     从 SQLite 档案读取已死(或当前不在场)Xia 的信息。
    ///     供 LineageService 做父系继承(死去的父亲)、UI 渲染死者节点。
    /// </summary>
    internal static class LineageArchiveReader
    {
        /// <summary>读档案 sex(0=男/1=女),无记录返回 -1。</summary>
        public static int GetSex(long pActorId)
        {
            var row = ReadRow(pActorId);
            return row?.sex ?? -1;
        }

        /// <summary>读死者谱系快照。无记录返回 false。</summary>
        public static bool TryGetLineage(long pActorId, out long pLineageId, out long pShiId,
            out string pFamilyName, out string pClanName, out int pNobleDistance)
        {
            pLineageId = -1;
            pShiId = -1;
            pFamilyName = "";
            pClanName = "";
            pNobleDistance = 99;

            var row = ReadRow(pActorId);
            if (row == null) return false;

            pLineageId = row.lineage_id;
            pShiId = row.shi_id;
            pFamilyName = row.family_name ?? "";
            pClanName = row.clan_name ?? "";
            pNobleDistance = row.noble_distance;
            return true;
        }

        /// <summary>按 id 读一行档案,反射填回 ActorArchiveTableItem。无则 null。</summary>
        public static ActorArchiveTableItem ReadRow(long pActorId)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return null;

            using var cmd = new SQLiteCommand(db);
            cmd.CommandText = $"SELECT * FROM {ActorArchiveTableItem.GetTableName()} WHERE ID=@id LIMIT 1";
            cmd.Parameters.AddWithValue("@id", pActorId);

            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var item = new ActorArchiveTableItem();
            item.ReadFromReader(reader);
            return item;
        }
    }
}
