using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class TributaryProtectionService
    {
        internal static bool IsProtectedPair(Kingdom left, Kingdom right)
        {
            if (left?.data == null || right?.data == null ||
                left == right || left.isRekt() || right.isRekt())
                return false;

            Kingdom tributary;
            Kingdom suzerain;
            if (VassalService.GetTributarySuzerainId(left) == right.id)
            {
                tributary = left;
                suzerain = right;
            }
            else if (VassalService.GetTributarySuzerainId(right) == left.id)
            {
                tributary = right;
                suzerain = left;
            }
            else
            {
                return false;
            }

            tributary.data.get(LineageKeys.TRIBUTARY_RELATION_ID,
                out long relationId, -1L);
            if (relationId < 0) return false;
            SQLiteConnection db =
                LineageArchiveManager.Instance?.OperatingDB;
            if (db == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful)
                return false;

            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT VASSAL_ID,SUZERAIN_ID," +
                    "ACTIVE,END_TIME,CONTRACT_TIER FROM " +
                    VassalRelationTableItem.GetTableName() +
                    " WHERE RELATION_ID=@relation LIMIT 1";
                command.Parameters.AddWithValue("@relation", relationId);
                using var reader =
                    (SQLiteDataReader)command.ExecuteReader();
                if (!reader.Read()) return false;
                return TributaryProtectionRules.IsDirectActivePair(
                    left.id, right.id,
                    reader.IsDBNull(0) ? -1L : reader.GetInt64(0),
                    reader.IsDBNull(1) ? -1L : reader.GetInt64(1),
                    !reader.IsDBNull(2) && reader.GetInt64(2) == 1L,
                    reader.IsDBNull(3) ? -1d : reader.GetDouble(3),
                    reader.IsDBNull(4)
                        ? VassalContractTierRules.Outer
                        : (int)reader.GetInt64(4));
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Tributary protection validation failed: relation=" +
                    relationId + " tributary=" + tributary.id +
                    " suzerain=" + suzerain.id + " error=" +
                    error.Message);
                return false;
            }
        }
    }
}
