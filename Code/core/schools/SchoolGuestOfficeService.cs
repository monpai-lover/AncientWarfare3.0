using System;
using System.Collections.Generic;
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
        private const int MaxAppointmentsPerYear = 16;
        private const int MaxHostKingdomsPerYear = 96;
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

        public static void LoadState()
        {
            _lastProcessYear = -1;
            _serviceSweepOffset = 0;
            _pendingFrame = 0;
            Pending.Clear();
            PendingOrder.Clear();
            SeedPendingRecovery();
        }

        public static void ClearRuntime()
        {
            _lastProcessYear = -1;
            _serviceSweepOffset = 0;
            _pendingFrame = 0;
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

        public static void ProcessYear(int pYear)
        {
            if (_lastProcessYear == pYear) return;
            _lastProcessYear = pYear;

            try
            {
                HistoricalSchoolAffiliationSnapshot[] annualStates =
                    HistoricalAffiliationService.ActiveSnapshots();
                CloseExpiredOrInvalidServices(pYear, annualStates);
                int budget = MaxAppointmentsPerYear;
                if (budget <= 0) return;
                Dictionary<long, List<GuestCandidateProfile>> candidateIndex =
                    BuildCandidateIndex(annualStates);

                foreach (Kingdom host in HostKingdoms())
                {
                    if (budget <= 0) break;
                    string[] offices = CourtTierRules.CentralOfficesForTier(
                        CourtService.ResolveTier(host));
                    if (offices.Length == 0) continue;
                    HashSet<string> occupied = new HashSet<string>(
                        CourtService.GetActiveOfficers(host, 96)
                            .Where(p => !string.IsNullOrEmpty(p.office_id))
                            .Select(p => p.office_id), StringComparer.Ordinal);
                    if (!candidateIndex.TryGetValue(host.id,
                            out List<GuestCandidateProfile> hostCandidates)) continue;
                    var appointedActors = new HashSet<long>();

                    foreach (string office in offices)
                    {
                        if (budget <= 0) break;
                        if (occupied.Contains(office)) continue;
                        GuestCandidate candidate = SelectCandidate(host, office, pYear,
                            hostCandidates, appointedActors);
                        if (candidate == null) continue;
                        City residence = HistoricalAffiliationService.ResidenceCity(
                            candidate.Actor);
                        int term = SchoolGuestOfficeRules.TermYears(candidate.Actor.data.id,
                            host.id, pYear);
                        if (!TryAppointAndRecord(candidate.Actor, host, office, residence,
                                pYear, term, "guest_service_started")) continue;
                        occupied.Add(office);
                        appointedActors.Add(candidate.Actor.data.id);
                        budget--;
                    }
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school guest office tick failed: " +
                                    error.Message);
            }
        }

        private static void CloseExpiredOrInvalidServices(int pYear,
            IReadOnlyList<HistoricalSchoolAffiliationSnapshot> pAnnualStates)
        {
            HistoricalSchoolAffiliationSnapshot[] states =
                (pAnnualStates ?? Array.Empty<HistoricalSchoolAffiliationSnapshot>())
                    .Where(p => p != null && p.LifecycleState ==
                                HistoricalSchoolLifecycleState.Serving)
                    .OrderBy(p => p.ActorId)
                    .ToArray();
            if (states.Length == 0) return;
            int start = _serviceSweepOffset % states.Length;
            int count = Math.Min(MaxServiceSweepPerYear, states.Length);
            for (int index = 0; index < count; index++)
            {
                HistoricalSchoolAffiliationSnapshot state = states[(start + index) % states.Length];
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
                    continue;
                if (projectionValid && remaining > 0) continue;

                if (!projectionValid && durableEnvironmentValid && remaining > 0)
                {
                    RegisterPendingRecovery(state);
                    if (Pending.TryGetValue(state.ActorId,
                            out PendingGuestOffice pending))
                    {
                        if (TryProcessPending(pending)) Pending.Remove(state.ActorId);
                        else SchedulePendingRetry(pending);
                    }
                    continue;
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
            _serviceSweepOffset = (start + count) % states.Length;
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

            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            City residence = HistoricalAffiliationService.ResidenceCity(pActor);
            if (string.IsNullOrEmpty(office) || residence?.data == null) return false;
            if (!CourtService.EndGuestOfficer(pActor, pHost, "guest_term_renewal", pYear))
                return false;

            int term = SchoolGuestOfficeRules.TermYears(pActor.data.id, pHost.id, pYear);
            return TryAppointAndRecord(pActor, pHost, office, residence, pYear, term,
                "guest_service_renewed");
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

        private static bool TryAppointAndRecord(Actor pActor, Kingdom pHost, string pOffice,
            City pResidence, int pStartYear, int pTermYears, string pEventType)
        {
            if (pActor?.data == null || pHost?.data == null || pResidence?.data == null ||
                pTermYears < SchoolGuestOfficeRules.MinTermYears ||
                pTermYears > SchoolGuestOfficeRules.MaxTermYears) return false;
            int endYear = pStartYear + pTermYears;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            if (!CourtService.CanAppointGuestOfficer(pActor, pHost, pOffice, pResidence) ||
                string.IsNullOrEmpty(school)) return false;
            HistoricalSchoolAffiliationSnapshot expected =
                HistoricalAffiliationService.Get(pActor.data.id);
            double worldTime = LineageService.CurTime();
            OfficialCareerAppointment appointment = OfficialCareerService.PrepareAppointment(
                pActor, pHost, CourtOfficeLayer.Central, pOffice, school, pResidence,
                pStartYear, worldTime);
            OfficialCareerPrior runtimePrior =
                CourtService.CaptureRuntimeOfficerProjection(pActor);
            GuestOfficeStartRequest request = GuestOfficePersistence.PrepareStart(expected,
                appointment, pEventType,
                school, pOffice, pHost.id, pResidence.data.id, pStartYear, endYear,
                worldTime);
            if (request == null) return false;
            var pending = PendingGuestOffice.ForStart(request, runtimePrior);
            if (!RegisterPendingStart(pending)) return false;
            bool completed = TryProcessPending(pending);
            if (completed) Pending.Remove(pending.ActorId);
            else SchedulePendingRetry(pending);
            return completed;
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
            if (pPending.EndExpectedAffiliation != null)
                return TryProcessPendingEnd(pPending);
            Actor actor = FindActor(pPending.ActorId);
            Kingdom host = FindKingdom(pPending.HostKingdomId);
            City residence = FindCity(pPending.CityId);
            if (actor?.data == null || host?.data == null || residence?.data == null ||
                residence.kingdom != host) return false;

            GuestOfficeStartResult startResult = null;
            GuestOfficeRecoveryResult recoveryResult = null;
            if (pPending.StartRequest != null)
            {
                startResult = GuestOfficePersistence.Start(pPending.StartRequest);
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
                    bool applied;
                    if (startResult != null)
                    {
                        applied = CourtService.ApplyCommittedOfficerProjection(actor, host,
                            CourtOfficeLayer.Central, pPending.OfficeId, pPending.SchoolId,
                            residence, startResult.Career, pPending.RuntimePrior,
                            pRecordCareerHistory: GuestOfficePendingRules.ShouldRecordHistory(
                                pPending.RecoveredExisting));
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
                            pRecordCareerHistory: false);
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

            if (GuestOfficePendingRules.ShouldRetain(pPending.PersistenceOutcome,
                    pPending.AffiliationAdopted, pPending.CourtApplied,
                    pPending.StatusApplied)) return false;
            if (pPending.StartRequest != null &&
                GuestOfficePendingRules.ShouldRecordHistory(pPending.RecoveredExisting))
                RecordSupplementalGuestHistory(actor, host, residence, pPending.OfficeId);
            return true;
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
            else if (pPending.EndRequest == null)
            {
                pPending.EndRequest = GuestOfficeEndPersistence.PrepareEnd(
                    pPending.EndExpectedAffiliation, pPending.EndReason,
                    pPending.EndedYear, pPending.EndedTime);
                if (pPending.EndRequest == null) return false;
                pPending.OfficeId = pPending.EndRequest.OfficeId;
                pPending.SchoolId = pPending.EndRequest.SchoolId;
            }
            if (!pPending.RecoverCommittedEnd)
            {
                GuestOfficeEndResult result = GuestOfficeEndPersistence.End(
                    pPending.EndRequest);
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

            return !GuestOfficeEndPendingRules.ShouldRetain(
                pPending.PersistenceOutcome, pPending.AffiliationAdopted,
                pPending.CourtApplied);
        }

        private static void SchedulePendingRetry(PendingGuestOffice pPending)
        {
            if (pPending == null) return;
            pPending.Attempts++;
            pPending.ReadyFrame = _pendingFrame +
                GuestOfficePendingRules.RetryDelayFrames(pPending.Attempts,
                    MaxPendingGuestRetryBackoffFrames);
        }

        private static void RecordSupplementalGuestHistory(Actor pActor, Kingdom pHost,
            City pResidence, string pOfficeId)
        {
            try
            {
                string name = SafeName(pActor);
                HistoryWriter.RecordPerson(pActor.data.id, pHost, name,
                    "school_guest_service", name + " served as " + (pOfficeId ?? ""),
                    ChronicleCategory.HONOR);
                HistoryWriter.RecordCity(pResidence, pHost, "school_guest_service",
                    name + " served the court");
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school guest history failed: " +
                                    error.Message);
            }
        }

        private static Dictionary<long, List<GuestCandidateProfile>> BuildCandidateIndex(
            IEnumerable<HistoricalSchoolAffiliationSnapshot> pStates)
        {
            var result = new Dictionary<long, List<GuestCandidateProfile>>();
            if (pStates == null) return result;
            foreach (HistoricalSchoolAffiliationSnapshot annualState in pStates)
            {
                if (annualState == null) continue;
                HistoricalSchoolAffiliationSnapshot state =
                    HistoricalAffiliationService.Get(annualState.ActorId);
                if (state == null || state.LifecycleState == HistoricalSchoolLifecycleState.Serving ||
                    state.ServiceKingdomId >= 0 ||
                    (state.LifecycleState != HistoricalSchoolLifecycleState.AtHome &&
                     state.LifecycleState != HistoricalSchoolLifecycleState.Resident)) continue;
                Actor actor = FindActor(state.ActorId);
                if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                    actor.isKing() || actor.isCityLeader() || GeneralService.IsGeneral(actor) ||
                    actor.hasTrait(LineageKeys.TRAIT_SLAVE) || actor.hasTrait("madness") ||
                    !actor.isSexMale()) continue;
                City residence = HistoricalAffiliationService.ResidenceCity(actor);
                Kingdom host = residence?.kingdom;
                if (residence?.data == null || residence.isRekt() || host?.data == null ||
                    host.isRekt() || state.HomeKingdomId == host.id) continue;
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string currentOffice, "");
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long currentKingdom, -1L);
                if (!string.IsNullOrEmpty(currentOffice) || currentKingdom >= 0) continue;

                SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                    actor.data.id);
                if (membership == null) continue;
                bool canonicalMaster =
                    HistoricalSchoolDescentService.IsCanonicalMaster(actor);
                if (!SchoolGuestOfficeRules.IsQualifiedTeacher(canonicalMaster,
                        membership.Source, membership.Reputation)) continue;
                HistoricalSchoolMasterDefinition definition = canonicalMaster
                    ? HistoricalSchoolDescentService.DefinitionFor(actor)
                    : null;
                float reputation = ScholarReputation(membership, definition);
                if (reputation < 15f) continue;

                if (!result.TryGetValue(host.id, out List<GuestCandidateProfile> bucket))
                {
                    bucket = new List<GuestCandidateProfile>();
                    result.Add(host.id, bucket);
                }
                bucket.Add(new GuestCandidateProfile(actor, state, membership, definition,
                    reputation));
            }
            return result;
        }

        private static GuestCandidate SelectCandidate(Kingdom pHost, string pOffice, int pYear,
            IReadOnlyList<GuestCandidateProfile> pCandidates, HashSet<long> pAppointedActors)
        {
            GuestCandidate best = null;
            SchoolGuestOfficeRankCandidate bestRank = default;
            for (int index = 0; index < (pCandidates?.Count ?? 0); index++)
            {
                GuestCandidateProfile profile = pCandidates[index];
                if (profile?.Actor?.data == null ||
                    pAppointedActors.Contains(profile.Actor.data.id) ||
                    !CanInvite(profile, pHost, pOffice, out GuestCandidate candidate))
                    continue;
                var rank = new SchoolGuestOfficeRankCandidate(candidate.Actor.data.id,
                    candidate.Score);
                if (best != null && !SchoolGuestOfficeRules.IsPreferred(rank, bestRank))
                    continue;
                best = candidate;
                bestRank = rank;
            }
            return best;
        }

        private static bool CanInvite(GuestCandidateProfile pProfile, Kingdom pHost,
            string pOffice, out GuestCandidate pCandidate)
        {
            pCandidate = null;
            Actor actor = pProfile?.Actor;
            if (actor?.data == null || pHost?.data == null || pHost.isRekt() ||
                !actor.isAlive() || actor.isRekt()) return false;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(actor.data.id);
            if (state == null || state.ActorId != pProfile.State.ActorId ||
                state.HomeKingdomId == pHost.id || state.ServiceKingdomId >= 0 ||
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

            bool officeFit = OfficeFit(actor, pOffice, membership.SchoolId,
                pProfile.Definition);
            float ability = OfficeAbility(actor, pOffice, pProfile.Definition);
            bool allowed = SchoolGuestOfficeRules.CanInvite(realScholar: true,
                alive: true, foreignHome: state.HomeKingdomId != pHost.id,
                residenceInHost: true, available: true, serviceFree: state.ServiceKingdomId < 0,
                forbidden: false, centralOfficeMale: true, reputationFit: true,
                officeFit) && ability >= 25f;
            if (!allowed) return false;

            float score = ability + pProfile.Reputation * 0.45f +
                          CourtSchoolAssignmentRules.CompatibilityBonus(pOffice,
                              membership.SchoolId) * 2f;
            if (pProfile.Definition != null) score += 8f;
            pCandidate = new GuestCandidate(actor, score);
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

        private static IEnumerable<Kingdom> HostKingdoms()
        {
            var result = new List<Kingdom>();
            try
            {
                if (World.world?.kingdoms == null) return result;
                foreach (Kingdom kingdom in World.world.kingdoms)
                    if (kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral() &&
                        CourtService.HasOfficialCourt(kingdom)) result.Add(kingdom);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school host scan failed: " + error.Message);
            }
            return result.OrderBy(p => p.id).Take(MaxHostKingdomsPerYear);
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
            public GuestOfficeStartRequest StartRequest { get; private set; }
            public GuestOfficeEndRequest EndRequest { get; set; }
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
            public int Attempts { get; set; }
            public int ReadyFrame { get; set; }

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
                return new PendingGuestOffice
                {
                    ActorId = pState.ActorId,
                    HostKingdomId = pState.ServiceKingdomId,
                    CityId = pState.ResidenceCityId,
                    EndActor = pActor,
                    EndExpectedAffiliation = pState,
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
            public GuestCandidate(Actor pActor, float pScore)
            {
                Actor = pActor;
                Score = pScore;
            }

            public Actor Actor { get; }
            public float Score { get; }
        }

        private sealed class GuestCandidateProfile
        {
            public GuestCandidateProfile(Actor pActor,
                HistoricalSchoolAffiliationSnapshot pState,
                SchoolMembershipRecord pMembership,
                HistoricalSchoolMasterDefinition pDefinition, float pReputation)
            {
                Actor = pActor;
                State = pState;
                Membership = pMembership;
                Definition = pDefinition;
                Reputation = pReputation;
            }

            public Actor Actor { get; }
            public HistoricalSchoolAffiliationSnapshot State { get; }
            public SchoolMembershipRecord Membership { get; }
            public HistoricalSchoolMasterDefinition Definition { get; }
            public float Reputation { get; }
        }
    }
}
