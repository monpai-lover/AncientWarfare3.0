using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.ui.windows
{
    public sealed class WarPeaceNegotiationRect
    {
        public WarPeaceNegotiationRect(float x, float y, float width,
            float height)
        {
            X = x;
            Y = y;
            Width = Math.Max(0f, width);
            Height = Math.Max(0f, height);
        }

        public float X { get; private set; }
        public float Y { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        public float Right { get { return X + Width; } }
        public float Bottom { get { return Y + Height; } }
    }

    public sealed class WarPeaceNegotiationLayout
    {
        public WarPeaceNegotiationRect Header { get; internal set; }
        public WarPeaceNegotiationRect Scope { get; internal set; }
        public WarPeaceNegotiationRect Demands { get; internal set; }
        public WarPeaceNegotiationRect Summary { get; internal set; }
        public WarPeaceNegotiationRect Concessions { get; internal set; }
        public WarPeaceNegotiationRect Status { get; internal set; }
        public WarPeaceNegotiationRect BackButton { get; internal set; }
        public WarPeaceNegotiationRect SubmitButton { get; internal set; }

        // Kept as an alias for callers compiled against the first UI slice.
        public WarPeaceNegotiationRect Terms { get { return Demands; } }
    }

    public static class WarPeaceNegotiationLayoutRules
    {
        private const float HeaderHeight = 86f;
        private const float ScopeHeight = 34f;
        private const float ColumnGap = 8f;
        private const float MainTop = HeaderHeight + ScopeHeight + 8f;
        private const float FooterHeight = 34f;
        private const float CommandWidth = 86f;
        private const float CommandGap = 6f;

        public static WarPeaceNegotiationLayout Calculate(float contentWidth,
            float contentHeight)
        {
            float width = Math.Max(1f, contentWidth);
            float height = Math.Max(1f, contentHeight);
            float summaryWidth = Math.Max(150f,
                Math.Min(220f, width * .30f));
            float bilateralWidth = Math.Max(0f,
                (width - summaryWidth - ColumnGap * 2f) * .5f);
            float mainHeight = Math.Max(0f,
                height - MainTop - FooterHeight - 4f);
            float submitX = Math.Max(0f, width - CommandWidth);
            float backX = Math.Max(0f,
                submitX - CommandGap - CommandWidth);
            float statusWidth = Math.Max(0f, backX - CommandGap);
            float footerY = Math.Max(0f, height - FooterHeight);

            return new WarPeaceNegotiationLayout
            {
                Header = new WarPeaceNegotiationRect(0f, 0f, width,
                    HeaderHeight),
                Scope = new WarPeaceNegotiationRect(0f, HeaderHeight + 4f,
                    width, ScopeHeight),
                Demands = new WarPeaceNegotiationRect(0f, MainTop,
                    bilateralWidth, mainHeight),
                Summary = new WarPeaceNegotiationRect(
                    bilateralWidth + ColumnGap, MainTop, summaryWidth,
                    mainHeight),
                Concessions = new WarPeaceNegotiationRect(
                    bilateralWidth + ColumnGap + summaryWidth + ColumnGap,
                    MainTop, bilateralWidth, mainHeight),
                Status = new WarPeaceNegotiationRect(0f, footerY,
                    statusWidth, FooterHeight),
                BackButton = new WarPeaceNegotiationRect(backX, footerY,
                    CommandWidth, FooterHeight),
                SubmitButton = new WarPeaceNegotiationRect(submitX, footerY,
                    CommandWidth, FooterHeight)
            };
        }
    }

    public sealed class WarPeaceNegotiationSummaryLayout
    {
        public WarPeaceNegotiationRect Title { get; internal set; }
        public WarPeaceNegotiationRect Capacity { get; internal set; }
        public WarPeaceNegotiationRect Spent { get; internal set; }
        public WarPeaceNegotiationRect Remaining { get; internal set; }
        public WarPeaceNegotiationRect NetDemand { get; internal set; }
        public WarPeaceNegotiationRect Exhaustion { get; internal set; }
        public WarPeaceNegotiationRect Acceptance { get; internal set; }
        public WarPeaceNegotiationRect Margin { get; internal set; }
        public WarPeaceNegotiationRect Factors { get; internal set; }
    }

    public static class WarPeaceNegotiationSummaryLayoutRules
    {
        public static WarPeaceNegotiationSummaryLayout Calculate(float width,
            float height)
        {
            float innerWidth = Math.Max(0f, width - 16f);
            if (height < 152f)
            {
                return new WarPeaceNegotiationSummaryLayout
                {
                    Title = new WarPeaceNegotiationRect(8f, 1f, innerWidth,
                        12f),
                    Capacity = new WarPeaceNegotiationRect(8f, 14f,
                        innerWidth, 9f),
                    Spent = new WarPeaceNegotiationRect(8f, 24f, innerWidth,
                        9f),
                    Remaining = new WarPeaceNegotiationRect(8f, 34f,
                        innerWidth, 9f),
                    NetDemand = new WarPeaceNegotiationRect(8f, 44f,
                        innerWidth, 9f),
                    Exhaustion = new WarPeaceNegotiationRect(8f, 54f,
                        innerWidth, 9f),
                    Acceptance = new WarPeaceNegotiationRect(8f, 64f,
                        innerWidth, 9f),
                    Margin = new WarPeaceNegotiationRect(8f, 74f,
                        innerWidth, 9f),
                    Factors = new WarPeaceNegotiationRect(8f, 84f,
                        innerWidth, Math.Max(0f, height - 84f))
                };
            }
            float factorsHeight = Math.Max(0f, height - 120f);
            return new WarPeaceNegotiationSummaryLayout
            {
                Title = new WarPeaceNegotiationRect(8f, 2f, innerWidth,
                    16f),
                Capacity = new WarPeaceNegotiationRect(8f, 19f, innerWidth,
                    12f),
                Spent = new WarPeaceNegotiationRect(8f, 32f, innerWidth,
                    12f),
                Remaining = new WarPeaceNegotiationRect(8f, 45f,
                    innerWidth, 12f),
                NetDemand = new WarPeaceNegotiationRect(8f, 58f,
                    innerWidth, 13f),
                Exhaustion = new WarPeaceNegotiationRect(8f, 72f,
                    innerWidth, 14f),
                Acceptance = new WarPeaceNegotiationRect(8f, 87f,
                    innerWidth, 15f),
                Margin = new WarPeaceNegotiationRect(8f, 103f, innerWidth,
                    13f),
                Factors = new WarPeaceNegotiationRect(8f, 118f,
                    innerWidth, factorsHeight)
            };
        }
    }

    public sealed class WarPeaceScoreBreakdown
    {
        public WarPeaceScoreBreakdown(int occupation, int battle,
            int objective)
            : this(occupation, battle, objective,
                occupation + battle + objective, 0)
        {
        }

        public WarPeaceScoreBreakdown(int occupation, int battle,
            int objective, int authoritativeTotal, int decisive)
        {
            Occupation = occupation;
            Battle = battle;
            Objective = objective;
            Total = WarPeaceTermsRules.ClampSignedWarScore(
                authoritativeTotal);
            Decisive = WarPeaceTermsRules.ClampSignedWarScore(decisive);
        }

        public int Occupation { get; private set; }
        public int Battle { get; private set; }
        public int Objective { get; private set; }
        public int Total { get; private set; }
        public int Decisive { get; private set; }
    }

    public enum WarPeaceOfferSide
    {
        Demand,
        Concession
    }

    public enum WarPeaceTermCategory
    {
        City,
        Resource,
        Treaty
    }

    public static class WarPeaceTermPresentationRules
    {
        public static WarPeaceOfferSide ResolveSide(int recipientValue)
        {
            return recipientValue > 0
                ? WarPeaceOfferSide.Concession
                : WarPeaceOfferSide.Demand;
        }

        public static WarPeaceTermCategory ResolveCategory(
            WarPeaceTermKind kind)
        {
            switch (kind)
            {
                case WarPeaceTermKind.CedeCity:
                    return WarPeaceTermCategory.City;
                case WarPeaceTermKind.GoldPayment:
                case WarPeaceTermKind.MaterialPayment:
                case WarPeaceTermKind.Reparations:
                    return WarPeaceTermCategory.Resource;
                default:
                    return WarPeaceTermCategory.Treaty;
            }
        }
    }

    public enum WarPeaceNegotiationOfferMode
    {
        WhitePeace,
        Surrender,
        EnforceDemands
    }

    public static class WarPeaceNegotiationOfferRules
    {
        public static WarPeaceNegotiationOfferMode ResolveInitialMode(
            int signedWarScore)
        {
            if (signedWarScore > 0)
                return WarPeaceNegotiationOfferMode.EnforceDemands;
            if (signedWarScore < 0)
                return WarPeaceNegotiationOfferMode.Surrender;
            return WarPeaceNegotiationOfferMode.WhitePeace;
        }

        public static string ResolveProposalTypeId(
            int netTermValueForRecipient, int signedWarScore)
        {
            return "peace";
        }
    }

    public enum DiplomacyWarScoreTone
    {
        Positive,
        Negative,
        Neutral
    }

    public static class DiplomacyWarScoreIndicatorRules
    {
        public static string Format(int score)
        {
            int value = WarPeaceTermsRules.ClampSignedWarScore(score);
            return value > 0 ? "+" + value : value.ToString();
        }

        public static DiplomacyWarScoreTone Tone(int score)
        {
            if (score > 0) return DiplomacyWarScoreTone.Positive;
            if (score < 0) return DiplomacyWarScoreTone.Negative;
            return DiplomacyWarScoreTone.Neutral;
        }
    }

    public enum WarPeaceTermDisabledReason
    {
        None,
        InsufficientCapacity,
        PrerequisiteFailed
    }

    public sealed class WarPeaceTermAvailability
    {
        public WarPeaceTermAvailability(bool enabled, int cost,
            WarPeaceTermDisabledReason disabledReason, string detailReason)
        {
            Enabled = enabled;
            Cost = cost;
            DisabledReason = disabledReason;
            DetailReason = detailReason ?? string.Empty;
        }

        public bool Enabled { get; private set; }
        public int Cost { get; private set; }
        public WarPeaceTermDisabledReason DisabledReason { get; private set; }
        public string DetailReason { get; private set; }
    }

    public static class WarPeaceTermAvailabilityRules
    {
        public static WarPeaceTermAvailability Resolve(WarPeaceTermKind kind,
            int remainingCapacity, int requestedCost,
            string prerequisiteFailure)
        {
            int cost = WarPeaceTermsRules.NormalizeTermCost(kind,
                requestedCost);
            if (!string.IsNullOrWhiteSpace(prerequisiteFailure))
                return new WarPeaceTermAvailability(false, cost,
                    WarPeaceTermDisabledReason.PrerequisiteFailed,
                    prerequisiteFailure);
            if (cost > Math.Max(0, remainingCapacity))
                return new WarPeaceTermAvailability(false, cost,
                    WarPeaceTermDisabledReason.InsufficientCapacity,
                    string.Empty);
            return new WarPeaceTermAvailability(true, cost,
                WarPeaceTermDisabledReason.None, string.Empty);
        }
    }

    public sealed class WarPeacePartyPresentation
    {
        public WarPeacePartyPresentation(long kingdomId, string kingdomName,
            long rulerActorId, string rulerName)
            : this(kingdomId, kingdomName, rulerActorId, rulerName, -1)
        {
        }

        public WarPeacePartyPresentation(long kingdomId, string kingdomName,
            long rulerActorId, string rulerName, int cityCount)
        {
            KingdomId = kingdomId;
            KingdomName = kingdomName ?? string.Empty;
            RulerActorId = rulerActorId;
            RulerName = rulerName ?? string.Empty;
            CityCount = Math.Max(-1, cityCount);
        }

        public long KingdomId { get; private set; }
        public string KingdomName { get; private set; }
        public long RulerActorId { get; private set; }
        public string RulerName { get; private set; }
        public int CityCount { get; private set; }
    }

    public sealed class WarPeaceTermPresentation
    {
        public WarPeaceTermPresentation(string id, WarPeaceTermKind kind,
            string titleKey, string titleFallback, string descriptionKey,
            string descriptionFallback, int requestedCost,
            int recipientValue, bool initiallySelected,
            string prerequisiteFailure)
            : this(id, kind, titleKey, titleFallback, descriptionKey,
                descriptionFallback, requestedCost, recipientValue,
                initiallySelected, prerequisiteFailure, string.Empty, -1)
        {
        }

        public WarPeaceTermPresentation(string id, WarPeaceTermKind kind,
            string titleKey, string titleFallback, string descriptionKey,
            string descriptionFallback, int requestedCost,
            int recipientValue, bool initiallySelected,
            string prerequisiteFailure, string detail)
            : this(id, kind, titleKey, titleFallback, descriptionKey,
                descriptionFallback, requestedCost, recipientValue,
                initiallySelected, prerequisiteFailure, detail, -1)
        {
        }

        public WarPeaceTermPresentation(string id, WarPeaceTermKind kind,
            string titleKey, string titleFallback, string descriptionKey,
            string descriptionFallback, int requestedCost,
            int recipientValue, bool initiallySelected,
            string prerequisiteFailure, string detail, long cityId)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A peace term id is required.",
                    "id");
            Id = id;
            Kind = kind;
            TitleKey = titleKey ?? string.Empty;
            TitleFallback = titleFallback ?? string.Empty;
            DescriptionKey = descriptionKey ?? string.Empty;
            DescriptionFallback = descriptionFallback ?? string.Empty;
            RequestedCost = requestedCost;
            RecipientValue = recipientValue;
            InitiallySelected = initiallySelected;
            PrerequisiteFailure = prerequisiteFailure ?? string.Empty;
            Detail = detail ?? string.Empty;
            CityId = cityId;
        }

        public string Id { get; private set; }
        public WarPeaceTermKind Kind { get; private set; }
        public string TitleKey { get; private set; }
        public string TitleFallback { get; private set; }
        public string DescriptionKey { get; private set; }
        public string DescriptionFallback { get; private set; }
        public int RequestedCost { get; private set; }
        public int RecipientValue { get; private set; }
        public bool InitiallySelected { get; private set; }
        public string PrerequisiteFailure { get; private set; }
        public string Detail { get; private set; }
        public long CityId { get; private set; }
    }

    public static class WarPeaceRecipientChoiceRules
    {
        public static bool Conflicts(WarPeaceTermPresentation pLeft,
            WarPeaceTermPresentation pRight)
        {
            return pLeft != null && pRight != null &&
                   pLeft.Kind == WarPeaceTermKind.CedeCity &&
                   pRight.Kind == WarPeaceTermKind.CedeCity &&
                   pLeft.CityId >= 0 && pLeft.CityId == pRight.CityId &&
                   !string.Equals(pLeft.Id, pRight.Id,
                       StringComparison.Ordinal);
        }
    }

    public sealed class WarPeaceAcceptanceContext
    {
        public WarPeaceAcceptanceContext(int baseNetTermValueForRecipient,
            int recipientResolve,
            int recipientWarExhaustion, int recipientMilitaryPressure)
        {
            BaseNetTermValueForRecipient = baseNetTermValueForRecipient;
            RecipientResolve = recipientResolve;
            RecipientWarExhaustion = recipientWarExhaustion;
            RecipientMilitaryPressure = recipientMilitaryPressure;
        }

        public int BaseNetTermValueForRecipient { get; private set; }
        public int RecipientResolve { get; private set; }
        public int RecipientWarExhaustion { get; private set; }
        public int RecipientMilitaryPressure { get; private set; }
    }

    public sealed class WarPeaceNegotiationPresentation
    {
        public WarPeaceNegotiationPresentation(string warName,
            WarPeacePartyPresentation requester,
            WarPeacePartyPresentation responder,
            WarPeaceScoreBreakdown requesterScore,
            WarPeaceScoreBreakdown responderScore,
            IEnumerable<WarPeaceTermPresentation> terms,
            WarPeaceAcceptanceContext acceptance,
            string externalSubmitDisabledReason)
            : this(warName, requester, responder, requesterScore,
                responderScore, terms, acceptance,
                externalSubmitDisabledReason, 0,
                acceptance == null ? 0 :
                    acceptance.RecipientWarExhaustion)
        { }

        public WarPeaceNegotiationPresentation(string warName,
            WarPeacePartyPresentation requester,
            WarPeacePartyPresentation responder,
            WarPeaceScoreBreakdown requesterScore,
            WarPeaceScoreBreakdown responderScore,
            IEnumerable<WarPeaceTermPresentation> terms,
            WarPeaceAcceptanceContext acceptance,
            string externalSubmitDisabledReason,
            int requesterExhaustion, int responderExhaustion)
            : this(warName, requester, responder, requesterScore,
                responderScore, terms, acceptance,
                externalSubmitDisabledReason, requesterExhaustion,
                responderExhaustion, "coalition",
                Array.Empty<string>())
        { }

        public WarPeaceNegotiationPresentation(string warName,
            WarPeacePartyPresentation requester,
            WarPeacePartyPresentation responder,
            WarPeaceScoreBreakdown requesterScore,
            WarPeaceScoreBreakdown responderScore,
            IEnumerable<WarPeaceTermPresentation> terms,
            WarPeaceAcceptanceContext acceptance,
            string externalSubmitDisabledReason,
            int requesterExhaustion, int responderExhaustion,
            string scope,
            IEnumerable<string> exitParticipantNames)
        {
            if (requester == null)
                throw new ArgumentNullException("requester");
            if (responder == null)
                throw new ArgumentNullException("responder");
            if (requesterScore == null)
                throw new ArgumentNullException("requesterScore");
            if (responderScore == null)
                throw new ArgumentNullException("responderScore");
            if (acceptance == null)
                throw new ArgumentNullException("acceptance");
            WarName = warName ?? string.Empty;
            Requester = requester;
            Responder = responder;
            RequesterScore = requesterScore;
            ResponderScore = responderScore;
            Terms = new List<WarPeaceTermPresentation>(terms ??
                new WarPeaceTermPresentation[0]).AsReadOnly();
            Acceptance = acceptance;
            RequesterExhaustion = ClampPercent(requesterExhaustion);
            ResponderExhaustion = ClampPercent(responderExhaustion);
            Scope = string.IsNullOrWhiteSpace(scope)
                ? "coalition"
                : scope;
            var exits = new List<string>();
            foreach (string name in exitParticipantNames ??
                     Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(name)) exits.Add(name);
            ExitParticipantNames = exits.AsReadOnly();
            ExternalSubmitDisabledReason =
                externalSubmitDisabledReason ?? string.Empty;
        }

        public string WarName { get; private set; }
        public WarPeacePartyPresentation Requester { get; private set; }
        public WarPeacePartyPresentation Responder { get; private set; }
        public WarPeaceScoreBreakdown RequesterScore { get; private set; }
        public WarPeaceScoreBreakdown ResponderScore { get; private set; }
        public IReadOnlyList<WarPeaceTermPresentation> Terms {
            get; private set;
        }
        public WarPeaceAcceptanceContext Acceptance { get; private set; }
        public int RequesterExhaustion { get; private set; }
        public int ResponderExhaustion { get; private set; }
        public string Scope { get; private set; }
        public IReadOnlyList<string> ExitParticipantNames {
            get; private set;
        }
        public string ExternalSubmitDisabledReason { get; private set; }

        private static int ClampPercent(int pValue)
        {
            return Math.Max(0, Math.Min(100, pValue));
        }
    }

    public sealed class WarPeaceNegotiationSelectionSummary
    {
        public WarPeaceNegotiationSelectionSummary(int warScore,
            WarPeaceOfferLedger ledger, int netTermValueForRecipient,
            WarPeaceAcceptanceResult acceptance, bool submitEnabled,
            string submitDisabledReason)
        {
            if (ledger == null) throw new ArgumentNullException("ledger");
            WarScore = WarPeaceTermsRules.ClampSignedWarScore(warScore);
            Capacity = WarPeaceOfferLedger.MaximumGross;
            DemandGross = ledger.DemandGross;
            ConcessionGross = ledger.ConcessionGross;
            NetDemand = ledger.NetDemand;
            DemandRemaining = ledger.DemandRemaining;
            ConcessionRemaining = ledger.ConcessionRemaining;
            Spent = DemandGross;
            Remaining = DemandRemaining;
            NetTermValueForRecipient = netTermValueForRecipient;
            Acceptance = acceptance;
            SubmitEnabled = submitEnabled;
            SubmitDisabledReason = submitDisabledReason ?? string.Empty;
        }

        public int WarScore { get; private set; }
        public int Capacity { get; private set; }
        public int DemandGross { get; private set; }
        public int ConcessionGross { get; private set; }
        public int NetDemand { get; private set; }
        public int DemandRemaining { get; private set; }
        public int ConcessionRemaining { get; private set; }
        public int Spent { get; private set; }
        public int Remaining { get; private set; }
        public int NetTermValueForRecipient { get; private set; }
        public WarPeaceAcceptanceResult Acceptance { get; private set; }
        public bool SubmitEnabled { get; private set; }
        public string SubmitDisabledReason { get; private set; }
    }

    public static class WarPeaceNegotiationCombinationRules
    {
        private const int MaximumTerms = 16;

        public static string Validate(
            IReadOnlyList<WarPeaceTermPresentation> terms,
            ISet<string> selectedTermIds)
        {
            return Validate(terms, selectedTermIds, -1, -1);
        }

        public static string Validate(
            IReadOnlyList<WarPeaceTermPresentation> terms,
            ISet<string> selectedTermIds, int requesterCityCount,
            int responderCityCount)
        {
            if (selectedTermIds == null || selectedTermIds.Count == 0)
                return "no_terms_selected";
            if (selectedTermIds.Count > MaximumTerms)
                return "invalid_term_count";
            bool whitePeace = false;
            int subjectTermCount = 0;
            int matched = 0;
            int demandCededCities = 0;
            int concessionCededCities = 0;
            bool demandRequiresSurvival = false;
            bool concessionRequiresSurvival = false;
            int termCount = terms == null ? 0 : terms.Count;
            for (int i = 0; i < termCount; i++)
            {
                WarPeaceTermPresentation term = terms[i];
                if (term == null || !selectedTermIds.Contains(term.Id))
                    continue;
                matched++;
                whitePeace |= term.Kind == WarPeaceTermKind.WhitePeace;
                if (term.Kind == WarPeaceTermKind.ForceVassal ||
                    term.Kind == WarPeaceTermKind.ForceTributary)
                    subjectTermCount++;
                WarPeaceOfferSide side = WarPeaceTermPresentationRules
                    .ResolveSide(term.RecipientValue);
                if (term.Kind == WarPeaceTermKind.CedeCity)
                {
                    if (side == WarPeaceOfferSide.Demand)
                        demandCededCities++;
                    else
                        concessionCededCities++;
                }
                if (WarPeaceTreatySurvivalRules.RequiresSourceSurvival(
                        term.Kind))
                {
                    if (side == WarPeaceOfferSide.Demand)
                        demandRequiresSurvival = true;
                    else
                        concessionRequiresSurvival = true;
                }
            }
            if (matched != selectedTermIds.Count)
                return "invalid_term_selection";
            if (whitePeace && matched > 1)
                return "white_peace_must_stand_alone";
            if (subjectTermCount > 1)
                return "conflicting_subject_terms";
            if (!WarPeaceTreatySurvivalRules.LeavesRequiredSourceAlive(
                    responderCityCount, demandCededCities,
                    demandRequiresSurvival) ||
                !WarPeaceTreatySurvivalRules.LeavesRequiredSourceAlive(
                    requesterCityCount, concessionCededCities,
                    concessionRequiresSurvival))
                return WarPeaceTreatySurvivalRules.FailureReason;
            return string.Empty;
        }
    }

    public static class WarPeaceNegotiationLiveStateRules
    {
        public static bool HasPartyChanged(
            WarPeacePartyPresentation currentRequester,
            WarPeacePartyPresentation currentResponder,
            WarPeacePartyPresentation nextRequester,
            WarPeacePartyPresentation nextResponder)
        {
            return !SameParty(currentRequester, nextRequester) ||
                   !SameParty(currentResponder, nextResponder);
        }

        public static bool HasChanged(
            WarPeaceNegotiationPresentation current,
            WarPeaceScoreBreakdown requesterScore,
            WarPeaceScoreBreakdown responderScore,
            WarPeaceAcceptanceContext acceptance,
            string externalSubmitDisabledReason)
        {
            return HasChanged(current, requesterScore, responderScore,
                current == null ? null : current.Terms, acceptance,
                current == null ? 0 : current.RequesterExhaustion,
                current == null ? 0 : current.ResponderExhaustion,
                externalSubmitDisabledReason);
        }

        public static bool HasChanged(
            WarPeaceNegotiationPresentation current,
            WarPeaceScoreBreakdown requesterScore,
            WarPeaceScoreBreakdown responderScore,
            IReadOnlyList<WarPeaceTermPresentation> terms,
            WarPeaceAcceptanceContext acceptance,
            string externalSubmitDisabledReason)
        {
            return HasChanged(current, requesterScore, responderScore, terms,
                acceptance, current == null ? 0 :
                    current.RequesterExhaustion,
                current == null ? 0 : current.ResponderExhaustion,
                externalSubmitDisabledReason);
        }

        public static bool HasChanged(
            WarPeaceNegotiationPresentation current,
            WarPeaceScoreBreakdown requesterScore,
            WarPeaceScoreBreakdown responderScore,
            IReadOnlyList<WarPeaceTermPresentation> terms,
            WarPeaceAcceptanceContext acceptance,
            int requesterExhaustion, int responderExhaustion,
            string externalSubmitDisabledReason)
        {
            if (current == null || requesterScore == null ||
                responderScore == null || terms == null ||
                acceptance == null) return true;
            return !SameScore(current.RequesterScore, requesterScore) ||
                   !SameScore(current.ResponderScore, responderScore) ||
                   !SameTerms(current.Terms, terms) ||
                   !SameAcceptance(current.Acceptance, acceptance) ||
                   current.RequesterExhaustion !=
                       Math.Max(0, Math.Min(100, requesterExhaustion)) ||
                   current.ResponderExhaustion !=
                       Math.Max(0, Math.Min(100, responderExhaustion)) ||
                   !string.Equals(current.ExternalSubmitDisabledReason,
                       externalSubmitDisabledReason ?? string.Empty,
                       StringComparison.Ordinal);
        }

        private static bool SameScore(WarPeaceScoreBreakdown left,
            WarPeaceScoreBreakdown right)
        {
            return left != null && right != null &&
                   left.Occupation == right.Occupation &&
                   left.Battle == right.Battle &&
                   left.Objective == right.Objective &&
                   left.Total == right.Total &&
                   left.Decisive == right.Decisive;
        }

        private static bool SameParty(WarPeacePartyPresentation left,
            WarPeacePartyPresentation right)
        {
            return left != null && right != null &&
                   left.KingdomId == right.KingdomId &&
                   left.RulerActorId == right.RulerActorId &&
                   left.CityCount == right.CityCount &&
                   string.Equals(left.KingdomName, right.KingdomName,
                       StringComparison.Ordinal) &&
                   string.Equals(left.RulerName, right.RulerName,
                       StringComparison.Ordinal);
        }

        private static bool SameAcceptance(WarPeaceAcceptanceContext left,
            WarPeaceAcceptanceContext right)
        {
            return left != null && right != null &&
                   left.BaseNetTermValueForRecipient ==
                   right.BaseNetTermValueForRecipient &&
                   left.RecipientResolve == right.RecipientResolve &&
                   left.RecipientWarExhaustion ==
                   right.RecipientWarExhaustion &&
                   left.RecipientMilitaryPressure ==
                   right.RecipientMilitaryPressure;
        }

        private static bool SameTerms(
            IReadOnlyList<WarPeaceTermPresentation> left,
            IReadOnlyList<WarPeaceTermPresentation> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                WarPeaceTermPresentation a = left[i];
                WarPeaceTermPresentation b = right[i];
                if (ReferenceEquals(a, b)) continue;
                if (a == null || b == null || a.Kind != b.Kind ||
                    a.RequestedCost != b.RequestedCost ||
                    a.RecipientValue != b.RecipientValue ||
                    !string.Equals(a.Id, b.Id, StringComparison.Ordinal) ||
                    !string.Equals(a.TitleKey, b.TitleKey,
                        StringComparison.Ordinal) ||
                    !string.Equals(a.TitleFallback, b.TitleFallback,
                        StringComparison.Ordinal) ||
                    !string.Equals(a.DescriptionKey, b.DescriptionKey,
                        StringComparison.Ordinal) ||
                    !string.Equals(a.DescriptionFallback,
                        b.DescriptionFallback, StringComparison.Ordinal) ||
                    !string.Equals(a.PrerequisiteFailure,
                        b.PrerequisiteFailure, StringComparison.Ordinal) ||
                    !string.Equals(a.Detail, b.Detail,
                        StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }

    public static class WarPeaceNegotiationSelectionRules
    {
        public static WarPeaceNegotiationSelectionSummary Summarize(
            WarPeaceNegotiationPresentation presentation,
            IEnumerable<string> selectedTermIds)
        {
            if (presentation == null)
                throw new ArgumentNullException("presentation");
            var selected = new HashSet<string>(selectedTermIds ??
                new string[0], StringComparer.Ordinal);
            var ledger = new WarPeaceOfferLedger();
            string disabledReason = presentation
                .ExternalSubmitDisabledReason;
            string combinationReason =
                WarPeaceNegotiationCombinationRules.Validate(
                    presentation.Terms, selected,
                    presentation.Requester.CityCount,
                    presentation.Responder.CityCount);
            if (string.IsNullOrEmpty(disabledReason) &&
                !string.IsNullOrEmpty(combinationReason))
                disabledReason = combinationReason;
            int matched = 0;

            for (int i = 0; i < presentation.Terms.Count; i++)
            {
                WarPeaceTermPresentation term = presentation.Terms[i];
                if (term == null || !selected.Contains(term.Id)) continue;
                matched++;
                bool concession = term.RecipientValue > 0;
                int sideRemaining = concession
                    ? ledger.ConcessionRemaining
                    : ledger.DemandRemaining;
                WarPeaceTermAvailability availability =
                    WarPeaceTermAvailabilityRules.Resolve(term.Kind,
                        sideRemaining, term.RequestedCost,
                        term.PrerequisiteFailure);
                if (!availability.Enabled &&
                    string.IsNullOrEmpty(disabledReason))
                    disabledReason = AvailabilityReason(availability);
                if (availability.Enabled)
                {
                    string ledgerReason;
                    bool added = concession
                        ? ledger.TryAddConcession(availability.Cost,
                            out ledgerReason)
                        : ledger.TryAddDemand(availability.Cost,
                            out ledgerReason);
                    if (!added && string.IsNullOrEmpty(disabledReason))
                        disabledReason = ledgerReason;
                }
            }

            if (matched != selected.Count &&
                     string.IsNullOrEmpty(disabledReason))
                disabledReason = "invalid_term_selection";

            int netValue = presentation.Acceptance
                               .BaseNetTermValueForRecipient -
                           ledger.NetDemand;
            var facts = new WarPeaceAcceptanceFacts(
                presentation.ResponderScore.Total, netValue,
                presentation.Acceptance.RecipientResolve,
                presentation.Acceptance.RecipientWarExhaustion,
                presentation.Acceptance.RecipientMilitaryPressure);
            WarPeaceAcceptanceResult acceptance =
                WarPeaceTermsRules.EvaluateAcceptance(facts);
            return new WarPeaceNegotiationSelectionSummary(
                presentation.RequesterScore.Total, ledger, netValue,
                acceptance, string.IsNullOrEmpty(disabledReason),
                disabledReason);
        }

        private static string AvailabilityReason(
            WarPeaceTermAvailability availability)
        {
            switch (availability.DisabledReason)
            {
                case WarPeaceTermDisabledReason.InsufficientCapacity:
                    return "treaty_side_capacity_exceeded";
                case WarPeaceTermDisabledReason.PrerequisiteFailed:
                    return availability.DetailReason;
                default:
                    return "peace_term_unavailable";
            }
        }
    }
}
