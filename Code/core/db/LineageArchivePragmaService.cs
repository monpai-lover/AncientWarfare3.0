using System;
using System.Data.SQLite;
using System.IO;

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

        public static void ConfigureSnapshotTarget(SQLiteConnection pConnection)
        {
            if (pConnection == null)
                throw new ArgumentNullException(nameof(pConnection));

            using var command = pConnection.CreateCommand();
            command.CommandText =
                "PRAGMA journal_mode=DELETE;" +
                "PRAGMA synchronous=FULL;" +
                "PRAGMA busy_timeout=2500;";
            command.ExecuteNonQuery();
        }

        public static string SnapshotTargetConnectionString(string pPath)
        {
            return new SQLiteConnectionStringBuilder
            {
                DataSource = pPath,
                Version = 3,
                Pooling = false
            }.ConnectionString;
        }

        public static string SnapshotReadOnlyConnectionString(string pPath)
        {
            return new SQLiteConnectionStringBuilder
            {
                DataSource = pPath,
                Version = 3,
                ReadOnly = true,
                FailIfMissing = true,
                Pooling = false
            }.ConnectionString;
        }

        public static bool TryValidateSnapshot(string pPath,
            out bool pJournalModeInvalid, out string pError)
        {
            pJournalModeInvalid = false;
            pError = string.Empty;

            try
            {
                using var connection = new SQLiteConnection(
                    SnapshotReadOnlyConnectionString(pPath));
                connection.Open();

                using (var quickCheck = connection.CreateCommand())
                {
                    quickCheck.CommandText = "PRAGMA quick_check;";
                    using var reader = quickCheck.ExecuteReader();
                    if (!reader.Read() ||
                        !string.Equals(reader.GetString(0), "ok",
                            StringComparison.OrdinalIgnoreCase) ||
                        reader.Read())
                    {
                        pError = "Lineage archive quick_check did not return ok.";
                        return false;
                    }
                }

                using (var journalMode = connection.CreateCommand())
                {
                    journalMode.CommandText = "PRAGMA journal_mode;";
                    string mode = Convert.ToString(journalMode.ExecuteScalar());
                    if (!string.Equals(mode, "delete",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        pJournalModeInvalid = true;
                        pError = "Lineage archive journal_mode is " + mode + ".";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception error) when (error is SQLiteException ||
                                          error is IOException ||
                                          error is UnauthorizedAccessException)
            {
                pError = error.Message;
                return false;
            }
        }
    }
}
