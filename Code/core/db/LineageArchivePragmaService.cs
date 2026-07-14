using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.db
{
    internal static class LineageArchivePragmaService
    {
        public static void Configure(SQLiteConnection pConnection)
        {
            if (pConnection == null)
                throw new ArgumentNullException(nameof(pConnection));

            using var command = pConnection.CreateCommand();
            command.CommandText =
                "PRAGMA journal_mode=WAL;" +
                "PRAGMA synchronous=NORMAL;" +
                "PRAGMA busy_timeout=2500;" +
                "PRAGMA wal_autocheckpoint=1000;";
            command.ExecuteNonQuery();
        }

        public static bool CheckpointForSave(SQLiteConnection pConnection)
        {
            if (pConnection == null) return true;

            try
            {
                using var command = pConnection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                command.ExecuteNonQuery();
                return true;
            }
            catch (SQLiteException error)
            {
                ModClass.LogWarning(
                    "Lineage archive WAL checkpoint failed: " + error.Message);
                return false;
            }
        }
    }
}
