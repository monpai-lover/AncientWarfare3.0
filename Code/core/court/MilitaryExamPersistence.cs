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

    internal sealed class MilitaryExamCandidateRecord
    {
        public long Id = -1L;
        public long SessionId = -1L;
        public long KingdomId = -1L;
        public long ActorId = -1L;
        public string ActorName = "";
        public long HomeCityId = -1L;
        public string HomeCityName = "";
        public int AgeSnapshot = -1;
        public int WarfareScore = -1;
        public int DamageScore = -1;
        public int StrengthScore = -1;
        public int SpeedScore = -1;
        public int DiplomacyScore = -1;
        public int MilitaryMeritScore = -1;
        public int TotalScore = -1;
        public string Qualification = "none";
        public string AppointmentTier = "none";
        public string AppointmentStatus = "pending";
        public double UpdatedTime = -1d;
    }

    internal static class MilitaryExamPersistence
    {
        private static readonly string SessionTable =
            MilitaryExamSessionTableItem.GetTableName();
        private static readonly string CandidateTable =
            MilitaryExamCandidateTableItem.GetTableName();

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

        public static bool InsertCandidates(SQLiteConnection pDb,
            System.Collections.Generic.IReadOnlyList<MilitaryExamCandidateRecord> pCandidates)
        {
            if (pDb == null || pCandidates == null || pCandidates.Count == 0) return true;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                foreach (MilitaryExamCandidateRecord candidate in pCandidates)
                {
                    if (candidate == null || candidate.SessionId < 0L || candidate.ActorId < 0L) continue;
                    if (candidate.Id < 0L) candidate.Id = NextCandidateId(pDb, transaction);
                    using var command = new SQLiteCommand(pDb) { Transaction = transaction };
                    command.CommandText = "INSERT OR IGNORE INTO " + CandidateTable +
                        " (ID,SESSION_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,HOME_CITY_ID," +
                        "HOME_CITY_NAME,AGE_SNAPSHOT,WARFARE_SCORE,DAMAGE_SCORE," +
                        "STRENGTH_SCORE,SPEED_SCORE,DIPLOMACY_SCORE,MILITARY_MERIT_SCORE," +
                        "TOTAL_SCORE,QUALIFICATION,APPOINTMENT_TIER,APPOINTMENT_STATUS," +
                        "UPDATED_TIME) VALUES (@id,@session,@kingdom,@actor,@name,@city," +
                        "@city_name,@age,@warfare,@damage,@strength,@speed,@diplomacy," +
                        "@merit,@total,@qualification,@tier,@status,@time)";
                    command.Parameters.AddWithValue("@id", candidate.Id);
                    command.Parameters.AddWithValue("@session", candidate.SessionId);
                    command.Parameters.AddWithValue("@kingdom", candidate.KingdomId);
                    command.Parameters.AddWithValue("@actor", candidate.ActorId);
                    command.Parameters.AddWithValue("@name", candidate.ActorName ?? "");
                    command.Parameters.AddWithValue("@city", candidate.HomeCityId);
                    command.Parameters.AddWithValue("@city_name", candidate.HomeCityName ?? "");
                    command.Parameters.AddWithValue("@age", candidate.AgeSnapshot);
                    command.Parameters.AddWithValue("@warfare", candidate.WarfareScore);
                    command.Parameters.AddWithValue("@damage", candidate.DamageScore);
                    command.Parameters.AddWithValue("@strength", candidate.StrengthScore);
                    command.Parameters.AddWithValue("@speed", candidate.SpeedScore);
                    command.Parameters.AddWithValue("@diplomacy", candidate.DiplomacyScore);
                    command.Parameters.AddWithValue("@merit", candidate.MilitaryMeritScore);
                    command.Parameters.AddWithValue("@total", candidate.TotalScore);
                    command.Parameters.AddWithValue("@qualification", candidate.Qualification ?? "none");
                    command.Parameters.AddWithValue("@tier", candidate.AppointmentTier ?? "none");
                    command.Parameters.AddWithValue("@status", candidate.AppointmentStatus ?? "pending");
                    command.Parameters.AddWithValue("@time", candidate.UpdatedTime);
                    command.ExecuteNonQuery();
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

        private static long NextCandidateId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(ID),0)+1 FROM " + CandidateTable;
            return System.Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
