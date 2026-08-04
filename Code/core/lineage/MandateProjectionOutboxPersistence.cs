using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class MandateProjectionOutboxPersistence
    {
        public const string Table = "MandateProjectionOutbox";
        public const string EffectTable = "MandateProjectionEffect";
        public const string CoreSnapshotTable =
            "MandateProjectionCoreSnapshot";

        public sealed class CoreCitySnapshot
        {
            public long CityId;
            public string CityName = "";
            public long OriginalKingdomId;
            public string OriginalKingdomName = "";
            public string OriginalKingdomColor = "";
            public string CoreType = "founding";
            public string SnapshotSource = "declaration";
        }

        public sealed class PendingProjection
        {
            public string OperationKey = "";
            public long PeriodId;
            public long KingdomId;
            public string KingdomName = "";
            public string KingdomColor = "";
            public string DynastyName = "";
            public long RulerActorId;
            public string RulerName = "";
            public long PreviousPeriodId = -1L;
            public long PreviousKingdomId = -1L;
            public string PreviousKingdomName = "";
            public string PreviousKingdomColor = "";
            public long PreviousRulerActorId = -1L;
            public string PreviousRulerName = "";
            public int PreviousMandateValue;
            public string PreviousEndReason = "replaced";
            public bool OldEndRequired;
            public int CurrentYear;
            public bool WasAlreadyEmperor;
            public string OriginType = "native";
            public string ClaimantKind = "orthodox";
            public string MapMarkerKind = "moh";
            public string NewYearPrefix = "";
            public string NewYearPrefixRich = "";
            public string PreviousYearPrefix = "";
            public string PreviousYearPrefixRich = "";
            public double CreatedTime;
            public string CoreSnapshotSource = "";
            public List<CoreCitySnapshot> CoreCitySnapshots = new();
        }

        private const string PendingColumns =
            "OPERATION_KEY,PERIOD_ID,KINGDOM_ID,KINGDOM_NAME," +
            "KINGDOM_COLOR,DYNASTY_NAME,RULER_ACTOR_ID,RULER_NAME," +
            "PREVIOUS_PERIOD_ID,PREVIOUS_KINGDOM_ID," +
            "PREVIOUS_KINGDOM_NAME,PREVIOUS_KINGDOM_COLOR," +
            "PREVIOUS_RULER_ACTOR_ID,PREVIOUS_RULER_NAME," +
            "PREVIOUS_MANDATE_VALUE,PREVIOUS_END_REASON," +
            "OLD_END_REQUIRED,CURRENT_YEAR,WAS_ALREADY_EMPEROR," +
            "ORIGIN_TYPE,CLAIMANT_KIND,MAP_MARKER_KIND," +
            "NEW_YEAR_PREFIX,NEW_YEAR_PREFIX_RICH," +
            "PREVIOUS_YEAR_PREFIX,PREVIOUS_YEAR_PREFIX_RICH,CREATED_TIME," +
            "CORE_SNAPSHOT_SOURCE";

        public static IReadOnlyList<string> RequiredEffects(
            bool pOldEndRequired)
        {
            var effects = new List<string>();
            if (pOldEndRequired)
            {
                effects.Add("old_runtime");
                effects.Add("old_revision");
                effects.Add("old_kingdom_history");
                effects.Add("old_mandate_event");
            }
            effects.Add("new_runtime");
            effects.Add("new_revision");
            effects.Add("new_mandate_event");
            effects.Add("new_kingdom_history");
            effects.Add("new_person_history");
            effects.Add("legal_cores");
            effects.Add("new_maps");
            return effects;
        }

        public static bool TryEnqueue(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, PendingProjection pPending,
            out string pError)
        {
            pError = "";
            if (pDb == null || pTransaction == null || pPending == null ||
                string.IsNullOrWhiteSpace(pPending.OperationKey) ||
                pPending.PeriodId < 0L || pPending.KingdomId < 0L ||
                pPending.RulerActorId < 0L)
            {
                pError = "invalid mandate projection outbox input";
                return false;
            }
            try
            {
                EnsureSchema(pDb, pTransaction);
                using var command = new SQLiteCommand(pDb)
                {
                    Transaction = pTransaction,
                    CommandText = "INSERT INTO " + Table + "(" +
                        PendingColumns + ") VALUES(@operation,@period," +
                        "@kingdom,@kingdomName,@kingdomColor,@dynasty," +
                        "@actor,@rulerName,@previousPeriod,@previousKingdom," +
                        "@previousKingdomName,@previousKingdomColor," +
                        "@previousActor,@previousRulerName," +
                        "@previousMandate,@previousReason,@oldRequired," +
                        "@year,@wasEmperor,@origin,@claimant,@marker," +
                        "@newYear,@newYearRich,@previousYear," +
                        "@previousYearRich,@time,@coreSnapshotSource)"
                };
                AddPendingParameters(command, pPending);
                if (command.ExecuteNonQuery() != 1)
                {
                    pError = "mandate projection outbox expected one row";
                    return false;
                }
                foreach (CoreCitySnapshot snapshot in
                         pPending.CoreCitySnapshots)
                    InsertCoreSnapshot(pDb, pTransaction,
                        pPending.OperationKey, pPending.CoreSnapshotSource,
                        snapshot);
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        public static bool TryReadPending(SQLiteConnection pDb,
            long pPeriodId, out PendingProjection pPending,
            out string pError)
        {
            pPending = null;
            pError = "";
            if (pDb == null || pPeriodId < 0L)
            {
                pError = "invalid pending mandate projection lookup";
                return false;
            }
            try
            {
                using (SQLiteTransaction transaction = pDb.BeginTransaction())
                {
                    EnsureSchema(pDb, transaction);
                    transaction.Commit();
                }
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "SELECT " + PendingColumns + " FROM " + Table +
                        " WHERE PERIOD_ID=@period LIMIT 1"
                };
                command.Parameters.AddWithValue("@period", pPeriodId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return true;
                    pPending = ReadPending(reader);
                }
                ReadCoreSnapshots(pDb, pPending);
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        public static bool TryReadPendingBatch(SQLiteConnection pDb,
            int pLimit, out List<PendingProjection> pPending,
            out string pError)
        {
            pPending = new List<PendingProjection>();
            pError = "";
            if (pDb == null || pLimit <= 0)
            {
                pError = "invalid pending mandate projection batch lookup";
                return false;
            }
            try
            {
                using (SQLiteTransaction transaction = pDb.BeginTransaction())
                {
                    EnsureSchema(pDb, transaction);
                    transaction.Commit();
                }
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "SELECT " + PendingColumns + " FROM " +
                        Table + " ORDER BY CREATED_TIME,PERIOD_ID," +
                        "OPERATION_KEY LIMIT @limit"
                };
                command.Parameters.AddWithValue("@limit", pLimit);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read()) pPending.Add(ReadPending(reader));
                }
                foreach (PendingProjection item in pPending)
                    ReadCoreSnapshots(pDb, item);
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        public static bool TryResumePendingBatch(SQLiteConnection pDb,
            int pLimit, Func<PendingProjection, string, bool> pPublishEffect,
            out int pAttempted, out int pCompleted, out string pError)
        {
            pAttempted = 0;
            pCompleted = 0;
            pError = "";
            if (pPublishEffect == null)
            {
                pError = "pending mandate projection publisher is unavailable";
                return false;
            }
            if (!TryReadPendingBatch(pDb, pLimit,
                    out List<PendingProjection> pending, out pError))
                return false;

            bool success = true;
            foreach (PendingProjection item in pending)
            {
                pAttempted++;
                bool drained = TryDrain(pDb, item.OperationKey,
                    effect => pPublishEffect(item, effect),
                    out bool complete, out string drainError);
                if (drained && complete)
                {
                    pCompleted++;
                    continue;
                }
                success = false;
                if (string.IsNullOrEmpty(pError)) pError = drainError;
            }
            return success;
        }

        public static bool TryDrain(SQLiteConnection pDb,
            string pOperationKey, Func<string, bool> pPublishEffect,
            out bool pComplete, out string pError)
        {
            pComplete = false;
            pError = "";
            if (pDb == null || string.IsNullOrWhiteSpace(pOperationKey) ||
                pPublishEffect == null)
            {
                pError = "invalid mandate projection outbox drain input";
                return false;
            }
            try
            {
                PendingProjection pending = ReadByOperation(
                    pDb, pOperationKey);
                if (pending == null)
                {
                    pComplete = true;
                    return true;
                }
                foreach (string effect in RequiredEffects(
                             pending.OldEndRequired))
                {
                    if (EffectCompleted(pDb, pOperationKey, effect))
                        continue;
                    bool published;
                    try { published = pPublishEffect(effect); }
                    catch (Exception error)
                    {
                        pError = error.Message;
                        return false;
                    }
                    if (!published)
                    {
                        pError = "mandate projection effect failed: " + effect;
                        return false;
                    }
                    if (!MarkEffectCompleted(pDb, pOperationKey, effect,
                            out pError))
                        return false;
                }

                using SQLiteTransaction cleanup = pDb.BeginTransaction();
                using (var deletePending = new SQLiteCommand(pDb)
                {
                    Transaction = cleanup,
                    CommandText = "DELETE FROM " + Table +
                                  " WHERE OPERATION_KEY=@operation"
                })
                {
                    deletePending.Parameters.AddWithValue("@operation",
                        pOperationKey);
                    if (deletePending.ExecuteNonQuery() != 1)
                    {
                        cleanup.Rollback();
                        pError = "mandate projection pending row changed";
                        return false;
                    }
                }
                using (var deleteEffects = new SQLiteCommand(pDb)
                {
                    Transaction = cleanup,
                    CommandText = "DELETE FROM " + EffectTable +
                                  " WHERE OPERATION_KEY=@operation"
                })
                {
                    deleteEffects.Parameters.AddWithValue("@operation",
                        pOperationKey);
                    deleteEffects.ExecuteNonQuery();
                }
                using (var deleteCoreSnapshots = new SQLiteCommand(pDb)
                {
                    Transaction = cleanup,
                    CommandText = "DELETE FROM " + CoreSnapshotTable +
                                  " WHERE OPERATION_KEY=@operation"
                })
                {
                    deleteCoreSnapshots.Parameters.AddWithValue("@operation",
                        pOperationKey);
                    deleteCoreSnapshots.ExecuteNonQuery();
                }
                cleanup.Commit();
                pComplete = true;
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        public static bool TryMigrateLegacyCoreSnapshots(
            SQLiteConnection pDb, string pOperationKey,
            IReadOnlyList<CoreCitySnapshot> pSnapshots,
            out bool pMigrated, out string pError)
        {
            pMigrated = false;
            pError = "";
            if (pDb == null || string.IsNullOrWhiteSpace(pOperationKey) ||
                pSnapshots == null)
            {
                pError = "invalid legacy mandate core snapshot migration";
                return false;
            }
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                EnsureSchema(pDb, transaction);
                string source;
                using (var read = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "SELECT CORE_SNAPSHOT_SOURCE FROM " +
                                  Table +
                                  " WHERE OPERATION_KEY=@operation LIMIT 1"
                })
                {
                    read.Parameters.AddWithValue("@operation", pOperationKey);
                    object value = read.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                    {
                        transaction.Rollback();
                        pError = "legacy mandate projection is missing";
                        return false;
                    }
                    source = Convert.ToString(value) ?? "";
                }
                if (!string.IsNullOrEmpty(source))
                {
                    transaction.Commit();
                    return true;
                }
                foreach (CoreCitySnapshot snapshot in pSnapshots)
                    InsertCoreSnapshot(pDb, transaction, pOperationKey,
                        "legacy", snapshot);
                using (var mark = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "UPDATE " + Table +
                                  " SET CORE_SNAPSHOT_SOURCE='legacy' " +
                                  "WHERE OPERATION_KEY=@operation AND " +
                                  "CORE_SNAPSHOT_SOURCE=''"
                })
                {
                    mark.Parameters.AddWithValue("@operation", pOperationKey);
                    if (mark.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "legacy mandate core snapshot marker changed");
                }
                transaction.Commit();
                pMigrated = true;
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                pError = error.Message;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public static bool TryApplyIdempotentRecord(SQLiteConnection pDb,
            string pTable, string pProjectionKey,
            Func<SQLiteTransaction, bool> pApply, out string pError)
        {
            pError = "";
            if (pDb == null || !ValidIdentifier(pTable) ||
                string.IsNullOrWhiteSpace(pProjectionKey) || pApply == null)
            {
                pError = "invalid idempotent projection record input";
                return false;
            }

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                if (ProjectionRecordExists(pDb, transaction, pTable,
                        pProjectionKey))
                {
                    transaction.Commit();
                    return true;
                }
                if (!pApply(transaction))
                {
                    transaction.Rollback();
                    pError = "idempotent projection record write failed";
                    return false;
                }
                if (!ProjectionRecordExists(pDb, transaction, pTable,
                        pProjectionKey))
                {
                    transaction.Rollback();
                    pError = "idempotent projection record was not written";
                    return false;
                }
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                pError = error.Message;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static void EnsureSchema(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "CREATE TABLE IF NOT EXISTS " + Table + "(" +
                    "OPERATION_KEY TEXT PRIMARY KEY,PERIOD_ID INTEGER NOT NULL," +
                    "KINGDOM_ID INTEGER NOT NULL,RULER_ACTOR_ID INTEGER NOT NULL," +
                    "KINGDOM_NAME TEXT NOT NULL DEFAULT ''," +
                    "KINGDOM_COLOR TEXT NOT NULL DEFAULT ''," +
                    "DYNASTY_NAME TEXT NOT NULL DEFAULT ''," +
                    "RULER_NAME TEXT NOT NULL DEFAULT ''," +
                    "PREVIOUS_PERIOD_ID INTEGER NOT NULL," +
                    "PREVIOUS_KINGDOM_ID INTEGER NOT NULL," +
                    "PREVIOUS_KINGDOM_NAME TEXT NOT NULL DEFAULT ''," +
                    "PREVIOUS_KINGDOM_COLOR TEXT NOT NULL DEFAULT ''," +
                    "PREVIOUS_RULER_ACTOR_ID INTEGER NOT NULL DEFAULT -1," +
                    "PREVIOUS_RULER_NAME TEXT NOT NULL DEFAULT ''," +
                    "PREVIOUS_MANDATE_VALUE INTEGER NOT NULL," +
                    "PREVIOUS_END_REASON TEXT NOT NULL," +
                    "OLD_END_REQUIRED INTEGER NOT NULL,CURRENT_YEAR INTEGER NOT NULL," +
                    "WAS_ALREADY_EMPEROR INTEGER NOT NULL,CREATED_TIME REAL NOT NULL);" +
                    "CREATE INDEX IF NOT EXISTS IDX_MANDATE_PROJECTION_PERIOD ON " +
                    Table + "(PERIOD_ID);" +
                    "CREATE TABLE IF NOT EXISTS " + EffectTable + "(" +
                    "OPERATION_KEY TEXT NOT NULL,EFFECT_KEY TEXT NOT NULL," +
                    "COMPLETED_TIME REAL NOT NULL DEFAULT 0," +
                    "PRIMARY KEY(OPERATION_KEY,EFFECT_KEY));" +
                    "CREATE TABLE IF NOT EXISTS " + CoreSnapshotTable + "(" +
                    "OPERATION_KEY TEXT NOT NULL,CITY_ID INTEGER NOT NULL," +
                    "CITY_NAME TEXT NOT NULL DEFAULT ''," +
                    "ORIGINAL_KINGDOM_ID INTEGER NOT NULL," +
                    "ORIGINAL_KINGDOM_NAME TEXT NOT NULL DEFAULT ''," +
                    "ORIGINAL_KINGDOM_COLOR TEXT NOT NULL DEFAULT ''," +
                    "CORE_TYPE TEXT NOT NULL DEFAULT 'founding'," +
                    "SNAPSHOT_SOURCE TEXT NOT NULL DEFAULT 'declaration'," +
                    "PRIMARY KEY(OPERATION_KEY,CITY_ID));"
            };
            command.ExecuteNonQuery();
            EnsureColumn(pDb, pTransaction, "KINGDOM_NAME",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "KINGDOM_COLOR",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "DYNASTY_NAME",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "RULER_NAME",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "PREVIOUS_KINGDOM_NAME",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "PREVIOUS_KINGDOM_COLOR",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "PREVIOUS_RULER_ACTOR_ID",
                "INTEGER NOT NULL DEFAULT -1");
            EnsureColumn(pDb, pTransaction, "PREVIOUS_RULER_NAME",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "ORIGIN_TYPE",
                "TEXT NOT NULL DEFAULT 'native'");
            EnsureColumn(pDb, pTransaction, "CLAIMANT_KIND",
                "TEXT NOT NULL DEFAULT 'orthodox'");
            EnsureColumn(pDb, pTransaction, "MAP_MARKER_KIND",
                "TEXT NOT NULL DEFAULT 'moh'");
            EnsureColumn(pDb, pTransaction, "NEW_YEAR_PREFIX",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "NEW_YEAR_PREFIX_RICH",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "PREVIOUS_YEAR_PREFIX",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "PREVIOUS_YEAR_PREFIX_RICH",
                "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(pDb, pTransaction, "CORE_SNAPSHOT_SOURCE",
                "TEXT NOT NULL DEFAULT ''");
        }

        private static void EnsureColumn(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pColumn,
            string pDefinition)
        {
            bool exists = false;
            using (var pragma = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "PRAGMA table_info(" + Table + ")"
            })
            using (SQLiteDataReader reader = pragma.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!string.Equals(Convert.ToString(reader.GetValue(1)),
                            pColumn, StringComparison.OrdinalIgnoreCase))
                        continue;
                    exists = true;
                    break;
                }
            }
            if (exists) return;
            using var alter = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "ALTER TABLE " + Table + " ADD COLUMN " +
                              pColumn + " " + pDefinition
            };
            alter.ExecuteNonQuery();
        }

        private static PendingProjection ReadByOperation(SQLiteConnection pDb,
            string pOperationKey)
        {
            using var command = new SQLiteCommand(pDb)
            {
                CommandText = "SELECT " + PendingColumns + " FROM " + Table +
                    " WHERE OPERATION_KEY=@operation LIMIT 1"
            };
            command.Parameters.AddWithValue("@operation", pOperationKey);
            PendingProjection pending;
            using (SQLiteDataReader reader = command.ExecuteReader())
                pending = reader.Read() ? ReadPending(reader) : null;
            if (pending != null) ReadCoreSnapshots(pDb, pending);
            return pending;
        }

        private static PendingProjection ReadPending(SQLiteDataReader pReader)
        {
            return new PendingProjection
            {
                OperationKey = Convert.ToString(pReader.GetValue(0)) ?? "",
                PeriodId = Convert.ToInt64(pReader.GetValue(1)),
                KingdomId = Convert.ToInt64(pReader.GetValue(2)),
                KingdomName = Convert.ToString(pReader.GetValue(3)) ?? "",
                KingdomColor = Convert.ToString(pReader.GetValue(4)) ?? "",
                DynastyName = Convert.ToString(pReader.GetValue(5)) ?? "",
                RulerActorId = Convert.ToInt64(pReader.GetValue(6)),
                RulerName = Convert.ToString(pReader.GetValue(7)) ?? "",
                PreviousPeriodId = Convert.ToInt64(pReader.GetValue(8)),
                PreviousKingdomId = Convert.ToInt64(pReader.GetValue(9)),
                PreviousKingdomName = Convert.ToString(
                    pReader.GetValue(10)) ?? "",
                PreviousKingdomColor = Convert.ToString(
                    pReader.GetValue(11)) ?? "",
                PreviousRulerActorId = Convert.ToInt64(
                    pReader.GetValue(12)),
                PreviousRulerName = Convert.ToString(
                    pReader.GetValue(13)) ?? "",
                PreviousMandateValue = Convert.ToInt32(pReader.GetValue(14)),
                PreviousEndReason = Convert.ToString(
                    pReader.GetValue(15)) ?? "",
                OldEndRequired = Convert.ToInt32(pReader.GetValue(16)) == 1,
                CurrentYear = Convert.ToInt32(pReader.GetValue(17)),
                WasAlreadyEmperor = Convert.ToInt32(
                    pReader.GetValue(18)) == 1,
                OriginType = Convert.ToString(pReader.GetValue(19)) ?? "native",
                ClaimantKind = Convert.ToString(
                    pReader.GetValue(20)) ?? "orthodox",
                MapMarkerKind = Convert.ToString(pReader.GetValue(21)) ?? "moh",
                NewYearPrefix = Convert.ToString(pReader.GetValue(22)) ?? "",
                NewYearPrefixRich = Convert.ToString(
                    pReader.GetValue(23)) ?? "",
                PreviousYearPrefix = Convert.ToString(
                    pReader.GetValue(24)) ?? "",
                PreviousYearPrefixRich = Convert.ToString(
                    pReader.GetValue(25)) ?? "",
                CreatedTime = Convert.ToDouble(pReader.GetValue(26)),
                CoreSnapshotSource = Convert.ToString(
                    pReader.GetValue(27)) ?? ""
            };
        }

        private static void InsertCoreSnapshot(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pOperationKey,
            string pSnapshotSource, CoreCitySnapshot pSnapshot)
        {
            if (pSnapshot == null || pSnapshot.CityId < 0L)
                throw new InvalidOperationException(
                    "invalid mandate core snapshot");
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " + CoreSnapshotTable +
                    "(OPERATION_KEY,CITY_ID,CITY_NAME," +
                    "ORIGINAL_KINGDOM_ID,ORIGINAL_KINGDOM_NAME," +
                    "ORIGINAL_KINGDOM_COLOR,CORE_TYPE,SNAPSHOT_SOURCE) " +
                    "VALUES(@operation,@city,@cityName,@kingdom," +
                    "@kingdomName,@kingdomColor,@coreType,@source)"
            };
            command.Parameters.AddWithValue("@operation", pOperationKey);
            command.Parameters.AddWithValue("@city", pSnapshot.CityId);
            command.Parameters.AddWithValue("@cityName",
                pSnapshot.CityName ?? "");
            command.Parameters.AddWithValue("@kingdom",
                pSnapshot.OriginalKingdomId);
            command.Parameters.AddWithValue("@kingdomName",
                pSnapshot.OriginalKingdomName ?? "");
            command.Parameters.AddWithValue("@kingdomColor",
                pSnapshot.OriginalKingdomColor ?? "");
            command.Parameters.AddWithValue("@coreType",
                pSnapshot.CoreType ?? "founding");
            command.Parameters.AddWithValue("@source",
                string.IsNullOrEmpty(pSnapshotSource)
                    ? pSnapshot.SnapshotSource ?? ""
                    : pSnapshotSource);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "mandate core snapshot expected one row");
        }

        private static void ReadCoreSnapshots(SQLiteConnection pDb,
            PendingProjection pPending)
        {
            pPending.CoreCitySnapshots.Clear();
            using var command = new SQLiteCommand(pDb)
            {
                CommandText = "SELECT CITY_ID,CITY_NAME," +
                    "ORIGINAL_KINGDOM_ID,ORIGINAL_KINGDOM_NAME," +
                    "ORIGINAL_KINGDOM_COLOR,CORE_TYPE,SNAPSHOT_SOURCE FROM " +
                    CoreSnapshotTable + " WHERE OPERATION_KEY=@operation " +
                    "ORDER BY CITY_ID"
            };
            command.Parameters.AddWithValue("@operation",
                pPending.OperationKey);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                pPending.CoreCitySnapshots.Add(new CoreCitySnapshot
                {
                    CityId = Convert.ToInt64(reader.GetValue(0)),
                    CityName = Convert.ToString(reader.GetValue(1)) ?? "",
                    OriginalKingdomId = Convert.ToInt64(reader.GetValue(2)),
                    OriginalKingdomName = Convert.ToString(
                        reader.GetValue(3)) ?? "",
                    OriginalKingdomColor = Convert.ToString(
                        reader.GetValue(4)) ?? "",
                    CoreType = Convert.ToString(reader.GetValue(5)) ??
                               "founding",
                    SnapshotSource = Convert.ToString(reader.GetValue(6)) ??
                                     ""
                });
            }
        }

        private static bool EffectCompleted(SQLiteConnection pDb,
            string pOperationKey, string pEffect)
        {
            using var command = new SQLiteCommand(pDb)
            {
                CommandText = "SELECT COUNT(*) FROM " + EffectTable +
                    " WHERE OPERATION_KEY=@operation AND EFFECT_KEY=@effect"
            };
            command.Parameters.AddWithValue("@operation", pOperationKey);
            command.Parameters.AddWithValue("@effect", pEffect);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static bool ProjectionRecordExists(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable,
            string pProjectionKey)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT COUNT(*) FROM " + pTable +
                              " WHERE PROJECTION_KEY=@key"
            };
            command.Parameters.AddWithValue("@key", pProjectionKey);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static bool ValidIdentifier(string pValue)
        {
            if (string.IsNullOrEmpty(pValue)) return false;
            for (int index = 0; index < pValue.Length; index++)
            {
                char value = pValue[index];
                if (!(value == '_' || char.IsLetterOrDigit(value)))
                    return false;
            }
            return true;
        }

        private static bool MarkEffectCompleted(SQLiteConnection pDb,
            string pOperationKey, string pEffect, out string pError)
        {
            pError = "";
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "INSERT OR IGNORE INTO " + EffectTable +
                        "(OPERATION_KEY,EFFECT_KEY,COMPLETED_TIME) " +
                        "VALUES(@operation,@effect,strftime('%s','now'))"
                };
                command.Parameters.AddWithValue("@operation", pOperationKey);
                command.Parameters.AddWithValue("@effect", pEffect);
                command.ExecuteNonQuery();
                return EffectCompleted(pDb, pOperationKey, pEffect);
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        private static void AddPendingParameters(SQLiteCommand pCommand,
            PendingProjection pPending)
        {
            pCommand.Parameters.AddWithValue("@operation",
                pPending.OperationKey);
            pCommand.Parameters.AddWithValue("@period", pPending.PeriodId);
            pCommand.Parameters.AddWithValue("@kingdom", pPending.KingdomId);
            pCommand.Parameters.AddWithValue("@kingdomName",
                pPending.KingdomName ?? "");
            pCommand.Parameters.AddWithValue("@kingdomColor",
                pPending.KingdomColor ?? "");
            pCommand.Parameters.AddWithValue("@dynasty",
                pPending.DynastyName ?? "");
            pCommand.Parameters.AddWithValue("@actor", pPending.RulerActorId);
            pCommand.Parameters.AddWithValue("@rulerName",
                pPending.RulerName ?? "");
            pCommand.Parameters.AddWithValue("@previousPeriod",
                pPending.PreviousPeriodId);
            pCommand.Parameters.AddWithValue("@previousKingdom",
                pPending.PreviousKingdomId);
            pCommand.Parameters.AddWithValue("@previousKingdomName",
                pPending.PreviousKingdomName ?? "");
            pCommand.Parameters.AddWithValue("@previousKingdomColor",
                pPending.PreviousKingdomColor ?? "");
            pCommand.Parameters.AddWithValue("@previousActor",
                pPending.PreviousRulerActorId);
            pCommand.Parameters.AddWithValue("@previousRulerName",
                pPending.PreviousRulerName ?? "");
            pCommand.Parameters.AddWithValue("@previousMandate",
                pPending.PreviousMandateValue);
            pCommand.Parameters.AddWithValue("@previousReason",
                pPending.PreviousEndReason ?? "replaced");
            pCommand.Parameters.AddWithValue("@oldRequired",
                pPending.OldEndRequired ? 1 : 0);
            pCommand.Parameters.AddWithValue("@year", pPending.CurrentYear);
            pCommand.Parameters.AddWithValue("@wasEmperor",
                pPending.WasAlreadyEmperor ? 1 : 0);
            pCommand.Parameters.AddWithValue("@origin",
                pPending.OriginType ?? "native");
            pCommand.Parameters.AddWithValue("@claimant",
                pPending.ClaimantKind ?? "orthodox");
            pCommand.Parameters.AddWithValue("@marker",
                pPending.MapMarkerKind ?? "moh");
            pCommand.Parameters.AddWithValue("@newYear",
                pPending.NewYearPrefix ?? "");
            pCommand.Parameters.AddWithValue("@newYearRich",
                pPending.NewYearPrefixRich ?? "");
            pCommand.Parameters.AddWithValue("@previousYear",
                pPending.PreviousYearPrefix ?? "");
            pCommand.Parameters.AddWithValue("@previousYearRich",
                pPending.PreviousYearPrefixRich ?? "");
            pCommand.Parameters.AddWithValue("@time", pPending.CreatedTime);
            pCommand.Parameters.AddWithValue("@coreSnapshotSource",
                pPending.CoreSnapshotSource ?? "");
        }
    }
}
