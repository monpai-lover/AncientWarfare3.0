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
        private const int MaxCandidatesPerOffice = 32;
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
                CloseExpiredOrInvalidServices(pYear);
                int budget = MaxAppointmentsPerYear;
                if (budget <= 0) return;
                HistoricalSchoolAffiliationSnapshot[] candidateStates =
                    HistoricalAffiliationService.ActiveSnapshots(pTravelEligibleOnly: true);

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

                    foreach (string office in offices)
                    {
                        if (budget <= 0) break;
                        if (occupied.Contains(office)) continue;
                        GuestCandidate candidate = SelectCandidate(host, office, pYear,
                            candidateStates);
                        if (candidate == null) continue;
                        City residence = HistoricalAffiliationService.ResidenceCity(
                            candidate.Actor);
                        int term = SchoolGuestOfficeRules.TermYears(candidate.Actor.data.id,
                            host.id, pYear);
                        if (!TryAppointAndRecord(candidate.Actor, host, office, residence,
                                pYear, term, "guest_service_started")) continue;
                        occupied.Add(office);
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

        private static void CloseExpiredOrInvalidServices(int pYear)
        {
            HistoricalSchoolAffiliationSnapshot[] states =
                HistoricalAffiliationService.ActiveSnapshots()
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

        private static GuestCandidate SelectCandidate(Kingdom pHost, string pOffice, int pYear,
            IEnumerable<HistoricalSchoolAffiliationSnapshot> pStates)
        {
            var candidates = new List<GuestCandidate>();
            foreach (HistoricalSchoolAffiliationSnapshot state in
                     pStates ?? Array.Empty<HistoricalSchoolAffiliationSnapshot>())
            {
                if (state == null || state.LifecycleState == HistoricalSchoolLifecycleState.Serving ||
                    state.ServiceKingdomId >= 0 || state.HomeKingdomId == pHost.id) continue;
                Actor actor = FindActor(state.ActorId);
                if (!CanInvite(actor, pHost, pOffice, state, out GuestCandidate candidate))
                    continue;
                candidates.Add(candidate);
            }

            return candidates.OrderByDescending(p => p.Score)
                .ThenBy(p => p.Actor.data.id)
                .Take(MaxCandidatesPerOffice)
                .FirstOrDefault();
        }

        private static bool CanInvite(Actor pActor, Kingdom pHost, string pOffice,
            HistoricalSchoolAffiliationSnapshot pState, out GuestCandidate pCandidate)
        {
            pCandidate = null;
            if (pActor?.data == null || pHost?.data == null || pState == null ||
                !pActor.isAlive() || pActor.isRekt() || pActor.isKing() ||
                pActor.isCityLeader() || GeneralService.IsGeneral(pActor) ||
                pActor.hasTrait(LineageKeys.TRAIT_SLAVE) || pActor.hasTrait("madness") ||
                pState.HomeKingdomId == pHost.id || pState.ServiceKingdomId >= 0 ||
                !HistoricalAffiliationService.IsAvailableForOffice(pActor) ||
                !HistoricalAffiliationService.IsPresentForInfluence(pActor)) return false;

            City residence = HistoricalAffiliationService.ResidenceCity(pActor);
            if (residence?.data == null || residence.isRekt() || residence.kingdom != pHost)
                return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string currentOffice, "");
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long currentKingdom, -1L);
            if (!string.IsNullOrEmpty(currentOffice) || currentKingdom >= 0) return false;
            if (!pActor.isSexMale()) return false; // all guest offices are central offices

            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                pActor.data.id);
            bool realScholar = HistoricalSchoolDescentService.IsCanonicalMaster(pActor) ||
                               SchoolLineageService.IsQualifiedTeacher(pActor);
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            float reputation = ScholarReputation(pActor, membership);
            bool officeFit = OfficeFit(pActor, pOffice, membership?.SchoolId ?? "", definition);
            bool reputationFit = reputation >= 15f;
            float ability = OfficeAbility(pActor, pOffice, definition);
            bool allowed = SchoolGuestOfficeRules.CanInvite(realScholar,
                alive: true, foreignHome: pState.HomeKingdomId != pHost.id,
                residenceInHost: true, available: true, serviceFree: pState.ServiceKingdomId < 0,
                forbidden: false, centralOfficeMale: pActor.isSexMale(), reputationFit,
                officeFit) && ability >= 25f;
            if (!allowed || membership == null) return false;

            float score = ability + reputation * 0.45f +
                          CourtSchoolAssignmentRules.CompatibilityBonus(pOffice,
                              membership.SchoolId) * 2f;
            if (definition != null) score += 8f;
            pCandidate = new GuestCandidate(pActor, score);
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
            float reputation = pMembership?.Reputation ?? 0f;
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            if (definition != null)
                reputation = Math.Max(reputation, definition.Abilities.Intelligence * 0.5f);
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
    }
}
