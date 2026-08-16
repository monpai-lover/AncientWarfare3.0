using System;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateBorderDecisionUsageService
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        internal static int ReadUses(long pPeriodId)
        {
            if (pPeriodId <= 0 || DB == null) return 0;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT BORDER_DEFENSE_USES FROM " +
                    DynastyPeriodTableItem.GetTableName() +
                    " WHERE DYNASTY_ID=@id LIMIT 1";
                command.Parameters.AddWithValue("@id", pPeriodId);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? 0 : Math.Max(0, Convert.ToInt32(value));
            }
            catch { return 0; }
        }

        internal static bool TryRecordUse(long pPeriodId)
        {
            if (!CanMutate() || pPeriodId <= 0 || DB == null) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    DynastyPeriodTableItem.GetTableName() +
                    " SET BORDER_DEFENSE_USES=BORDER_DEFENSE_USES+1 " +
                    "WHERE DYNASTY_ID=@id AND BORDER_DEFENSE_USES<@max";
                command.Parameters.AddWithValue("@id", pPeriodId);
                command.Parameters.AddWithValue("@max",
                    MandateBorderDecisionRules.MaximumUsesPerDynasty);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate border use persistence failed: " +
                                    e.Message);
                return false;
            }
        }

        internal static bool TryRollbackUse(long pPeriodId)
        {
            if (!CanMutate() || pPeriodId <= 0 || DB == null) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    DynastyPeriodTableItem.GetTableName() +
                    " SET BORDER_DEFENSE_USES=BORDER_DEFENSE_USES-1 " +
                    "WHERE DYNASTY_ID=@id AND BORDER_DEFENSE_USES>0";
                command.Parameters.AddWithValue("@id", pPeriodId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate border use rollback failed: " +
                                    e.Message);
                return false;
            }
        }

        private static bool CanMutate()
        {
            return MandateAuthorityMutationRules.CanMutate(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
