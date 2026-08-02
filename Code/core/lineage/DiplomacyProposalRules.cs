using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum DiplomacyProposalType
    {
        None = 0,
        Alliance = 1,
        Peace = 2,
        NonAggression = 3,
        JoinWar = 4,
        Vassalize = 5,
        Tributary = 6,
        EndAlliance = 7,
        EndVassal = 8,
        Truce = 9,
        BreakNonAggression = 10,
        Coalition = 11,
        RoyalMarriage = 12,
        Surrender = 13,
        EnforceDemands = 14,
        HouseholdOffering = 15
    }

    public enum DiplomacyProposalStatus
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,
        Expired = 3,
        Cancelled = 4,
        Processing = 5
    }

    public readonly struct DiplomacyProposalScoreFacts
    {
        public DiplomacyProposalScoreFacts(int opinion, float requesterPower,
            float responderPower, bool sharedEnemy, bool responderLosingWar,
            bool allied, bool requesterIsMandate, float requesterDiplomacy,
            float responderDiplomacy,
            bool proposedMarriageDirectRoyal = false,
            float responderPeace = .5f,
            float responderAggression = .5f,
            bool requesterLosingWar = false,
            bool requesterReadyForPeace = false,
            bool responderReadyForPeace = false,
            bool responderReadyToConcede = false,
            bool hasWarSettlementReadiness = false,
            int requesterSurrenderWarSituation = 0,
            int requesterSurrenderPower = 0,
            int requesterSurrenderResolve = 0,
            bool hasDetailedSurrender = false,
            bool proposedPrincipalWife = false,
            bool proposedConsortRequest = false)
        {
            Opinion = opinion;
            RequesterPower = requesterPower;
            ResponderPower = responderPower;
            SharedEnemy = sharedEnemy;
            ResponderLosingWar = responderLosingWar;
            Allied = allied;
            RequesterIsMandate = requesterIsMandate;
            RequesterDiplomacy = requesterDiplomacy;
            ResponderDiplomacy = responderDiplomacy;
            ProposedMarriageDirectRoyal = proposedMarriageDirectRoyal;
            ResponderPeace = responderPeace;
            ResponderAggression = responderAggression;
            RequesterLosingWar = requesterLosingWar;
            RequesterReadyForPeace = requesterReadyForPeace;
            ResponderReadyForPeace = responderReadyForPeace;
            ResponderReadyToConcede = responderReadyToConcede;
            HasWarSettlementReadiness = hasWarSettlementReadiness;
            RequesterSurrenderWarSituation = requesterSurrenderWarSituation;
            RequesterSurrenderPower = requesterSurrenderPower;
            RequesterSurrenderResolve = requesterSurrenderResolve;
            HasDetailedSurrender = hasDetailedSurrender;
            ProposedPrincipalWife = proposedPrincipalWife;
            ProposedConsortRequest = proposedConsortRequest;
        }

        public int Opinion { get; }
        public float RequesterPower { get; }
        public float ResponderPower { get; }
        public bool SharedEnemy { get; }
        public bool ResponderLosingWar { get; }
        public bool Allied { get; }
        public bool RequesterIsMandate { get; }
        public float RequesterDiplomacy { get; }
        public float ResponderDiplomacy { get; }
        public bool ProposedMarriageDirectRoyal { get; }
        public float ResponderPeace { get; }
        public float ResponderAggression { get; }
        public bool RequesterLosingWar { get; }
        public bool RequesterReadyForPeace { get; }
        public bool ResponderReadyForPeace { get; }
        public bool ResponderReadyToConcede { get; }
        public bool HasWarSettlementReadiness { get; }
        public int RequesterSurrenderWarSituation { get; }
        public int RequesterSurrenderPower { get; }
        public int RequesterSurrenderResolve { get; }
        public bool HasDetailedSurrender { get; }
        public bool ProposedPrincipalWife { get; }
        public bool ProposedConsortRequest { get; }
    }

    public readonly struct DiplomacyProposalScorePart
    {
        public DiplomacyProposalScorePart(string pKey, int pValue)
        {
            Key = pKey ?? "";
            Value = pValue;
        }

        public string Key { get; }
        public int Value { get; }
    }

    public readonly struct DiplomacyAvailabilityFacts
    {
        public DiplomacyAvailabilityFacts(bool atWar, bool allied,
            bool requesterIsSubject, bool responderIsSubject,
            bool directSubjectRelation, bool hasJoinableWar,
            bool requesterIsMandate, bool activeNonAggression,
            bool activeTruce, string subjectFailureReason = "",
            string allianceFailureReason = "")
            : this(atWar, allied, requesterIsSubject, responderIsSubject,
                directSubjectRelation, hasJoinableWar, requesterIsMandate,
                activeNonAggression, activeTruce, subjectFailureReason,
                allianceFailureReason, activeWarPreparation: false,
                peaceNegotiators: true)
        {
        }

        public DiplomacyAvailabilityFacts(bool atWar, bool allied,
            bool requesterIsSubject, bool responderIsSubject,
            bool directSubjectRelation, bool hasJoinableWar,
            bool requesterIsMandate, bool activeNonAggression,
            bool activeTruce, string subjectFailureReason,
            string allianceFailureReason, bool activeWarPreparation)
            : this(atWar, allied, requesterIsSubject, responderIsSubject,
                directSubjectRelation, hasJoinableWar, requesterIsMandate,
                activeNonAggression, activeTruce, subjectFailureReason,
                allianceFailureReason, activeWarPreparation,
                peaceNegotiators: true)
        {
        }

        public DiplomacyAvailabilityFacts(bool atWar, bool allied,
            bool requesterIsSubject, bool responderIsSubject,
            bool directSubjectRelation, bool hasJoinableWar,
            bool requesterIsMandate, bool activeNonAggression,
            bool activeTruce, string subjectFailureReason,
            string allianceFailureReason, bool activeWarPreparation,
            bool peaceNegotiators,
            WarSettlementPosition warPosition = WarSettlementPosition.Contested)
        {
            AtWar = atWar;
            Allied = allied;
            RequesterIsSubject = requesterIsSubject;
            ResponderIsSubject = responderIsSubject;
            DirectSubjectRelation = directSubjectRelation;
            HasJoinableWar = hasJoinableWar;
            RequesterIsMandate = requesterIsMandate;
            ActiveNonAggression = activeNonAggression;
            ActiveTruce = activeTruce;
            SubjectFailureReason = subjectFailureReason ?? "";
            AllianceFailureReason = allianceFailureReason ?? "";
            ActiveWarPreparation = activeWarPreparation;
            PeaceNegotiators = peaceNegotiators;
            WarPosition = warPosition;
        }

        public bool AtWar { get; }
        public bool Allied { get; }
        public bool RequesterIsSubject { get; }
        public bool ResponderIsSubject { get; }
        public bool DirectSubjectRelation { get; }
        public bool HasJoinableWar { get; }
        public bool RequesterIsMandate { get; }
        public bool ActiveNonAggression { get; }
        public bool ActiveTruce { get; }
        public string SubjectFailureReason { get; }
        public string AllianceFailureReason { get; }
        public bool ActiveWarPreparation { get; }
        public bool PeaceNegotiators { get; }
        public WarSettlementPosition WarPosition { get; }
    }

    public sealed class DiplomacyProposalAssessment
    {
        public DiplomacyProposalAssessment(int pScore, int pThreshold,
            IReadOnlyList<DiplomacyProposalScorePart> pParts)
        {
            Score = pScore;
            Threshold = pThreshold;
            ExpectedAccepted = pScore >= pThreshold;
            Parts = pParts ?? Array.Empty<DiplomacyProposalScorePart>();
        }

        public int Score { get; }
        public int Threshold { get; }
        public bool ExpectedAccepted { get; }
        public IReadOnlyList<DiplomacyProposalScorePart> Parts { get; }
    }

    public static class DiplomacyProposalRules
    {
        private const double WorldTimePerDay = 1d / 6d;
        public const int AcceptanceThreshold = 60;
        public const int AiProposalCooldownYears = 8;
        public const int AiRejectionCooldownYears = 12;
        public const int AiPeaceRejectionCooldownYears = 1;
        public const int TruceYears = 10;
        public const int BrokenPactTruceYears = 5;
        public const int MinimumResponseDelayDays = 3;
        public const int MaximumResponseDelayDays = 180;
        public const int MaximumProcessingRecoveriesPerFrame = 1;
        public const float MaximumNonBorderAllianceCapitalDistance = 120f;

        public static double NextResponseRuntimeTime(double pCurrentTime)
        {
            return pCurrentTime + WorldTimePerDay;
        }

        public static bool IsOutstanding(DiplomacyProposalStatus pStatus)
        {
            return pStatus == DiplomacyProposalStatus.Pending ||
                   pStatus == DiplomacyProposalStatus.Processing;
        }

        public static bool ShouldRetryFailedResponse(
            DiplomacyProposalStatus pStatus)
        {
            return pStatus == DiplomacyProposalStatus.Pending;
        }

        public static int ExpiryYears(DiplomacyProposalType pType)
        {
            return IsPeaceProposal(pType) ? 2 : 4;
        }

        public static bool CanOpenPair(bool sameKingdom,
            bool existingPending)
        {
            return !sameKingdom && !existingPending;
        }

        public static bool CanCreate(DiplomacyProposalType pType,
            bool atWar, bool allied, bool requesterIsSubject,
            bool responderIsSubject, bool hasJoinableWar)
        {
            return CanCreate(pType, new DiplomacyAvailabilityFacts(
                atWar, allied, requesterIsSubject, responderIsSubject,
                requesterIsSubject || responderIsSubject, hasJoinableWar,
                requesterIsMandate: true, activeNonAggression: false,
                activeTruce: false));
        }

        public static bool CanCreate(DiplomacyProposalType pType,
            DiplomacyAvailabilityFacts pFacts)
        {
            return string.IsNullOrEmpty(UnavailableReason(pType, pFacts));
        }

        public static string AllianceDistanceFailure(bool sharesBorder,
            bool hasBothCapitals, float capitalDistance)
        {
            if (sharesBorder) return "";
            if (!hasBothCapitals) return "alliance_unavailable";
            return !float.IsNaN(capitalDistance) &&
                   !float.IsInfinity(capitalDistance) &&
                   capitalDistance >= 0f && capitalDistance <=
                   MaximumNonBorderAllianceCapitalDistance
                ? ""
                : "alliance_too_distant";
        }

        public static string UnavailableReason(DiplomacyProposalType pType,
            DiplomacyAvailabilityFacts pFacts)
        {
            if (pFacts.ActiveWarPreparation && IsCooperative(pType))
                return "war_preparation";
            switch (pType)
            {
                case DiplomacyProposalType.Alliance:
                    if (pFacts.AtWar) return "at_war";
                    if (pFacts.Allied) return "already_allied";
                    if (pFacts.RequesterIsSubject) return "requester_subject";
                    if (pFacts.ResponderIsSubject) return "responder_subject";
                    if (!string.IsNullOrEmpty(pFacts.AllianceFailureReason))
                        return pFacts.AllianceFailureReason;
                    return "";
                case DiplomacyProposalType.Peace:
                    if (!pFacts.AtWar) return "not_at_war";
                    return pFacts.PeaceNegotiators ? "" : "not_war_leader";
                case DiplomacyProposalType.Surrender:
                    if (!pFacts.AtWar) return "not_at_war";
                    if (!pFacts.PeaceNegotiators) return "not_war_leader";
                    return pFacts.WarPosition == WarSettlementPosition.Losing
                        ? ""
                        : "not_losing_war";
                case DiplomacyProposalType.EnforceDemands:
                    if (!pFacts.AtWar) return "not_at_war";
                    if (!pFacts.PeaceNegotiators) return "not_war_leader";
                    return pFacts.WarPosition == WarSettlementPosition.Winning
                        ? ""
                        : "not_winning_war";
                case DiplomacyProposalType.NonAggression:
                    if (pFacts.AtWar) return "at_war";
                    if (pFacts.Allied) return "already_allied";
                    if (pFacts.DirectSubjectRelation)
                        return "subject_non_aggression";
                    if (pFacts.ActiveNonAggression)
                        return "active_non_aggression";
                    return "";
                case DiplomacyProposalType.BreakNonAggression:
                    if (pFacts.AtWar) return "at_war";
                    return pFacts.ActiveNonAggression
                        ? ""
                        : "no_active_non_aggression";
                case DiplomacyProposalType.JoinWar:
                    if (pFacts.AtWar) return "at_war";
                    if (!pFacts.Allied) return "not_allied";
                    if (pFacts.RequesterIsSubject) return "requester_subject";
                    if (pFacts.ResponderIsSubject) return "responder_subject";
                    return pFacts.HasJoinableWar ? "" : "no_joinable_war";
                case DiplomacyProposalType.Vassalize:
                    if (pFacts.AtWar) return "at_war";
                    if (pFacts.Allied) return "already_allied";
                    if (pFacts.RequesterIsSubject) return "requester_subject";
                    if (pFacts.ResponderIsSubject) return "responder_subject";
                    if (!string.IsNullOrEmpty(pFacts.SubjectFailureReason))
                        return pFacts.SubjectFailureReason;
                    return "";
                case DiplomacyProposalType.Tributary:
                    if (!pFacts.RequesterIsMandate) return "requires_mandate";
                    if (pFacts.AtWar) return "at_war";
                    if (pFacts.Allied) return "already_allied";
                    if (pFacts.RequesterIsSubject) return "requester_subject";
                    if (pFacts.ResponderIsSubject) return "responder_subject";
                    if (!string.IsNullOrEmpty(pFacts.SubjectFailureReason))
                        return pFacts.SubjectFailureReason;
                    return "";
                case DiplomacyProposalType.EndAlliance:
                    return pFacts.Allied ? "" : "not_allied";
                case DiplomacyProposalType.EndVassal:
                    return pFacts.DirectSubjectRelation
                        ? ""
                        : "no_vassal_relation";
                case DiplomacyProposalType.RoyalMarriage:
                case DiplomacyProposalType.HouseholdOffering:
                    return "";
                case DiplomacyProposalType.Coalition:
                    if (pFacts.AtWar) return "at_war";
                    if (pFacts.RequesterIsSubject) return "requester_subject";
                    if (pFacts.ResponderIsSubject) return "responder_subject";
                    return "";
                default:
                    return "unavailable";
            }
        }

        public static int ResponseDelayDays(float pCapitalDistanceTiles)
        {
            float distance = Math.Max(0f, pCapitalDistanceTiles);
            int days = MinimumResponseDelayDays +
                       (int)Math.Round(distance * 1.5f,
                           MidpointRounding.AwayFromZero);
            return Math.Max(MinimumResponseDelayDays,
                Math.Min(MaximumResponseDelayDays, days));
        }

        public static double WorldTimeForDays(int pDays)
        {
            return Math.Max(0, pDays) / 6d;
        }

        public static int TreatyYearsRemaining(int pCurrentYear,
            int pUntilYear)
        {
            return Math.Max(0, pUntilYear - pCurrentYear);
        }

        public static int AcceptanceScore(DiplomacyProposalType pType,
            DiplomacyProposalScoreFacts pFacts)
        {
            return Assess(pType, pFacts).Score;
        }

        public static DiplomacyProposalAssessment Assess(
            DiplomacyProposalType pType,
            DiplomacyProposalScoreFacts pFacts)
        {
            int opinion = Clamp(pFacts.Opinion, -100, 100);
            float requester = Math.Max(1f, pFacts.RequesterPower);
            float responder = Math.Max(1f, pFacts.ResponderPower);
            float ratio = requester / responder;
            int diplomacy = Clamp((int)Math.Round(
                (pFacts.RequesterDiplomacy - pFacts.ResponderDiplomacy) /
                3f), -8, 8);
            var parts = new List<DiplomacyProposalScorePart>(6);
            void Add(string pKey, int pValue)
            {
                parts.Add(new DiplomacyProposalScorePart(pKey, pValue));
            }

            switch (pType)
            {
                case DiplomacyProposalType.Alliance:
                    Add("base", 35);
                    Add("opinion", opinion / 4);
                    Add("shared_enemy", pFacts.SharedEnemy ? 15 : 0);
                    Add("mandate", pFacts.RequesterIsMandate ? 5 : 0);
                    Add("diplomacy", diplomacy);
                    break;
                case DiplomacyProposalType.BreakNonAggression:
                    Add("base", 100);
                    break;
                case DiplomacyProposalType.Peace:
                    Add("base", 42);
                    Add("opinion", opinion / 5);
                    Add("war_situation", pFacts.HasWarSettlementReadiness
                        ? pFacts.ResponderReadyForPeace ||
                          pFacts.RequesterLosingWar &&
                          pFacts.RequesterReadyForPeace ? 28 : -8
                        : pFacts.ResponderLosingWar ? 28 : -8);
                    Add("power", ratio >= 1.5f ? 8 : ratio <= .7f ? -8 : 0);
                    Add("court", PeaceCourtScore(pFacts.ResponderPeace,
                        pFacts.ResponderAggression));
                    break;
                case DiplomacyProposalType.Surrender:
                    Add("base", -100);
                    Add("war_situation", pFacts.HasDetailedSurrender
                        ? pFacts.RequesterSurrenderWarSituation
                        : pFacts.RequesterLosingWar ? 150 : -40);
                    Add("power", pFacts.HasDetailedSurrender
                        ? pFacts.RequesterSurrenderPower
                        : PowerSurrenderScore(ratio));
                    if (pFacts.HasDetailedSurrender)
                        Add("court", pFacts.RequesterSurrenderResolve);
                    break;
                case DiplomacyProposalType.EnforceDemands:
                    Add("base", 10);
                    Add("opinion", opinion / 10);
                    Add("war_situation", pFacts.HasWarSettlementReadiness
                        ? pFacts.ResponderReadyToConcede ? 50 : -30
                        : pFacts.ResponderLosingWar ? 50 : -30);
                    Add("power", PowerEnforcementScore(ratio));
                    Add("court", PeaceCourtScore(pFacts.ResponderPeace,
                        pFacts.ResponderAggression));
                    break;
                case DiplomacyProposalType.NonAggression:
                    Add("base", 42);
                    Add("opinion", opinion / 4);
                    Add("shared_enemy", pFacts.SharedEnemy ? 10 : 0);
                    Add("diplomacy", diplomacy);
                    break;
                case DiplomacyProposalType.JoinWar:
                    Add("base", 28);
                    Add("opinion", opinion / 4);
                    Add("alliance", pFacts.Allied ? 20 : 0);
                    Add("shared_enemy", pFacts.SharedEnemy ? 18 : 0);
                    Add("diplomacy", diplomacy);
                    break;
                case DiplomacyProposalType.Vassalize:
                    Add("base", 10);
                    Add("opinion", opinion / 5);
                    Add("power", PowerSubmissionScore(ratio));
                    Add("war_situation", pFacts.ResponderLosingWar ? 15 : 0);
                    Add("mandate", pFacts.RequesterIsMandate ? 5 : 0);
                    Add("diplomacy", diplomacy);
                    break;
                case DiplomacyProposalType.Tributary:
                    Add("base", 25);
                    Add("opinion", opinion / 5);
                    Add("power", PowerTributaryScore(ratio));
                    Add("war_situation", pFacts.ResponderLosingWar ? 12 : 0);
                    Add("mandate", pFacts.RequesterIsMandate ? 4 : 0);
                    Add("diplomacy", diplomacy);
                    break;
                case DiplomacyProposalType.EndAlliance:
                    Add("base", 35);
                    Add("opinion", -opinion / 3);
                    Add("alliance", pFacts.Allied ? 0 : 30);
                    break;
                case DiplomacyProposalType.EndVassal:
                    Add("base", 15);
                    Add("opinion", -opinion / 4);
                    Add("power", ratio <= .7f ? 25 : ratio >= 1.5f ? -20 : 0);
                    break;
                case DiplomacyProposalType.RoyalMarriage:
                    Add("base", 35);
                    Add("opinion", opinion / 3);
                    Add("diplomacy", diplomacy);
                    Add("direct_royal_marriage",
                        pFacts.ProposedMarriageDirectRoyal ? 10 : 0);
                    break;
                case DiplomacyProposalType.HouseholdOffering:
                    Add("base", 25);
                    Add("opinion", opinion / 3);
                    Add("diplomacy", diplomacy);
                    Add("household_rank",
                        pFacts.ProposedPrincipalWife ? 8 : 2);
                    Add("consort_request",
                        pFacts.ProposedConsortRequest ? 23 : 0);
                    break;
                case DiplomacyProposalType.Coalition:
                    Add("base", 32);
                    Add("opinion", opinion / 3);
                    Add("shared_enemy", pFacts.SharedEnemy ? 15 : 0);
                    Add("diplomacy", diplomacy);
                    break;
                default:
                    Add("base", 0);
                    break;
            }

            int score = 0;
            for (int i = 0; i < parts.Count; i++) score += parts[i].Value;
            return new DiplomacyProposalAssessment(Clamp(score, 0, 100),
                AcceptanceThreshold, parts);
        }

        public static DiplomacyProposalAssessment AssessProtectionRequest(
            int opinion, float requesterDiplomacy,
            float responderDiplomacy, int protectionRiskPenalty)
        {
            int normalizedOpinion = Clamp(opinion, -100, 100);
            int diplomacy = Clamp((int)Math.Round(
                (requesterDiplomacy - responderDiplomacy) / 3f), -8, 8);
            var parts = new List<DiplomacyProposalScorePart>(4)
            {
                new DiplomacyProposalScorePart("base", 30),
                new DiplomacyProposalScorePart("opinion",
                    normalizedOpinion / 2),
                new DiplomacyProposalScorePart("diplomacy", diplomacy),
                new DiplomacyProposalScorePart("protection_risk",
                    protectionRiskPenalty)
            };
            int score = 0;
            for (int index = 0; index < parts.Count; index++)
                score += parts[index].Value;
            return new DiplomacyProposalAssessment(Clamp(score, 0, 100),
                AcceptanceThreshold, parts);
        }

        public static bool ShouldAccept(int pScore)
        {
            return pScore >= AcceptanceThreshold;
        }

        public static bool CanSendAiProposal(bool playerInitiated,
            bool allowed, bool receiverExpectedAccepted,
            bool rejectionCooldownActive)
        {
            if (!allowed) return false;
            return playerInitiated ||
                   receiverExpectedAccepted && !rejectionCooldownActive;
        }

        public static bool IsAiRejectionCooldownActive(int currentYear,
            int rejectionYear)
        {
            return rejectionYear >= 0 && currentYear >= rejectionYear &&
                   currentYear - rejectionYear < AiRejectionCooldownYears;
        }

        public static int AiRejectionCooldownYearsFor(
            DiplomacyProposalType pType)
        {
            return IsPeaceProposal(pType)
                ? AiPeaceRejectionCooldownYears
                : AiRejectionCooldownYears;
        }

        public static bool IsAiRejectionCooldownActive(int currentYear,
            int rejectionYear, DiplomacyProposalType pType)
        {
            return rejectionYear >= 0 && currentYear >= rejectionYear &&
                   currentYear - rejectionYear <
                   AiRejectionCooldownYearsFor(pType);
        }

        public static bool IsCooperative(DiplomacyProposalType pType)
        {
            return pType == DiplomacyProposalType.Alliance ||
                   pType == DiplomacyProposalType.NonAggression ||
                   pType == DiplomacyProposalType.RoyalMarriage ||
                   pType == DiplomacyProposalType.HouseholdOffering ||
                   pType == DiplomacyProposalType.Coalition ||
                   pType == DiplomacyProposalType.Vassalize ||
                   pType == DiplomacyProposalType.Tributary;
        }

        public static bool IsPeaceProposal(DiplomacyProposalType pType)
        {
            return pType == DiplomacyProposalType.Peace ||
                   pType == DiplomacyProposalType.Surrender ||
                   pType == DiplomacyProposalType.EnforceDemands;
        }

        public static bool IsExpired(int pCurrentYear, int pExpiryYear)
        {
            return pExpiryYear >= 0 && pCurrentYear > pExpiryYear;
        }

        public static bool BlocksWarWithActivePact(bool activePact,
            bool systemWar, bool independenceWar)
        {
            return activePact && !systemWar && !independenceWar;
        }

        public static bool IsUnilateral(DiplomacyProposalType pType)
        {
            return pType == DiplomacyProposalType.BreakNonAggression ||
                   pType == DiplomacyProposalType.EndAlliance;
        }

        public static bool ShouldBreakNonAggression(int opinion,
            float requesterPower, float responderPower)
        {
            return opinion <= -50 && Math.Max(0f, requesterPower) >=
                   Math.Max(1f, responderPower) * 1.2f;
        }

        private static int PowerSubmissionScore(float pRatio)
        {
            if (pRatio >= 3f) return 45;
            if (pRatio >= 2f) return 35;
            if (pRatio >= 1.5f) return 20;
            if (pRatio <= .8f) return -20;
            return 0;
        }

        private static int PowerTributaryScore(float pRatio)
        {
            if (pRatio >= 2.5f) return 35;
            if (pRatio >= 1.7f) return 25;
            if (pRatio >= 1.3f) return 12;
            if (pRatio <= .8f) return -15;
            return 0;
        }

        private static int PowerEnforcementScore(float pRatio)
        {
            if (pRatio >= 3f) return 35;
            if (pRatio >= 2f) return 28;
            if (pRatio >= 1.5f) return 20;
            if (pRatio <= .8f) return -25;
            return 0;
        }

        private static int PowerSurrenderScore(float pRequesterToResponderRatio)
        {
            if (pRequesterToResponderRatio <= .35f) return 25;
            if (pRequesterToResponderRatio <= .5f) return 20;
            if (pRequesterToResponderRatio <= .7f) return 12;
            if (pRequesterToResponderRatio >= 1.5f) return -25;
            if (pRequesterToResponderRatio >= 1.2f) return -12;
            return 0;
        }

        private static int PeaceCourtScore(float pPeace,
            float pAggression)
        {
            float peace = Math.Max(0f, Math.Min(1f, pPeace));
            float aggression = Math.Max(0f, Math.Min(1f, pAggression));
            return Clamp((int)Math.Round((peace - aggression) * 20f,
                MidpointRounding.AwayFromZero), -20, 20);
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }
    }
}
