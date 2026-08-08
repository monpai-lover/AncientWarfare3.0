using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.ui.windows
{
    internal static class WarPeaceNegotiationController
    {
        private sealed class Session
        {
            public War War;
            public Kingdom Requester;
            public Kingdom Responder;
            public WarPeaceSettlementScopeKind Scope;
            public long ExitRootKingdomId = -1L;
            public List<WarPeaceSettlementParticipantSnapshot> Participants =
                new List<WarPeaceSettlementParticipantSnapshot>();
            public WarPeaceNegotiationPresentation Presentation;
            public Dictionary<string,
                WarPeaceSettlementTermDraft> Terms = new(
                StringComparer.Ordinal);
        }

        private static Session _session;
        private static bool _subscribed;

        public static bool Open(Kingdom pRequester, Kingdom pResponder)
        {
            if (!TryGetNegotiationContext(pRequester, pResponder,
                    out War war, out WarScoreSnapshot score,
                    out WarPeaceSettlementScopeKind scope,
                    out long exitRootKingdomId,
                    out List<WarPeaceSettlementParticipantSnapshot>
                        participants, out _))
                return false;

            EnsureSubscribed();
            WarPeaceDefaultOfferMode mode = ToSettlementMode(
                WarPeaceNegotiationOfferRules.ResolveInitialMode(
                    score.Score));
            var session = new Session
            {
                War = war,
                Requester = pRequester,
                Responder = pResponder,
                Scope = scope,
                ExitRootKingdomId = exitRootKingdomId,
                Participants = participants
            };
            WarPeaceSettlementDraft defaults =
                WarPeaceSettlementService.Instance.BuildDefaultDraft(war,
                    pRequester, pResponder, score.Score, mode);
            IReadOnlyList<WarPeaceTermPresentation> terms =
                BuildTermPresentations(session, score.Score,
                    defaults.Terms);

            bool requesterAttacker = war.isAttacker(pRequester);
            int requesterExhaustion = requesterAttacker
                ? score.AttackerExhaustion
                : score.DefenderExhaustion;
            int responderExhaustion = war.isAttacker(pResponder)
                ? score.AttackerExhaustion
                : score.DefenderExhaustion;
            int resolve = ResolveCourtResolve(pResponder);
            var requesterBreakdown = new WarPeaceScoreBreakdown(
                score.CityScore, score.BattleScore,
                score.GoalScore + score.LossScore, score.Score,
                score.DecisiveScore);
            var responderBreakdown = new WarPeaceScoreBreakdown(
                -score.CityScore, -score.BattleScore,
                -score.GoalScore - score.LossScore, -score.Score,
                -score.DecisiveScore);
            session.Presentation = new WarPeaceNegotiationPresentation(
                war.name,
                Party(pRequester, war), Party(pResponder, war), requesterBreakdown,
                responderBreakdown, terms,
                new WarPeaceAcceptanceContext(0, resolve,
                    responderExhaustion, Math.Max(0, score.Score)), "",
                requesterExhaustion, responderExhaustion,
                WarPeaceSettlementScopeRules.ScopeId(scope),
                BuildExitParticipantNames(participants));
            _session = session;
            WarPeaceNegotiationWindow.Open(session.Presentation);
            return true;
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            WarPeaceNegotiationWindow.SubmitRequested += Submit;
            _subscribed = true;
        }

        private static void Submit(
            WarPeaceNegotiationPresentation pPresentation,
            IReadOnlyList<string> pSelectedTermIds)
        {
            Session session = _session;
            if (session?.War?.data == null ||
                !ReferenceEquals(session.Presentation, pPresentation)) return;
            var draft = new WarPeaceSettlementDraft
            {
                WarId = session.War.data.id,
                RequesterKingdomId = session.Requester.id,
                ResponderKingdomId = session.Responder.id,
                Scope = session.Scope,
                ExitRootKingdomId = session.ExitRootKingdomId,
                PlayerInitiated = true
            };
            for (int i = 0; i < session.Participants.Count; i++)
                draft.Participants.Add(session.Participants[i].Clone());
            if (WarScoreService.TryGetSnapshot(session.War,
                    session.Requester, out WarScoreSnapshot current))
                draft.SignedWarScore = current.Score;
            for (int i = 0; i < (pSelectedTermIds?.Count ?? 0); i++)
                if (session.Terms.TryGetValue(pSelectedTermIds[i],
                        out WarPeaceSettlementTermDraft term))
                    draft.Terms.Add(term.Clone());
            if (draft.Terms.Count == 0)
            {
                string message = AW_L10n.Text(
                    "aw_war_peace_disabled_no_terms",
                    "Select at least one peace term");
                WarPeaceNegotiationWindow.ShowSubmitFailure(message);
                WorldTip.showNow(message, false, "top");
                return;
            }

            WarPeaceNegotiationSelectionSummary selection =
                WarPeaceNegotiationSelectionRules.Summarize(
                    pPresentation, pSelectedTermIds);
            string proposalType = WarPeaceNegotiationOfferRules.ResolveProposalTypeId(
                    selection.NetTermValueForRecipient,
                    draft.SignedWarScore);
            AW3CommandResult result;
            try
            {
                result = AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.CreateWarPeaceProposal(
                        session.Requester.id, session.Responder.id,
                        proposalType, session.War.data.id,
                        WarPeaceDraftCodec.Serialize(draft)));
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "War peace proposal submit failed: war=" +
                    session.War.data.id + " requester=" +
                    session.Requester.id + " responder=" +
                    session.Responder.id + " exception=" +
                    exception.GetType().Name + ": " + exception.Message);
                result = AW3CommandResult.Rejected(
                    AW3CommandError.ExecutionFailed,
                    "peace_submit_exception");
            }
            if (result?.Accepted != true && result?.Status !=
                    AW3CommandStatus.Pending)
            {
                string key = DiplomacyFailureReasonRules.StableKey(
                    result?.MessageKey ?? "execution_failed");
                string message = DiplomacyConversationWindow
                    .ProposalFailure(key);
                WarPeaceNegotiationWindow.ShowSubmitFailure(message);
                WorldTip.showNow(message, false, "top");
                return;
            }
            WarPeaceNegotiationWindow.ClearSubmitFailure();
            DiplomacyConversationWindow.Open(session.Requester.id);
        }

        private static War FindLeaderWar(Kingdom pRequester,
            Kingdom pResponder)
        {
            int inspected = 0;
            try
            {
                foreach (War war in pRequester.getWars())
                {
                    if (inspected++ >= 16) break;
                    if (war?.data == null || war.hasEnded()) continue;
                    bool leaders = war.isMainAttacker(pRequester) &&
                                   war.isMainDefender(pResponder) ||
                                   war.isMainAttacker(pResponder) &&
                                   war.isMainDefender(pRequester);
                    if (leaders) return war;
                }
            }
            catch { }
            return null;
        }

        internal static bool TryGetMenuWarScore(Kingdom pRequester,
            Kingdom pResponder, out int pScore, out string pReason)
        {
            pScore = 0;
            if (!TryGetNegotiationContext(pRequester, pResponder,
                    out _, out WarScoreSnapshot score, out _, out _,
                    out _, out pReason))
                return false;
            pScore = score.Score;
            return true;
        }

        internal static bool TryRefreshLivePresentation(
            WarPeaceNegotiationPresentation pCurrent,
            out WarPeaceNegotiationPresentation pUpdated)
        {
            pUpdated = pCurrent;
            Session session = _session;
            if (pCurrent == null || session?.War?.data == null ||
                !ReferenceEquals(session.Presentation, pCurrent))
                return false;

            WarPeaceScoreBreakdown requesterScore =
                pCurrent.RequesterScore;
            WarPeaceScoreBreakdown responderScore =
                pCurrent.ResponderScore;
            IReadOnlyList<WarPeaceTermPresentation> terms = pCurrent.Terms;
            WarPeaceAcceptanceContext acceptance = pCurrent.Acceptance;
            int requesterExhaustion = pCurrent.RequesterExhaustion;
            int responderExhaustion = pCurrent.ResponderExhaustion;
            string disabledReason = string.Empty;
            WarPeacePartyPresentation requesterParty =
                Party(session.Requester, session.War);
            WarPeacePartyPresentation responderParty =
                Party(session.Responder, session.War);
            bool partyChanged = WarPeaceNegotiationLiveStateRules
                .HasPartyChanged(pCurrent.Requester, pCurrent.Responder,
                    requesterParty, responderParty);
            War war = session.War;
            if (war.hasEnded())
                disabledReason = "war_no_longer_active";
            else if (ZhuluPeaceGuard.BlocksOrdinarySettlement(war))
                disabledReason = ZhuluPeaceGuard.Reason(war);
            else if (RebellionDirectTerritoryTransferService.
                         BlocksOrdinarySettlement(war))
                disabledReason = RebellionDirectTerritoryTransferRules.
                    SettlementBlockedReason;
            else
            {
                if (!TryResolveSettlementScope(war, session.Requester,
                        session.Responder,
                        out WarPeaceSettlementScopeKind liveScope,
                        out long liveExitRootId,
                        out WarParticipantRosterContext liveContext,
                        out disabledReason))
                {
                }
                else if (liveScope != session.Scope ||
                         liveExitRootId != session.ExitRootKingdomId ||
                         !liveContext.ValidateParticipantSnapshots(
                             session.Participants, out disabledReason))
                    disabledReason = "participant_roster_changed";
                else if (WarScoreService.TryGetSnapshot(war,
                             session.Requester,
                             out WarScoreSnapshot score))
                {
                    requesterScore = new WarPeaceScoreBreakdown(
                        score.CityScore, score.BattleScore,
                        score.GoalScore + score.LossScore, score.Score,
                        score.DecisiveScore);
                    responderScore = new WarPeaceScoreBreakdown(
                        -score.CityScore, -score.BattleScore,
                        -score.GoalScore - score.LossScore, -score.Score,
                        -score.DecisiveScore);
                    requesterExhaustion =
                        war.isAttacker(session.Requester)
                            ? score.AttackerExhaustion
                            : score.DefenderExhaustion;
                    responderExhaustion =
                        war.isAttacker(session.Responder)
                        ? score.AttackerExhaustion
                        : score.DefenderExhaustion;
                    acceptance = new WarPeaceAcceptanceContext(
                        0,
                        ResolveCourtResolve(session.Responder),
                        responderExhaustion,
                        Math.Max(0, score.Score));
                    terms = BuildTermPresentations(session, score.Score,
                        null);
                }
                else
                    disabledReason = "war_score_unavailable";
            }

            if (!partyChanged &&
                !WarPeaceNegotiationLiveStateRules.HasChanged(pCurrent,
                    requesterScore, responderScore, terms, acceptance,
                    requesterExhaustion, responderExhaustion,
                    disabledReason)) return false;
            pUpdated = new WarPeaceNegotiationPresentation(
                pCurrent.WarName, requesterParty, responderParty,
                requesterScore, responderScore, terms, acceptance,
                disabledReason, requesterExhaustion, responderExhaustion,
                WarPeaceSettlementScopeRules.ScopeId(session.Scope),
                BuildExitParticipantNames(session.Participants));
            session.Presentation = pUpdated;
            return true;
        }

        private static bool TryGetNegotiationContext(Kingdom pRequester,
            Kingdom pResponder, out War pWar, out WarScoreSnapshot pScore,
            out WarPeaceSettlementScopeKind pScope,
            out long pExitRootKingdomId,
            out List<WarPeaceSettlementParticipantSnapshot> pParticipants,
            out string pReason)
        {
            pWar = null;
            pScore = null;
            pScope = WarPeaceSettlementScopeKind.Coalition;
            pExitRootKingdomId = -1L;
            pParticipants = new List<WarPeaceSettlementParticipantSnapshot>();
            pReason = "unavailable";
            if (pRequester?.data == null || pResponder?.data == null)
                return false;
            War sharedWar = FindSharedWar(pRequester, pResponder);
            if (sharedWar?.data == null)
            {
                pReason = "not_at_war";
                return false;
            }
            pWar = sharedWar;
            if (ZhuluPeaceGuard.BlocksOrdinarySettlement(sharedWar))
            {
                pReason = ZhuluPeaceGuard.Reason(sharedWar);
                return false;
            }
            if (RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(sharedWar))
            {
                pReason = RebellionDirectTerritoryTransferRules.
                    SettlementBlockedReason;
                return false;
            }
            if (!TryResolveSettlementScope(sharedWar, pRequester,
                    pResponder, out pScope, out pExitRootKingdomId,
                    out WarParticipantRosterContext context, out pReason))
                return false;
            pParticipants = context.BuildParticipantSnapshots();
            if (!WarScoreService.TryGetSnapshot(pWar, pRequester,
                    out pScore))
            {
                pReason = "war_score_unavailable";
                return false;
            }
            pReason = string.Empty;
            return true;
        }

        private static bool TryResolveSettlementScope(War pWar,
            Kingdom pRequester, Kingdom pResponder,
            out WarPeaceSettlementScopeKind pScope,
            out long pExitRootKingdomId,
            out WarParticipantRosterContext pContext, out string pReason)
        {
            pScope = WarPeaceSettlementScopeKind.Coalition;
            pExitRootKingdomId = -1L;
            pContext = null;
            pReason = "not_war_leader";
            if (pWar?.data == null || pRequester?.data == null ||
                pResponder?.data == null) return false;

            bool requesterLeader = pWar.isMainAttacker(pRequester) ||
                                   pWar.isMainDefender(pRequester);
            bool responderLeader = pWar.isMainAttacker(pResponder) ||
                                   pWar.isMainDefender(pResponder);
            if (requesterLeader && responderLeader)
                pScope = WarPeaceSettlementScopeKind.Coalition;
            else if (requesterLeader != responderLeader)
            {
                pScope = WarPeaceSettlementScopeKind.SeparateParticipant;
                pExitRootKingdomId = requesterLeader
                    ? pResponder.id
                    : pRequester.id;
            }
            else return false;

            if (!WarParticipantRosterService.TryBuildReadOnly(pWar,
                    pExitRootKingdomId, out pContext, out pReason))
                return false;
            if (!pContext.TryGet(pRequester.id,
                    out WarParticipantRosterEntry requesterEntry) ||
                !pContext.TryGet(pResponder.id,
                    out WarParticipantRosterEntry responderEntry))
            {
                pReason = "participant_roster_changed";
                return false;
            }
            WarParticipantRoleKind exitRole =
                WarParticipantRoleKind.Unknown;
            if (pExitRootKingdomId >= 0 && pContext.TryGet(
                    pExitRootKingdomId,
                    out WarParticipantRosterEntry exitRoot))
                exitRole = exitRoot.Role;
            var authority = new WarPeaceNegotiationAuthorityFacts(
                sameWar: true,
                opposingSides: requesterEntry.Side != responderEntry.Side &&
                               requesterEntry.Side !=
                               WarParticipantSideKind.Unknown &&
                               responderEntry.Side !=
                               WarParticipantSideKind.Unknown,
                requesterIsParticipant: true,
                responderIsParticipant: true,
                requesterIsWarLeader: requesterLeader,
                responderIsWarLeader: responderLeader,
                exitRootRole: exitRole);
            if (WarPeaceSettlementScopeRules.CanNegotiate(pScope,
                    authority))
            {
                pReason = "";
                return true;
            }
            pReason = pScope == WarPeaceSettlementScopeKind.Coalition
                ? "settlement_requires_war_leaders"
                : "separate_peace_not_authorized";
            return false;
        }

        private static War FindSharedWar(Kingdom pRequester,
            Kingdom pResponder)
        {
            int inspected = 0;
            try
            {
                foreach (War war in pRequester.getWars())
                {
                    if (inspected++ >= 16) break;
                    if (war?.data != null && !war.hasEnded() &&
                        war.hasKingdom(pResponder)) return war;
                }
            }
            catch { }
            return null;
        }

        private static WarPeacePartyPresentation Party(Kingdom pKingdom,
            War pWar)
        {
            Actor ruler = pKingdom?.king;
            int armyStrength = 0;
            int casualties = 0;
            try
            {
                if (pWar?.data != null && pKingdom?.data != null)
                {
                    if (pWar.isAttacker(pKingdom))
                    {
                        armyStrength = pWar.countAttackersWarriors();
                        casualties = pWar.getDeadAttackers();
                    }
                    else if (pWar.isDefender(pKingdom))
                    {
                        armyStrength = pWar.countDefendersWarriors();
                        casualties = pWar.getDeadDefenders();
                    }
                }
            }
            catch { }
            return new WarPeacePartyPresentation(pKingdom?.id ?? -1L,
                SuccessionDisputeService.GetDisplayName(pKingdom),
                ruler?.data?.id ?? -1L,
                ruler?.getName() ?? "", CountLiveCities(pKingdom),
                armyStrength, casualties);
        }

        private static int CountLiveCities(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1;
            try { return Math.Max(0, pKingdom.countCities()); }
            catch { return -1; }
        }

        private static IReadOnlyList<string> BuildExitParticipantNames(
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot>
                pParticipants)
        {
            var names = new List<string>();
            for (int i = 0; i < (pParticipants?.Count ?? 0); i++)
            {
                WarPeaceSettlementParticipantSnapshot participant =
                    pParticipants[i];
                if (participant?.IncludedInExitGroup != true) continue;
                Kingdom kingdom = WarPeaceSettlementWorld.FindKingdom(
                    participant.KingdomId);
                string name = kingdom?.data == null
                    ? participant.KingdomId.ToString()
                    : SuccessionDisputeService.GetDisplayName(kingdom);
                if (!string.IsNullOrWhiteSpace(name) &&
                    !names.Contains(name)) names.Add(name);
            }
            return names.AsReadOnly();
        }

        private static int ResolveCourtResolve(Kingdom pKingdom)
        {
            int value = 50;
            try
            {
                CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
                value += (int)Math.Round(((court?.war ?? .5f) -
                                          (court?.peace ?? .5f)) * 30f);
                Actor ruler = pKingdom.king;
                if (ruler?.data != null)
                    value += (int)Math.Round(ruler.stats["warfare"] / 4f);
            }
            catch { }
            return Math.Max(0, Math.Min(100, value));
        }

        private static bool MatchesAny(
            IReadOnlyList<WarPeaceSettlementTermDraft> pDefaults,
            WarPeaceSettlementTermDraft pTerm)
        {
            for (int i = 0; i < (pDefaults?.Count ?? 0); i++)
            {
                WarPeaceSettlementTermDraft other = pDefaults[i];
                if (other.Kind == pTerm.Kind &&
                    other.FromKingdomId == pTerm.FromKingdomId &&
                    other.ToKingdomId == pTerm.ToKingdomId &&
                    other.CityId == pTerm.CityId &&
                    other.CaptiveActorId == pTerm.CaptiveActorId &&
                    other.ClaimId == pTerm.ClaimId &&
                    string.Equals(other.ResourceId, pTerm.ResourceId,
                        StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static IReadOnlyList<WarPeaceDefaultTermCandidate>
            BuildBilateralTermCandidates(Session pSession, int pSignedScore)
        {
            return WarPeaceBilateralCandidateRules.Build(
                pSession.Requester, pSession.Responder,
                (payer, beneficiary) => BuildScopeTermCandidates(
                    pSession, payer, beneficiary),
                WarPeaceDefaultOfferRules.MaximumCandidates);
        }

        private static IReadOnlyList<WarPeaceDefaultTermCandidate>
            BuildScopeTermCandidates(Session pSession, Kingdom pPayer,
                Kingdom pBeneficiary)
        {
            var result = new List<WarPeaceDefaultTermCandidate>(
                WarPeaceDefaultOfferRules.MaximumCandidates);
            WarPeaceSettlementParticipantSnapshot payerSnapshot =
                FindParticipantSnapshot(pSession.Participants,
                    pPayer?.id ?? -1L);
            if (payerSnapshot == null)
            {
                AppendUniqueCandidates(result,
                    WarPeaceSettlementService.Instance.
                        BuildDirectedTermCandidates(pSession.War, pPayer,
                            pBeneficiary));
                return result;
            }

            AppendUniqueCandidates(result,
                WarPeaceSettlementService.Instance.
                    BuildDirectedTermCandidates(pSession.War, pPayer,
                        pBeneficiary));
            for (int i = 0; i < pSession.Participants.Count; i++)
            {
                WarPeaceSettlementParticipantSnapshot participant =
                    pSession.Participants[i];
                if (participant == null ||
                    participant.KingdomId == pPayer.id ||
                    !string.Equals(participant.SideKind,
                        payerSnapshot.SideKind, StringComparison.Ordinal) ||
                    pSession.Scope ==
                        WarPeaceSettlementScopeKind.SeparateParticipant &&
                    !participant.IncludedInExitGroup) continue;
                Kingdom owner = WarPeaceSettlementWorld.FindKingdom(
                    participant.KingdomId);
                if (owner?.data == null || owner.isRekt()) continue;
                AppendUniqueCandidates(result,
                    WarPeaceSettlementService.Instance.
                        BuildDirectedTermCandidates(pSession.War, owner,
                            pBeneficiary));
            }
            return result;
        }

        private static WarPeaceSettlementParticipantSnapshot
            FindParticipantSnapshot(
                IReadOnlyList<WarPeaceSettlementParticipantSnapshot>
                    pParticipants, long pKingdomId)
        {
            for (int i = 0; i < (pParticipants?.Count ?? 0); i++)
                if (pParticipants[i]?.KingdomId == pKingdomId)
                    return pParticipants[i];
            return null;
        }

        private static IReadOnlyList<WarPeaceTermPresentation>
            BuildTermPresentations(Session pSession, int pSignedScore,
                IReadOnlyList<WarPeaceSettlementTermDraft> pDefaults)
        {
            IReadOnlyList<WarPeaceDefaultTermCandidate> candidates =
                BuildBilateralTermCandidates(pSession, pSignedScore);
            var terms = new List<WarPeaceTermPresentation>(
                candidates.Count);
            var drafts = new Dictionary<string,
                WarPeaceSettlementTermDraft>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                WarPeaceDefaultTermCandidate candidate = candidates[i];
                WarPeaceSettlementTermDraft term = candidate?.Term;
                if (term == null) continue;
                string id = WarPeaceTermIdentityRules.Build(term.Kind,
                    term.FromKingdomId, term.ToKingdomId, term.CityId,
                    term.CaptiveActorId, term.ClaimId, term.Amount,
                    term.DurationYears, term.ResourceId);
                if (string.IsNullOrEmpty(id) || drafts.ContainsKey(id))
                    continue;
                drafts[id] = term.Clone();
                DescribeTerm(term, out string titleKey,
                    out string titleFallback, out string descriptionKey,
                    out string descriptionFallback, out string detail);
                int cost = WarPeaceTermsRules.NormalizeTermCost(term.Kind,
                    term.RequestedCost);
                bool recipientKnown = WarPeaceSettlementValidationRules.
                    TryResolveRecipientSide(pSession.Requester.id,
                        pSession.Responder.id, pSession.Participants,
                        term.ToKingdomId,
                        out bool recipientOnRequesterSide);
                int recipientValue = term.Kind ==
                    WarPeaceTermKind.WhitePeace ? 0 :
                    recipientKnown && !recipientOnRequesterSide
                        ? cost
                        : -cost;
                terms.Add(new WarPeaceTermPresentation(id, term.Kind,
                    titleKey, titleFallback, descriptionKey,
                    descriptionFallback, term.RequestedCost,
                    recipientValue,
                    pDefaults != null && MatchesAny(pDefaults, term),
                     candidate.Eligible ? "" :
                    CandidatePrerequisiteFailure(term), detail, term.CityId));
            }
            pSession.Terms = drafts;
            return terms.AsReadOnly();
        }

        private static void AppendUniqueCandidates(
            List<WarPeaceDefaultTermCandidate> pTarget,
            IReadOnlyList<WarPeaceDefaultTermCandidate> pCandidates)
        {
            int limit = WarPeaceDefaultOfferRules.MaximumCandidates * 8;
            for (int i = 0; i < (pCandidates?.Count ?? 0) &&
                            pTarget.Count < limit; i++)
            {
                WarPeaceDefaultTermCandidate candidate = pCandidates[i];
                if (candidate?.Term == null ||
                    ContainsCandidate(pTarget, candidate.Term)) continue;
                pTarget.Add(candidate);
            }
        }

        private static bool ContainsCandidate(
            IReadOnlyList<WarPeaceDefaultTermCandidate> pCandidates,
            WarPeaceSettlementTermDraft pTerm)
        {
            for (int i = 0; i < pCandidates.Count; i++)
            {
                WarPeaceSettlementTermDraft other = pCandidates[i]?.Term;
                if (other != null && other.Kind == pTerm.Kind &&
                    other.FromKingdomId == pTerm.FromKingdomId &&
                    other.ToKingdomId == pTerm.ToKingdomId &&
                    other.CityId == pTerm.CityId &&
                    other.CaptiveActorId == pTerm.CaptiveActorId &&
                    other.ClaimId == pTerm.ClaimId &&
                    string.Equals(other.ResourceId, pTerm.ResourceId,
                        StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static WarPeaceDefaultOfferMode ToSettlementMode(
            WarPeaceNegotiationOfferMode pMode)
        {
            return pMode == WarPeaceNegotiationOfferMode.Surrender
                ? WarPeaceDefaultOfferMode.Surrender
                : pMode == WarPeaceNegotiationOfferMode.EnforceDemands
                    ? WarPeaceDefaultOfferMode.EnforceDemands
                    : WarPeaceDefaultOfferMode.WhitePeace;
        }

        private static void DescribeTerm(WarPeaceSettlementTermDraft pTerm,
            out string pTitleKey, out string pTitleFallback,
            out string pDescriptionKey, out string pDescriptionFallback,
            out string pDetail)
        {
            string id = pTerm.Kind.ToString().ToLowerInvariant();
            pTitleKey = "aw_war_peace_term_" + id;
            pDescriptionKey = pTitleKey + "_desc";
            pTitleFallback = pTerm.Kind switch
            {
                WarPeaceTermKind.WhitePeace => "White peace",
                WarPeaceTermKind.GoldPayment => "Gold payment",
                WarPeaceTermKind.MaterialPayment => "Material payment",
                WarPeaceTermKind.Reparations => "War reparations",
                WarPeaceTermKind.ReleaseCaptives => "Release captives",
                WarPeaceTermKind.RenounceClaims => "Renounce claims",
                WarPeaceTermKind.ForceTributary => "Force tributary",
                WarPeaceTermKind.CedeCity => "Cede city",
                WarPeaceTermKind.ForceVassal => "Force vassalage",
                _ => pTerm.Kind.ToString()
            };
            pDescriptionFallback = pTitleFallback;
            pDetail = BuildTermDetail(pTerm);
        }

        private static string BuildTermDetail(
            WarPeaceSettlementTermDraft pTerm)
        {
            switch (pTerm.Kind)
            {
                case WarPeaceTermKind.CedeCity:
                    return FormatDetail(
                        "aw_war_peace_detail_city_transfer",
                        "{0} -> {1}: {2}",
                        WarPeaceSettlementWorld.FindKingdom(
                            pTerm.FromKingdomId)?.name ??
                        pTerm.FromKingdomId.ToString(),
                        WarPeaceSettlementWorld.FindKingdom(
                            pTerm.ToKingdomId)?.name ??
                        pTerm.ToKingdomId.ToString(),
                        WarPeaceSettlementWorld.FindCity(pTerm.CityId)
                            ?.name ?? pTerm.CityId.ToString());
                case WarPeaceTermKind.GoldPayment:
                case WarPeaceTermKind.MaterialPayment:
                    return FormatDetail("aw_war_peace_detail_payment",
                        "{0} {1}", pTerm.Amount,
                        ResourceName(pTerm.ResourceId));
                case WarPeaceTermKind.Reparations:
                    return FormatDetail("aw_war_peace_detail_reparations",
                        "{0} {1} each year for {2} years", pTerm.Amount,
                        ResourceName(pTerm.ResourceId),
                        pTerm.DurationYears);
                case WarPeaceTermKind.ReleaseCaptives:
                    return FormatDetail("aw_war_peace_detail_captive",
                        "Captive: {0}",
                        WarPeaceSettlementWorld.FindActor(
                            pTerm.CaptiveActorId)?.getName() ??
                        pTerm.CaptiveActorId.ToString());
                case WarPeaceTermKind.RenounceClaims:
                    return FormatDetail("aw_war_peace_detail_claim",
                        "Claim #{0}", pTerm.ClaimId);
                case WarPeaceTermKind.ForceTributary:
                case WarPeaceTermKind.ForceVassal:
                    return FormatDetail("aw_war_peace_detail_subject",
                        "{0} -> {1}",
                        WarPeaceSettlementWorld.FindKingdom(
                            pTerm.FromKingdomId)?.name ??
                        pTerm.FromKingdomId.ToString(),
                        WarPeaceSettlementWorld.FindKingdom(
                            pTerm.ToKingdomId)?.name ??
                        pTerm.ToKingdomId.ToString());
                default:
                    return string.Empty;
            }
        }

        private static string CandidatePrerequisiteFailure(
            WarPeaceSettlementTermDraft pTerm)
        {
            switch (pTerm.Kind)
            {
                case WarPeaceTermKind.CedeCity:
                    return "no_territorial_basis";
                case WarPeaceTermKind.GoldPayment:
                case WarPeaceTermKind.MaterialPayment:
                    return "invalid_payment_amount";
                case WarPeaceTermKind.Reparations:
                    return "invalid_reparations";
                case WarPeaceTermKind.ReleaseCaptives:
                    return "invalid_or_duplicate_captive";
                case WarPeaceTermKind.RenounceClaims:
                    return "invalid_or_duplicate_claim";
                case WarPeaceTermKind.ForceTributary:
                case WarPeaceTermKind.ForceVassal:
                    return "force_subject_failed";
                default:
                    return "prerequisite_failed";
            }
        }

        private static string ResourceName(string pResourceId)
        {
            return WarPeaceResourceNameService.Resolve(pResourceId);
        }

        private static string FormatDetail(string pKey, string pFallback,
            params object[] pValues)
        {
            return string.Format(AW_L10n.Text(pKey, pFallback), pValues);
        }
    }
}
