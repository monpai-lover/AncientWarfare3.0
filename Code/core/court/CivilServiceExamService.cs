using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceExamService
    {
        private readonly struct DueSession : IComparable<DueSession>
        {
            public DueSession(long pDueDay, long pSessionId)
            {
                DueDay = pDueDay;
                SessionId = pSessionId;
            }

            public long DueDay { get; }
            public long SessionId { get; }

            public int CompareTo(DueSession pOther)
            {
                int due = DueDay.CompareTo(pOther.DueDay);
                return due != 0 ? due : SessionId.CompareTo(pOther.SessionId);
            }
        }

        private sealed class ExamDemandSnapshot
        {
            public int CentralVacancies;
            public int CityVacancies;
            public int WaitingCandidateCount;
            public int ReserveTarget;
            public int AdmissionQuota;
        }

        private sealed class PendingRulerDeathWrite
        {
            internal long WorldGeneration;
            internal long SessionId;
            internal long KingdomId;
            internal long DueWorldDay;
        }

        private static readonly SortedSet<DueSession> DueSessions =
            new SortedSet<DueSession>();
        private static readonly Dictionary<long, CivilServiceExamSessionRecord>
            PlayerRankingByKingdom =
                new Dictionary<long, CivilServiceExamSessionRecord>();
        private static readonly Dictionary<long, PendingRulerDeathWrite>
            PendingRulerDeathWrites =
                new Dictionary<long, PendingRulerDeathWrite>();
        private static readonly Queue<long> RulerDeathRetryQueue =
            new Queue<long>();
        private static readonly HashSet<long> RulerDeathRetrySet =
            new HashSet<long>();
        private static readonly HashSet<string> InFlightRulerDeathWrites =
            new HashSet<string>(StringComparer.Ordinal);
        // Runtime creation and rebuild both populate DueSessions. Keep one
        // recovery lookup for legacy saves, never poll SQLite while idle.
        private static bool _dueSessionRecoveryPending = true;
        private const string ForeignInvitationYearKey =
            "aw3_civil_service_foreign_invitation_year";
        private const string ForeignInvitationCountKey =
            "aw3_civil_service_foreign_invitation_count";

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        internal static bool HasPendingRulerDeathPersistence =>
            PendingRulerDeathWrites.Count > 0;

        internal static bool PreparePendingRulerDeathPersistenceForSave()
        {
            if (PendingRulerDeathWrites.Count == 0) return true;
            var pending = new List<PendingRulerDeathWrite>(
                PendingRulerDeathWrites.Values);
            bool accepted = true;
            for (int i = 0; i < pending.Count; i++)
                accepted &= TryEnqueueRulerDeathWrite(pending[i]);
            return accepted;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (DB == null || pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral() ||
                !CivilServiceQualificationService.HasExaminationSystem(
                    pKingdom)) return;

            int year = Date.getCurrentYear();
            InviteForeignScholars(pKingdom, year);
            pKingdom.data.get(LineageKeys.CIVIL_SERVICE_EXAM_ANCHOR_YEAR,
                out int anchorYear, -1);
            if (anchorYear < 1)
            {
                pKingdom.data.set(
                    LineageKeys.CIVIL_SERVICE_EXAM_ANCHOR_YEAR,
                    CivilServiceExamRules.FirstOpeningYear(year));
                return;
            }
            if (!CivilServiceExamRules.IsCycleYear(year, anchorYear)) return;

            CivilServiceExamMode mode = CivilServiceExamRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
            if (!TryResolveDemandSnapshot(pKingdom, mode, out
                    ExamDemandSnapshot demand)) return;
            long openDay = DueWorldDay(year,
                CivilServiceExamStage.Scheduled, pKingdom.id, mode);
            var session = new CivilServiceExamSessionRecord
            {
                KingdomId = pKingdom.id,
                KingdomName = pKingdom.name ?? "",
                Mode = ModeValue(mode),
                CycleYear = year,
                Stage = "scheduled",
                Status = "scheduled",
                OpenWorldDay = openDay,
                NextDueWorldDay = openDay,
                HostRulerId = pKingdom.king?.data?.id ?? -1L,
                CentralVacancies = demand.CentralVacancies,
                CityVacancies = demand.CityVacancies,
                WaitingCandidateCount = demand.WaitingCandidateCount,
                ReserveTarget = demand.ReserveTarget,
                AdmissionQuota = demand.AdmissionQuota,
                UpdatedTime = LineageService.CurTime()
            };
            if (!CivilServiceExamPersistence.TryCreateSession(DB, session))
                return;
            Enqueue(session);
        }

        internal static int CandidateTargetForRealm(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral())
                return CivilServiceExamRules.SuggestedCandidateTarget;
            CivilServiceExamMode mode = CivilServiceExamRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
            if (!TryResolveDemandSnapshot(pKingdom, mode, out
                    ExamDemandSnapshot demand))
                return CivilServiceExamRules.SuggestedCandidateTarget;
            return CivilServiceExamRules.CandidateTarget(
                LivingPopulation(pKingdom), demand.AdmissionQuota);
        }

        private static bool TryResolveDemandSnapshot(Kingdom pKingdom,
            CivilServiceExamMode pMode, out ExamDemandSnapshot pDemand)
        {
            pDemand = null;
            SQLiteConnection db = DB;
            if (db == null || pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral()) return false;

            var hostCityIds = new List<long>();
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    hostCityIds.Add(city.data.id);
                }
            }
            catch { return false; }

            int cityCount = hostCityIds.Count;
            string[] centralOffices =
                CourtService.CentralOfficeIdsForCurrentProfile(pKingdom);
            int establishedPosts = centralOffices.Length + cityCount;
            int reserveTarget = CivilServiceExamRules.ReserveTarget(establishedPosts);
            if (!CivilServiceWaitingPoolQuery.TryLoadActorIds(db,
                    CivilServiceExamCandidateTableItem.GetTableName(),
                    CivilServiceExamSessionTableItem.GetTableName(),
                    ActorArchiveTableItem.GetTableName(),
                    CourtOfficerTableItem.GetTableName(),
                    SchoolAffiliationTableItem.GetTableName(), pKingdom.id,
                    hostCityIds, CivilServiceExamRules.CandidateLimit,
                    out IReadOnlyList<long> waitingActorIds)) return false;

            var hostCities = new HashSet<long>(hostCityIds);
            int waitingCandidateCount = 0;
            foreach (long actorId in waitingActorIds)
            {
                Actor actor = FindActor(actorId);
                if (IsWaitingCandidate(actor, pKingdom, hostCities))
                    waitingCandidateCount++;
            }

            int centralVacancies = CountCentralVacancies(pKingdom);
            int cityVacancies = CountCityVacancies(pKingdom);
            CivilServiceExamStage finalStage = pMode ==
                CivilServiceExamMode.Imperial
                    ? CivilServiceExamStage.Palace
                    : CivilServiceExamStage.National;
            int finalCapacity = CivilServiceExamRules.StageCapacity(pMode,
                finalStage, cityCount);
            pDemand = new ExamDemandSnapshot
            {
                CentralVacancies = centralVacancies,
                CityVacancies = cityVacancies,
                WaitingCandidateCount = waitingCandidateCount,
                ReserveTarget = reserveTarget,
                AdmissionQuota = CivilServiceExamRules.FinalAdmissionQuota(
                    centralVacancies, cityVacancies, waitingCandidateCount,
                    reserveTarget, finalCapacity)
            };
            return true;
        }

        private static bool IsWaitingCandidate(Actor pActor, Kingdom pHost,
            HashSet<long> pHostCityIds)
        {
            if (pActor?.data == null || pHost?.data == null) return false;
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            bool domestic = CourtAffiliationResolver.IsDomestic(pActor, pHost);
            bool hostQualifiedResident = affiliation != null &&
                affiliation.HomeKingdomId != pHost.id &&
                affiliation.ServiceKingdomId < 0L &&
                affiliation.LifecycleState ==
                    HistoricalSchoolLifecycleState.Resident &&
                pHostCityIds != null &&
                pHostCityIds.Contains(affiliation.ResidenceCityId);
            return CivilServiceExamRules.IsWaitingCandidate(
                pActor.isAlive() && !pActor.isRekt(), pActor.isAdult(),
                pActor.isSexMale(), SlaveService.IsSlave(pActor),
                HistoricalAffiliationService.IsAvailableForOffice(pActor),
                HasRuntimeOffice(pActor), domestic || hostQualifiedResident);
        }

        private static bool HasRuntimeOffice(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, "");
            if (!string.IsNullOrEmpty(officeId)) return true;
            try { return pActor.isCityLeader(); }
            catch { return false; }
        }

        private static void InviteForeignScholars(Kingdom pKingdom,
            int pYear)
        {
            City capital = pKingdom?.capital;
            if (capital?.data == null || capital.isRekt() ||
                capital.kingdom != pKingdom) return;
            int candidateTarget = CandidateTargetForRealm(pKingdom);
            int eligibleCount = CivilServiceExamCandidateQuery.
                CountEligibleForInvitation(pKingdom, pYear,
                    candidateTarget);
            pKingdom.data.get(ForeignInvitationYearKey,
                out int invitationYear, -1);
            pKingdom.data.get(ForeignInvitationCountKey,
                out int annualInvitedCount, 0);
            if (invitationYear != pYear) annualInvitedCount = 0;

            List<long> actorIds = CivilServiceExamCandidateQuery.
                LoadForeignInvitationActorIds(pKingdom, pYear,
                    CivilServiceExamRules.ForeignInvitationSourceLimit);
            int invitationCount = CivilServiceExamRules.ForeignInvitationCount(
                examinationEnabled: true, targetCandidates: candidateTarget,
                eligibleCount: eligibleCount,
                annualInvitedCount: annualInvitedCount,
                availableForeignCount: actorIds.Count);
            if (invitationCount <= 0) return;

            int invited = 0;
            foreach (long actorId in actorIds)
            {
                Actor actor = FindActor(actorId);
                if (!HistoricalSchoolTravelService.TryInviteToCity(actor,
                        capital, pYear)) continue;
                invited++;
                annualInvitedCount++;
                pKingdom.data.set(ForeignInvitationYearKey, pYear);
                pKingdom.data.set(ForeignInvitationCountKey,
                    annualInvitedCount);
                if (invited >= invitationCount) break;
            }
        }

        public static void ProcessAuthorityCycle()
        {
            RetryRulerDeathWrite();
            if (DB == null) return;
            CivilServiceLegacyTransitionService.ProcessVersionedBackfill();
            CivilServiceQualificationService.ProcessRuntimeRebuild();
            long day = CurrentWorldDay();
            CivilServiceExamSessionRecord session = TakeDueSession(day);
            if (session == null) return;

            Kingdom kingdom = FindKingdom(session.KingdomId);
            if (!IsLiveKingdom(kingdom))
            {
                CivilServiceExamPersistence.CancelActiveSession(DB,
                    session.Id, LineageService.CurTime());
                PlayerRankingByKingdom.Remove(session.KingdomId);
                return;
            }

            if (session.Stage == "scheduled")
                ProcessScheduled(session, kingdom, day);
            else if (session.Stage == "ranking")
                ProcessFinalRanking(session, kingdom);
            else if (session.Status == "stage_ranking")
                ProcessStageRanking(session, kingdom, day);
            else
                ProcessStageScores(session, kingdom, day);

            CivilServiceExamSessionRecord refreshed =
                CivilServiceExamPersistence.LoadSession(DB, session.Id);
            Enqueue(refreshed);
        }

        public static void ClearRuntime()
        {
            DueSessions.Clear();
            PlayerRankingByKingdom.Clear();
            PendingRulerDeathWrites.Clear();
            RulerDeathRetryQueue.Clear();
            RulerDeathRetrySet.Clear();
            InFlightRulerDeathWrites.Clear();
            _dueSessionRecoveryPending = true;
            CivilServiceQualificationService.ClearRuntime();
            CivilServiceLegacyTransitionService.ClearRuntime();
        }

        public static void RebuildRuntime()
        {
            DueSessions.Clear();
            PlayerRankingByKingdom.Clear();
            if (DB == null) return;
            foreach (CivilServiceExamSessionRecord session in
                     CivilServiceExamPersistence.LoadActiveSessions(DB))
                Enqueue(session);
            _dueSessionRecoveryPending = false;
        }

        public static void OnCurrentRulerDied(Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsApplying ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                pKingdom?.data == null || pKingdom.id < 0L)
                return;
            if (!PlayerRankingByKingdom.TryGetValue(pKingdom.id,
                    out CivilServiceExamSessionRecord session) ||
                !IsPlayerRankingPending(session)) return;
            long dueDay = CurrentWorldDay();
            DueSessions.Remove(new DueSession(session.NextDueWorldDay,
                session.Id));
            session.NextDueWorldDay = dueDay;
            session.PlayerRankingPending = false;
            PlayerRankingByKingdom.Remove(pKingdom.id);
            var pending = new PendingRulerDeathWrite
            {
                WorldGeneration = AWAsyncRuntime.WorldGeneration,
                SessionId = session.Id,
                KingdomId = pKingdom.id,
                DueWorldDay = dueDay
            };
            PendingRulerDeathWrites[session.Id] = pending;
            if (!TryEnqueueRulerDeathWrite(pending))
                QueueRulerDeathRetry(session.Id);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsApplying || DB == null ||
                pKingdom?.data == null || pKingdom.id < 0L) return;
            PlayerRankingByKingdom.Remove(pKingdom.id);
            List<CivilServiceExamSessionRecord> active =
                CivilServiceExamPersistence.LoadActiveSessions(DB);
            int cancelled = CivilServiceExamPersistence.
                CancelActiveSessionForKingdom(DB, pKingdom.id,
                    LineageService.CurTime());
            if (cancelled < 0) return;
            foreach (CivilServiceExamSessionRecord session in active)
            {
                if (session?.KingdomId != pKingdom.id) continue;
                DueSessions.Remove(new DueSession(session.NextDueWorldDay,
                    session.Id));
            }
        }

        public static bool TrySubmitPlayerRanking(long pKingdomId,
            long pSessionId, IReadOnlyList<long> pPreferredTopCandidateIds,
            out string pReasonKey)
        {
            pReasonKey = "aw_civil_service_exam_submit_failed";
            if (DB == null || AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                pReasonKey = "aw_civil_service_exam_read_only";
                return false;
            }
            Kingdom kingdom = FindKingdom(pKingdomId);
            CivilServiceExamSessionRecord session =
                CivilServiceExamPersistence.LoadSession(DB, pSessionId);
            if (!IsLiveKingdom(kingdom) || session == null ||
                session.KingdomId != pKingdomId ||
                session.Mode != "imperial_exam" ||
                session.Stage != "ranking" ||
                session.Status != "ranking_pending" ||
                !session.PlayerRankingPending)
            {
                pReasonKey = "aw_civil_service_exam_ranking_stale";
                return false;
            }
            List<CivilServiceExamCandidateRecord> finalists =
                CivilServiceExamPersistence.LoadPlayerRankingFinalists(DB,
                    pSessionId, CivilServiceExamRules.CandidateLimit);
            var facts = new List<CivilServiceRankingFacts>(finalists.Count);
            var byCandidate = new Dictionary<long,
                CivilServiceExamCandidateRecord>();
            foreach (CivilServiceExamCandidateRecord finalist in finalists)
            {
                facts.Add(new CivilServiceRankingFacts(finalist.Id,
                    finalist.ActorId, finalist.PalaceScore));
                byCandidate[finalist.Id] = finalist;
            }
            if (!CivilServiceExamRules.TryBuildPlayerRanking(facts,
                    pPreferredTopCandidateIds, out long[] orderedIds))
            {
                pReasonKey = "aw_civil_service_exam_ranking_invalid";
                return false;
            }

            var rankings = new List<CivilServiceExamRanking>(orderedIds.Length);
            var rankedCandidates = new List<CivilServiceExamCandidateRecord>(
                orderedIds.Length);
            for (int index = 0; index < orderedIds.Length; index++)
            {
                CivilServiceExamCandidateRecord candidate =
                    byCandidate[orderedIds[index]];
                int rank = index + 1;
                rankedCandidates.Add(candidate);
                rankings.Add(new CivilServiceExamRanking
                {
                    CandidateId = candidate.Id,
                    FinalRank = rank,
                    FinalTitle = ImperialRankTitle(rank),
                    EntryBonus = rank == 1 ? 2 : rank <= 3 ? 1 : 0
                });
            }
            if (!CivilServiceExamPersistence.FinalizeRanking(DB,
                    pSessionId, kingdom.king?.data?.id ?? -1L, rankings,
                    LineageService.CurTime())) return false;

            DueSessions.Remove(new DueSession(session.NextDueWorldDay,
                session.Id));
            PlayerRankingByKingdom.Remove(pKingdomId);
            RecordCommittedTopRanks(kingdom, session, rankedCandidates,
                rankings);
            CivilServiceQualificationService.RebuildRuntimeProjections();
            CourtService.FillVacanciesAfterCivilServiceExam(kingdom);
            ChronicleEvents.OnCivilServiceExamCompleted(kingdom,
                session.CycleYear, session.Mode);
            pReasonKey = "aw_civil_service_exam_submit_success";
            return true;
        }

        private static void ProcessScheduled(
            CivilServiceExamSessionRecord pSession, Kingdom pKingdom,
            long pDay)
        {
            List<CivilServiceExamCandidateRecord> candidates =
                CivilServiceExamCandidateQuery.Build(pKingdom, pSession.Id,
                    pSession.CycleYear, ParseMode(pSession.Mode));
            if (!CivilServiceExamRules.ShouldOpenCandidateRoll(candidates.Count))
            {
                CivilServiceExamPersistence.CompleteStage(DB, pSession.Id,
                    "scheduled", "scheduled", "scheduled",
                    SaturatingAdd(pDay,
                        CivilServiceExamRules.EmptyCandidateRetryDays),
                    LineageService.CurTime());
                return;
            }
            if (!CivilServiceExamPersistence.InsertCandidates(DB, candidates))
            {
                CivilServiceExamPersistence.CompleteStage(DB, pSession.Id,
                    "scheduled", "scheduled", "scheduled",
                    SaturatingAdd(pDay, 1L), LineageService.CurTime());
                return;
            }

            CivilServiceExamMode mode = ParseMode(pSession.Mode);
            CivilServiceExamStage first = mode == CivilServiceExamMode.Imperial
                ? CivilServiceExamStage.Local
                : CivilServiceExamStage.Prefectural;
            if (CivilServiceExamPersistence.CompleteStage(DB, pSession.Id,
                    "scheduled", StageValue(first), "running",
                    DueWorldDay(pSession.CycleYear, first, pKingdom.id, mode),
                    LineageService.CurTime()))
                ChronicleEvents.OnCivilServiceExamOpened(pKingdom,
                    pSession.CycleYear, pSession.Mode, candidates.Count);
        }

        private static void ProcessStageScores(
            CivilServiceExamSessionRecord pSession, Kingdom pKingdom,
            long pDay)
        {
            CivilServiceExamMode mode = ParseMode(pSession.Mode);
            CivilServiceExamStage stage = ParseStage(pSession.Stage);
            if (!IsScoredStage(mode, stage))
            {
                CivilServiceExamPersistence.CancelActiveSession(DB,
                    pSession.Id, LineageService.CurTime());
                return;
            }

            List<CivilServiceExamCandidateRecord> page =
                CivilServiceExamPersistence.LoadCandidatesPage(DB,
                    pSession.Id, pSession.CandidateCursor,
                    CivilServiceExamRules.AuthorityCandidateBudget);
            if (page.Count == 0)
            {
                CivilServiceExamPersistence.CompleteStage(DB,
                    pSession.Id, pSession.Stage, pSession.Stage,
                    "stage_ranking", pDay, LineageService.CurTime());
                return;
            }

            var updates = new List<CivilServiceExamCandidateUpdate>(page.Count);
            foreach (CivilServiceExamCandidateRecord candidate in page)
                updates.Add(BuildStageScoreUpdate(pSession, pKingdom,
                    candidate, mode, stage));
            if (CivilServiceExamPersistence.CommitCandidateBatch(DB,
                    pSession.Id, pSession.CandidateCursor, updates,
                    pSession.CandidateCursor + page.Count,
                    LineageService.CurTime()))
                RecordCommittedQualificationHistory(pKingdom, pSession,
                    page, updates);
        }

        private static void ProcessStageRanking(
            CivilServiceExamSessionRecord pSession, Kingdom pKingdom,
            long pDay)
        {
            CivilServiceExamMode mode = ParseMode(pSession.Mode);
            CivilServiceExamStage stage = ParseStage(pSession.Stage);
            List<CivilServiceExamCandidateRecord> page =
                CivilServiceExamPersistence.LoadStageRankingPage(DB,
                    pSession.Id, pSession.Stage,
                    CivilServiceExamRules.AuthorityCandidateBudget);
            if (page.Count == 0)
            {
                MoveToNextStage(pSession, pKingdom, mode, stage);
                return;
            }

            int quota = StageQuota(pSession, pKingdom, mode, stage);
            var updates = new List<CivilServiceExamCandidateUpdate>(page.Count);
            for (int index = 0; index < page.Count; index++)
            {
                CivilServiceExamCandidateRecord candidate = page[index];
                int position = pSession.CandidateCursor + index;
                bool passed = position < quota &&
                              CivilServiceExamRules.Passes(
                                  StageScore(candidate, stage));
                CivilServiceExamQualificationProgress(candidate, mode,
                    stage, passed, out string qualification,
                    out int entryBonus);
                CivilServiceExamCandidateUpdate update = Copy(candidate);
                update.StageResult = passed ? "passed" : "failed";
                SetStageResult(update, stage, update.StageResult);
                update.Qualification = qualification;
                update.EntryBonus = entryBonus;
                updates.Add(update);
            }
            if (CivilServiceExamPersistence.CommitCandidateBatch(DB,
                    pSession.Id, pSession.CandidateCursor, updates,
                    pSession.CandidateCursor + page.Count,
                    LineageService.CurTime()))
                RecordCommittedQualificationHistory(pKingdom, pSession,
                    page, updates);
        }

        private static void ProcessFinalRanking(
            CivilServiceExamSessionRecord pSession, Kingdom pKingdom)
        {
            CivilServiceExamMode mode = ParseMode(pSession.Mode);
            List<CivilServiceExamCandidateRecord> finalists =
                CivilServiceExamPersistence.LoadFinalRankingCandidates(DB,
                    pSession.Id, mode, CivilServiceExamRules.CandidateLimit);
            pKingdom.data.get(LineageKeys.COURT_DOMINANT_SCHOOL,
                out string dominantSchool, "");
            var facts = new List<CivilServiceAiRankingFacts>(finalists.Count);
            var byCandidate = new Dictionary<long,
                CivilServiceExamCandidateRecord>();
            foreach (CivilServiceExamCandidateRecord candidate in finalists)
            {
                int rawScore = mode == CivilServiceExamMode.Imperial
                    ? candidate.PalaceScore
                    : candidate.NationalScore;
                facts.Add(new CivilServiceAiRankingFacts(candidate.Id,
                    candidate.ActorId, rawScore, candidate.SchoolId));
                byCandidate[candidate.Id] = candidate;
            }
            IReadOnlyList<long> orderedIds =
                CivilServiceExamRules.BuildAiRanking(facts, dominantSchool,
                    RulerExamAbility(pKingdom.king));
            var page = new List<CivilServiceExamCandidateRecord>(
                CivilServiceExamRules.AuthorityCandidateBudget);
            foreach (long candidateId in orderedIds)
            {
                if (!byCandidate.TryGetValue(candidateId,
                        out CivilServiceExamCandidateRecord candidate) ||
                    candidate.FinalRank > 0) continue;
                page.Add(candidate);
                if (page.Count >= CivilServiceExamRules.
                        AuthorityCandidateBudget) break;
            }
            if (page.Count == 0)
            {
                if (CivilServiceExamPersistence.CompleteRanking(DB,
                        pSession.Id, pKingdom.king?.data?.id ?? -1L,
                        LineageService.CurTime()))
                {
                    CivilServiceQualificationService.
                        RebuildRuntimeProjections();
                    CourtService.FillVacanciesAfterCivilServiceExam(pKingdom);
                    ChronicleEvents.OnCivilServiceExamCompleted(pKingdom,
                        pSession.CycleYear, pSession.Mode);
                }
                return;
            }

            var rankings = new List<CivilServiceExamRanking>(page.Count);
            for (int index = 0; index < page.Count; index++)
            {
                int rank = pSession.CandidateCursor + index + 1;
                rankings.Add(new CivilServiceExamRanking
                {
                    CandidateId = page[index].Id,
                    FinalRank = rank,
                    FinalTitle = mode == CivilServiceExamMode.Imperial
                        ? ImperialRankTitle(rank)
                        : "",
                    EntryBonus = rank == 1 ? 2 : rank <= 3 ? 1 : 0
                });
            }
            if (CivilServiceExamPersistence.CommitFinalRankingBatch(DB,
                    pSession.Id, pSession.CandidateCursor, rankings,
                    pSession.CandidateCursor + page.Count,
                    LineageService.CurTime()))
                RecordCommittedTopRanks(pKingdom, pSession, page, rankings);
        }

        private static int RulerExamAbility(Actor pRuler)
        {
            if (pRuler?.data == null) return 50;
            int intelligence = CivilServiceExamRules.NormalizeActorAbility(
                SafeStat(pRuler, "intelligence"));
            int stewardship = CivilServiceExamRules.NormalizeActorAbility(
                SafeStat(pRuler, "stewardship"));
            int diplomacy = CivilServiceExamRules.NormalizeActorAbility(
                SafeStat(pRuler, "diplomacy"));
            return (intelligence + stewardship + diplomacy) / 3;
        }

        private static void RecordCommittedQualificationHistory(
            Kingdom pKingdom, CivilServiceExamSessionRecord pSession,
            IReadOnlyList<CivilServiceExamCandidateRecord> pBefore,
            IReadOnlyList<CivilServiceExamCandidateUpdate> pAfter)
        {
            int count = Math.Min(pBefore?.Count ?? 0, pAfter?.Count ?? 0);
            for (int index = 0; index < count; index++)
            {
                CivilServiceExamCandidateRecord before = pBefore[index];
                CivilServiceExamCandidateUpdate after = pAfter[index];
                if (before == null || after == null ||
                    string.IsNullOrEmpty(after.Qualification) ||
                    after.Qualification == "none" ||
                    string.Equals(before.Qualification, after.Qualification,
                        StringComparison.Ordinal)) continue;
                ChronicleEvents.OnCivilServiceQualification(pKingdom,
                    before.ActorId, before.ActorName, after.Qualification,
                    pSession.CycleYear);
            }
        }

        private static void RecordCommittedTopRanks(Kingdom pKingdom,
            CivilServiceExamSessionRecord pSession,
            IReadOnlyList<CivilServiceExamCandidateRecord> pCandidates,
            IReadOnlyList<CivilServiceExamRanking> pRankings)
        {
            int count = Math.Min(pCandidates?.Count ?? 0,
                pRankings?.Count ?? 0);
            for (int index = 0; index < count; index++)
            {
                CivilServiceExamRanking ranking = pRankings[index];
                CivilServiceExamCandidateRecord candidate =
                    pCandidates[index];
                if (candidate == null || ranking == null ||
                    ranking.FinalRank < 1 || ranking.FinalRank > 3) continue;
                ChronicleEvents.OnCivilServiceTopRanked(pKingdom,
                    candidate.ActorId, candidate.ActorName,
                    ranking.FinalRank, ranking.FinalTitle,
                    pSession.CycleYear);
            }
        }

        private static CivilServiceExamCandidateUpdate BuildStageScoreUpdate(
            CivilServiceExamSessionRecord pSession, Kingdom pKingdom,
            CivilServiceExamCandidateRecord pCandidate,
            CivilServiceExamMode pMode, CivilServiceExamStage pStage)
        {
            CivilServiceExamCandidateUpdate update = Copy(pCandidate);
            Actor actor = FindActor(pCandidate.ActorId);
            HistoricalSchoolAffiliationSnapshot affiliation = actor?.data ==
                null ? null : HistoricalAffiliationService.Get(actor.data.id);
            City residence = actor?.data == null
                ? null
                : HistoricalAffiliationService.ResidenceCity(actor);
            bool present = actor?.data != null &&
                CivilServiceExamRules.IsPresentAtHostExamination(
                    actor.isAlive(), actor.isRekt(), actor.kingdom == pKingdom,
                    affiliation?.LifecycleState ==
                        HistoricalSchoolLifecycleState.Resident,
                    residence?.data != null && residence.kingdom == pKingdom,
                    affiliation?.HomeKingdomId >= 0L &&
                    affiliation.HomeKingdomId != pKingdom.id);
            if (!present)
            {
                update.StageResult = "absent";
                SetStageResult(update, pStage, update.StageResult);
                return update;
            }

            CivilServiceQualification qualification =
                ParseQualification(pCandidate.Qualification);
            if (!ShouldSitStage(pCandidate, qualification, pMode, pStage,
                    out bool advanced))
            {
                if (advanced)
                {
                    update.StageResult = "advanced";
                    SetStageResult(update, pStage, update.StageResult);
                }
                return update;
            }

            int score = Score(actor, pSession.Id, pCandidate.ActorId,
                pStage);
            switch (pStage)
            {
                case CivilServiceExamStage.Local:
                case CivilServiceExamStage.Prefectural:
                    update.LocalScore = score;
                    break;
                case CivilServiceExamStage.Metropolitan:
                    update.MetropolitanScore = score;
                    break;
                case CivilServiceExamStage.Palace:
                    update.PalaceScore = score;
                    break;
                case CivilServiceExamStage.National:
                    update.NationalScore = score;
                    break;
            }
            update.StageResult = "scored";
            SetStageResult(update, pStage, update.StageResult);
            return update;
        }

        private static bool ShouldSitStage(
            CivilServiceExamCandidateRecord pCandidate,
            CivilServiceQualification pQualification,
            CivilServiceExamMode pMode, CivilServiceExamStage pStage,
            out bool pAdvanced)
        {
            pAdvanced = false;
            if (pMode == CivilServiceExamMode.Imperial)
            {
                if (pStage == CivilServiceExamStage.Local)
                {
                    pAdvanced = CivilServiceExamRules.CanEnterMetropolitan(
                                    pQualification) ||
                                pQualification == CivilServiceQualification.Jinshi;
                    return !pAdvanced;
                }
                if (pStage == CivilServiceExamStage.Metropolitan)
                    return pCandidate.CurrentStageResult == "passed" ||
                           pCandidate.CurrentStageResult == "advanced";
                if (pStage == CivilServiceExamStage.Palace)
                    return pCandidate.CurrentStageResult == "passed" &&
                           pQualification == CivilServiceQualification.Gongshi;
                return false;
            }

            if (pStage == CivilServiceExamStage.Prefectural)
            {
                pAdvanced = pQualification == CivilServiceQualification.Gongshi ||
                            pQualification == CivilServiceQualification.Jinshi;
                return !pAdvanced;
            }
            return pStage == CivilServiceExamStage.National &&
                   (pCandidate.CurrentStageResult == "passed" ||
                    pCandidate.CurrentStageResult == "advanced");
        }

        private static void MoveToNextStage(
            CivilServiceExamSessionRecord pSession, Kingdom pKingdom,
            CivilServiceExamMode pMode, CivilServiceExamStage pStage)
        {
            CivilServiceExamStage next = pStage switch
            {
                CivilServiceExamStage.Local =>
                    CivilServiceExamStage.Metropolitan,
                CivilServiceExamStage.Metropolitan =>
                    CivilServiceExamStage.Palace,
                CivilServiceExamStage.Prefectural =>
                    CivilServiceExamStage.National,
                CivilServiceExamStage.Palace => CivilServiceExamStage.Ranking,
                CivilServiceExamStage.National => CivilServiceExamStage.Ranking,
                _ => CivilServiceExamStage.Cancelled
            };
            if (next == CivilServiceExamStage.Cancelled)
            {
                CivilServiceExamPersistence.CancelActiveSession(DB,
                    pSession.Id, LineageService.CurTime());
                return;
            }
            long due = DueWorldDay(pSession.CycleYear, next,
                pKingdom.id, pMode);
            if (pMode == CivilServiceExamMode.Imperial &&
                pStage == CivilServiceExamStage.Palace)
            {
                CivilServiceExamPersistence.MarkPlayerRankingPending(DB,
                    pSession.Id, pSession.Stage, due,
                    LineageService.CurTime());
                return;
            }
            CivilServiceExamPersistence.CompleteStage(DB, pSession.Id,
                pSession.Stage, StageValue(next), "running", due,
                LineageService.CurTime());
        }

        private static CivilServiceExamCandidateUpdate Copy(
            CivilServiceExamCandidateRecord pCandidate)
        {
            return new CivilServiceExamCandidateUpdate
            {
                Id = pCandidate.Id,
                LocalScore = pCandidate.LocalScore,
                MetropolitanScore = pCandidate.MetropolitanScore,
                PalaceScore = pCandidate.PalaceScore,
                NationalScore = pCandidate.NationalScore,
                LocalResult = pCandidate.LocalResult,
                MetropolitanResult = pCandidate.MetropolitanResult,
                PalaceResult = pCandidate.PalaceResult,
                NationalResult = pCandidate.NationalResult,
                StageResult = pCandidate.CurrentStageResult,
                Qualification = pCandidate.Qualification,
                EntryBonus = pCandidate.EntryBonus
            };
        }

        private static void SetStageResult(
            CivilServiceExamCandidateUpdate pUpdate,
            CivilServiceExamStage pStage, string pResult)
        {
            if (pUpdate == null) return;
            string result = string.IsNullOrEmpty(pResult)
                ? "pending"
                : pResult;
            switch (pStage)
            {
                case CivilServiceExamStage.Local:
                case CivilServiceExamStage.Prefectural:
                    pUpdate.LocalResult = result;
                    break;
                case CivilServiceExamStage.Metropolitan:
                    pUpdate.MetropolitanResult = result;
                    break;
                case CivilServiceExamStage.Palace:
                    pUpdate.PalaceResult = result;
                    break;
                case CivilServiceExamStage.National:
                    pUpdate.NationalResult = result;
                    break;
            }
        }

        private static void CivilServiceExamQualificationProgress(
            CivilServiceExamCandidateRecord pCandidate,
            CivilServiceExamMode pMode, CivilServiceExamStage pStage,
            bool pPassed, out string pQualification, out int pEntryBonus)
        {
            pQualification = pCandidate.Qualification ?? "none";
            pEntryBonus = pCandidate.EntryBonus;
            if (!pPassed) return;
            CivilServiceQualification qualification =
                CivilServiceExamRules.QualificationAfterPass(pMode, pStage);
            if (qualification != CivilServiceQualification.None)
                pQualification = QualificationValue(qualification);
        }

        private static int Score(Actor pActor, long pSessionId,
            long pActorId, CivilServiceExamStage pStage)
        {
            int knowledge = CivilServiceExamRules.NormalizeActorAbility(
                SafeStat(pActor, "intelligence"));
            int stageAbility = pStage switch
            {
                CivilServiceExamStage.Local or
                CivilServiceExamStage.Prefectural =>
                    (CivilServiceExamRules.NormalizeActorAbility(
                         SafeStat(pActor, "stewardship")) +
                     CivilServiceExamRules.NormalizeActorAbility(
                         SafeStat(pActor, "diplomacy"))) / 2,
                CivilServiceExamStage.Metropolitan or
                CivilServiceExamStage.National =>
                    (CivilServiceExamRules.NormalizeActorAbility(
                         SafeStat(pActor, "intelligence")) +
                     CivilServiceExamRules.NormalizeActorAbility(
                         SafeStat(pActor, "stewardship"))) / 2,
                CivilServiceExamStage.Palace =>
                    (CivilServiceExamRules.NormalizeActorAbility(
                         SafeStat(pActor, "intelligence")) +
                     CivilServiceExamRules.NormalizeActorAbility(
                         SafeStat(pActor, "diplomacy"))) / 2,
                _ => knowledge
            };
            int education = EducationScore(pActor);
            return CivilServiceExamRules.Score(knowledge, stageAbility,
                education, CivilServiceExamRules.DeterministicJitter(
                    pSessionId, pActorId, pStage));
        }

        private static int EducationScore(Actor pActor)
        {
            SchoolMembershipRecord membership =
                SchoolMembershipService.GetActive(pActor.data.id);
            if (membership == null) return 0;
            int years = Math.Max(0,
                Date.getCurrentYear() - membership.StartYear);
            return Math.Min(100, 60 + Math.Min(30, years * 5) +
                                  Math.Min(10,
                                      (int)Math.Max(0f,
                                          membership.Reputation / 10f)));
        }

        private static int StageQuota(CivilServiceExamSessionRecord pSession,
            Kingdom pKingdom, CivilServiceExamMode pMode,
            CivilServiceExamStage pStage)
        {
            int cityCount = CountLiveCities(pKingdom);
            CivilServiceExamStage finalStage = pMode ==
                CivilServiceExamMode.Imperial
                    ? CivilServiceExamStage.Palace
                    : CivilServiceExamStage.National;
            int finalCapacity = CivilServiceExamRules.StageCapacity(pMode,
                finalStage, cityCount);
            int finalQuota = pSession?.AdmissionQuota >= 0
                ? pSession.AdmissionQuota
                : LegacyFinalAdmissionQuota(pKingdom, finalCapacity);
            int stageCap = CivilServiceExamRules.StageCapacity(pMode,
                pStage, cityCount);
            return CivilServiceExamRules.AdmissionQuotaForStage(pStage,
                finalQuota, stageCap);
        }

        private static int LegacyFinalAdmissionQuota(Kingdom pKingdom,
            int pFinalCapacity)
        {
            int central = CountCentralVacancies(pKingdom);
            int city = CountCityVacancies(pKingdom);
            int vacancies = Math.Max(0, central) + Math.Max(0, city);
            int legacyReserve = Math.Max(1, (vacancies + 3) / 4);
            return Math.Min(Math.Max(0, pFinalCapacity),
                vacancies + legacyReserve);
        }

        private static int CountCentralVacancies(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            string[] expected =
                CourtService.CentralOfficeIdsForCurrentProfile(pKingdom);
            if (expected.Length == 0) return 0;
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (CourtOfficerView officer in CourtService.GetActiveOfficers(
                         pKingdom, 96))
                if (officer?.layer == CourtOfficeLayer.Central &&
                    !string.IsNullOrEmpty(officer.office_id))
                    occupied.Add(officer.office_id);
            int count = 0;
            for (int index = 0; index < expected.Length; index++)
                if (!occupied.Contains(expected[index])) count++;
            return count;
        }

        private static int CountCityVacancies(Kingdom pKingdom)
        {
            int count = 0;
            if (pKingdom?.data == null) return count;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    Actor leader = city.leader;
                    if (leader?.data == null || leader.isRekt() ||
                        leader.city != city) count++;
                }
            }
            catch { }
            return count;
        }

        private static int StageScore(CivilServiceExamCandidateRecord pCandidate,
            CivilServiceExamStage pStage)
        {
            return pStage switch
            {
                CivilServiceExamStage.Local or
                CivilServiceExamStage.Prefectural => pCandidate.LocalScore,
                CivilServiceExamStage.Metropolitan =>
                    pCandidate.MetropolitanScore,
                CivilServiceExamStage.Palace => pCandidate.PalaceScore,
                CivilServiceExamStage.National => pCandidate.NationalScore,
                _ => -1
            };
        }

        private static CivilServiceExamSessionRecord TakeDueSession(long pDay)
        {
            while (DueSessions.Count > 0)
            {
                DueSession due = DueSessions.Min;
                if (due.DueDay > pDay) break;
                DueSessions.Remove(due);
                CivilServiceExamSessionRecord session =
                    CivilServiceExamPersistence.LoadSession(DB, due.SessionId);
                if (IsActive(session) && session.NextDueWorldDay <= pDay)
                    return session;
                Enqueue(session);
            }
            if (!_dueSessionRecoveryPending) return null;
            _dueSessionRecoveryPending = false;
            CivilServiceExamSessionRecord fallback =
                CivilServiceExamPersistence.LoadDueSession(DB, pDay);
            if (fallback != null)
                DueSessions.Remove(new DueSession(fallback.NextDueWorldDay,
                    fallback.Id));
            return fallback;
        }

        private static void Enqueue(CivilServiceExamSessionRecord pSession)
        {
            IndexPlayerRankingSession(pSession);
            if (!IsActive(pSession) || pSession.NextDueWorldDay < 0L) return;
            DueSessions.Add(new DueSession(pSession.NextDueWorldDay,
                pSession.Id));
        }

        private static void IndexPlayerRankingSession(
            CivilServiceExamSessionRecord pSession)
        {
            if (IsPlayerRankingPending(pSession))
            {
                PlayerRankingByKingdom[pSession.KingdomId] = pSession;
                return;
            }
            if (pSession != null &&
                PlayerRankingByKingdom.TryGetValue(pSession.KingdomId,
                    out CivilServiceExamSessionRecord indexed) &&
                indexed.Id == pSession.Id)
                PlayerRankingByKingdom.Remove(pSession.KingdomId);
        }

        private static bool IsPlayerRankingPending(
            CivilServiceExamSessionRecord pSession)
        {
            return pSession != null && pSession.KingdomId >= 0L &&
                   pSession.PlayerRankingPending &&
                   string.Equals(pSession.Mode, "imperial_exam",
                       StringComparison.Ordinal) &&
                   string.Equals(pSession.Stage, "ranking",
                       StringComparison.Ordinal) &&
                   string.Equals(pSession.Status, "ranking_pending",
                       StringComparison.Ordinal);
        }

        private static bool TryEnqueueRulerDeathWrite(
            PendingRulerDeathWrite pPending)
        {
            if (pPending == null ||
                pPending.WorldGeneration != AWAsyncRuntime.WorldGeneration)
                return false;
            var facts = new CivilServiceRulerDeathWriteFacts(
                pPending.SessionId, pPending.KingdomId,
                pPending.DueWorldDay, LineageService.CurTime());
            string operationKey = "civil-service-ruler-death:v1:" +
                pPending.WorldGeneration + ":" + pPending.KingdomId + ":" +
                pPending.SessionId + ":" + pPending.DueWorldDay;
            if (!InFlightRulerDeathWrites.Add(operationKey)) return true;
            bool accepted = HistoricalWriteService.TryEnqueueCustom(operationKey,
                (sequence, stamp) =>
                    new CivilServiceRulerDeathWriteEnvelope(sequence,
                        operationKey, stamp, facts),
                (sequence, outcome) => OnRulerDeathWriteCommitted(
                    pPending, outcome),
                (sequence, error) => OnRulerDeathWriteFailed(pPending),
                out _, out _);
            if (!accepted) InFlightRulerDeathWrites.Remove(operationKey);
            return accepted;
        }

        private static void OnRulerDeathWriteCommitted(
            PendingRulerDeathWrite pPending, object pOutcome)
        {
            if (pPending != null)
                InFlightRulerDeathWrites.Remove(
                    RulerDeathOperationKey(pPending));
            if (pPending == null ||
                pPending.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                !PendingRulerDeathWrites.TryGetValue(pPending.SessionId,
                    out PendingRulerDeathWrite current) ||
                !ReferenceEquals(current, pPending)) return;
            PendingRulerDeathWrites.Remove(pPending.SessionId);
            if (pOutcome is CivilServiceRulerDeathWriteResult result &&
                result.Accepted)
            {
                DueSessions.Add(new DueSession(pPending.DueWorldDay,
                    pPending.SessionId));
                return;
            }
            _dueSessionRecoveryPending = true;
        }

        private static void OnRulerDeathWriteFailed(
            PendingRulerDeathWrite pPending)
        {
            if (pPending != null)
                InFlightRulerDeathWrites.Remove(
                    RulerDeathOperationKey(pPending));
            if (pPending == null ||
                pPending.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                !PendingRulerDeathWrites.TryGetValue(pPending.SessionId,
                    out PendingRulerDeathWrite current) ||
                !ReferenceEquals(current, pPending)) return;
            QueueRulerDeathRetry(pPending.SessionId);
        }

        private static string RulerDeathOperationKey(
            PendingRulerDeathWrite pPending)
        {
            return "civil-service-ruler-death:v1:" +
                   pPending.WorldGeneration + ":" + pPending.KingdomId + ":" +
                   pPending.SessionId + ":" + pPending.DueWorldDay;
        }

        private static void QueueRulerDeathRetry(long pSessionId)
        {
            if (pSessionId < 0L || !RulerDeathRetrySet.Add(pSessionId))
                return;
            RulerDeathRetryQueue.Enqueue(pSessionId);
        }

        private static void RetryRulerDeathWrite()
        {
            if (RulerDeathRetryQueue.Count == 0) return;
            long sessionId = RulerDeathRetryQueue.Dequeue();
            RulerDeathRetrySet.Remove(sessionId);
            if (!PendingRulerDeathWrites.TryGetValue(sessionId,
                    out PendingRulerDeathWrite pending)) return;
            if (!TryEnqueueRulerDeathWrite(pending))
                QueueRulerDeathRetry(sessionId);
        }

        private static bool IsActive(CivilServiceExamSessionRecord pSession)
        {
            return pSession != null && (pSession.Status == "scheduled" ||
                   pSession.Status == "running" ||
                   pSession.Status == "stage_ranking" ||
                   pSession.Status == "ranking_pending");
        }

        private static bool IsScoredStage(CivilServiceExamMode pMode,
            CivilServiceExamStage pStage)
        {
            return pMode == CivilServiceExamMode.Imperial
                ? pStage == CivilServiceExamStage.Local ||
                  pStage == CivilServiceExamStage.Metropolitan ||
                  pStage == CivilServiceExamStage.Palace
                : pStage == CivilServiceExamStage.Prefectural ||
                  pStage == CivilServiceExamStage.National;
        }

        private static long DueWorldDay(int pYear,
            CivilServiceExamStage pStage, long pKingdomId,
            CivilServiceExamMode pMode)
        {
            long start = Math.Max(0L, (long)Math.Max(1, pYear) - 1L) * 360L;
            int percent = CivilServiceExamRules.StagePercent(pMode, pStage);
            long withinYear = Math.Max(0, percent) * 360L / 100L +
                              CivilServiceExamRules.KingdomOffsetDays(
                                  pKingdomId);
            return SaturatingAdd(start, Math.Min(359L, withinYear));
        }

        private static long CurrentWorldDay()
        {
            try
            {
                double time = Math.Max(0d,
                    World.world?.getCurWorldTime() ?? 0d);
                double day = Math.Floor(time * 6d);
                return day >= long.MaxValue ? long.MaxValue : (long)day;
            }
            catch { return 0L; }
        }

        private static long SaturatingAdd(long pValue, long pDelta)
        {
            if (pDelta <= 0L) return Math.Max(0L, pValue);
            return pValue > long.MaxValue - pDelta
                ? long.MaxValue
                : Math.Max(0L, pValue) + pDelta;
        }

        private static int CountLiveCities(Kingdom pKingdom)
        {
            int count = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && !city.isRekt()) count++;
            }
            catch { }
            return count;
        }

        private static int LivingPopulation(Kingdom pKingdom)
        {
            long population = 0L;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    population += Math.Max(0, city.getPopulationPeople());
                    if (population >= int.MaxValue) return int.MaxValue;
                }
            }
            catch { }
            return (int)Math.Max(0L, population);
        }

        private static int SafeStat(Actor pActor, string pStat)
        {
            try
            {
                return (int)Math.Max(0f, Math.Min(100f,
                    pActor?.stats?[pStat] ?? 0f));
            }
            catch { return 0; }
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   !pKingdom.isNeutral();
        }

        private static string ModeValue(CivilServiceExamMode pMode)
        {
            return pMode == CivilServiceExamMode.Imperial
                ? "imperial_exam"
                : "tributary_exam";
        }

        private static CivilServiceExamMode ParseMode(string pMode)
        {
            return pMode == "imperial_exam"
                ? CivilServiceExamMode.Imperial
                : CivilServiceExamMode.Tribute;
        }

        private static string StageValue(CivilServiceExamStage pStage)
        {
            return pStage switch
            {
                CivilServiceExamStage.Local => "local",
                CivilServiceExamStage.Prefectural => "prefectural",
                CivilServiceExamStage.Metropolitan => "metropolitan",
                CivilServiceExamStage.Palace => "palace",
                CivilServiceExamStage.National => "national",
                CivilServiceExamStage.Ranking => "ranking",
                CivilServiceExamStage.Completed => "completed",
                CivilServiceExamStage.Cancelled => "cancelled",
                _ => "scheduled"
            };
        }

        private static CivilServiceExamStage ParseStage(string pStage)
        {
            return pStage switch
            {
                "local" => CivilServiceExamStage.Local,
                "prefectural" => CivilServiceExamStage.Prefectural,
                "metropolitan" => CivilServiceExamStage.Metropolitan,
                "palace" => CivilServiceExamStage.Palace,
                "national" => CivilServiceExamStage.National,
                "ranking" => CivilServiceExamStage.Ranking,
                "completed" => CivilServiceExamStage.Completed,
                "cancelled" => CivilServiceExamStage.Cancelled,
                _ => CivilServiceExamStage.Scheduled
            };
        }

        private static CivilServiceQualification ParseQualification(
            string pValue)
        {
            return pValue switch
            {
                "juren" => CivilServiceQualification.Juren,
                "gongshi" => CivilServiceQualification.Gongshi,
                "jinshi" => CivilServiceQualification.Jinshi,
                _ => CivilServiceQualification.None
            };
        }

        private static string QualificationValue(
            CivilServiceQualification pQualification)
        {
            return pQualification switch
            {
                CivilServiceQualification.Juren => "juren",
                CivilServiceQualification.Gongshi => "gongshi",
                CivilServiceQualification.Jinshi => "jinshi",
                _ => "none"
            };
        }

        private static string ImperialRankTitle(int pRank)
        {
            return pRank switch
            {
                1 => "zhuangyuan",
                2 => "bangyan",
                3 => "tanhua",
                _ => ""
            };
        }
    }
}
