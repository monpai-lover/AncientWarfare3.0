using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;

namespace AncientWarfare3.core.db
{
    internal static class LocalizedNameIdentitySchema
    {
        internal const string TableName = "LocalizedNameIdentity";
        internal const string LegacyBackupTableName =
            "LocalizedNameIdentity_LegacyIdentityKeyPrimaryKey";
        private const string CurrentRepairTableName =
            "LocalizedNameIdentity_CurrentSchemaRepair";

        private static readonly Dictionary<string, string> RequiredColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["IDENTITY_KEY"] = "TEXT",
                ["META_TYPE"] = "TEXT",
                ["OBJECT_ID"] = "INTEGER",
                ["NATIVE_NAME"] = "TEXT",
                ["CHINESE_NAME"] = "TEXT",
                ["GIVEN_NAME"] = "TEXT",
                ["FAMILY_COMPONENT"] = "TEXT",
                ["GENERATOR_ID"] = "TEXT",
                ["CULTURE_ID"] = "INTEGER",
                ["SCHEMA_VERSION"] = "INTEGER",
                ["UPDATED_TIME"] = "REAL"
            };

        private static readonly Dictionary<string, string> MetaTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Unit"] = "Unit",
                ["City"] = "City",
                ["Kingdom"] = "Kingdom",
                ["Clan"] = "Clan",
                ["Culture"] = "Culture",
                ["Language"] = "Language",
                ["Religion"] = "Religion",
                ["Subspecies"] = "Subspecies",
                ["Alliance"] = "Alliance",
                ["War"] = "War",
                ["Book"] = "Book",
                ["Item"] = "Item"
            };

        internal static void Ensure(SQLiteConnection pDb)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));

            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                if (!TableExists(pDb, transaction, TableName))
                {
                    CreateCurrentTable(pDb, transaction);
                    transaction.Commit();
                    return;
                }

                Dictionary<string, int> primaryKey = ReadPrimaryKey(pDb,
                    transaction, TableName);
                if (IsCurrentPrimaryKey(primaryKey))
                {
                    if (!HasCurrentContract(pDb, transaction))
                        RepairCurrentTable(pDb, transaction);
                    transaction.Commit();
                    return;
                }

                if (!IsLegacyPrimaryKey(primaryKey))
                    throw new InvalidOperationException(
                        "Localized-name identity table has an unsupported " +
                        "primary key.");
                if (TableExists(pDb, transaction, LegacyBackupTableName))
                    throw new InvalidOperationException(
                        "Localized-name identity legacy backup already exists.");

                Execute(pDb, transaction, "ALTER TABLE " + TableName +
                    " RENAME TO " + LegacyBackupTableName);
                CreateCurrentTable(pDb, transaction);
                CopyRows(pDb, transaction, LegacyBackupTableName);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch { }
                throw;
            }
        }

        internal static bool TryNormalizeMetaType(string pMetaType,
            out string pNormalized)
        {
            string candidate = (pMetaType ?? string.Empty).Trim();
            if (MetaTypes.TryGetValue(candidate, out pNormalized)) return true;
            pNormalized = string.Empty;
            return false;
        }

        private static void CreateCurrentTable(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            Execute(pDb, pTransaction,
                "CREATE TABLE " + TableName + " (" +
                "IDENTITY_KEY TEXT NOT NULL UNIQUE, " +
                "META_TYPE TEXT NOT NULL, " +
                "OBJECT_ID INTEGER NOT NULL, " +
                "NATIVE_NAME TEXT NOT NULL DEFAULT '', " +
                "CHINESE_NAME TEXT NOT NULL DEFAULT '', " +
                "GIVEN_NAME TEXT NOT NULL DEFAULT '', " +
                "FAMILY_COMPONENT TEXT NOT NULL DEFAULT '', " +
                "GENERATOR_ID TEXT NOT NULL DEFAULT '', " +
                "CULTURE_ID INTEGER NOT NULL DEFAULT -1, " +
                "SCHEMA_VERSION INTEGER NOT NULL DEFAULT 0, " +
                "UPDATED_TIME REAL NOT NULL DEFAULT -1, " +
                "PRIMARY KEY (META_TYPE, OBJECT_ID), " +
                "CHECK (OBJECT_ID >= 0), " +
                "CHECK (IDENTITY_KEY = META_TYPE || ':' || " +
                "CAST(OBJECT_ID AS TEXT)))");
        }

        private static void RepairCurrentTable(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            if (TableExists(pDb, pTransaction, CurrentRepairTableName))
                throw new InvalidOperationException(
                    "Localized-name current-schema repair table already exists.");
            Execute(pDb, pTransaction, "ALTER TABLE " + TableName +
                " RENAME TO " + CurrentRepairTableName);
            CreateCurrentTable(pDb, pTransaction);
            CopyRows(pDb, pTransaction, CurrentRepairTableName);
            Execute(pDb, pTransaction, "DROP TABLE " +
                CurrentRepairTableName);
        }

        private static void CopyRows(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pSourceTable)
        {
            Dictionary<string, ColumnShape> columns = ReadColumns(pDb,
                pTransaction, pSourceTable);
            if (!columns.ContainsKey("META_TYPE") ||
                !columns.ContainsKey("OBJECT_ID"))
                throw new InvalidOperationException(
                    "Localized-name identity source is missing identity " +
                    "columns.");

            var winners = new Dictionary<string, LegacyRow>(
                StringComparer.Ordinal);
            bool hasSourceRowId = !NormalizeSql(ReadCreateSql(pDb,
                pTransaction, pSourceTable)).Contains("withoutrowid");
            using (var command = new SQLiteCommand(
                       BuildCopyQuery(pSourceTable, columns, hasSourceRowId),
                       pDb,
                       pTransaction))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string sourceType = ReadString(reader, 1);
                    if (!TryNormalizeMetaType(sourceType,
                            out string metaType))
                        throw new InvalidOperationException(
                            "Unsupported localized-name meta type: " +
                            sourceType);
                    long objectId = ReadLong(reader, 2, -1L);
                    if (objectId < 0)
                        throw new InvalidOperationException(
                            "Localized-name object id must be non-negative.");

                    string key = metaType + ":" + objectId;
                    var candidate = new LegacyRow
                    {
                        IdentityKey = key,
                        SourceIdentityKey = ReadString(reader, 0),
                        SourceMetaType = sourceType,
                        SourceRowId = ReadLong(reader, 11, long.MaxValue),
                        MetaType = metaType,
                        ObjectId = objectId,
                        NativeName = ReadString(reader, 3),
                        ChineseName = ReadString(reader, 4),
                        GivenName = ReadString(reader, 5),
                        FamilyComponent = ReadString(reader, 6),
                        GeneratorId = ReadString(reader, 7),
                        CultureId = ReadLong(reader, 8, -1L),
                        SchemaVersion = ReadLong(reader, 9, 0L),
                        UpdatedTime = ReadDouble(reader, 10, -1d)
                    };
                    if (!winners.TryGetValue(key, out LegacyRow winner) ||
                        IsPreferred(candidate, winner))
                        winners[key] = candidate;
                }
            }

            var ordered = new List<LegacyRow>(winners.Values);
            ordered.Sort((pLeft, pRight) => string.CompareOrdinal(
                pLeft.IdentityKey, pRight.IdentityKey));
            foreach (LegacyRow row in ordered)
                Insert(pDb, pTransaction, row);
        }

        private static string BuildCopyQuery(string pSourceTable,
            Dictionary<string, ColumnShape> pColumns, bool pHasSourceRowId)
        {
            string identity = TextExpression(pColumns, "IDENTITY_KEY");
            string updated = NumberExpression(pColumns, "UPDATED_TIME", "-1");
            return "SELECT " + identity + " AS IDENTITY_KEY," +
                   "META_TYPE,OBJECT_ID," +
                   TextExpression(pColumns, "NATIVE_NAME") + "," +
                   TextExpression(pColumns, "CHINESE_NAME") + "," +
                   TextExpression(pColumns, "GIVEN_NAME") + "," +
                   TextExpression(pColumns, "FAMILY_COMPONENT") + "," +
                   TextExpression(pColumns, "GENERATOR_ID") + "," +
                   NumberExpression(pColumns, "CULTURE_ID", "-1") + "," +
                   NumberExpression(pColumns, "SCHEMA_VERSION", "0") + "," +
                   updated + " AS UPDATED_TIME," +
                   (pHasSourceRowId ? "rowid" : "9223372036854775807") +
                   " AS SOURCE_ROWID FROM " + pSourceTable;
        }

        private static bool IsPreferred(LegacyRow pCandidate,
            LegacyRow pWinner)
        {
            int updated = pCandidate.UpdatedTime.CompareTo(pWinner.UpdatedTime);
            if (updated != 0) return updated > 0;
            int identity = string.CompareOrdinal(pCandidate.SourceIdentityKey,
                pWinner.SourceIdentityKey);
            if (identity != 0) return identity < 0;
            int metaType = string.CompareOrdinal(pCandidate.SourceMetaType,
                pWinner.SourceMetaType);
            if (metaType != 0) return metaType < 0;
            return pCandidate.SourceRowId < pWinner.SourceRowId;
        }

        private static string TextExpression(
            Dictionary<string, ColumnShape> pColumns, string pColumn)
        {
            return pColumns.ContainsKey(pColumn)
                ? "COALESCE(" + pColumn + ",'')"
                : "''";
        }

        private static string NumberExpression(
            Dictionary<string, ColumnShape> pColumns, string pColumn,
            string pDefault)
        {
            return pColumns.ContainsKey(pColumn)
                ? "COALESCE(" + pColumn + "," + pDefault + ")"
                : pDefault;
        }

        private static void Insert(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, LegacyRow pRow)
        {
            using var command = new SQLiteCommand(
                "INSERT INTO " + TableName +
                " (IDENTITY_KEY,META_TYPE,OBJECT_ID,NATIVE_NAME," +
                "CHINESE_NAME,GIVEN_NAME,FAMILY_COMPONENT,GENERATOR_ID," +
                "CULTURE_ID,SCHEMA_VERSION,UPDATED_TIME) VALUES " +
                "(@key,@type,@object,@native,@chinese,@given,@family," +
                "@generator,@culture,@schema,@time)", pDb, pTransaction);
            command.Parameters.AddWithValue("@key", pRow.IdentityKey);
            command.Parameters.AddWithValue("@type", pRow.MetaType);
            command.Parameters.AddWithValue("@object", pRow.ObjectId);
            command.Parameters.AddWithValue("@native", pRow.NativeName);
            command.Parameters.AddWithValue("@chinese", pRow.ChineseName);
            command.Parameters.AddWithValue("@given", pRow.GivenName);
            command.Parameters.AddWithValue("@family", pRow.FamilyComponent);
            command.Parameters.AddWithValue("@generator", pRow.GeneratorId);
            command.Parameters.AddWithValue("@culture", pRow.CultureId);
            command.Parameters.AddWithValue("@schema", pRow.SchemaVersion);
            command.Parameters.AddWithValue("@time", pRow.UpdatedTime);
            command.ExecuteNonQuery();
        }

        private static Dictionary<string, int> ReadPrimaryKey(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            string pTableName)
        {
            var result = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            using var command = new SQLiteCommand(
                "PRAGMA table_info('" + pTableName + "')", pDb,
                pTransaction);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int position = Convert.ToInt32(reader["pk"]);
                if (position > 0)
                    result[Convert.ToString(reader["name"])] = position;
            }
            return result;
        }

        private static bool HasCurrentContract(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            Dictionary<string, ColumnShape> columns = ReadColumns(pDb,
                pTransaction, TableName);
            foreach (KeyValuePair<string, string> required in RequiredColumns)
            {
                if (!columns.TryGetValue(required.Key,
                        out ColumnShape actual) || !actual.NotNull ||
                    !string.Equals(actual.Type, required.Value,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!HasDedicatedIdentityKeyUniqueIndex(pDb, pTransaction))
                return false;
            string createSql = NormalizeSql(ReadCreateSql(pDb, pTransaction,
                TableName));
            return createSql.Contains("check(object_id>=0)") &&
                   createSql.Contains("check(identity_key=meta_type||':'||" +
                                      "cast(object_idastext))");
        }

        private static Dictionary<string, ColumnShape> ReadColumns(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            string pTableName)
        {
            var result = new Dictionary<string, ColumnShape>(
                StringComparer.OrdinalIgnoreCase);
            using var command = new SQLiteCommand(
                "PRAGMA table_info('" + pTableName + "')", pDb,
                pTransaction);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string name = Convert.ToString(reader["name"]);
                result[name] = new ColumnShape
                {
                    Type = Convert.ToString(reader["type"]),
                    NotNull = Convert.ToInt32(reader["notnull"]) == 1
                };
            }
            return result;
        }

        private static bool HasDedicatedIdentityKeyUniqueIndex(
            SQLiteConnection pDb, SQLiteTransaction pTransaction)
        {
            var uniqueIndexes = new List<string>();
            using (var command = new SQLiteCommand(
                       "PRAGMA index_list('" + TableName + "')", pDb,
                       pTransaction))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (Convert.ToInt32(reader["unique"]) == 1 &&
                        Convert.ToInt32(reader["partial"]) == 0)
                        uniqueIndexes.Add(Convert.ToString(reader["name"]));
                }
            }

            foreach (string indexName in uniqueIndexes)
            {
                var columns = new List<string>();
                using var command = new SQLiteCommand(
                    "PRAGMA index_info('" + indexName.Replace("'", "''") +
                    "')", pDb, pTransaction);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    columns.Add(Convert.ToString(reader["name"]));
                if (columns.Count == 1 && string.Equals(columns[0],
                        "IDENTITY_KEY", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string ReadCreateSql(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTableName)
        {
            using var command = new SQLiteCommand(
                "SELECT sql FROM sqlite_master WHERE type='table' AND " +
                "name=@name", pDb, pTransaction);
            command.Parameters.AddWithValue("@name", pTableName);
            return Convert.ToString(command.ExecuteScalar());
        }

        private static string NormalizeSql(string pSql)
        {
            var result = new StringBuilder();
            foreach (char value in pSql ?? string.Empty)
            {
                if (char.IsWhiteSpace(value) || value == '"' ||
                    value == '[' || value == ']') continue;
                result.Append(char.ToLowerInvariant(value));
            }
            return result.ToString();
        }

        private static bool IsCurrentPrimaryKey(
            Dictionary<string, int> pPrimaryKey)
        {
            return pPrimaryKey.Count == 2 &&
                   pPrimaryKey.TryGetValue("META_TYPE", out int meta) &&
                   meta == 1 &&
                   pPrimaryKey.TryGetValue("OBJECT_ID", out int objectId) &&
                   objectId == 2;
        }

        private static bool IsLegacyPrimaryKey(
            Dictionary<string, int> pPrimaryKey)
        {
            return pPrimaryKey.Count == 1 &&
                   pPrimaryKey.TryGetValue("IDENTITY_KEY", out int key) &&
                   key == 1;
        }

        private static bool TableExists(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTableName)
        {
            using var command = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' " +
                "AND name=@name", pDb, pTransaction);
            command.Parameters.AddWithValue("@name", pTableName);
            return Convert.ToInt64(command.ExecuteScalar()) == 1L;
        }

        private static void Execute(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pSql)
        {
            using var command = new SQLiteCommand(pSql, pDb, pTransaction);
            command.ExecuteNonQuery();
        }

        private static string ReadString(SQLiteDataReader pReader,
            int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal)
                ? string.Empty
                : Convert.ToString(pReader[pOrdinal]);
        }

        private static long ReadLong(SQLiteDataReader pReader, int pOrdinal,
            long pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToInt64(pReader[pOrdinal]);
        }

        private static double ReadDouble(SQLiteDataReader pReader,
            int pOrdinal, double pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToDouble(pReader[pOrdinal]);
        }

        private sealed class LegacyRow
        {
            internal string IdentityKey;
            internal string SourceIdentityKey;
            internal string SourceMetaType;
            internal long SourceRowId;
            internal string MetaType;
            internal long ObjectId;
            internal string NativeName;
            internal string ChineseName;
            internal string GivenName;
            internal string FamilyComponent;
            internal string GeneratorId;
            internal long CultureId;
            internal long SchemaVersion;
            internal double UpdatedTime;
        }

        private sealed class ColumnShape
        {
            internal string Type;
            internal bool NotNull;
        }
    }
}
