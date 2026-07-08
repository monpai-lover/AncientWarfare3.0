using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class KingdomRenameSyncService
    {
        [ThreadStatic] private static int _suppressDepth;

        private static readonly (string idColumn, string nameColumn)[] NAME_COLUMN_PAIRS =
        {
            ("KINGDOM_ID", "KINGDOM_NAME"),
            ("KINGDOM_ID", "SUBJECT_NAME"),
            ("CONTEXT_KINGDOM_ID", "CONTEXT_KINGDOM_NAME"),
            ("CONTEXT_KINGDOM_ID", "KINGDOM_NAME"),
            ("OWNER_KINGDOM_ID", "OWNER_KINGDOM_NAME"),
            ("ORIGINAL_KINGDOM_ID", "ORIGINAL_KINGDOM_NAME"),
            ("OCCUPIER_KINGDOM_ID", "OCCUPIER_KINGDOM_NAME"),
            ("SUZERAIN_ID", "SUZERAIN_NAME"),
            ("HOST_KINGDOM_ID", "HOST_KINGDOM_NAME"),
            ("SOURCE_KINGDOM_ID", "SOURCE_KINGDOM_NAME"),
            ("TARGET_KINGDOM_ID", "TARGET_KINGDOM_NAME"),
            ("REBEL_ORIGIN_KINGDOM_ID", "REBEL_ORIGIN_KINGDOM_NAME")
        };

        public static bool IsSuppressed => _suppressDepth > 0;

        public static void Suppress(Action pAction)
        {
            if (pAction == null) return;
            _suppressDepth++;
            try { pAction(); }
            finally { _suppressDepth = Math.Max(0, _suppressDepth - 1); }
        }

        public static void OnKingdomNameChanged(Kingdom pKingdom, string pOldName, string pNewName, bool pTrack)
        {
            bool archivable = KingdomArchiveWriter.IsArchivable(pKingdom);
            if (!KingdomRenameRules.ShouldRecordRename(pOldName, pNewName, pTrack, archivable, IsSuppressed))
            {
                if (archivable)
                {
                    KingdomArchiveWriter.Upsert(pKingdom);
                    if (pTrack && IsSuppressed && (pOldName ?? "").Trim() != (pNewName ?? "").Trim())
                        SyncNameSnapshots(pKingdom.id, pNewName);
                }
                return;
            }

            KingdomArchiveWriter.Upsert(pKingdom);
            SyncNameSnapshots(pKingdom.id, pNewName);
            RecordRenameEvent(pKingdom, pOldName, pNewName);
        }

        private static void RecordRenameEvent(Kingdom pKingdom, string pOldName, string pNewName)
        {
            string color = HistoryColors.FromKingdom(pKingdom);
            HistoryText oldText = HistoryText.Colored(pOldName ?? "", color);
            HistoryText newText = HistoryText.Colored(pNewName ?? pKingdom.name ?? "", color);
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RENAMED,
                oldText + HistoryText.PlainText(" \u6539\u56fd\u540d\u4e3a ") + newText,
                HistoryTarget.Kingdom(pKingdom));
        }

        private static void SyncNameSnapshots(long pKingdomId, string pNewName)
        {
            var manager = LineageArchiveManager.Instance;
            SQLiteConnection db = manager?.OperatingDB;
            if (db == null || !manager.InitializeSuccessful || pKingdomId < 0) return;

            try
            {
                foreach (string table in ReadTableNames(db))
                {
                    HashSet<string> columns = ReadColumns(db, table);
                    foreach (var pair in NAME_COLUMN_PAIRS)
                    {
                        if (!columns.Contains(pair.idColumn) || !columns.Contains(pair.nameColumn)) continue;
                        UpdateNameColumn(db, table, pair.idColumn, pair.nameColumn, pKingdomId, pNewName);
                    }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("KingdomRenameSyncService.SyncNameSnapshots: " + e.Message);
            }
        }

        private static List<string> ReadTableNames(SQLiteConnection pDb)
        {
            var result = new List<string>();
            using var cmd = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'", pDb);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }

        private static HashSet<string> ReadColumns(SQLiteConnection pDb, string pTable)
        {
            var result = new HashSet<string>();
            using var cmd = new SQLiteCommand("PRAGMA table_info(" + QuoteIdentifier(pTable) + ")", pDb);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (!string.IsNullOrEmpty(name)) result.Add(name.ToUpperInvariant());
            }
            return result;
        }

        private static void UpdateNameColumn(SQLiteConnection pDb, string pTable, string pIdColumn,
            string pNameColumn, long pKingdomId, string pNewName)
        {
            using var cmd = new SQLiteCommand(
                "UPDATE " + QuoteIdentifier(pTable) +
                " SET " + QuoteIdentifier(pNameColumn) + "=@name" +
                " WHERE " + QuoteIdentifier(pIdColumn) + "=@kid", pDb);
            cmd.Parameters.AddWithValue("@name", pNewName ?? "");
            cmd.Parameters.AddWithValue("@kid", pKingdomId);
            cmd.ExecuteNonQuery();
        }

        private static string QuoteIdentifier(string pIdentifier)
        {
            return "\"" + (pIdentifier ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
