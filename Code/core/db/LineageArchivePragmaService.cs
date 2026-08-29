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
                // 负值按 KB 计,所以是 32MB。默认是 2000 页,而这个库
                // 的 page_size 是 1024(System.Data.SQLite 3.9.2 的旧默认值,
                // 3.12 才改成 4096),等于只有 2MB 页缓存 —— 而运行库开局就
                // 8.5MB,长局实测能涨到 276MB,热页根本留不住。
                //
                // 在真实库上实测同一批 central 官职计数查询:
                //   cache_size=2000           51.1 us/次
                //   cache_size=-32768(32MB)  31.1 us/次
                // 快 1.64 倍,且对 mod 里每一次读都生效(学派/朝廷是读密集的)。
                //
                // 同时测了 mmap_size=64MB:37.3 us/次,比只调 cache 更慢,
                // 所以不开。
                "PRAGMA cache_size=-32768;" +
                // 阈值按页算,1000 页在 1024 的页大小下只是 1MB。实测检查点
                // 成本约为「5ms 固定 + 每 MB 约 6ms」:
                //   139KB 4.48ms / 1.06MB 12.44ms / 4.24MB 29.33ms
                // 固定开销在小 WAL 时占主导,所以调低阈值会让总量变差
                // (256KB 阈值 = 4 次 x 6.4ms = 25.6ms/MB,而 1MB 阈值是
                // 12.4ms/MB)。保持 1000。
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
