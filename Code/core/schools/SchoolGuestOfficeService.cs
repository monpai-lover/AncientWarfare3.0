using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    /// <summary>
    /// Coordinates cross-state appointments for scholars who are physically resident
    /// in a host city. Nationality remains the affiliation snapshot's home kingdom;
    /// one durable transaction starts service and another closes affiliation plus career;
    /// live projections are adopted only after either transaction is proven committed.
    /// </summary>
    internal static class SchoolGuestOfficeService
    {
        private const int MaxHostCitiesPerYear = 128;
        private const int MaxGuestCandidatesPerHost = 192;
        private const int MaxServiceSweepPerYear = 512;
        private const int MaxPendingGuestOperations = 512;
        private const int MaxPendingGuestRecoveryScan = 2048;
        private const int MaxPendingGuestDrainPerFrame = 8;
        private const int MaxPendingGuestScanPerFrame = 32;
        private const int MaxPendingGuestRetryBackoffFrames = 3600;
        private static int _lastProcessYear = -1;
        private static int _serviceSweepOffset;
        private static int _pendingFrame;
        private static readonly Dictionary<long, PendingGuestOffice> Pending =
            new Dictionary<long, PendingGuestOffice>();
        private static readonly Queue<PendingGuestOffice> PendingOrder =
            new Queue<PendingGuestOffice>();
        private static AnnualGuestWork _annualWork;

        private sealed class AnnualGuestWork
        {
            public AnnualGuestWork(int pYear, long[] pServingActorIds,
                int pServiceStart, int pServiceCount)
            {
                Year = pYear;
                ServingActorIds = pServingActorIds ?? Array.Empty<long>();
                ServiceStart = pServiceStart;
                ServiceCursor = new HistoricalSchoolBoundedWorkCursor(
                    pServiceStart, pServiceCount, ServingActorIds.Length);
            }

            public int Year { get; }
            public long[] ServingActorIds { get; }
            public int ServiceStart { get; }
            public HistoricalSchoolBoundedWorkCursor ServiceCursor { get; }
        }

        internal sealed class VacancyCandidateSession
        {
            internal VacancyCandidateSession(
                List<GuestCandidateProfile> pCandidates,
                HashSet<long> pAppointedActors)
            {
                Candidates = pCandidates ?? new List<GuestCandidateProfile>();
                AppointedActors = pAppointedActors ?? new HashSet<long>();
            }

            internal List<GuestCandidateProfile> Candidates { get; }
            internal HashSet<long> AppointedActors { get; }
        }

        public static void LoadState()
        {
            _lastProcessYear = -1;
            _serviceSweepOffset = 0;
            _pendingFrame = 0;
            _annualWork = null;
            Pending.Clear();
            PendingOrder.Clear();
            SeedPendingRecovery();
        }

        public static void ClearRuntime()
        {
            _lastProcessYear = -1;
            _serviceSweepOffset = 0;
            _pendingFrame = 0;
            _annualWork = null;
            Pending.Clear();
            PendingOrder.Clear();
        }

        public static void ProcessPendingFrame()
        {
            _pendingFrame++;
            int scanCount = GuestOfficePendingRules.DrainCount(PendingOrder.Count,
                MaxPendingGuestScanPerFrame);
            int attempts = 0;
            for (int index = 0; index < scanCount; index++)
            {
                PendingGuestOffice pending = PendingOrder.Dequeue();
                long actorId = pending?.ActorId ?? -1L;
                if (!Pending.TryGetValue(actorId, out PendingGuestOffice current) ||
                    !ReferenceEquals(current, pending)) continue;
                if (pending.ReadyFrame > _pendingFrame ||
                    attempts >= MaxPendingGuestDrainPerFrame)
                {
                    PendingOrder.Enqueue(pending);
                    continue;
                }
                attempts++;
                bool completed = false;
                try
                {
                    completed = TryProcessPending(pending);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Pending guest projection retry failed for actor " +
                                        actorId + ": " + error.Message);
                }
                if (completed)
                {
                    Pending.Remove(actorId);
                    continue;
                }
                if (Pending.ContainsKey(actorId))
                {
                    SchedulePendingRetry(pending);
                    PendingOrder.Enqueue(pending);
                }
            }
        }

        public static bool ProcessYearFrame(int pYear)
        {
            if (pYear < 0 || _lastProcessYear == pYear) return true;
            if (_annualWork == null || _annualWork.Year != pYear)
                _annualWork = BeginAnnualWork(pYear);
            AnnualGuestWork work = _annualWork;

            try
            {
                if (work.ServiceCursor.TryTake(out int serviceIndex))
                {
                    ProcessServiceActor(work.ServingActorIds[serviceIndex], pYear);
                    return false;
                }

                return FinishAnnualWork(work);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school guest office tick failed: " +
                                    error.Message);
                return false;
            }
        }

        private static AnnualGuestWork BeginAnnualWork(int pYear)
        {
            long[] actorIds = HistoricalSchoolRuntimeIndex.Instance.ServingActorIds();
            int start = actorIds.Length == 0
                ? 0
                : _serviceSweepOffset % actorIds.Length;
            int count = Math.Min(MaxServiceSweepPerYear, actorIds.Length);
            return new AnnualGuestWork(pYear, actorIds, start, count);
        }

        private static bool FinishAnnualWork(AnnualGuestWork pWork)
        {
            if (pWork == null) return true;
            if (pWork.ServingActorIds.Length > 0)
                _serviceSweepOffset = (pWork.ServiceStart +
                    pWork.ServiceCursor.Processed) % pWork.ServingActorIds.Length;
            _lastProcessYear = pWork.Year;
            if (ReferenceEquals(_annualWork, pWork)) _annualWork = null;
            return true;
        }

        private static void ProcessServiceActor(long pActorId, int pYear)
        {
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActorId);
            if (state?.LifecycleState != HistoricalSchoolLifecycleState.Serving ||
                state.ServiceKingdomId < 0) return;
            Actor actor = FindActor(state.ActorId);
            Kingdom host = FindKingdom(state.ServiceKingdomId);
            bool alive = actor?.data != null && actor.isAlive() && !actor.isRekt();
            bool hostAlive = host?.data != null && !host.isRekt();
            City residence = alive ? HistoricalAffiliationService.ResidenceCity(actor) : null;
            bool residenceValid = residence?.data != null && !residence.isRekt() &&
                                  residence.kingdom == host;
            bool durableEnvironmentValid = alive && hostAlive && residenceValid &&
                                           CourtService.HasOfficialCourt(host);
            bool projectionValid = durableEnvironmentValid &&
                                   IsGuestProjectionValid(actor, host);
            int remaining = state.ServiceEndYear < 0
                ? 0
                : state.ServiceEndYear - pYear;

            if (projectionValid && remaining <= 0 && TryRenew(actor, host, state, pYear))
                return;
            if (projectionValid && remaining > 0) return;

            if (!projectionValid && durableEnvironmentValid && remaining > 0)
            {
                RegisterPendingRecovery(state);
                if (Pending.TryGetValue(state.ActorId,
                        out PendingGuestOffice pending))
                {
                    if (TryProcessPending(pending)) Pending.Remove(state.ActorId);
                    else SchedulePendingRetry(pending);
                }
                return;
            }

            string reason = !hostAlive
                ? "guest_host_lost"
                : actor?.data == null
                    ? "guest_actor_missing"
                    : !alive
                        ? "guest_actor_dead"
                        : "guest_term_expired";
            EndGuestOfficer(state.ActorId, actor, host, reason, pYear);
        }

        internal static VacancyCandidateSession CreateVacancyCandidateSession(
            Kingdom pHost)
        {
            int year = Date.getCurrentYear();
            return new VacancyCandidateSession(BuildCandidateIndex(pHost, year),
                new HashSet<long>(Pending.Values.Where(p => p != null &&
                        !p.PersistenceCleanFailure &&
                        p.EndExpectedAffiliation == null &&
                        p.HostKingdomId == (pHost?.id ?? -1L))
                    .Select(p => p.ActorId)));
        }

        internal static CourtVacancyOutcome TryFillRegisteredVacancy(
            Kingdom pHost, string pOfficeId,
            VacancyCandidateSession pSession)
        {
            if (pHost?.data == null || pHost.isRekt() ||
                string.IsNullOrEmpty(pOfficeId) || pSession == null)
                return CourtVacancyOutcome.Invalid;
            if (CourtService.GetActiveOfficers(pHost, 96).Any(row =>
                    row != null && row.layer == CourtOfficeLayer.Central &&
                    row.office_id == pOfficeId) ||
                IsOfficeReserved(pHost.id, pOfficeId))
                return CourtVacancyOutcome.Invalid;

            int year = Date.getCurrentYear();
            GuestCandidate candidate = SelectCandidate(pHost, pOfficeId, year,
                pSession.Candidates, pSession.AppointedActors,
                pAllowActing: true);
            if (candidate == null) return CourtVacancyOutcome.NoCandidate;
            City residence = HistoricalAffiliationService.ResidenceCity(
                candidate.Actor);
            int term = SchoolGuestOfficeRules.TermYears(
                candidate.Actor.data.id, pHost.id, year);
            GuestOfficeSubmissionOutcome submission = TryAppointAndRecord(
                candidate.Actor, pHost, pOfficeId, residence, year, term,
                "guest_service_started", candidate.IsActing);
            if (!SchoolGuestOfficeRules.ReservesOffice(submission))
                return CourtVacancyOutcome.TechnicalFailure;
            pSession.AppointedActors.Add(candidate.Actor.data.id);
            return CourtVacancyOutcome.Filled;
        }

        internal static bool IsOfficeReserved(long pHostKingdomId,
            string pOfficeId)
        {
            if (pHostKingdomId < 0 || string.IsNullOrEmpty(pOfficeId))
                return false;
            foreach (PendingGuestOffice pending in Pending.Values)
                if (pending != null && !pending.PersistenceCleanFailure &&
                    pending.EndExpectedAffiliation == null &&
                    pending.HostKingdomId == pHostKingdomId &&
                    pending.OfficeId == pOfficeId) return true;
            return false;
        }

        private static bool TryRenew(Actor pActor, Kingdom pHost,
            HistoricalSchoolAffiliationSnapshot pState, int pYear)
        {
            if (pActor?.data == null || pHost?.data == null || pState == null ||
                pState.ServiceEndYear < 0 || pState.ServiceEndYear > pYear) return false;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                pActor.data.id);
            if (membership == null || !SchoolGuestOfficeRules.ShouldRenew(
                    ScholarReputation(pActor, membership), HostReceptiveness(pHost), 0,
                    pHost.data != null && !pHost.isRekt(), pActor.isAlive())) return false;

            int term = SchoolGuestOfficeRules.TermYears(pActor.data.id, pHost.id, pYear);
            int renewedEndYear = SchoolGuestOfficeRules.RenewedEndYear(pYear, term);
            if (!OfficialCareerStateService.ExtendTermEndYear(
                    pActor, pHost, renewedEndYear)) return false;
            return HistoricalAffiliationService.RenewService(
                pActor, renewedEndYear);
        }

        internal static bool EndGuestOfficer(Actor pActor, Kingdom pHost, string pReason,
            int pYear)
        {
            return EndGuestOfficer(pActor?.data?.id ?? -1L, pActor, pHost, pReason, pYear);
        }

        internal static bool EndGuestOfficer(long pActorId, Kingdom pHost, string pReason,
            int pYear)
        {
            return EndGuestOfficer(pActorId, FindActor(pActorId), pHost, pReason, pYear);
        }

        private static bool EndGuestOfficer(long pActorId, Actor pActor, Kingdom pHost,
            string pReason, int pYear)
        {
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActorId);
            if (state?.LifecycleState != HistoricalSchoolLifecycleState.Serving ||
                state.ServiceKingdomId < 0 ||
                (pHost?.data != null && state.ServiceKingdomId != pHost.id)) return false;

            if (!TryGetPendingEnd(pActorId, out PendingGuestOffice pending))
            {
                pending = PendingGuestOffice.ForEnd(state, pActor,
                    pReason ?? "guest_term", pYear, LineageService.CurTime());
                if (!RegisterPendingEnd(pending)) return false;
            }
            bool completed = TryProcessPending(pending);
            if (completed) Pending.Remove(pending.ActorId);
            else SchedulePendingRetry(pending);
            return completed && GuestOfficeEndPendingRules.CanOpenNextTerm(
                pending.PersistenceOutcome);
        }

        private static bool TryGetPendingEnd(long pActorId,
            out PendingGuestOffice pPending)
        {
            if (Pending.TryGetValue(pActorId, out pPending) &&
                pPending?.EndExpectedAffiliation != null &&
                !pPending.RecoverCommittedEnd) return true;
            pPending = null;
            return false;
        }

        private static GuestOfficeSubmissionOutcome TryAppointAndRecord(
            Actor pActor, Kingdom pHost, string pOffice, City pResidence,
            int pStartYear, int pTermYears, string pEventType, bool pActing)
        {
            pTermYears = SchoolGuestOfficeRules.NormalizeTermYears(pTermYears);
            if (pActor?.data == null || pHost?.data == null || pResidence?.data == null ||
                pTermYears < SchoolGuestOfficeRules.MinTermYears ||
                pTermYears > SchoolGuestOfficeRules.MaxTermYears)
                return GuestOfficeSubmissionOutcome.Rejected;
            City appointmentCapital = pHost.capital;
            if (appointmentCapital?.data == null || appointmentCapital.isRekt() ||
                appointmentCapital.kingdom != pHost)
                return GuestOfficeSubmissionOutcome.Rejected;
            int endYear = pStartYear + pTermYears;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            if (!CourtService.CanAppointGuestOfficer(pActor, pHost, pOffice,
                    pResidence, pActing) || string.IsNullOrEmpty(school))
                return GuestOfficeSubmissionOutcome.Rejected;
            HistoricalSchoolAffiliationSnapshot expected =
                HistoricalAffiliationService.Get(pActor.data.id);
            double worldTime = LineageService.CurTime();
            OfficialCareerAppointment appointment = OfficialCareerService.PrepareAppointment(
                pActor, pHost, CourtOfficeLayer.Central, pOffice, school, appointmentCapital,
                pStartYear, worldTime, pActing: pActing,
                pVacancyPromotion: !pActing);
            OfficialCareerPrior runtimePrior =
                CourtService.CaptureRuntimeOfficerProjection(pActor);
            GuestOfficeStartRequest request = GuestOfficePersistence.PrepareStart(expected,
                appointment, pEventType,
                school, pOffice, pHost.id, appointmentCapital.data.id, pStartYear, endYear,
                worldTime);
            if (request == null) return GuestOfficeSubmissionOutcome.Rejected;
            var pending = PendingGuestOffice.ForStart(request, runtimePrior);
            if (!RegisterPendingStart(pending))
                return GuestOfficeSubmissionOutcome.Rejected;
            bool completed = TryProcessPending(pending);
            if (completed) Pending.Remove(pending.ActorId);
            else SchedulePendingRetry(pending);
            return completed
                ? GuestOfficeSubmissionOutcome.Completed
                : GuestOfficeSubmissionOutcome.Queued;
        }

        private static bool RegisterPendingStart(PendingGuestOffice pPending)
        {
            if (pPending == null || pPending.ActorId < 0 ||
                Pending.ContainsKey(pPending.ActorId) ||
                Pending.Count >= MaxPendingGuestOperations) return false;
            Pending.Add(pPending.ActorId, pPending);
            PendingOrder.Enqueue(pPending);
            return true;
        }

        private static bool RegisterPendingEnd(PendingGuestOffice pPending)
        {
            if (pPending == null || pPending.ActorId < 0) return false;
            bool replacing = Pending.ContainsKey(pPending.ActorId);
            if (!replacing && Pending.Count >= MaxPendingGuestOperations) return false;
            Pending[pPending.ActorId] = pPending;
            PendingOrder.Enqueue(pPending);
            return true;
        }

        private static bool RegisterPendingRecovery(
            HistoricalSchoolAffiliationSnapshot pState)
        {
            if (pState == null || pState.ActorId < 0 ||
                pState.LifecycleState != HistoricalSchoolLifecycleState.Serving) return false;
            if (Pending.ContainsKey(pState.ActorId)) return true;
            if (Pending.Count >= MaxPendingGuestOperations) return false;
            PendingGuestOffice pending = PendingGuestOffice.ForRecovery(pState);
            Pending.Add(pending.ActorId, pending);
            PendingOrder.Enqueue(pending);
            return true;
        }

        private static bool RegisterPendingEndRecovery(
            HistoricalSchoolAffiliationSnapshot pState)
        {
            if (pState == null || pState.ActorId < 0 || pState.ServiceKingdomId >= 0 ||
                pState.ServiceStartYear >= 0 || pState.ServiceEndYear < 0 ||
                (pState.LifecycleState != HistoricalSchoolLifecycleState.AtHome &&
                 pState.LifecycleState != HistoricalSchoolLifecycleState.Resident))
                return false;
            Actor actor = FindActor(pState.ActorId);
            if (actor?.data == null) return false;
            actor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long runtimeKingdomId, -1L);
            actor.data.get(LineageKeys.COURT_LAYER, out string runtimeLayer, "");
            actor.data.get(LineageKeys.COURT_OFFICE_ID, out string runtimeOffice, "");
            bool hasRuntimeProjection = runtimeKingdomId >= 0 &&
                runtimeLayer == CourtOfficeLayer.Central &&
                !string.IsNullOrEmpty(runtimeOffice);
            bool hasGuestStatus;
            try { hasGuestStatus = actor.hasStatus(HistoricalSchoolContent.GuestStatusId); }
            catch { hasGuestStatus = false; }
            if (!hasRuntimeProjection && !hasGuestStatus) return false;
            if (!hasRuntimeProjection)
            {
                runtimeKingdomId = -1L;
                runtimeOffice = "";
            }
            if (Pending.ContainsKey(pState.ActorId)) return true;
            if (Pending.Count >= MaxPendingGuestOperations) return false;
            PendingGuestOffice pending = PendingGuestOffice.ForEndRecovery(pState, actor,
                runtimeKingdomId, runtimeOffice);
            Pending.Add(pending.ActorId, pending);
            PendingOrder.Enqueue(pending);
            return true;
        }

        private static void SeedPendingRecovery()
        {
            int seeded = 0;
            foreach (HistoricalSchoolAffiliationSnapshot state in
                     HistoricalAffiliationService.BoundedRecoverySnapshots(
                         MaxPendingGuestRecoveryScan))
            {
                bool registered = state?.LifecycleState ==
                                  HistoricalSchoolLifecycleState.Serving
                    ? RegisterPendingRecovery(state)
                    : RegisterPendingEndRecovery(state);
                if (registered) seeded++;
                if (seeded >= MaxPendingGuestOperations) break;
            }
        }

        private static bool TryProcessPending(PendingGuestOffice pPending)
        {
            if (pPending == null) return true;
            if (pPending.PersistenceCleanFailure) return true;
            if (pPending.EndSuperseded) return true;
            if (pPending.EndExpectedAffiliation != null)
                return TryProcessPendingEnd(pPending);
            Actor actor = FindActor(pPending.ActorId);
            Kingdom host = FindKingdom(pPending.HostKingdomId);
            if (pPending.StartRequest != null &&
                pPending.CommittedStartResult != null &&
                CourtService.IsWesternElective(host))
                return CancelInvalidPendingGuestStart(pPending, actor, host);
            City residence = FindCity(pPending.CityId);
            if (actor?.data == null || host?.data == null || residence?.data == null ||
                residence.kingdom != host) return false;
            if (pPending.StartRequest != null &&
                pPending.CommittedStartResult == null &&
                !CanCommitPendingGuestStart(actor, host, residence,
                    pPending))
                return CancelInvalidPendingGuestStart(pPending, actor, host);

            GuestOfficeStartResult startResult = null;
            GuestOfficeRecoveryResult recoveryResult = null;
            if (pPending.StartRequest != null)
            {
                if (pPending.CommittedStartResult == null)
                {
                    if (pPending.PersistenceQueued) return false;
                    if (!HistoricalSchoolWriteBufferService.TryEnqueue(
                            new GuestStartWriteOperation(pPending))) return false;
                    pPending.PersistenceQueued = true;
                    return false;
                }
                startResult = pPending.CommittedStartResult;
                pPending.PersistenceOutcome = startResult.Persistence.Outcome;
                pPending.RecoveredExisting |= startResult.RecoveredExisting;
                if (!GuestOfficeAdoptionRules.ShouldAdopt(
                        startResult.Persistence.Outcome)) return false;
                pPending.CommittedAffiliation = startResult.Affiliation;
            }
            else
            {
                HistoricalSchoolAffiliationSnapshot current =
                    HistoricalAffiliationService.Get(pPending.ActorId);
                if (current?.LifecycleState != HistoricalSchoolLifecycleState.Serving)
                    return true;
                recoveryResult = GuestOfficePersistence.ReadCommittedTuple(
                    pPending.ActorId, pPending.RecoveryAffiliation);
                if (recoveryResult.Decision == GuestOfficeRecoveryDecision.Retry)
                    return false;
                if (recoveryResult.Decision != GuestOfficeRecoveryDecision.Adopt)
                    return false;
                pPending.PersistenceOutcome = GuestOfficePersistenceOutcome.Committed;
                pPending.RecoveredExisting = true;
                pPending.CommittedAffiliation = recoveryResult.Affiliation;
                pPending.OfficeId = recoveryResult.OfficeId;
                pPending.SchoolId = recoveryResult.SchoolId;
                pPending.IsActing = recoveryResult.IsActing;
            }

            if (!pPending.AffiliationAdopted)
            {
                if (!HistoricalAffiliationService.AdoptCommittedService(
                        pPending.CommittedAffiliation)) return false;
                pPending.AffiliationAdopted = true;
            }

            if (!pPending.CourtApplied)
            {
                try
                {
                    if (actor.city != residence || actor.kingdom != host)
                    {
                        using (FormalAffiliationTransferScope.Open(
                                   actor.data.id, host.id, residence.data.id))
                        {
                            if (actor.kingdom != host) actor.joinKingdom(host);
                            actor.joinCity(residence);
                        }
                    }
                    if (!(actor.city == residence && actor.kingdom == host))
                        return false;
                    bool applied;
                    if (startResult != null)
                    {
                        applied = CourtService.ApplyCommittedOfficerProjection(actor, host,
                            CourtOfficeLayer.Central, pPending.OfficeId, pPending.SchoolId,
                            residence, startResult.Career, pPending.RuntimePrior,
                            pRecordCareerHistory: GuestOfficePendingRules.ShouldRecordHistory(
                                pPending.RecoveredExisting),
                            pActing: pPending.IsActing,
                            pStateProjectionCommitted: true);
                    }
                    else
                    {
                        var recoveredCareer = new OfficialCareerAppointmentResult(
                            OfficialCareerPersistenceOutcome.Committed,
                            OfficialCareerMutation.Refreshed);
                        applied = CourtService.ApplyCommittedOfficerProjection(actor, host,
                            CourtOfficeLayer.Central, recoveryResult.OfficeId,
                            recoveryResult.SchoolId, residence, recoveredCareer,
                            CourtService.CaptureRuntimeOfficerProjection(actor),
                            pRecordCareerHistory: false,
                            pActing: recoveryResult.IsActing);
                    }
                    if (!applied) return false;
                    pPending.CourtApplied = true;
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Committed guest court projection failed for actor " +
                                        pPending.ActorId + ": " + error.Message);
                    return false;
                }
            }

            if (!pPending.StatusApplied)
            {
                try
                {
                    actor.addStatusEffect(HistoricalSchoolContent.GuestStatusId, 120f,
                        pColorEffect: false);
                    pPending.StatusApplied = true;
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Committed guest status projection failed for actor " +
                                        pPending.ActorId + ": " + error.Message);
                    return false;
                }
            }

            if (pPending.StartRequest != null &&
                !OfficialCareerStateService.ExtendTermEndYear(
                    actor, host, pPending.StartRequest.EndYear))
                return false;

            if (GuestOfficePendingRules.ShouldRetain(pPending.PersistenceOutcome,
                    pPending.AffiliationAdopted, pPending.CourtApplied,
                    pPending.StatusApplied)) return false;
            if (pPending.StartRequest != null &&
                GuestOfficePendingRules.ShouldRecordHistory(pPending.RecoveredExisting))
                RecordSupplementalGuestHistory(actor, host, residence, pPending.OfficeId);
            return true;
        }

        private static bool CanCommitPendingGuestStart(Actor pActor,
            Kingdom pHost, City pResidence, PendingGuestOffice pPending)
        {
            return pPending?.StartRequest != null &&
                   !CourtService.IsWesternElective(pHost) &&
                   CourtService.CanAppointGuestOfficer(pActor, pHost,
                       pPending.OfficeId, pResidence, pPending.IsActing);
        }

        private static bool CancelInvalidPendingGuestStart(
            PendingGuestOffice pPending, Actor pActor, Kingdom pHost)
        {
            if (pPending == null) return true;
            if (pPending.PersistenceQueued) return false;
            if (pPending.CommittedStartResult?.Persistence.Outcome !=
                GuestOfficePersistenceOutcome.Committed)
            {
                pPending.PersistenceCleanFailure = true;
                return true;
            }
            if (!CourtService.IsWesternElective(pHost) ||
                !ConvertCommittedStartToEnd(pPending, pActor)) return false;
            return TryProcessPendingEnd(pPending);
        }

        private static bool ConvertCommittedStartToEnd(
            PendingGuestOffice pPending, Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot committed =
                pPending?.CommittedStartResult?.Affiliation;
            if (committed == null) return false;
            return pPending.BeginEndCompensation(committed, pActor,
                "elective_reform", Date.getCurrentYear(),
                LineageService.CurTime());
        }

        private static bool TryProcessPendingEnd(PendingGuestOffice pPending)
        {
            if (pPending?.EndExpectedAffiliation == null) return true;
            if (pPending.RecoverCommittedEnd)
            {
                GuestOfficeEndRecoveryResult recovery =
                    GuestOfficeEndPersistence.ReadCommittedEnd(
                        pPending.EndExpectedAffiliation, pPending.HostKingdomId,
                        CourtOfficeLayer.Central, pPending.OfficeId);
                pPending.PersistenceOutcome = recovery.Persistence.Outcome;
                if (recovery.Persistence.Outcome ==
                    GuestOfficePersistenceOutcome.CleanFailure) return true;
                if (!GuestOfficeEndPendingRules.CanOpenNextTerm(
                        recovery.Persistence.Outcome)) return false;
                pPending.CommittedAffiliation = recovery.Affiliation;
                pPending.HostKingdomId = recovery.HostKingdomId;
                pPending.OfficeId = recovery.OfficeId;
                pPending.EndReason = recovery.EndReason;
            }
            if (!pPending.RecoverCommittedEnd)
            {
                if (pPending.CommittedEndResult == null)
                {
                    if (pPending.PersistenceQueued) return false;
                    if (!HistoricalSchoolWriteBufferService.TryEnqueue(
                            new GuestEndWriteOperation(pPending))) return false;
                    pPending.PersistenceQueued = true;
                    return false;
                }
                GuestOfficeEndResult result = pPending.CommittedEndResult;
                pPending.PersistenceOutcome = result.Persistence.Outcome;
                pPending.RecoveredExisting |= result.RecoveredExisting;
                if (!GuestOfficeEndPendingRules.CanOpenNextTerm(
                        result.Persistence.Outcome)) return false;
                pPending.CommittedAffiliation = result.Affiliation;
            }

            if (!pPending.AffiliationAdopted)
            {
                if (!HistoricalAffiliationService.AdoptCommittedServiceEnd(
                        pPending.CommittedAffiliation)) return false;
                pPending.AffiliationAdopted = true;
            }

            if (!pPending.CourtApplied)
            {
                Actor actor = pPending.EndActor ?? FindActor(pPending.ActorId);
                Kingdom host = FindKingdom(pPending.HostKingdomId);
                if (!CourtService.ApplyCommittedGuestOfficerEnd(actor, host,
                        pPending.HostKingdomId, pPending.OfficeId,
                        pPending.EndReason)) return false;
                pPending.CourtApplied = true;
            }

            if (GuestOfficeEndPendingRules.ShouldRetain(
                    pPending.PersistenceOutcome, pPending.AffiliationAdopted,
                    pPending.CourtApplied)) return false;
            QueueWesternElectiveVacancy(pPending);
            return true;
        }

        private static void QueueWesternElectiveVacancy(PendingGuestOffice pPending)
        {
            Kingdom host = FindKingdom(pPending?.HostKingdomId ?? -1L);
            if (!CourtService.IsWesternElective(host)) return;
            WesternCourtElectionService.EnqueueVacancy(host,
                pPending.OfficeId, pPending.ActorId);
        }

        private static void SchedulePendingRetry(PendingGuestOffice pPending)
        {
            if (pPending == null) return;
            if (pPending.PersistenceQueued)
            {
                pPending.ReadyFrame = int.MaxValue;
                return;
            }
            pPending.Attempts++;
            pPending.ReadyFrame = _pendingFrame +
                GuestOfficePendingRules.RetryDelayFrames(pPending.Attempts,
                    MaxPendingGuestRetryBackoffFrames);
        }

        private static HistoricalSchoolTeachingPersistenceOutcome WriteOutcome(
            GuestOfficePersistenceOutcome pOutcome, bool pRecoveredExisting)
        {
            if (pOutcome == GuestOfficePersistenceOutcome.Committed)
                return pRecoveredExisting
                    ? HistoricalSchoolTeachingPersistenceOutcome.Replayed
                    : HistoricalSchoolTeachingPersistenceOutcome.Committed;
            return pOutcome == GuestOfficePersistenceOutcome.CleanFailure
                ? HistoricalSchoolTeachingPersistenceOutcome.CleanFailure
                : HistoricalSchoolTeachingPersistenceOutcome.Unknown;
        }

        private static HistoricalSchoolTeachingPersistenceOutcome
            GuestEndPreparationOutcome(
                GuestOfficeEndPreparationResult pPreparation)
        {
            if (pPreparation?.Decision ==
                    GuestOfficeEndPreparationDecision.AlreadyEnded ||
                pPreparation?.Decision ==
                    GuestOfficeEndPreparationDecision.Superseded)
                return HistoricalSchoolTeachingPersistenceOutcome.Replayed;
            return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
        }

        private static HistoricalSchoolAffiliationSnapshot CopyAffiliation(
            HistoricalSchoolAffiliationSnapshot pAffiliation)
        {
            if (pAffiliation == null) return null;
            return new HistoricalSchoolAffiliationSnapshot(
                pAffiliation.ActorId, pAffiliation.HomeKingdomId,
                pAffiliation.HomeKingdomName, pAffiliation.HometownCityId,
                pAffiliation.ResidenceCityId,
                pAffiliation.PreviousResidenceCityId,
                pAffiliation.DestinationCityId,
                pAffiliation.ServiceKingdomId,
                pAffiliation.LifecycleState,
                pAffiliation.ServiceStartYear,
                pAffiliation.ServiceEndYear,
                pAffiliation.LastTravelYear,
                pAffiliation.TravelWaitStartYear,
                pAffiliation.VoyageStartYear,
                pAffiliation.VoyageArrivalYear,
                pAffiliation.TransportFailures);
        }

        private sealed class GuestStartWriteOperation : IHistoricalSchoolWriteOperation
        {
            private readonly PendingGuestOffice _pending;
            private GuestOfficeStartResult _result;
            private OfficialCareerAppointmentProjection _stateProjection;

            public GuestStartWriteOperation(PendingGuestOffice pPending)
            {
                _pending = pPending;
            }

            public string OperationKey => _pending?.StartRequest?.OperationKey ?? "";

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                Actor actor = FindActor(_pending.ActorId);
                Kingdom host = FindKingdom(_pending.HostKingdomId);
                City residence = FindCity(_pending.CityId);
                if (!CanCommitPendingGuestStart(actor, host, residence,
                        _pending))
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
                _result = GuestOfficePersistence.StartInTransaction(pDb, pTransaction,
                    _pending.StartRequest);
                if (_result?.Persistence.Outcome ==
                    GuestOfficePersistenceOutcome.Committed)
                {
                    _stateProjection = OfficialCareerStateService.StageAppointment(
                        pDb, pTransaction, actor, host, CourtOfficeLayer.Central,
                        _pending.OfficeId, residence,
                        pActing: _pending.IsActing,
                        pVacancyPromotion: _pending.StartRequest?.CareerAppointment?.
                            VacancyPromotion == true);
                }
                return WriteOutcome(_result?.Persistence.Outcome ??
                    GuestOfficePersistenceOutcome.Unknown,
                    _result?.RecoveredExisting ?? false);
            }

            public void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome)
            {
                _pending.CommittedStartResult = _result;
                OfficialCareerStateService.PublishAppointment(_stateProjection);
                ReleasePendingWrite(_pending);
            }

            public void OnCleanFailure()
            {
                _pending.PersistenceCleanFailure = true;
                ReleasePendingWrite(_pending);
            }
        }

        private sealed class GuestEndWriteOperation : IHistoricalSchoolWriteOperation,
            IHistoricalSchoolAsyncWriteOperation,
            IHistoricalSchoolRetainedCleanFailure
        {
            private readonly PendingGuestOffice _pending;
            private readonly HistoricalSchoolAffiliationSnapshot
                _expectedAffiliation;
            private readonly string _endReason;
            private readonly int _endedYear;
            private readonly double _endedTime;
            private readonly string _officeId;
            private readonly string _schoolId;
            private readonly GuestEndWriteResult _backgroundResult =
                new GuestEndWriteResult();
            private GuestOfficeEndPreparationResult _preparation;
            private GuestOfficeEndRequest _request;
            private GuestOfficeEndResult _result;
            private bool _retainedAfterCleanFailure;

            public GuestEndWriteOperation(PendingGuestOffice pPending)
            {
                _pending = pPending;
                _expectedAffiliation = CopyAffiliation(
                    pPending?.EndExpectedAffiliation);
                _endReason = pPending?.EndReason ?? "";
                _endedYear = pPending?.EndedYear ?? -1;
                _endedTime = pPending?.EndedTime ?? -1d;
                _officeId = pPending?.OfficeId ?? "";
                _schoolId = pPending?.SchoolId ?? "";
            }

            public string OperationKey => "guest-end:v1|actor=" +
                (_expectedAffiliation?.ActorId ?? -1L) + "|host=" +
                (_expectedAffiliation?.ServiceKingdomId ?? -1L) + "|office=" +
                _officeId + "|year=" + _endedYear + "|reason=" + _endReason;

            public bool RetainsPendingAfterCleanFailure =>
                _retainedAfterCleanFailure;

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                // guest-end 实测 12.9~21.4 ms/op,是合批之后学派唯一的大头。
                // 但它牵到的三张表都很小(SchoolAffiliation 990 /
                // CourtOfficer 268 / OfficialCareerState 215),EXPLAIN QUERY
                // PLAN 显示全部走主键或索引,而一个含写入的事务固定开销只有
                // 约 0.83ms —— 也就是说读代码解释不了这个量级。分两段计时,
                // 让下一份日志直接说出是准备阶段还是落库阶段。
                _request = _pending.EndRequest;
                if (_request == null)
                {
                    long preparing = AncientWarfare3.core.performance
                        .AWDiagnosticsGate.Enabled
                        ? Stopwatch.GetTimestamp()
                        : 0L;
                    _preparation = GuestOfficeEndPersistence.
                        PrepareEndAttemptInTransaction(pDb,
                        pTransaction, _expectedAffiliation, _endReason,
                        _endedYear, _endedTime, _officeId, _schoolId);
                    HistoricalSchoolWriteDiagnostics.AccountOperation(
                        "guest-end.prepare", preparing);
                    _request = _preparation.Request;
                }
                if (_request == null)
                    return GuestEndPreparationOutcome(_preparation);
                long ending = AncientWarfare3.core.performance
                    .AWDiagnosticsGate.Enabled
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                _result = GuestOfficeEndPersistence.EndInTransaction(pDb,
                    pTransaction, _request);
                HistoricalSchoolWriteDiagnostics.AccountOperation(
                    "guest-end.commit_stage", ending);
                return WriteOutcome(_result?.Persistence.Outcome ??
                    GuestOfficePersistenceOutcome.Unknown,
                    _result?.RecoveredExisting ?? false);
            }

            public IHistoricalSchoolBackgroundWrite DetachBackgroundWrite()
            {
                return new GuestEndBackgroundWrite(_expectedAffiliation,
                    _endReason, _endedYear, _endedTime, _officeId, _schoolId,
                    _backgroundResult);
            }

            public void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome)
            {
                _preparation = _backgroundResult.Preparation ?? _preparation;
                if (_preparation?.Decision ==
                    GuestOfficeEndPreparationDecision.Superseded)
                {
                    _pending.MarkEndSuperseded();
                    ReleasePendingWrite(_pending);
                    return;
                }
                if (_preparation?.Decision ==
                    GuestOfficeEndPreparationDecision.AlreadyEnded)
                {
                    if (_preparation.CurrentAffiliation == null)
                        _pending.PersistenceCleanFailure = true;
                    else
                        _pending.CommittedEndResult = new GuestOfficeEndResult(
                            GuestOfficePersistenceOutcome.Committed,
                            _preparation.CurrentAffiliation,
                            OfficialCareerPersistence.ResultForClose(null,
                                OfficialCareerPersistenceOutcome.Committed),
                            pRecoveredExisting: true);
                    ReleasePendingWrite(_pending);
                    return;
                }
                _request = _backgroundResult.Request ?? _request;
                _result = _backgroundResult.Result ?? _result;
                _pending.EndRequest = _request;
                if (_request != null)
                {
                    _pending.OfficeId = _request.OfficeId;
                    _pending.SchoolId = _request.SchoolId;
                }
                _pending.CommittedEndResult = _result;
                ReleasePendingWrite(_pending);
            }

            public void OnCleanFailure()
            {
                _preparation = _backgroundResult.Preparation ?? _preparation;
                if (_preparation == null ||
                    GuestOfficeEndRetryRules.RetainPending(
                        _preparation.Decision))
                {
                    _pending.RefreshEndExpectation(
                        _preparation?.CurrentAffiliation);
                    DeferPendingEndWrite(_pending, _preparation);
                    _retainedAfterCleanFailure = true;
                    return;
                }
                _pending.PersistenceCleanFailure = true;
                ReleasePendingWrite(_pending);
            }
        }

        private sealed class GuestEndWriteResult
        {
            public GuestOfficeEndPreparationResult Preparation;
            public GuestOfficeEndRequest Request;
            public GuestOfficeEndResult Result;
        }

        private sealed class GuestEndBackgroundWrite :
            IHistoricalSchoolBackgroundWrite
        {
            private readonly HistoricalSchoolAffiliationSnapshot
                _expectedAffiliation;
            private readonly string _endReason;
            private readonly int _endedYear;
            private readonly double _endedTime;
            private readonly string _officeId;
            private readonly string _schoolId;
            private readonly GuestEndWriteResult _result;

            public GuestEndBackgroundWrite(
                HistoricalSchoolAffiliationSnapshot pExpectedAffiliation,
                string pEndReason, int pEndedYear, double pEndedTime,
                string pOfficeId, string pSchoolId,
                GuestEndWriteResult pResult)
            {
                _expectedAffiliation = pExpectedAffiliation;
                _endReason = pEndReason ?? "";
                _endedYear = pEndedYear;
                _endedTime = pEndedTime;
                _officeId = pOfficeId ?? "";
                _schoolId = pSchoolId ?? "";
                _result = pResult ?? throw new ArgumentNullException(nameof(pResult));
            }

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                GuestOfficeEndPreparationResult preparation =
                    GuestOfficeEndPersistence.PrepareEndAttemptInTransaction(pDb,
                        pTransaction, _expectedAffiliation, _endReason,
                        _endedYear, _endedTime, _officeId, _schoolId);
                _result.Preparation = preparation;
                GuestOfficeEndRequest request = preparation.Request;
                if (request == null)
                    return GuestEndPreparationOutcome(preparation);
                GuestOfficeEndResult result =
                    GuestOfficeEndPersistence.EndInTransaction(pDb, pTransaction,
                        request);
                _result.Request = request;
                _result.Result = result;
                return WriteOutcome(result?.Persistence.Outcome ??
                    GuestOfficePersistenceOutcome.Unknown,
                    result?.RecoveredExisting ?? false);
            }
        }

        private static void ReleasePendingWrite(PendingGuestOffice pPending)
        {
            if (pPending == null) return;
            pPending.PersistenceQueued = false;
            pPending.Attempts = 0;
            pPending.ReadyFrame = 0;
        }

        private static void DeferPendingEndWrite(PendingGuestOffice pPending,
            GuestOfficeEndPreparationResult pPreparation)
        {
            if (pPending == null) return;
            pPending.PersistenceQueued = false;
            pPending.Attempts = Math.Min(30, pPending.Attempts + 1);
            pPending.ReadyFrame = _pendingFrame +
                GuestOfficePendingRules.RetryDelayFrames(pPending.Attempts,
                    MaxPendingGuestRetryBackoffFrames);
            if (!GuestOfficeEndRetryRules.ShouldLogAttempt(pPending.Attempts))
                return;
            ModClass.LogWarning("Guest office end deferred: actor=" +
                                pPending.ActorId + " host=" +
                                pPending.HostKingdomId + " office=" +
                                pPending.OfficeId + " active_careers=" +
                                (pPreparation?.ActiveCentralCareerCount ?? -1) +
                                " reason=" +
                                (pPreparation?.ConflictReason ??
                                 "write_clean_failure"));
        }

        private static void RecordSupplementalGuestHistory(Actor pActor, Kingdom pHost,
            City pResidence, string pOfficeId)
        {
            try
            {
                string name = SafeName(pActor);
                HistoryWriter.RecordPerson(pActor.data.id, pHost, name,
                    "school_guest_service",
                    HistoryText.Actor(pActor, name) +
                    HistoryLocalizationRules.H(
                        "aw_hist_school_served_as") +
                    HistoryText.PlainText(
                        CourtInstitutionService.OfficeName(
                            pHost, pOfficeId)),
                    ChronicleCategory.HONOR);
                HistoryWriter.RecordCity(pResidence, pHost, "school_guest_service",
                    HistoryText.Actor(pActor, name) +
                    HistoryLocalizationRules.H(
                        "aw_hist_school_served_court"));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school guest history failed: " +
                                    error.Message);
            }
        }

        private static List<GuestCandidateProfile> BuildCandidateIndex(Kingdom pHost,
            int pYear)
        {
            var result = new List<GuestCandidateProfile>();
            if (pHost?.data == null || pHost.isRekt()) return result;
            var seenActors = new HashSet<long>();
            bool hasExaminationSystem = CivilServiceQualificationService.
                HasExaminationSystem(pHost);
            int cities = 0;
            foreach (City city in pHost.getCities())
            {
                if (cities++ >= MaxHostCitiesPerYear ||
                    result.Count >= MaxGuestCandidatesPerHost) break;
                if (city?.data == null || city.isRekt() || city.kingdom != pHost) continue;
                if (!hasExaminationSystem)
                {
                    foreach (long actorId in HistoricalSchoolRuntimeIndex.Instance.ResidentIds(city.data.id))
                    {
                        if (result.Count >= MaxGuestCandidatesPerHost) break;
                        TryAddCandidateProfile(result, seenActors, pHost,
                            actorId, pYear, hasExaminationSystem,
                            pHostIssuedQualification: false);
                    }
                    continue;
                }
                foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                {
                    foreach (long actorId in HistoricalSchoolRuntimeIndex.Instance.ResidentTeacherIds(city.data.id, school.Id))
                    {
                        if (result.Count >= MaxGuestCandidatesPerHost) break;
                        TryAddCandidateProfile(result, seenActors, pHost,
                            actorId, pYear, hasExaminationSystem,
                            pHostIssuedQualification: false);
                    }
                    if (result.Count >= MaxGuestCandidatesPerHost) break;
                }
            }
            if (hasExaminationSystem && result.Count < MaxGuestCandidatesPerHost)
                foreach (long actorId in CivilServiceExamCandidateQuery.
                             LoadQualifiedForeignResidentActorIds(pHost,
                                 MaxGuestCandidatesPerHost - result.Count))
                {
                    TryAddCandidateProfile(result, seenActors, pHost,
                        actorId, pYear, hasExaminationSystem,
                        pHostIssuedQualification: true);
                    if (result.Count >= MaxGuestCandidatesPerHost) break;
                }
            return result;
        }

        private static void TryAddCandidateProfile(
            List<GuestCandidateProfile> pResult, HashSet<long> pSeenActors,
            Kingdom pHost, long pActorId, int pYear,
            bool pHasExaminationSystem, bool pHostIssuedQualification)
        {
            if (pResult == null || pSeenActors == null ||
                pResult.Count >= MaxGuestCandidatesPerHost ||
                !pSeenActors.Add(pActorId)) return;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActorId);
            if (state == null || state.ServiceKingdomId >= 0 ||
                (state.LifecycleState != HistoricalSchoolLifecycleState.AtHome &&
                 state.LifecycleState != HistoricalSchoolLifecycleState.Resident))
                return;
            Actor actor = FindActor(pActorId);
            if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                !actor.isAdult() ||
                actor.isKing() || actor.isCityLeader() ||
                GeneralService.IsGeneral(actor) ||
                actor.hasTrait(LineageKeys.TRAIT_SLAVE) ||
                actor.hasTrait("madness") ||
                !actor.isSexMale()) return;
            actor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string currentOffice, "");
            actor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long currentKingdom, -1L);
            if (!string.IsNullOrEmpty(currentOffice) || currentKingdom >= 0)
                return;
            SchoolMembershipRecord membership =
                SchoolMembershipService.GetActive(pActorId);
            if (membership == null) return;
            bool canonicalMaster =
                HistoricalSchoolDescentService.IsCanonicalMaster(actor);
            bool qualifiedTeacher = SchoolGuestOfficeRules.IsQualifiedTeacher(
                canonicalMaster, membership.Standing);
            bool educatedScholar = HistoricalSchoolEducationService.IsEducated(
                actor, pYear);
            if (!CivilServiceExamRules.CanEnterGuestCandidateIndex(
                    actor.isSexMale(), pHasExaminationSystem, educatedScholar,
                    qualifiedTeacher,
                    pHostIssuedQualification)) return;
            HistoricalSchoolMasterDefinition definition = canonicalMaster
                ? HistoricalSchoolDescentService.DefinitionFor(actor)
                : null;
            float reputation = ScholarReputation(membership, definition);
            if (pHasExaminationSystem && !pHostIssuedQualification &&
                reputation < 15f) return;
            pResult.Add(new GuestCandidateProfile(actor, state, membership,
                definition, reputation, pHostIssuedQualification));
        }

        private static GuestCandidate SelectCandidate(Kingdom pHost, string pOffice, int pYear,
            IReadOnlyList<GuestCandidateProfile> pCandidates,
            HashSet<long> pAppointedActors, bool pAllowActing)
        {
            GuestCandidate best = null;
            SchoolGuestOfficeRankCandidate bestRank = default;
            for (int index = 0; index < (pCandidates?.Count ?? 0); index++)
            {
                GuestCandidateProfile profile = pCandidates[index];
                if (profile?.Actor?.data == null ||
                    pAppointedActors.Contains(profile.Actor.data.id) ||
                    !CanInvite(profile, pHost, pOffice, pAllowActing,
                        out GuestCandidate candidate))
                    continue;
                var rank = new SchoolGuestOfficeRankCandidate(candidate.Actor.data.id,
                    candidate.Score, candidate.IsActing);
                if (best != null && !SchoolGuestOfficeRules.IsPreferred(rank, bestRank))
                    continue;
                best = candidate;
                bestRank = rank;
            }
            return best;
        }

        private static bool CanInvite(GuestCandidateProfile pProfile, Kingdom pHost,
            string pOffice, bool pAllowActing, out GuestCandidate pCandidate)
        {
            pCandidate = null;
            Actor actor = pProfile?.Actor;
            if (actor?.data == null || pHost?.data == null || pHost.isRekt() ||
                !actor.isAlive() || actor.isRekt()) return false;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(actor.data.id);
            if (state == null || state.ActorId != pProfile.State.ActorId ||
                state.ServiceKingdomId >= 0 ||
                (state.LifecycleState != HistoricalSchoolLifecycleState.AtHome &&
                 state.LifecycleState != HistoricalSchoolLifecycleState.Resident)) return false;
            City residence = HistoricalAffiliationService.ResidenceCity(actor);
            if (residence?.data == null || residence.isRekt() || residence.kingdom != pHost)
                return false;
            actor.data.get(LineageKeys.COURT_OFFICE_ID, out string currentOffice, "");
            actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long currentKingdom, -1L);
            if (!string.IsNullOrEmpty(currentOffice) || currentKingdom >= 0) return false;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(actor.data.id);
            if (membership == null || membership.MembershipId != pProfile.Membership.MembershipId)
                return false;
            bool canonicalMaster =
                HistoricalSchoolDescentService.IsCanonicalMaster(actor);
            bool qualifiedTeacher = SchoolGuestOfficeRules.IsQualifiedTeacher(
                canonicalMaster, membership.Standing);
            bool hasExaminationSystem = CivilServiceQualificationService.
                HasExaminationSystem(pHost);
            bool educatedScholar = HistoricalSchoolEducationService.IsEducated(
                actor, Date.getCurrentYear());
            bool hostIssuedQualification = pProfile.HostIssuedQualification &&
                CivilServiceExamCandidateQuery.HasHostIssuedQualification(actor,
                    pHost);
            if (!CivilServiceExamRules.CanEnterGuestCandidateIndex(
                    actor.isSexMale(), hasExaminationSystem, educatedScholar,
                    qualifiedTeacher,
                    hostIssuedQualification)) return false;

            bool officeFit = OfficeFit(actor, pOffice, membership.SchoolId,
                pProfile.Definition);
            float ability = OfficeAbility(actor, pOffice, pProfile.Definition);
            bool allowed = SchoolGuestOfficeRules.CanInvite(realScholar: true,
                alive: true, adult: actor.isAdult(),
                residenceInHost: true, available: true, serviceFree: state.ServiceKingdomId < 0,
                forbidden: false,
                centralOfficeMale: actor.isSexMale(),
                reputationFit: !hasExaminationSystem || hostIssuedQualification ||
                    pProfile.Reputation >= 15f,
                officeFit) && ability >= 25f;
            if (!allowed) return false;

            bool formal = CourtService.CanAppointGuestOfficer(actor, pHost,
                pOffice, residence, pActing: false);
            bool acting = !formal && pAllowActing && hostIssuedQualification &&
                          CourtService.CanAppointGuestOfficer(actor, pHost,
                              pOffice, residence, pActing: true);
            if (!formal && !acting) return false;

            float score = ability + pProfile.Reputation * 0.45f +
                          CourtSchoolAssignmentRules.CompatibilityBonus(pOffice,
                              membership.SchoolId) * 2f;
            if (pProfile.Definition != null) score += 8f;
            pCandidate = new GuestCandidate(actor, score, acting);
            return true;
        }

        private static bool OfficeFit(Actor pActor, string pOffice, string pSchool,
            HistoricalSchoolMasterDefinition pDefinition)
        {
            switch (pOffice ?? "")
            {
                case CourtOfficeId.ImperialPhysician:
                    return pSchool == CourtSchoolId.Medical;
                case CourtOfficeId.ImperialAstrologer:
                    return pSchool == CourtSchoolId.YinYang;
                case CourtOfficeId.Marshal:
                case CourtOfficeId.Bingbu:
                    return pSchool == CourtSchoolId.Military;
                default:
                    return CourtSchoolAssignmentRules.CompatibilityBonus(pOffice, pSchool) > 0f ||
                           OfficeAbility(pActor, pOffice, pDefinition) >= 45f;
            }
        }

        private static float OfficeAbility(Actor pActor, string pOffice,
            HistoricalSchoolMasterDefinition pDefinition)
        {
            float stewardship = SafeStat(pActor, "stewardship");
            float diplomacy = SafeStat(pActor, "diplomacy");
            float warfare = SafeStat(pActor, "warfare");
            float intelligence = SafeStat(pActor, "intelligence");
            if (pDefinition != null)
            {
                stewardship = Math.Max(stewardship, pDefinition.Abilities.Stewardship);
                diplomacy = Math.Max(diplomacy, pDefinition.Abilities.Diplomacy);
                warfare = Math.Max(warfare, pDefinition.Abilities.Warfare);
                intelligence = Math.Max(intelligence, pDefinition.Abilities.Intelligence);
            }
            switch (pOffice ?? "")
            {
                case CourtOfficeId.Marshal:
                case CourtOfficeId.Bingbu:
                    return warfare;
                case CourtOfficeId.Chancellor:
                case CourtOfficeId.Zhongshu:
                case CourtOfficeId.Menxia:
                    return (diplomacy + intelligence) * 0.5f;
                case CourtOfficeId.Censor:
                case CourtOfficeId.Justice:
                case CourtOfficeId.Xingbu:
                    return (intelligence + stewardship) * 0.5f;
                case CourtOfficeId.Steward:
                case CourtOfficeId.Hubu:
                case CourtOfficeId.GranaryOfficer:
                    return stewardship;
                case CourtOfficeId.ImperialPhysician:
                    return (intelligence + stewardship) * 0.5f;
                case CourtOfficeId.ImperialAstrologer:
                    return (intelligence + diplomacy) * 0.5f;
                default:
                    return intelligence;
            }
        }

        private static float ScholarReputation(Actor pActor, SchoolMembershipRecord pMembership)
        {
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            return ScholarReputation(pMembership, definition);
        }

        private static float ScholarReputation(SchoolMembershipRecord pMembership,
            HistoricalSchoolMasterDefinition pDefinition)
        {
            float reputation = pMembership?.Reputation ?? 0f;
            if (pDefinition != null)
                reputation = Math.Max(reputation, pDefinition.Abilities.Intelligence * 0.5f);
            return Math.Max(0f, Math.Min(100f, reputation));
        }

        private static bool IsGuestProjectionValid(Actor pActor, Kingdom pHost)
        {
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long kingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            return kingdomId == pHost.id && !string.IsNullOrEmpty(office) &&
                   layer == CourtOfficeLayer.Central &&
                   CourtAffiliationResolver.CanServe(pActor, pHost, layer);
        }

        private static Actor FindActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static float HostReceptiveness(Kingdom pHost)
        {
            try
            {
                float diplomacy = pHost?.king?.stats?["diplomacy"] ?? 50f;
                return Math.Max(0f, Math.Min(1f, diplomacy / 100f));
            }
            catch { return 0.5f; }
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return Math.Max(0f, Math.Min(100f, pActor?.stats?[pKey] ?? 0f)); }
            catch { return 0f; }
        }

        private static string SafeName(Actor pActor)
        {
            try { return pActor?.getName() ?? pActor?.data?.name ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }

        private sealed class PendingGuestOffice
        {
            private PendingGuestOffice()
            {
            }

            public long ActorId { get; private set; }
            public long HostKingdomId { get; set; }
            public long CityId { get; private set; }
            public string OfficeId { get; set; } = "";
            public string SchoolId { get; set; } = "";
            public bool IsActing { get; set; }
            public GuestOfficeStartRequest StartRequest { get; private set; }
            public GuestOfficeEndRequest EndRequest { get; set; }
            public GuestOfficeStartResult CommittedStartResult { get; set; }
            public GuestOfficeEndResult CommittedEndResult { get; set; }
            public Actor EndActor { get; private set; }
            public HistoricalSchoolAffiliationSnapshot EndExpectedAffiliation {
                get;
                private set;
            }
            public string EndReason { get; set; } = "";
            public int EndedYear { get; private set; } = -1;
            public double EndedTime { get; private set; } = -1d;
            public HistoricalSchoolAffiliationSnapshot RecoveryAffiliation {
                get;
                private set;
            }
            public HistoricalSchoolAffiliationSnapshot CommittedAffiliation { get; set; }
            public OfficialCareerPrior RuntimePrior { get; private set; }
            public GuestOfficePersistenceOutcome PersistenceOutcome { get; set; } =
                GuestOfficePersistenceOutcome.Unknown;
            public bool RecoveredExisting { get; set; }
            public bool RecoverCommittedEnd { get; private set; }
            public bool AffiliationAdopted { get; set; }
            public bool CourtApplied { get; set; }
            public bool StatusApplied { get; set; }
            public bool PersistenceQueued { get; set; }
            public bool PersistenceCleanFailure { get; set; }
            public bool EndSuperseded { get; private set; }
            public int Attempts { get; set; }
            public int ReadyFrame { get; set; }

            public bool BeginEndCompensation(
                HistoricalSchoolAffiliationSnapshot pCommitted, Actor pActor,
                string pReason, int pEndedYear, double pEndedTime)
            {
                if (pCommitted == null || pCommitted.ActorId != ActorId ||
                    pCommitted.ServiceKingdomId != HostKingdomId ||
                    pCommitted.LifecycleState !=
                    HistoricalSchoolLifecycleState.Serving) return false;
                EndActor = pActor;
                EndExpectedAffiliation = pCommitted;
                EndReason = pReason ?? "guest_start_cancelled";
                EndedYear = Math.Max(pCommitted.ServiceStartYear, pEndedYear);
                EndedTime = pEndedTime;
                EndRequest = null;
                CommittedEndResult = null;
                CommittedAffiliation = null;
                PersistenceOutcome = GuestOfficePersistenceOutcome.Unknown;
                RecoverCommittedEnd = false;
                AffiliationAdopted = false;
                CourtApplied = false;
                StatusApplied = false;
                PersistenceQueued = false;
                PersistenceCleanFailure = false;
                Attempts = 0;
                ReadyFrame = 0;
                return true;
            }

            public void RefreshEndExpectation(
                HistoricalSchoolAffiliationSnapshot pCurrent)
            {
                if (pCurrent == null || pCurrent.ActorId != ActorId ||
                    pCurrent.LifecycleState !=
                    HistoricalSchoolLifecycleState.Serving)
                    return;
                EndExpectedAffiliation = pCurrent;
                HostKingdomId = pCurrent.ServiceKingdomId;
                CityId = pCurrent.ResidenceCityId;
                EndRequest = null;
                CommittedEndResult = null;
            }

            public void MarkEndSuperseded()
            {
                EndSuperseded = true;
            }

            public static PendingGuestOffice ForStart(GuestOfficeStartRequest pRequest,
                OfficialCareerPrior pRuntimePrior)
            {
                if (pRequest == null) return null;
                return new PendingGuestOffice
                {
                    ActorId = pRequest.CareerAppointment?.ActorId ?? -1L,
                    HostKingdomId = pRequest.HostKingdomId,
                    CityId = pRequest.CityId,
                    OfficeId = pRequest.OfficeId,
                    SchoolId = pRequest.SchoolId,
                    IsActing = pRequest.CareerAppointment?.IsActing ?? false,
                    StartRequest = pRequest,
                    RuntimePrior = pRuntimePrior
                };
            }

            public static PendingGuestOffice ForRecovery(
                HistoricalSchoolAffiliationSnapshot pState)
            {
                if (pState == null) return null;
                return new PendingGuestOffice
                {
                    ActorId = pState.ActorId,
                    HostKingdomId = pState.ServiceKingdomId,
                    CityId = pState.ResidenceCityId,
                    RecoveryAffiliation = pState,
                    RecoveredExisting = true
                };
            }

            public static PendingGuestOffice ForEnd(
                HistoricalSchoolAffiliationSnapshot pState, Actor pActor, string pReason,
                int pEndedYear, double pEndedTime)
            {
                if (pState == null || pState.ActorId < 0 ||
                    pState.ServiceKingdomId < 0 ||
                    pState.LifecycleState != HistoricalSchoolLifecycleState.Serving)
                    return null;
                string runtimeOffice = "";
                string runtimeSchool = "";
                try
                {
                    pActor?.data?.get(LineageKeys.COURT_OFFICE_ID,
                        out runtimeOffice, "");
                    pActor?.data?.get(LineageKeys.COURT_SCHOOL,
                        out runtimeSchool, "");
                }
                catch { }
                return new PendingGuestOffice
                {
                    ActorId = pState.ActorId,
                    HostKingdomId = pState.ServiceKingdomId,
                    CityId = pState.ResidenceCityId,
                    EndActor = pActor,
                    EndExpectedAffiliation = pState,
                    OfficeId = runtimeOffice,
                    SchoolId = runtimeSchool,
                    EndReason = pReason ?? "guest_term",
                    EndedYear = pEndedYear,
                    EndedTime = pEndedTime
                };
            }

            public static PendingGuestOffice ForEndRecovery(
                HistoricalSchoolAffiliationSnapshot pState, Actor pActor,
                long pHostKingdomId, string pOfficeId)
            {
                if (pState == null || pState.ActorId < 0 || pHostKingdomId < -1L)
                    return null;
                return new PendingGuestOffice
                {
                    ActorId = pState.ActorId,
                    HostKingdomId = pHostKingdomId,
                    CityId = pState.ResidenceCityId,
                    OfficeId = pOfficeId,
                    EndActor = pActor,
                    EndExpectedAffiliation = pState,
                    RecoverCommittedEnd = true,
                    RecoveredExisting = true
                };
            }
        }

        private sealed class GuestCandidate
        {
            public GuestCandidate(Actor pActor, float pScore, bool pActing)
            {
                Actor = pActor;
                Score = pScore;
                IsActing = pActing;
            }

            public Actor Actor { get; }
            public float Score { get; }
            public bool IsActing { get; }
        }

        internal sealed class GuestCandidateProfile
        {
            public GuestCandidateProfile(Actor pActor,
                HistoricalSchoolAffiliationSnapshot pState,
                SchoolMembershipRecord pMembership,
                HistoricalSchoolMasterDefinition pDefinition,
                float pReputation, bool pHostIssuedQualification)
            {
                Actor = pActor;
                State = pState;
                Membership = pMembership;
                Definition = pDefinition;
                Reputation = pReputation;
                HostIssuedQualification = pHostIssuedQualification;
            }

            public Actor Actor { get; }
            public HistoricalSchoolAffiliationSnapshot State { get; }
            public SchoolMembershipRecord Membership { get; }
            public HistoricalSchoolMasterDefinition Definition { get; }
            public float Reputation { get; }
            public bool HostIssuedQualification { get; }
        }
    }
}
