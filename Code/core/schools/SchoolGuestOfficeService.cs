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
    /// in a host city.  Nationality remains the affiliation snapshot's home kingdom;
    /// only the temporary service kingdom and court projection change.  The guarded
    /// CourtService path performs TryBeginService before TryAppointGuestOfficer writes
    /// the court row, keeping the two projections from drifting apart.
    /// </summary>
    internal static class SchoolGuestOfficeService
    {
        private const int MaxAppointmentsPerYear = 16;
        private const int MaxHostKingdomsPerYear = 96;
        private const int MaxServiceSweepPerYear = 512;
        private static int _lastProcessYear = -1;
        private static int _serviceSweepOffset;

        public static void LoadState()
        {
            _lastProcessYear = -1;
            _serviceSweepOffset = 0;
        }

        public static void ClearRuntime()
        {
            _lastProcessYear = -1;
            _serviceSweepOffset = 0;
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
                bool projectionValid = alive && hostAlive && residenceValid &&
                                       CourtService.HasOfficialCourt(host) &&
                                       IsGuestProjectionValid(actor, host);
                int remaining = state.ServiceEndYear < 0
                    ? 0
                    : state.ServiceEndYear - pYear;

                if (projectionValid && remaining <= 0 && TryRenew(actor, host, state, pYear))
                    continue;
                if (projectionValid && remaining > 0) continue;

                if (actor?.data != null)
                {
                    CourtService.EndGuestOfficer(actor, host,
                        hostAlive ? "guest_term_expired" : "guest_host_lost", pYear);
                }
                else
                {
                    // The actor may already have been removed from the live unit index.
                    // Close both durable projections by id so a missing unit cannot leave
                    // an immortal service row or occupied office.
                    OfficialCareerService.EndForKingdom(state.ActorId,
                        state.ServiceKingdomId, "guest_actor_missing");
                    HistoricalAffiliationService.EndService(state.ActorId, pYear);
                }
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

        private static bool TryAppointAndRecord(Actor pActor, Kingdom pHost, string pOffice,
            City pResidence, int pStartYear, int pTermYears, string pEventType)
        {
            if (pActor?.data == null || pHost?.data == null || pResidence?.data == null ||
                pTermYears < SchoolGuestOfficeRules.MinTermYears ||
                pTermYears > SchoolGuestOfficeRules.MaxTermYears) return false;
            int endYear = pStartYear + pTermYears;
            if (!CourtService.TryAppointGuestOfficer(pActor, pHost, pOffice, pResidence,
                    pStartYear, endYear)) return false;

            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            string payload = (pOffice ?? "") + "|" + pStartYear + "|" + endYear;
            if (!HistoricalSchoolStore.RecordSchoolEvent(pEventType, pActor.data.id, -1,
                    school, pResidence.data.id, pHost.id, pStartYear, payload, 3,
                    World.world?.getCurWorldTime() ?? 0d))
            {
                CourtService.EndGuestOfficer(pActor, pHost, "guest_event_failed", pStartYear);
                return false;
            }

            try
            {
                pActor.addStatusEffect(HistoricalSchoolContent.GuestStatusId, 120f,
                    pColorEffect: false);
                string name = SafeName(pActor);
                HistoryWriter.RecordPerson(pActor.data.id, pHost, name,
                    "school_guest_service", name + " served as " + (pOffice ?? ""),
                    ChronicleCategory.HONOR);
                HistoryWriter.RecordCity(pResidence, pHost, "school_guest_service",
                    name + " served the court");
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school guest history failed: " +
                                    error.Message);
            }
            return true;
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
