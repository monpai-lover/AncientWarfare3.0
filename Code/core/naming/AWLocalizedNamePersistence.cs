using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNamePersistence
    {
        private static string Table =>
            LocalizedNameIdentityTableItem.GetTableName();

        internal static AWLocalizedNameIdentitySnapshot Capture(
            BaseSystemData pData)
        {
            if (pData == null)
                return new AWLocalizedNameIdentitySnapshot("", "", "", "",
                    "", -1L, 0);
            pData.get(AWNameDataKeys.NativeName, out string nativeName,
                string.Empty);
            pData.get(AWNameDataKeys.ChineseName, out string chineseName,
                string.Empty);
            pData.get(AWNameDataKeys.GivenName, out string givenName,
                string.Empty);
            pData.get(AWNameDataKeys.FamilyComponent,
                out string familyComponent, string.Empty);
            pData.get(AWNameDataKeys.GeneratorId, out string generatorId,
                string.Empty);
            pData.get(AWNameDataKeys.CultureId, out long cultureId, -1L);
            pData.get(AWNameDataKeys.NamingSchemaVersion,
                out int schemaVersion, 0);
            return new AWLocalizedNameIdentitySnapshot(nativeName, chineseName,
                givenName, familyComponent, generatorId, cultureId,
                schemaVersion);
        }

        internal static void Apply(BaseSystemData pData,
            AWLocalizedNameIdentitySnapshot pIdentity)
        {
            if (pData == null || pIdentity == null) return;
            pData.set(AWNameDataKeys.NativeName, pIdentity.NativeName ??
                string.Empty);
            pData.set(AWNameDataKeys.ChineseName, pIdentity.ChineseName ??
                string.Empty);
            pData.set(AWNameDataKeys.GivenName, pIdentity.GivenName ??
                string.Empty);
            pData.set(AWNameDataKeys.FamilyComponent,
                pIdentity.FamilyComponent ?? string.Empty);
            pData.set(AWNameDataKeys.GeneratorId, pIdentity.GeneratorId ??
                string.Empty);
            pData.set(AWNameDataKeys.CultureId, pIdentity.CultureId);
            pData.set(AWNameDataKeys.NamingSchemaVersion,
                pIdentity.SchemaVersion);
            AWLocalizedNameService.ProjectStored(pData);
        }

        internal static bool TryLoad(string pMetaType, long pObjectId,
            out AWLocalizedNameIdentitySnapshot pIdentity)
        {
            pIdentity = null;
            SQLiteConnection db = GetDb();
            if (db == null || pObjectId < 0 ||
                !LocalizedNameIdentitySchema.TryNormalizeMetaType(pMetaType,
                    out string metaType)) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT NATIVE_NAME,CHINESE_NAME," +
                    "GIVEN_NAME,FAMILY_COMPONENT,GENERATOR_ID,CULTURE_ID," +
                    "SCHEMA_VERSION FROM " + Table +
                    " WHERE META_TYPE=@type AND OBJECT_ID=@object LIMIT 1";
                command.Parameters.AddWithValue("@type", metaType);
                command.Parameters.AddWithValue("@object", pObjectId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pIdentity = new AWLocalizedNameIdentitySnapshot(
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.IsDBNull(5) ? -1L : reader.GetInt64(5),
                    reader.IsDBNull(6) ? 0 :
                        Convert.ToInt32(reader.GetInt64(6)));
                return true;
            }
            catch { return false; }
        }

        internal static bool Upsert(string pMetaType, long pObjectId,
            BaseSystemData pData)
        {
            if (pData == null) return false;
            return Upsert(pMetaType, pObjectId, Capture(pData));
        }

        internal static bool Upsert(string pMetaType, long pObjectId,
            AWLocalizedNameIdentitySnapshot pIdentity)
        {
            SQLiteConnection db = GetDb();
            if (db == null || pObjectId < 0 || pIdentity == null ||
                !LocalizedNameIdentitySchema.TryNormalizeMetaType(pMetaType,
                    out string metaType)) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "INSERT INTO " + Table +
                    " (IDENTITY_KEY,META_TYPE,OBJECT_ID,NATIVE_NAME," +
                    "CHINESE_NAME,GIVEN_NAME,FAMILY_COMPONENT,GENERATOR_ID," +
                    "CULTURE_ID,SCHEMA_VERSION,UPDATED_TIME) VALUES " +
                    "(@key,@type,@object,@native,@chinese,@given,@family," +
                    "@generator,@culture,@schema,@time) " +
                    "ON CONFLICT(META_TYPE,OBJECT_ID) DO UPDATE SET " +
                    "IDENTITY_KEY=excluded.IDENTITY_KEY," +
                    "NATIVE_NAME=excluded.NATIVE_NAME," +
                    "CHINESE_NAME=excluded.CHINESE_NAME," +
                    "GIVEN_NAME=excluded.GIVEN_NAME," +
                    "FAMILY_COMPONENT=excluded.FAMILY_COMPONENT," +
                    "GENERATOR_ID=excluded.GENERATOR_ID," +
                    "CULTURE_ID=excluded.CULTURE_ID," +
                    "SCHEMA_VERSION=excluded.SCHEMA_VERSION," +
                    "UPDATED_TIME=excluded.UPDATED_TIME";
                command.Parameters.AddWithValue("@key", IdentityKey(metaType,
                    pObjectId));
                command.Parameters.AddWithValue("@type", metaType);
                command.Parameters.AddWithValue("@object", pObjectId);
                command.Parameters.AddWithValue("@native", pIdentity.NativeName ??
                    string.Empty);
                command.Parameters.AddWithValue("@chinese",
                    pIdentity.ChineseName ?? string.Empty);
                command.Parameters.AddWithValue("@given", pIdentity.GivenName ??
                    string.Empty);
                command.Parameters.AddWithValue("@family",
                    pIdentity.FamilyComponent ?? string.Empty);
                command.Parameters.AddWithValue("@generator",
                    pIdentity.GeneratorId ?? string.Empty);
                command.Parameters.AddWithValue("@culture", pIdentity.CultureId);
                command.Parameters.AddWithValue("@schema", pIdentity.SchemaVersion);
                command.Parameters.AddWithValue("@time",
                    World.world?.getCurWorldTime() ?? 0d);
                return command.ExecuteNonQuery() == 1;
            }
            catch { return false; }
        }

        internal static string IdentityKey(string pMetaType, long pObjectId)
        {
            return LocalizedNameIdentitySchema.TryNormalizeMetaType(pMetaType,
                out string metaType)
                ? metaType + ":" + pObjectId
                : string.Empty;
        }

        private static SQLiteConnection GetDb()
        {
            LineageArchiveManager manager = LineageArchiveManager.Instance;
            return manager != null && manager.IsOperational
                ? manager.OperatingDB
                : null;
        }
    }
}
