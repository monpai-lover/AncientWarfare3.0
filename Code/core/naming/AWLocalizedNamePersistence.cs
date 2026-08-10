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

        internal static bool Apply(BaseSystemData pData,
            AWLocalizedNameIdentitySnapshot pIdentity)
        {
            if (pData == null || pIdentity == null) return false;
            string before = pData.name ?? string.Empty;
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
            return AWLocalizedNameProjectionChangeRules.ShouldInvalidate(
                before, pData.name);
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
                using SQLiteTransaction transaction = db.BeginTransaction();
                using var command = new SQLiteCommand(db)
                {
                    Transaction = transaction
                };
                AddIdentityParameters(command, metaType, pObjectId,
                    pIdentity);
                command.CommandText = "UPDATE " + Table + " SET " +
                    "IDENTITY_KEY=@key,NATIVE_NAME=@native," +
                    "CHINESE_NAME=@chinese,GIVEN_NAME=@given," +
                    "FAMILY_COMPONENT=@family,GENERATOR_ID=@generator," +
                    "CULTURE_ID=@culture,SCHEMA_VERSION=@schema," +
                    "UPDATED_TIME=@time WHERE META_TYPE=@type AND " +
                    "OBJECT_ID=@object";
                int updated = command.ExecuteNonQuery();
                if (updated == 0)
                {
                    command.CommandText = "INSERT INTO " + Table +
                        " (IDENTITY_KEY,META_TYPE,OBJECT_ID,NATIVE_NAME," +
                        "CHINESE_NAME,GIVEN_NAME,FAMILY_COMPONENT,GENERATOR_ID," +
                        "CULTURE_ID,SCHEMA_VERSION,UPDATED_TIME) VALUES " +
                        "(@key,@type,@object,@native,@chinese,@given,@family," +
                        "@generator,@culture,@schema,@time)";
                    if (command.ExecuteNonQuery() != 1) return false;
                }
                transaction.Commit();
                return updated == 1 || updated == 0;
            }
            catch { return false; }
        }

        private static void AddIdentityParameters(SQLiteCommand pCommand,
            string pMetaType, long pObjectId,
            AWLocalizedNameIdentitySnapshot pIdentity)
        {
            pCommand.Parameters.AddWithValue("@key", IdentityKey(pMetaType,
                pObjectId));
            pCommand.Parameters.AddWithValue("@type", pMetaType);
            pCommand.Parameters.AddWithValue("@object", pObjectId);
            pCommand.Parameters.AddWithValue("@native", pIdentity.NativeName ??
                string.Empty);
            pCommand.Parameters.AddWithValue("@chinese",
                pIdentity.ChineseName ?? string.Empty);
            pCommand.Parameters.AddWithValue("@given", pIdentity.GivenName ??
                string.Empty);
            pCommand.Parameters.AddWithValue("@family",
                pIdentity.FamilyComponent ?? string.Empty);
            pCommand.Parameters.AddWithValue("@generator",
                pIdentity.GeneratorId ?? string.Empty);
            pCommand.Parameters.AddWithValue("@culture", pIdentity.CultureId);
            pCommand.Parameters.AddWithValue("@schema",
                pIdentity.SchemaVersion);
            pCommand.Parameters.AddWithValue("@time",
                World.world?.getCurWorldTime() ?? 0d);
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
