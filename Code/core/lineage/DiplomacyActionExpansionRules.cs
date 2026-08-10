using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum DiplomaticOperationType
    {
        None = 0,
        SpyNetwork = 1,
        ForgeDocuments = 2
    }

    public enum DiplomacySelectionIndicator
    {
        Disabled,
        Neutral,
        Accept,
        Reject
    }

    public enum CoalitionWarJoinSide
    {
        None = 0,
        Attackers = 1,
        Defenders = 2
    }

    public enum RoyalMarriageKinship
    {
        Collateral = 0,
        DirectChild = 1,
        Ruler = 2
    }

    public enum RoyalMarriageDirection
    {
        RequesterMaleResponderFemale = 0,
        RequesterFemaleResponderMale = 1
    }

    public readonly struct DiplomaticOperationChances
    {
        public DiplomaticOperationChances(int successChance,
            int discoveryChance)
        {
            SuccessChance = successChance;
            DiscoveryChance = discoveryChance;
        }

        public int SuccessChance { get; }
        public int DiscoveryChance { get; }
    }

    public readonly struct DiplomaticOperationOutcome
    {
        public DiplomaticOperationOutcome(bool succeeded, bool discovered)
        {
            Succeeded = succeeded;
            Discovered = discovered;
        }

        public bool Succeeded { get; }
        public bool Discovered { get; }
    }

    public readonly struct CoalitionTargetFacts
    {
        public CoalitionTargetFacts(bool distinctRealms,
            bool targetAlive, bool targetCivilized,
            bool subjectConflict, bool servingTargetInWar,
            bool targetHasMandate, float targetPower,
            float strongerMemberPower)
        {
            DistinctRealms = distinctRealms;
            TargetAlive = targetAlive;
            TargetCivilized = targetCivilized;
            SubjectConflict = subjectConflict;
            ServingTargetInWar = servingTargetInWar;
            TargetHasMandate = targetHasMandate;
            TargetPower = targetPower;
            StrongerMemberPower = strongerMemberPower;
        }

        public bool DistinctRealms { get; }
        public bool TargetAlive { get; }
        public bool TargetCivilized { get; }
        public bool SubjectConflict { get; }
        public bool ServingTargetInWar { get; }
        public bool TargetHasMandate { get; }
        public float TargetPower { get; }
        public float StrongerMemberPower { get; }
    }

    public readonly struct RoyalMarriageCandidateFacts
    {
        public RoyalMarriageCandidateFacts(long actorId, bool alive,
            bool adult, bool breedingAge, bool unmarried,
            bool royalLineage, bool male, bool reigningRuler = false)
        {
            ActorId = actorId;
            Alive = alive;
            Adult = adult;
            BreedingAge = breedingAge;
            Unmarried = unmarried;
            RoyalLineage = royalLineage;
            Male = male;
            ReigningRuler = reigningRuler;
        }

        public long ActorId { get; }
        public bool Alive { get; }
        public bool Adult { get; }
        public bool BreedingAge { get; }
        public bool Unmarried { get; }
        public bool RoyalLineage { get; }
        public bool Male { get; }
        public bool ReigningRuler { get; }
    }

    public readonly struct RoyalMarriagePairScore
    {
        public RoyalMarriagePairScore(long firstActorId,
            long secondActorId, int directRoyalChildren,
            int generationDistance, int ageDifference,
            int combinedMerit)
        {
            FirstActorId = firstActorId;
            SecondActorId = secondActorId;
            DirectRoyalChildren = directRoyalChildren;
            GenerationDistance = generationDistance;
            AgeDifference = ageDifference;
            CombinedMerit = combinedMerit;
        }

        public long FirstActorId { get; }
        public long SecondActorId { get; }
        public int DirectRoyalChildren { get; }
        public int GenerationDistance { get; }
        public int AgeDifference { get; }
        public int CombinedMerit { get; }
    }

    public static class DiplomacyActionExpansionRules
    {
        public const int MaximumRoyalCandidatesPerRealm = 24;
        public const int MaximumRoyalArchiveIdsScannedPerRealm = 96;
        public const int MaximumCoalitionTargets = 64;
        public const int MaximumActiveCoalitionsPerRealm = 2;
        public const int CoalitionYears = 12;
        public const int SpyNetworkYears = 8;
        public const int MaximumDueOperationsPerFrame = 1;
        public const int MaximumMarriageMaintenanceRows = 8;
        public const int MaximumAiForgeryCitiesScanned = 4;
        public const int StrongForgeryMinimumNetworkStrength = 60;

        public static DiplomacySelectionIndicator ResolveSelectionIndicator(
            bool available, bool selected, bool requiresSecondarySelection,
            bool expectedAccepted)
        {
            if (!available) return DiplomacySelectionIndicator.Disabled;
            if (requiresSecondarySelection && !selected)
                return DiplomacySelectionIndicator.Neutral;
            return expectedAccepted
                ? DiplomacySelectionIndicator.Accept
                : DiplomacySelectionIndicator.Reject;
        }

        public static bool ShouldUseAnnexationOperation(
            bool sourceIsDirectSuzerain, bool hasActiveSpyNetwork)
        {
            return sourceIsDirectSuzerain && hasActiveSpyNetwork;
        }

        public static bool IsEligibleCoalitionTarget(
            CoalitionTargetFacts pFacts)
        {
            if (!pFacts.DistinctRealms || !pFacts.TargetAlive ||
                !pFacts.TargetCivilized || pFacts.SubjectConflict ||
                pFacts.ServingTargetInWar)
                return false;
            if (pFacts.TargetHasMandate) return true;
            float memberPower = Math.Max(1f, pFacts.StrongerMemberPower);
            return Math.Max(0f, pFacts.TargetPower) >= memberPower * 1.25f;
        }

        public static CoalitionWarJoinSide ResolveCoalitionWarJoin(
            bool active, int currentYear, int endYear,
            bool targetIsAttacker, bool targetIsDefender,
            bool memberIsAttacker, bool memberIsDefender,
            bool partnerAlreadyInWar, bool subjectConflict)
        {
            if (!active || currentYear > endYear || partnerAlreadyInWar ||
                subjectConflict || targetIsAttacker == targetIsDefender)
                return CoalitionWarJoinSide.None;
            if (targetIsDefender && memberIsAttacker)
                return CoalitionWarJoinSide.Attackers;
            if (targetIsAttacker && memberIsDefender)
                return CoalitionWarJoinSide.Defenders;
            return CoalitionWarJoinSide.None;
        }

        public static string CoalitionUnavailableReason(bool membersAtWar,
            bool requesterSubject, bool responderSubject,
            int requesterActiveCount, int responderActiveCount,
            bool duplicateTarget, bool validTarget)
        {
            if (membersAtWar) return "at_war";
            if (requesterSubject) return "requester_subject";
            if (responderSubject) return "responder_subject";
            if (requesterActiveCount >= MaximumActiveCoalitionsPerRealm ||
                responderActiveCount >= MaximumActiveCoalitionsPerRealm)
                return "coalition_limit";
            if (duplicateTarget) return "active_coalition";
            return validTarget ? "" : "invalid_coalition_target";
        }

        public static bool IsEligibleMarriageCandidate(
            RoyalMarriageCandidateFacts pFacts)
        {
            return pFacts.ActorId >= 0 && pFacts.Alive && pFacts.Adult &&
                   pFacts.BreedingAge && pFacts.Unmarried &&
                   pFacts.RoyalLineage && !pFacts.ReigningRuler;
        }

        public static RoyalMarriageKinship ClassifyMarriageKinship(
            long actorId, long rulerId, long firstParentId,
            long secondParentId)
        {
            if (actorId >= 0 && actorId == rulerId)
                return RoyalMarriageKinship.Ruler;
            if (rulerId >= 0 &&
                (firstParentId == rulerId || secondParentId == rulerId))
                return RoyalMarriageKinship.DirectChild;
            return RoyalMarriageKinship.Collateral;
        }

        public static bool IsDirectMarriageKinship(
            RoyalMarriageKinship pKinship)
        {
            return pKinship == RoyalMarriageKinship.Ruler ||
                   pKinship == RoyalMarriageKinship.DirectChild;
        }

        public static int MarriageGenerationDistance(
            RoyalMarriageKinship pKinship)
        {
            return pKinship switch
            {
                RoyalMarriageKinship.Ruler => 0,
                RoyalMarriageKinship.DirectChild => 1,
                _ => 2
            };
        }

        public static bool MatchesMarriageDirection(
            RoyalMarriageDirection pDirection, bool requesterSide,
            bool male)
        {
            bool requesterMale = pDirection ==
                                 RoyalMarriageDirection
                                     .RequesterMaleResponderFemale;
            return requesterSide ? male == requesterMale :
                male != requesterMale;
        }

        public static bool CanPairMarriageInDirection(
            RoyalMarriageCandidateFacts pRequester,
            RoyalMarriageCandidateFacts pResponder, bool related,
            RoyalMarriageDirection pDirection)
        {
            return MatchesMarriageDirection(pDirection,
                       requesterSide: true, pRequester.Male) &&
                   MatchesMarriageDirection(pDirection,
                       requesterSide: false, pResponder.Male) &&
                   CanPairMarriage(pRequester, pResponder, related);
        }

        public static bool IsAvailableForDynasticMarriage(
            bool hasNaturalPartner, bool hasActiveDynasticMarriage)
        {
            // WorldBox lover links form naturally; only our ledger is a
            // binding diplomatic marriage.
            _ = hasNaturalPartner;
            return !hasActiveDynasticMarriage;
        }

        public static bool CanPairMarriage(
            RoyalMarriageCandidateFacts first,
            RoyalMarriageCandidateFacts second, bool related)
        {
            return IsEligibleMarriageCandidate(first) &&
                   IsEligibleMarriageCandidate(second) &&
                   first.ActorId != second.ActorId &&
                   first.Male != second.Male && !related;
        }

        public static bool TryFindMarriagePair(
            IReadOnlyList<RoyalMarriageCandidateFacts> pRequesterCandidates,
            IReadOnlyList<RoyalMarriageCandidateFacts> pResponderCandidates,
            Func<long, long, bool> pRelated,
            out long pRequesterActorId, out long pResponderActorId)
        {
            pRequesterActorId = -1L;
            pResponderActorId = -1L;
            if (pRequesterCandidates == null || pResponderCandidates == null)
                return false;
            for (int i = 0; i < pRequesterCandidates.Count; i++)
            for (int j = 0; j < pResponderCandidates.Count; j++)
            {
                RoyalMarriageCandidateFacts requester =
                    pRequesterCandidates[i];
                RoyalMarriageCandidateFacts responder =
                    pResponderCandidates[j];
                if (!CanPairMarriage(requester, responder,
                        pRelated?.Invoke(requester.ActorId,
                            responder.ActorId) ?? true))
                    continue;
                pRequesterActorId = requester.ActorId;
                pResponderActorId = responder.ActorId;
                return true;
            }
            return false;
        }

        public static int CompareMarriagePair(
            RoyalMarriagePairScore pLeft,
            RoyalMarriagePairScore pRight)
        {
            int result = pRight.DirectRoyalChildren.CompareTo(
                pLeft.DirectRoyalChildren);
            if (result != 0) return result;
            result = pLeft.GenerationDistance.CompareTo(
                pRight.GenerationDistance);
            if (result != 0) return result;
            result = pLeft.AgeDifference.CompareTo(pRight.AgeDifference);
            if (result != 0) return result;
            result = pRight.CombinedMerit.CompareTo(pLeft.CombinedMerit);
            if (result != 0) return result;
            result = pLeft.FirstActorId.CompareTo(pRight.FirstActorId);
            return result != 0
                ? result
                : pLeft.SecondActorId.CompareTo(pRight.SecondActorId);
        }

        public static long StableActorTie(long pFirstActorId,
            long pSecondActorId)
        {
            return Math.Min(pFirstActorId, pSecondActorId);
        }

        public static long NextMarriageMaintenanceCursor(
            IReadOnlyList<long> pInspectedMarriageIds)
        {
            return pInspectedMarriageIds == null ||
                   pInspectedMarriageIds.Count == 0
                ? -1L
                : pInspectedMarriageIds[pInspectedMarriageIds.Count - 1];
        }

        public static int OperationDurationYears(float capitalDistanceTiles,
            int diplomacy, int intelligence, bool strongForgery)
        {
            int distanceYears = (int)Math.Floor(
                Math.Max(0f, capitalDistanceTiles) / 150f);
            int abilityReduction = Math.Max(0,
                Math.Max(0, diplomacy) + Math.Max(0, intelligence)) / 30;
            int years = 2 + distanceYears +
                        (strongForgery ? 1 : 0) - abilityReduction;
            return Math.Max(1, Math.Min(4, years));
        }

        public static int StablePercentRoll(long pOperationId,
            long pSourceKingdomId, long pTargetKingdomId,
            int pStartYear, int pSalt)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Mix(ref hash, (ulong)pOperationId);
                Mix(ref hash, (ulong)pSourceKingdomId);
                Mix(ref hash, (ulong)pTargetKingdomId);
                Mix(ref hash, (uint)pStartYear);
                Mix(ref hash, (uint)pSalt);
                return (int)(hash % 100UL);
            }
        }

        public static DiplomaticOperationChances OperationChances(
            int sourceDiplomacy, int sourceIntelligence,
            int targetDiplomacy, int targetIntelligence,
            bool forgery, bool strongForgery)
        {
            int sourceAbility = Math.Max(0, sourceDiplomacy) +
                                Math.Max(0, sourceIntelligence);
            int targetAbility = Math.Max(0, targetDiplomacy) +
                                Math.Max(0, targetIntelligence);
            int success = (forgery ? 55 : 65) +
                          (sourceAbility - targetAbility) / 4 -
                          (strongForgery ? 20 : 0);
            int discovery = (forgery ? 35 : 25) +
                            (targetAbility - sourceAbility) / 6 +
                            (strongForgery ? 15 : 0);
            return new DiplomaticOperationChances(
                Math.Max(10, Math.Min(90, success)),
                Math.Max(5, Math.Min(90, discovery)));
        }

        public static DiplomaticOperationOutcome ResolveOperationOutcome(
            int successRoll, int discoveryRoll,
            int successChance, int discoveryChance)
        {
            return new DiplomaticOperationOutcome(
                successRoll < Math.Max(0, Math.Min(100, successChance)),
                discoveryRoll < Math.Max(0, Math.Min(100, discoveryChance)));
        }

        public static int NetworkStrengthForSuccess(int successChance)
        {
            return Math.Max(40, Math.Min(90, successChance));
        }

        public static int NetworkExpiryYear(int completionYear)
        {
            return completionYear + SpyNetworkYears;
        }

        public static string ForgeUnavailableReason(bool activeSpyNetwork,
            bool targetCityOwned, bool canFabricate,
            bool strongForgery, int networkStrength)
        {
            if (!activeSpyNetwork) return "spy_network_required";
            if (!targetCityOwned) return "target_city_changed";
            if (!canFabricate) return "fabrication_unavailable";
            if (strongForgery && networkStrength <
                StrongForgeryMinimumNetworkStrength)
                return "network_too_weak";
            return "";
        }

        public static string MarriageUnavailableReason(bool atWar,
            bool bothHaveKings, bool activeMarriage,
            bool hasCandidatePair)
        {
            if (atWar) return "at_war";
            if (!bothHaveKings) return "missing_royal_house";
            if (activeMarriage) return "active_royal_marriage";
            if (!hasCandidatePair) return "no_royal_candidate";
            return "";
        }

        /// <summary>
        /// Resolves the AW dynasty used by royal-marriage candidate queries.
        /// A loaded king can temporarily lack the live LINEAGE_ID field while
        /// the kingdom's persisted AW succession identity is already valid.
        /// Never fall back to the game's native Clan/royal_clan_id here.
        /// </summary>
        public static long ResolveRoyalMarriageLineage(long actorLineageId,
            long kingdomLegitimateLineageId)
        {
            return actorLineageId >= 0L
                ? actorLineageId
                : kingdomLegitimateLineageId >= 0L
                    ? kingdomLegitimateLineageId
                    : -1L;
        }

        public static bool IsModifierActive(int currentYear,
            int untilYear)
        {
            return untilYear >= 0 && currentYear <= untilYear;
        }

        private static void Mix(ref ulong pHash, ulong pValue)
        {
            pHash ^= pValue;
            pHash *= 1099511628211UL;
        }
    }
}
