using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.court
{
    internal sealed class CivilServiceExamSessionRecord
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
        public bool PlayerRankingPending;
        public int CandidateCursor;
        public int CentralVacancies = -1;
        public int CityVacancies = -1;
        public int WaitingCandidateCount = -1;
        public int ReserveTarget = -1;
        public int AdmissionQuota = -1;
        public double UpdatedTime = -1d;
    }

    internal sealed class CivilServiceExamCandidateRecord
    {
        public long Id = -1L;
        public long SessionId = -1L;
        public long KingdomId = -1L;
        public long ActorId = -1L;
        public string ActorName = "";
        public long HomeCityId = -1L;
        public string HomeCityName = "";
        public string SocialOrigin = "commoner";
        public string SchoolId = "";
        public int LocalGrade;
        public int LocalScore = -1;
        public int MetropolitanScore = -1;
        public int PalaceScore = -1;
        public int NationalScore = -1;
        public string LocalResult = "pending";
        public string MetropolitanResult = "pending";
        public string PalaceResult = "pending";
        public string NationalResult = "pending";
        public string CurrentStageResult = "pending";
        public string Qualification = "none";
        public int FinalRank;
        public string FinalTitle = "";
        public int EntryBonus;
        public double UpdatedTime = -1d;
    }

    internal sealed class CivilServiceExamCandidateUpdate
    {
        public long Id = -1L;
        public int LocalScore = -1;
        public int MetropolitanScore = -1;
        public int PalaceScore = -1;
        public int NationalScore = -1;
        public string LocalResult = "pending";
        public string MetropolitanResult = "pending";
        public string PalaceResult = "pending";
        public string NationalResult = "pending";
        public string StageResult = "pending";
        public string Qualification = "none";
        public int EntryBonus = 0;
    }

    internal sealed class CivilServiceExamRanking
    {
        public long CandidateId = -1L;
        public int FinalRank = 0;
        public string FinalTitle = "";
        public int EntryBonus = 0;
    }

    internal sealed class CivilServiceQualificationRecord
    {
        public long CandidateId;
        public long ActorId;
        public long KingdomId;
        public long SessionId;
        public string Qualification = "none";
        public int ResultYear = -1;
        public int EntryBonus;
    }

    internal static class CivilServiceExamPersistence
    {
        private static readonly string SessionTable =
            CivilServiceExamSessionTableItem.GetTableName();
        private static readonly string CandidateTable =
            CivilServiceExamCandidateTableItem.GetTableName();

        public static bool TryCreateSession(SQLiteConnection pDb,
            CivilServiceExamSessionRecord pSession)
        {
            if (pDb == null || pSession == null || pSession.KingdomId < 0L ||
                pSession.CycleYear < 0) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                if (pSession.Id < 0L)
                    pSession.Id = NextId(pDb, transaction, SessionTable);
                using var command = new SQLiteCommand(pDb)
                    { Transaction = transaction };
                command.CommandText = "INSERT OR IGNORE INTO " + SessionTable +
                    " (ID,KINGDOM_ID,KINGDOM_NAME,MODE,CYCLE_YEAR,STAGE,STATUS," +
                    "OPEN_WORLD_DAY,NEXT_DUE_WORLD_DAY,HOST_RULER_ID," +
                    "FINAL_RULER_ID,PLAYER_RANKING_PENDING,CANDIDATE_CURSOR," +
                    "CENTRAL_VACANCIES,CITY_VACANCIES," +
                    "WAITING_CANDIDATE_COUNT,RESERVE_TARGET,ADMISSION_QUOTA," +
                    "UPDATED_TIME) VALUES (@id,@kingdom,@name,@mode,@year," +
                    "@stage,@status,@open,@due,@host,@final,@pending,@cursor," +
                    "@central_vacancies,@city_vacancies," +
                    "@waiting_candidate_count,@reserve_target," +
                    "@admission_quota,@time)";
                AddSessionParameters(command, pSession);
                int affected = command.ExecuteNonQuery();
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
            finally { transaction?.Dispose(); }
        }

        public static bool InsertCandidates(SQLiteConnection pDb,
            IReadOnlyList<CivilServiceExamCandidateRecord> pCandidates)
        {
            if (pDb == null || pCandidates == null) return false;
            if (pCandidates.Count == 0) return true;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                long nextId = NextId(pDb, transaction, CandidateTable);
                foreach (CivilServiceExamCandidateRecord candidate in pCandidates)
                {
                    if (candidate == null || candidate.SessionId < 0L ||
                        candidate.ActorId < 0L)
                    {
                        transaction.Rollback();
                        return false;
                    }
                    if (candidate.Id < 0L) candidate.Id = nextId++;
                    using var command = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    command.CommandText = "INSERT OR IGNORE INTO " + CandidateTable +
                        " (ID,SESSION_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME," +
                        "HOME_CITY_ID,HOME_CITY_NAME,SOCIAL_ORIGIN,SCHOOL_ID," +
                        "LOCAL_GRADE,LOCAL_SCORE,METROPOLITAN_SCORE,PALACE_SCORE," +
                        "NATIONAL_SCORE,LOCAL_RESULT,METROPOLITAN_RESULT," +
                        "PALACE_RESULT,NATIONAL_RESULT,CURRENT_STAGE_RESULT,QUALIFICATION," +
                        "FINAL_RANK,FINAL_TITLE,ENTRY_BONUS,UPDATED_TIME) VALUES " +
                        "(@id,@session,@kingdom,@actor,@name,@city,@city_name," +
                        "@origin,@school,@grade,@local,@metro,@palace,@national," +
                        "@local_result,@metro_result,@palace_result,@national_result," +
                        "@result,@qualification,@rank,@title,@bonus,@time)";
                    AddCandidateParameters(command, candidate);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction?.Rollback(); } catch { }
                return false;
            }
            finally { transaction?.Dispose(); }
        }

        public static CivilServiceExamSessionRecord LoadDueSession(
            SQLiteConnection pDb, long pWorldDay)
        {
            if (pDb == null) return null;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = SessionProjection + " WHERE STATUS IN " +
                    "('scheduled','running','stage_ranking','ranking_pending') AND " +
                    "NEXT_DUE_WORLD_DAY<=@due ORDER BY NEXT_DUE_WORLD_DAY,ID LIMIT 1";
                command.Parameters.AddWithValue("@due", pWorldDay);
                using SQLiteDataReader reader = command.ExecuteReader();
                return reader.Read() ? ReadSession(reader) : null;
            }
            catch { return null; }
        }

        public static List<CivilServiceExamSessionRecord> LoadActiveSessions(
            SQLiteConnection pDb, int pLimit = 512)
        {
            var result = new List<CivilServiceExamSessionRecord>();
            if (pDb == null || pLimit <= 0) return result;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = SessionProjection + " WHERE STATUS IN " +
                    "('scheduled','running','stage_ranking','ranking_pending') " +
                    "ORDER BY NEXT_DUE_WORLD_DAY,ID LIMIT @limit";
                command.Parameters.AddWithValue("@limit", Math.Min(512, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadSession(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        public static CivilServiceExamSessionRecord LoadSession(
            SQLiteConnection pDb, long pSessionId)
        {
            if (pDb == null || pSessionId < 0L) return null;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = SessionProjection + " WHERE ID=@id LIMIT 1";
                command.Parameters.AddWithValue("@id", pSessionId);
                using SQLiteDataReader reader = command.ExecuteReader();
                return reader.Read() ? ReadSession(reader) : null;
            }
            catch { return null; }
        }

        public static List<CivilServiceExamCandidateRecord> LoadCandidatesPage(
            SQLiteConnection pDb, long pSessionId, int pOffset, int pLimit)
        {
            var result = new List<CivilServiceExamCandidateRecord>();
            if (pDb == null || pSessionId < 0L || pOffset < 0 || pLimit <= 0)
                return result;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = CandidateProjection +
                    " WHERE SESSION_ID=@session ORDER BY ID LIMIT @limit OFFSET @offset";
                command.Parameters.AddWithValue("@session", pSessionId);
                command.Parameters.AddWithValue("@limit", Math.Min(
                    CivilServiceExamRules.AuthorityCandidateBudget, pLimit));
                command.Parameters.AddWithValue("@offset", pOffset);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadCandidate(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        public static List<CivilServiceExamCandidateRecord>
            LoadStageRankingPage(SQLiteConnection pDb, long pSessionId,
                string pStage, int pLimit)
        {
            var result = new List<CivilServiceExamCandidateRecord>();
            string scoreColumn = StageScoreColumn(pStage);
            if (pDb == null || pSessionId < 0L || pLimit <= 0 ||
                string.IsNullOrEmpty(scoreColumn)) return result;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = CandidateProjection +
                    " WHERE SESSION_ID=@session AND " +
                    "CURRENT_STAGE_RESULT='scored' ORDER BY " + scoreColumn +
                    " DESC,ACTOR_ID ASC LIMIT @limit";
                command.Parameters.AddWithValue("@session", pSessionId);
                command.Parameters.AddWithValue("@limit", Math.Min(
                    CivilServiceExamRules.AuthorityCandidateBudget, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadCandidate(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        public static List<CivilServiceExamCandidateRecord>
            LoadFinalRankingPage(SQLiteConnection pDb, long pSessionId,
                CivilServiceExamMode pMode, int pLimit)
        {
            var result = new List<CivilServiceExamCandidateRecord>();
            if (pDb == null || pSessionId < 0L || pLimit <= 0) return result;
            try
            {
                bool imperial = pMode == CivilServiceExamMode.Imperial;
                using var command = new SQLiteCommand(pDb);
                command.CommandText = CandidateProjection +
                    " WHERE SESSION_ID=@session AND QUALIFICATION=@qualification " +
                    "AND CURRENT_STAGE_RESULT='passed' AND FINAL_RANK=0 " +
                    "ORDER BY " + (imperial ? "PALACE_SCORE" : "NATIONAL_SCORE") +
                    " DESC,ACTOR_ID ASC LIMIT @limit";
                command.Parameters.AddWithValue("@session", pSessionId);
                command.Parameters.AddWithValue("@qualification",
                    imperial ? "jinshi" : "gongshi");
                command.Parameters.AddWithValue("@limit", Math.Min(
                    CivilServiceExamRules.AuthorityCandidateBudget, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadCandidate(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        public static List<CivilServiceExamCandidateRecord>
            LoadFinalRankingCandidates(SQLiteConnection pDb, long pSessionId,
                CivilServiceExamMode pMode, int pLimit)
        {
            var result = new List<CivilServiceExamCandidateRecord>();
            if (pDb == null || pSessionId < 0L || pLimit <= 0) return result;
            try
            {
                bool imperial = pMode == CivilServiceExamMode.Imperial;
                using var command = new SQLiteCommand(pDb);
                command.CommandText = CandidateProjection +
                    " WHERE SESSION_ID=@session AND QUALIFICATION=@qualification " +
                    "AND CURRENT_STAGE_RESULT='passed' ORDER BY ACTOR_ID " +
                    "LIMIT @limit";
                command.Parameters.AddWithValue("@session", pSessionId);
                command.Parameters.AddWithValue("@qualification",
                    imperial ? "jinshi" : "gongshi");
                command.Parameters.AddWithValue("@limit", Math.Min(
                    CivilServiceExamRules.CandidateLimit, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadCandidate(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        public static List<CivilServiceExamCandidateRecord>
            LoadPlayerRankingFinalists(SQLiteConnection pDb,
                long pSessionId, int pLimit)
        {
            var result = new List<CivilServiceExamCandidateRecord>();
            if (pDb == null || pSessionId < 0L || pLimit <= 0) return result;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = CandidateProjection +
                    " WHERE SESSION_ID=@session AND QUALIFICATION='jinshi' " +
                    "AND CURRENT_STAGE_RESULT='passed' ORDER BY " +
                    "PALACE_SCORE DESC,ACTOR_ID ASC LIMIT @limit";
                command.Parameters.AddWithValue("@session", pSessionId);
                command.Parameters.AddWithValue("@limit", Math.Min(
                    CivilServiceExamRules.CandidateLimit, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadCandidate(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        public static bool CommitCandidateBatch(SQLiteConnection pDb,
            long pSessionId, int pExpectedCursor,
            IReadOnlyList<CivilServiceExamCandidateUpdate> pUpdates,
            int pNextCursor, double pUpdatedTime)
        {
            if (pDb == null || pSessionId < 0L || pExpectedCursor < 0 ||
                pNextCursor < pExpectedCursor || pUpdates == null ||
                pUpdates.Count > CivilServiceExamRules.AuthorityCandidateBudget)
                return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                foreach (CivilServiceExamCandidateUpdate update in pUpdates)
                {
                    if (update == null || update.Id < 0L)
                    {
                        transaction.Rollback();
                        return false;
                    }
                    using var candidate = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    candidate.CommandText = "UPDATE " + CandidateTable +
                        " SET LOCAL_SCORE=@local,METROPOLITAN_SCORE=@metro," +
                        "PALACE_SCORE=@palace,NATIONAL_SCORE=@national," +
                        "LOCAL_RESULT=@local_result," +
                        "METROPOLITAN_RESULT=@metro_result," +
                        "PALACE_RESULT=@palace_result," +
                        "NATIONAL_RESULT=@national_result," +
                        "CURRENT_STAGE_RESULT=@result,QUALIFICATION=@qualification," +
                        "ENTRY_BONUS=@bonus,UPDATED_TIME=@time " +
                        "WHERE ID=@candidate AND SESSION_ID=@session";
                    candidate.Parameters.AddWithValue("@local", update.LocalScore);
                    candidate.Parameters.AddWithValue("@metro", update.MetropolitanScore);
                    candidate.Parameters.AddWithValue("@palace", update.PalaceScore);
                    candidate.Parameters.AddWithValue("@national", update.NationalScore);
                    candidate.Parameters.AddWithValue("@local_result",
                        update.LocalResult ?? "pending");
                    candidate.Parameters.AddWithValue("@metro_result",
                        update.MetropolitanResult ?? "pending");
                    candidate.Parameters.AddWithValue("@palace_result",
                        update.PalaceResult ?? "pending");
                    candidate.Parameters.AddWithValue("@national_result",
                        update.NationalResult ?? "pending");
                    candidate.Parameters.AddWithValue("@result", update.StageResult ?? "");
                    candidate.Parameters.AddWithValue("@qualification", update.Qualification ?? "none");
                    candidate.Parameters.AddWithValue("@bonus", update.EntryBonus);
                    candidate.Parameters.AddWithValue("@time", pUpdatedTime);
                    candidate.Parameters.AddWithValue("@candidate", update.Id);
                    candidate.Parameters.AddWithValue("@session", pSessionId);
                    if (candidate.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                using var session = new SQLiteCommand(pDb)
                    { Transaction = transaction };
                session.CommandText = "UPDATE " + SessionTable +
                    " SET CANDIDATE_CURSOR=@next,UPDATED_TIME=@time " +
                    "WHERE ID=@id AND CANDIDATE_CURSOR=@expected_cursor";
                session.Parameters.AddWithValue("@next", pNextCursor);
                session.Parameters.AddWithValue("@time", pUpdatedTime);
                session.Parameters.AddWithValue("@id", pSessionId);
                session.Parameters.AddWithValue("@expected_cursor", pExpectedCursor);
                if (session.ExecuteNonQuery() != 1)
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
            finally { transaction?.Dispose(); }
        }

        public static bool CompleteStage(SQLiteConnection pDb, long pSessionId,
            string pExpectedStage, string pNextStage, string pStatus,
            long pNextDueWorldDay, double pUpdatedTime)
        {
            if (pDb == null || pSessionId < 0L ||
                string.IsNullOrEmpty(pExpectedStage) ||
                string.IsNullOrEmpty(pNextStage)) return false;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "UPDATE " + SessionTable +
                    " SET STAGE=@next,STATUS=@status,NEXT_DUE_WORLD_DAY=@due," +
                    "CANDIDATE_CURSOR=0,UPDATED_TIME=@time " +
                    "WHERE ID=@id AND STAGE=@expected";
                command.Parameters.AddWithValue("@next", pNextStage);
                command.Parameters.AddWithValue("@status", pStatus ?? "running");
                command.Parameters.AddWithValue("@due", pNextDueWorldDay);
                command.Parameters.AddWithValue("@time", pUpdatedTime);
                command.Parameters.AddWithValue("@id", pSessionId);
                command.Parameters.AddWithValue("@expected", pExpectedStage);
                return command.ExecuteNonQuery() == 1;
            }
            catch { return false; }
        }

        public static bool MarkPlayerRankingPending(SQLiteConnection pDb,
            long pSessionId, string pExpectedStage,
            long pNextDueWorldDay, double pUpdatedTime)
        {
            if (pDb == null || pSessionId < 0L) return false;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "UPDATE " + SessionTable +
                    " SET STAGE='ranking',STATUS='ranking_pending'," +
                    "PLAYER_RANKING_PENDING=1,NEXT_DUE_WORLD_DAY=@due," +
                    "CANDIDATE_CURSOR=0,UPDATED_TIME=@time " +
                    "WHERE ID=@id AND STAGE=@expected AND " +
                    "STATUS IN ('running','stage_ranking')";
                command.Parameters.AddWithValue("@due", pNextDueWorldDay);
                command.Parameters.AddWithValue("@time", pUpdatedTime);
                command.Parameters.AddWithValue("@id", pSessionId);
                command.Parameters.AddWithValue("@expected",
                    pExpectedStage ?? "palace");
                return command.ExecuteNonQuery() == 1;
            }
            catch { return false; }
        }

        public static bool FinalizeRanking(SQLiteConnection pDb,
            long pSessionId, long pFinalRulerId,
            IReadOnlyList<CivilServiceExamRanking> pRankings,
            double pUpdatedTime)
        {
            if (pDb == null || pSessionId < 0L || pRankings == null)
                return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                foreach (CivilServiceExamRanking ranking in pRankings)
                {
                    using var candidate = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    candidate.CommandText = "UPDATE " + CandidateTable +
                        " SET FINAL_RANK=@rank,FINAL_TITLE=@title," +
                        "ENTRY_BONUS=@bonus,UPDATED_TIME=@time " +
                        "WHERE ID=@candidate AND SESSION_ID=@session";
                    candidate.Parameters.AddWithValue("@rank", ranking.FinalRank);
                    candidate.Parameters.AddWithValue("@title", ranking.FinalTitle ?? "");
                    candidate.Parameters.AddWithValue("@bonus", ranking.EntryBonus);
                    candidate.Parameters.AddWithValue("@time", pUpdatedTime);
                    candidate.Parameters.AddWithValue("@candidate", ranking.CandidateId);
                    candidate.Parameters.AddWithValue("@session", pSessionId);
                    if (candidate.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                using var session = new SQLiteCommand(pDb)
                    { Transaction = transaction };
                session.CommandText = "UPDATE " + SessionTable +
                    " SET STAGE='completed',STATUS='completed',FINAL_RULER_ID=@ruler," +
                    "PLAYER_RANKING_PENDING=0,UPDATED_TIME=@time " +
                    "WHERE ID=@session AND STAGE='ranking' AND STATUS='ranking_pending' AND " +
                    "PLAYER_RANKING_PENDING=1";
                session.Parameters.AddWithValue("@ruler", pFinalRulerId);
                session.Parameters.AddWithValue("@time", pUpdatedTime);
                session.Parameters.AddWithValue("@session", pSessionId);
                if (session.ExecuteNonQuery() != 1)
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
            finally { transaction?.Dispose(); }
        }

        public static bool CommitFinalRankingBatch(SQLiteConnection pDb,
            long pSessionId, int pExpectedCursor,
            IReadOnlyList<CivilServiceExamRanking> pRankings,
            int pNextCursor, double pUpdatedTime)
        {
            if (pDb == null || pSessionId < 0L || pExpectedCursor < 0 ||
                pNextCursor < pExpectedCursor || pRankings == null ||
                pRankings.Count > CivilServiceExamRules.AuthorityCandidateBudget)
                return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                foreach (CivilServiceExamRanking ranking in pRankings)
                {
                    if (ranking == null || ranking.CandidateId < 0L)
                    {
                        transaction.Rollback();
                        return false;
                    }
                    using var candidate = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    candidate.CommandText = "UPDATE " + CandidateTable +
                        " SET FINAL_RANK=@rank,FINAL_TITLE=@title," +
                        "ENTRY_BONUS=@bonus,UPDATED_TIME=@time " +
                        "WHERE ID=@candidate AND SESSION_ID=@session " +
                        "AND FINAL_RANK=0";
                    candidate.Parameters.AddWithValue("@rank", ranking.FinalRank);
                    candidate.Parameters.AddWithValue("@title", ranking.FinalTitle ?? "");
                    candidate.Parameters.AddWithValue("@bonus", ranking.EntryBonus);
                    candidate.Parameters.AddWithValue("@time", pUpdatedTime);
                    candidate.Parameters.AddWithValue("@candidate", ranking.CandidateId);
                    candidate.Parameters.AddWithValue("@session", pSessionId);
                    if (candidate.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                using var session = new SQLiteCommand(pDb)
                    { Transaction = transaction };
                session.CommandText = "UPDATE " + SessionTable +
                    " SET CANDIDATE_CURSOR=@next,UPDATED_TIME=@time " +
                    "WHERE ID=@id AND STAGE='ranking' AND " +
                    "CANDIDATE_CURSOR=@expected_cursor";
                session.Parameters.AddWithValue("@next", pNextCursor);
                session.Parameters.AddWithValue("@time", pUpdatedTime);
                session.Parameters.AddWithValue("@id", pSessionId);
                session.Parameters.AddWithValue("@expected_cursor", pExpectedCursor);
                if (session.ExecuteNonQuery() != 1)
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
            finally { transaction?.Dispose(); }
        }

        public static bool CompleteRanking(SQLiteConnection pDb,
            long pSessionId, long pFinalRulerId, double pUpdatedTime)
        {
            if (pDb == null || pSessionId < 0L) return false;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "UPDATE " + SessionTable +
                    " SET STAGE='completed',STATUS='completed'," +
                    "FINAL_RULER_ID=@ruler,PLAYER_RANKING_PENDING=0," +
                    "UPDATED_TIME=@time WHERE ID=@id AND STAGE='ranking' " +
                    "AND STATUS<>'completed'";
                command.Parameters.AddWithValue("@ruler", pFinalRulerId);
                command.Parameters.AddWithValue("@time", pUpdatedTime);
                command.Parameters.AddWithValue("@id", pSessionId);
                return command.ExecuteNonQuery() == 1;
            }
            catch { return false; }
        }

        public static bool CancelActiveSession(SQLiteConnection pDb,
            long pSessionId, double pUpdatedTime)
        {
            if (pDb == null || pSessionId < 0L) return false;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "UPDATE " + SessionTable +
                    " SET STAGE='cancelled',STATUS='cancelled'," +
                    "PLAYER_RANKING_PENDING=0,UPDATED_TIME=@time " +
                    "WHERE ID=@id AND STATUS IN " +
                    "('scheduled','running','stage_ranking','ranking_pending')";
                command.Parameters.AddWithValue("@time", pUpdatedTime);
                command.Parameters.AddWithValue("@id", pSessionId);
                return command.ExecuteNonQuery() == 1;
            }
            catch { return false; }
        }

        public static int CancelActiveSessionForKingdom(
            SQLiteConnection pDb, long pKingdomId, double pUpdatedTime)
        {
            if (pDb == null || pKingdomId < 0L) return -1;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "UPDATE " + SessionTable +
                    " SET STAGE='cancelled',STATUS='cancelled'," +
                    "PLAYER_RANKING_PENDING=0,UPDATED_TIME=@time " +
                    "WHERE KINGDOM_ID=@kingdom AND STATUS IN " +
                    "('scheduled','running','stage_ranking','ranking_pending')";
                command.Parameters.AddWithValue("@time", pUpdatedTime);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                return command.ExecuteNonQuery();
            }
            catch { return -1; }
        }

        public static CivilServiceQualificationRecord LoadLatestQualification(
            SQLiteConnection pDb, long pActorId, long pKingdomId)
        {
            if (pDb == null || pActorId < 0L || pKingdomId < 0L) return null;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT C.ID,C.ACTOR_ID,C.KINGDOM_ID,C.SESSION_ID," +
                    "C.QUALIFICATION,S.CYCLE_YEAR,C.ENTRY_BONUS FROM " +
                    CandidateTable + " C JOIN " + SessionTable +
                    " S ON S.ID=C.SESSION_ID WHERE C.ACTOR_ID=@actor AND " +
                    "C.KINGDOM_ID=@kingdom AND C.QUALIFICATION<>'none' AND " +
                    "S.STATUS='completed' ORDER BY S.CYCLE_YEAR DESC," +
                    "C.ENTRY_BONUS DESC,C.ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@actor", pActorId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return null;
                return new CivilServiceQualificationRecord
                {
                    CandidateId = Convert.ToInt64(reader.GetValue(0)),
                    ActorId = Convert.ToInt64(reader.GetValue(1)),
                    KingdomId = Convert.ToInt64(reader.GetValue(2)),
                    SessionId = Convert.ToInt64(reader.GetValue(3)),
                    Qualification = Convert.ToString(reader.GetValue(4)) ?? "none",
                    ResultYear = Convert.ToInt32(reader.GetValue(5)),
                    EntryBonus = Convert.ToInt32(reader.GetValue(6))
                };
            }
            catch { return null; }
        }

        public static List<CivilServiceQualificationRecord>
            LoadLatestQualificationsPage(SQLiteConnection pDb,
                long pAfterCandidateId, int pLimit)
        {
            var result = new List<CivilServiceQualificationRecord>();
            if (pDb == null || pLimit <= 0) return result;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT C.ID,C.ACTOR_ID,C.KINGDOM_ID," +
                    "C.SESSION_ID,C.QUALIFICATION,S.CYCLE_YEAR,C.ENTRY_BONUS " +
                    "FROM " + CandidateTable + " C JOIN " + SessionTable +
                    " S ON S.ID=C.SESSION_ID WHERE C.ID>@after AND " +
                    "C.QUALIFICATION<>'none' AND S.STATUS='completed' AND " +
                    "NOT EXISTS (SELECT 1 FROM " + CandidateTable +
                    " C2 JOIN " + SessionTable +
                    " S2 ON S2.ID=C2.SESSION_ID WHERE C2.ACTOR_ID=C.ACTOR_ID " +
                    "AND C2.KINGDOM_ID=C.KINGDOM_ID AND " +
                    "C2.QUALIFICATION<>'none' AND S2.STATUS='completed' AND " +
                    "(S2.CYCLE_YEAR>S.CYCLE_YEAR OR " +
                    "(S2.CYCLE_YEAR=S.CYCLE_YEAR AND C2.ID>C.ID))) " +
                    "ORDER BY C.ID LIMIT @limit";
                command.Parameters.AddWithValue("@after", pAfterCandidateId);
                command.Parameters.AddWithValue("@limit", Math.Min(64, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new CivilServiceQualificationRecord
                    {
                        CandidateId = Convert.ToInt64(reader.GetValue(0)),
                        ActorId = Convert.ToInt64(reader.GetValue(1)),
                        KingdomId = Convert.ToInt64(reader.GetValue(2)),
                        SessionId = Convert.ToInt64(reader.GetValue(3)),
                        Qualification = Value(reader, 4),
                        ResultYear = Convert.ToInt32(reader.GetValue(5)),
                        EntryBonus = Convert.ToInt32(reader.GetValue(6))
                    });
                }
            }
            catch { result.Clear(); }
            return result;
        }

        private const string SessionProjection =
            "SELECT ID,KINGDOM_ID,KINGDOM_NAME,MODE,CYCLE_YEAR,STAGE,STATUS," +
            "OPEN_WORLD_DAY,NEXT_DUE_WORLD_DAY,HOST_RULER_ID,FINAL_RULER_ID," +
            "PLAYER_RANKING_PENDING,CANDIDATE_CURSOR,CENTRAL_VACANCIES," +
            "CITY_VACANCIES,WAITING_CANDIDATE_COUNT,RESERVE_TARGET," +
            "ADMISSION_QUOTA,UPDATED_TIME FROM " +
            "CivilServiceExamSession";

        private const string CandidateProjection =
            "SELECT ID,SESSION_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,HOME_CITY_ID," +
            "HOME_CITY_NAME,SOCIAL_ORIGIN,SCHOOL_ID,LOCAL_GRADE,LOCAL_SCORE," +
            "METROPOLITAN_SCORE,PALACE_SCORE,NATIONAL_SCORE," +
            "LOCAL_RESULT,METROPOLITAN_RESULT,PALACE_RESULT,NATIONAL_RESULT," +
            "CURRENT_STAGE_RESULT,QUALIFICATION,FINAL_RANK,FINAL_TITLE," +
            "ENTRY_BONUS,UPDATED_TIME FROM CivilServiceExamCandidate";

        private static CivilServiceExamSessionRecord ReadSession(
            SQLiteDataReader pReader)
        {
            return new CivilServiceExamSessionRecord
            {
                Id = Convert.ToInt64(pReader.GetValue(0)),
                KingdomId = Convert.ToInt64(pReader.GetValue(1)),
                KingdomName = Value(pReader, 2),
                Mode = Value(pReader, 3),
                CycleYear = Convert.ToInt32(pReader.GetValue(4)),
                Stage = Value(pReader, 5),
                Status = Value(pReader, 6),
                OpenWorldDay = Convert.ToInt64(pReader.GetValue(7)),
                NextDueWorldDay = Convert.ToInt64(pReader.GetValue(8)),
                HostRulerId = Convert.ToInt64(pReader.GetValue(9)),
                FinalRulerId = Convert.ToInt64(pReader.GetValue(10)),
                PlayerRankingPending = Convert.ToInt32(pReader.GetValue(11)) != 0,
                CandidateCursor = Convert.ToInt32(pReader.GetValue(12)),
                CentralVacancies = Convert.ToInt32(pReader.GetValue(13)),
                CityVacancies = Convert.ToInt32(pReader.GetValue(14)),
                WaitingCandidateCount = Convert.ToInt32(pReader.GetValue(15)),
                ReserveTarget = Convert.ToInt32(pReader.GetValue(16)),
                AdmissionQuota = Convert.ToInt32(pReader.GetValue(17)),
                UpdatedTime = Convert.ToDouble(pReader.GetValue(18))
            };
        }

        private static CivilServiceExamCandidateRecord ReadCandidate(
            SQLiteDataReader pReader)
        {
            var candidate = new CivilServiceExamCandidateRecord
            {
                Id = Convert.ToInt64(pReader.GetValue(0)),
                SessionId = Convert.ToInt64(pReader.GetValue(1)),
                KingdomId = Convert.ToInt64(pReader.GetValue(2)),
                ActorId = Convert.ToInt64(pReader.GetValue(3)),
                ActorName = Value(pReader, 4),
                HomeCityId = Convert.ToInt64(pReader.GetValue(5)),
                HomeCityName = Value(pReader, 6),
                SocialOrigin = Value(pReader, 7),
                SchoolId = Value(pReader, 8),
                LocalGrade = Convert.ToInt32(pReader.GetValue(9)),
                LocalScore = Convert.ToInt32(pReader.GetValue(10)),
                MetropolitanScore = Convert.ToInt32(pReader.GetValue(11)),
                PalaceScore = Convert.ToInt32(pReader.GetValue(12)),
                NationalScore = Convert.ToInt32(pReader.GetValue(13)),
                LocalResult = Value(pReader, 14),
                MetropolitanResult = Value(pReader, 15),
                PalaceResult = Value(pReader, 16),
                NationalResult = Value(pReader, 17),
                CurrentStageResult = Value(pReader, 18),
                Qualification = Value(pReader, 19),
                FinalRank = Convert.ToInt32(pReader.GetValue(20)),
                FinalTitle = Value(pReader, 21),
                EntryBonus = Convert.ToInt32(pReader.GetValue(22)),
                UpdatedTime = Convert.ToDouble(pReader.GetValue(23))
            };
            RepairLegacyStageResults(candidate);
            return candidate;
        }

        private static void RepairLegacyStageResults(
            CivilServiceExamCandidateRecord pCandidate)
        {
            if (pCandidate == null) return;
            pCandidate.LocalResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.Local,
                    pCandidate.LocalResult, pCandidate.LocalScore,
                    pCandidate.MetropolitanScore, pCandidate.PalaceScore,
                    pCandidate.NationalScore, pCandidate.Qualification,
                    pCandidate.CurrentStageResult);
            pCandidate.MetropolitanResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.Metropolitan,
                    pCandidate.MetropolitanResult, pCandidate.LocalScore,
                    pCandidate.MetropolitanScore, pCandidate.PalaceScore,
                    pCandidate.NationalScore, pCandidate.Qualification,
                    pCandidate.CurrentStageResult);
            pCandidate.PalaceResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.Palace,
                    pCandidate.PalaceResult, pCandidate.LocalScore,
                    pCandidate.MetropolitanScore, pCandidate.PalaceScore,
                    pCandidate.NationalScore, pCandidate.Qualification,
                    pCandidate.CurrentStageResult);
            pCandidate.NationalResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.National,
                    pCandidate.NationalResult, pCandidate.LocalScore,
                    pCandidate.MetropolitanScore, pCandidate.PalaceScore,
                    pCandidate.NationalScore, pCandidate.Qualification,
                    pCandidate.CurrentStageResult);
        }

        private static void AddSessionParameters(SQLiteCommand pCommand,
            CivilServiceExamSessionRecord pSession)
        {
            pCommand.Parameters.AddWithValue("@id", pSession.Id);
            pCommand.Parameters.AddWithValue("@kingdom", pSession.KingdomId);
            pCommand.Parameters.AddWithValue("@name", pSession.KingdomName ?? "");
            pCommand.Parameters.AddWithValue("@mode", pSession.Mode ?? "");
            pCommand.Parameters.AddWithValue("@year", pSession.CycleYear);
            pCommand.Parameters.AddWithValue("@stage", pSession.Stage ?? "scheduled");
            pCommand.Parameters.AddWithValue("@status", pSession.Status ?? "scheduled");
            pCommand.Parameters.AddWithValue("@open", pSession.OpenWorldDay);
            pCommand.Parameters.AddWithValue("@due", pSession.NextDueWorldDay);
            pCommand.Parameters.AddWithValue("@host", pSession.HostRulerId);
            pCommand.Parameters.AddWithValue("@final", pSession.FinalRulerId);
            pCommand.Parameters.AddWithValue("@pending", pSession.PlayerRankingPending ? 1 : 0);
            pCommand.Parameters.AddWithValue("@cursor", pSession.CandidateCursor);
            pCommand.Parameters.AddWithValue("@central_vacancies",
                pSession.CentralVacancies);
            pCommand.Parameters.AddWithValue("@city_vacancies",
                pSession.CityVacancies);
            pCommand.Parameters.AddWithValue("@waiting_candidate_count",
                pSession.WaitingCandidateCount);
            pCommand.Parameters.AddWithValue("@reserve_target",
                pSession.ReserveTarget);
            pCommand.Parameters.AddWithValue("@admission_quota",
                pSession.AdmissionQuota);
            pCommand.Parameters.AddWithValue("@time", pSession.UpdatedTime);
        }

        private static void AddCandidateParameters(SQLiteCommand pCommand,
            CivilServiceExamCandidateRecord pCandidate)
        {
            pCommand.Parameters.AddWithValue("@id", pCandidate.Id);
            pCommand.Parameters.AddWithValue("@session", pCandidate.SessionId);
            pCommand.Parameters.AddWithValue("@kingdom", pCandidate.KingdomId);
            pCommand.Parameters.AddWithValue("@actor", pCandidate.ActorId);
            pCommand.Parameters.AddWithValue("@name", pCandidate.ActorName ?? "");
            pCommand.Parameters.AddWithValue("@city", pCandidate.HomeCityId);
            pCommand.Parameters.AddWithValue("@city_name", pCandidate.HomeCityName ?? "");
            pCommand.Parameters.AddWithValue("@origin", pCandidate.SocialOrigin ?? "commoner");
            pCommand.Parameters.AddWithValue("@school", pCandidate.SchoolId ?? "");
            pCommand.Parameters.AddWithValue("@grade", pCandidate.LocalGrade);
            pCommand.Parameters.AddWithValue("@local", pCandidate.LocalScore);
            pCommand.Parameters.AddWithValue("@metro", pCandidate.MetropolitanScore);
            pCommand.Parameters.AddWithValue("@palace", pCandidate.PalaceScore);
            pCommand.Parameters.AddWithValue("@national", pCandidate.NationalScore);
            pCommand.Parameters.AddWithValue("@local_result",
                pCandidate.LocalResult ?? "pending");
            pCommand.Parameters.AddWithValue("@metro_result",
                pCandidate.MetropolitanResult ?? "pending");
            pCommand.Parameters.AddWithValue("@palace_result",
                pCandidate.PalaceResult ?? "pending");
            pCommand.Parameters.AddWithValue("@national_result",
                pCandidate.NationalResult ?? "pending");
            pCommand.Parameters.AddWithValue("@result", pCandidate.CurrentStageResult ?? "pending");
            pCommand.Parameters.AddWithValue("@qualification", pCandidate.Qualification ?? "none");
            pCommand.Parameters.AddWithValue("@rank", pCandidate.FinalRank);
            pCommand.Parameters.AddWithValue("@title", pCandidate.FinalTitle ?? "");
            pCommand.Parameters.AddWithValue("@bonus", pCandidate.EntryBonus);
            pCommand.Parameters.AddWithValue("@time", pCandidate.UpdatedTime);
        }

        private static long NextId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(ID),0)+1 FROM " + pTable;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static string Value(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex)
                ? ""
                : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static string StageScoreColumn(string pStage)
        {
            return pStage switch
            {
                "local" => "LOCAL_SCORE",
                "prefectural" => "LOCAL_SCORE",
                "metropolitan" => "METROPOLITAN_SCORE",
                "palace" => "PALACE_SCORE",
                "national" => "NATIONAL_SCORE",
                "ranking_imperial" => "PALACE_SCORE",
                "ranking_tribute" => "NATIONAL_SCORE",
                _ => ""
            };
        }
    }
}
