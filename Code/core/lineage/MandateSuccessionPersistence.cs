using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class MandateSuccessionPersistence
    {
        public static bool TryRefreshRuler(SQLiteConnection pDb,
            string pTable, long stateId, long kingdomId, long periodId,
            long rulerActorId, string rulerName, string dynastyName,
            double updatedTime, out string pError)
        {
            pError = "";
            if (pDb == null ||
                !string.Equals(pTable, "MandateState",
                    StringComparison.Ordinal) ||
                stateId < 0L || kingdomId < 0L || periodId < 0L ||
                rulerActorId < 0L)
            {
                pError = "invalid mandate succession persistence input";
                return false;
            }

            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText =
                    "UPDATE " + pTable +
                    " SET EMPEROR_ACTOR_ID=@actor,EMPEROR_NAME=@name," +
                    "DYNASTY_NAME=@dynasty,UPDATED_TIME=@time " +
                    "WHERE STATE_ID=@state AND ACTIVE=1" +
                    " AND KINGDOM_ID=@kingdom AND PERIOD_ID=@period";
                command.Parameters.AddWithValue("@actor", rulerActorId);
                command.Parameters.AddWithValue("@name", rulerName ?? "");
                command.Parameters.AddWithValue("@dynasty",
                    dynastyName ?? "");
                command.Parameters.AddWithValue("@time", updatedTime);
                command.Parameters.AddWithValue("@state", stateId);
                command.Parameters.AddWithValue("@kingdom", kingdomId);
                command.Parameters.AddWithValue("@period", periodId);
                int affected = command.ExecuteNonQuery();
                if (affected == 1) return true;
                pError = "mandate succession expected one state row, got " +
                         affected;
                return false;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }
    }
}
