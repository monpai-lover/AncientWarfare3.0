using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.court
{
    internal static class CourtDispositionPersistence
    {
        private const string Table = "SocialAction";

        public static long Begin(SQLiteConnection pDb,
            CourtDispositionCommand pCommand, int pPoliticalCost,
            int pStartYear, double pStartTime)
        {
            if (pDb == null || pCommand == null ||
                string.IsNullOrWhiteSpace(pCommand.OperationKey) ||
                pPoliticalCost < 0) return -1L;

            try
            {
                using SQLiteTransaction transaction = pDb.BeginTransaction();
                long actionId = NextId(pDb, transaction);
                using var insert = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "INSERT INTO " + Table +
                        " (ACTION_ID,OPERATION_KEY,KINGDOM_ID,RULER_ACTOR_ID," +
                        "TARGET_ACTOR_ID,ACTION_TYPE,INT_PARAMETER,LONG_PARAMETER," +
                        "POLITICAL_COST,RESULT,REASON,START_YEAR,START_TIME,END_TIME)" +
                        " VALUES (@id,@key,@kingdom,@ruler,@target,@type,@int,@long," +
                        "@cost,'pending','',@year,@start,-1)"
                };
                insert.Parameters.AddWithValue("@id", actionId);
                insert.Parameters.AddWithValue("@key", pCommand.OperationKey);
                insert.Parameters.AddWithValue("@kingdom", pCommand.KingdomId);
                insert.Parameters.AddWithValue("@ruler", pCommand.RulerActorId);
                insert.Parameters.AddWithValue("@target", pCommand.TargetActorId);
                insert.Parameters.AddWithValue("@type",
                    pCommand.Action.ToString().ToLowerInvariant());
                insert.Parameters.AddWithValue("@int", pCommand.IntParameter);
                insert.Parameters.AddWithValue("@long", pCommand.LongParameter);
                insert.Parameters.AddWithValue("@cost", pPoliticalCost);
                insert.Parameters.AddWithValue("@year", pStartYear);
                insert.Parameters.AddWithValue("@start", pStartTime);
                if (insert.ExecuteNonQuery() != 1) return -1L;
                transaction.Commit();
                return actionId;
            }
            catch (SQLiteException)
            {
                return -1L;
            }
        }

        public static bool Finalize(SQLiteConnection pDb, long pActionId,
            CourtDispositionOutcome pOutcome, string pReason, double pEndTime)
        {
            if (pDb == null || pActionId < 0 ||
                pOutcome == CourtDispositionOutcome.Rejected) return false;
            try
            {
                using var update = new SQLiteCommand(pDb)
                {
                    CommandText = "UPDATE " + Table +
                        " SET RESULT=@result,REASON=@reason,END_TIME=@end" +
                        " WHERE ACTION_ID=@id AND RESULT='pending'"
                };
                update.Parameters.AddWithValue("@result",
                    pOutcome.ToString().ToLowerInvariant());
                update.Parameters.AddWithValue("@reason", pReason ?? "");
                update.Parameters.AddWithValue("@end", pEndTime);
                update.Parameters.AddWithValue("@id", pActionId);
                return update.ExecuteNonQuery() == 1;
            }
            catch (SQLiteException)
            {
                return false;
            }
        }

        public static CourtDispositionOutcome? ReadOutcome(
            SQLiteConnection pDb, long pActionId)
        {
            if (pDb == null || pActionId < 0) return null;
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "SELECT RESULT FROM " + Table +
                        " WHERE ACTION_ID=@id LIMIT 1"
                };
                command.Parameters.AddWithValue("@id", pActionId);
                string result = command.ExecuteScalar()?.ToString() ?? "";
                return Enum.TryParse(result, true,
                    out CourtDispositionOutcome outcome)
                    ? outcome
                    : (CourtDispositionOutcome?)null;
            }
            catch (SQLiteException)
            {
                return null;
            }
        }

        public static CourtDispositionLedgerEntry ReadByOperationKey(
            SQLiteConnection pDb, string pOperationKey)
        {
            if (pDb == null || string.IsNullOrWhiteSpace(pOperationKey))
                return null;
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "SELECT ACTION_ID,RESULT,REASON," +
                        "POLITICAL_COST FROM " + Table +
                        " WHERE OPERATION_KEY=@key LIMIT 1"
                };
                command.Parameters.AddWithValue("@key", pOperationKey);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return null;
                string result = reader.IsDBNull(1)
                    ? ""
                    : reader.GetString(1);
                CourtDispositionOutcome? outcome = Enum.TryParse(result, true,
                    out CourtDispositionOutcome parsed)
                    ? parsed
                    : (CourtDispositionOutcome?)null;
                return new CourtDispositionLedgerEntry(reader.GetInt64(0),
                    outcome, reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.GetInt32(3));
            }
            catch (SQLiteException)
            {
                return null;
            }
        }

        private static long NextId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT IFNULL(MAX(ACTION_ID),0)+1 FROM " + Table
            };
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
