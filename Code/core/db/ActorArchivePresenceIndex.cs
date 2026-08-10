using System.Collections.Concurrent;
using System.Data.SQLite;
using System.Threading;

namespace AncientWarfare3.core.db
{
    internal static class ActorArchivePresenceIndex
    {
        private static ConcurrentDictionary<long, byte> _livingActorIds =
            new ConcurrentDictionary<long, byte>();
        private static int _ready;

        internal static bool IsReady => Volatile.Read(ref _ready) != 0;

        internal static bool Contains(long pActorId)
        {
            return pActorId >= 0L &&
                   Volatile.Read(ref _livingActorIds).ContainsKey(pActorId);
        }

        internal static void Mark(long pActorId)
        {
            if (pActorId < 0L) return;
            Volatile.Read(ref _livingActorIds)[pActorId] = 0;
        }

        internal static void Remove(long pActorId)
        {
            if (pActorId < 0L) return;
            Volatile.Read(ref _livingActorIds).TryRemove(pActorId, out _);
        }

        internal static void ResetEmpty()
        {
            Interlocked.Exchange(ref _livingActorIds,
                new ConcurrentDictionary<long, byte>());
            Volatile.Write(ref _ready, 1);
        }

        internal static void ClearUnknown()
        {
            Interlocked.Exchange(ref _livingActorIds,
                new ConcurrentDictionary<long, byte>());
            Volatile.Write(ref _ready, 0);
        }

        internal static bool Rebuild(SQLiteConnection pDatabase)
        {
            if (pDatabase == null)
            {
                ClearUnknown();
                return false;
            }

            var rebuilt = new ConcurrentDictionary<long, byte>();
            try
            {
                using var command = new SQLiteCommand(pDatabase);
                command.CommandText = "SELECT ID FROM " +
                    ActorArchiveTableItem.GetTableName() +
                    " WHERE IS_ALIVE=1";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                        rebuilt[reader.GetInt64(0)] = 0;
                }

                Interlocked.Exchange(ref _livingActorIds, rebuilt);
                Volatile.Write(ref _ready, 1);
                return true;
            }
            catch
            {
                ClearUnknown();
                return false;
            }
        }
    }
}
