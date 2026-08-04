using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class DynastyStateNamePersistence
    {
        public sealed class CurrentDynastyState
        {
            public bool Exists;
            public long DynastyId = -1L;
            public long ShiId = -1L;
            public string StateName = "";
        }

        public static bool TryReadCurrent(SQLiteConnection pDb,
            string pTable, long kingdomId,
            out CurrentDynastyState pState, out string pError)
        {
            pState = new CurrentDynastyState();
            pError = "";
            if (pDb == null ||
                !string.Equals(pTable, "DynastyPeriod",
                    StringComparison.Ordinal) || kingdomId < 0L)
            {
                pError = "invalid current dynasty state-name read input";
                return false;
            }
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "SELECT DYNASTY_ID,IFNULL(SHI_ID,-1)," +
                        "IFNULL(STATE_NAME,'') FROM " + pTable +
                        " WHERE KINGDOM_ID=@kingdom AND END_TIME=-1 " +
                        "ORDER BY START_TIME DESC,DYNASTY_ID DESC LIMIT 1"
                };
                command.Parameters.AddWithValue("@kingdom", kingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return true;
                pState.Exists = true;
                pState.DynastyId = Convert.ToInt64(reader.GetValue(0));
                pState.ShiId = Convert.ToInt64(reader.GetValue(1));
                pState.StateName = Convert.ToString(reader.GetValue(2)) ?? "";
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }

        public static bool TryUpdateCurrentStateName(SQLiteConnection pDb,
            string pTable, long kingdomId, long dynastyId, long shiId,
            string stateName, out string pError)
        {
            pError = "";
            if (pDb == null ||
                !string.Equals(pTable, "DynastyPeriod",
                    StringComparison.Ordinal) || kingdomId < 0L ||
                dynastyId < 0L || shiId < 0L ||
                !StateNameRules.IsValid(stateName))
            {
                pError = "invalid dynasty state-name update input";
                return false;
            }
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "UPDATE " + pTable +
                        " SET STATE_NAME=@state WHERE DYNASTY_ID=@dynasty " +
                        "AND KINGDOM_ID=@kingdom AND SHI_ID=@shi " +
                        "AND END_TIME=-1 AND IFNULL(STATE_NAME,'')<>@state"
                };
                command.Parameters.AddWithValue("@state", stateName);
                command.Parameters.AddWithValue("@dynasty", dynastyId);
                command.Parameters.AddWithValue("@kingdom", kingdomId);
                command.Parameters.AddWithValue("@shi", shiId);
                int affected = command.ExecuteNonQuery();
                if (affected == 1) return true;
                pError = "dynasty state-name update expected one pending row, got " +
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
