using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class DynastyTitleRegistryService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        // 谥号/庙号登记表在一次统治期内几乎不变,只有 TryReserve 授予新号时才写入。
        // 但每次君主死亡的谥号评定要连查 ReadLatestCycle x2 / ReadUsed x2 /
        // ReadLatestValue,全是主线程同步 SQLite 往返。这里按 (shi, title_type)
        // 常驻缓存,授予时失效,世界重置时清空。
        private readonly struct RegistryKey : IEquatable<RegistryKey>
        {
            internal RegistryKey(long pShiId, string pTitleType)
            {
                ShiId = pShiId;
                TitleType = pTitleType ?? "";
            }

            private long ShiId { get; }
            private string TitleType { get; }

            public bool Equals(RegistryKey pOther)
            {
                return ShiId == pOther.ShiId &&
                       string.Equals(TitleType, pOther.TitleType,
                           StringComparison.Ordinal);
            }

            public override bool Equals(object pObject)
            {
                return pObject is RegistryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ShiId.GetHashCode() * 397 ^
                           (TitleType?.GetHashCode() ?? 0);
                }
            }
        }

        private sealed class RegistryEntry
        {
            internal bool HasLatestCycle;
            internal int LatestCycle;
            internal bool HasLatestValue;
            internal string LatestValue;
            internal Dictionary<int, HashSet<string>> UsedByCycle;
        }

        private static readonly object CacheGate = new object();
        private static readonly Dictionary<RegistryKey, RegistryEntry> Cache =
            new Dictionary<RegistryKey, RegistryEntry>();

        public static void ClearRuntime()
        {
            lock (CacheGate) Cache.Clear();
        }

        private static void Invalidate(long pShiId, string pTitleType)
        {
            lock (CacheGate)
                Cache.Remove(new RegistryKey(pShiId, Normalize(pTitleType)));
        }

        private static string Normalize(string pTitleType)
        {
            return pTitleType?.Trim() ?? "";
        }

        private static RegistryEntry GetOrCreateLocked(RegistryKey pKey)
        {
            if (!Cache.TryGetValue(pKey, out RegistryEntry entry))
            {
                entry = new RegistryEntry();
                Cache[pKey] = entry;
            }
            return entry;
        }

        public static int ReadLatestCycle(long pShiId, string pTitleType)
        {
            if (!Ready || pShiId < 0 || string.IsNullOrWhiteSpace(pTitleType)) return 0;
            string kind = Normalize(pTitleType);
            var key = new RegistryKey(pShiId, kind);
            lock (CacheGate)
                if (Cache.TryGetValue(key, out RegistryEntry cached) &&
                    cached.HasLatestCycle)
                    return cached.LatestCycle;
            int value;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(MAX(CYCLE_NO),0) FROM " +
                                      DynastyTitleRegistryTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi AND TITLE_TYPE=@kind";
                command.Parameters.AddWithValue("@shi", pShiId);
                command.Parameters.AddWithValue("@kind", kind);
                value = Math.Max(0, Convert.ToInt32(command.ExecuteScalar()));
            }
            catch { return 0; }
            lock (CacheGate)
            {
                RegistryEntry entry = GetOrCreateLocked(key);
                entry.LatestCycle = value;
                entry.HasLatestCycle = true;
            }
            return value;
        }

        public static string ReadLatestValue(long pShiId, string pTitleType)
        {
            if (!Ready || pShiId < 0 || string.IsNullOrWhiteSpace(pTitleType)) return "";
            string kind = Normalize(pTitleType);
            var key = new RegistryKey(pShiId, kind);
            lock (CacheGate)
                if (Cache.TryGetValue(key, out RegistryEntry cached) &&
                    cached.HasLatestValue)
                    return cached.LatestValue;
            string value;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(TITLE_VALUE,'') FROM " +
                                      DynastyTitleRegistryTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi AND TITLE_TYPE=@kind " +
                                      "ORDER BY USED_TIME DESC,REGISTRY_ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@shi", pShiId);
                command.Parameters.AddWithValue("@kind", kind);
                value = Convert.ToString(command.ExecuteScalar()) ?? "";
            }
            catch { return ""; }
            lock (CacheGate)
            {
                RegistryEntry entry = GetOrCreateLocked(key);
                entry.LatestValue = value;
                entry.HasLatestValue = true;
            }
            return value;
        }

        public static HashSet<string> ReadUsed(long pShiId, string pTitleType, int pCycleNo)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!Ready || pShiId < 0 || string.IsNullOrWhiteSpace(pTitleType)) return result;
            string kind = Normalize(pTitleType);
            int cycle = Math.Max(0, pCycleNo);
            var key = new RegistryKey(pShiId, kind);
            // 命中时返回副本:调用方(谥号选择规则)会持有并可能改动这个集合,
            // 直接交出缓存实例会污染后续读取。
            lock (CacheGate)
                if (Cache.TryGetValue(key, out RegistryEntry cached) &&
                    cached.UsedByCycle != null &&
                    cached.UsedByCycle.TryGetValue(cycle,
                        out HashSet<string> hit))
                    return new HashSet<string>(hit, StringComparer.Ordinal);
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT TITLE_VALUE FROM " +
                                  DynastyTitleRegistryTableItem.GetTableName() +
                                  " WHERE SHI_ID=@shi AND TITLE_TYPE=@kind AND CYCLE_NO=@cycle";
            command.Parameters.AddWithValue("@shi", pShiId);
            command.Parameters.AddWithValue("@kind", kind);
            command.Parameters.AddWithValue("@cycle", cycle);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (!string.IsNullOrEmpty(value)) result.Add(value);
            }
            lock (CacheGate)
            {
                RegistryEntry entry = GetOrCreateLocked(key);
                entry.UsedByCycle ??= new Dictionary<int, HashSet<string>>();
                entry.UsedByCycle[cycle] =
                    new HashSet<string>(result, StringComparer.Ordinal);
            }
            return result;
        }

        public static bool TryReserve(SQLiteConnection pDb, SQLiteTransaction pTransaction,
            long pShiId, string pTitleType, string pValue, int pCycleNo,
            long pActorId, long pReignId, double pTime)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));
            if (pTransaction == null) throw new ArgumentNullException(nameof(pTransaction));
            if (pShiId < 0) throw new ArgumentOutOfRangeException(nameof(pShiId));
            if (string.IsNullOrWhiteSpace(pTitleType))
                throw new ArgumentException("Title type is required.", nameof(pTitleType));
            if (string.IsNullOrWhiteSpace(pValue))
                throw new ArgumentException("Title value is required.", nameof(pValue));

            try
            {
                using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
                command.CommandText = "INSERT INTO " +
                                      DynastyTitleRegistryTableItem.GetTableName() +
                                      " (REGISTRY_ID,SHI_ID,TITLE_TYPE,TITLE_VALUE,CYCLE_NO," +
                                      "ACTOR_ID,REIGN_ID,USED_TIME) VALUES (" +
                                      "(SELECT IFNULL(MAX(REGISTRY_ID),-1)+1 FROM " +
                                      DynastyTitleRegistryTableItem.GetTableName() +
                                      "),@shi,@kind,@value,@cycle,@actor,@reign,@time)";
                command.Parameters.AddWithValue("@shi", pShiId);
                command.Parameters.AddWithValue("@kind", pTitleType.Trim());
                command.Parameters.AddWithValue("@value", pValue.Trim());
                command.Parameters.AddWithValue("@cycle", Math.Max(0, pCycleNo));
                command.Parameters.AddWithValue("@actor", pActorId);
                command.Parameters.AddWithValue("@reign", pReignId);
                command.Parameters.AddWithValue("@time", pTime);
                command.ExecuteNonQuery();
                // 事务此后若回滚,失效也只是让下次读回源,不会读到错值。
                Invalidate(pShiId, pTitleType);
                return true;
            }
            catch (SQLiteException error)
                when (error.ResultCode == SQLiteErrorCode.Constraint)
            {
                return false;
            }
        }
    }
}
