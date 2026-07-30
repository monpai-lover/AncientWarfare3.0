using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.court
{
    internal static class OfficialCareerRewardCooldownPersistence
    {
        public static bool TryRecord(SQLiteConnection pDb, string pTable,
            long actorId, long kingdomId, int rewardYear, double updatedTime)
        {
            if (pDb == null || !IsIdentifier(pTable) || actorId < 0L ||
                kingdomId < 0L || rewardYear < 0)
                return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                int affected;
                using (var update = new SQLiteCommand(pDb)
                       { Transaction = transaction })
                {
                    update.CommandText = "UPDATE " + pTable +
                        " SET KINGDOM_ID=@kingdom," +
                        "LAST_NOBLE_REWARD_YEAR=@year,UPDATED_TIME=@time" +
                        " WHERE ACTOR_ID=@actor";
                    AddParameters(update, actorId, kingdomId, rewardYear,
                        updatedTime);
                    affected = update.ExecuteNonQuery();
                }
                if (affected == 0)
                {
                    using var insert = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    insert.CommandText = "INSERT INTO " + pTable +
                        " (ACTOR_ID,KINGDOM_ID,LAST_NOBLE_REWARD_YEAR," +
                        "UPDATED_TIME) VALUES (@actor,@kingdom,@year,@time)";
                    AddParameters(insert, actorId, kingdomId, rewardYear,
                        updatedTime);
                    affected = insert.ExecuteNonQuery();
                }
                if (affected != 1)
                {
                    transaction.Rollback();
                    return false;
                }
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction?.Rollback(); } catch { }
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        private static void AddParameters(SQLiteCommand pCommand,
            long pActorId, long pKingdomId, int pRewardYear,
            double pUpdatedTime)
        {
            pCommand.Parameters.AddWithValue("@actor", pActorId);
            pCommand.Parameters.AddWithValue("@kingdom", pKingdomId);
            pCommand.Parameters.AddWithValue("@year", pRewardYear);
            pCommand.Parameters.AddWithValue("@time", pUpdatedTime);
        }

        private static bool IsIdentifier(string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            for (int i = 0; i < pValue.Length; i++)
            {
                char value = pValue[i];
                if (char.IsLetterOrDigit(value) || value == '_') continue;
                return false;
            }
            return true;
        }
    }
}
