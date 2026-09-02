using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.historyapi;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.performance;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal sealed class DiplomacyProposal
    {
        public long ProposalId;
        public long RequesterKingdomId;
        public string RequesterName = "";
        public long ResponderKingdomId;
        public string ResponderName = "";
        public DiplomacyProposalType Type;
        public DiplomacyProposalStatus Status;
        public long WarId = -1L;
        public bool PlayerInitiated;
        public int CreatedYear;
        public int ExpiryYear;
        public int ResponseYear = -1;
        public int TreatyUntilYear = -1;
        public string ResponseReason = "";
        public double CreatedTime = -1d;
        public double ResponseDueTime = -1d;
        public double ResponseTime = -1d;
        public string RequesterTitle = "";
        public string ResponderTitle = "";
        public string RequestYearPrefix = "";
        public string ResponseYearPrefix = "";
        public DiplomacyLetterStyle RequestStyle = DiplomacyLetterStyle.Peer;
        public DiplomacyLetterTone RequestTone = DiplomacyLetterTone.Neutral;
        public DiplomacyLetterStyle ResponseStyle = DiplomacyLetterStyle.Peer;
        public DiplomacyLetterTone ResponseTone = DiplomacyLetterTone.Neutral;
        public long TargetKingdomId = -1L;
        public long RequesterActorId = -1L;
        public long ResponderActorId = -1L;
        public long TargetCityId = -1L;
        public string DetailId = "";
    }

    internal readonly struct DiplomacyProposalSelection
    {
        public DiplomacyProposalSelection(long pTargetKingdomId,
            long pRequesterActorId, long pResponderActorId,
            long pTargetCityId, string pDetailId)
        {
            TargetKingdomId = pTargetKingdomId;
            RequesterActorId = pRequesterActorId;
            ResponderActorId = pResponderActorId;
            TargetCityId = pTargetCityId;
            DetailId = pDetailId ?? "";
        }

        public long TargetKingdomId { get; }
        public long RequesterActorId { get; }
        public long ResponderActorId { get; }
        public long TargetCityId { get; }
        public string DetailId { get; }

        public static DiplomacyProposalSelection Empty =>
            new DiplomacyProposalSelection(-1L, -1L, -1L, -1L, "");
    }

    internal sealed class DiplomacyActionAssessment
    {
        public bool Allowed;
        public string UnavailableReason = "";
        public DiplomacyProposalAssessment Acceptance;
    }

    internal readonly struct AsyncDiplomacyCommitCandidate
    {
        public AsyncDiplomacyCommitCandidate(long pResponderKingdomId,
            DiplomacyProposalType pType,
            AsyncDiplomacyProposalKind pKind,
            long pWarId,
            DiplomacyProposalSelection pSelection)
        {
            ResponderKingdomId = pResponderKingdomId;
            Type = pType;
            Kind = pKind;
            WarId = pWarId;
            Selection = pSelection;
        }

        public long ResponderKingdomId { get; }
        public DiplomacyProposalType Type { get; }
        public AsyncDiplomacyProposalKind Kind { get; }
        public long WarId { get; }
        public DiplomacyProposalSelection Selection { get; }
        public AsyncDiplomacySelectionIdentity Identity =>
            new AsyncDiplomacySelectionIdentity(ResponderKingdomId,
                (int)Type, Kind, WarId, Selection.TargetKingdomId,
                Selection.RequesterActorId, Selection.ResponderActorId,
                Selection.TargetCityId, Selection.DetailId);
    }

    internal static class DiplomacyProposalService
    {
        private const double WorldTimePerDay = 1d / 6d;
        private const int MaximumConsortRequestTargetChecks = 24;
        private const string SettlementInitialAttackerCities =
            "aw_settlement_initial_attacker_cities";
        private const string SettlementInitialDefenderCities =
            "aw_settlement_initial_defender_cities";
        private const string SettlementInitialAttackerWarriors =
            "aw_settlement_initial_attacker_warriors";
        private const string SettlementInitialDefenderWarriors =
            "aw_settlement_initial_defender_warriors";
        private const string ProposalSelectColumns =
            "PROPOSAL_ID,REQUESTER_KINGDOM_ID,REQUESTER_NAME," +
            "RESPONDER_KINGDOM_ID,RESPONDER_NAME,PROPOSAL_TYPE,STATUS," +
            "WAR_ID,PLAYER_INITIATED,CREATED_YEAR,EXPIRY_YEAR," +
            "RESPONSE_YEAR,TREATY_UNTIL_YEAR,RESPONSE_REASON,CREATED_TIME," +
            "RESPONSE_DUE_TIME,RESPONSE_TIME,REQUESTER_TITLE," +
            "RESPONDER_TITLE,REQUEST_YEAR_PREFIX,RESPONSE_YEAR_PREFIX," +
            "TARGET_KINGDOM_ID,REQUESTER_ACTOR_ID,RESPONDER_ACTOR_ID," +
            "TARGET_CITY_ID,DETAIL_ID,REQUEST_STYLE,REQUEST_TONE," +
            "RESPONSE_STYLE,RESPONSE_TONE";
        private static double _nextResponsePollTime = -1d;
        private static double _nextProcessingPollTime = -1d;
        private static readonly Random Rng = new Random();
        private static readonly DiplomacyProposalRuntimeState<
            BoundedRoundRobinCursor<War>> ProposalRuntime = new();
        private static readonly Dictionary<long,
            BoundedRoundRobinCursor<Kingdom>>
                ConsortRequestTargetCursors = new();
        private static readonly Dictionary<long,
            (int Year, IReadOnlyList<Kingdom> Candidates)>
                ConsortRequestTargetBatches = new();
        private static long _nextAsyncAdmissionLeaseId;
        public static event Action<long, long> PairChanged;
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static bool TryCreate(Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, bool pPlayerInitiated,
            long pWarId, out DiplomacyProposal pProposal,
            out string pReason)
        {
            DiplomacyProposalSelection selection =
                DiplomacyProposalSelection.Empty;
            if (pType == DiplomacyProposalType.Coalition)
            {
                pProposal = null;
                pReason = "coalition_target_required";
                return false;
            }
            if (pType == DiplomacyProposalType.HouseholdOffering)
            {
                pProposal = null;
                pReason = "household_selection_required";
                return false;
            }
            if (pType == DiplomacyProposalType.RoyalMarriage)
            {
                DiplomaticMarriagePreview preview =
                    DiplomaticMarriageService.Prepare(
                        pRequester, pResponder);
                if (!preview.Available)
                {
                    pProposal = null;
                    pReason = preview.Reason;
                    return false;
                }
                selection = new DiplomacyProposalSelection(-1L,
                    preview.RequesterActorId,
                    preview.ResponderActorId, -1L,
                    preview.DirectRoyalMarriage ? "direct" : "collateral");
            }
            return TryCreateSelected(pRequester, pResponder, pType,
                pPlayerInitiated, pWarId, selection,
                out pProposal, out pReason);
        }

        internal static bool TryCreateAiProtectionProposal(
            Kingdom pRequester, Kingdom pProtector, Kingdom pThreat)
        {
            if (pRequester?.data == null || pProtector?.data == null ||
                pThreat?.data == null) return false;
            War defensiveWar = FindWarBetween(pRequester, pThreat, -1L);
            long warId = defensiveWar?.data != null &&
                         !defensiveWar.hasEnded() &&
                         defensiveWar.isDefender(pRequester)
                ? defensiveWar.data.id
                : -1L;
            var selection = new DiplomacyProposalSelection(pThreat.id,
                -1L, -1L, -1L,
                DiplomacyProposalOpportunityRules.VassalizeSeekDetail);
            return TryCreateSelected(pRequester, pProtector,
                DiplomacyProposalType.Vassalize,
                pPlayerInitiated: false, warId, selection, out _, out _);
        }

        internal static bool TryCreateWithSelection(Kingdom pRequester,
            Kingdom pResponder, DiplomacyProposalType pType,
            bool pPlayerInitiated, long pWarId,
            DiplomacyProposalSelection pSelection,
            out DiplomacyProposal pProposal, out string pReason)
        {
            return TryCreateSelected(pRequester, pResponder, pType,
                pPlayerInitiated, pWarId, pSelection,
                out pProposal, out pReason);
        }

        private static bool TryCreateSelected(Kingdom pRequester,
            Kingdom pResponder, DiplomacyProposalType pType,
            bool pPlayerInitiated, long pWarId,
            DiplomacyProposalSelection pSelection,
            out DiplomacyProposal pProposal, out string pReason)
        {
            pProposal = null;
            pReason = "invalid";
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester.isRekt() ||
                pResponder.isRekt() || pRequester == pResponder) return false;
            if (!MilitaryGovernorateWarRules.CanUseStateProposal(
                    VassalService.GetSubjectKind(pRequester),
                    VassalService.GetSubjectKind(pResponder)))
            {
                pReason = "military_governorate_no_diplomacy";
                return false;
            }
            if (!pPlayerInitiated &&
                !DiplomacyAiRules.AllowsAiInitiation(pType))
            {
                pReason = pType == DiplomacyProposalType.Alliance
                    ? "ai_alliance_actions_disabled"
                    : "ai_vassal_actions_disabled";
                return false;
            }
            if (!DiplomacyProposalRules.IsUnilateral(pType) &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                pReason = "pending_exists";
                return false;
            }
            if (pType == DiplomacyProposalType.Coalition)
            {
                DiplomaticCoalitionPreview preview =
                    DiplomaticCoalitionService.Prepare(pRequester,
                        pResponder, FindKingdom(pSelection.TargetKingdomId));
                if (!preview.Available)
                {
                    pReason = preview.Reason;
                    return false;
                }
            }
            else if (pType == DiplomacyProposalType.RoyalMarriage)
            {
                DiplomaticMarriagePreview preview =
                    DiplomaticMarriageService.PrepareSelection(pRequester,
                        pResponder, pSelection.RequesterActorId,
                        pSelection.ResponderActorId);
                if (!preview.Available)
                {
                    pReason = preview.Reason;
                    return false;
                }
                pSelection = new DiplomacyProposalSelection(-1L,
                    preview.RequesterActorId, preview.ResponderActorId, -1L,
                    preview.DirectRoyalMarriage ? "direct" : "collateral");
            }
            else if (pType == DiplomacyProposalType.HouseholdOffering)
            {
                if (RulerHouseholdRules.IsConsortRequestDetail(
                        pSelection.DetailId))
                {
                    int opinion = DiplomacyOpinionService.Read(pResponder,
                        pRequester);
                    RulerHouseholdConsortRequestPreview requestPreview =
                        RulerHouseholdService.PrepareConsortRequest(
                            pRequester, pResponder, opinion,
                            pEquivalentPending: false,
                            pRejectionCooldown: false);
                    if (!requestPreview.Available ||
                        requestPreview.RulerActorId !=
                        pSelection.ResponderActorId)
                    {
                        pReason = requestPreview.Available
                            ? "household_ruler_stale"
                            : requestPreview.Reason;
                        return false;
                    }
                    pSelection = new DiplomacyProposalSelection(-1L, -1L,
                        requestPreview.RulerActorId, -1L,
                        RulerHouseholdRules.ConsortRequestDetailId);
                }
                else
                {
                    if (!RulerHouseholdRules.TryParseKind(pSelection.DetailId,
                            out RulerHouseholdKind kind))
                    {
                        pReason = "invalid_household_kind";
                        return false;
                    }
                    RulerHouseholdOfferPreview preview =
                        RulerHouseholdService.PrepareOffer(pRequester,
                            pResponder, pSelection.RequesterActorId, kind);
                    if (!preview.Available ||
                        preview.RulerActorId != pSelection.ResponderActorId)
                    {
                        pReason = preview.Available
                            ? "household_ruler_stale"
                            : preview.Reason;
                        return false;
                    }
                    pSelection = new DiplomacyProposalSelection(-1L,
                        preview.CandidateActorId, preview.RulerActorId, -1L,
                        RulerHouseholdRules.DetailId(kind));
                }
            }
            DiplomacyActionAssessment directionalVassalization = null;
            if (pType == DiplomacyProposalType.Vassalize)
            {
                pSelection = new DiplomacyProposalSelection(
                    pSelection.TargetKingdomId,
                    pSelection.RequesterActorId,
                    pSelection.ResponderActorId,
                    pSelection.TargetCityId,
                    NormalizeVassalizationDetail(pSelection.DetailId));
                directionalVassalization =
                    AssessVassalizationWithSelection(pRequester,
                        pResponder, pWarId, pSelection,
                        pIgnorePending: true);
                if (!directionalVassalization.Allowed)
                {
                    pReason = directionalVassalization.UnavailableReason;
                    return false;
                }
            }
            ProposalContext context = ReadContext(pRequester, pResponder,
                pType, pWarId);
            string unavailable = directionalVassalization == null
                ? DiplomacyProposalRules.UnavailableReason(pType,
                    context.Availability)
                : "";
            if (!string.IsNullOrEmpty(unavailable))
            {
                pReason = unavailable;
                return false;
            }
            if (pType == DiplomacyProposalType.BreakNonAggression)
                return TryBreakNonAggression(pRequester, pResponder,
                    pPlayerInitiated, out pReason);

            bool preparedSettlementHere = false;
            if (DiplomacyProposalRules.IsPeaceProposal(pType))
            {
                if (string.IsNullOrWhiteSpace(pSelection.DetailId))
                {
                    if (!TryPrepareDefaultPeaceSettlement(pRequester,
                            pResponder, pType, context, pPlayerInitiated,
                            out string detailId, out pReason)) return false;
                    pSelection = new DiplomacyProposalSelection(-1L, -1L,
                        -1L, -1L, detailId);
                    preparedSettlementHere = true;
                }
                else
                {
                    WarPeaceValidationResult validation =
                        WarPeaceSettlementService.Instance.Validate(
                            pSelection.DetailId);
                    if (!validation.Success)
                    {
                        pReason = validation.Reason;
                        return false;
                    }
                }
            }

            if (!pPlayerInitiated)
            {
                bool receiverExpectedAccepted =
                    DiplomacyProposalRules.IsUnilateral(pType);
                if (!receiverExpectedAccepted &&
                    DiplomacyProposalRules.IsPeaceProposal(pType))
                {
                    WarPeaceAcceptanceResult settlementAcceptance =
                        WarPeaceSettlementService.Instance.EvaluateAi(
                            pSelection.DetailId,
                            SettlementResolve(pResponder),
                            pType == DiplomacyProposalType.Surrender);
                    receiverExpectedAccepted = settlementAcceptance.Accept;
                }
                else if (!receiverExpectedAccepted)
                {
                    DiplomacyProposalAssessment acceptance =
                        directionalVassalization?.Acceptance ??
                        DiplomacyProposalRules.Assess(pType,
                            BuildScoreFacts(pRequester, pResponder,
                                pSelection.DetailId == "direct",
                                pType == DiplomacyProposalType
                                    .HouseholdOffering &&
                                pSelection.DetailId == "principal_wife",
                                context.WarId, context,
                                pProposedConsortRequest:
                                    RulerHouseholdRules
                                        .IsConsortRequestDetail(
                                            pSelection.DetailId)));
                    receiverExpectedAccepted =
                        acceptance.ExpectedAccepted;
                }
                bool rejectionCooldown = HasRecentAiRejectionForSelection(
                    pRequester.id, pResponder.id, pType,
                    pSelection.DetailId, SafeYear());
                if (!DiplomacyProposalRules.CanSendAiProposal(
                        playerInitiated: false, allowed: true,
                        receiverExpectedAccepted: receiverExpectedAccepted,
                        rejectionCooldownActive: rejectionCooldown))
                {
                    pReason = rejectionCooldown
                        ? "ai_rejection_cooldown"
                        : "predicted_rejection";
                    if (DiplomacyProposalRules.IsPeaceProposal(pType))
                        WarPeaceSettlementService.Instance.Cancel(
                            pSelection.DetailId, pReason);
                    return false;
                }
            }

            int year = SafeYear();
            double createdTime = LineageService.CurTime();
            int responseDelayDays = DiplomacyProposalRules.ResponseDelayDays(
                CapitalDistance(pRequester, pResponder));
            double responseDueTime = createdTime +
                DiplomacyProposalRules.WorldTimeForDays(responseDelayDays);
            string requesterTitle = DiplomaticSenderTitle(pRequester);
            string requestYearPrefix = HistoryWriter.BuildYearPrefix(
                createdTime, pRequester);
            DiplomacyLetterStyle requestStyle = ResolveLetterStyle(
                pRequester, pResponder);
            DiplomacyLetterTone requestTone = ResolveLetterTone(
                pRequester, pResponder);
            long proposalId = TableIdAllocator.Next(DB,
                DiplomacyProposalTableItem.GetTableName(), "PROPOSAL_ID");
            pProposal = new DiplomacyProposal
            {
                ProposalId = proposalId,
                RequesterKingdomId = pRequester.id,
                RequesterName = pRequester.name ?? "",
                ResponderKingdomId = pResponder.id,
                ResponderName = pResponder.name ?? "",
                Type = pType,
                Status = DiplomacyProposalStatus.Pending,
                WarId = context.WarId,
                PlayerInitiated = pPlayerInitiated,
                CreatedYear = year,
                ExpiryYear = year + DiplomacyProposalRules.ExpiryYears(pType),
                CreatedTime = createdTime,
                ResponseDueTime = responseDueTime,
                RequesterTitle = requesterTitle,
                RequestYearPrefix = requestYearPrefix,
                RequestStyle = requestStyle,
                RequestTone = requestTone,
                TargetKingdomId = pSelection.TargetKingdomId,
                RequesterActorId = pSelection.RequesterActorId,
                ResponderActorId = pSelection.ResponderActorId,
                TargetCityId = pSelection.TargetCityId,
                DetailId = pSelection.DetailId
            };
            try
            {
                DB.Insert(DiplomacyProposalTableItem.GetTableName(),
                    ColumnVal.Create("PROPOSAL_ID", proposalId),
                    ColumnVal.Create("REQUESTER_KINGDOM_ID", pRequester.id),
                    ColumnVal.Create("REQUESTER_NAME", pRequester.name ?? ""),
                    ColumnVal.Create("RESPONDER_KINGDOM_ID", pResponder.id),
                    ColumnVal.Create("RESPONDER_NAME", pResponder.name ?? ""),
                    ColumnVal.Create("PROPOSAL_TYPE", TypeId(pType)),
                    ColumnVal.Create("STATUS", "pending"),
                    ColumnVal.Create("WAR_ID", context.WarId),
                    ColumnVal.Create("PLAYER_INITIATED",
                        pPlayerInitiated ? 1 : 0),
                    ColumnVal.Create("CREATED_YEAR", year),
                    ColumnVal.Create("EXPIRY_YEAR", pProposal.ExpiryYear),
                    ColumnVal.Create("RESPONSE_YEAR", -1),
                    ColumnVal.Create("TREATY_UNTIL_YEAR", -1),
                    ColumnVal.Create("CREATED_TIME", createdTime),
                    ColumnVal.Create("RESPONSE_DUE_TIME", responseDueTime),
                    ColumnVal.Create("RESPONSE_TIME", -1.0),
                    ColumnVal.Create("RESPONSE_REASON", ""),
                    ColumnVal.Create("REQUESTER_TITLE", requesterTitle),
                    ColumnVal.Create("RESPONDER_TITLE", ""),
                    ColumnVal.Create("REQUEST_YEAR_PREFIX", requestYearPrefix),
                    ColumnVal.Create("RESPONSE_YEAR_PREFIX", ""),
                    ColumnVal.Create("REQUEST_STYLE",
                        DiplomacyConversationRules.LetterStyleId(requestStyle)),
                    ColumnVal.Create("REQUEST_TONE",
                        DiplomacyConversationRules.LetterToneId(requestTone)),
                    ColumnVal.Create("RESPONSE_STYLE", "peer"),
                    ColumnVal.Create("RESPONSE_TONE", "neutral"),
                    ColumnVal.Create("TARGET_KINGDOM_ID",
                        pSelection.TargetKingdomId),
                    ColumnVal.Create("REQUESTER_ACTOR_ID",
                        pSelection.RequesterActorId),
                    ColumnVal.Create("RESPONDER_ACTOR_ID",
                        pSelection.ResponderActorId),
                    ColumnVal.Create("TARGET_CITY_ID",
                        pSelection.TargetCityId),
                    ColumnVal.Create("DETAIL_ID", pSelection.DetailId));
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy proposal create failed: " +
                                    exception.Message);
                pProposal = null;
                pReason = "write_failed";
                if (preparedSettlementHere)
                    WarPeaceSettlementService.Instance.Cancel(
                        pSelection.DetailId, pReason);
                return false;
            }
            AW3HistoryEventPublisher.PublishDiplomacy(proposalId,
                "DiplomacyProposal", "proposal:" + TypeId(pType),
                pRequester.id, pResponder.id, createdTime, year,
                requestYearPrefix, "pending", pSelection.DetailId);
            DiplomacyConversationService.RecordProposal(pRequester,
                pResponder, proposalId);
            NotifyPair(pRequester.id, pResponder.id);
            pReason = "";
            return true;
        }

        private static bool TryPrepareDefaultPeaceSettlement(
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, ProposalContext pContext,
            bool pPlayerInitiated, out string pDetailId,
            out string pReason)
        {
            War war = FindWarBetween(pRequester, pResponder,
                pContext?.WarId ?? -1L);
            if (!TryResolvePeaceScope(war, pRequester, pResponder,
                    out WarPeaceSettlementScopeKind scope,
                    out long exitRootKingdomId, out pReason))
            {
                pDetailId = "";
                return false;
            }
            return TryPrepareDefaultPeaceSettlement(pRequester, pResponder,
                pType, war, pPlayerInitiated, scope, exitRootKingdomId,
                out pDetailId, out pReason);
        }

        private static bool TryPrepareDefaultPeaceSettlement(
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, War pWar,
            bool pPlayerInitiated, WarPeaceSettlementScopeKind pScope,
            long pExitRootKingdomId, out string pDetailId,
            out string pReason)
        {
            pDetailId = "";
            pReason = "war_no_longer_active";
            War war = pWar;
            if (war?.data == null || war.hasEnded() ||
                !WarScoreService.TryGetSnapshot(war, pRequester,
                    out WarScoreSnapshot score)) return false;
            WarPeaceDefaultOfferMode mode = pType switch
            {
                DiplomacyProposalType.Surrender =>
                    WarPeaceDefaultOfferMode.Surrender,
                DiplomacyProposalType.EnforceDemands =>
                    WarPeaceDefaultOfferMode.EnforceDemands,
                _ => WarPeaceDefaultOfferMode.WhitePeace
            };
            WarPeaceSettlementDraft draft = pScope ==
                WarPeaceSettlementScopeKind.SeparateParticipant
                ? WarPeaceSettlementService.Instance.
                    BuildDefaultSeparateParticipantDraft(war, pRequester,
                        pResponder, score.Score, mode)
                : WarPeaceSettlementService.Instance.BuildDefaultDraft(war,
                    pRequester, pResponder, score.Score, mode);
            draft.PlayerInitiated = pPlayerInitiated;
            draft.Scope = pScope;
            draft.ExitRootKingdomId = pExitRootKingdomId;
            WarPeacePrepareResult prepared =
                WarPeaceSettlementService.Instance.Prepare(draft);
            if (!prepared.Success || prepared.Proposal == null)
            {
                pReason = prepared.Reason;
                return false;
            }
            pDetailId = prepared.Proposal.DetailId;
            pReason = "";
            return true;
        }

        public static bool CanPropose(Kingdom pRequester,
            Kingdom pResponder, DiplomacyProposalType pType,
            long pWarId, out string pReason)
        {
            DiplomacyActionAssessment assessment = Assess(pRequester,
                pResponder, pType, pWarId);
            pReason = assessment.UnavailableReason;
            return assessment.Allowed;
        }

        public static DiplomacyActionAssessment Assess(Kingdom pRequester,
            Kingdom pResponder, DiplomacyProposalType pType, long pWarId,
            bool pIgnorePending = false)
        {
            var result = new DiplomacyActionAssessment
            {
                Allowed = false,
                UnavailableReason = "invalid"
            };
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt()) return result;
            if (!pIgnorePending &&
                !DiplomacyProposalRules.IsUnilateral(pType) &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                result.UnavailableReason = "pending_exists";
                return result;
            }
            if (pType == DiplomacyProposalType.RoyalMarriage)
                return AssessRoyalMarriageWithPreview(pRequester,
                    pResponder, out _, pIgnorePending: pIgnorePending);
            if (pType == DiplomacyProposalType.HouseholdOffering)
            {
                result.UnavailableReason = "household_selection_required";
                return result;
            }
            if (pType == DiplomacyProposalType.Coalition)
            {
                result.UnavailableReason = "coalition_target_required";
                return result;
            }
            ProposalContext context = ReadContext(pRequester, pResponder,
                pType, pWarId);
            result.UnavailableReason =
                DiplomacyProposalRules.UnavailableReason(pType,
                    context.Availability);
            result.Allowed = string.IsNullOrEmpty(result.UnavailableReason);
            if (result.Allowed)
                result.Acceptance = DiplomacyProposalRules.Assess(pType,
                    BuildScoreFacts(pRequester, pResponder,
                        pWarId: context.WarId, pContext: context));
            return result;
        }

        private static DiplomacyActionAssessment AssessReadOnly(
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, long pWarId,
            MandateReport pMandateReport, bool pIgnorePending = false)
        {
            var result = new DiplomacyActionAssessment
            {
                Allowed = false,
                UnavailableReason = "invalid"
            };
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt()) return result;
            if (!pIgnorePending &&
                !DiplomacyProposalRules.IsUnilateral(pType) &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                result.UnavailableReason = "pending_exists";
                return result;
            }
            if (pType == DiplomacyProposalType.RoyalMarriage)
                return AssessRoyalMarriageWithPreviewReadOnly(pRequester,
                    pResponder, out _, pMandateReport, pIgnorePending);
            if (pType == DiplomacyProposalType.HouseholdOffering)
            {
                result.UnavailableReason = "household_selection_required";
                return result;
            }
            if (pType == DiplomacyProposalType.Coalition)
            {
                result.UnavailableReason = "coalition_target_required";
                return result;
            }
            ProposalContext context = ReadContextReadOnly(pRequester,
                pResponder, pType, pWarId, pMandateReport);
            result.UnavailableReason = DiplomacyProposalRules
                .UnavailableReason(pType, context.Availability);
            result.Allowed = string.IsNullOrEmpty(result.UnavailableReason);
            if (result.Allowed)
                result.Acceptance = DiplomacyProposalRules.Assess(pType,
                    BuildScoreFactsReadOnly(pRequester, pResponder,
                        pMandateReport, pWarId: context.WarId,
                        pContext: context));
            return result;
        }

        private static DiplomacyActionAssessment
            AssessVassalizationWithSelection(Kingdom pRequester,
                Kingdom pResponder, long pWarId,
                DiplomacyProposalSelection pSelection,
                bool pIgnorePending = false)
        {
            return AssessVassalizationWithSelectionCore(pRequester,
                pResponder, pWarId, pSelection, null, pReadOnly: false,
                pIgnorePending);
        }

        private static DiplomacyActionAssessment
            AssessVassalizationWithSelectionReadOnly(Kingdom pRequester,
                Kingdom pResponder, long pWarId,
                DiplomacyProposalSelection pSelection,
                MandateReport pMandateReport,
                bool pIgnorePending = false)
        {
            return AssessVassalizationWithSelectionCore(pRequester,
                pResponder, pWarId, pSelection, pMandateReport,
                pReadOnly: true, pIgnorePending);
        }

        private static DiplomacyActionAssessment
            AssessVassalizationWithSelectionCore(Kingdom pRequester,
                Kingdom pResponder, long pWarId,
                DiplomacyProposalSelection pSelection,
                MandateReport pMandateReport, bool pReadOnly,
                bool pIgnorePending)
        {
            var result = new DiplomacyActionAssessment
            {
                Allowed = false,
                UnavailableReason = "invalid"
            };
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt()) return result;
            if (!pIgnorePending &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                result.UnavailableReason = "pending_exists";
                return result;
            }

            string detailId = NormalizeVassalizationDetail(
                pSelection.DetailId);
            if (detailId == DiplomacyProposalOpportunityRules
                    .VassalizeInternalizeDetail)
            {
                bool responderImperial = KingdomTitleService.IsEmperor(
                    pResponder);
                bool responderHasMandate = pReadOnly
                    ? MandateService.IsMandateKingdomReadOnly(pResponder,
                        pMandateReport)
                    : MandateService.IsMandateKingdom(pResponder);
                int tier = DiplomacyProposalOpportunityRules
                    .InternalizationTier(
                        VassalService.GetTributarySuzerain(pRequester) ==
                        pResponder, responderImperial,
                        responderHasMandate);
                if (!VassalService.CanInternalizeTributary(pRequester,
                        pResponder, tier, out result.UnavailableReason))
                    return result;
                result.Allowed = true;
                result.UnavailableReason = "";
                result.Acceptance = DiplomacyProposalRules.Assess(
                    DiplomacyProposalType.Vassalize,
                    pReadOnly
                        ? BuildScoreFactsReadOnly(pResponder, pRequester,
                            pMandateReport)
                        : BuildScoreFacts(pResponder, pRequester));
                return result;
            }

            if (detailId ==
                DiplomacyProposalOpportunityRules.VassalizeSeekDetail)
            {
                result.UnavailableReason = ValidateProtectionRequest(
                    pRequester, pResponder, pWarId,
                    pSelection.TargetKingdomId, out Kingdom threat,
                    out float enemyToProtectorPower);
                result.Allowed = string.IsNullOrEmpty(
                    result.UnavailableReason);
                if (!result.Allowed) return result;
                int opinion = DiplomacyOpinionService.Read(pResponder,
                    pRequester);
                CourtSnapshot court = CourtService.GetSnapshot(pResponder);
                int riskPenalty = DiplomacyProposalOpportunityRules
                    .ProtectionRiskPenalty(enemyToProtectorPower,
                        excellentRelations: opinion >= 70,
                        sharedEnemy: SafeEnemy(pResponder, threat),
                        warCourt: court?.war ?? .5f);
                result.Acceptance = DiplomacyProposalRules
                    .AssessProtectionRequest(opinion,
                        SafeStat(pRequester.king, "diplomacy"),
                        SafeStat(pResponder.king, "diplomacy"),
                        riskPenalty);
                return result;
            }

            if (detailId !=
                DiplomacyProposalOpportunityRules.VassalizeDemandDetail)
            {
                result.UnavailableReason = "invalid_vassalize_direction";
                return result;
            }
            if (FindWarBetween(pRequester, pResponder, -1L) != null)
            {
                result.UnavailableReason = "at_war";
                return result;
            }
            if (SafeAllied(pRequester, pResponder))
            {
                result.UnavailableReason = "already_allied";
                return result;
            }
            if (IsSubject(pRequester))
            {
                result.UnavailableReason = "requester_subject";
                return result;
            }
            if (IsSubject(pResponder))
            {
                result.UnavailableReason = "responder_subject";
                return result;
            }
            if (!VassalService.CanSetVassal(pResponder, pRequester,
                    out string subjectFailure))
            {
                result.UnavailableReason = subjectFailure;
                return result;
            }
            result.Allowed = true;
            result.UnavailableReason = "";
            result.Acceptance = DiplomacyProposalRules.Assess(
                DiplomacyProposalType.Vassalize,
                pReadOnly
                    ? BuildScoreFactsReadOnly(pRequester, pResponder,
                        pMandateReport)
                    : BuildScoreFacts(pRequester, pResponder));
            return result;
        }

        private static string NormalizeVassalizationDetail(
            string pDetailId)
        {
            return string.IsNullOrWhiteSpace(pDetailId)
                ? DiplomacyProposalOpportunityRules.VassalizeDemandDetail
                : pDetailId;
        }

        private static string ValidateProtectionRequest(Kingdom pRequester,
            Kingdom pProtector, long pWarId, long pThreatKingdomId,
            out Kingdom pThreat, out float pEnemyToProtectorPower)
        {
            pThreat = FindKingdom(pThreatKingdomId);
            pEnemyToProtectorPower = float.MaxValue;
            if (FindWarBetween(pRequester, pProtector, -1L) != null)
                return "protector_war_conflict";
            if (SafeAllied(pRequester, pProtector))
                return "already_allied";
            if (IsSubject(pRequester)) return "requester_subject";
            if (IsSubject(pProtector)) return "responder_subject";
            if (!KingdomAdjacency.AreDirectNeighbors(pRequester,
                    pProtector)) return "not_adjacent";
            if (!VassalService.CanSetVassal(pRequester, pProtector,
                    out string subjectFailure)) return subjectFailure;

            float requesterPower = Math.Max(1f,
                VassalService.GetPowerScore(pRequester,
                    pIncludeVassals: false));
            float protectorPower = Math.Max(1f,
                VassalService.GetWarPowerScore(pProtector,
                    pIncludeVassals: true));
            if (protectorPower < requesterPower * 1.9f)
                return "protector_too_weak";
            if (DiplomacyOpinionService.Read(pRequester, pProtector) < -25)
                return "protector_relations_low";

            float enemyPower;
            if (pWarId >= 0L)
            {
                War defensiveWar = FindWar(pWarId);
                if (defensiveWar?.data == null || defensiveWar.hasEnded() ||
                    !defensiveWar.isDefender(pRequester))
                    return "join_war_stale";
                if (defensiveWar.isAttacker(pProtector) ||
                    defensiveWar.isDefender(pProtector) ||
                    HasProtectorEnemySubjectConflict(defensiveWar,
                        pProtector))
                    return "protector_war_conflict";
                Kingdom activeThreat = FindOpponent(defensiveWar,
                    pRequester);
                if (activeThreat?.data == null ||
                    pThreat?.data != null && pThreat != activeThreat)
                    return "join_war_stale";
                pThreat = activeThreat;
                enemyPower = EnemyCoalitionPower(defensiveWar, pRequester);
            }
            else
            {
                if (pThreat?.data == null || pThreat == pRequester ||
                    pThreat == pProtector || pThreat.isRekt())
                    return "protection_threat_stale";
                enemyPower = VassalService.GetWarPowerScore(pThreat,
                    pIncludeVassals: true);
            }
            if (enemyPower < requesterPower * 1.6f)
                return "protection_threat_stale";
            pEnemyToProtectorPower = enemyPower / protectorPower;
            return "";
        }

        private static bool HasProtectorEnemySubjectConflict(War pWar,
            Kingdom pProtector)
        {
            Kingdom protectorRoot = VassalService.GetRootSuzerain(
                pProtector) ?? pProtector;
            try
            {
                foreach (Kingdom attacker in pWar.getAttackers())
                    if ((VassalService.GetRootSuzerain(attacker) ??
                         attacker) == protectorRoot)
                        return true;
            }
            catch { return true; }
            return false;
        }

        private static bool SafeEnemy(Kingdom pFirst, Kingdom pSecond)
        {
            try
            {
                return pFirst?.data != null && pSecond?.data != null &&
                       (pFirst.isEnemy(pSecond) ||
                        pSecond.isEnemy(pFirst));
            }
            catch { return false; }
        }

        internal static DiplomacyActionAssessment
            AssessRoyalMarriageWithPreview(Kingdom pRequester,
                Kingdom pResponder, out DiplomaticMarriagePreview pPreview,
                bool pIgnorePending = false)
        {
            pPreview = new DiplomaticMarriagePreview();
            var result = new DiplomacyActionAssessment
            {
                Allowed = false,
                UnavailableReason = "invalid"
            };
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt()) return result;
            if (!pIgnorePending &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                result.UnavailableReason = "pending_exists";
                return result;
            }
            pPreview = DiplomaticMarriageService.Prepare(pRequester,
                pResponder);
            result.UnavailableReason = pPreview.Reason;
            result.Allowed = pPreview.Available;
            if (result.Allowed)
                result.Acceptance = DiplomacyProposalRules.Assess(
                    DiplomacyProposalType.RoyalMarriage,
                    BuildScoreFacts(pRequester, pResponder,
                        pPreview.DirectRoyalMarriage));
            return result;
        }

        private static DiplomacyActionAssessment AssessRoyalMarriageWithPreviewReadOnly(
            Kingdom pRequester, Kingdom pResponder,
            out DiplomaticMarriagePreview pPreview,
            MandateReport pMandateReport, bool pIgnorePending = false)
        {
            pPreview = new DiplomaticMarriagePreview();
            var result = new DiplomacyActionAssessment
            {
                Allowed = false,
                UnavailableReason = "invalid"
            };
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt()) return result;
            if (!pIgnorePending &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                result.UnavailableReason = "pending_exists";
                return result;
            }
            pPreview = DiplomaticMarriageService.Prepare(pRequester,
                pResponder);
            result.UnavailableReason = pPreview.Reason;
            result.Allowed = pPreview.Available;
            if (result.Allowed)
                result.Acceptance = DiplomacyProposalRules.Assess(
                    DiplomacyProposalType.RoyalMarriage,
                    BuildScoreFactsReadOnly(pRequester, pResponder,
                        pMandateReport, pPreview.DirectRoyalMarriage));
            return result;
        }

        internal static DiplomacyActionAssessment AssessWithSelection(
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, long pWarId,
            DiplomacyProposalSelection pSelection,
            bool pIgnorePending = false)
        {
            if (pType == DiplomacyProposalType.Vassalize)
                return AssessVassalizationWithSelection(pRequester,
                    pResponder, pWarId, pSelection, pIgnorePending);
            if (pType == DiplomacyProposalType.HouseholdOffering)
            {
                var householdResult = new DiplomacyActionAssessment
                {
                    Allowed = false,
                    UnavailableReason = "invalid_household_selection"
                };
                if (!Ready || pRequester?.data == null ||
                    pResponder?.data == null || pRequester == pResponder ||
                    pRequester.isRekt() || pResponder.isRekt())
                    return householdResult;
                if (!pIgnorePending &&
                    HasPendingPair(pRequester.id, pResponder.id))
                {
                    householdResult.UnavailableReason = "pending_exists";
                    return householdResult;
                }
                if (RulerHouseholdRules.IsConsortRequestDetail(
                        pSelection.DetailId))
                {
                    int opinion = DiplomacyOpinionService.Read(pResponder,
                        pRequester);
                    RulerHouseholdConsortRequestPreview requestPreview =
                        RulerHouseholdService.PrepareConsortRequest(
                            pRequester, pResponder, opinion,
                            pEquivalentPending: false,
                            pRejectionCooldown: false);
                    householdResult.UnavailableReason =
                        requestPreview.Available &&
                        requestPreview.RulerActorId !=
                        pSelection.ResponderActorId
                            ? "household_ruler_stale"
                            : requestPreview.Reason;
                    householdResult.Allowed = requestPreview.Available &&
                                              requestPreview.RulerActorId ==
                                              pSelection.ResponderActorId;
                    if (householdResult.Allowed)
                        householdResult.Acceptance =
                            DiplomacyProposalRules.Assess(pType,
                                BuildScoreFacts(pRequester, pResponder,
                                    pProposedConsortRequest: true));
                    return householdResult;
                }
                if (!RulerHouseholdRules.TryParseKind(pSelection.DetailId,
                        out RulerHouseholdKind kind))
                    return householdResult;
                RulerHouseholdOfferPreview householdPreview =
                    RulerHouseholdService.PrepareOffer(pRequester,
                        pResponder, pSelection.RequesterActorId, kind);
                householdResult.UnavailableReason =
                    householdPreview.Available &&
                    householdPreview.RulerActorId !=
                    pSelection.ResponderActorId
                    ? "household_ruler_stale"
                    : householdPreview.Reason;
                householdResult.Allowed = householdPreview.Available &&
                                          householdPreview.RulerActorId ==
                                          pSelection.ResponderActorId;
                if (householdResult.Allowed)
                    householdResult.Acceptance =
                        DiplomacyProposalRules.Assess(pType,
                            BuildScoreFacts(pRequester, pResponder,
                                pProposedPrincipalWife: kind ==
                                    RulerHouseholdKind.PrincipalWife));
                return householdResult;
            }
            if (pType == DiplomacyProposalType.RoyalMarriage)
            {
                var marriageResult = new DiplomacyActionAssessment
                {
                    Allowed = false,
                    UnavailableReason = "invalid"
                };
                if (!Ready || pRequester?.data == null ||
                    pResponder?.data == null || pRequester == pResponder ||
                    pRequester.isRekt() || pResponder.isRekt())
                    return marriageResult;
                if (!pIgnorePending &&
                    HasPendingPair(pRequester.id, pResponder.id))
                {
                    marriageResult.UnavailableReason = "pending_exists";
                    return marriageResult;
                }
                DiplomaticMarriagePreview marriage =
                    DiplomaticMarriageService.PrepareSelection(pRequester,
                        pResponder, pSelection.RequesterActorId,
                        pSelection.ResponderActorId);
                marriageResult.UnavailableReason = marriage.Reason;
                marriageResult.Allowed = marriage.Available;
                if (marriageResult.Allowed)
                    marriageResult.Acceptance = DiplomacyProposalRules.Assess(
                        pType, BuildScoreFacts(pRequester, pResponder,
                            marriage.DirectRoyalMarriage));
                return marriageResult;
            }
            if (pType != DiplomacyProposalType.Coalition)
                return Assess(pRequester, pResponder, pType, pWarId);
            var result = new DiplomacyActionAssessment
            {
                Allowed = false,
                UnavailableReason = "invalid_coalition_target"
            };
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt()) return result;
            if (!pIgnorePending &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                result.UnavailableReason = "pending_exists";
                return result;
            }
            DiplomaticCoalitionPreview preview =
                DiplomaticCoalitionService.Prepare(pRequester, pResponder,
                    FindKingdom(pSelection.TargetKingdomId));
            result.UnavailableReason = preview.Reason;
            result.Allowed = preview.Available;
            if (result.Allowed)
                result.Acceptance = DiplomacyProposalRules.Assess(pType,
                    BuildScoreFacts(pRequester, pResponder));
            return result;
        }

        private static DiplomacyActionAssessment AssessWithSelectionReadOnly(
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, long pWarId,
            DiplomacyProposalSelection pSelection,
            MandateReport pMandateReport, bool pIgnorePending = false)
        {
            if (pType == DiplomacyProposalType.Vassalize)
                return AssessVassalizationWithSelectionReadOnly(pRequester,
                    pResponder, pWarId, pSelection, pMandateReport,
                    pIgnorePending);
            if (pType == DiplomacyProposalType.HouseholdOffering)
            {
                var householdResult = new DiplomacyActionAssessment
                {
                    Allowed = false,
                    UnavailableReason = "invalid_household_selection"
                };
                if (!Ready || pRequester?.data == null ||
                    pResponder?.data == null || pRequester == pResponder ||
                    pRequester.isRekt() || pResponder.isRekt())
                    return householdResult;
                if (!pIgnorePending &&
                    HasPendingPair(pRequester.id, pResponder.id))
                {
                    householdResult.UnavailableReason = "pending_exists";
                    return householdResult;
                }
                if (RulerHouseholdRules.IsConsortRequestDetail(
                        pSelection.DetailId))
                {
                    int opinion = DiplomacyOpinionService.Read(pResponder,
                        pRequester);
                    RulerHouseholdConsortRequestPreview requestPreview =
                        RulerHouseholdService.PrepareConsortRequest(
                            pRequester, pResponder, opinion,
                            pEquivalentPending: false,
                            pRejectionCooldown: false);
                    householdResult.UnavailableReason =
                        requestPreview.Available &&
                        requestPreview.RulerActorId !=
                        pSelection.ResponderActorId
                            ? "household_ruler_stale"
                            : requestPreview.Reason;
                    householdResult.Allowed = requestPreview.Available &&
                                              requestPreview.RulerActorId ==
                                              pSelection.ResponderActorId;
                    if (householdResult.Allowed)
                        householdResult.Acceptance =
                            DiplomacyProposalRules.Assess(pType,
                                BuildScoreFactsReadOnly(pRequester,
                                    pResponder, pMandateReport,
                                    pProposedConsortRequest: true));
                    return householdResult;
                }
                if (!RulerHouseholdRules.TryParseKind(pSelection.DetailId,
                        out RulerHouseholdKind kind))
                    return householdResult;
                RulerHouseholdOfferPreview householdPreview =
                    RulerHouseholdService.PrepareOffer(pRequester,
                        pResponder, pSelection.RequesterActorId, kind);
                householdResult.UnavailableReason =
                    householdPreview.Available &&
                    householdPreview.RulerActorId !=
                    pSelection.ResponderActorId
                    ? "household_ruler_stale"
                    : householdPreview.Reason;
                householdResult.Allowed = householdPreview.Available &&
                                          householdPreview.RulerActorId ==
                                          pSelection.ResponderActorId;
                if (householdResult.Allowed)
                    householdResult.Acceptance =
                        DiplomacyProposalRules.Assess(pType,
                            BuildScoreFactsReadOnly(pRequester, pResponder,
                                pMandateReport,
                                pProposedPrincipalWife: kind ==
                                    RulerHouseholdKind.PrincipalWife));
                return householdResult;
            }
            if (pType == DiplomacyProposalType.RoyalMarriage)
            {
                var marriageResult = new DiplomacyActionAssessment
                {
                    Allowed = false,
                    UnavailableReason = "invalid"
                };
                if (!Ready || pRequester?.data == null ||
                    pResponder?.data == null || pRequester == pResponder ||
                    pRequester.isRekt() || pResponder.isRekt())
                    return marriageResult;
                if (!pIgnorePending &&
                    HasPendingPair(pRequester.id, pResponder.id))
                {
                    marriageResult.UnavailableReason = "pending_exists";
                    return marriageResult;
                }
                DiplomaticMarriagePreview marriage =
                    DiplomaticMarriageService.PrepareSelection(pRequester,
                        pResponder, pSelection.RequesterActorId,
                        pSelection.ResponderActorId);
                marriageResult.UnavailableReason = marriage.Reason;
                marriageResult.Allowed = marriage.Available;
                if (marriageResult.Allowed)
                    marriageResult.Acceptance =
                        DiplomacyProposalRules.Assess(pType,
                            BuildScoreFactsReadOnly(pRequester, pResponder,
                                pMandateReport,
                                marriage.DirectRoyalMarriage));
                return marriageResult;
            }
            if (pType != DiplomacyProposalType.Coalition)
                return AssessReadOnly(pRequester, pResponder, pType, pWarId,
                    pMandateReport, pIgnorePending);
            var result = new DiplomacyActionAssessment
            {
                Allowed = false,
                UnavailableReason = "invalid_coalition_target"
            };
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt()) return result;
            if (!pIgnorePending &&
                HasPendingPair(pRequester.id, pResponder.id))
            {
                result.UnavailableReason = "pending_exists";
                return result;
            }
            DiplomaticCoalitionPreview preview =
                DiplomaticCoalitionService.PrepareReadOnly(pRequester,
                    pResponder, FindKingdom(pSelection.TargetKingdomId),
                    pMandateReport);
            result.UnavailableReason = preview.Reason;
            result.Allowed = preview.Available;
            if (result.Allowed)
                result.Acceptance = DiplomacyProposalRules.Assess(pType,
                    BuildScoreFactsReadOnly(pRequester, pResponder,
                        pMandateReport));
            return result;
        }

        public static IReadOnlyList<DiplomacyProposal> ReadPair(
            long pKingdomA, long pKingdomB, int pLimit = 32)
        {
            var result = new List<DiplomacyProposal>();
            if (!Ready || !DiplomacyConversationRules.TryNormalizePair(
                    pKingdomA, pKingdomB, out DiplomacyKingdomPair pair))
                return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT " + ProposalSelectColumns +
                    " FROM " + DiplomacyProposalTableItem.GetTableName() +
                    " WHERE (REQUESTER_KINGDOM_ID=@a AND " +
                    "RESPONDER_KINGDOM_ID=@b) OR " +
                    "(REQUESTER_KINGDOM_ID=@b AND RESPONDER_KINGDOM_ID=@a) " +
                    "ORDER BY CREATED_TIME DESC,PROPOSAL_ID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@a", pair.FirstKingdomId);
                command.Parameters.AddWithValue("@b", pair.SecondKingdomId);
                command.Parameters.AddWithValue("@limit",
                    Math.Max(1, Math.Min(64, pLimit)));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadProposal(reader));
                result.Reverse();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy proposals read failed: " +
                                    exception.Message);
            }
            return result;
        }

        internal static DiplomacyProposal ReadProposalById(long pProposalId)
        {
            try
            {
                return Find(pProposalId);
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryAttachConsortRequestCandidate(
            long pProposalId, long pCandidateActorId, out string pReason)
        {
            pReason = "invalid_consort_request";
            if (AW3MultiplayerReplicaScope.IsReplicaSession || !Ready ||
                pProposalId < 0L || pCandidateActorId < 0L)
                return false;
            DiplomacyProposal proposal = Find(pProposalId);
            if (proposal == null ||
                proposal.Status != DiplomacyProposalStatus.Pending ||
                proposal.Type != DiplomacyProposalType.HouseholdOffering ||
                !RulerHouseholdRules.IsConsortRequestDetail(
                    proposal.DetailId))
            {
                pReason = "already_responded";
                return false;
            }
            if (proposal.RequesterActorId >= 0L)
            {
                pReason = proposal.RequesterActorId == pCandidateActorId
                    ? ""
                    : "household_candidate_already_selected";
                return proposal.RequesterActorId == pCandidateActorId;
            }
            Kingdom vacancyRealm = FindKingdom(proposal.RequesterKingdomId);
            Kingdom supplierRealm = FindKingdom(proposal.ResponderKingdomId);
            if (vacancyRealm?.king?.data == null ||
                supplierRealm?.data == null || vacancyRealm.king.data.id !=
                proposal.ResponderActorId)
            {
                pReason = "household_ruler_stale";
                return false;
            }
            RulerHouseholdOfferPreview preview =
                RulerHouseholdService.PrepareOffer(supplierRealm,
                    vacancyRealm, pCandidateActorId,
                    RulerHouseholdKind.Consort);
            if (!preview.Available)
            {
                pReason = preview.Reason;
                return false;
            }
            using var command = new SQLiteCommand(DB);
            command.CommandText = "UPDATE " +
                DiplomacyProposalTableItem.GetTableName() +
                " SET REQUESTER_ACTOR_ID=@candidate WHERE " +
                "PROPOSAL_ID=@id AND STATUS='pending' AND " +
                "PROPOSAL_TYPE='household_offering' AND " +
                "DETAIL_ID=@request_detail AND REQUESTER_ACTOR_ID<0";
            command.Parameters.AddWithValue("@candidate",
                preview.CandidateActorId);
            command.Parameters.AddWithValue("@id", pProposalId);
            command.Parameters.AddWithValue("@request_detail",
                RulerHouseholdRules.ConsortRequestDetailId);
            if (command.ExecuteNonQuery() != 1)
            {
                pReason = "household_candidate_already_selected";
                return false;
            }
            NotifyPair(proposal.RequesterKingdomId,
                proposal.ResponderKingdomId);
            pReason = "";
            return true;
        }

        public static bool Respond(long pProposalId, bool pAccept,
            bool pPlayerResponse, out string pReason)
        {
            pReason = "not_found";
            DiplomacyProposal proposal;
            try
            {
                proposal = Find(pProposalId);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy response read failed: " +
                                    "proposal=" + pProposalId +
                                    ", error=" + exception.Message);
                pReason = "write_failed";
                return false;
            }
            if (proposal == null) return false;
            if (proposal.Status != DiplomacyProposalStatus.Pending)
            {
                pReason = "already_responded";
                return false;
            }
            if (DiplomacyProposalRules.IsExpired(SafeYear(),
                    proposal.ExpiryYear))
            {
                bool closed = Close(proposal, DiplomacyProposalStatus.Expired,
                    "expired", -1);
                if (closed && DiplomacyProposalRules.IsPeaceProposal(
                        proposal.Type))
                    WarPeaceSettlementService.Instance.Cancel(
                        proposal.DetailId, "expired");
                pReason = closed ? "expired" : "write_failed";
                return false;
            }
            if (!pAccept)
            {
                bool closed = Close(proposal,
                    DiplomacyProposalStatus.Rejected,
                    pPlayerResponse ? "player_rejected" : "ai_rejected", -1);
                if (closed && DiplomacyProposalRules.IsPeaceProposal(
                        proposal.Type))
                    WarPeaceSettlementService.Instance.Respond(
                        proposal.DetailId, accept: false);
                pReason = closed ? "" : "write_failed";
                return closed;
            }
            Kingdom requester = FindKingdom(proposal.RequesterKingdomId);
            Kingdom responder = FindKingdom(proposal.ResponderKingdomId);
            if (requester?.data == null || responder?.data == null)
            {
                bool closed = Close(proposal,
                    DiplomacyProposalStatus.Cancelled,
                    "no_longer_available", -1);
                if (closed && DiplomacyProposalRules.IsPeaceProposal(
                        proposal.Type))
                    WarPeaceSettlementService.Instance.Cancel(
                        proposal.DetailId, "no_longer_available");
                pReason = closed ? "no_longer_available" : "write_failed";
                return false;
            }
            if (RulerHouseholdRules.IsConsortRequestDetail(
                    proposal.DetailId) && proposal.RequesterActorId < 0L)
            {
                if (RulerHouseholdRules.ShouldDeferConsortRequestAcceptance(
                        pPlayerResponse, candidateSelected: false))
                {
                    pReason = "household_candidate_selection_required";
                    return false;
                }
                if (!TrySelectAiConsortForRequest(proposal, out pReason))
                {
                    bool closed = Close(proposal,
                        DiplomacyProposalStatus.Cancelled, pReason, -1);
                    if (!closed) pReason = "write_failed";
                    return false;
                }
                proposal = Find(pProposalId);
                if (proposal?.RequesterActorId < 0L)
                {
                    pReason = "household_candidate_selection_required";
                    return false;
                }
            }
            ProposalContext current = ReadContext(requester, responder,
                proposal.Type, proposal.WarId);
            string unavailable = DiplomacyProposalRules.UnavailableReason(
                proposal.Type, current.Availability);
            if (!string.IsNullOrEmpty(unavailable))
            {
                bool closed = Close(proposal,
                    DiplomacyProposalStatus.Cancelled, unavailable, -1);
                if (closed && DiplomacyProposalRules.IsPeaceProposal(
                        proposal.Type))
                    WarPeaceSettlementService.Instance.Cancel(
                        proposal.DetailId, unavailable);
                pReason = closed ? unavailable : "write_failed";
                return false;
            }
            if (!ReserveForExecution(proposal))
            {
                pReason = "already_responded";
                return false;
            }
            if (!Execute(proposal, out int treatyUntil, out pReason))
            {
                ModClass.LogWarning("Diplomacy response execution rejected: " +
                                    "proposal=" + proposal.ProposalId +
                                    ", type=" + TypeId(proposal.Type) +
                                    ", requester=" +
                                    proposal.RequesterKingdomId +
                                    ", responder=" +
                                     proposal.ResponderKingdomId +
                                     ", reason=" + pReason);
                if (ShouldRetryAllianceWithdrawal(proposal, pReason))
                {
                    _nextProcessingPollTime =
                        LineageService.CurTime() + WorldTimePerDay;
                    return false;
                }
                if (DiplomacyProposalRules.IsPeaceProposal(
                        proposal.Type))
                {
                    WarPeaceDecisionResult cancelled =
                        WarPeaceSettlementService.Instance.Cancel(
                            proposal.DetailId, pReason);
                    if (!cancelled.Success &&
                        (cancelled.Status ==
                             WarPeaceSettlementStatus.Accepted ||
                         cancelled.Status ==
                             WarPeaceSettlementStatus.Executing ||
                         cancelled.Status ==
                             WarPeaceSettlementStatus.TermsApplied))
                    {
                        _nextProcessingPollTime =
                            LineageService.CurTime() + WorldTimePerDay;
                        return false;
                    }
                }
                bool closed = CloseReserved(proposal,
                    DiplomacyProposalStatus.Cancelled,
                    pReason, -1);
                if (!closed)
                    ModClass.LogWarning(
                        "Diplomacy failed execution could not close reserved proposal=" +
                        proposal.ProposalId);
                return false;
            }
            bool accepted = CloseReserved(proposal,
                DiplomacyProposalStatus.Accepted,
                pPlayerResponse ? "player_accepted" : "ai_accepted",
                treatyUntil);
            if (accepted) return true;

            // The world-side effect has already committed. Keep the durable
            // reservation for the bounded recovery loop instead of allowing
            // the same diplomacy action to execute twice.
            _nextProcessingPollTime = LineageService.CurTime();
            ModClass.LogWarning("Diplomacy proposal finalization deferred: " +
                                "proposal=" + proposal.ProposalId +
                                ", target_status=accepted");
            pReason = "";
            return true;
        }

        public static bool HasPendingPair(long pKingdomA, long pKingdomB)
        {
            if (!Ready || !DiplomacyConversationRules.TryNormalizePair(
                    pKingdomA, pKingdomB, out DiplomacyKingdomPair pair))
                return false;
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT 1 FROM " +
                DiplomacyProposalTableItem.GetTableName() +
                " WHERE STATUS IN ('pending','processing') AND " +
                "MIN(REQUESTER_KINGDOM_ID,RESPONDER_KINGDOM_ID)=@a AND " +
                "MAX(REQUESTER_KINGDOM_ID,RESPONDER_KINGDOM_ID)=@b LIMIT 1";
            command.Parameters.AddWithValue("@a", pair.FirstKingdomId);
            command.Parameters.AddWithValue("@b", pair.SecondKingdomId);
            return command.ExecuteScalar() != null;
        }

        public static bool HasActiveNonAggression(Kingdom pKingdomA,
            Kingdom pKingdomB)
        {
            return TryGetActiveNonAggression(pKingdomA, pKingdomB, out _);
        }

        public static bool TryGetActiveNonAggression(Kingdom pKingdomA,
            Kingdom pKingdomB, out int pUntilYear)
        {
            ReadActiveTreatyYears(pKingdomA, pKingdomB,
                out pUntilYear, out _);
            return pUntilYear >= SafeYear();
        }

        public static bool TryGetActiveTruce(Kingdom pKingdomA,
            Kingdom pKingdomB, out int pUntilYear)
        {
            ReadActiveTreatyYears(pKingdomA, pKingdomB,
                out _, out pUntilYear);
            return pUntilYear >= SafeYear();
        }

        public static bool HasActiveWarBlocker(Kingdom pKingdomA,
            Kingdom pKingdomB)
        {
            ReadActiveTreatyYears(pKingdomA, pKingdomB,
                out int nonAggressionUntil, out int truceUntil);
            int year = SafeYear();
            return nonAggressionUntil >= year || truceUntil >= year;
        }

        public static bool RegisterTruce(War pWar)
        {
            if (pWar?.data == null) return false;
            return RegisterTrucePair(pWar.data.id,
                pWar.getMainAttacker(), pWar.getMainDefender(),
                DiplomacyProposalRules.TruceYears, "war_settlement",
                pWarName: WarRuntimeDisplayService.Resolve(pWar));
        }

        public static bool RegisterCoalitionTruces(War pWar,
            IReadOnlyList<Kingdom> pAttackers,
            IReadOnlyList<Kingdom> pDefenders)
        {
            if (pWar?.data == null || pAttackers == null ||
                pDefenders == null) return false;
            bool foundPair = false;
            bool success = true;
            for (int attackerIndex = 0;
                 attackerIndex < pAttackers.Count; attackerIndex++)
            {
                Kingdom attacker = pAttackers[attackerIndex];
                if (attacker?.data == null) continue;
                for (int defenderIndex = 0;
                     defenderIndex < pDefenders.Count; defenderIndex++)
                {
                    Kingdom defender = pDefenders[defenderIndex];
                    if (defender?.data == null || attacker == defender)
                        continue;
                    foundPair = true;
                    if (!RegisterTrucePair(pWar.data.id, attacker, defender,
                            DiplomacyProposalRules.TruceYears,
                            "war_settlement", pWarName:
                            WarRuntimeDisplayService.Resolve(pWar)))
                        success = false;
                }
            }
            return foundPair && success;
        }

        public static bool HasCoalitionSettlementTruces(
            WarPeaceSettlementProposal pProposal)
        {
            return ProcessCoalitionSettlementTruces(pProposal,
                pWriteMissing: false);
        }

        public static bool EnsureCoalitionSettlementTruces(
            WarPeaceSettlementProposal pProposal)
        {
            return ProcessCoalitionSettlementTruces(pProposal,
                pWriteMissing: true);
        }

        private static bool ProcessCoalitionSettlementTruces(
            WarPeaceSettlementProposal pProposal, bool pWriteMissing)
        {
            if (!Ready || pProposal == null || pProposal.WarId < 0 ||
                pProposal.Scope != WarPeaceSettlementScopeKind.Coalition ||
                pProposal.Participants == null ||
                pProposal.Participants.Count > 64) return false;
            if (pProposal.Participants.Count == 0)
                return ProcessLegacyCoalitionSettlementTruce(pProposal,
                    pWriteMissing);

            var attackers = new List<long>();
            var defenders = new List<long>();
            var seen = new HashSet<long>();
            for (int i = 0; i < pProposal.Participants.Count; i++)
            {
                WarPeaceSettlementParticipantSnapshot participant =
                    pProposal.Participants[i];
                if (participant == null || participant.KingdomId < 0 ||
                    !seen.Add(participant.KingdomId)) return false;
                if (string.Equals(participant.SideKind, "attacker",
                        StringComparison.Ordinal))
                    attackers.Add(participant.KingdomId);
                else if (string.Equals(participant.SideKind, "defender",
                             StringComparison.Ordinal))
                    defenders.Add(participant.KingdomId);
                else return false;
            }
            if (attackers.Count == 0 || defenders.Count == 0) return false;

            int treatyStartYear = SettlementTruceStartYear(pProposal);
            int requiredUntil = RequiredTreatyUntil(treatyStartYear,
                DiplomacyProposalRules.TruceYears);
            if (!TryReadAdequateWarTrucePairs(pProposal.WarId,
                    requiredUntil, out HashSet<string> existing))
                return false;

            for (int attackerIndex = 0;
                 attackerIndex < attackers.Count; attackerIndex++)
            {
                long attackerId = attackers[attackerIndex];
                for (int defenderIndex = 0;
                     defenderIndex < defenders.Count; defenderIndex++)
                {
                    long defenderId = defenders[defenderIndex];
                    string pairKey = TreatyPairKey(attackerId, defenderId);
                    Kingdom attacker = FindKingdom(attackerId);
                    Kingdom defender = FindKingdom(defenderId);
                    if (existing.Contains(pairKey))
                    {
                        ReconcilePendingDeclarationsForActiveTreaty(
                            attacker, defender);
                        continue;
                    }
                    if (!pWriteMissing) return false;
                    if (!RegisterTrucePair(pProposal.WarId, attacker,
                            defender, DiplomacyProposalRules.TruceYears,
                            "war_settlement", treatyStartYear,
                            WarRuntimeDisplayService.Resolve(
                                FindWar(pProposal.WarId))))
                        return false;
                    existing.Add(pairKey);
                }
            }
            return true;
        }

        private static bool ProcessLegacyCoalitionSettlementTruce(
            WarPeaceSettlementProposal pProposal, bool pWriteMissing)
        {
            long requesterId = pProposal.RequesterKingdomId;
            long responderId = pProposal.ResponderKingdomId;
            if (requesterId < 0 || responderId < 0 ||
                requesterId == responderId) return false;
            int treatyStartYear = SettlementTruceStartYear(pProposal);
            int requiredUntil = RequiredTreatyUntil(treatyStartYear,
                DiplomacyProposalRules.TruceYears);
            if (!TryReadAdequateWarTrucePairs(pProposal.WarId,
                    requiredUntil, out HashSet<string> existing))
                return false;
            if (existing.Contains(TreatyPairKey(requesterId, responderId)))
            {
                ReconcilePendingDeclarationsForActiveTreaty(
                    FindKingdom(requesterId), FindKingdom(responderId));
                return true;
            }
            if (!pWriteMissing) return false;
            return RegisterTrucePair(pProposal.WarId,
                FindKingdom(requesterId), FindKingdom(responderId),
                DiplomacyProposalRules.TruceYears, "war_settlement_legacy",
                treatyStartYear, WarRuntimeDisplayService.Resolve(
                    FindWar(pProposal.WarId)));
        }

        public const int SeparatePeaceTruceYears = 5;

        public static bool RegisterSeparatePeaceTruce(War pWar,
            Kingdom pFirst, Kingdom pSecond,
            int pTreatyStartYear = -1)
        {
            return pWar?.data != null && RegisterTrucePair(pWar.data.id,
                pFirst, pSecond, SeparatePeaceTruceYears,
                "separate_peace_settlement", pTreatyStartYear,
                WarRuntimeDisplayService.Resolve(pWar));
        }

        private static bool RegisterTrucePair(long pWarId, Kingdom pFirst,
            Kingdom pSecond, int pDurationYears, string pReason,
            int pTreatyStartYear = -1, string pWarName = "")
        {
            if (!Ready || pWarId < 0 || pFirst?.data == null ||
                pSecond?.data == null || pFirst == pSecond ||
                pDurationYears <= 0) return false;
            try
            {
                int year = SafeYear();
                int treatyStartYear = pTreatyStartYear >= 0
                    ? pTreatyStartYear
                    : year;
                int treatyUntil = RequiredTreatyUntil(treatyStartYear,
                    pDurationYears);
                using (var existing = new SQLiteCommand(DB))
                {
                    existing.CommandText = "SELECT 1 FROM " +
                        DiplomacyProposalTableItem.GetTableName() +
                        " WHERE PROPOSAL_TYPE='truce' AND WAR_ID=@war AND " +
                        "STATUS='accepted' AND " +
                        "TREATY_UNTIL_YEAR>=@required_until AND " +
                        "((REQUESTER_KINGDOM_ID=@a AND " +
                        "RESPONDER_KINGDOM_ID=@b) OR " +
                        "(REQUESTER_KINGDOM_ID=@b AND " +
                        "RESPONDER_KINGDOM_ID=@a)) LIMIT 1";
                    existing.Parameters.AddWithValue("@war", pWarId);
                    existing.Parameters.AddWithValue("@a", pFirst.id);
                    existing.Parameters.AddWithValue("@b", pSecond.id);
                    existing.Parameters.AddWithValue("@required_until",
                        treatyUntil);
                    if (existing.ExecuteScalar() != null)
                    {
                        ReconcilePendingDeclarationsForActiveTreaty(
                            pFirst, pSecond);
                        return true;
                    }
                }

                long proposalId = TableIdAllocator.Next(DB,
                    DiplomacyProposalTableItem.GetTableName(), "PROPOSAL_ID");
                double now = LineageService.CurTime();
                string attackerTitle = DiplomaticSenderTitle(pFirst);
                string defenderTitle = DiplomaticSenderTitle(pSecond);
                string attackerPrefix = HistoryWriter.BuildYearPrefix(now,
                    pFirst);
                string defenderPrefix = HistoryWriter.BuildYearPrefix(now,
                    pSecond);
                DiplomacyLetterStyle requestStyle = ResolveLetterStyle(
                    pFirst, pSecond);
                DiplomacyLetterStyle responseStyle = ResolveLetterStyle(
                    pSecond, pFirst);
                DiplomacyLetterTone requestTone = ResolveLetterTone(
                    pFirst, pSecond);
                DiplomacyLetterTone responseTone = ResolveLetterTone(
                    pSecond, pFirst);
                DB.Insert(DiplomacyProposalTableItem.GetTableName(),
                    ColumnVal.Create("PROPOSAL_ID", proposalId),
                    ColumnVal.Create("REQUESTER_KINGDOM_ID", pFirst.id),
                    ColumnVal.Create("REQUESTER_NAME", pFirst.name ?? ""),
                    ColumnVal.Create("RESPONDER_KINGDOM_ID", pSecond.id),
                    ColumnVal.Create("RESPONDER_NAME", pSecond.name ?? ""),
                    ColumnVal.Create("PROPOSAL_TYPE", "truce"),
                    ColumnVal.Create("STATUS", "accepted"),
                    ColumnVal.Create("WAR_ID", pWarId),
                    ColumnVal.Create("DETAIL_ID",
                        WarRuntimeDisplayRules.IsDisplayName(pWarName)
                            ? pWarName
                            : ""),
                    ColumnVal.Create("PLAYER_INITIATED", 0),
                    ColumnVal.Create("CREATED_YEAR", year),
                    ColumnVal.Create("EXPIRY_YEAR", treatyUntil),
                    ColumnVal.Create("RESPONSE_YEAR", year),
                    ColumnVal.Create("TREATY_UNTIL_YEAR", treatyUntil),
                    ColumnVal.Create("CREATED_TIME", now),
                    ColumnVal.Create("RESPONSE_DUE_TIME", now),
                    ColumnVal.Create("RESPONSE_TIME", now),
                    ColumnVal.Create("RESPONSE_REASON", pReason ??
                        "war_settlement"),
                    ColumnVal.Create("REQUESTER_TITLE", attackerTitle),
                    ColumnVal.Create("RESPONDER_TITLE", defenderTitle),
                    ColumnVal.Create("REQUEST_YEAR_PREFIX", attackerPrefix),
                    ColumnVal.Create("RESPONSE_YEAR_PREFIX", defenderPrefix),
                    ColumnVal.Create("REQUEST_STYLE",
                        DiplomacyConversationRules.LetterStyleId(requestStyle)),
                    ColumnVal.Create("REQUEST_TONE",
                        DiplomacyConversationRules.LetterToneId(requestTone)),
                    ColumnVal.Create("RESPONSE_STYLE",
                        DiplomacyConversationRules.LetterStyleId(responseStyle)),
                    ColumnVal.Create("RESPONSE_TONE",
                        DiplomacyConversationRules.LetterToneId(responseTone)));
                DiplomacyConversationService.RecordProposal(pFirst,
                    pSecond, proposalId);
                NotifyPair(pFirst.id, pSecond.id);
                ReconcilePendingDeclarationsForActiveTreaty(
                    pFirst, pSecond);
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy truce write failed: " +
                                    exception.Message);
                return false;
            }
        }

        internal static void ReconcilePendingDeclarationsForActiveTreaty(
            Kingdom pFirst, Kingdom pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null ||
                pFirst == pSecond) return;
            try
            {
                DiplomaticWarDeclarationService.ClearPendingForPair(
                    pFirst, pSecond, "active_war_blocker");
                DiplomaticWarDeclarationService.ClearPendingForPair(
                    pSecond, pFirst, "active_war_blocker");
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Diplomacy truce declaration reconciliation failed: " +
                    exception.Message);
            }
        }

        private static int SettlementTruceStartYear(
            WarPeaceSettlementProposal pProposal)
        {
            if (pProposal?.ResponseYear >= 0) return pProposal.ResponseYear;
            if (pProposal?.CreatedYear >= 0) return pProposal.CreatedYear;
            return SafeYear();
        }

        private static int RequiredTreatyUntil(int pStartYear,
            int pDurationYears)
        {
            return (int)Math.Min(int.MaxValue,
                (long)Math.Max(0, pStartYear) +
                Math.Max(0, pDurationYears));
        }

        private static bool TryReadAdequateWarTrucePairs(long pWarId,
            int pRequiredUntil, out HashSet<string> pPairs)
        {
            pPairs = new HashSet<string>(StringComparer.Ordinal);
            if (!Ready || pWarId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID FROM " +
                    DiplomacyProposalTableItem.GetTableName() +
                    " WHERE PROPOSAL_TYPE='truce' AND WAR_ID=@war AND " +
                    "STATUS='accepted' AND " +
                    "TREATY_UNTIL_YEAR>=@required_until";
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@required_until",
                    pRequiredUntil);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    pPairs.Add(TreatyPairKey(reader.GetInt64(0),
                        reader.GetInt64(1)));
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy truce coverage read failed: " +
                                    exception.Message);
                pPairs.Clear();
                return false;
            }
        }

        private static string TreatyPairKey(long pFirstId, long pSecondId)
        {
            long low = Math.Min(pFirstId, pSecondId);
            long high = Math.Max(pFirstId, pSecondId);
            return low + ":" + high;
        }

        public static void ProcessFrame()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            if (!Ready || World.world == null || World.world.isPaused()) return;
            double now = LineageService.CurTime();
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            bool recovered;
            try { recovered = TryRecoverOneProcessing(now); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "diplomacy_recover_processing", diagnostic);
            }
            if (recovered) return;
            if (_nextResponsePollTime > now + WorldTimePerDay)
                _nextResponsePollTime = now;
            if (_nextResponsePollTime > now) return;

            long proposalId;
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { proposalId = FindDuePendingProposal(now); }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy due response query failed: " +
                                    exception.Message);
                _nextResponsePollTime = now + WorldTimePerDay;
                return;
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "diplomacy_due_query", diagnostic);
            }
            if (proposalId < 0)
            {
                _nextResponsePollTime = now + WorldTimePerDay;
                return;
            }

            bool advanced = false;
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { advanced = ProcessOneDueProposal(proposalId, now); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "diplomacy_evaluate_response", diagnostic);
            }
            _nextResponsePollTime = advanced ? now : now + WorldTimePerDay;
        }

        private static bool ProcessOneDueProposal(long pProposalId,
            double pNow)
        {
            try
            {
                if (EvaluateAndRespond(pProposalId)) return true;
                DiplomacyProposal current = Find(pProposalId);
                if (current == null ||
                    !DiplomacyProposalRules.ShouldRetryFailedResponse(
                        current.Status))
                    return true;
                ModClass.LogWarning(
                    "Diplomacy due response was not handled: proposal=" +
                    pProposalId);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Diplomacy due response execution failed: proposal=" +
                    pProposalId + ", error=" + exception.Message);
            }
            return DeferFailedResponse(pProposalId, pNow);
        }

        private static bool DeferFailedResponse(long pProposalId,
            double pNow)
        {
            try
            {
                double next = DiplomacyProposalRules
                    .NextResponseRuntimeTime(pNow);
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    DiplomacyProposalTableItem.GetTableName() +
                    " SET RESPONSE_DUE_TIME=@next," +
                    "RESPONSE_REASON='response_retry' " +
                    "WHERE PROPOSAL_ID=@id AND STATUS='pending'";
                command.Parameters.AddWithValue("@next", next);
                command.Parameters.AddWithValue("@id", pProposalId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Diplomacy response retry write failed: proposal=" +
                    pProposalId + ", error=" + exception.Message);
                return false;
            }
        }

        public static void ClearRuntime()
        {
            _nextResponsePollTime = -1d;
            _nextProcessingPollTime = -1d;
            ProposalRuntime.Clear();
            ClearConsortRequestTargetCursors();
            WartimeMilitaryPotentialService.ClearRuntime();
            WarForceEliminationSettlementService.ClearRuntime();
        }

        public static void OnKingdomDestroyed(long pKingdomId)
        {
            if (pKingdomId < 0L) return;
            ProposalRuntime.RemoveKingdom(pKingdomId);
            RemoveConsortRequestTargetCursor(pKingdomId);
            WartimeMilitaryPotentialService.RemoveKingdom(pKingdomId);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            _ = RunAuthoritativeYear(pKingdom);
        }

        internal static AsyncStrategyAuthorityTrace RunAuthoritativeYear(
            Kingdom pKingdom)
        {
            if (!TryPrepareAnnualProposal(pKingdom, out int year))
                return AsyncStrategyAuthorityTrace.Skipped("maintenance");
            if (!ProposalRuntime.TryBeginAnnualDecision(pKingdom.id, year))
                return AsyncStrategyAuthorityTrace.Skipped(
                    "already_processed");
            if (TryScheduleWarPeace(pKingdom))
                return AsyncStrategyAuthorityTrace.Skipped(
                    "war_settlement");
            if (!GeneralAiProposalCooldownReady(pKingdom, year))
                return AsyncStrategyAuthorityTrace.Skipped("cooldown");
            if (RulerHouseholdService.TryFillOneDomesticVacancy(pKingdom))
                return AsyncStrategyAuthorityTrace.Skipped(
                    "domestic_household");
            if (!TryPrepareOrdinaryAiProposals(pKingdom, year,
                    out Kingdom contact,
                    out List<PreparedAiProposal> candidates))
                return AsyncStrategyAuthorityTrace.Planned("none");
            string trace = SummarizePreparedAiProposals(contact, candidates);
            if (!TryCreatePreparedOrdinary(pKingdom, contact, candidates))
                return AsyncStrategyAuthorityTrace.Planned(trace);
            pKingdom.data.set(LineageKeys.DIPLOMACY_AI_LAST_PROPOSAL_YEAR,
                year);
            return AsyncStrategyAuthorityTrace.Planned(trace);
        }

        internal static bool TryPrepareAsyncProposalYear(Kingdom pKingdom,
            int pRequestedYear)
        {
            if (!TryPrepareAnnualProposal(pKingdom, out int year) ||
                year != pRequestedYear) return false;
            if (TryScheduleWarPeace(pKingdom)) return false;
            return GeneralAiProposalCooldownReady(pKingdom, year);
        }

        internal static bool TryBeginAsyncProposalYear(Kingdom pKingdom,
            int pRequestedYear, long pExpectedResponderId,
            out AsyncStrategyAdmissionToken pToken)
        {
            pToken = default;
            if (AW3MultiplayerReplicaScope.IsReplicaSession || !Ready ||
                pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral() || SafeYear() != pRequestedYear ||
                pExpectedResponderId < 0L)
                return false;
            if (!GeneralAiProposalCooldownReady(pKingdom, pRequestedYear))
                return false;
            Kingdom contact = PeekDiplomacyContact(pKingdom);
            long observedResponderId = contact?.data == null
                ? -1L
                : contact.id;
            if (observedResponderId != pExpectedResponderId) return false;
            int cityCount = pKingdom.cities?.Count ?? 0;
            if (cityCount <= 0) return false;
            pKingdom.data.get(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                out int previousCursor, 0);
            long leaseId = NextAsyncAdmissionLeaseId();
            if (!ProposalRuntime.TryReserveAnnualDecision(pKingdom.id,
                    pRequestedYear, leaseId, out int previousYear))
                return false;
            if (!AsyncStrategyAdmissionToken.TryCreateDiplomacy(leaseId,
                    previousYear, pRequestedYear, previousCursor, cityCount,
                    pExpectedResponderId, observedResponderId, out pToken))
            {
                ProposalRuntime.TryRollbackAnnualDecision(pKingdom.id,
                    pRequestedYear, leaseId, previousYear);
                return false;
            }
            pKingdom.data.set(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                pToken.ReservedCursor);
            return true;
        }

        internal static bool TryRollbackAsyncProposalYear(Kingdom pKingdom,
            AsyncStrategyAdmissionToken pToken)
        {
            if (pKingdom?.data == null || !pToken.IsValid ||
                !pToken.HasCursor)
                return false;
            pKingdom.data.get(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                out int currentCursor, 0);
            int currentMarker = pToken.ReservedMarker;
            int rollbackCursor = currentCursor;
            if (!pToken.TryRollback(pToken.LeaseId, ref currentMarker,
                    ref rollbackCursor) ||
                !ProposalRuntime.TryRollbackAnnualDecision(pKingdom.id,
                    pToken.ReservedMarker, pToken.LeaseId,
                    pToken.PreviousMarker))
                return false;
            pKingdom.data.set(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                rollbackCursor);
            return true;
        }

        internal static bool TryCompleteAsyncProposalYear(
            Kingdom pKingdom, int pRequestedYear,
            AsyncStrategyAdmissionToken pToken)
        {
            if (pKingdom?.data == null || !pToken.IsValid ||
                !pToken.HasCursor ||
                !ProposalRuntime.TryCompleteAnnualDecision(pKingdom.id,
                    pRequestedYear, pToken.LeaseId))
                return false;
            pKingdom.data.get(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                out int currentCursor, 0);
            if (currentCursor != pToken.ReservedCursor) return false;
            if (!TryPrepareAnnualProposal(pKingdom, out int year) ||
                year != pRequestedYear)
                return false;
            if (TryScheduleWarPeace(pKingdom)) return false;
            if (!GeneralAiProposalCooldownReady(pKingdom, year))
                return false;
            return !RulerHouseholdService.TryFillOneDomesticVacancy(
                pKingdom);
        }

        internal static bool TryCompleteDomesticOnlyProposalYear(
            Kingdom pKingdom, int pRequestedYear,
            bool pForeignCaptureAvailable)
        {
            long kingdomId = pKingdom?.data == null ? -1L : pKingdom.id;
            return DomesticHouseholdAnnualCompletion.TryRun(
                ProposalRuntime, kingdomId, pRequestedYear,
                pForeignCaptureAvailable,
                resolveCurrentYear: () =>
                    TryPrepareAnnualProposal(pKingdom, out int year)
                        ? (int?)year
                        : null,
                tryScheduleWarPeace: () => TryScheduleWarPeace(pKingdom),
                cooldownReady: year =>
                    GeneralAiProposalCooldownReady(pKingdom, year),
                tryFillOneDomesticVacancy: () =>
                    RulerHouseholdService.TryFillOneDomesticVacancy(
                        pKingdom));
        }

        private static long NextAsyncAdmissionLeaseId()
        {
            _nextAsyncAdmissionLeaseId = _nextAsyncAdmissionLeaseId ==
                                         long.MaxValue
                ? 1L
                : _nextAsyncAdmissionLeaseId + 1L;
            return _nextAsyncAdmissionLeaseId;
        }

        private static bool TryPrepareAnnualProposal(Kingdom pKingdom,
            out int pYear)
        {
            pYear = -1;
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return false;
            if (!Ready || pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral()) return false;
            pYear = SafeYear();
            int year = pYear;
            return ProposalRuntime.GetOrRunAnnualPreparation(pKingdom.id,
                year, () => RunAnnualDiplomacyMaintenance(pKingdom, year));
        }

        private static bool RunAnnualDiplomacyMaintenance(Kingdom pKingdom,
            int pYear)
        {
            CalibrateOwnedWarScores(pKingdom, pYear);
            WarPeaceSettlementService.Instance.ProcessReparations(pKingdom);
            const int recoveryPasses = 4;
            var recoveryWarIds = new List<long>(4);
            try
            {
                foreach (War war in WarRecoveryCursor(pKingdom).Take(4))
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    recoveryWarIds.Add(war.data.id);
                }
            }
            catch { }
            for (int i = 0; i < recoveryPasses; i++)
            {
                if (!WarPeaceSettlementService.Instance
                        .RecoverOneForKingdom(pKingdom.id, recoveryWarIds))
                    break;
            }
            double now = LineageService.CurTime();
            DiplomacyProposal incoming = FindOldestPendingIncoming(
                pKingdom.id);
            if (incoming != null && incoming.ResponseDueTime <= now)
            {
                ProcessOneDueProposal(incoming.ProposalId, now);
                return false;
            }
            return true;
        }

        private static bool GeneralAiProposalCooldownReady(Kingdom pKingdom,
            int pYear)
        {
            pKingdom.data.get(LineageKeys.DIPLOMACY_AI_LAST_PROPOSAL_YEAR,
                out int lastYear, -1);
            return lastYear < 0 || pYear - lastYear >=
                   DiplomacyProposalRules.AiProposalCooldownYears;
        }

        private static void CalibrateOwnedWarScores(Kingdom pKingdom,
            int pYear)
        {
            const int maximumWarsPerKingdomYear = 8;
            int inspected = 0;
            try
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (inspected++ >= maximumWarsPerKingdomYear) break;
                    if (war?.data == null || war.hasEnded() ||
                        !war.isMainAttacker(pKingdom)) continue;
                    WarScoreService.CalibrateYear(war, pYear);
                }
            }
            catch { }
        }

        public static void RegisterWarSettlementBaseline(War pWar)
        {
            if (pWar?.data == null) return;
            WarParticipantMobilizationBaselines mobilization =
                WarParticipantMobilizationBaselineService.
                    RegisterExistingParticipants(pWar);
            pWar.data.set(SettlementInitialAttackerCities,
                Math.Max(0, pWar.countAttackersCities()));
            pWar.data.set(SettlementInitialDefenderCities,
                Math.Max(0, pWar.countDefendersCities()));
            pWar.data.set(SettlementInitialAttackerWarriors,
                mobilization.Attackers);
            pWar.data.set(SettlementInitialDefenderWarriors,
                mobilization.Defenders);
        }

        private static bool EvaluateAndRespond(long pProposalId)
        {
            DiplomacyProposal proposal = Find(pProposalId);
            Kingdom requester = FindKingdom(proposal?.RequesterKingdomId ?? -1L);
            Kingdom responder = FindKingdom(proposal?.ResponderKingdomId ?? -1L);
            if (proposal == null) return true;
            if (requester?.data == null || responder?.data == null)
            {
                bool closed = Close(proposal,
                    DiplomacyProposalStatus.Cancelled,
                    "no_longer_available", -1);
                if (closed && DiplomacyProposalRules.IsPeaceProposal(
                        proposal.Type))
                    WarPeaceSettlementService.Instance.Cancel(
                        proposal.DetailId, "no_longer_available");
                return closed;
            }
            var selection = new DiplomacyProposalSelection(
                proposal.TargetKingdomId, proposal.RequesterActorId,
                proposal.ResponderActorId, proposal.TargetCityId,
                proposal.DetailId);
            DiplomacyActionAssessment assessment = proposal.Type is
                DiplomacyProposalType.Coalition or
                DiplomacyProposalType.RoyalMarriage or
                DiplomacyProposalType.HouseholdOffering or
                DiplomacyProposalType.Vassalize
                ? AssessWithSelection(requester, responder, proposal.Type,
                    proposal.WarId, selection, pIgnorePending: true)
                : Assess(requester, responder, proposal.Type, proposal.WarId,
                    pIgnorePending: true);
            if (!assessment.Allowed)
            {
                // Re-enter the authoritative responder path so the changed
                // world state closes this request instead of leaving a
                // permanent pending pair lock.
                return Respond(proposal.ProposalId, pAccept: true,
                    pPlayerResponse: false, out _);
            }
            if (assessment.Acceptance == null)
            {
                return Respond(proposal.ProposalId, pAccept: false,
                    pPlayerResponse: false, out _);
            }
            bool accept = DiplomacyProposalRules.IsUnilateral(
                              proposal.Type) ||
                          assessment.Acceptance.ExpectedAccepted;
            if (DiplomacyProposalRules.IsPeaceProposal(proposal.Type))
            {
                WarPeaceAcceptanceResult settlementAcceptance =
                    WarPeaceSettlementService.Instance.EvaluateAi(
                        proposal.DetailId,
                        SettlementResolve(responder),
                        proposal.Type == DiplomacyProposalType.Surrender);
                accept = settlementAcceptance.Accept;
            }
            return Respond(proposal.ProposalId, accept,
                pPlayerResponse: false, out _);
        }

        private static int SettlementResolve(Kingdom pKingdom)
        {
            int value = 50;
            try
            {
                CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
                value += (int)Math.Round(((court?.war ?? .5f) -
                                          (court?.peace ?? .5f)) * 30f);
                Actor ruler = pKingdom?.king;
                if (ruler?.data != null)
                    value += (int)Math.Round(ruler.stats["warfare"] / 4f);
            }
            catch { }
            return Math.Max(0, Math.Min(100, value));
        }

        private static DiplomacyProposalScoreFacts BuildScoreFacts(
            Kingdom pRequester, Kingdom pResponder,
            bool pDirectRoyalMarriage = false,
            bool pProposedPrincipalWife = false, long pWarId = -1L,
            ProposalContext pContext = null,
            bool pProposedConsortRequest = false)
        {
            return BuildScoreFactsCore(pRequester, pResponder,
                MandateService.IsMandateKingdom(pRequester),
                pDirectRoyalMarriage, pProposedPrincipalWife, pWarId,
                pContext, pProposedConsortRequest);
        }

        private static DiplomacyProposalScoreFacts BuildScoreFactsReadOnly(
            Kingdom pRequester, Kingdom pResponder,
            MandateReport pMandateReport,
            bool pDirectRoyalMarriage = false,
            bool pProposedPrincipalWife = false, long pWarId = -1L,
            ProposalContext pContext = null,
            bool pProposedConsortRequest = false)
        {
            return BuildScoreFactsCore(pRequester, pResponder,
                MandateService.IsMandateKingdomReadOnly(pRequester,
                    pMandateReport), pDirectRoyalMarriage,
                pProposedPrincipalWife, pWarId, pContext,
                pProposedConsortRequest);
        }

        private static DiplomacyProposalScoreFacts BuildScoreFactsCore(
            Kingdom pRequester, Kingdom pResponder,
            bool pRequesterIsMandate, bool pDirectRoyalMarriage,
            bool pProposedPrincipalWife, long pWarId,
            ProposalContext pContext, bool pProposedConsortRequest)
        {
            int opinion = DiplomacyOpinionService.Read(pResponder, pRequester);
            CourtSnapshot responderCourt = CourtService.GetSnapshot(
                pResponder);
            float requesterPower = Math.Max(1, pRequester.power);
            float responderPower = Math.Max(1, pResponder.power);
            WarSettlementEvaluation settlement = pContext?.Settlement;
            if (settlement == null)
            {
                War pairWar = FindWarBetween(pRequester, pResponder, pWarId);
                settlement = BuildWarSettlementEvaluation(pRequester,
                    pResponder, pairWar);
            }
            if (settlement != null)
            {
                requesterPower = settlement.RequesterPower;
                responderPower = settlement.ResponderPower;
            }
            return new DiplomacyProposalScoreFacts(opinion,
                requesterPower, responderPower,
                SharedEnemy(pRequester, pResponder),
                settlement?.Position == WarSettlementPosition.Winning,
                SafeAllied(pRequester, pResponder),
                pRequesterIsMandate,
                SafeStat(pRequester.king, "diplomacy"),
                SafeStat(pResponder.king, "diplomacy"),
                proposedMarriageDirectRoyal: pDirectRoyalMarriage,
                responderPeace: responderCourt?.peace ?? .5f,
                responderAggression: responderCourt?.aggression ?? .5f,
                requesterLosingWar:
                    settlement?.Position == WarSettlementPosition.Losing,
                requesterReadyForPeace:
                    settlement?.RequesterReadyForPeace == true,
                responderReadyForPeace:
                    settlement?.ResponderReadyForPeace == true,
                responderReadyToConcede:
                    settlement?.ResponderReadyToConcede == true,
                hasWarSettlementReadiness: settlement != null,
                requesterSurrenderWarSituation:
                    settlement?.RequesterSurrenderWarSituation ?? 0,
                requesterSurrenderPower:
                    settlement?.RequesterSurrenderPower ?? 0,
                requesterSurrenderResolve:
                    settlement?.RequesterSurrenderResolve ?? 0,
                hasDetailedSurrender: settlement != null,
                proposedPrincipalWife: pProposedPrincipalWife,
                proposedConsortRequest: pProposedConsortRequest);
        }

        private static bool Execute(DiplomacyProposal pProposal,
            out int pTreatyUntil, out string pReason)
        {
            pTreatyUntil = -1;
            pReason = "execution_failed";
            Kingdom requester = FindKingdom(pProposal.RequesterKingdomId);
            Kingdom responder = FindKingdom(pProposal.ResponderKingdomId);
            if (requester?.data == null || responder?.data == null) return false;
            if (!MilitaryGovernorateWarRules.CanUseStateProposal(
                    VassalService.GetSubjectKind(requester),
                    VassalService.GetSubjectKind(responder)))
            {
                pReason = "military_governorate_no_diplomacy";
                return false;
            }
            try
            {
                switch (pProposal.Type)
                {
                    case DiplomacyProposalType.Alliance:
                        string allianceFailure = AllianceExecutionFailure(
                            requester, responder);
                        if (!string.IsNullOrEmpty(allianceFailure))
                        {
                            pReason = allianceFailure;
                            return false;
                        }
                        if (!TryFormOrJoinAlliance(requester, responder))
                        {
                            pReason = "alliance_execution_failed";
                            return false;
                        }
                        break;
                    case DiplomacyProposalType.Peace:
                    case DiplomacyProposalType.Surrender:
                    case DiplomacyProposalType.EnforceDemands:
                        WarPeaceExecutionResult settlementResult =
                            WarPeaceSettlementService.Instance
                                .AcceptAndExecuteOrResume(
                                    pProposal.DetailId);
                        if (!settlementResult.Success)
                        {
                            pReason = settlementResult.Reason;
                            return false;
                        }
                        break;
                    case DiplomacyProposalType.NonAggression:
                        pTreatyUntil = SafeYear() + 10;
                        break;
                    case DiplomacyProposalType.JoinWar:
                        War joinWar = FindWar(pProposal.WarId);
                        if (joinWar?.data == null || joinWar.hasEnded())
                        {
                            pReason = "no_joinable_war";
                            return false;
                        }
                        using (WarParticipantEntrySourceScope.Open(joinWar,
                                   responder,
                                   WarParticipantEntrySourceKind.AllianceCall,
                                   requester))
                        {
                            if (joinWar.isAttacker(requester))
                                joinWar.joinAttackers(responder);
                            else if (joinWar.isDefender(requester))
                                joinWar.joinDefenders(responder);
                            else
                            {
                                pReason = "no_joinable_war";
                                return false;
                            }
                        }
                        if (!joinWar.isAttacker(responder) &&
                            !joinWar.isDefender(responder))
                        {
                            pReason = "join_war_execution_failed";
                            return false;
                        }
                        break;
                    case DiplomacyProposalType.Vassalize:
                        string vassalDirection =
                            NormalizeVassalizationDetail(pProposal.DetailId);
                        if (vassalDirection ==
                            DiplomacyProposalOpportunityRules
                                .VassalizeInternalizeDetail)
                        {
                            int internalTier =
                                DiplomacyProposalOpportunityRules
                                    .InternalizationTier(
                                        VassalService
                                            .GetTributarySuzerain(
                                                requester) == responder,
                                        KingdomTitleService.IsEmperor(
                                            responder),
                                        MandateService.IsMandateKingdom(
                                            responder));
                            if (!VassalService.TryInternalizeTributary(
                                    requester, responder, internalTier,
                                    pProposal.WarId, out pReason))
                                return false;
                            break;
                        }
                        if (vassalDirection ==
                            DiplomacyProposalOpportunityRules
                                .VassalizeSeekDetail)
                        {
                            string protectionFailure =
                                ValidateProtectionRequest(requester,
                                    responder, pProposal.WarId,
                                    pProposal.TargetKingdomId, out _,
                                    out _);
                            if (!string.IsNullOrEmpty(protectionFailure))
                            {
                                pReason = protectionFailure;
                                return false;
                            }
                            if (!VassalService.SetVassal(requester,
                                    responder, "diplomatic_protection",
                                    pProposal.WarId,
                                    pContractTier:
                                    VassalContractTierRules.Outer))
                            {
                                pReason = "subject_write_failed";
                                return false;
                            }
                            if (pProposal.WarId >= 0L &&
                                !TryJoinProtectorToDefensiveWar(responder,
                                    requester, pProposal.WarId))
                            {
                                VassalService.EndVassal(requester,
                                    "protection_war_entry_failed");
                                pReason = "protection_war_entry_failed";
                                return false;
                            }
                            break;
                        }
                        if (vassalDirection !=
                            DiplomacyProposalOpportunityRules
                                .VassalizeDemandDetail)
                        {
                            pReason = "invalid_vassalize_direction";
                            return false;
                        }
                        if (!VassalService.CanSetVassal(responder, requester,
                                out string vassalFailure))
                        {
                            pReason = vassalFailure;
                            return false;
                        }
                        if (!VassalService.SetVassal(responder, requester,
                                "diplomatic_request", pProposal.WarId,
                                pContractTier:
                                VassalContractTierRules.Outer))
                        {
                            pReason = "subject_write_failed";
                            return false;
                        }
                        break;
                    case DiplomacyProposalType.Tributary:
                        if (!VassalService.CanSetVassal(responder, requester,
                                out string tributaryFailure))
                        {
                            pReason = tributaryFailure;
                            return false;
                        }
                        if (!VassalService.SetTributary(responder, requester,
                                "diplomatic_request"))
                        {
                            pReason = "subject_write_failed";
                            return false;
                        }
                        break;
                    case DiplomacyProposalType.EndAlliance:
                        Alliance alliance = requester.getAlliance();
                        if (alliance != null && alliance.hasKingdom(responder))
                        {
                            alliance.leave(requester);
                            if (SafeAllied(requester, responder))
                            {
                                pReason = "alliance_execution_failed";
                                return false;
                            }
                        }
                        if (!EnsureAllianceWithdrawalTruce(pProposal,
                                requester, responder, out pTreatyUntil))
                        {
                            pReason = "alliance_truce_write_failed";
                            return false;
                        }
                        break;
                    case DiplomacyProposalType.EndVassal:
                        Kingdom subject;
                        if (pProposal.DetailId ==
                            DiplomacyProposalOpportunityRules
                                .EndVassalReleaseDetail)
                        {
                            if (GetAnySuzerain(responder) != requester)
                            {
                                pReason = "no_vassal_relation";
                                return false;
                            }
                            subject = responder;
                        }
                        else if (pProposal.DetailId ==
                                 DiplomacyProposalOpportunityRules
                                     .EndVassalRequestDetail)
                        {
                            if (GetAnySuzerain(requester) != responder)
                            {
                                pReason = "no_vassal_relation";
                                return false;
                            }
                            subject = requester;
                        }
                        else if (string.IsNullOrWhiteSpace(
                                     pProposal.DetailId))
                        {
                            subject = GetAnySuzerain(requester) == responder
                                ? requester
                                : GetAnySuzerain(responder) == requester
                                    ? responder
                                    : null;
                            if (subject?.data == null)
                            {
                                pReason = "no_vassal_relation";
                                return false;
                            }
                        }
                        else
                        {
                            pReason = "invalid_end_vassal_direction";
                            return false;
                        }
                        if (!VassalService.EndVassal(subject,
                                "diplomatic_release"))
                        {
                            pReason = "no_vassal_relation";
                            return false;
                        }
                        break;
                    case DiplomacyProposalType.RoyalMarriage:
                        if (!DiplomaticMarriageService.TryCommit(
                                pProposal, out pReason))
                            return false;
                        break;
                    case DiplomacyProposalType.HouseholdOffering:
                        bool consortRequest = RulerHouseholdRules
                            .IsConsortRequestDetail(pProposal.DetailId);
                        Kingdom householdSource = consortRequest
                            ? responder
                            : requester;
                        Kingdom householdRecipient = consortRequest
                            ? requester
                            : responder;
                        RulerHouseholdKind kind;
                        if (consortRequest)
                            kind = RulerHouseholdKind.Consort;
                        else if (!RulerHouseholdRules.TryParseKind(
                                     pProposal.DetailId, out kind))
                        {
                            pReason = "invalid_household_kind";
                            return false;
                        }
                        if (householdRecipient.king?.data == null ||
                            householdRecipient.king.data.id !=
                            pProposal.ResponderActorId)
                        {
                            pReason = "household_ruler_stale";
                            return false;
                        }
                        if (!RulerHouseholdService.TryCommit(householdSource,
                                householdRecipient,
                                pProposal.RequesterActorId, kind,
                                pProposal.ProposalId, out pReason))
                            return false;
                        break;
                    case DiplomacyProposalType.Coalition:
                        if (!DiplomaticCoalitionService.TryCommit(
                                pProposal, out pReason))
                            return false;
                        break;
                    default:
                        pReason = "unavailable";
                        return false;
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy proposal execution failed: " +
                                    exception.Message);
                return false;
            }
            pReason = "";
            return true;
        }

        private static bool Close(DiplomacyProposal pProposal,
            DiplomacyProposalStatus pStatus, string pReason,
            int pTreatyUntil)
        {
            return CloseFromStatus(pProposal, pStatus, pReason,
                pTreatyUntil, "pending", pCaptureResponseMetadata: true);
        }

        private static bool CloseReserved(DiplomacyProposal pProposal,
            DiplomacyProposalStatus pStatus, string pReason,
            int pTreatyUntil)
        {
            return CloseFromStatus(pProposal, pStatus, pReason,
                pTreatyUntil, "processing", pCaptureResponseMetadata: false);
        }

        private static bool CloseFromStatus(DiplomacyProposal pProposal,
            DiplomacyProposalStatus pStatus, string pReason,
            int pTreatyUntil, string pExpectedStatus,
            bool pCaptureResponseMetadata)
        {
            try
            {
                Kingdom responder = FindKingdom(pProposal.ResponderKingdomId);
                double responseTime = LineageService.CurTime();
                string responderTitle = DiplomaticSenderTitle(responder);
                string responseYearPrefix = HistoryWriter.BuildYearPrefix(
                    responseTime, responder);
                Kingdom requester = FindKingdom(
                    pProposal.RequesterKingdomId);
                DiplomacyLetterStyle responseStyle = ResolveLetterStyle(
                    responder, requester);
                DiplomacyLetterTone responseTone = ResolveLetterTone(
                    responder, requester);
                using var command = new SQLiteCommand(DB);
                command.CommandText = pCaptureResponseMetadata
                    ? "UPDATE " + DiplomacyProposalTableItem.GetTableName() +
                      " SET STATUS=@status,RESPONSE_YEAR=@year," +
                      "TREATY_UNTIL_YEAR=@treaty,RESPONSE_TIME=@time," +
                      "RESPONSE_REASON=@reason,RESPONDER_TITLE=@title," +
                      "RESPONSE_YEAR_PREFIX=@prefix," +
                      "RESPONSE_STYLE=@style,RESPONSE_TONE=@tone " +
                      "WHERE PROPOSAL_ID=@id AND STATUS=@expected"
                    : "UPDATE " + DiplomacyProposalTableItem.GetTableName() +
                      " SET STATUS=@status,TREATY_UNTIL_YEAR=@treaty," +
                      "RESPONSE_REASON=@reason WHERE PROPOSAL_ID=@id AND " +
                      "STATUS=@expected";
                command.Parameters.AddWithValue("@status", StatusId(pStatus));
                command.Parameters.AddWithValue("@year", SafeYear());
                command.Parameters.AddWithValue("@treaty", pTreatyUntil);
                command.Parameters.AddWithValue("@reason", pReason ?? "");
                if (pCaptureResponseMetadata)
                {
                    command.Parameters.AddWithValue("@time", responseTime);
                    command.Parameters.AddWithValue("@title", responderTitle);
                    command.Parameters.AddWithValue("@prefix",
                        responseYearPrefix);
                    command.Parameters.AddWithValue("@style",
                        DiplomacyConversationRules.LetterStyleId(
                            responseStyle));
                    command.Parameters.AddWithValue("@tone",
                        DiplomacyConversationRules.LetterToneId(responseTone));
                }
                command.Parameters.AddWithValue("@id", pProposal.ProposalId);
                command.Parameters.AddWithValue("@expected",
                    pExpectedStatus ?? "pending");
                bool changed = command.ExecuteNonQuery() == 1;
                if (changed)
                {
                    AW3HistoryEventPublisher.PublishDiplomacy(
                        pProposal.ProposalId, "DiplomacyProposalResponse",
                        "proposal_response", pProposal.RequesterKingdomId,
                        pProposal.ResponderKingdomId, responseTime,
                        SafeYear(), responseYearPrefix, StatusId(pStatus),
                        pReason);
                    NotifyPair(pProposal.RequesterKingdomId,
                        pProposal.ResponderKingdomId);
                }
                else
                    ModClass.LogWarning("Diplomacy proposal close failed: " +
                                        "proposal=" + pProposal.ProposalId +
                                        ", target_status=" +
                                        StatusId(pStatus) +
                                        ", expected_status=" +
                                        (pExpectedStatus ?? "pending"));
                return changed;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy proposal close failed: " +
                                    "proposal=" +
                                    (pProposal?.ProposalId ?? -1L) +
                                    ", target_status=" + StatusId(pStatus) +
                                    ", error=" + exception.Message);
                return false;
            }
        }

        private static bool TryJoinProtectorToDefensiveWar(
            Kingdom pProtector, Kingdom pProtectedRealm, long pWarId)
        {
            War war = FindWar(pWarId);
            if (war?.data == null || war.hasEnded() ||
                pProtector?.data == null || pProtectedRealm?.data == null ||
                !war.isDefender(pProtectedRealm) ||
                war.isAttacker(pProtector) || war.isDefender(pProtector) ||
                HasProtectorEnemySubjectConflict(war, pProtector))
                return false;
            try
            {
                using (WarParticipantEntrySourceScope.Open(war, pProtector,
                           WarParticipantEntrySourceKind.AllianceCall,
                           pProtectedRealm))
                    war.joinDefenders(pProtector);
                return war.isDefender(pProtector) &&
                       !war.isAttacker(pProtector);
            }
            catch { return false; }
        }

        private static bool ReserveForExecution(DiplomacyProposal pProposal)
        {
            if (!Ready || pProposal == null || pProposal.ProposalId < 0)
                return false;
            try
            {
                double reservedAt = LineageService.CurTime();
                Kingdom responder = FindKingdom(
                    pProposal.ResponderKingdomId);
                string responderTitle = DiplomaticSenderTitle(responder);
                string responseYearPrefix = HistoryWriter.BuildYearPrefix(
                    reservedAt, responder);
                Kingdom requester = FindKingdom(
                    pProposal.RequesterKingdomId);
                DiplomacyLetterStyle responseStyle = ResolveLetterStyle(
                    responder, requester);
                DiplomacyLetterTone responseTone = ResolveLetterTone(
                    responder, requester);
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    DiplomacyProposalTableItem.GetTableName() +
                    " SET STATUS='processing',RESPONSE_YEAR=@year," +
                    "RESPONSE_TIME=@time,RESPONSE_REASON='processing'," +
                    "RESPONDER_TITLE=@title,RESPONSE_YEAR_PREFIX=@prefix," +
                    "RESPONSE_STYLE=@style,RESPONSE_TONE=@tone " +
                    "WHERE PROPOSAL_ID=@id AND STATUS='pending'";
                command.Parameters.AddWithValue("@id", pProposal.ProposalId);
                command.Parameters.AddWithValue("@year", SafeYear());
                command.Parameters.AddWithValue("@time", reservedAt);
                command.Parameters.AddWithValue("@title", responderTitle);
                command.Parameters.AddWithValue("@prefix",
                    responseYearPrefix);
                command.Parameters.AddWithValue("@style",
                    DiplomacyConversationRules.LetterStyleId(responseStyle));
                command.Parameters.AddWithValue("@tone",
                    DiplomacyConversationRules.LetterToneId(responseTone));
                bool reserved = command.ExecuteNonQuery() == 1;
                if (reserved)
                {
                    _nextProcessingPollTime = reservedAt;
                    NotifyPair(pProposal.RequesterKingdomId,
                        pProposal.ResponderKingdomId);
                }
                return reserved;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Diplomacy proposal reservation failed: proposal=" +
                    (pProposal?.ProposalId ?? -1L) + ", error=" +
                    exception.Message);
                return false;
            }
        }

        private static bool TryRecoverOneProcessing(double pNow)
        {
            if (_nextProcessingPollTime > pNow + WorldTimePerDay)
                _nextProcessingPollTime = pNow;
            if (_nextProcessingPollTime > pNow) return false;

            long proposalId;
            try { proposalId = FindOldestProcessingProposal(); }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Diplomacy processing recovery query failed: " +
                    exception.Message);
                _nextProcessingPollTime = pNow + WorldTimePerDay;
                return false;
            }
            if (proposalId < 0)
            {
                _nextProcessingPollTime = pNow + WorldTimePerDay;
                return false;
            }

            bool recovered = RecoverProcessingProposal(proposalId);
            _nextProcessingPollTime = recovered
                ? pNow
                : pNow + WorldTimePerDay;
            return true;
        }

        private static bool RecoverProcessingProposal(long pProposalId)
        {
            DiplomacyProposal proposal;
            try { proposal = Find(pProposalId); }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Diplomacy processing recovery read failed: proposal=" +
                    pProposalId + ", error=" + exception.Message);
                return false;
            }
            if (proposal == null || proposal.Status !=
                    DiplomacyProposalStatus.Processing)
                return true;

            int treatyUntil = -1;
            string reason;
            bool alreadyApplied =
                !DiplomacyProposalRules.IsPeaceProposal(proposal.Type) &&
                EffectAlreadyApplied(proposal, out treatyUntil);
            if (alreadyApplied ||
                Execute(proposal, out treatyUntil, out reason))
            {
                return CloseReserved(proposal,
                    DiplomacyProposalStatus.Accepted,
                    "recovered_accepted", treatyUntil);
            }

            if (string.IsNullOrEmpty(reason)) reason = "execution_failed";
            if (ShouldRetryAllianceWithdrawal(proposal, reason))
            {
                _nextProcessingPollTime =
                    LineageService.CurTime() + WorldTimePerDay;
                return false;
            }
            if (DiplomacyProposalRules.IsPeaceProposal(proposal.Type))
            {
                WarPeaceDecisionResult cancelled =
                    WarPeaceSettlementService.Instance.Cancel(
                        proposal.DetailId, reason);
                if (!cancelled.Success &&
                    (cancelled.Status ==
                         WarPeaceSettlementStatus.Accepted ||
                     cancelled.Status ==
                         WarPeaceSettlementStatus.Executing ||
                     cancelled.Status ==
                         WarPeaceSettlementStatus.TermsApplied))
                    return false;
            }
            return CloseReserved(proposal,
                DiplomacyProposalStatus.Cancelled, reason, -1);
        }

        private static bool EffectAlreadyApplied(DiplomacyProposal pProposal,
            out int pTreatyUntil)
        {
            pTreatyUntil = -1;
            Kingdom requester = FindKingdom(pProposal.RequesterKingdomId);
            Kingdom responder = FindKingdom(pProposal.ResponderKingdomId);
            if (requester?.data == null || responder?.data == null)
                return false;

            switch (pProposal.Type)
            {
                case DiplomacyProposalType.Alliance:
                    Alliance requesterAlliance = requester.getAlliance();
                    return requesterAlliance != null &&
                           requesterAlliance == responder.getAlliance();
                case DiplomacyProposalType.Peace:
                case DiplomacyProposalType.Surrender:
                case DiplomacyProposalType.EnforceDemands:
                    return FindWarBetween(requester, responder,
                        pProposal.WarId) == null;
                case DiplomacyProposalType.JoinWar:
                    War war = FindWar(pProposal.WarId);
                    if (war?.data == null || war.hasEnded()) return false;
                    return war.isAttacker(requester) &&
                           war.isAttacker(responder) ||
                           war.isDefender(requester) &&
                           war.isDefender(responder);
                case DiplomacyProposalType.Vassalize:
                    string direction = NormalizeVassalizationDetail(
                        pProposal.DetailId);
                    if (direction == DiplomacyProposalOpportunityRules
                            .VassalizeSeekDetail)
                    {
                        if (VassalService.GetSuzerain(requester) != responder)
                            return false;
                        War protectionWar = FindWar(pProposal.WarId);
                        return pProposal.WarId < 0L ||
                               protectionWar?.data == null ||
                               protectionWar.hasEnded() ||
                               protectionWar.isDefender(responder);
                    }
                    if (direction == DiplomacyProposalOpportunityRules
                            .VassalizeInternalizeDetail)
                    {
                        if (VassalService.GetSuzerain(requester) != responder)
                            return false;
                        requester.data.get(
                            LineageKeys.VASSAL_CONTRACT_TIER,
                            out int actualTier,
                            VassalContractTierRules.Outer);
                        int expectedTier =
                            DiplomacyProposalOpportunityRules
                                .InternalizationTier(
                                    requesterTributaryOfResponder: true,
                                    responderImperial:
                                    KingdomTitleService.IsEmperor(responder),
                                    responderHasMandate:
                                    MandateService.IsMandateKingdom(
                                        responder));
                        return actualTier == expectedTier;
                    }
                    return direction == DiplomacyProposalOpportunityRules
                               .VassalizeDemandDetail &&
                           VassalService.GetSuzerain(responder) == requester;
                case DiplomacyProposalType.Tributary:
                    return VassalService.GetTributarySuzerain(responder) ==
                           requester;
                case DiplomacyProposalType.EndAlliance:
                    if (SafeAllied(requester, responder)) return false;
                    return DiplomacyTreatyPersistence.HasProposalTruce(DB,
                        DiplomacyProposalTableItem.GetTableName(),
                        pProposal.ProposalId, requester.id, responder.id,
                        SafeYear(),
                        DiplomacyProposalRules.BrokenPactTruceYears,
                        out pTreatyUntil);
                case DiplomacyProposalType.EndVassal:
                    return !HasDirectSubjectRelation(requester, responder);
                default:
                    return false;
            }
        }

        private static bool ShouldRetryAllianceWithdrawal(
            DiplomacyProposal pProposal, string pReason)
        {
            if (pProposal?.Type != DiplomacyProposalType.EndAlliance ||
                pReason != "alliance_truce_write_failed") return false;
            Kingdom requester = FindKingdom(pProposal.RequesterKingdomId);
            Kingdom responder = FindKingdom(pProposal.ResponderKingdomId);
            return requester?.data != null && responder?.data != null &&
                   !SafeAllied(requester, responder);
        }

        private static bool HasDirectSubjectRelation(Kingdom pFirst,
            Kingdom pSecond)
        {
            return VassalService.GetSuzerain(pFirst) == pSecond ||
                   VassalService.GetTributarySuzerain(pFirst) == pSecond ||
                   VassalService.GetSuzerain(pSecond) == pFirst ||
                   VassalService.GetTributarySuzerain(pSecond) == pFirst;
        }

        private static long FindOldestProcessingProposal()
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT PROPOSAL_ID FROM " +
                DiplomacyProposalTableItem.GetTableName() +
                " WHERE STATUS='processing' ORDER BY RESPONSE_TIME," +
                "PROPOSAL_ID LIMIT " +
                DiplomacyProposalRules.MaximumProcessingRecoveriesPerFrame;
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? -1L
                : Convert.ToInt64(value);
        }

        private static bool TryFormOrJoinAlliance(Kingdom pRequester,
            Kingdom pResponder)
        {
            if (!string.IsNullOrEmpty(AllianceExecutionFailure(
                    pRequester, pResponder))) return false;
            Alliance requesterAlliance = pRequester.getAlliance();
            Alliance responderAlliance = pResponder.getAlliance();
            if (requesterAlliance != null && requesterAlliance ==
                responderAlliance) return true;
            if (requesterAlliance != null && responderAlliance != null)
                return false;
            Alliance alliance = requesterAlliance ?? responderAlliance;
            Kingdom joining = requesterAlliance == null
                ? pRequester
                : pResponder;
            if (alliance != null)
                return alliance.join(joining);
            return World.world.alliances.newAlliance(pRequester,
                       pResponder)?.data != null;
        }

        private static string AllianceExecutionFailure(Kingdom pRequester,
            Kingdom pResponder)
        {
            try
            {
                if (pRequester?.data == null || pResponder?.data == null)
                    return "alliance_unavailable";
                Alliance requesterAlliance = pRequester?.getAlliance();
                Alliance responderAlliance = pResponder?.getAlliance();
                if (requesterAlliance != null &&
                    requesterAlliance == responderAlliance) return "";
                WorldTile requesterCapital = pRequester.capital?.getTile();
                WorldTile responderCapital = pResponder.capital?.getTile();
                bool hasBothCapitals = requesterCapital != null &&
                                       responderCapital != null;
                float capitalDistance = hasBothCapitals
                    ? Toolbox.DistTile(requesterCapital, responderCapital)
                    : float.PositiveInfinity;
                string distanceFailure =
                    DiplomacyProposalRules.AllianceDistanceFailure(
                        KingdomAdjacency.AreDirectNeighbors(pRequester,
                            pResponder), hasBothCapitals, capitalDistance);
                if (!string.IsNullOrEmpty(distanceFailure))
                    return distanceFailure;
                if (requesterAlliance != null && responderAlliance != null &&
                    requesterAlliance != responderAlliance)
                    return "alliance_conflict";
                Alliance alliance = requesterAlliance ?? responderAlliance;
                if (alliance == null) return "";
                Kingdom joining = requesterAlliance == null
                    ? pRequester
                    : pResponder;
                return joining?.data != null && alliance.canJoin(joining)
                    ? ""
                    : "alliance_members_refuse";
            }
            catch
            {
                return "alliance_unavailable";
            }
        }

        private static DiplomacyProposal FindOldestPendingIncoming(
            long pResponderId)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT " + ProposalSelectColumns +
                " FROM " + DiplomacyProposalTableItem.GetTableName() +
                " WHERE RESPONDER_KINGDOM_ID=@id AND STATUS='pending' " +
                "ORDER BY CREATED_TIME,PROPOSAL_ID LIMIT 1";
            command.Parameters.AddWithValue("@id", pResponderId);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadProposal(reader) : null;
        }

        private static long FindDuePendingProposal(double pNow)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT PROPOSAL_ID FROM " +
                DiplomacyProposalTableItem.GetTableName() +
                " WHERE STATUS='pending' AND RESPONSE_DUE_TIME>=0 AND " +
                "RESPONSE_DUE_TIME<=@now ORDER BY RESPONSE_DUE_TIME," +
                "PROPOSAL_ID LIMIT 1";
            command.Parameters.AddWithValue("@now", pNow);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? -1L
                : Convert.ToInt64(value);
        }

        private sealed class PreparedAiProposal
        {
            public DiplomacyProposalAiCandidate Candidate;
            public DiplomacyProposalSelection Selection;
            public long WarId = -1L;
        }

        private sealed class JoinWarCandidateFacts
        {
            public War War;
            public bool CapitalThreatened;
            public bool Losing;
            public float EnemyPower;
        }

        private sealed class PreparedWarSettlement
        {
            public War War;
            public Kingdom Opponent;
            public DiplomacyProposalType Type;
            public WarSettlementSelectionCandidate Candidate;
            public WarPeaceSettlementScopeKind Scope =
                WarPeaceSettlementScopeKind.Coalition;
            public long ExitRootKingdomId = -1L;
        }

        public static bool TryScheduleWarPeace(Kingdom pRequester)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession || !Ready ||
                pRequester?.data == null || pRequester.isRekt() ||
                pRequester.isNeutral()) return false;
            int year = SafeYear();
            if (ProposalRuntime.WasWarSettlementAssessed(pRequester.id,
                    year))
                return false;
            ProposalRuntime.MarkWarSettlementAssessed(pRequester.id, year);
            bool created = TryCreatePreparedWarSettlement(pRequester, year);
            if (!created) return false;
            pRequester.data.set(
                LineageKeys.DIPLOMACY_AI_LAST_PROPOSAL_YEAR, year);
            KingdomStrategyRevisionService.MarkChanged(pRequester.id);
            return true;
        }

        public static bool IsProtectedWar(War pWar)
        {
            if (pWar?.data == null ||
                LineageArchiveManager.Instance == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful)
                return true;
            string type;
            try { type = pWar.getAsset()?.id ?? pWar.data.war_type ?? ""; }
            catch { type = pWar.data.war_type ?? ""; }
            Kingdom attacker = pWar.main_attacker;
            Kingdom defender = pWar.main_defender;
            bool mandateConquest = attacker?.data != null &&
                defender?.data != null &&
                MandateService.GetCurrentMandateKingdom() == attacker &&
                WarTerritoryService.CanUseMandateConquest(attacker,
                    defender);
            bool protectedGoal = WarTerritoryService.HasOpenGoalType(
                pWar.data.id, WarTerritoryService.GOAL_TAKE_MANDATE,
                WarTerritoryService.GOAL_MANDATE_CONQUEST,
                WarTerritoryService.GOAL_INDEPENDENCE);
            bool authoritativeRebellion;
            try
            {
                authoritativeRebellion =
                    pWar.getAsset()?.rebellion == true;
            }
            catch { authoritativeRebellion = false; }
            return WarPeaceProtectionRules.IsProtected(type,
                mandateConquest, protectedGoal,
                pAuthoritativeRebellion: authoritativeRebellion);
        }

        private static bool TryCreateBoundedAiProposal(Kingdom pRequester,
            int pYear)
        {
            if (!TryPrepareOrdinaryAiProposals(pRequester, pYear,
                    out Kingdom contact,
                    out List<PreparedAiProposal> candidates)) return false;
            return TryCreatePreparedOrdinary(pRequester, contact, candidates);
        }

        private static bool TryCreatePreparedWarSettlement(
            Kingdom pRequester, int pYear)
        {
            PreparedWarSettlement warSettlement =
                SelectBoundedWarSettlement(pRequester, pYear);
            if (warSettlement?.War?.data == null ||
                warSettlement.Opponent?.data == null) return false;
            if (warSettlement.Scope ==
                WarPeaceSettlementScopeKind.SeparateParticipant)
                return TryCreatePreparedSeparatePeace(pRequester,
                    warSettlement);
            return warSettlement.Type != DiplomacyProposalType.None &&
                   TryCreateSelected(pRequester, warSettlement.Opponent,
                       warSettlement.Type, pPlayerInitiated: false,
                       warSettlement.War.data.id,
                       DiplomacyProposalSelection.Empty, out _, out _);
        }

        private static bool TryCreatePreparedSeparatePeace(
            Kingdom pRequester, PreparedWarSettlement pSettlement)
        {
            if (pSettlement?.War?.data == null ||
                pSettlement.Opponent?.data == null ||
                pSettlement.Type == DiplomacyProposalType.None ||
                pSettlement.ExitRootKingdomId < 0L) return false;
            if (!TryPrepareDefaultPeaceSettlement(pRequester,
                    pSettlement.Opponent, pSettlement.Type,
                    pSettlement.War, pPlayerInitiated: false,
                    WarPeaceSettlementScopeKind.SeparateParticipant,
                    pSettlement.ExitRootKingdomId, out string detailId,
                    out _)) return false;
            var selection = new DiplomacyProposalSelection(-1L, -1L,
                -1L, -1L, detailId);
            bool created = TryCreateSelected(pRequester,
                pSettlement.Opponent, pSettlement.Type,
                pPlayerInitiated: false, pSettlement.War.data.id,
                selection, out _, out _);
            if (!created)
                WarPeaceSettlementService.Instance.Cancel(detailId,
                    "outer_proposal_not_created");
            return created;
        }

        private static bool TryPrepareOrdinaryAiProposals(
            Kingdom pRequester, int pYear, out Kingdom pContact,
            out List<PreparedAiProposal> pCandidates)
        {
            return TryBuildOrdinaryAiProposals(pRequester, pYear,
                NextDiplomacyContact(pRequester), out pContact,
                out pCandidates);
        }

        private static bool TryPrepareOrdinaryAiProposalsReadOnly(
            Kingdom pRequester, int pYear, MandateReport pMandateReport,
            out Kingdom pContact,
            out List<PreparedAiProposal> pCandidates,
            out AsyncDiplomacySelectionTargetFacts[] pSelectionTargets)
        {
            return TryBuildOrdinaryAiProposalsReadOnly(pRequester, pYear,
                PeekDiplomacyContact(pRequester), pMandateReport,
                out pContact, out pCandidates, out pSelectionTargets);
        }

        private static bool TryBuildOrdinaryAiProposals(
            Kingdom pRequester, int pYear, Kingdom pContact,
            out Kingdom pResolvedContact,
            out List<PreparedAiProposal> pCandidates)
        {
            pResolvedContact = pContact;
            pCandidates = new List<PreparedAiProposal>(12);
            Kingdom contact = pResolvedContact;
            if (contact?.data == null) return false;
            int opinion = DiplomacyOpinionService.Read(contact, pRequester);
            if (HasActiveNonAggression(pRequester, contact) &&
                DiplomacyProposalRules.ShouldBreakNonAggression(opinion,
                    pRequester.power, contact.power))
            {
                pCandidates.Add(new PreparedAiProposal
                {
                    Candidate = new DiplomacyProposalAiCandidate(
                        DiplomacyProposalType.BreakNonAggression, true,
                        opinion, 1f, false, 0f, false,
                        targetKingdomId: contact.id),
                    Selection = DiplomacyProposalSelection.Empty
                });
                return true;
            }

            float requesterPowerRatio = Math.Max(1, pRequester.power) /
                                        (float)Math.Max(1, contact.power);
            if (TryPrepareJoinWarCandidate(pRequester, contact, opinion,
                    requesterPowerRatio, out PreparedAiProposal joinWar))
                pCandidates.Add(joinWar);
            if (AWPerformanceSettings.EnableAiVassalActions &&
                TryPrepareVassalizationCandidate(pRequester, contact,
                    opinion, requesterPowerRatio,
                    out PreparedAiProposal vassalization))
                pCandidates.Add(vassalization);
            if (TryPrepareEndVassalCandidate(pRequester, contact, opinion,
                    requesterPowerRatio,
                    out PreparedAiProposal endVassal))
                pCandidates.Add(endVassal);
            if (TryPrepareEndAllianceCandidate(pRequester, contact, opinion,
                    requesterPowerRatio,
                    out PreparedAiProposal endAlliance))
                pCandidates.Add(endAlliance);
            if (AWPerformanceSettings.EnableAiVassalActions &&
                pRequester.power >= Math.Max(1, contact.power) * 2.4f &&
                opinion >= 0)
                AddOrdinaryAiCandidate(pCandidates, pRequester, contact,
                    DiplomacyProposalType.Tributary, opinion,
                    requesterPowerRatio);
            if (AWPerformanceSettings.EnableAiAllianceActions &&
                opinion >= 60)
                AddOrdinaryAiCandidate(pCandidates, pRequester, contact,
                    DiplomacyProposalType.Alliance, opinion,
                    requesterPowerRatio);
            if (opinion >= 30)
                AddOrdinaryAiCandidate(pCandidates, pRequester, contact,
                    DiplomacyProposalType.NonAggression, opinion,
                    requesterPowerRatio);

            if (opinion >= 20)
            {
                DiplomacyActionAssessment marriageAssessment =
                    AssessRoyalMarriageWithPreview(pRequester, contact,
                        out DiplomaticMarriagePreview marriage);
                if (ExpectedAccepted(marriageAssessment))
                    pCandidates.Add(new PreparedAiProposal
                    {
                        Candidate = new DiplomacyProposalAiCandidate(
                            DiplomacyProposalType.RoyalMarriage, true,
                            opinion, requesterPowerRatio,
                            marriage.DirectRoyalMarriage, 0f, false,
                            targetKingdomId: contact.id),
                        Selection = new DiplomacyProposalSelection(-1L,
                            marriage.RequesterActorId,
                            marriage.ResponderActorId, -1L,
                            marriage.DirectRoyalMarriage
                                ? "direct"
                                : "collateral")
                    });
            }

            bool upperSubject = GetAnySuzerain(contact) == pRequester;
            PreparedAiProposal upperHousehold = null;
            bool upperHouseholdOfferAdded = upperSubject &&
                TryPrepareUpperRealmHouseholdCandidate(pRequester, contact,
                    opinion, requesterPowerRatio,
                    requesterSuzerainOfResponder: upperSubject,
                    out upperHousehold);
            if (upperHouseholdOfferAdded)
                pCandidates.Add(upperHousehold);

            PreparedAiProposal consortRequest = null;
            bool consortRequestAdded = !upperSubject && opinion >=
                RulerHouseholdRules.MinimumConsortRequestOpinion &&
                TryPrepareConsortRequestCandidate(pRequester, contact,
                    opinion, requesterPowerRatio, out consortRequest);
            if (consortRequestAdded) pCandidates.Add(consortRequest);

            if (!upperSubject && !consortRequestAdded && opinion >= 20 &&
                TryPrepareHouseholdCandidate(pRequester, contact, opinion,
                    requesterPowerRatio, out PreparedAiProposal household))
                pCandidates.Add(household);

            if (opinion >= 10 && TryPrepareCoalitionCandidate(pRequester,
                    contact, pYear, opinion, requesterPowerRatio,
                    out PreparedAiProposal coalition))
                pCandidates.Add(coalition);
            return pCandidates.Count > 0;
        }

        private static bool TryBuildOrdinaryAiProposalsReadOnly(
            Kingdom pRequester, int pYear, Kingdom pContact,
            MandateReport pMandateReport, out Kingdom pResolvedContact,
            out List<PreparedAiProposal> pCandidates,
            out AsyncDiplomacySelectionTargetFacts[] pSelectionTargets)
        {
            pResolvedContact = pContact;
            pCandidates = new List<PreparedAiProposal>(12);
            pSelectionTargets =
                Array.Empty<AsyncDiplomacySelectionTargetFacts>();
            Kingdom contact = pResolvedContact;
            if (contact?.data == null) return false;
            int opinion = DiplomacyOpinionService.Read(contact, pRequester);
            if (HasActiveNonAggression(pRequester, contact) &&
                DiplomacyProposalRules.ShouldBreakNonAggression(opinion,
                    pRequester.power, contact.power))
            {
                pCandidates.Add(new PreparedAiProposal
                {
                    Candidate = new DiplomacyProposalAiCandidate(
                        DiplomacyProposalType.BreakNonAggression, true,
                        opinion, 1f, false, 0f, false,
                        targetKingdomId: contact.id),
                    Selection = DiplomacyProposalSelection.Empty
                });
                return true;
            }

            float requesterPowerRatio = Math.Max(1, pRequester.power) /
                                        (float)Math.Max(1, contact.power);
            if (TryPrepareJoinWarCandidateReadOnly(pRequester, contact,
                    opinion, requesterPowerRatio, pMandateReport,
                    out PreparedAiProposal joinWar))
                pCandidates.Add(joinWar);
            if (AWPerformanceSettings.EnableAiVassalActions &&
                TryPrepareVassalizationCandidateReadOnly(pRequester,
                    contact, opinion, requesterPowerRatio, pMandateReport,
                    out PreparedAiProposal vassalization))
                pCandidates.Add(vassalization);
            if (TryPrepareEndVassalCandidateReadOnly(pRequester, contact,
                    opinion, requesterPowerRatio, pMandateReport,
                    out PreparedAiProposal endVassal))
                pCandidates.Add(endVassal);
            if (TryPrepareEndAllianceCandidate(pRequester, contact, opinion,
                    requesterPowerRatio,
                    out PreparedAiProposal endAlliance))
                pCandidates.Add(endAlliance);
            if (AWPerformanceSettings.EnableAiVassalActions &&
                pRequester.power >= Math.Max(1, contact.power) * 2.4f &&
                opinion >= 0)
                AddOrdinaryAiCandidateReadOnly(pCandidates, pRequester,
                    contact, DiplomacyProposalType.Tributary, opinion,
                    requesterPowerRatio, pMandateReport);
            if (AWPerformanceSettings.EnableAiAllianceActions &&
                opinion >= 60)
                AddOrdinaryAiCandidateReadOnly(pCandidates, pRequester,
                    contact, DiplomacyProposalType.Alliance, opinion,
                    requesterPowerRatio, pMandateReport);
            if (opinion >= 30)
                AddOrdinaryAiCandidateReadOnly(pCandidates, pRequester,
                    contact, DiplomacyProposalType.NonAggression, opinion,
                    requesterPowerRatio, pMandateReport);

            if (opinion >= 20)
            {
                DiplomacyActionAssessment marriageAssessment =
                    AssessRoyalMarriageWithPreviewReadOnly(pRequester,
                        contact, out DiplomaticMarriagePreview marriage,
                        pMandateReport);
                if (ExpectedAccepted(marriageAssessment))
                    pCandidates.Add(new PreparedAiProposal
                    {
                        Candidate = new DiplomacyProposalAiCandidate(
                            DiplomacyProposalType.RoyalMarriage, true,
                            opinion, requesterPowerRatio,
                            marriage.DirectRoyalMarriage, 0f, false,
                            targetKingdomId: contact.id),
                        Selection = new DiplomacyProposalSelection(-1L,
                            marriage.RequesterActorId,
                            marriage.ResponderActorId, -1L,
                            marriage.DirectRoyalMarriage
                                ? "direct"
                                : "collateral")
                    });
            }


            bool upperSubject = GetAnySuzerain(contact) == pRequester;
            PreparedAiProposal upperHousehold = null;
            bool upperHouseholdOfferAdded = upperSubject &&
                TryPrepareUpperRealmHouseholdCandidateReadOnly(pRequester,
                    contact, opinion, requesterPowerRatio, pMandateReport,
                    requesterSuzerainOfResponder: upperSubject,
                    out upperHousehold);
            if (upperHouseholdOfferAdded)
                pCandidates.Add(upperHousehold);

            PreparedAiProposal consortRequest = null;
            bool consortRequestAdded = !upperSubject && opinion >=
                RulerHouseholdRules.MinimumConsortRequestOpinion &&
                TryPrepareConsortRequestCandidateReadOnly(pRequester,
                    contact, opinion, requesterPowerRatio, pMandateReport,
                    out consortRequest);
            if (consortRequestAdded) pCandidates.Add(consortRequest);

            if (!upperSubject && !consortRequestAdded && opinion >= 20 &&
                TryPrepareHouseholdCandidateReadOnly(pRequester, contact,
                    opinion, requesterPowerRatio, pMandateReport,
                    out PreparedAiProposal household))
                pCandidates.Add(household);

            if (opinion >= 10)
            {
                bool coalitionReady = TryPrepareCoalitionCandidateReadOnly(
                    pRequester, contact, pYear, opinion,
                    requesterPowerRatio, pMandateReport,
                    out PreparedAiProposal coalition,
                    out pSelectionTargets);
                if (coalitionReady) pCandidates.Add(coalition);
            }
            return pCandidates.Count > 0;
        }

        private static string SummarizePreparedAiProposals(Kingdom pContact,
            IReadOnlyList<PreparedAiProposal> pCandidates)
        {
            if (pContact?.data == null || pCandidates == null ||
                pCandidates.Count == 0) return "none";
            var remaining = new List<PreparedAiProposal>(pCandidates);
            var trace = new List<AsyncStrategyCandidate>(remaining.Count);
            while (remaining.Count > 0)
            {
                var facts = new List<DiplomacyProposalAiCandidate>(
                    remaining.Count);
                for (int index = 0; index < remaining.Count; index++)
                    facts.Add(remaining[index].Candidate);
                DiplomacyProposalAiCandidate best =
                    DiplomacyProposalAiRules.SelectBest(facts);
                int selected = FindPreparedCandidate(remaining, best);
                if (selected < 0) break;
                PreparedAiProposal prepared = remaining[selected];
                remaining.RemoveAt(selected);
                AsyncDiplomacyProposalKind kind = AsyncKind(
                    prepared.Candidate.Type);
                if (kind == AsyncDiplomacyProposalKind.None) continue;
                trace.Add(new AsyncStrategyCandidate(pContact.id,
                    AsyncStrategyAction.DiplomacyProposal, kind,
                    DiplomacyProposalAiRules.Score(prepared.Candidate), 0d));
            }
            return AsyncStrategyShadowRules.SummarizeDecisions(trace);
        }

        private static bool TryCreatePreparedOrdinary(Kingdom pRequester,
            Kingdom pContact, List<PreparedAiProposal> pCandidates)
        {
            List<PreparedAiProposal> candidates = pCandidates;
            Kingdom contact = pContact;
            while (candidates.Count > 0)
            {
                var facts = new List<DiplomacyProposalAiCandidate>(
                    candidates.Count);
                for (int i = 0; i < candidates.Count; i++)
                    facts.Add(candidates[i].Candidate);
                DiplomacyProposalAiCandidate best =
                    DiplomacyProposalAiRules.SelectBest(facts);
                int index = FindPreparedCandidate(candidates, best);
                if (index < 0) return false;
                PreparedAiProposal prepared = candidates[index];
                candidates.RemoveAt(index);
                bool created = TryCreateSelected(pRequester, contact,
                    prepared.Candidate.Type, pPlayerInitiated: false,
                    prepared.WarId, prepared.Selection, out _, out _);
                if (created) return true;
            }
            return false;
        }

        internal static bool TryCaptureAsyncProposal(Kingdom pRequester,
            int pRequestedYear, out KingdomStrategyFacts pSource,
            out AsyncDiplomacyProposalFacts[] pFacts,
            out AsyncDiplomacyCommitCandidate[] pCommitCandidates,
            out AsyncDiplomacySelectionTargetFacts[] pSelectionTargets)
        {
            pSource = default;
            pFacts = Array.Empty<AsyncDiplomacyProposalFacts>();
            pCommitCandidates = Array.Empty<AsyncDiplomacyCommitCandidate>();
            pSelectionTargets =
                Array.Empty<AsyncDiplomacySelectionTargetFacts>();
            if (AW3MultiplayerReplicaScope.IsReplicaSession || !Ready ||
                pRequester?.data == null || pRequester.isRekt() ||
                pRequester.isNeutral() || SafeYear() != pRequestedYear)
                return false;
            if (!GeneralAiProposalCooldownReady(pRequester, pRequestedYear))
                return false;
            MandateReport mandateReport = MandateService.ReadReportReadOnly();
            if (!TryPrepareOrdinaryAiProposalsReadOnly(pRequester,
                    pRequestedYear, mandateReport,
                    out Kingdom contact,
                    out List<PreparedAiProposal> prepared,
                    out pSelectionTargets)) return false;

            return TryBuildAsyncProposalCapture(pRequester, pRequestedYear,
                contact, prepared, out pSource, out pFacts,
                out pCommitCandidates);
        }

        private static bool TryCaptureCurrentAsyncProposal(
            Kingdom pRequester, Kingdom pContact, int pRequestedYear,
            out KingdomStrategyFacts pSource,
            out AsyncDiplomacyProposalFacts[] pFacts,
            out AsyncDiplomacyCommitCandidate[] pCommitCandidates,
            out AsyncDiplomacySelectionTargetFacts[] pSelectionTargets)
        {
            pSource = default;
            pFacts = Array.Empty<AsyncDiplomacyProposalFacts>();
            pCommitCandidates = Array.Empty<AsyncDiplomacyCommitCandidate>();
            pSelectionTargets =
                Array.Empty<AsyncDiplomacySelectionTargetFacts>();
            if (pRequester?.data == null || pContact?.data == null ||
                pRequester.isRekt() || pContact.isRekt() ||
                pRequester.isNeutral() || pContact.isNeutral() ||
                SafeYear() != pRequestedYear)
                return false;
            MandateReport mandateReport = MandateService.ReadReportReadOnly();
            if (!TryBuildOrdinaryAiProposalsReadOnly(pRequester,
                    pRequestedYear, pContact, mandateReport,
                    out Kingdom resolvedContact,
                    out List<PreparedAiProposal> prepared,
                    out pSelectionTargets) || resolvedContact != pContact)
                return false;
            return TryBuildAsyncProposalCapture(pRequester, pRequestedYear,
                pContact, prepared, out pSource, out pFacts,
                out pCommitCandidates);
        }

        private static bool TryBuildAsyncProposalCapture(
            Kingdom pRequester, int pRequestedYear, Kingdom pContact,
            IReadOnlyList<PreparedAiProposal> pPrepared,
            out KingdomStrategyFacts pSource,
            out AsyncDiplomacyProposalFacts[] pFacts,
            out AsyncDiplomacyCommitCandidate[] pCommitCandidates)
        {
            pSource = default;
            pFacts = Array.Empty<AsyncDiplomacyProposalFacts>();
            pCommitCandidates = Array.Empty<AsyncDiplomacyCommitCandidate>();
            if (pRequester?.data == null || pContact?.data == null ||
                pPrepared == null) return false;

            CourtSnapshot court = CourtService.GetSnapshot(pRequester);
            Kingdom root = VassalService.GetRootSuzerain(pRequester);
            pSource = new KingdomStrategyFacts(pRequester.id,
                Math.Max(1f, pRequester.power), court?.war ?? .5f,
                court?.peace ?? .5f, court?.aggression ?? .5f,
                root?.id ?? pRequester.id);
            bool activeBlocker = HasPendingPair(pRequester.id, pContact.id);
            var facts = new List<AsyncDiplomacyProposalFacts>(pPrepared.Count);
            var commits = new List<AsyncDiplomacyCommitCandidate>(
                pPrepared.Count);
            for (int index = 0; index < pPrepared.Count; index++)
            {
                PreparedAiProposal item = pPrepared[index];
                AsyncDiplomacyProposalKind kind = AsyncKind(
                    item.Candidate.Type);
                if (kind == AsyncDiplomacyProposalKind.None) continue;
                bool cooldown = HasRecentAiRejectionForSelection(
                    pRequester.id, pContact.id, item.Candidate.Type,
                    item.Selection.DetailId, pRequestedYear);
                facts.Add(new AsyncDiplomacyProposalFacts(pContact.id, kind,
                    DiplomacyProposalAiRules.Score(item.Candidate),
                    activeBlocker && !DiplomacyProposalRules.IsUnilateral(
                        item.Candidate.Type), cooldown));
                commits.Add(new AsyncDiplomacyCommitCandidate(pContact.id,
                    item.Candidate.Type, kind, item.WarId,
                    item.Selection));
            }
            pFacts = facts.ToArray();
            pCommitCandidates = commits.ToArray();
            return pFacts.Length > 0;
        }

        internal static AsyncDiplomacySelectionIdentity[]
            BuildSelectionIdentities(
                IReadOnlyList<AsyncDiplomacyCommitCandidate> pCandidates)
        {
            if (pCandidates == null || pCandidates.Count == 0)
                return Array.Empty<AsyncDiplomacySelectionIdentity>();
            var result = new AsyncDiplomacySelectionIdentity[
                pCandidates.Count];
            for (int index = 0; index < pCandidates.Count; index++)
            {
                AsyncDiplomacyCommitCandidate candidate = pCandidates[index];
                result[index] = candidate.Identity;
            }
            return result;
        }

        internal static bool TryCommitAsyncProposal(
            AsyncStrategyPlan pPlan,
            IReadOnlyList<AsyncDiplomacyCommitCandidate> pCandidates,
            long pCurrentTick, bool pShadowOnly)
        {
            if (pPlan == null || pCandidates == null ||
                pPlan.Action != AsyncStrategyAction.DiplomacyProposal ||
                !AsyncStrategyPlanRules.Accept(pPlan,
                    AncientWarfare3.core.asyncwork.AWAsyncRuntime
                        .WorldGeneration,
                    KingdomStrategyRevisionService.Current, SafeYear(),
                    pCurrentTick,
                    maxAgeTicks: 600L)) return false;
            AsyncDiplomacyCommitCandidate selected = default;
            bool found = false;
            for (int index = 0; index < pCandidates.Count; index++)
            {
                AsyncDiplomacyCommitCandidate candidate = pCandidates[index];
                if (candidate.ResponderKingdomId != pPlan.TargetKingdomId ||
                    candidate.Kind != pPlan.ProposalKind) continue;
                selected = candidate;
                found = true;
                break;
            }
            if (!found) return false;
            Kingdom requester = FindKingdom(pPlan.SourceKingdomId);
            Kingdom responder = FindKingdom(selected.ResponderKingdomId);
            if (requester?.data == null || responder?.data == null ||
                requester.isRekt() || responder.isRekt() ||
                requester.isNeutral() || responder.isNeutral()) return false;
            int year = pPlan.CaptureYear;
            if (pPlan.FactFingerprint == null ||
                !TryCaptureCurrentAsyncProposal(requester, responder, year,
                    out KingdomStrategyFacts currentSource,
                    out AsyncDiplomacyProposalFacts[] currentFacts,
                    out AsyncDiplomacyCommitCandidate[]
                        currentCommitCandidates,
                    out AsyncDiplomacySelectionTargetFacts[]
                        currentSelectionTargets) ||
                !pPlan.FactFingerprint.MatchesDiplomacy(currentSource,
                    currentFacts, currentSelectionTargets,
                    BuildSelectionIdentities(currentCommitCandidates)))
                return false;
            AsyncDiplomacyCommitCandidate currentSelected = default;
            bool foundCurrent = false;
            for (int index = 0; index < currentCommitCandidates.Length;
                 index++)
            {
                AsyncDiplomacyCommitCandidate candidate =
                    currentCommitCandidates[index];
                if (candidate.ResponderKingdomId != pPlan.TargetKingdomId ||
                    candidate.Kind != pPlan.ProposalKind) continue;
                currentSelected = candidate;
                foundCurrent = true;
                break;
            }
            if (!foundCurrent ||
                !selected.Identity.Matches(currentSelected.Identity))
                return false;
            if (HasRecentAiRejectionForSelection(requester.id,
                    responder.id, currentSelected.Type,
                    currentSelected.Selection.DetailId, year)) return false;
            DiplomacyActionAssessment assessment = AssessWithSelection(
                requester, responder, currentSelected.Type,
                currentSelected.WarId,
                currentSelected.Selection);
            if (assessment?.Allowed != true ||
                !DiplomacyProposalRules.IsUnilateral(
                    currentSelected.Type) &&
                !ExpectedAccepted(assessment)) return false;
            if (pShadowOnly) return false;
            bool created = TryCreateSelected(requester, responder,
                currentSelected.Type, pPlayerInitiated: false,
                currentSelected.WarId, currentSelected.Selection,
                out _, out _);
            if (!created) return false;
            requester.data.set(
                LineageKeys.DIPLOMACY_AI_LAST_PROPOSAL_YEAR, year);
            KingdomStrategyRevisionService.MarkChanged(requester.id,
                responder.id);
            return true;
        }

        private static AsyncDiplomacyProposalKind AsyncKind(
            DiplomacyProposalType pType)
        {
            return pType switch
            {
                DiplomacyProposalType.Alliance =>
                    AsyncDiplomacyProposalKind.Alliance,
                DiplomacyProposalType.NonAggression =>
                    AsyncDiplomacyProposalKind.NonAggression,
                DiplomacyProposalType.RoyalMarriage =>
                    AsyncDiplomacyProposalKind.RoyalMarriage,
                DiplomacyProposalType.Tributary =>
                    AsyncDiplomacyProposalKind.Tributary,
                DiplomacyProposalType.Truce =>
                    AsyncDiplomacyProposalKind.Truce,
                DiplomacyProposalType.EndAlliance =>
                    AsyncDiplomacyProposalKind.EndAlliance,
                DiplomacyProposalType.BreakNonAggression =>
                    AsyncDiplomacyProposalKind.BreakNonAggression,
                DiplomacyProposalType.Coalition =>
                    AsyncDiplomacyProposalKind.Coalition,
                DiplomacyProposalType.HouseholdOffering =>
                    AsyncDiplomacyProposalKind.HouseholdOffering,
                DiplomacyProposalType.JoinWar =>
                    AsyncDiplomacyProposalKind.JoinWar,
                DiplomacyProposalType.Vassalize =>
                    AsyncDiplomacyProposalKind.Vassalize,
                DiplomacyProposalType.EndVassal =>
                    AsyncDiplomacyProposalKind.EndVassal,
                _ => AsyncDiplomacyProposalKind.None
            };
        }

        private static PreparedWarSettlement SelectBoundedWarSettlement(
            Kingdom pRequester, int pYear)
        {
            var prepared = new List<PreparedWarSettlement>(
                DiplomacyProposalAiRules.MaximumWarSettlementAssessments);
            var candidates = new List<WarSettlementSelectionCandidate>(
                DiplomacyProposalAiRules.MaximumWarSettlementAssessments);
            try
            {
                IReadOnlyList<War> scannedWars =
                    WarSettlementCursor(pRequester).Take(
                        DiplomacyProposalAiRules.
                            MaximumWarSettlementScanBudget);
                List<War> activeWars = OrderBoundedWarsByAge(scannedWars);
                int activeWarCount = activeWars.Count;
                int assessmentCount = Math.Min(activeWarCount,
                    DiplomacyProposalAiRules.
                        MaximumWarSettlementAssessments);
                for (int warIndex = 0; warIndex < assessmentCount;
                     warIndex++)
                {
                    War war = activeWars[warIndex];
                    bool protectedWar = IsProtectedWar(war);
                    if (war?.data == null || war.hasEnded() || protectedWar)
                        continue;
                    Kingdom opponent = FindOpponent(war, pRequester);
                    if (opponent?.data == null ||
                        !WarScoreService.TryGetSnapshot(war, pRequester,
                            out WarScoreSnapshot snapshot) ||
                        !WarParticipantRosterService.TryBuildReadOnly(war,
                            -1L, out WarParticipantRosterContext roster,
                            out _))
                        continue;
                    WarSettlementAiFacts facts = BuildWarSettlementFacts(
                        pRequester, opponent, war);
                    WarSettlementPosition position =
                        DiplomacyProposalAiRules
                            .ResolvePositionFromSignedWarScore(snapshot.Score);
                    WarSettlementAiDecision decision =
                        DiplomacyProposalAiRules.SelectWarSettlement(facts,
                            position,
                            (float)Rng.NextDouble());
                    decision = DiplomacyProposalAiRules.
                        ApplyMultiWarPeacePressure(decision,
                            activeWarCount, facts.WarYears, position);
                    bool warLeader = IsWarLeader(war, pRequester);
                    bool totalWar;
                    try { totalWar = war.isTotalWar(); }
                    catch { totalWar = false; }
                    if (!warLeader)
                    {
                        PreparedWarSettlement separate =
                            BuildParticipantSeparatePeaceCandidate(war,
                                pRequester, opponent, roster, snapshot,
                                facts, position, decision, totalWar,
                                protectedWar, pYear);
                        AddWarSettlementCandidate(prepared, candidates,
                            separate);
                        continue;
                    }

                    DiplomacyProposalType coalitionType = decision switch
                    {
                        WarSettlementAiDecision.Surrender =>
                            DiplomacyProposalType.Surrender,
                        WarSettlementAiDecision.Peace =>
                            DiplomacyProposalType.Peace,
                        WarSettlementAiDecision.EnforceDemands =>
                            DiplomacyProposalType.EnforceDemands,
                        _ => DiplomacyProposalType.None
                    };
                    DiplomacyActionAssessment assessment = coalitionType ==
                            DiplomacyProposalType.None
                        ? null
                        : Assess(pRequester, opponent, coalitionType,
                            war.data.id,
                            pIgnorePending: true);
                    bool rejectionCooldown = coalitionType !=
                        DiplomacyProposalType.None && HasRecentAiRejection(
                            pRequester.id, opponent.id, coalitionType, pYear);
                    bool requesterReadyForPeace =
                        DiplomacyProposalAiRules.IsReadyToAcceptPeace(facts,
                            position);
                    bool coalitionEligible = IsWarLeaderPair(war,
                            pRequester, opponent) &&
                        !HasPendingPair(pRequester.id, opponent.id) &&
                        DiplomacyProposalAiRules
                        .CanQueueWarSettlementProposal(decision,
                            requesterReadyForPeace,
                            assessment?.Allowed == true,
                            rejectionCooldown);
                    if (coalitionType != DiplomacyProposalType.None)
                        AddWarSettlementCandidate(prepared, candidates,
                            new PreparedWarSettlement
                            {
                                War = war,
                                Opponent = opponent,
                                Type = coalitionType,
                                Candidate =
                                    new WarSettlementSelectionCandidate(
                                        coalitionEligible, decision,
                                        DiplomacyProposalAiRules.
                                            SettlementUrgency(facts,
                                                decision, snapshot.Score),
                                        warYears: facts.WarYears)
                            });

                    if (prepared.Count >= DiplomacyProposalAiRules.
                            MaximumWarSettlementAssessments) continue;
                    PreparedWarSettlement targeted =
                        SelectLeaderSeparatePeaceCandidate(war, pRequester,
                            roster, snapshot, position, facts.WarYears,
                            totalWar, protectedWar, pYear);
                    AddWarSettlementCandidate(prepared, candidates,
                        targeted);
                }
            }
            catch { }
            int selected = DiplomacyProposalAiRules
                .SelectBestWarSettlementIndex(candidates);
            return selected >= 0 && selected < prepared.Count
                ? prepared[selected]
                : null;
        }

        private static void AddWarSettlementCandidate(
            ICollection<PreparedWarSettlement> pPrepared,
            ICollection<WarSettlementSelectionCandidate> pCandidates,
            PreparedWarSettlement pCandidate)
        {
            if (pCandidate == null || pPrepared.Count >=
                DiplomacyProposalAiRules.MaximumWarSettlementAssessments)
                return;
            pPrepared.Add(pCandidate);
            pCandidates.Add(pCandidate.Candidate);
        }

        private static PreparedWarSettlement
            BuildParticipantSeparatePeaceCandidate(War pWar,
                Kingdom pRequester, Kingdom pOpponent,
                WarParticipantRosterContext pRoster,
                WarScoreSnapshot pSnapshot, WarSettlementAiFacts pFacts,
                WarSettlementPosition pPosition,
                WarSettlementAiDecision pDecision, bool pTotalWar,
                bool pProtectedWar, int pYear)
        {
            if (pRoster == null || !pRoster.TryGet(pRequester.id,
                    out WarParticipantRosterEntry exitRoot) ||
                !pRoster.TryGet(pOpponent.id,
                    out WarParticipantRosterEntry leader)) return null;
            if (!WarParticipantRosterService.TryBuildReadOnly(pWar,
                    pRequester.id, out WarParticipantRosterContext exitRoster,
                    out _) || !exitRoster.TryGet(pRequester.id,
                    out exitRoot) || !exitRoster.TryGet(pOpponent.id,
                    out leader)) return null;
            DiplomacyProposalType type = DiplomacyProposalAiRules.
                SeparatePeaceProposalType(requesterIsExitRoot: true,
                    pPosition);
            bool wantsPeace = pDecision == WarSettlementAiDecision.Peace ||
                              pDecision ==
                              WarSettlementAiDecision.Surrender;
            var authority = new WarPeaceNegotiationAuthorityFacts(true,
                exitRoot.Side != leader.Side,
                requesterIsParticipant: true,
                responderIsParticipant: true,
                requesterIsWarLeader: false,
                responderIsWarLeader: true,
                exitRootRole: exitRoot.Role);
            var separateFacts = new SeparatePeaceAiCandidateFacts(
                authorizedPair: WarPeaceSettlementScopeRules.CanNegotiate(
                    WarPeaceSettlementScopeKind.SeparateParticipant,
                    authority),
                totalWar: pTotalWar, protectedWar: pProtectedWar,
                pendingProposal: HasPendingPair(pRequester.id,
                    pOpponent.id),
                recentRejection: HasRecentSeparatePeaceRejection(
                    pRequester.id, pOpponent.id, pWar.data.id, pYear),
                exitRootRole: exitRoot.Role,
                occupiedCityRatio: ExitRootOccupiedCityRatio(pWar,
                    exitRoot, exitRoster),
                exitWarExhaustion: ParticipantExhaustion(pWar, pSnapshot,
                    exitRoot),
                exitToRequesterPowerRatio: ExitGroupPowerRatio(exitRoot,
                    exitRoster, pOpponent),
                exitShareOfCoalitionPower: SidePowerShare(exitRoot,
                    exitRoster),
                requesterIsWarLeader: false,
                exitRootWantsPeace: wantsPeace);
            DiplomacyActionAssessment assessment = Assess(pRequester,
                pOpponent, type, pWar.data.id, pIgnorePending: true);
            bool eligible = assessment?.Allowed == true &&
                DiplomacyProposalAiRules.CanQueueSeparatePeace(
                    separateFacts);
            int urgency = DiplomacyProposalAiRules.SettlementUrgency(
                pFacts, pDecision, pSnapshot.Score) + Math.Min(150,
                DiplomacyProposalAiRules.SeparatePeaceTargetScore(
                    separateFacts));
            return new PreparedWarSettlement
            {
                War = pWar,
                Opponent = pOpponent,
                Type = type,
                Scope = WarPeaceSettlementScopeKind.SeparateParticipant,
                ExitRootKingdomId = pRequester.id,
                Candidate = new WarSettlementSelectionCandidate(eligible,
                    pDecision, urgency, pFacts.WarYears)
            };
        }

        private static PreparedWarSettlement
            SelectLeaderSeparatePeaceCandidate(War pWar,
                Kingdom pRequester, WarParticipantRosterContext pRoster,
                WarScoreSnapshot pSnapshot, WarSettlementPosition pPosition,
                int pWarYears, bool pTotalWar, bool pProtectedWar, int pYear)
        {
            if (pRoster == null || !pRoster.TryGet(pRequester.id,
                    out WarParticipantRosterEntry requesterEntry)) return null;
            DiplomacyProposalType type = DiplomacyProposalAiRules.
                SeparatePeaceProposalType(requesterIsExitRoot: false,
                    pPosition);
            PreparedWarSettlement best = null;
            int bestScore = int.MinValue;
            int assessed = 0;
            for (int i = 0; i < pRoster.Participants.Count && assessed <
                            DiplomacyProposalAiRules.
                                MaximumSeparatePeaceTargetAssessments; i++)
            {
                WarParticipantRosterEntry exitRoot =
                    pRoster.Participants[i];
                if (exitRoot?.Kingdom?.data == null ||
                    exitRoot.Side == requesterEntry.Side ||
                    exitRoot.Role != WarParticipantRoleKind.Independent &&
                    exitRoot.Role != WarParticipantRoleKind.Tributary)
                    continue;
                assessed++;
                if (!WarParticipantRosterService.TryBuildReadOnly(pWar,
                        exitRoot.KingdomId,
                        out WarParticipantRosterContext exitRoster,
                        out _) || !exitRoster.TryGet(exitRoot.KingdomId,
                        out WarParticipantRosterEntry scopedExitRoot))
                    continue;
                exitRoot = scopedExitRoot;
                var authority = new WarPeaceNegotiationAuthorityFacts(true,
                    opposingSides: true, requesterIsParticipant: true,
                    responderIsParticipant: true,
                    requesterIsWarLeader: true,
                    responderIsWarLeader: false,
                    exitRootRole: exitRoot.Role);
                var facts = new SeparatePeaceAiCandidateFacts(
                    authorizedPair: WarPeaceSettlementScopeRules.CanNegotiate(
                        WarPeaceSettlementScopeKind.SeparateParticipant,
                        authority),
                    totalWar: pTotalWar, protectedWar: pProtectedWar,
                    pendingProposal: HasPendingPair(pRequester.id,
                        exitRoot.KingdomId),
                    recentRejection: HasRecentSeparatePeaceRejection(
                        pRequester.id, exitRoot.KingdomId, pWar.data.id,
                        pYear),
                    exitRootRole: exitRoot.Role,
                    occupiedCityRatio: ExitRootOccupiedCityRatio(pWar,
                        exitRoot, exitRoster),
                    exitWarExhaustion: ParticipantExhaustion(pWar,
                        pSnapshot, exitRoot),
                    exitToRequesterPowerRatio: ExitGroupPowerRatio(exitRoot,
                        exitRoster, pRequester),
                    exitShareOfCoalitionPower: SidePowerShare(exitRoot,
                        exitRoster),
                    requesterIsWarLeader: true,
                    exitRootWantsPeace: false);
                DiplomacyActionAssessment assessment = Assess(pRequester,
                    exitRoot.Kingdom, type, pWar.data.id,
                    pIgnorePending: true);
                if (assessment?.Allowed != true ||
                    !DiplomacyProposalAiRules.CanQueueSeparatePeace(facts))
                    continue;
                int score = DiplomacyProposalAiRules.
                    SeparatePeaceTargetScore(facts);
                if (best != null && (score < bestScore || score == bestScore &&
                    exitRoot.KingdomId >= best.ExitRootKingdomId)) continue;
                bestScore = score;
                WarSettlementAiDecision decision = type switch
                {
                    DiplomacyProposalType.EnforceDemands =>
                        WarSettlementAiDecision.EnforceDemands,
                    DiplomacyProposalType.Surrender =>
                        WarSettlementAiDecision.Surrender,
                    _ => WarSettlementAiDecision.Peace
                };
                best = new PreparedWarSettlement
                {
                    War = pWar,
                    Opponent = exitRoot.Kingdom,
                    Type = type,
                    Scope = WarPeaceSettlementScopeKind.SeparateParticipant,
                    ExitRootKingdomId = exitRoot.KingdomId,
                    Candidate = new WarSettlementSelectionCandidate(true,
                        decision, 175 + score, pWarYears)
                };
            }
            return best;
        }

        private static float ExitRootOccupiedCityRatio(War pWar,
            WarParticipantRosterEntry pExitRoot,
            WarParticipantRosterContext pRoster)
        {
            var liveCityIds = new List<long>();
            try
            {
                foreach (City city in pExitRoot.Kingdom.getCities())
                {
                    if (liveCityIds.Count >= 64) break;
                    if (city?.data == null || city.isRekt()) continue;
                    liveCityIds.Add(city.data.id);
                }
            }
            catch { }
            IReadOnlyList<WarScoreOccupiedCitySnapshot> snapshots =
                WarScoreService.ReadFrozenOccupationsForHomeKingdom(
                    pWar.data.id, pExitRoot.KingdomId, 64);
            var frozen = new List<SeparatePeaceFrozenCityFacts>(
                snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                WarScoreOccupiedCitySnapshot snapshot = snapshots[i];
                bool opposing = pRoster.TryGet(
                    snapshot.ControllerKingdomId,
                    out WarParticipantRosterEntry controller) &&
                    controller.Side != pExitRoot.Side;
                frozen.Add(new SeparatePeaceFrozenCityFacts(snapshot.CityId,
                    snapshot.HomeKingdomId, opposing));
            }
            return SeparatePeaceRuntimeFactsRules.OccupiedCityRatio(
                pExitRoot.KingdomId, liveCityIds, frozen);
        }

        private static int SideExhaustion(WarScoreSnapshot pSnapshot,
            WarParticipantSideKind pSide)
        {
            return pSide == WarParticipantSideKind.Attacker
                ? pSnapshot.AttackerExhaustion
                : pSide == WarParticipantSideKind.Defender
                    ? pSnapshot.DefenderExhaustion
                    : 0;
        }

        private static int ParticipantExhaustion(War pWar,
            WarScoreSnapshot pSnapshot,
            WarParticipantRosterEntry pParticipant)
        {
            int sideExhaustion = SideExhaustion(pSnapshot,
                pParticipant.Side);
            int baseline = 0;
            try
            {
                pWar.data.get(WarParticipantMobilizationBaselineRules.
                    PotentialKey(pParticipant.KingdomId), out baseline, 0);
            }
            catch { }
            int current = 0;
            bool potentialReady = false;
            try
            {
                potentialReady = WartimeMilitaryPotentialService.
                    TryCountPotentialWarriorsBounded(pParticipant.Kingdom,
                        DiplomacyProposalAiRules.
                            MaximumSeparatePeacePotentialCityScans,
                        out current);
            }
            catch { }
            if (!potentialReady) return sideExhaustion;
            return SeparatePeaceRuntimeFactsRules.ParticipantExhaustion(
                sideExhaustion, baseline, current);
        }

        private static float SidePowerShare(
            WarParticipantRosterEntry pExitRoot,
            WarParticipantRosterContext pRoster)
        {
            return SeparatePeaceRuntimeFactsRules.ExitGroupPowerShare(
                BuildExitGroupPowerFacts(pExitRoot, pRoster));
        }

        private static float ExitGroupPowerRatio(
            WarParticipantRosterEntry pExitRoot,
            WarParticipantRosterContext pRoster, Kingdom pOpponent)
        {
            long exitPower = SeparatePeaceRuntimeFactsRules.ExitGroupPower(
                BuildExitGroupPowerFacts(pExitRoot, pRoster));
            return Math.Max(1L, exitPower) /
                   (float)Math.Max(1L, pOpponent?.power ?? 0L);
        }

        private static List<SeparatePeaceParticipantPowerFacts>
            BuildExitGroupPowerFacts(WarParticipantRosterEntry pExitRoot,
                WarParticipantRosterContext pRoster)
        {
            var result = new List<SeparatePeaceParticipantPowerFacts>(
                pRoster?.Participants?.Count ?? 0);
            if (pRoster == null) return result;
            for (int i = 0; i < pRoster.Participants.Count; i++)
            {
                WarParticipantRosterEntry participant =
                    pRoster.Participants[i];
                if (participant?.Kingdom?.data == null) continue;
                result.Add(new SeparatePeaceParticipantPowerFacts(
                    participant.KingdomId,
                    participant.Side == pExitRoot.Side,
                    participant.IncludedInExitGroup,
                    participant.Kingdom.power));
            }
            return result;
        }

        private static float SafePowerRatio(Kingdom pNumerator,
            Kingdom pDenominator)
        {
            return Math.Max(1L, pNumerator?.power ?? 0L) /
                   (float)Math.Max(1L, pDenominator?.power ?? 0L);
        }

        private static List<War> OrderBoundedWarsByAge(
            IReadOnlyList<War> pWars)
        {
            var result = new List<War>();
            if (pWars == null) return result;
            for (int i = 0; i < pWars.Count; i++)
            {
                War war = pWars[i];
                try
                {
                    if (war?.data != null && !war.hasEnded())
                        result.Add(war);
                }
                catch { }
            }
            result.Sort(CompareOldestWarFirst);
            return result;
        }

        private static int CompareOldestWarFirst(War pLeft, War pRight)
        {
            int leftYears = SafeWarValue(() => pLeft.getDuration());
            int rightYears = SafeWarValue(() => pRight.getDuration());
            int years = rightYears.CompareTo(leftYears);
            if (years != 0) return years;
            double leftCreated = pLeft?.data?.created_time ?? double.MaxValue;
            double rightCreated = pRight?.data?.created_time ??
                                  double.MaxValue;
            int created = leftCreated.CompareTo(rightCreated);
            if (created != 0) return created;
            return (pLeft?.data?.id ?? long.MaxValue).CompareTo(
                pRight?.data?.id ?? long.MaxValue);
        }

        private static BoundedRoundRobinCursor<War> WarSettlementCursor(
            Kingdom pKingdom)
        {
            return ProposalRuntime.GetOrAddSettlementCursor(pKingdom.id,
                () => new BoundedRoundRobinCursor<War>(() =>
                    EnumerateWars(pKingdom)));
        }

        private static BoundedRoundRobinCursor<War> WarRecoveryCursor(
            Kingdom pKingdom)
        {
            return ProposalRuntime.GetOrAddRecoveryCursor(pKingdom.id,
                () => new BoundedRoundRobinCursor<War>(() =>
                    EnumerateWars(pKingdom)));
        }

        private static IEnumerable<War> EnumerateWars(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return Array.Empty<War>();
            try { return pKingdom.getWars(); }
            catch { return Array.Empty<War>(); }
        }

        private static void AddOrdinaryAiCandidate(
            ICollection<PreparedAiProposal> pCandidates,
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, int pOpinion,
            float pRequesterPowerRatio)
        {
            DiplomacyActionAssessment assessment = Assess(pRequester,
                pResponder, pType, -1L);
            if (!ExpectedAccepted(assessment)) return;
            pCandidates.Add(new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(pType, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id),
                Selection = DiplomacyProposalSelection.Empty
            });
        }

        private static bool TryPrepareJoinWarCandidate(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            if (!TrySelectJoinWarCandidate(pRequester, pResponder,
                    out JoinWarCandidateFacts selected)) return false;
            DiplomacyActionAssessment assessment = AssessWithSelection(
                pRequester, pResponder, DiplomacyProposalType.JoinWar,
                selected.War.data.id, DiplomacyProposalSelection.Empty);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = BuildJoinWarProposal(pResponder, pOpinion,
                pRequesterPowerRatio, selected);
            return true;
        }

        private static bool TryPrepareJoinWarCandidateReadOnly(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            if (!TrySelectJoinWarCandidate(pRequester, pResponder,
                    out JoinWarCandidateFacts selected)) return false;
            DiplomacyActionAssessment assessment =
                AssessWithSelectionReadOnly(pRequester, pResponder,
                    DiplomacyProposalType.JoinWar, selected.War.data.id,
                    DiplomacyProposalSelection.Empty, pMandateReport);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = BuildJoinWarProposal(pResponder, pOpinion,
                pRequesterPowerRatio, selected);
            return true;
        }

        private static PreparedAiProposal BuildJoinWarProposal(
            Kingdom pResponder, int pOpinion, float pRequesterPowerRatio,
            JoinWarCandidateFacts pSelected)
        {
            int urgency = (pSelected.CapitalThreatened ? 50 : 0) +
                          (pSelected.Losing ? 30 : 0) +
                          Math.Min(30, (int)Math.Round(
                              Math.Max(0f, pSelected.EnemyPower) / 100f));
            return new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.JoinWar, true, pOpinion,
                    pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id, urgency: urgency),
                Selection = DiplomacyProposalSelection.Empty,
                WarId = pSelected.War.data.id
            };
        }

        private static bool TrySelectJoinWarCandidate(Kingdom pRequester,
            Kingdom pResponder, out JoinWarCandidateFacts pSelected)
        {
            pSelected = null;
            if (!SafeAllied(pRequester, pResponder)) return false;
            var candidates = new List<JoinWarCandidateFacts>(
                DiplomacyProposalAiRules.MaximumJoinWarAssessments);
            try
            {
                foreach (War war in pRequester.getWars())
                {
                    if (candidates.Count >=
                        DiplomacyProposalAiRules.MaximumJoinWarAssessments)
                        break;
                    if (war?.data == null || war.hasEnded() ||
                        !war.isAttacker(pRequester) &&
                        !war.isDefender(pRequester) ||
                        war.isAttacker(pResponder) ||
                        war.isDefender(pResponder)) continue;
                    bool subjectConflict = HasJoinWarSubjectConflict(
                        war, pResponder);
                    if (DiplomacyProposalOpportunityRules.JoinWarDirection(
                            allied: true, requesterInWar: true,
                            responderInWar: false, subjectConflict) !=
                        OrdinaryDiplomacyDirection.JoinWar) continue;
                    bool losing = WarScoreService.TryGetSnapshot(war,
                                      pRequester,
                                      out WarScoreSnapshot snapshot) &&
                                  snapshot.Score < 0;
                    candidates.Add(new JoinWarCandidateFacts
                    {
                        War = war,
                        CapitalThreatened = IsCapitalThreatened(war,
                            pRequester),
                        Losing = losing,
                        EnemyPower = EnemyCoalitionPower(war, pRequester)
                    });
                }
            }
            catch { return false; }
            if (candidates.Count == 0) return false;
            candidates.Sort(CompareJoinWarCandidates);
            pSelected = candidates[0];
            return true;
        }

        private static int CompareJoinWarCandidates(
            JoinWarCandidateFacts pLeft, JoinWarCandidateFacts pRight)
        {
            int result = pRight.CapitalThreatened.CompareTo(
                pLeft.CapitalThreatened);
            if (result != 0) return result;
            result = pRight.Losing.CompareTo(pLeft.Losing);
            if (result != 0) return result;
            result = pRight.EnemyPower.CompareTo(pLeft.EnemyPower);
            return result != 0
                ? result
                : pLeft.War.data.id.CompareTo(pRight.War.data.id);
        }

        private static bool HasJoinWarSubjectConflict(War pWar,
            Kingdom pResponder)
        {
            Kingdom responderRoot = VassalService.GetRootSuzerain(
                pResponder) ?? pResponder;
            try
            {
                foreach (Kingdom participant in pWar.getAttackers())
                    if (participant != pResponder &&
                        (VassalService.GetRootSuzerain(participant) ??
                         participant) == responderRoot)
                        return true;
                foreach (Kingdom participant in pWar.getDefenders())
                    if (participant != pResponder &&
                        (VassalService.GetRootSuzerain(participant) ??
                         participant) == responderRoot)
                        return true;
            }
            catch { return true; }
            return false;
        }

        private static float EnemyCoalitionPower(War pWar,
            Kingdom pRequester)
        {
            float power = 0f;
            try
            {
                IEnumerable<Kingdom> enemies = pWar.isAttacker(pRequester)
                    ? pWar.getDefenders()
                    : pWar.getAttackers();
                foreach (Kingdom enemy in enemies)
                    power += Math.Max(0f, enemy?.power ?? 0f);
            }
            catch { }
            return power;
        }

        private static bool TryPrepareVassalizationCandidate(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio,
            out PreparedAiProposal pCandidate)
        {
            return TryPrepareVassalizationCandidateCore(pRequester,
                pResponder, pOpinion, pRequesterPowerRatio, null,
                pReadOnly: false,
                out pCandidate);
        }

        private static bool TryPrepareVassalizationCandidateReadOnly(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            out PreparedAiProposal pCandidate)
        {
            return TryPrepareVassalizationCandidateCore(pRequester,
                pResponder, pOpinion, pRequesterPowerRatio, pMandateReport,
                pReadOnly: true,
                out pCandidate);
        }

        private static bool TryPrepareVassalizationCandidateCore(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            bool pReadOnly,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            bool requesterTributaryOfResponder =
                VassalService.GetTributarySuzerain(pRequester) ==
                pResponder;
            bool responderImperial = KingdomTitleService.IsEmperor(
                pResponder);
            War emergencyWar = FindDefensiveProtectionWar(pRequester,
                pResponder);
            bool defensiveEmergency = emergencyWar?.data != null;
            bool canSetVassal = requesterTributaryOfResponder ||
                (pRequesterPowerRatio < 1f
                    ? VassalService.CanSetVassal(pRequester, pResponder)
                    : VassalService.CanSetVassal(pResponder, pRequester));
            OrdinaryDiplomacyDirection direction =
                DiplomacyProposalOpportunityRules.VassalizeDirection(
                    atWar: FindWarBetween(pRequester, pResponder, -1L) !=
                           null,
                    allied: SafeAllied(pRequester, pResponder),
                    requesterIsSubject: GetAnySuzerain(pRequester) != null,
                    responderIsSubject: GetAnySuzerain(pResponder) != null,
                    canSetVassal: canSetVassal,
                    requesterToResponderPower: pRequesterPowerRatio,
                    threatened: false,
                    defensiveEmergency: defensiveEmergency,
                    requesterTributaryOfResponder:
                    requesterTributaryOfResponder,
                    responderImperial: responderImperial);
            string detailId = VassalizationDetail(direction);
            if (string.IsNullOrEmpty(detailId)) return false;
            long warId = direction ==
                         OrdinaryDiplomacyDirection.VassalizeSeek
                ? emergencyWar?.data?.id ?? -1L
                : -1L;
            long threatId = direction ==
                            OrdinaryDiplomacyDirection.VassalizeSeek
                ? FindOpponent(emergencyWar, pRequester)?.id ?? -1L
                : -1L;
            var selection = new DiplomacyProposalSelection(threatId, -1L,
                -1L, -1L, detailId);
            DiplomacyActionAssessment assessment = pReadOnly
                ? AssessWithSelectionReadOnly(pRequester, pResponder,
                    DiplomacyProposalType.Vassalize, warId, selection,
                    pMandateReport)
                : AssessWithSelection(pRequester, pResponder,
                    DiplomacyProposalType.Vassalize, warId, selection);
            if (!ExpectedAccepted(assessment)) return false;
            int urgency = direction switch
            {
                OrdinaryDiplomacyDirection.VassalizeSeek => 70,
                OrdinaryDiplomacyDirection.VassalizeInternalize => 25,
                _ => Math.Min(50, (int)Math.Round(Math.Max(0f,
                    pRequesterPowerRatio - 1f) * 20f))
            };
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.Vassalize, true, pOpinion,
                    pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id, urgency: urgency),
                Selection = selection,
                WarId = warId
            };
            return true;
        }

        private static string VassalizationDetail(
            OrdinaryDiplomacyDirection pDirection)
        {
            return pDirection switch
            {
                OrdinaryDiplomacyDirection.VassalizeDemand =>
                    DiplomacyProposalOpportunityRules.VassalizeDemandDetail,
                OrdinaryDiplomacyDirection.VassalizeSeek =>
                    DiplomacyProposalOpportunityRules.VassalizeSeekDetail,
                OrdinaryDiplomacyDirection.VassalizeInternalize =>
                    DiplomacyProposalOpportunityRules
                        .VassalizeInternalizeDetail,
                _ => ""
            };
        }

        private static War FindDefensiveProtectionWar(Kingdom pRequester,
            Kingdom pProtector)
        {
            War selected = null;
            int selectedScore = int.MaxValue;
            try
            {
                foreach (War war in pRequester.getWars())
                {
                    if (war?.data == null || war.hasEnded() ||
                        !war.isDefender(pRequester) ||
                        war.isAttacker(pProtector) ||
                        war.isDefender(pProtector)) continue;
                    bool capitalThreatened = IsCapitalThreatened(war,
                        pRequester);
                    int score = 0;
                    bool hasScore = WarScoreService.TryGetSnapshot(war,
                        pRequester, out WarScoreSnapshot snapshot);
                    if (hasScore) score = snapshot.Score;
                    if (!capitalThreatened && (!hasScore || score > -35))
                        continue;
                    if (selected != null && score > selectedScore ||
                        selected != null && score == selectedScore &&
                        war.data.id >= selected.data.id) continue;
                    selected = war;
                    selectedScore = score;
                }
            }
            catch { return null; }
            return selected;
        }

        private static bool TryPrepareEndVassalCandidate(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio,
            out PreparedAiProposal pCandidate)
        {
            return TryPrepareEndVassalCandidateCore(pRequester, pResponder,
                pOpinion, pRequesterPowerRatio, null, pReadOnly: false,
                out pCandidate);
        }

        private static bool TryPrepareEndVassalCandidateReadOnly(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            out PreparedAiProposal pCandidate)
        {
            return TryPrepareEndVassalCandidateCore(pRequester, pResponder,
                pOpinion, pRequesterPowerRatio, pMandateReport,
                pReadOnly: true,
                out pCandidate);
        }

        private static bool TryPrepareEndVassalCandidateCore(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            bool pReadOnly,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            OrdinaryDiplomacyDirection direction =
                DiplomacyProposalOpportunityRules.EndVassalDirection(
                    requesterSuzerainOfResponder:
                    GetAnySuzerain(pResponder) == pRequester,
                    requesterSubjectOfResponder:
                    GetAnySuzerain(pRequester) == pResponder);
            if (direction == OrdinaryDiplomacyDirection.None) return false;
            string detailId = direction ==
                              OrdinaryDiplomacyDirection.EndVassalRelease
                ? DiplomacyProposalOpportunityRules.EndVassalReleaseDetail
                : DiplomacyProposalOpportunityRules.EndVassalRequestDetail;
            var selection = new DiplomacyProposalSelection(-1L, -1L,
                -1L, -1L, detailId);
            DiplomacyActionAssessment assessment = pReadOnly
                ? AssessWithSelectionReadOnly(pRequester, pResponder,
                    DiplomacyProposalType.EndVassal, -1L, selection,
                    pMandateReport)
                : AssessWithSelection(pRequester, pResponder,
                    DiplomacyProposalType.EndVassal, -1L, selection);
            if (!ExpectedAccepted(assessment)) return false;
            Kingdom subject = direction ==
                              OrdinaryDiplomacyDirection.EndVassalRelease
                ? pResponder
                : pRequester;
            int autonomy = VassalService.GetEffectiveRelationTerms(subject)
                .Autonomy;
            int years = VassalService.GetYearsSinceRelationStarted(subject);
            int urgency = Math.Max(0, autonomy - 40) +
                          Math.Min(20, Math.Max(0, years)) +
                          Math.Max(0, -pOpinion) / 2;
            if (direction ==
                OrdinaryDiplomacyDirection.EndVassalRelease)
                urgency += Math.Max(0,
                    VassalService.GetDirectVassalCount(pRequester) - 4) * 8;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.EndVassal, true, pOpinion,
                    pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id, urgency: urgency),
                Selection = selection
            };
            return true;
        }

        private static bool TryPrepareEndAllianceCandidate(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            int liability = Math.Max(0, -pOpinion);
            if (pResponder.power < Math.Max(1, pRequester.power) * .35f)
                liability += 20;
            liability += Math.Min(30,
                CountUnsharedWars(pResponder, pRequester) * 10);
            if (!DiplomacyProposalOpportunityRules.ShouldEndAlliance(
                    SafeAllied(pRequester, pResponder), pOpinion,
                    liability)) return false;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.EndAlliance, true, pOpinion,
                    pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id, urgency: liability),
                Selection = DiplomacyProposalSelection.Empty
            };
            return true;
        }

        private static int CountUnsharedWars(Kingdom pKingdom,
            Kingdom pAlly)
        {
            int count = 0;
            try
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (war?.data == null || war.hasEnded() ||
                        war.isAttacker(pAlly) || war.isDefender(pAlly))
                        continue;
                    count++;
                    if (count >= 3) break;
                }
            }
            catch { }
            return count;
        }

        private static void AddOrdinaryAiCandidateReadOnly(
            ICollection<PreparedAiProposal> pCandidates,
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport)
        {
            DiplomacyActionAssessment assessment = AssessReadOnly(pRequester,
                pResponder, pType, -1L, pMandateReport);
            if (!ExpectedAccepted(assessment)) return;
            pCandidates.Add(new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(pType, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id),
                Selection = DiplomacyProposalSelection.Empty
            });
        }

        private static bool TryPrepareHouseholdCandidate(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            if (!RulerHouseholdService.TryPrepareAiOffer(pRequester,
                    pResponder, out RulerHouseholdOfferPreview preview))
                return false;
            var selection = new DiplomacyProposalSelection(-1L,
                preview.CandidateActorId, preview.RulerActorId, -1L,
                RulerHouseholdRules.DetailId(preview.Kind));
            DiplomacyActionAssessment assessment = AssessWithSelection(
                pRequester, pResponder,
                DiplomacyProposalType.HouseholdOffering, -1L, selection);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.HouseholdOffering, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id,
                    principalHouseholdOffer: preview.Kind ==
                        RulerHouseholdKind.PrincipalWife,
                    urgency: RulerHouseholdRules.AiProposalUrgency(
                        preview.HasPrincipalWife,
                        preview.ActiveConsorts)),
                Selection = selection
            };
            return true;
        }

        private static bool TryPrepareUpperRealmHouseholdCandidate(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio,
            bool requesterSuzerainOfResponder,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            bool candidateAvailable =
                RulerHouseholdService.TryPrepareAiOffer(pRequester,
                    pResponder, out RulerHouseholdOfferPreview preview);
            bool recipientRulerEligible = pResponder?.king?.data != null &&
                preview != null && preview.RulerActorId ==
                pResponder.king.data.id;
            if (!RulerHouseholdRules.CanUpperRealmOfferToSubject(
                    requesterSuzerainOfResponder, candidateAvailable,
                    recipientRulerEligible)) return false;

            var selection = new DiplomacyProposalSelection(-1L,
                preview.CandidateActorId, preview.RulerActorId, -1L,
                RulerHouseholdRules.DetailId(preview.Kind));
            DiplomacyActionAssessment assessment = AssessWithSelection(
                pRequester, pResponder,
                DiplomacyProposalType.HouseholdOffering, -1L, selection);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.HouseholdOffering, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id,
                    principalHouseholdOffer: preview.Kind ==
                        RulerHouseholdKind.PrincipalWife,
                    urgency: RulerHouseholdRules.AiProposalUrgency(
                        preview.HasPrincipalWife,
                        preview.ActiveConsorts)),
                Selection = selection
            };
            return true;
        }

        private static bool TryPrepareConsortRequestCandidate(
            Kingdom pVacancyRealm, Kingdom pSupplierRealm, int pOpinion,
            float pRequesterPowerRatio,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            bool pending = HasPendingPair(pVacancyRealm.id,
                pSupplierRealm.id);
            bool cooldown = HasRecentAiHouseholdRequestRejection(
                pVacancyRealm.id, pSupplierRealm.id, SafeYear());
            RulerHouseholdConsortRequestPreview preview =
                RulerHouseholdService.PrepareConsortRequest(pVacancyRealm,
                    pSupplierRealm, pOpinion, pending, cooldown);
            if (!preview.Available) return false;
            var selection = new DiplomacyProposalSelection(-1L, -1L,
                preview.RulerActorId, -1L,
                RulerHouseholdRules.ConsortRequestDetailId);
            DiplomacyActionAssessment assessment = AssessWithSelection(
                pVacancyRealm, pSupplierRealm,
                DiplomacyProposalType.HouseholdOffering, -1L, selection);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.HouseholdOffering, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pSupplierRealm.id,
                    principalHouseholdOffer: false,
                    urgency: RulerHouseholdRules.AiProposalUrgency(
                        hasPrincipalWife: true,
                        activeConsorts: preview.ActiveConsorts)),
                Selection = selection
            };
            return true;
        }

        private static bool TryPrepareHouseholdCandidateReadOnly(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            if (!RulerHouseholdService.TryPrepareAiOffer(pRequester,
                    pResponder, out RulerHouseholdOfferPreview preview))
                return false;
            var selection = new DiplomacyProposalSelection(-1L,
                preview.CandidateActorId, preview.RulerActorId, -1L,
                RulerHouseholdRules.DetailId(preview.Kind));
            DiplomacyActionAssessment assessment =
                AssessWithSelectionReadOnly(pRequester, pResponder,
                    DiplomacyProposalType.HouseholdOffering, -1L,
                    selection, pMandateReport);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.HouseholdOffering, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id,
                    principalHouseholdOffer: preview.Kind ==
                        RulerHouseholdKind.PrincipalWife,
                    urgency: RulerHouseholdRules.AiProposalUrgency(
                        preview.HasPrincipalWife,
                        preview.ActiveConsorts)),
                Selection = selection
            };
            return true;
        }

        private static bool TryPrepareUpperRealmHouseholdCandidateReadOnly(
            Kingdom pRequester, Kingdom pResponder, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            bool requesterSuzerainOfResponder,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            bool candidateAvailable =
                RulerHouseholdService.TryPrepareAiOffer(pRequester,
                    pResponder, out RulerHouseholdOfferPreview preview);
            bool recipientRulerEligible = pResponder?.king?.data != null &&
                preview != null && preview.RulerActorId ==
                pResponder.king.data.id;
            if (!RulerHouseholdRules.CanUpperRealmOfferToSubject(
                    requesterSuzerainOfResponder, candidateAvailable,
                    recipientRulerEligible)) return false;

            var selection = new DiplomacyProposalSelection(-1L,
                preview.CandidateActorId, preview.RulerActorId, -1L,
                RulerHouseholdRules.DetailId(preview.Kind));
            DiplomacyActionAssessment assessment =
                AssessWithSelectionReadOnly(pRequester, pResponder,
                    DiplomacyProposalType.HouseholdOffering, -1L,
                    selection, pMandateReport);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.HouseholdOffering, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pResponder.id,
                    principalHouseholdOffer: preview.Kind ==
                        RulerHouseholdKind.PrincipalWife,
                    urgency: RulerHouseholdRules.AiProposalUrgency(
                        preview.HasPrincipalWife,
                        preview.ActiveConsorts)),
                Selection = selection
            };
            return true;
        }

        private static bool TryPrepareConsortRequestCandidateReadOnly(
            Kingdom pVacancyRealm, Kingdom pSupplierRealm, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            out PreparedAiProposal pCandidate)
        {
            pCandidate = null;
            bool pending = HasPendingPair(pVacancyRealm.id,
                pSupplierRealm.id);
            bool cooldown = HasRecentAiHouseholdRequestRejection(
                pVacancyRealm.id, pSupplierRealm.id, SafeYear());
            RulerHouseholdConsortRequestPreview preview =
                RulerHouseholdService.PrepareConsortRequest(pVacancyRealm,
                    pSupplierRealm, pOpinion, pending, cooldown);
            if (!preview.Available) return false;
            var selection = new DiplomacyProposalSelection(-1L, -1L,
                preview.RulerActorId, -1L,
                RulerHouseholdRules.ConsortRequestDetailId);
            DiplomacyActionAssessment assessment =
                AssessWithSelectionReadOnly(pVacancyRealm, pSupplierRealm,
                    DiplomacyProposalType.HouseholdOffering, -1L,
                    selection, pMandateReport);
            if (!ExpectedAccepted(assessment)) return false;
            pCandidate = new PreparedAiProposal
            {
                Candidate = new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.HouseholdOffering, true,
                    pOpinion, pRequesterPowerRatio, false, 0f, false,
                    targetKingdomId: pSupplierRealm.id,
                    principalHouseholdOffer: false,
                    urgency: RulerHouseholdRules.AiProposalUrgency(
                        hasPrincipalWife: true,
                        activeConsorts: preview.ActiveConsorts)),
                Selection = selection
            };
            return true;
        }

        private static bool TrySelectAiConsortForRequest(
            DiplomacyProposal pProposal, out string pReason)
        {
            pReason = "no_household_candidate";
            if (pProposal == null ||
                !RulerHouseholdRules.IsConsortRequestDetail(
                    pProposal.DetailId)) return false;
            Kingdom vacancyRealm = FindKingdom(
                pProposal.RequesterKingdomId);
            Kingdom supplierRealm = FindKingdom(
                pProposal.ResponderKingdomId);
            if (!RulerHouseholdService.TryPrepareAiConsortOffer(
                    supplierRealm, vacancyRealm,
                    out RulerHouseholdOfferPreview preview)) return false;
            return TryAttachConsortRequestCandidate(pProposal.ProposalId,
                preview.CandidateActorId, out pReason);
        }

        private static bool ExpectedAccepted(
            DiplomacyActionAssessment pAssessment)
        {
            return pAssessment?.Allowed == true &&
                   pAssessment.Acceptance?.ExpectedAccepted == true;
        }

        private static int FindPreparedCandidate(
            IReadOnlyList<PreparedAiProposal> pCandidates,
            DiplomacyProposalAiCandidate pSelected)
        {
            for (int i = 0; i < pCandidates.Count; i++)
                if (pCandidates[i].Candidate.Type == pSelected.Type &&
                    pCandidates[i].Candidate.TargetKingdomId ==
                    pSelected.TargetKingdomId)
                    return i;
            return -1;
        }

        private static bool TryPrepareCoalitionCandidate(Kingdom pRequester,
            Kingdom pResponder, int pYear, int pOpinion,
            float pRequesterPowerRatio, out PreparedAiProposal pPrepared)
        {
            pPrepared = null;
            List<Kingdom> targets = CollectBoundedCoalitionTargets(pRequester,
                pResponder, pYear);
            int assessments = 0;
            while (targets.Count > 0 && assessments <
                   DiplomacyProposalAiRules.MaximumCoalitionAssessments)
            {
                int bestIndex = 0;
                long bestThreat = CoalitionThreatScore(targets[0], pRequester,
                    pResponder);
                for (int i = 1; i < targets.Count; i++)
                {
                    long threat = CoalitionThreatScore(targets[i], pRequester,
                        pResponder);
                    if (threat < bestThreat || threat == bestThreat &&
                        targets[i].id >= targets[bestIndex].id) continue;
                    bestIndex = i;
                    bestThreat = threat;
                }
                Kingdom target = targets[bestIndex];
                targets.RemoveAt(bestIndex);
                assessments++;
                var selection = new DiplomacyProposalSelection(target.id,
                    -1L, -1L, -1L, "");
                DiplomacyActionAssessment assessment = AssessWithSelection(
                    pRequester, pResponder, DiplomacyProposalType.Coalition,
                    -1L, selection);
                if (!ExpectedAccepted(assessment)) continue;
                float targetRatio = Math.Max(1, target.power) /
                    (float)Math.Max(1, Math.Max(pRequester.power,
                        pResponder.power));
                pPrepared = new PreparedAiProposal
                {
                    Candidate = new DiplomacyProposalAiCandidate(
                        DiplomacyProposalType.Coalition, true, pOpinion,
                        pRequesterPowerRatio, false, targetRatio,
                        MandateService.IsMandateKingdom(target),
                        pResponder.id),
                    Selection = selection
                };
                return true;
            }
            return false;
        }

        private static bool TryPrepareCoalitionCandidateReadOnly(
            Kingdom pRequester, Kingdom pResponder, int pYear, int pOpinion,
            float pRequesterPowerRatio, MandateReport pMandateReport,
            out PreparedAiProposal pPrepared,
            out AsyncDiplomacySelectionTargetFacts[] pSelectionTargets)
        {
            pPrepared = null;
            List<Kingdom> targets = CollectBoundedCoalitionTargetsReadOnly(
                pRequester, pResponder, pYear, pMandateReport);
            var targetFacts = new List<AsyncDiplomacySelectionTargetFacts>(
                targets.Count);
            var targetsById = new Dictionary<long, Kingdom>();
            for (int index = 0; index < targets.Count; index++)
            {
                AsyncDiplomacySelectionTargetFacts facts =
                    CaptureSelectionTargetFacts(targets[index], pRequester,
                        pResponder, pMandateReport);
                targetFacts.Add(facts);
                if (facts.TargetKingdomId >= 0L)
                    targetsById[facts.TargetKingdomId] = targets[index];
            }
            pSelectionTargets = targetFacts.ToArray();
            IReadOnlyList<AsyncDiplomacySelectionTargetFacts> rankedTargets =
                AsyncDiplomacyCoalitionRules.RankParticipants(targetFacts);
            int assessments = 0;
            for (int index = 0; index < rankedTargets.Count && assessments <
                 DiplomacyProposalAiRules.MaximumCoalitionAssessments;
                 index++)
            {
                AsyncDiplomacySelectionTargetFacts selectedTargetFacts =
                    rankedTargets[index];
                assessments++;
                if (!AsyncDiplomacyCoalitionRules.IsEligible(
                        selectedTargetFacts) ||
                    !targetsById.TryGetValue(
                        selectedTargetFacts.TargetKingdomId,
                        out Kingdom target)) continue;
                var selection = new DiplomacyProposalSelection(
                    selectedTargetFacts.TargetKingdomId, -1L, -1L, -1L, "");
                DiplomacyActionAssessment assessment =
                    AssessWithSelectionReadOnly(pRequester, pResponder,
                        DiplomacyProposalType.Coalition, -1L, selection,
                        pMandateReport);
                if (!ExpectedAccepted(assessment)) continue;
                float targetRatio = Math.Max(1f, selectedTargetFacts.Power) /
                    (float)Math.Max(1, Math.Max(pRequester.power,
                        pResponder.power));
                pPrepared = new PreparedAiProposal
                {
                    Candidate = new DiplomacyProposalAiCandidate(
                        DiplomacyProposalType.Coalition, true, pOpinion,
                        pRequesterPowerRatio, false, targetRatio,
                        selectedTargetFacts.TargetHasMandate, pResponder.id),
                    Selection = selection
                };
                return true;
            }
            return false;
        }

        private static AsyncDiplomacySelectionTargetFacts
            CaptureSelectionTargetFacts(Kingdom pTarget,
                Kingdom pRequester, Kingdom pResponder,
                MandateReport pMandateReport)
        {
            try
            {
                DiplomaticCoalitionService.PrepareReadOnly(pRequester,
                    pResponder, pTarget, pMandateReport,
                    out AsyncDiplomacySelectionTargetFacts facts);
                return facts;
            }
            catch
            {
                return new AsyncDiplomacySelectionTargetFacts(
                    pTarget?.data == null ? -1L : pTarget.id,
                    Math.Max(1, pTarget?.power ?? 0), false, false, false,
                    eligible: false, serviceReady: false);
            }
        }

        private static List<Kingdom> CollectBoundedCoalitionTargets(
            Kingdom pRequester, Kingdom pResponder, int pYear)
        {
            var result = new List<Kingdom>(
                DiplomacyProposalAiRules.MaximumCoalitionTargets);
            AddCoalitionTarget(result,
                MandateService.GetCurrentMandateKingdom(), pRequester,
                pResponder);
            War requesterWar = FindFirstWar(pRequester);
            AddCoalitionTarget(result,
                FindOpponent(requesterWar, pRequester), pRequester,
                pResponder);
            War responderWar = FindFirstWar(pResponder);
            AddCoalitionTarget(result,
                FindOpponent(responderWar, pResponder), pRequester,
                pResponder);

            int cityCount = pResponder?.cities?.Count ?? 0;
            if (cityCount <= 0) return result;
            int start = (int)(((pResponder.id & long.MaxValue) + pYear) %
                              cityCount);
            int inspect = Math.Min(cityCount,
                DiplomacyProposalAiRules.MaximumCoalitionCities);
            for (int offset = 0; offset < inspect && result.Count <
                 DiplomacyProposalAiRules.MaximumCoalitionTargets; offset++)
            {
                City city = pResponder.cities[(start + offset) % cityCount];
                if (city?.neighbours_kingdoms == null) continue;
                foreach (Kingdom neighbor in city.neighbours_kingdoms)
                {
                    AddCoalitionTarget(result, neighbor, pRequester,
                        pResponder);
                    if (result.Count >= DiplomacyProposalAiRules
                            .MaximumCoalitionTargets)
                        break;
                }
            }
            return result;
        }

        private static List<Kingdom> CollectBoundedCoalitionTargetsReadOnly(
            Kingdom pRequester, Kingdom pResponder, int pYear,
            MandateReport pMandateReport)
        {
            var result = new List<Kingdom>(
                DiplomacyProposalAiRules.MaximumCoalitionTargets);
            AddCoalitionTarget(result,
                MandateService.GetCurrentMandateKingdomReadOnly(
                    pMandateReport), pRequester, pResponder);
            War requesterWar = FindFirstWar(pRequester);
            AddCoalitionTarget(result,
                FindOpponent(requesterWar, pRequester), pRequester,
                pResponder);
            War responderWar = FindFirstWar(pResponder);
            AddCoalitionTarget(result,
                FindOpponent(responderWar, pResponder), pRequester,
                pResponder);

            int cityCount = pResponder?.cities?.Count ?? 0;
            if (cityCount <= 0) return result;
            int start = (int)(((pResponder.id & long.MaxValue) + pYear) %
                              cityCount);
            int inspect = Math.Min(cityCount,
                DiplomacyProposalAiRules.MaximumCoalitionCities);
            for (int offset = 0; offset < inspect && result.Count <
                 DiplomacyProposalAiRules.MaximumCoalitionTargets; offset++)
            {
                City city = pResponder.cities[(start + offset) % cityCount];
                if (city?.neighbours_kingdoms == null) continue;
                foreach (Kingdom neighbor in city.neighbours_kingdoms)
                {
                    AddCoalitionTarget(result, neighbor, pRequester,
                        pResponder);
                    if (result.Count >= DiplomacyProposalAiRules
                            .MaximumCoalitionTargets) break;
                }
            }
            return result;
        }

        private static void AddCoalitionTarget(ICollection<Kingdom> pTargets,
            Kingdom pTarget, Kingdom pRequester, Kingdom pResponder)
        {
            if (pTarget?.data == null || pTarget.isRekt() ||
                !pTarget.isCiv() || pTarget.isNeutral() ||
                pTarget == pRequester || pTarget == pResponder ||
                pTargets.Count >=
                DiplomacyProposalAiRules.MaximumCoalitionTargets)
                return;
            foreach (Kingdom existing in pTargets)
                if (existing == pTarget) return;
            pTargets.Add(pTarget);
        }

        private static long CoalitionThreatScore(Kingdom pTarget,
            Kingdom pRequester, Kingdom pResponder)
        {
            if (pTarget?.data == null) return long.MinValue;
            long score = Math.Max(1L, pTarget.power);
            if (MandateService.IsMandateKingdom(pTarget)) score += 100000L;
            try
            {
                if (pRequester.isEnemy(pTarget)) score += 20000L;
                if (pResponder.isEnemy(pTarget)) score += 20000L;
            }
            catch { }
            return score;
        }

        private static Kingdom NextDiplomacyContact(Kingdom pKingdom)
        {
            Kingdom requestTarget = FindPreferredConsortRequestTarget(
                pKingdom);
            if (requestTarget?.data == null) return NextBorderContact(pKingdom);
            NextBorderContact(pKingdom);
            return requestTarget;
        }

        private static Kingdom PeekDiplomacyContact(Kingdom pKingdom)
        {
            return FindPreferredConsortRequestTarget(pKingdom) ??
                   PeekBorderContact(pKingdom);
        }

        private static Kingdom FindPreferredConsortRequestTarget(
            Kingdom pVacancyRealm)
        {
            if (pVacancyRealm?.data == null || World.world?.kingdoms == null)
                return null;
            Kingdom best = null;
            float bestDistance = float.MaxValue;
            int bestOpinion = int.MinValue;
            int year = SafeYear();
            IReadOnlyList<Kingdom> candidates =
                ConsortRequestTargetBatch(pVacancyRealm, year);
            for (int i = 0; i < candidates.Count; i++)
            {
                Kingdom candidate = candidates[i];
                if (candidate?.data == null || candidate == pVacancyRealm ||
                    candidate.isRekt() || candidate.isNeutral() ||
                    !candidate.isCiv()) continue;
                int opinion = DiplomacyOpinionService.Read(candidate,
                    pVacancyRealm);
                if (opinion <
                    RulerHouseholdRules.MinimumConsortRequestOpinion ||
                    HasPendingPair(pVacancyRealm.id, candidate.id) ||
                    HasRecentAiHouseholdRequestRejection(pVacancyRealm.id,
                        candidate.id, year) ||
                    !RulerHouseholdService.HasPlausibleConsortSupplier(
                        candidate, pVacancyRealm)) continue;
                DiplomacyProposalAssessment acceptance =
                    DiplomacyProposalRules.Assess(
                        DiplomacyProposalType.HouseholdOffering,
                        BuildScoreFacts(pVacancyRealm, candidate,
                            pProposedConsortRequest: true));
                if (!acceptance.ExpectedAccepted) continue;
                float distance = CapitalDistance(pVacancyRealm, candidate);
                if (!RulerHouseholdRules.IsBetterConsortRequestTarget(
                        distance, opinion, candidate.id, bestDistance,
                        bestOpinion, best?.id ?? -1L)) continue;
                best = candidate;
                bestDistance = distance;
                bestOpinion = opinion;
            }
            return best;
        }

        private static IReadOnlyList<Kingdom> ConsortRequestTargetBatch(
            Kingdom pVacancyRealm, int pYear)
        {
            long kingdomId = pVacancyRealm.id;
            if (ConsortRequestTargetBatches.TryGetValue(kingdomId,
                    out (int Year, IReadOnlyList<Kingdom> Candidates) cached) &&
                cached.Year == pYear)
                return cached.Candidates;
            if (!ConsortRequestTargetCursors.TryGetValue(kingdomId,
                    out BoundedRoundRobinCursor<Kingdom> cursor))
            {
                cursor = new BoundedRoundRobinCursor<Kingdom>(() =>
                    EnumerateConsortRequestTargetKingdoms());
                ConsortRequestTargetCursors[kingdomId] = cursor;
            }
            IReadOnlyList<Kingdom> candidates = cursor.
                Take(MaximumConsortRequestTargetChecks);
            ConsortRequestTargetBatches[kingdomId] = (pYear, candidates);
            return candidates;
        }

        private static IEnumerable<Kingdom>
            EnumerateConsortRequestTargetKingdoms()
        {
            if (World.world?.kingdoms == null) return Array.Empty<Kingdom>();
            return World.world.kingdoms;
        }

        private static void ClearConsortRequestTargetCursors()
        {
            foreach (BoundedRoundRobinCursor<Kingdom> cursor in
                     ConsortRequestTargetCursors.Values)
                cursor?.Dispose();
            ConsortRequestTargetCursors.Clear();
            ConsortRequestTargetBatches.Clear();
        }

        private static void RemoveConsortRequestTargetCursor(long pKingdomId)
        {
            if (ConsortRequestTargetCursors.TryGetValue(pKingdomId,
                    out BoundedRoundRobinCursor<Kingdom> cursor))
            {
                ConsortRequestTargetCursors.Remove(pKingdomId);
                cursor?.Dispose();
            }
            ConsortRequestTargetBatches.Remove(pKingdomId);
        }

        private static Kingdom NextBorderContact(Kingdom pKingdom)
        {
            int count = pKingdom?.cities?.Count ?? 0;
            if (count == 0) return null;
            pKingdom.data.get(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                out int cursor, 0);
            cursor = Math.Max(0, cursor) % count;
            pKingdom.data.set(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                (cursor + 1) % count);
            return BorderContactAt(pKingdom, cursor);
        }

        private static Kingdom PeekBorderContact(Kingdom pKingdom)
        {
            int count = pKingdom?.cities?.Count ?? 0;
            if (count == 0) return null;
            pKingdom.data.get(LineageKeys.DIPLOMACY_AI_CITY_CURSOR,
                out int cursor, 0);
            cursor = Math.Max(0, cursor) % count;
            return BorderContactAt(pKingdom, cursor);
        }

        private static Kingdom BorderContactAt(Kingdom pKingdom, int pCursor)
        {
            City city = pKingdom.cities[pCursor];
            if (city?.neighbours_kingdoms == null) return null;
            foreach (Kingdom neighbor in city.neighbours_kingdoms)
                if (neighbor?.data != null && neighbor != pKingdom &&
                    !neighbor.isRekt() && !neighbor.isNeutral())
                    return neighbor;
            return null;
        }

        private static War FindFirstWar(Kingdom pKingdom)
        {
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) return war;
            }
            catch { }
            return null;
        }

        private static Kingdom FindOpponent(War pWar, Kingdom pKingdom)
        {
            try
            {
                return pWar.isAttacker(pKingdom)
                    ? pWar.getMainDefender()
                    : pWar.getMainAttacker();
            }
            catch { return null; }
        }

        private static void NotifyPair(long pKingdomA, long pKingdomB)
        {
            KingdomStrategyRevisionService.MarkChanged(pKingdomA, pKingdomB);
            try { PairChanged?.Invoke(pKingdomA, pKingdomB); }
            catch { }
        }

        private static DiplomacyProposal Find(long pProposalId)
        {
            if (!Ready || pProposalId < 0) return null;
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT " + ProposalSelectColumns +
                " FROM " + DiplomacyProposalTableItem.GetTableName() +
                " WHERE PROPOSAL_ID=@id LIMIT 1";
            command.Parameters.AddWithValue("@id", pProposalId);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadProposal(reader) : null;
        }

        private static DiplomacyProposal ReadProposal(SQLiteDataReader pReader)
        {
            return new DiplomacyProposal
            {
                ProposalId = pReader.GetInt64(0),
                RequesterKingdomId = pReader.GetInt64(1),
                RequesterName = ReadString(pReader, 2),
                ResponderKingdomId = pReader.GetInt64(3),
                ResponderName = ReadString(pReader, 4),
                Type = ParseType(ReadString(pReader, 5)),
                Status = ParseStatus(ReadString(pReader, 6)),
                WarId = pReader.GetInt64(7),
                PlayerInitiated = pReader.GetInt32(8) != 0,
                CreatedYear = pReader.GetInt32(9),
                ExpiryYear = pReader.GetInt32(10),
                ResponseYear = pReader.GetInt32(11),
                TreatyUntilYear = pReader.GetInt32(12),
                ResponseReason = ReadString(pReader, 13),
                CreatedTime = pReader.GetDouble(14),
                ResponseDueTime = pReader.GetDouble(15),
                ResponseTime = pReader.GetDouble(16),
                RequesterTitle = ReadString(pReader, 17),
                ResponderTitle = ReadString(pReader, 18),
                RequestYearPrefix = ReadString(pReader, 19),
                ResponseYearPrefix = ReadString(pReader, 20),
                TargetKingdomId = pReader.GetInt64(21),
                RequesterActorId = pReader.GetInt64(22),
                ResponderActorId = pReader.GetInt64(23),
                TargetCityId = pReader.GetInt64(24),
                DetailId = ReadString(pReader, 25),
                RequestStyle = DiplomacyConversationRules.ParseLetterStyle(
                    ReadString(pReader, 26)),
                RequestTone = DiplomacyConversationRules.ParseLetterTone(
                    ReadString(pReader, 27)),
                ResponseStyle = DiplomacyConversationRules.ParseLetterStyle(
                    ReadString(pReader, 28)),
                ResponseTone = DiplomacyConversationRules.ParseLetterTone(
                    ReadString(pReader, 29))
            };
        }

        private sealed class ProposalContext
        {
            public bool AtWar;
            public bool Allied;
            public bool RequesterIsSubject;
            public bool ResponderIsSubject;
            public bool HasJoinableWar;
            public DiplomacyAvailabilityFacts Availability;
            public long WarId = -1;
            public WarSettlementEvaluation Settlement;
        }

        private sealed class WarSettlementEvaluation
        {
            public WarSettlementPosition Position;
            public float RequesterPower;
            public float ResponderPower;
            public bool RequesterReadyForPeace;
            public bool ResponderReadyForPeace;
            public bool ResponderReadyToConcede;
            public int RequesterSurrenderWarSituation;
            public int RequesterSurrenderPower;
            public int RequesterSurrenderResolve;
        }

        private static ProposalContext ReadContext(Kingdom pRequester,
            Kingdom pResponder, DiplomacyProposalType pType, long pWarId)
        {
            return ReadContextCore(pRequester, pResponder, pType, pWarId,
                MandateService.IsMandateKingdom(pRequester));
        }

        private static ProposalContext ReadContextReadOnly(
            Kingdom pRequester, Kingdom pResponder,
            DiplomacyProposalType pType, long pWarId,
            MandateReport pMandateReport)
        {
            return ReadContextCore(pRequester, pResponder, pType, pWarId,
                MandateService.IsMandateKingdomReadOnly(pRequester,
                    pMandateReport));
        }

        private static ProposalContext ReadContextCore(Kingdom pRequester,
            Kingdom pResponder, DiplomacyProposalType pType, long pWarId,
            bool pRequesterIsMandate)
        {
            War pairWar = FindWarBetween(pRequester, pResponder, pWarId);
            War joinWar = FindJoinableWar(pRequester, pResponder, pWarId);
            bool allied = SafeAllied(pRequester, pResponder);
            Kingdom requesterSuzerain = GetAnySuzerain(pRequester);
            Kingdom responderSuzerain = GetAnySuzerain(pResponder);
            bool requesterSubject = requesterSuzerain?.data != null;
            bool responderSubject = responderSuzerain?.data != null;
            bool directSubjectRelation = requesterSuzerain == pResponder ||
                                         responderSuzerain == pRequester;
            ReadActiveTreatyYears(pRequester, pResponder,
                out int nonAggressionUntil, out int truceUntil);
            int year = SafeYear();
            string subjectFailure = "";
            if (pType == DiplomacyProposalType.Vassalize ||
                pType == DiplomacyProposalType.Tributary)
                VassalService.CanSetVassal(pResponder, pRequester, out subjectFailure);
            string allianceFailure = pType == DiplomacyProposalType.Alliance
                ? AllianceExecutionFailure(pRequester, pResponder)
                : "";
            WarSettlementEvaluation settlement =
                DiplomacyProposalRules.IsPeaceProposal(pType)
                    ? BuildWarSettlementEvaluation(pRequester, pResponder,
                        pairWar)
                    : null;
            WarSettlementPosition warPosition = settlement?.Position ??
                WarSettlementPosition.Contested;
            var availability = new DiplomacyAvailabilityFacts(
                atWar: pairWar?.data != null,
                allied: allied,
                requesterIsSubject: requesterSubject,
                responderIsSubject: responderSubject,
                directSubjectRelation: directSubjectRelation,
                hasJoinableWar: joinWar?.data != null,
                requesterIsMandate: pRequesterIsMandate,
                activeNonAggression: nonAggressionUntil >= year,
                activeTruce: truceUntil >= year,
                subjectFailureReason: subjectFailure,
                allianceFailureReason: allianceFailure,
                activeWarPreparation:
                WarNoticeService.HasActiveNoticeBetween(
                    pRequester, pResponder),
                peaceNegotiators: pairWar?.data == null ||
                    CanNegotiatePeacePair(pairWar, pRequester, pResponder),
                warPosition: warPosition);
            return new ProposalContext
            {
                AtWar = availability.AtWar,
                Allied = availability.Allied,
                RequesterIsSubject = availability.RequesterIsSubject,
                ResponderIsSubject = availability.ResponderIsSubject,
                HasJoinableWar = availability.HasJoinableWar,
                Availability = availability,
                WarId = pairWar?.data?.id ?? joinWar?.data?.id ?? pWarId,
                Settlement = settlement
            };
        }

        private static WarSettlementEvaluation BuildWarSettlementEvaluation(
            Kingdom pRequester, Kingdom pResponder, War pWar)
        {
            if (pWar?.data == null) return null;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return null;
            bool requesterAttacker = pWar.isAttacker(pRequester);
            WarSettlementAiFacts attackerFacts = BuildWarSettlementFacts(
                attacker, defender, pWar);
            WarSettlementAiFacts defenderFacts = BuildWarSettlementFacts(
                defender, attacker, pWar);
            WarSettlementAiFacts requesterFacts = requesterAttacker
                ? attackerFacts
                : defenderFacts;
            WarSettlementAiFacts responderFacts = requesterAttacker
                ? defenderFacts
                : attackerFacts;
            WarSettlementPosition requesterPosition =
                WarSettlementPosition.Contested;
            if (WarScoreService.TryGetSnapshot(pWar, pRequester,
                    out WarScoreSnapshot snapshot))
                requesterPosition = DiplomacyProposalAiRules
                    .ResolvePositionFromSignedWarScore(snapshot.Score);
            WarSettlementPosition responderPosition =
                DiplomacyProposalAiRules.Opposite(requesterPosition);
            return new WarSettlementEvaluation
            {
                Position = requesterPosition,
                RequesterPower = Math.Max(1, SafeWarValue(requesterAttacker
                    ? () => pWar.countAttackersWarriors()
                    : () => pWar.countDefendersWarriors())),
                ResponderPower = Math.Max(1, SafeWarValue(requesterAttacker
                    ? () => pWar.countDefendersWarriors()
                    : () => pWar.countAttackersWarriors())),
                RequesterReadyForPeace =
                    DiplomacyProposalAiRules.IsReadyToAcceptPeace(
                        requesterFacts, requesterPosition),
                RequesterSurrenderWarSituation =
                    DiplomacyProposalAiRules.SurrenderWarSituationScore(
                        requesterFacts),
                RequesterSurrenderPower =
                    DiplomacyProposalAiRules.SurrenderPowerScore(
                        requesterFacts),
                RequesterSurrenderResolve =
                    DiplomacyProposalAiRules.SurrenderResolveScore(
                        requesterFacts),
                ResponderReadyForPeace =
                    DiplomacyProposalAiRules.IsReadyToAcceptPeace(
                        responderFacts, responderPosition),
                ResponderReadyToConcede =
                    DiplomacyProposalAiRules.IsReadyToConcede(responderFacts,
                        responderPosition)
            };
        }

        private static Kingdom GetAnySuzerain(Kingdom pKingdom)
        {
            return VassalService.GetSuzerain(pKingdom) ??
                   VassalService.GetTributarySuzerain(pKingdom);
        }

        private static void ReadActiveTreatyYears(Kingdom pKingdomA,
            Kingdom pKingdomB, out int pNonAggressionUntil,
            out int pTruceUntil)
        {
            pNonAggressionUntil = -1;
            pTruceUntil = -1;
            if (!Ready || pKingdomA?.data == null ||
                pKingdomB?.data == null) return;
            DiplomacyTreatyPersistence.TryReadActiveTreatyYears(DB,
                DiplomacyProposalTableItem.GetTableName(), pKingdomA.id,
                pKingdomB.id, SafeYear(), out pNonAggressionUntil,
                out pTruceUntil);
        }

        private static bool HasRecentAiRejectionForSelection(long pKingdomA,
            long pKingdomB, DiplomacyProposalType pType, string pDetailId,
            int pCurrentYear)
        {
            return pType == DiplomacyProposalType.HouseholdOffering &&
                   RulerHouseholdRules.IsConsortRequestDetail(pDetailId)
                ? HasRecentAiHouseholdRequestRejection(pKingdomA, pKingdomB,
                    pCurrentYear)
                : HasRecentAiRejection(pKingdomA, pKingdomB, pType,
                    pCurrentYear);
        }

        private static bool HasRecentAiHouseholdRequestRejection(
            long pKingdomA, long pKingdomB, int pCurrentYear)
        {
            if (!Ready || pKingdomA < 0L || pKingdomB < 0L) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT MAX(RESPONSE_YEAR) FROM " +
                    DiplomacyProposalTableItem.GetTableName() +
                    " WHERE MIN(REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID)=@first AND " +
                    "MAX(REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID)=@second AND " +
                    "PROPOSAL_TYPE='household_offering' AND " +
                    "DETAIL_ID=@request_detail AND PLAYER_INITIATED=0 AND " +
                    "STATUS='rejected'";
                command.Parameters.AddWithValue("@first",
                    Math.Min(pKingdomA, pKingdomB));
                command.Parameters.AddWithValue("@second",
                    Math.Max(pKingdomA, pKingdomB));
                command.Parameters.AddWithValue("@request_detail",
                    RulerHouseholdRules.ConsortRequestDetailId);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                return DiplomacyProposalRules.IsAiRejectionCooldownActive(
                    pCurrentYear, Convert.ToInt32(value),
                    DiplomacyProposalType.HouseholdOffering);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasRecentAiRejection(long pKingdomA,
            long pKingdomB, DiplomacyProposalType pType, int pCurrentYear)
        {
            if (!Ready || pKingdomA < 0 || pKingdomB < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT MAX(RESPONSE_YEAR) FROM " +
                    DiplomacyProposalTableItem.GetTableName() +
                    " WHERE MIN(REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID)=@first AND " +
                    "MAX(REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID)=@second AND " +
                    "PROPOSAL_TYPE=@type AND PLAYER_INITIATED=0 AND " +
                    "STATUS='rejected'";
                command.Parameters.AddWithValue("@first",
                    Math.Min(pKingdomA, pKingdomB));
                command.Parameters.AddWithValue("@second",
                    Math.Max(pKingdomA, pKingdomB));
                command.Parameters.AddWithValue("@type", TypeId(pType));
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                return DiplomacyProposalRules.IsAiRejectionCooldownActive(
                    pCurrentYear, Convert.ToInt32(value), pType);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasRecentSeparatePeaceRejection(long pKingdomA,
            long pKingdomB, long pWarId, int pCurrentYear)
        {
            if (!Ready || pKingdomA < 0 || pKingdomB < 0 || pWarId < 0)
                return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT MAX(RESPONSE_YEAR) FROM " +
                    DiplomacyProposalTableItem.GetTableName() +
                    " WHERE MIN(REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID)=@first AND " +
                    "MAX(REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID)=@second AND WAR_ID=@war AND " +
                    "PROPOSAL_TYPE IN ('peace','surrender'," +
                    "'enforce_demands') AND PLAYER_INITIATED=0 AND " +
                    "STATUS='rejected'";
                command.Parameters.AddWithValue("@first",
                    Math.Min(pKingdomA, pKingdomB));
                command.Parameters.AddWithValue("@second",
                    Math.Max(pKingdomA, pKingdomB));
                command.Parameters.AddWithValue("@war", pWarId);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                return DiplomacyProposalRules.IsAiRejectionCooldownActive(
                    pCurrentYear, Convert.ToInt32(value),
                    DiplomacyProposalType.Peace);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSubject(Kingdom pKingdom)
        {
            return VassalService.GetSuzerain(pKingdom)?.data != null ||
                   VassalService.GetTributarySuzerain(pKingdom)?.data != null;
        }

        private static War FindJoinableWar(Kingdom pRequester,
            Kingdom pResponder, long pWarId)
        {
            War specified = FindWar(pWarId);
            if (specified?.data != null && !specified.hasEnded() &&
                (specified.isAttacker(pRequester) ||
                 specified.isDefender(pRequester)) &&
                !specified.isAttacker(pResponder) &&
                !specified.isDefender(pResponder)) return specified;
            try
            {
                foreach (War war in pRequester.getWars())
                    if (war?.data != null && !war.hasEnded() &&
                        !war.isAttacker(pResponder) &&
                        !war.isDefender(pResponder)) return war;
            }
            catch { }
            return null;
        }

        private static War FindWarBetween(Kingdom pA, Kingdom pB,
            long pPreferredWarId)
        {
            War preferred = FindWar(pPreferredWarId);
            if (preferred?.data != null && !preferred.hasEnded() &&
                ((preferred.isAttacker(pA) && preferred.isDefender(pB)) ||
                 (preferred.isDefender(pA) && preferred.isAttacker(pB))))
                return preferred;
            try
            {
                foreach (War war in pA.getWars())
                    if (war?.data != null && !war.hasEnded() &&
                        ((war.isAttacker(pA) && war.isDefender(pB)) ||
                         (war.isDefender(pA) && war.isAttacker(pB))))
                        return war;
            }
            catch { }
            return null;
        }

        private static War FindWar(long pWarId)
        {
            try { return pWarId >= 0 ? World.world?.wars?.get(pWarId) : null; }
            catch { return null; }
        }

        private static bool SafeAllied(Kingdom pA, Kingdom pB)
        {
            try { return Alliance.isSame(pA?.getAlliance(), pB?.getAlliance()); }
            catch { return false; }
        }

        private static bool SharedEnemy(Kingdom pA, Kingdom pB)
        {
            try
            {
                foreach (War left in pA.getWars())
                    foreach (War right in pB.getWars())
                        if (left?.data != null && left == right &&
                            left.isAttacker(pA) == left.isAttacker(pB))
                            return true;
            }
            catch { }
            return false;
        }

        private static WarSettlementAiFacts BuildWarSettlementFacts(
            Kingdom pRequester, Kingdom pOpponent, War pWar)
        {
            bool requesterAttacker = pWar?.isAttacker(pRequester) == true;
            int requesterWarriors = requesterAttacker
                ? SafeWarValue(() => pWar.countAttackersWarriors())
                : SafeWarValue(() => pWar.countDefendersWarriors());
            int opponentWarriors = requesterAttacker
                ? SafeWarValue(() => pWar.countDefendersWarriors())
                : SafeWarValue(() => pWar.countAttackersWarriors());
            int requesterDead = requesterAttacker
                ? SafeWarValue(() => pWar.getDeadAttackers())
                : SafeWarValue(() => pWar.getDeadDefenders());
            int opponentDead = requesterAttacker
                ? SafeWarValue(() => pWar.getDeadDefenders())
                : SafeWarValue(() => pWar.getDeadAttackers());
            int requesterCities = requesterAttacker
                ? SafeWarValue(() => pWar.countAttackersCities())
                : SafeWarValue(() => pWar.countDefendersCities());
            int opponentCities = requesterAttacker
                ? SafeWarValue(() => pWar.countDefendersCities())
                : SafeWarValue(() => pWar.countAttackersCities());

            EnsureWarSettlementBaseline(pWar);
            WarScoreSnapshot requesterScore = null;
            WarScoreService.TryGetSnapshot(pWar, pRequester,
                out requesterScore);
            int initialRequesterWarriors = ReadWarBaseline(pWar,
                requesterAttacker ? SettlementInitialAttackerWarriors :
                SettlementInitialDefenderWarriors, requesterWarriors);
            int initialOpponentWarriors = ReadWarBaseline(pWar,
                requesterAttacker ? SettlementInitialDefenderWarriors :
                SettlementInitialAttackerWarriors, opponentWarriors);
            if (requesterScore != null)
            {
                int snapshotRequesterBaseline = requesterAttacker
                    ? requesterScore.AttackerMobilizationBaseline
                    : requesterScore.DefenderMobilizationBaseline;
                int snapshotOpponentBaseline = requesterAttacker
                    ? requesterScore.DefenderMobilizationBaseline
                    : requesterScore.AttackerMobilizationBaseline;
                if (snapshotRequesterBaseline > 0)
                    initialRequesterWarriors = snapshotRequesterBaseline;
                if (snapshotOpponentBaseline > 0)
                    initialOpponentWarriors = snapshotOpponentBaseline;
            }
            int initialRequesterCities = ReadWarBaseline(pWar,
                requesterAttacker ? SettlementInitialAttackerCities :
                SettlementInitialDefenderCities, requesterCities);
            int initialOpponentCities = ReadWarBaseline(pWar,
                requesterAttacker ? SettlementInitialDefenderCities :
                SettlementInitialAttackerCities, opponentCities);

            float requesterLoss = LossRatio(requesterDead,
                initialRequesterWarriors);
            float opponentLoss = LossRatio(opponentDead,
                initialOpponentWarriors);
            float requesterCitiesLost = CityLossRatio(requesterCities,
                initialRequesterCities);
            float opponentCitiesLost = CityLossRatio(opponentCities,
                initialOpponentCities);
            int years = SafeWarValue(() => pWar.getDuration());
            ReadRealmCondition(pRequester, out float foodSecurity,
                out float order);
            CourtSnapshot court = CourtService.GetSnapshot(pRequester);
            Actor ruler = pRequester?.king;
            bool weakRuler = IsWeakSettlementRuler(ruler);
            bool resoluteRuler = IsResoluteSettlementRuler(ruler);
            bool peaceCourt = IsPeaceCourtDominant(court);
            bool warCourt = IsWarCourtDominant(court);
            float requesterFatigue = WarFatigue(years, requesterLoss,
                requesterCitiesLost, foodSecurity);
            float opponentFatigue = WarFatigue(years, opponentLoss,
                opponentCitiesLost, 1f);
            int requesterSignedScore = requesterScore?.Score ?? 0;
            int opponentSignedScore = -requesterSignedScore;
            if (WarScoreService.TryGetSnapshot(pWar, pOpponent,
                    out WarScoreSnapshot opponentScore))
                opponentSignedScore = opponentScore.Score;
            WarMilitaryFacts requesterMilitary =
                WarMilitaryFactsService.Build(pRequester, pWar,
                    requesterSignedScore);
            WarMilitaryFacts opponentMilitary =
                WarMilitaryFactsService.Build(pOpponent, pWar,
                    opponentSignedScore);

            return new WarSettlementAiFacts(years,
                requesterWarriors / (float)Math.Max(1, opponentWarriors),
                requesterLoss, opponentLoss, requesterCitiesLost,
                opponentCitiesLost,
                IsCapitalThreatened(pWar, pRequester),
                IsCapitalThreatened(pWar, pOpponent),
                IsBorderThreatened(pWar, pRequester), requesterFatigue,
                opponentFatigue, foodSecurity, order, weakRuler,
                resoluteRuler, peaceCourt, warCourt,
                IsHighLegitimacyWar(pWar, pRequester),
                requesterMilitary.AvailableFieldArmies,
                opponentMilitary.AvailableFieldArmies,
                requesterMilitary.FrontCollapsed,
                opponentMilitary.FrontCollapsed,
                requesterMilitary.AverageSupply,
                opponentMilitary.AverageSupply,
                requesterMilitary.AverageOrganization,
                opponentMilitary.AverageOrganization,
                requesterMilitary.CanCounterattack,
                opponentMilitary.CanCounterattack,
                requesterWarExhaustion: requesterScore == null ? 0 :
                    requesterAttacker
                        ? requesterScore.AttackerExhaustion
                        : requesterScore.DefenderExhaustion,
                opponentWarExhaustion: requesterScore == null ? 0 :
                    requesterAttacker
                        ? requesterScore.DefenderExhaustion
                        : requesterScore.AttackerExhaustion);
        }

        private static void EnsureWarSettlementBaseline(War pWar)
        {
            if (pWar?.data == null) return;
            pWar.data.get(SettlementInitialAttackerCities,
                out int initialCities, -1);
            if (initialCities < 0) RegisterWarSettlementBaseline(pWar);
        }

        private static int ReadWarBaseline(War pWar, string pKey,
            int pFallback)
        {
            if (pWar?.data == null) return Math.Max(0, pFallback);
            pWar.data.get(pKey, out int value, Math.Max(0, pFallback));
            return Math.Max(0, value);
        }

        private static int SafeWarValue(Func<int> pRead)
        {
            try { return Math.Max(0, pRead()); }
            catch { return 0; }
        }

        private static float LossRatio(int pDead, int pMobilizationBaseline)
        {
            int baseline = Math.Max(0, pMobilizationBaseline);
            return baseline <= 0 ? 0f : Math.Min(1f,
                Math.Max(0, pDead) / (float)baseline);
        }

        private static float CityLossRatio(int pCurrent, int pInitial)
        {
            if (pInitial <= 0) return pCurrent <= 0 ? 1f : 0f;
            return Math.Max(0f, Math.Min(1f,
                (pInitial - Math.Max(0, pCurrent)) / (float)pInitial));
        }

        private static float WarFatigue(int pYears, float pLossRatio,
            float pCitiesLostRatio, float pFoodSecurity)
        {
            return Math.Max(0f, Math.Min(1f,
                Math.Min(1f, Math.Max(0, pYears) / 15f) * .35f +
                pLossRatio * .45f + pCitiesLostRatio * .30f +
                (1f - Math.Max(0f, Math.Min(1f, pFoodSecurity))) * .15f));
        }

        private static void ReadRealmCondition(Kingdom pKingdom,
            out float pFoodSecurity, out float pOrder)
        {
            int population = 0;
            int hungry = 0;
            int cities = 0;
            int citiesWithFood = 0;
            float loyalty = 0f;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    cities++;
                    int cityPopulation = Math.Max(0,
                        city.status?.population ?? city.getPopulationPeople());
                    population += cityPopulation;
                    hungry += Math.Max(0, city.status?.hungry ?? 0);
                    if (city.countFoodTotal() > 0) citiesWithFood++;
                    loyalty += Math.Max(0, Math.Min(100,
                        city.getLoyalty())) / 100f;
                }
            }
            catch { }
            float hungerSecurity = population <= 0 ? .5f :
                1f - Math.Min(1f, hungry / (float)population);
            float stockSecurity = cities <= 0 ? 0f :
                citiesWithFood / (float)cities;
            pFoodSecurity = Math.Max(0f, Math.Min(1f,
                hungerSecurity * .7f + stockSecurity * .3f));
            float cityOrder = cities <= 0 ? 0f : loyalty / cities;
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            pOrder = Math.Max(0f, Math.Min(1f,
                cityOrder * .7f + (court?.order ?? .5f) * .3f));
        }

        private static bool IsCapitalThreatened(War pWar,
            Kingdom pKingdom)
        {
            return IsCityThreatened(pWar, pKingdom, pKingdom?.capital);
        }

        private static bool IsBorderThreatened(War pWar,
            Kingdom pKingdom)
        {
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (IsCityThreatened(pWar, pKingdom, city)) return true;
            }
            catch { }
            return false;
        }

        private static bool IsCityThreatened(War pWar, Kingdom pKingdom,
            City pCity)
        {
            if (pWar?.data == null || pKingdom?.data == null ||
                pCity?.data == null || pCity.isRekt()) return false;
            try { if (pCity.isGettingCaptured()) return true; }
            catch { }
            try
            {
                foreach (Kingdom neighbor in pCity.neighbours_kingdoms)
                {
                    if (neighbor?.data == null) continue;
                    if (pWar.isAttacker(pKingdom) &&
                        pWar.isDefender(neighbor) ||
                        pWar.isDefender(pKingdom) &&
                        pWar.isAttacker(neighbor)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsWeakSettlementRuler(Actor pRuler)
        {
            if (pRuler?.data == null || !pRuler.isAlive() ||
                pRuler.isRekt()) return true;
            try { if (pRuler.isBaby()) return true; }
            catch { }
            float ability = SafeStat(pRuler, "warfare") +
                            SafeStat(pRuler, "diplomacy") +
                            SafeStat(pRuler, "stewardship");
            return ability < 18f || SafeHasTrait(pRuler, "peaceful") &&
                SafeStat(pRuler, "warfare") < 8f;
        }

        private static bool IsResoluteSettlementRuler(Actor pRuler)
        {
            if (pRuler?.data == null) return false;
            float warfare = SafeStat(pRuler, "warfare");
            return SafeHasTrait(pRuler, "ambitious") ||
                   SafeHasTrait(pRuler, "strong_minded") ||
                   SafeHasTrait(pRuler, "bloodlust") || warfare >= 12f &&
                   warfare >= SafeStat(pRuler, "diplomacy") &&
                   warfare >= SafeStat(pRuler, "stewardship");
        }

        private static bool SafeHasTrait(Actor pActor, string pTrait)
        {
            try { return pActor?.hasTrait(pTrait) == true; }
            catch { return false; }
        }

        private static bool IsPeaceCourtDominant(CourtSnapshot pCourt)
        {
            string school = pCourt?.dominant_school ?? "";
            bool peaceSchool = school == CourtSchoolId.Dao ||
                               school == CourtSchoolId.Mohist ||
                               school == CourtSchoolId.Diplomat;
            return peaceSchool || (pCourt?.peace ?? .5f) >= .62f &&
                   (pCourt?.peace ?? .5f) > (pCourt?.war ?? .5f) &&
                   (pCourt?.peace ?? .5f) > (pCourt?.aggression ?? .5f);
        }

        private static bool IsWarCourtDominant(CourtSnapshot pCourt)
        {
            string school = pCourt?.dominant_school ?? "";
            return school == CourtSchoolId.Military ||
                   school == CourtSchoolId.Warrior ||
                   (pCourt?.war ?? .5f) + (pCourt?.aggression ?? .5f) >
                   (pCourt?.peace ?? .5f) + .85f;
        }

        private static bool IsHighLegitimacyWar(War pWar,
            Kingdom pRequester)
        {
            if (pWar?.data == null || pRequester?.data == null) return false;
            if (pWar.isDefender(pRequester)) return true;
            string type = "";
            try { type = pWar.getAsset()?.id ?? ""; }
            catch { }
            return type == "independence_war" ||
                   type == WarDecisionService.WAR_RESTORATION ||
                   type == SuccessionDisputeRules.WarTypeId ||
                   type == CoupRestorationRules.WarTypeId ||
                   type == FeudatoryJingnanRules.WarTypeId ||
                   type == MandateService.WAR_TIANMING_REBEL ||
                   type == GeneralRebellionService.WAR_GENERAL_REBELLION ||
                   type == GeneralRebellionService.WAR_FIEF_INDEPENDENCE;
        }

        private static bool IsWarLeaderPair(War pWar, Kingdom pFirst,
            Kingdom pSecond)
        {
            if (pWar?.data == null || pFirst?.data == null ||
                pSecond?.data == null) return false;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            return attacker == pFirst && defender == pSecond ||
                   attacker == pSecond && defender == pFirst;
        }

        private static bool CanNegotiatePeacePair(War pWar,
            Kingdom pRequester, Kingdom pResponder)
        {
            return TryResolvePeaceScope(pWar, pRequester, pResponder,
                out _, out _, out _);
        }

        private static bool TryResolvePeaceScope(War pWar,
            Kingdom pRequester, Kingdom pResponder,
            out WarPeaceSettlementScopeKind pScope,
            out long pExitRootKingdomId, out string pReason)
        {
            pScope = WarPeaceSettlementScopeKind.Coalition;
            pExitRootKingdomId = -1L;
            pReason = "not_war_leader";
            if (pWar?.data == null || pRequester?.data == null ||
                pResponder?.data == null || pWar.hasEnded()) return false;
            if (ZhuluPeaceGuard.BlocksOrdinarySettlement(pWar))
            {
                pReason = ZhuluPeaceGuard.Reason(pWar);
                return false;
            }

            bool requesterLeader = IsWarLeader(pWar, pRequester);
            bool responderLeader = IsWarLeader(pWar, pResponder);
            if (requesterLeader && responderLeader)
            {
                if (!IsWarLeaderPair(pWar, pRequester, pResponder))
                    return false;
                pReason = "";
                return true;
            }
            if (requesterLeader == responderLeader) return false;

            pScope = WarPeaceSettlementScopeKind.SeparateParticipant;
            pExitRootKingdomId = requesterLeader
                ? pResponder.id
                : pRequester.id;
            if (!WarParticipantRosterService.TryBuildReadOnly(pWar,
                    pExitRootKingdomId,
                    out WarParticipantRosterContext context,
                    out pReason) ||
                !context.TryGet(pRequester.id,
                    out WarParticipantRosterEntry requesterEntry) ||
                !context.TryGet(pResponder.id,
                    out WarParticipantRosterEntry responderEntry) ||
                !context.TryGet(pExitRootKingdomId,
                    out WarParticipantRosterEntry exitRoot))
            {
                if (string.IsNullOrEmpty(pReason))
                    pReason = "participant_roster_changed";
                return false;
            }
            var authority = new WarPeaceNegotiationAuthorityFacts(
                sameWar: true,
                opposingSides: requesterEntry.Side != responderEntry.Side &&
                    requesterEntry.Side != WarParticipantSideKind.Unknown &&
                    responderEntry.Side != WarParticipantSideKind.Unknown,
                requesterIsParticipant: true,
                responderIsParticipant: true,
                requesterIsWarLeader: requesterLeader,
                responderIsWarLeader: responderLeader,
                exitRootRole: exitRoot.Role);
            if (!WarPeaceSettlementScopeRules.CanNegotiate(pScope,
                    authority))
            {
                pReason = "separate_peace_not_authorized";
                return false;
            }
            pReason = "";
            return true;
        }

        private static bool IsWarLeader(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return false;
            try
            {
                return pWar.getMainAttacker() == pKingdom ||
                       pWar.getMainDefender() == pKingdom;
            }
            catch { return false; }
        }

        private static WarWinner WinnerForKingdom(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null)
                return WarWinner.Nobody;
            if (pWar.getMainAttacker() == pKingdom)
                return WarWinner.Attackers;
            if (pWar.getMainDefender() == pKingdom)
                return WarWinner.Defenders;
            return WarWinner.Nobody;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static float CapitalDistance(Kingdom pRequester,
            Kingdom pResponder)
        {
            try
            {
                WorldTile requesterTile = pRequester?.capital?.getTile();
                WorldTile responderTile = pResponder?.capital?.getTile();
                if (requesterTile != null && responderTile != null)
                    return Toolbox.DistTile(requesterTile, responderTile);
            }
            catch { }
            return 60f;
        }

        private static DiplomacyLetterStyle ResolveLetterStyle(
            Kingdom pSpeaker, Kingdom pRecipient)
        {
            bool speakerIsSuzerain = IsDirectSubjectOf(pRecipient, pSpeaker);
            bool speakerIsSubject = IsDirectSubjectOf(pSpeaker, pRecipient);
            return DiplomacyConversationRules.ResolveLetterStyle(
                MandateService.IsMandateKingdom(pSpeaker),
                speakerIsSuzerain, speakerIsSubject);
        }

        private static DiplomacyLetterTone ResolveLetterTone(
            Kingdom pSpeaker, Kingdom pRecipient)
        {
            int opinion = 0;
            bool atWar = false;
            try
            {
                opinion = DiplomacyOpinionService.Read(pSpeaker, pRecipient);
                atWar = pSpeaker?.isEnemy(pRecipient) == true;
            }
            catch { }
            return DiplomacyConversationRules.ResolveLetterTone(opinion,
                atWar);
        }

        private static bool IsDirectSubjectOf(Kingdom pSubject,
            Kingdom pSuzerain)
        {
            if (pSubject?.data == null || pSuzerain?.data == null)
                return false;
            return VassalService.GetSuzerain(pSubject) == pSuzerain ||
                   VassalService.GetTributarySuzerain(pSubject) == pSuzerain;
        }

        private static string DiplomaticSenderTitle(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            string actorName = pKingdom.king?.getName() ?? "";
            string title = RulerAppellationService.GetFullLivingAppellation(
                pKingdom);
            if (string.IsNullOrEmpty(title))
                title = KingdomTitleService.GetTitleChar(
                    KingdomTitleService.GetTitle(pKingdom));
            if (string.IsNullOrEmpty(actorName)) return title ?? "";
            return string.IsNullOrEmpty(title) || actorName.Contains(title)
                ? actorName
                : actorName + " · " + title;
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private static bool TryBreakNonAggression(Kingdom pRequester,
            Kingdom pResponder, bool pPlayerInitiated, out string pReason)
        {
            int year = SafeYear();
            double now = LineageService.CurTime();
            var request = new DiplomacyTreatyBreakRequest(
                pRequester.id, pRequester.name ?? "", pResponder.id,
                pResponder.name ?? "", year,
                year + DiplomacyProposalRules.BrokenPactTruceYears, now,
                DiplomaticSenderTitle(pRequester),
                HistoryWriter.BuildYearPrefix(now, pRequester),
                DiplomacyConversationRules.LetterStyleId(
                    ResolveLetterStyle(pRequester, pResponder)),
                DiplomacyConversationRules.LetterToneId(
                    ResolveLetterTone(pRequester, pResponder)),
                pPlayerInitiated);
            DiplomacyTreatyBreakOutcome outcome =
                DiplomacyTreatyPersistence.BreakNonAggression(DB,
                    DiplomacyProposalTableItem.GetTableName(), request,
                    out _);
            if (outcome == DiplomacyTreatyBreakOutcome.NoActivePact)
            {
                pReason = "no_active_non_aggression";
                return false;
            }
            if (outcome != DiplomacyTreatyBreakOutcome.Committed)
            {
                pReason = "write_failed";
                return false;
            }
            int truceUntil = year +
                             DiplomacyProposalRules.BrokenPactTruceYears;
            DiplomacyConversationService.RecordNonAggressionBroken(
                pRequester, pResponder, truceUntil);
            NotifyPair(pRequester.id, pResponder.id);
            ReconcilePendingDeclarationsForActiveTreaty(
                pRequester, pResponder);
            pReason = "";
            return true;
        }

        private static bool EnsureAllianceWithdrawalTruce(
            DiplomacyProposal pProposal, Kingdom pRequester,
            Kingdom pResponder, out int pTruceUntilYear)
        {
            int year = SafeYear();
            pTruceUntilYear = year +
                               DiplomacyProposalRules
                                   .BrokenPactTruceYears;
            double now = LineageService.CurTime();
            var request = new DiplomacyTreatyBreakRequest(
                pRequester.id, pRequester.name ?? "", pResponder.id,
                pResponder.name ?? "", year, pTruceUntilYear, now,
                DiplomaticSenderTitle(pRequester),
                HistoryWriter.BuildYearPrefix(now, pRequester),
                DiplomacyConversationRules.LetterStyleId(
                    ResolveLetterStyle(pRequester, pResponder)),
                DiplomacyConversationRules.LetterToneId(
                    ResolveLetterTone(pRequester, pResponder)),
                pProposal?.PlayerInitiated == true);
            bool committed = pProposal != null &&
                             DiplomacyTreatyPersistence.EnsureProposalTruce(
                                 DB,
                                 DiplomacyProposalTableItem.GetTableName(),
                                 pProposal.ProposalId, request, out _);
            if (committed)
                ReconcilePendingDeclarationsForActiveTreaty(
                    pRequester, pResponder);
            return committed;
        }

        private static string TypeId(DiplomacyProposalType pType)
        {
            return pType switch
            {
                DiplomacyProposalType.Alliance => "alliance",
                DiplomacyProposalType.Peace => "peace",
                DiplomacyProposalType.NonAggression => "non_aggression",
                DiplomacyProposalType.JoinWar => "join_war",
                DiplomacyProposalType.Vassalize => "vassalize",
                DiplomacyProposalType.Tributary => "tributary",
                DiplomacyProposalType.EndAlliance => "end_alliance",
                DiplomacyProposalType.EndVassal => "end_vassal",
                DiplomacyProposalType.Truce => "truce",
                DiplomacyProposalType.BreakNonAggression =>
                    "break_non_aggression",
                DiplomacyProposalType.Coalition => "coalition",
                DiplomacyProposalType.RoyalMarriage => "royal_marriage",
                DiplomacyProposalType.Surrender => "surrender",
                DiplomacyProposalType.EnforceDemands => "enforce_demands",
                DiplomacyProposalType.HouseholdOffering =>
                    "household_offering",
                _ => "none"
            };
        }

        private static DiplomacyProposalType ParseType(string pType)
        {
            return pType switch
            {
                "alliance" => DiplomacyProposalType.Alliance,
                "peace" => DiplomacyProposalType.Peace,
                "non_aggression" => DiplomacyProposalType.NonAggression,
                "join_war" => DiplomacyProposalType.JoinWar,
                "vassalize" => DiplomacyProposalType.Vassalize,
                "tributary" => DiplomacyProposalType.Tributary,
                "end_alliance" => DiplomacyProposalType.EndAlliance,
                "end_vassal" => DiplomacyProposalType.EndVassal,
                "truce" => DiplomacyProposalType.Truce,
                "break_non_aggression" =>
                    DiplomacyProposalType.BreakNonAggression,
                "coalition" => DiplomacyProposalType.Coalition,
                "royal_marriage" => DiplomacyProposalType.RoyalMarriage,
                "surrender" => DiplomacyProposalType.Surrender,
                "enforce_demands" => DiplomacyProposalType.EnforceDemands,
                "household_offering" =>
                    DiplomacyProposalType.HouseholdOffering,
                _ => DiplomacyProposalType.None
            };
        }

        private static string StatusId(DiplomacyProposalStatus pStatus)
        {
            return pStatus switch
            {
                DiplomacyProposalStatus.Accepted => "accepted",
                DiplomacyProposalStatus.Rejected => "rejected",
                DiplomacyProposalStatus.Expired => "expired",
                DiplomacyProposalStatus.Cancelled => "cancelled",
                DiplomacyProposalStatus.Processing => "processing",
                _ => "pending"
            };
        }

        private static DiplomacyProposalStatus ParseStatus(string pStatus)
        {
            return pStatus switch
            {
                "accepted" => DiplomacyProposalStatus.Accepted,
                "rejected" => DiplomacyProposalStatus.Rejected,
                "expired" => DiplomacyProposalStatus.Expired,
                "cancelled" => DiplomacyProposalStatus.Cancelled,
                "processing" => DiplomacyProposalStatus.Processing,
                _ => DiplomacyProposalStatus.Pending
            };
        }

        private static string ReadString(SQLiteDataReader pReader,
            int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : pReader.GetString(pIndex);
        }
    }
}
