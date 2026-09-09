using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.court
{
    internal sealed class MilitaryExamSessionRecord
    {
        public long Id = -1L;
        public long KingdomId = -1L;
        public string KingdomName = "";
        public string Mode = "";
        public int CycleYear = -1;
        public string Stage = "scheduled";
        public string Status = "scheduled";
        public long OpenWorldDay = -1L;
        public long NextDueWorldDay = -1L;
        public long HostRulerId = -1L;
        public long FinalRulerId = -1L;
        public int CandidateCursor;
        public int GeneralTarget = -1;
        public int LocalTarget = -1;
        public int GeneralVacancies = -1;
        public int LocalVacancies = -1;
        public int WaitingCandidateCount = -1;
        public int ReserveTarget = -1;
        public int AdmissionQuota = -1;
        public double UpdatedTime = -1d;
    }

    internal static class MilitaryExamPersistence
    {
        private static readonly string SessionTable =
            MilitaryExamSessionTableItem.GetTableName();

        public static bool TryCreateSession(SQLiteConnection pDb,
            MilitaryExamSessionRecord pSession)
        {
            if (pDb == null || pSession == null || pSession.KingdomId < 0L ||
                pSession.CycleYear < 0) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                if (pSession.Id < 0L)
                    pSession.Id = NextId(pDb, transaction);
                using var command = new SQLiteCommand(pDb)
                    { Transaction = transaction };
                command.CommandText = "INSERT OR IGNORE INTO " + SessionTable +
                    " (ID,KINGDOM_ID,KINGDOM_NAME,MODE,CYCLE_YEAR,STAGE,STATUS," +
                    "OPEN_WORLD_DAY,NEXT_DUE_WORLD_DAY,HOST_RULER_ID,FINAL_RULER_ID," +
                    "CANDIDATE_CURSOR,GENERAL_TARGET,LOCAL_TARGET,GENERAL_VACANCIES," +
                    "LOCAL_VACANCIES,WAITING_CANDIDATE_COUNT,RESERVE_TARGET," +
                    "ADMISSION_QUOTA,UPDATED_TIME) VALUES " +
                    "(@id,@kingdom,@name,@mode,@year,@stage,@status,@open,@due," +
                    "@host,@final,@cursor,@general_target,@local_target," +
                    "@general_vacancies,@local_vacancies,@waiting,@reserve,@quota,@time)";
                command.Parameters.AddWithValue("@id", pSession.Id);
                command.Parameters.AddWithValue("@kingdom", pSession.KingdomId);
                command.Parameters.AddWithValue("@name", pSession.KingdomName ?? "");
                command.Parameters.AddWithValue("@mode", pSession.Mode ?? "");
                command.Parameters.AddWithValue("@year", pSession.CycleYear);
                command.Parameters.AddWithValue("@stage", pSession.Stage ?? "scheduled");
                command.Parameters.AddWithValue("@status", pSession.Status ?? "scheduled");
                command.Parameters.AddWithValue("@open", pSession.OpenWorldDay);
                command.Parameters.AddWithValue("@due", pSession.NextDueWorldDay);
                command.Parameters.AddWithValue("@host", pSession.HostRulerId);
                command.Parameters.AddWithValue("@final", pSession.FinalRulerId);
                command.Parameters.AddWithValue("@cursor", pSession.CandidateCursor);
                command.Parameters.AddWithValue("@general_target", pSession.GeneralTarget);
                command.Parameters.AddWithValue("@local_target", pSession.LocalTarget);
                command.Parameters.AddWithValue("@general_vacancies", pSession.GeneralVacancies);
                command.Parameters.AddWithValue("@local_vacancies", pSession.LocalVacancies);
                command.Parameters.AddWithValue("@waiting", pSession.WaitingCandidateCount);
                command.Parameters.AddWithValue("@reserve", pSession.ReserveTarget);
                command.Parameters.AddWithValue("@quota", pSession.AdmissionQuota);
                command.Parameters.AddWithValue("@time", pSession.UpdatedTime);
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
                transaction.Commit();
                return true;
            }
            catch { try { transaction?.Rollback(); } catch { } return false; }
            finally { transaction?.Dispose(); }
        }

        private static long NextId(SQLiteConnection pDb, SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(ID),0)+1 FROM " + SessionTable;
            return System.Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
