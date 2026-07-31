using System;

namespace AncientWarfare3.core.lineage
{
    internal enum OrdinaryDiplomacyDirection
    {
        None = 0,
        JoinWar = 1,
        VassalizeDemand = 2,
        VassalizeSeek = 3,
        VassalizeInternalize = 4,
        EndVassalRelease = 5,
        EndVassalRequest = 6,
        EndAlliance = 7,
        UpperHouseholdOffer = 8
    }

    internal static class DiplomacyProposalOpportunityRules
    {
        public const string VassalizeDemandDetail = "vassalize_demand";
        public const string VassalizeSeekDetail = "vassalize_seek";
        public const string VassalizeInternalizeDetail =
            "vassalize_internalize";
        public const string EndVassalReleaseDetail =
            "end_vassal_release";
        public const string EndVassalRequestDetail =
            "end_vassal_request";

        public static OrdinaryDiplomacyDirection JoinWarDirection(
            bool allied, bool requesterInWar, bool responderInWar,
            bool subjectConflict)
        {
            return allied && requesterInWar && !responderInWar &&
                   !subjectConflict
                ? OrdinaryDiplomacyDirection.JoinWar
                : OrdinaryDiplomacyDirection.None;
        }

        public static OrdinaryDiplomacyDirection VassalizeDirection(
            bool atWar, bool allied, bool requesterIsSubject,
            bool responderIsSubject, bool canSetVassal,
            float requesterToResponderPower, bool threatened,
            bool defensiveEmergency,
            bool requesterTributaryOfResponder, bool responderImperial)
        {
            if (requesterTributaryOfResponder && responderImperial &&
                canSetVassal)
                return OrdinaryDiplomacyDirection.VassalizeInternalize;
            if (allied || requesterIsSubject || responderIsSubject ||
                !canSetVassal)
                return OrdinaryDiplomacyDirection.None;
            if (!atWar && requesterToResponderPower >= 2f)
                return OrdinaryDiplomacyDirection.VassalizeDemand;
            if (((!atWar && threatened) || defensiveEmergency) &&
                requesterToResponderPower < 1f)
                return OrdinaryDiplomacyDirection.VassalizeSeek;
            return OrdinaryDiplomacyDirection.None;
        }

        public static int InternalizationTier(
            bool requesterTributaryOfResponder, bool responderImperial,
            bool responderHasMandate)
        {
            if (!requesterTributaryOfResponder || !responderImperial)
                return -1;
            return responderHasMandate
                ? VassalContractTierRules.Inner
                : VassalContractTierRules.Outer;
        }

        public static OrdinaryDiplomacyDirection EndVassalDirection(
            bool requesterSuzerainOfResponder,
            bool requesterSubjectOfResponder)
        {
            if (requesterSuzerainOfResponder)
                return OrdinaryDiplomacyDirection.EndVassalRelease;
            return requesterSubjectOfResponder
                ? OrdinaryDiplomacyDirection.EndVassalRequest
                : OrdinaryDiplomacyDirection.None;
        }

        public static bool ShouldEndAlliance(bool allied, int opinion,
            int liabilityScore)
        {
            return allied && (opinion <= -40 || liabilityScore >= 50);
        }

        public static bool CanUpperRealmOfferHousehold(
            bool requesterSuzerainOfResponder, bool candidateAvailable,
            bool recipientRulerEligible)
        {
            return RulerHouseholdRules.CanUpperRealmOfferToSubject(
                requesterSuzerainOfResponder, candidateAvailable,
                recipientRulerEligible);
        }

        public static int ProtectionRiskPenalty(float enemyToProtectorPower,
            bool excellentRelations, bool sharedEnemy, float warCourt)
        {
            float ratio = Math.Max(0f, enemyToProtectorPower);
            int penalty = ratio > 1.6f
                ? -70
                : ratio >= 1.2f
                    ? -35
                    : ratio <= .8f
                        ? 15
                        : 0;
            if (excellentRelations) penalty += 15;
            if (sharedEnemy) penalty += 15;
            if (warCourt >= .75f) penalty += 15;
            return penalty;
        }
    }
}
