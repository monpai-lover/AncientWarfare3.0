using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;

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
            if (ActorArchivePendingStore.TryRead(pActorId,
                    out ActorArchiveTableItem pending)) return pending;
            LineageBulkSnapshot snapshot =
                LineageBulkSnapshotContext.Current;
            if (snapshot != null && snapshot.TryGetActor(pActorId,
                    out ActorArchiveTableItem bulk)) return bulk;
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

        public static IReadOnlyList<long> ReadLivingNobleActorIds(
            long pKingdomId, long pAfterActorId, int pLimit,
            out long pNextCursor)
        {
            pNextCursor = pAfterActorId;
            if (pKingdomId < 0 || pLimit <= 0)
                return Array.Empty<long>();
            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return Array.Empty<long>();

            int limit = Math.Min(64, Math.Max(1, pLimit));
            List<long> result = ReadLivingNobleActorIdsCore(db,
                pKingdomId, pAfterActorId, limit);
            if (result.Count == 0 && pAfterActorId >= 0)
                result = ReadLivingNobleActorIdsCore(db, pKingdomId,
                    -1L, limit);
            if (result.Count > 0)
                pNextCursor = result[result.Count - 1];
            return result;
        }

        public static IReadOnlyList<long> ReadLivingDeclinedNobleActorIds(
            long pKingdomId, long pAfterActorId, int pLimit,
            out long pNextCursor)
        {
            pNextCursor = pAfterActorId;
            if (pKingdomId < 0 || pLimit <= 0)
                return Array.Empty<long>();
            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return Array.Empty<long>();

            int limit = Math.Min(64, Math.Max(1, pLimit));
            List<long> result = HistoricalSchoolEducationCandidateQuery.
                LoadDeclinedNobles(db, ActorArchiveTableItem.GetTableName(),
                    pKingdomId, pAfterActorId, limit);
            if (result.Count == 0 && pAfterActorId >= 0)
                result = HistoricalSchoolEducationCandidateQuery.
                    LoadDeclinedNobles(db,
                        ActorArchiveTableItem.GetTableName(), pKingdomId,
                        -1L, limit);
            if (result.Count > 0)
                pNextCursor = result[result.Count - 1];
            return result;
        }

        private static List<long> ReadLivingNobleActorIdsCore(
            SQLiteConnection pDb, long pKingdomId, long pAfterActorId,
            int pLimit)
        {
            var result = new List<long>(pLimit);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT ID FROM " +
                ActorArchiveTableItem.GetTableName() +
                " WHERE KINGDOM_ID=@kingdom AND IS_ALIVE=1 " +
                "AND STATUS=@status AND ID>@cursor " +
                "ORDER BY ID LIMIT @limit";
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@status", LineageStatus.NOBLE);
            command.Parameters.AddWithValue("@cursor", pAfterActorId);
            command.Parameters.AddWithValue("@limit", pLimit);
            using var reader = (SQLiteDataReader)command.ExecuteReader();
            while (reader.Read()) result.Add(reader.GetInt64(0));
            return result;
        }
    }
}
