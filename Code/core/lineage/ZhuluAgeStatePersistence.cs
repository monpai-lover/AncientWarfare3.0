using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluAgeStatePersistence
    {
        private const long StateId = 1L;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
            LineageArchiveManager.Instance.InitializeSuccessful;

        internal static bool IsReady => Ready;

        internal static bool ReadEntryActive()
        {
            if (!Ready) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ENTRY_ACTIVE FROM " +
                    ZhuluAgeStateTableItem.GetTableName() +
                    " WHERE STATE_ID=@id LIMIT 1";
                command.Parameters.AddWithValue("@id", StateId);
                object value = command.ExecuteScalar();
                return value != null && value != DBNull.Value &&
                       Convert.ToInt32(value) != 0;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Zhulu age state read failed: " + exception.Message);
                return false;
            }
        }

        internal static bool WriteEntryActive(bool pActive)
        {
            if (!Ready) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "INSERT OR REPLACE INTO " +
                    ZhuluAgeStateTableItem.GetTableName() +
                    "(STATE_ID,ENTRY_ACTIVE,UPDATED_TIME) " +
                    "VALUES(@id,@active,@time)";
                command.Parameters.AddWithValue("@id", StateId);
                command.Parameters.AddWithValue("@active", pActive ? 1 : 0);
                command.Parameters.AddWithValue("@time",
                    LineageService.CurTime());
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Zhulu age state write failed: " + exception.Message);
                return false;
            }
        }
    }
}
