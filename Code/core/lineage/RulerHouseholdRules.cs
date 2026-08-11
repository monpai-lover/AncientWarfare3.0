using System;

namespace AncientWarfare3.core.lineage
{
    public enum RulerHouseholdKind
    {
        PrincipalWife,
        Consort
    }

    public enum RulerHouseholdRealmTier
    {
        Lower,
        Kingdom,
        Empire
    }

    public readonly struct RulerHouseholdOfferFacts
    {
        public RulerHouseholdOfferFacts(bool candidateEligible,
            bool rulerEligible, bool related, bool hasPrincipalWife,
            int activeConsorts, int consortCapacity)
        {
            CandidateEligible = candidateEligible;
            RulerEligible = rulerEligible;
            Related = related;
            HasPrincipalWife = hasPrincipalWife;
            ActiveConsorts = activeConsorts;
            ConsortCapacity = consortCapacity;
        }

        public bool CandidateEligible { get; }
        public bool RulerEligible { get; }
        public bool Related { get; }
        public bool HasPrincipalWife { get; }
        public int ActiveConsorts { get; }
        public int ConsortCapacity { get; }
    }

    public readonly struct RulerHouseholdConsortRequestFacts
    {
        public RulerHouseholdConsortRequestFacts(bool rulerEligible,
            int activeConsorts, int consortCapacity,
            bool requesterIndependent, bool supplierIndependent, int opinion,
            bool supplierHasCandidate, bool equivalentPending,
            bool rejectionCooldown)
        {
            RulerEligible = rulerEligible;
            ActiveConsorts = activeConsorts;
            ConsortCapacity = consortCapacity;
            RequesterIndependent = requesterIndependent;
            SupplierIndependent = supplierIndependent;
            Opinion = opinion;
            SupplierHasCandidate = supplierHasCandidate;
            EquivalentPending = equivalentPending;
            RejectionCooldown = rejectionCooldown;
        }

        public bool RulerEligible { get; }
        public int ActiveConsorts { get; }
        public int ConsortCapacity { get; }
        public bool RequesterIndependent { get; }
        public bool SupplierIndependent { get; }
        public int Opinion { get; }
        public bool SupplierHasCandidate { get; }
        public bool EquivalentPending { get; }
        public bool RejectionCooldown { get; }
    }

    public static class RulerHouseholdRules
    {
        public const int MaximumPregnancyStartsPerKingdomYear = 2;
        public const int MinimumCandidateAge = 18;
        public const int MaximumCandidateAge = 33;
        public const int MinimumConsortRequestOpinion = 30;
        public const string ConsortRequestDetailId = "consort_request";

        public static int ConsortCapacity(RulerHouseholdRealmTier pTier)
        {
            return pTier switch
            {
                RulerHouseholdRealmTier.Empire => 8,
                RulerHouseholdRealmTier.Kingdom => 4,
                _ => 2
            };
        }

        public static string TitleKey(RulerHouseholdRealmTier pTier,
            RulerHouseholdKind pKind)
        {
            return TitleKey(pTier, pKind, rulerIsFemale: false);
        }

        public static string TitleKey(RulerHouseholdRealmTier pTier,
            RulerHouseholdKind pKind, bool rulerIsFemale)
        {
            if (pKind == RulerHouseholdKind.PrincipalWife)
            {
                if (rulerIsFemale)
                    return "aw_household_title_royal_husband";
                return pTier switch
                {
                    RulerHouseholdRealmTier.Empire =>
                        "aw_household_title_empress",
                    RulerHouseholdRealmTier.Kingdom =>
                        "aw_household_title_queen",
                    _ => "aw_household_title_principal_wife"
                };
            }

            return pTier switch
            {
                RulerHouseholdRealmTier.Empire =>
                    "aw_household_title_imperial_consort",
                RulerHouseholdRealmTier.Kingdom =>
                    "aw_household_title_royal_consort",
                _ => "aw_household_title_secondary_consort"
            };
        }

        public static bool ShouldChildFollowPromotedParent(
            bool parentIsMale, bool parentIsReigningRuler,
            bool fatherIsMatrilocal)
        {
            return parentIsMale || parentIsReigningRuler ||
                   fatherIsMatrilocal;
        }

        public static bool ShouldEstablishMatrilocal(bool womanValid,
            int womanAuthorityTier, bool manValid, int manAuthorityTier)
        {
            return womanValid && manValid && womanAuthorityTier > 0 &&
                   womanAuthorityTier > manAuthorityTier;
        }

        public static int SelectBirthLineageSourceSlot(bool parent1Male,
            bool parent1Complete, bool parent1MatrilocalToParent2,
            bool parent2Male, bool parent2Complete,
            bool parent2MatrilocalToParent1)
        {
            if (parent1MatrilocalToParent2 && parent2Complete) return 2;
            if (parent2MatrilocalToParent1 && parent1Complete) return 1;
            if (parent1Male && parent1Complete) return 1;
            if (parent2Male && parent2Complete) return 2;
            if (parent1Complete) return 1;
            return parent2Complete ? 2 : -1;
        }

        public static bool IsRoyalMarriageCandidate(bool otherwiseEligible,
            bool reigningRuler)
        {
            return otherwiseEligible && !reigningRuler;
        }

        public static bool IsHouseholdCandidateAge(int pAge)
        {
            return pAge >= MinimumCandidateAge &&
                   pAge <= MaximumCandidateAge;
        }

        public static int HouseholdCandidatePriority(
            bool memberOfRulingLineage)
        {
            return HouseholdCandidatePriority(memberOfRulingLineage,
                directChildOfRuler: false);
        }

        public static int HouseholdCandidatePriority(
            bool memberOfRulingLineage, bool directChildOfRuler)
        {
            if (memberOfRulingLineage && directChildOfRuler) return 0;
            return memberOfRulingLineage ? 1 : 2;
        }

        public static bool CanOffer(RulerHouseholdOfferFacts pFacts,
            RulerHouseholdKind pKind)
        {
            if (!pFacts.CandidateEligible || !pFacts.RulerEligible ||
                pFacts.Related)
                return false;

            if (pKind == RulerHouseholdKind.PrincipalWife)
                return !pFacts.HasPrincipalWife;

            return pFacts.ConsortCapacity > 0 &&
                   pFacts.ActiveConsorts < pFacts.ConsortCapacity;
        }

        public static int RelationshipBonus(RulerHouseholdKind pKind)
        {
            return pKind == RulerHouseholdKind.PrincipalWife ? 12 : 6;
        }

        public static bool TrySelectAiOfferKind(bool hasPrincipalWife,
            int activeConsorts, int consortCapacity,
            out RulerHouseholdKind pKind)
        {
            if (!hasPrincipalWife)
            {
                pKind = RulerHouseholdKind.PrincipalWife;
                return true;
            }
            if (consortCapacity > 0 && activeConsorts >= 0 &&
                activeConsorts < consortCapacity)
            {
                pKind = RulerHouseholdKind.Consort;
                return true;
            }
            pKind = default;
            return false;
        }

        public static int AiProposalUrgency(bool hasPrincipalWife,
            int activeConsorts)
        {
            return !hasPrincipalWife || activeConsorts <= 0 ? 30 : 0;
        }

        public static bool CanUpperRealmOfferToSubject(
            bool requesterSuzerainOfResponder, bool candidateAvailable,
            bool recipientRulerEligible)
        {
            return requesterSuzerainOfResponder && candidateAvailable &&
                   recipientRulerEligible;
        }

        public static bool CanRequestConsort(
            RulerHouseholdConsortRequestFacts pFacts)
        {
            return pFacts.RulerEligible && pFacts.ConsortCapacity > 0 &&
                   pFacts.ActiveConsorts >= 0 &&
                   pFacts.ActiveConsorts < pFacts.ConsortCapacity &&
                   pFacts.RequesterIndependent &&
                   pFacts.SupplierIndependent &&
                   pFacts.Opinion >= MinimumConsortRequestOpinion &&
                   pFacts.SupplierHasCandidate &&
                   !pFacts.EquivalentPending &&
                   !pFacts.RejectionCooldown;
        }

        public static bool IsConsortRequestDetail(string pDetailId)
        {
            return string.Equals(pDetailId, ConsortRequestDetailId,
                StringComparison.Ordinal);
        }

        public static bool ShouldDeferConsortRequestAcceptance(
            bool playerResponse, bool candidateSelected)
        {
            return playerResponse && !candidateSelected;
        }

        public static bool IsBetterConsortRequestTarget(
            float candidateDistance, int candidateOpinion,
            long candidateKingdomId, float currentDistance,
            int currentOpinion, long currentKingdomId)
        {
            if (currentKingdomId < 0L) return true;
            int distance = Math.Max(0f, candidateDistance).CompareTo(
                Math.Max(0f, currentDistance));
            if (distance != 0) return distance < 0;
            int opinion = candidateOpinion.CompareTo(currentOpinion);
            if (opinion != 0) return opinion > 0;
            return candidateKingdomId < currentKingdomId;
        }

        public static string DetailId(RulerHouseholdKind pKind)
        {
            return pKind == RulerHouseholdKind.PrincipalWife
                ? "principal_wife"
                : "consort";
        }

        public static bool TryParseKind(string pDetailId,
            out RulerHouseholdKind pKind)
        {
            if (string.Equals(pDetailId, "principal_wife",
                    StringComparison.Ordinal))
            {
                pKind = RulerHouseholdKind.PrincipalWife;
                return true;
            }
            if (string.Equals(pDetailId, "consort",
                    StringComparison.Ordinal))
            {
                pKind = RulerHouseholdKind.Consort;
                return true;
            }
            pKind = default;
            return false;
        }

        public static bool ShouldCloseRelationship(bool active,
            bool rulerAlive, bool partnerAlive, bool rulerStillReigning,
            bool sameRecipientRealm)
        {
            return active && (!rulerAlive || !partnerAlive ||
                              !rulerStillReigning || !sameRecipientRealm);
        }

        public static int PregnancyStartsForYear(int eligibleConsorts)
        {
            return Math.Min(Math.Max(eligibleConsorts, 0),
                MaximumPregnancyStartsPerKingdomYear);
        }

        public static bool ShouldQueryOnDeath(bool currentRuler,
            bool formerRuler, bool cachedHouseholdPartner)
        {
            return currentRuler || formerRuler || cachedHouseholdPartner;
        }
    }
}
