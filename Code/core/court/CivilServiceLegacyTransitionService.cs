using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceLegacyTransitionService
    {
        private static bool _backfillComplete;
        private static readonly List<long> _backfillKingdomIds =
            new List<long>();
        private static object _backfillWorld;
        private static int _backfillCursor;

        internal static void ClearRuntime()
        {
            _backfillComplete = false;
            _backfillKingdomIds.Clear();
            _backfillWorld = null;
            _backfillCursor = 0;
        }

        internal static void OnTechnologyCompleted(Kingdom pKingdom,
            string pTechnologyId)
        {
            if (!string.Equals(pTechnologyId,
                    CivilServiceQualificationService.TechnologyId,
                    StringComparison.Ordinal)) return;
            ApplyTransition(pKingdom);
        }

        internal static void ProcessVersionedBackfill()
        {
            MapBox world = World.world;
            if (_backfillComplete || world?.kingdoms == null) return;
            if (!ReferenceEquals(_backfillWorld, world))
                InitializeBackfill(world);

            while (_backfillCursor < _backfillKingdomIds.Count)
            {
                long kingdomId = _backfillKingdomIds[_backfillCursor];
                Kingdom pending = world.kingdoms.get(kingdomId);
                if (!NeedsTransition(pending))
                {
                    _backfillCursor++;
                    continue;
                }

                if (ApplyTransition(pending)) _backfillCursor++;
                return;
            }
            _backfillComplete = true;
        }

        private static void InitializeBackfill(MapBox pWorld)
        {
            _backfillKingdomIds.Clear();
            _backfillWorld = pWorld;
            _backfillCursor = 0;
            _backfillComplete = false;
            foreach (Kingdom kingdom in pWorld.kingdoms)
            {
                if (kingdom?.data != null)
                    _backfillKingdomIds.Add(kingdom.id);
            }
        }

        internal static bool HasUsableCredential(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !IsFormalCivilAppointment(pLayer, pOfficeId)) return false;
            pActor.data.get(
                LineageKeys.CIVIL_SERVICE_LEGACY_CREDENTIAL_KINGDOM_ID,
                out long issuerKingdomId, -1L);
            pActor.data.get(
                LineageKeys.CIVIL_SERVICE_LEGACY_CREDENTIAL_REMAINING,
                out bool credentialRemaining, false);
            return CivilServiceLegacyTransitionRules.CanUseCredential(
                issuerKingdomId, pKingdom.id, credentialRemaining,
                isFormalAppointment: true);
        }

        internal static void ConsumeAfterCommittedAppointment(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId,
            bool pActing)
        {
            if (pActing || !HasUsableCredential(pActor, pKingdom, pLayer,
                    pOfficeId)) return;
            pActor.data.set(
                LineageKeys.CIVIL_SERVICE_LEGACY_CREDENTIAL_REMAINING,
                false);
        }

        internal static void AppendEligibleCandidates(Kingdom pKingdom,
            List<Actor> pRoster)
        {
            if (pKingdom?.data == null || pRoster == null) return;
            var knownActorIds = new HashSet<long>();
            foreach (Actor actor in pRoster)
                if (actor?.data != null) knownActorIds.Add(actor.data.id);

            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                    TryAppendEligibleCandidate(actor, pKingdom, pRoster,
                        knownActorIds);
            }
            catch { }
        }

        private static void TryAppendEligibleCandidate(Actor pActor,
            Kingdom pKingdom, List<Actor> pRoster,
            HashSet<long> pKnownActorIds)
        {
            try
            {
                if (pActor?.data == null ||
                    pKnownActorIds.Contains(pActor.data.id) ||
                    !HasUsableCredential(pActor, pKingdom,
                        CourtOfficeLayer.Central,
                        CourtOfficeId.TaiZai)) return;
                pRoster.Add(pActor);
                pKnownActorIds.Add(pActor.data.id);
            }
            catch { }
        }

        private static bool NeedsTransition(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                !CivilServiceQualificationService.HasExaminationSystem(
                    pKingdom)) return false;
            pKingdom.data.get(LineageKeys.CIVIL_SERVICE_LEGACY_TRANSITION_VERSION,
                out int version, 0);
            return version < CivilServiceLegacyTransitionRules.TransitionVersion;
        }

        private static bool ApplyTransition(Kingdom pKingdom)
        {
            if (!NeedsTransition(pKingdom)) return true;

            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                    TryGrantCredentialIfEligible(actor, pKingdom);
            }
            catch
            {
                return false;
            }

            pKingdom.data.set(LineageKeys.CIVIL_SERVICE_LEGACY_TRANSITION_VERSION,
                CivilServiceLegacyTransitionRules.TransitionVersion);
            return true;
        }

        private static void GrantCredentialIfEligible(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string currentOffice, "");
            bool hasFormalQualification =
                CivilServiceQualificationService.HasFormalQualification(
                    pActor, pKingdom);
            bool transitionAlreadyApplied = HasUsableCredential(pActor,
                pKingdom, CourtOfficeLayer.Central, CourtOfficeId.TaiZai);
            if (!CivilServiceLegacyTransitionRules.ShouldIssueCredential(
                    transitionAlreadyApplied,
                    IsPreExaminationCandidateEligible(pActor, pKingdom),
                    hasFormalQualification,
                    !string.IsNullOrEmpty(currentOffice))) return;

            pActor.data.set(
                LineageKeys.CIVIL_SERVICE_LEGACY_CREDENTIAL_KINGDOM_ID,
                pKingdom.id);
            pActor.data.set(
                LineageKeys.CIVIL_SERVICE_LEGACY_CREDENTIAL_REMAINING,
                true);
        }

        private static void TryGrantCredentialIfEligible(Actor pActor,
            Kingdom pKingdom)
        {
            try { GrantCredentialIfEligible(pActor, pKingdom); }
            catch { }
        }

        private static bool IsPreExaminationCandidateEligible(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pActor.isRekt() || !RoyalGuardOfficeRules.
                    CanAppearInOfficeCandidateList(
                        RoyalGuardService.IsRoyalGuard(pActor))) return false;
            bool alive = pActor.isAlive();
            bool adult = pActor.isAdult();
            bool male = pActor.isSexMale();
            bool slave = pActor.hasTrait(LineageKeys.TRAIT_SLAVE);
            bool madness = pActor.hasTrait("madness");
            bool king = pActor.isKing();
            bool domestic = CourtAffiliationResolver.IsDomestic(pActor,
                pKingdom);
            bool royalAsylum = RoyalAsylumService.IsActive(pActor);
            bool affiliationAvailable = HistoricalAffiliationService.
                IsAvailableForOffice(pActor);
            if (!CourtManualAppointmentRules.CanListCandidate(
                    new CourtManualCandidateFacts(alive, adult, domestic,
                        slave, madness, male, royalAsylum, king,
                        hasCentralOffice: false, affiliationAvailable)))
                return false;
            return HistoricalSchoolEducationService.CanAppoint(pActor,
                pKingdom, CourtOfficeLayer.Central, CourtOfficeId.TaiZai);
        }

        private static bool IsFormalCivilAppointment(string pLayer,
            string pOfficeId)
        {
            if (pLayer == CourtOfficeLayer.Military ||
                pOfficeId == CourtOfficeId.SiMa ||
                pOfficeId == CourtOfficeId.Marshal ||
                pOfficeId == CourtOfficeId.Bingbu ||
                pOfficeId == CourtPyramidRoleId.General) return false;
            return pLayer == CourtOfficeLayer.Central ||
                   pLayer == CourtOfficeLayer.City ||
                   pLayer == CourtOfficeLayer.Censor ||
                   pLayer == CourtOfficeLayer.Feudatory;
        }
    }
}
