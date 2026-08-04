using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class MandateLegalCoreProjectionPersistence
    {
        public static bool TryReadInheritedSnapshots(SQLiteConnection pDb,
            long previousPeriodId,
            out List<MandateProjectionOutboxPersistence.CoreCitySnapshot>
                pSnapshots,
            out string pError)
        {
            pSnapshots = new List<MandateProjectionOutboxPersistence.
                CoreCitySnapshot>();
            pError = "";
            if (pDb == null || previousPeriodId < 0L)
            {
                pError = "invalid inherited mandate core lookup";
                return false;
            }
            try
            {
                var captured = new HashSet<long>();
                using (var command = new SQLiteCommand(pDb)
                {
                    CommandText = "SELECT CITY_ID,CITY_NAME," +
                        "ORIGINAL_KINGDOM_ID,ORIGINAL_KINGDOM_NAME," +
                        "ORIGINAL_KINGDOM_COLOR FROM MandateCoreCity " +
                        "WHERE PERIOD_ID=@period AND ACTIVE=1 " +
                        "ORDER BY CORE_ID"
                })
                {
                    command.Parameters.AddWithValue("@period",
                        previousPeriodId);
                    using SQLiteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        AddInherited(pSnapshots, captured,
                            Convert.ToInt64(reader.GetValue(0)),
                            Convert.ToString(reader.GetValue(1)) ?? "",
                            Convert.ToInt64(reader.GetValue(2)),
                            Convert.ToString(reader.GetValue(3)) ?? "",
                            Convert.ToString(reader.GetValue(4)) ?? "");
                    }
                }
                if (!MandateProjectionOutboxPersistence.
                        TryReadCoreSnapshotsByPeriod(pDb, previousPeriodId,
                            out List<MandateProjectionOutboxPersistence.
                                CoreCitySnapshot> pending, out pError))
                    return false;
                foreach (MandateProjectionOutboxPersistence.CoreCitySnapshot
                         snapshot in pending)
                {
                    if (snapshot == null) continue;
                    AddInherited(pSnapshots, captured, snapshot.CityId,
                        snapshot.CityName, snapshot.OriginalKingdomId,
                        snapshot.OriginalKingdomName,
                        snapshot.OriginalKingdomColor);
                }
                pSnapshots.Sort((left, right) =>
                    left.CityId.CompareTo(right.CityId));
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        public static bool TryProject(SQLiteConnection pDb, long stateId,
            long periodId,
            IReadOnlyList<MandateProjectionOutboxPersistence.
                CoreCitySnapshot> pSnapshots,
            string projectionKeyPrefix, double addedTime,
            bool requireCurrentStateUpdate, out int pCount,
            out string pError)
        {
            pCount = 0;
            pError = "";
            if (pDb == null || stateId < 0L || periodId < 0L ||
                pSnapshots == null ||
                string.IsNullOrWhiteSpace(projectionKeyPrefix))
            {
                pError = "invalid mandate legal-core projection";
                return false;
            }
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                long nextCoreId = ReadNextCoreId(pDb, transaction);
                var captured = new HashSet<long>();
                foreach (MandateProjectionOutboxPersistence.CoreCitySnapshot
                         snapshot in pSnapshots)
                {
                    if (snapshot == null || snapshot.CityId < 0L ||
                        !captured.Add(snapshot.CityId))
                        continue;
                    string projectionKey = projectionKeyPrefix +
                                           snapshot.CityId;
                    if (!EnsureCore(pDb, transaction, periodId, snapshot,
                            projectionKey, addedTime, ref nextCoreId))
                        throw new InvalidOperationException(
                            "mandate legal core expected one row");
                }
                pCount = CountCores(pDb, transaction, periodId);
                UpdatePeriodCount(pDb, transaction, periodId, pCount);
                if (requireCurrentStateUpdate)
                    UpdateCurrentStateCount(pDb, transaction, stateId,
                        periodId, pCount, addedTime);
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                pError = error.Message;
                pCount = 0;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static void AddInherited(
            List<MandateProjectionOutboxPersistence.CoreCitySnapshot>
                pSnapshots,
            HashSet<long> pCaptured, long pCityId, string pCityName,
            long pKingdomId, string pKingdomName, string pKingdomColor)
        {
            if (pCityId < 0L || !pCaptured.Add(pCityId)) return;
            pSnapshots.Add(new MandateProjectionOutboxPersistence.
                CoreCitySnapshot
            {
                CityId = pCityId,
                CityName = pCityName ?? "",
                OriginalKingdomId = pKingdomId,
                OriginalKingdomName = pKingdomName ?? "",
                OriginalKingdomColor = pKingdomColor ?? "",
                CoreType = "inherited",
                SnapshotSource = "inheritance"
            });
        }

        private static long ReadNextCoreId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT COALESCE(MAX(CORE_ID),0)+1 " +
                              "FROM MandateCoreCity"
            };
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static bool EnsureCore(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pPeriodId,
            MandateProjectionOutboxPersistence.CoreCitySnapshot pSnapshot,
            string pProjectionKey, double pAddedTime, ref long pNextCoreId)
        {
            using (var existing = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT CORE_ID,PROJECTION_KEY FROM " +
                    "MandateCoreCity WHERE PERIOD_ID=@period " +
                    "AND CITY_ID=@city AND ACTIVE=1 LIMIT 1"
            })
            {
                existing.Parameters.AddWithValue("@period", pPeriodId);
                existing.Parameters.AddWithValue("@city", pSnapshot.CityId);
                using SQLiteDataReader reader = existing.ExecuteReader();
                if (reader.Read())
                {
                    long coreId = Convert.ToInt64(reader.GetValue(0));
                    string currentKey = Convert.ToString(
                        reader.GetValue(1)) ?? "";
                    reader.Close();
                    if (!string.IsNullOrWhiteSpace(currentKey)) return true;
                    using var backfill = new SQLiteCommand(pDb)
                    {
                        Transaction = pTransaction,
                        CommandText = "UPDATE MandateCoreCity SET " +
                            "PROJECTION_KEY=@key WHERE CORE_ID=@id " +
                            "AND PROJECTION_KEY=''"
                    };
                    backfill.Parameters.AddWithValue("@key", pProjectionKey);
                    backfill.Parameters.AddWithValue("@id", coreId);
                    return backfill.ExecuteNonQuery() == 1;
                }
            }
            using var insert = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO MandateCoreCity(" +
                    "CORE_ID,PERIOD_ID,CITY_ID,CITY_NAME," +
                    "ORIGINAL_KINGDOM_ID,ORIGINAL_KINGDOM_NAME," +
                    "ORIGINAL_KINGDOM_COLOR,CORE_TYPE,ADDED_TIME,ACTIVE," +
                    "PROJECTION_KEY) VALUES(@id,@period,@city,@cityName," +
                    "@kingdom,@kingdomName,@color,@type,@time,1,@key)"
            };
            insert.Parameters.AddWithValue("@id", pNextCoreId++);
            insert.Parameters.AddWithValue("@period", pPeriodId);
            insert.Parameters.AddWithValue("@city", pSnapshot.CityId);
            insert.Parameters.AddWithValue("@cityName",
                pSnapshot.CityName ?? "");
            insert.Parameters.AddWithValue("@kingdom",
                pSnapshot.OriginalKingdomId);
            insert.Parameters.AddWithValue("@kingdomName",
                pSnapshot.OriginalKingdomName ?? "");
            insert.Parameters.AddWithValue("@color",
                NormalizeColor(pSnapshot.OriginalKingdomColor));
            insert.Parameters.AddWithValue("@type",
                string.IsNullOrEmpty(pSnapshot.CoreType)
                    ? "founding"
                    : pSnapshot.CoreType);
            insert.Parameters.AddWithValue("@time", pAddedTime);
            insert.Parameters.AddWithValue("@key", pProjectionKey);
            return insert.ExecuteNonQuery() == 1;
        }

        private static string NormalizeColor(string pColor)
        {
            if (string.IsNullOrEmpty(pColor)) return "";
            string color = pColor.Trim();
            if (string.IsNullOrEmpty(color)) return "";
            return color[0] == '#' ? color : "#" + color;
        }

        private static int CountCores(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pPeriodId)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT COUNT(*) FROM MandateCoreCity " +
                              "WHERE PERIOD_ID=@period AND ACTIVE=1"
            };
            command.Parameters.AddWithValue("@period", pPeriodId);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static void UpdatePeriodCount(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pPeriodId, int pCount)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "UPDATE MandatePeriod SET " +
                    "LEGAL_CORE_COUNT=@count WHERE PERIOD_ID=@period"
            };
            command.Parameters.AddWithValue("@count", pCount);
            command.Parameters.AddWithValue("@period", pPeriodId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "mandate legal core expected one period");
        }

        private static void UpdateCurrentStateCount(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pStateId, long pPeriodId,
            int pCount, double pUpdatedTime)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "UPDATE MandateState SET " +
                    "ORIGINAL_CORE_COUNT=@count,UPDATED_TIME=@time " +
                    "WHERE STATE_ID=@state AND PERIOD_ID=@period AND ACTIVE=1"
            };
            command.Parameters.AddWithValue("@count", pCount);
            command.Parameters.AddWithValue("@time", pUpdatedTime);
            command.Parameters.AddWithValue("@state", pStateId);
            command.Parameters.AddWithValue("@period", pPeriodId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "mandate legal core expected one current state");
        }
    }
}
