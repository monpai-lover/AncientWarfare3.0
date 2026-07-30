using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal sealed class CivilServiceExamSessionView
    {
        public long SessionId = -1L;
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

        public bool IsCompleted => Status == "completed";
        public bool IsActive => Status == "scheduled" ||
                                Status == "running" ||
                                Status == "stage_ranking" ||
                                Status == "ranking_pending";
    }

    internal sealed class CivilServiceExamCandidateView
    {
        public long CandidateId = -1L;
        public long SessionId = -1L;
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
        public string StageResult = "pending";
        public string Qualification = "none";
        public int FinalRank;
        public string FinalTitle = "";
        public int EntryBonus;
    }

    internal sealed class CivilServiceExamSnapshot
    {
        public long KingdomId = -1L;
        public CivilServiceExamSessionView SelectedSession;
        public IReadOnlyList<CivilServiceExamSessionView> Sessions =
            Array.Empty<CivilServiceExamSessionView>();
        public IReadOnlyList<CivilServiceExamCandidateView> Candidates =
            Array.Empty<CivilServiceExamCandidateView>();
    }

    internal static class CivilServiceExamReadModel
    {
        public const int SessionHistoryLimit = 24;
        public const int CandidateLimit = 96;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public static CivilServiceExamSnapshot Load(long pKingdomId,
            long pSelectedSessionId = -1L)
        {
            var snapshot = new CivilServiceExamSnapshot
                { KingdomId = pKingdomId };
            if (DB == null || pKingdomId < 0L) return snapshot;

            List<CivilServiceExamSessionView> sessions =
                LoadSessions(pKingdomId, SessionHistoryLimit);
            snapshot.Sessions = sessions;
            snapshot.SelectedSession = SelectSession(sessions,
                pSelectedSessionId);
            if (snapshot.SelectedSession != null)
                snapshot.Candidates = LoadCandidates(
                    snapshot.SelectedSession.SessionId, CandidateLimit);
            return snapshot;
        }

        public static CivilServiceExamMode ResolveMode(Kingdom pKingdom)
        {
            return CivilServiceExamRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
        }

        public static CivilServiceExamMode ResolveMode(
            CivilServiceExamSessionView pSession, Kingdom pKingdom)
        {
            if (pSession?.Mode == "imperial_exam")
                return CivilServiceExamMode.Imperial;
            if (pSession?.Mode == "tributary_exam" ||
                pSession?.Mode == "tribute_exam")
                return CivilServiceExamMode.Tribute;
            return ResolveMode(pKingdom);
        }

        public static string ModeLocalizationKey(
            CivilServiceExamSessionView pSession, Kingdom pKingdom)
        {
            return ResolveMode(pSession, pKingdom) ==
                   CivilServiceExamMode.Imperial
                ? "aw_civil_service_mode_imperial"
                : "aw_civil_service_mode_tribute";
        }

        public static string ModeLocalizationKey(Kingdom pKingdom)
        {
            return ModeLocalizationKey(null, pKingdom);
        }

        private static List<CivilServiceExamSessionView> LoadSessions(
            long pKingdomId, int pLimit)
        {
            var result = new List<CivilServiceExamSessionView>();
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT ID,KINGDOM_ID,KINGDOM_NAME,MODE,CYCLE_YEAR," +
                    "STAGE,STATUS,OPEN_WORLD_DAY,NEXT_DUE_WORLD_DAY," +
                    "HOST_RULER_ID,FINAL_RULER_ID,PLAYER_RANKING_PENDING," +
                    "CANDIDATE_CURSOR,CENTRAL_VACANCIES,CITY_VACANCIES," +
                    "WAITING_CANDIDATE_COUNT,RESERVE_TARGET," +
                    "ADMISSION_QUOTA FROM " +
                    CivilServiceExamSessionTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom ORDER BY " +
                    "CASE WHEN STATUS IN ('scheduled','running'," +
                    "'stage_ranking','ranking_pending') THEN 0 ELSE 1 END," +
                    "CYCLE_YEAR DESC,ID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@limit",
                    Math.Min(SessionHistoryLimit, Math.Max(1, pLimit)));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadSession(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        private static List<CivilServiceExamCandidateView> LoadCandidates(
            long pSessionId, int pLimit)
        {
            var result = new List<CivilServiceExamCandidateView>();
            if (pSessionId < 0L) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT ID,SESSION_ID,ACTOR_ID,ACTOR_NAME,HOME_CITY_ID,HOME_CITY_NAME," +
                    "SOCIAL_ORIGIN,SCHOOL_ID,LOCAL_GRADE,LOCAL_SCORE," +
                    "METROPOLITAN_SCORE,PALACE_SCORE,NATIONAL_SCORE," +
                    "LOCAL_RESULT,METROPOLITAN_RESULT,PALACE_RESULT," +
                    "NATIONAL_RESULT,CURRENT_STAGE_RESULT,QUALIFICATION,FINAL_RANK," +
                    "FINAL_TITLE,ENTRY_BONUS FROM " +
                    CivilServiceExamCandidateTableItem.GetTableName() +
                    " WHERE SESSION_ID=@session ORDER BY " +
                    "CASE WHEN FINAL_RANK>0 THEN 0 ELSE 1 END," +
                    "FINAL_RANK ASC,PALACE_SCORE DESC,NATIONAL_SCORE DESC," +
                    "ACTOR_ID ASC LIMIT @limit";
                command.Parameters.AddWithValue("@session", pSessionId);
                command.Parameters.AddWithValue("@limit",
                    Math.Min(CandidateLimit, Math.Max(1, pLimit)));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadCandidate(reader));
            }
            catch { result.Clear(); }
            return result;
        }

        private static CivilServiceExamSessionView SelectSession(
            IReadOnlyList<CivilServiceExamSessionView> pSessions,
            long pSelectedSessionId)
        {
            if (pSessions == null || pSessions.Count == 0) return null;
            if (pSelectedSessionId >= 0L)
                for (int index = 0; index < pSessions.Count; index++)
                    if (pSessions[index].SessionId == pSelectedSessionId)
                        return pSessions[index];
            for (int index = 0; index < pSessions.Count; index++)
                if (pSessions[index].IsActive) return pSessions[index];
            return pSessions[0];
        }

        private static CivilServiceExamSessionView ReadSession(
            SQLiteDataReader pReader)
        {
            return new CivilServiceExamSessionView
            {
                SessionId = Convert.ToInt64(pReader.GetValue(0)),
                KingdomId = Convert.ToInt64(pReader.GetValue(1)),
                KingdomName = Text(pReader, 2),
                Mode = Text(pReader, 3),
                CycleYear = Convert.ToInt32(pReader.GetValue(4)),
                Stage = Text(pReader, 5),
                Status = Text(pReader, 6),
                OpenWorldDay = Convert.ToInt64(pReader.GetValue(7)),
                NextDueWorldDay = Convert.ToInt64(pReader.GetValue(8)),
                HostRulerId = Convert.ToInt64(pReader.GetValue(9)),
                FinalRulerId = Convert.ToInt64(pReader.GetValue(10)),
                PlayerRankingPending =
                    Convert.ToInt32(pReader.GetValue(11)) != 0,
                CandidateCursor = Convert.ToInt32(pReader.GetValue(12)),
                CentralVacancies = Convert.ToInt32(pReader.GetValue(13)),
                CityVacancies = Convert.ToInt32(pReader.GetValue(14)),
                WaitingCandidateCount = Convert.ToInt32(pReader.GetValue(15)),
                ReserveTarget = Convert.ToInt32(pReader.GetValue(16)),
                AdmissionQuota = Convert.ToInt32(pReader.GetValue(17))
            };
        }

        private static CivilServiceExamCandidateView ReadCandidate(
            SQLiteDataReader pReader)
        {
            var candidate = new CivilServiceExamCandidateView
            {
                CandidateId = Convert.ToInt64(pReader.GetValue(0)),
                SessionId = Convert.ToInt64(pReader.GetValue(1)),
                ActorId = Convert.ToInt64(pReader.GetValue(2)),
                ActorName = Text(pReader, 3),
                HomeCityId = Convert.ToInt64(pReader.GetValue(4)),
                HomeCityName = Text(pReader, 5),
                SocialOrigin = Text(pReader, 6),
                SchoolId = Text(pReader, 7),
                LocalGrade = Convert.ToInt32(pReader.GetValue(8)),
                LocalScore = Convert.ToInt32(pReader.GetValue(9)),
                MetropolitanScore = Convert.ToInt32(pReader.GetValue(10)),
                PalaceScore = Convert.ToInt32(pReader.GetValue(11)),
                NationalScore = Convert.ToInt32(pReader.GetValue(12)),
                LocalResult = Text(pReader, 13),
                MetropolitanResult = Text(pReader, 14),
                PalaceResult = Text(pReader, 15),
                NationalResult = Text(pReader, 16),
                StageResult = Text(pReader, 17),
                Qualification = Text(pReader, 18),
                FinalRank = Convert.ToInt32(pReader.GetValue(19)),
                FinalTitle = Text(pReader, 20),
                EntryBonus = Convert.ToInt32(pReader.GetValue(21))
            };
            candidate.LocalResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.Local,
                    candidate.LocalResult, candidate.LocalScore,
                    candidate.MetropolitanScore, candidate.PalaceScore,
                    candidate.NationalScore, candidate.Qualification,
                    candidate.StageResult);
            candidate.MetropolitanResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.Metropolitan,
                    candidate.MetropolitanResult, candidate.LocalScore,
                    candidate.MetropolitanScore, candidate.PalaceScore,
                    candidate.NationalScore, candidate.Qualification,
                    candidate.StageResult);
            candidate.PalaceResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.Palace,
                    candidate.PalaceResult, candidate.LocalScore,
                    candidate.MetropolitanScore, candidate.PalaceScore,
                    candidate.NationalScore, candidate.Qualification,
                    candidate.StageResult);
            candidate.NationalResult = CivilServiceExamRules.
                RepairLegacyStageResult(CivilServiceExamStage.National,
                    candidate.NationalResult, candidate.LocalScore,
                    candidate.MetropolitanScore, candidate.PalaceScore,
                    candidate.NationalScore, candidate.Qualification,
                    candidate.StageResult);
            return candidate;
        }

        private static string Text(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex)
                ? ""
                : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }
    }
}
