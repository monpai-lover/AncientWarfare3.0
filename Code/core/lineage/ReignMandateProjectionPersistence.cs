using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class ReignMandateProjectionPersistence
    {
        public static bool TryProject(SQLiteConnection pDb, string pTable,
            long kingdomId, long rulerActorId, long mandatePeriodId,
            int emperorTitle, string stateName, out string pError)
        {
            pError = "";
            if (pDb == null ||
                !string.Equals(pTable, "KingdomReign",
                    StringComparison.Ordinal) ||
                kingdomId < 0L || rulerActorId < 0L ||
                mandatePeriodId < 0L || emperorTitle < 0)
            {
                pError = "invalid reign mandate projection input";
                return false;
            }

            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText =
                    "UPDATE " + pTable +
                    " SET MANDATE_PERIOD_ID=@period," +
                    "HIGHEST_TITLE=CASE WHEN HIGHEST_TITLE<@emperor " +
                    "THEN @emperor ELSE HIGHEST_TITLE END," +
                    "STATE_NAME_SNAPSHOT=CASE WHEN @state<>'' THEN @state " +
                    "ELSE STATE_NAME_SNAPSHOT END WHERE REIGN_ID=(" +
                    "SELECT REIGN_ID FROM " + pTable +
                    " WHERE KINGDOM_ID=@kingdom AND KING_ACTOR_ID=@actor" +
                    " AND END_TIME=-1 ORDER BY START_TIME DESC," +
                    "REIGN_ID DESC LIMIT 1) AND KINGDOM_ID=@kingdom" +
                    " AND KING_ACTOR_ID=@actor AND END_TIME=-1";
                command.Parameters.AddWithValue("@period", mandatePeriodId);
                command.Parameters.AddWithValue("@emperor", emperorTitle);
                command.Parameters.AddWithValue("@state", stateName ?? "");
                command.Parameters.AddWithValue("@kingdom", kingdomId);
                command.Parameters.AddWithValue("@actor", rulerActorId);
                int affected = command.ExecuteNonQuery();
                if (affected == 1) return true;
                pError = "mandate reign projection expected one row, got " +
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
