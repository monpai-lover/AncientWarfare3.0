using System;
using System.Data.SQLite;
using System.Threading.Tasks;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.historyapi
{
    internal static class AW3HistoryReadConnection
    {
        public static bool TryRead<T>(Func<SQLiteConnection, T> read,
            out T result)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            result = default;
            long epoch = LineageArchiveManager.RuntimeDatabaseEpoch;
            string path;
            try { path = LineageArchiveManager.RuntimeDbPath; }
            catch { return false; }
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                result = Task.Run(() => ReadOnForeignThread(read, path, epoch))
                    .GetAwaiter().GetResult();
                return epoch == LineageArchiveManager.RuntimeDatabaseEpoch;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static T ReadOnForeignThread<T>(Func<SQLiteConnection, T> read,
            string path, long epoch)
        {
            if (epoch != LineageArchiveManager.RuntimeDatabaseEpoch)
                return default;
            using var connection = new SQLiteConnection(
                LineageArchivePragmaService.SnapshotReadOnlyConnectionString(path));
            connection.Open();
            if (epoch != LineageArchiveManager.RuntimeDatabaseEpoch)
                return default;
            using (HistoryQuery.EnterBackgroundRead(connection))
            using (LineageQuery.EnterBackgroundRead(connection))
            {
                T result = read(connection);
                return epoch == LineageArchiveManager.RuntimeDatabaseEpoch
                    ? result : default;
            }
        }
    }
}
