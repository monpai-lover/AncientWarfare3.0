using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    public sealed partial class WarPeaceSettlementService
    {
        private readonly IWarPeaceSettlementStore _store;
        private readonly IWarPeaceSettlementWorld _world;

        public WarPeaceSettlementService(IWarPeaceSettlementStore store,
            IWarPeaceSettlementWorld world)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public WarPeacePrepareResult Prepare(WarPeaceSettlementDraft draft)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeacePrepareResult(false, null,
                    "replica_read_only");
            if (draft == null)
                return new WarPeacePrepareResult(false, null,
                    "invalid_settlement_participants");
            if (!_world.TryPrepareScope(draft, out string reason))
                return new WarPeacePrepareResult(false, null,
                    string.IsNullOrEmpty(reason)
                        ? "invalid_settlement_scope"
                        : reason);
            if (!_world.TryGetAuthoritativeSignedWarScore(draft,
                    out int signedWarScore, out reason))
                return new WarPeacePrepareResult(false, null,
                    string.IsNullOrEmpty(reason)
                        ? "war_score_unavailable"
                        : reason);
            draft.SignedWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                signedWarScore);
            if (!WarPeaceSettlementValidationRules.TryMaterialize(draft,
                    _world, out var terms, out reason))
                return new WarPeacePrepareResult(false, null, reason);
            if (!_world.TryPrepareScope(draft, out reason))
                return new WarPeacePrepareResult(false, null,
                    string.IsNullOrEmpty(reason)
                        ? "participant_roster_changed"
                        : reason);
            if (!WarPeaceSettlementValidationRules.TryMaterialize(draft,
                    _world, out terms, out reason))
                return new WarPeacePrepareResult(false, null, reason);
            if (!_store.TryCreate(draft, terms, out var proposal,
                    out reason))
                return new WarPeacePrepareResult(false, null,
                    string.IsNullOrEmpty(reason) ?
                        "settlement_persistence_failed" : reason);
            return new WarPeacePrepareResult(true, proposal, "");
        }

        public WarPeaceExecutionResult ForceDecisiveSettlement(
            WarPeaceSettlementDraft pDraft)
        {
            if (pDraft == null || !IsWinningDecisiveScore(
                    pDraft.SignedWarScore))
                return new WarPeaceExecutionResult(false, -1,
                    "war_score_not_decisive");

            if (!_world.TryGetAuthoritativeSignedWarScore(pDraft,
                    out int signedWarScore, out string scoreReason))
                return new WarPeaceExecutionResult(false, -1,
                    string.IsNullOrEmpty(scoreReason)
                        ? "war_score_unavailable"
                        : scoreReason);
            pDraft.SignedWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                signedWarScore);
            if (!IsWinningDecisiveScore(pDraft.SignedWarScore))
                return new WarPeaceExecutionResult(false, -1,
                    "war_score_not_decisive");

            if (_store is IWarPeaceSettlementActionableStore lookup &&
                lookup.TryReadActionableWinnerProposalForWar(pDraft.WarId,
                    pDraft.RequesterKingdomId, pDraft.ResponderKingdomId,
                    out long existingProposalId))
            {
                if (!_store.TryRead(existingProposalId,
                        out WarPeaceSettlementProposal existing))
                    return new WarPeaceExecutionResult(false,
                        existingProposalId, "settlement_not_found");
                if (existing.Status == WarPeaceSettlementStatus.Executing ||
                    existing.Status ==
                    WarPeaceSettlementStatus.TermsApplied ||
                    existing.Status == WarPeaceSettlementStatus.Executed)
                    return AcceptAndExecuteOrResume(existingProposalId);

                WarPeaceValidationResult validation =
                    Validate(existingProposalId);
                if (validation.Success)
                    return AcceptAndExecuteOrResume(existingProposalId);
                WarPeaceDecisionResult cancelled = Cancel(
                    existing.DetailId, "superseded_by_decisive_settlement");
                if (!cancelled.Success)
                    return AcceptAndExecuteOrResume(existingProposalId);
            }

            WarPeacePrepareResult prepared = Prepare(pDraft);
            if (!prepared.Success || prepared.Proposal == null)
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, prepared.Reason);
            if (!IsWinningDecisiveScore(
                    prepared.Proposal.SignedWarScore))
            {
                Cancel(prepared.DetailId, "war_score_not_decisive");
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, "war_score_not_decisive");
            }
            return AcceptAndExecuteOrResume(prepared.ProposalId);
        }

        public WarPeaceExecutionResult ForceGoalSettlement(
            WarPeaceSettlementDraft pDraft,
            IReadOnlyList<WarGoalSettlementFacts> pGoalFacts,
            int expectedGoalCount)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceExecutionResult(false, -1,
                    "replica_read_only");
            if (pDraft == null)
                return new WarPeaceExecutionResult(false, -1,
                    "invalid_settlement_participants");
            if (!_world.TryGetAuthoritativeSignedWarScore(pDraft,
                    out int signedWarScore, out string scoreReason))
                return new WarPeaceExecutionResult(false, -1,
                    string.IsNullOrEmpty(scoreReason)
                        ? "war_score_unavailable"
                        : scoreReason);
            pDraft.SignedWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                signedWarScore);
            if (!HasExactGoalTermBindings(pDraft, pGoalFacts,
                    expectedGoalCount))
                return new WarPeaceExecutionResult(false, -1,
                    "war_goal_bundle_incomplete");
            if (!WarGoalSettlementRules.TryValidateForceBundle(
                    pDraft.SignedWarScore, pGoalFacts, expectedGoalCount,
                    out string reason))
                return new WarPeaceExecutionResult(false, -1, reason);

            if (!TryClearPendingTerminalPredecessor(pDraft,
                    "superseded_by_terminal_goal_settlement",
                    out WarPeaceExecutionResult predecessor))
                return predecessor;

            WarPeacePrepareResult prepared = Prepare(pDraft);
            if (!prepared.Success || prepared.Proposal == null)
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, prepared.Reason);
            if (!WarGoalSettlementRules.TryValidateForceBundle(
                    prepared.Proposal.SignedWarScore, pGoalFacts,
                    expectedGoalCount, out reason))
            {
                Cancel(prepared.DetailId, reason);
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, reason);
            }
            return AcceptAndExecuteOrResume(prepared.ProposalId);
        }

        public WarPeaceExecutionResult ForceExhaustionSettlement(
            WarPeaceSettlementDraft pDraft, int attackerExhaustion,
            int defenderExhaustion)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceExecutionResult(false, -1,
                    "replica_read_only");
            if (!WarExhaustionSettlementRules.CanForceSettlement(
                    attackerExhaustion, defenderExhaustion))
                return new WarPeaceExecutionResult(false, -1,
                    "war_exhaustion_not_maximum");
            if (pDraft == null)
                return new WarPeaceExecutionResult(false, -1,
                    "invalid_settlement_participants");
            pDraft.AutomaticExhaustionSettlement = true;
            if (!_world.TryGetAuthoritativeSignedWarScore(pDraft,
                    out int signedWarScore, out string scoreReason))
                return new WarPeaceExecutionResult(false, -1,
                    string.IsNullOrEmpty(scoreReason)
                        ? "war_score_unavailable"
                        : scoreReason);
            pDraft.SignedWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                signedWarScore);

            WarPeacePrepareResult prepared = Prepare(pDraft);
            if (!prepared.Success || prepared.Proposal == null)
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, prepared.Reason);
            bool whitePeace = prepared.Proposal.Terms.Count == 1 &&
                              prepared.Proposal.Terms[0].Kind ==
                              WarPeaceTermKind.WhitePeace;
            if (!whitePeace &&
                (prepared.Proposal.SignedWarScore <= 0 ||
                 prepared.Proposal.TotalCost >
                 prepared.Proposal.SignedWarScore))
            {
                const string reason = "war_exhaustion_score_changed";
                Cancel(prepared.DetailId, reason);
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, reason);
            }
            return AcceptAndExecuteOrResume(prepared.ProposalId);
        }

        public WarPeaceExecutionResult ForceMilitaryEliminationSettlement(
            WarPeaceSettlementDraft pDraft,
            WarForceEliminationDecision pExpectedDecision)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceExecutionResult(false, -1,
                    "replica_read_only");
            if (pDraft == null)
                return new WarPeaceExecutionResult(false, -1,
                    "invalid_settlement_participants");
            War war = WarPeaceSettlementWorld.FindWar(pDraft.WarId);
            if (!WarForceEliminationSettlementService.
                    TryGetConfirmedDecision(war,
                        out WarForceEliminationDecision current) ||
                !SameDecision(pExpectedDecision, current))
                return new WarPeaceExecutionResult(false, -1,
                    "military_elimination_state_changed");

            int effectiveScore = current.Kind ==
                WarForceEliminationDecisionKind.AttackersSurrender ||
                current.Kind ==
                WarForceEliminationDecisionKind.DefendersSurrender
                    ? WarPeaceTermsRules.MaximumWarScore
                    : current.Score;
            pDraft.SignedWarScore = effectiveScore;
            if (!TryClearPendingTerminalPredecessor(pDraft,
                    "superseded_by_terminal_force_settlement",
                    out WarPeaceExecutionResult predecessor))
                return predecessor;
            WarPeacePrepareResult prepared = PrepareWithForcedScore(
                pDraft, effectiveScore);
            if (!prepared.Success || prepared.Proposal == null)
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, prepared.Reason);
            return AcceptAndExecuteOrResume(prepared.ProposalId);
        }

        internal WarPeaceExecutionResult ForceDeJureRegionSettlement(
            WarPeaceSettlementDraft pDraft, long pWarGoalId)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceExecutionResult(false, -1,
                    "replica_read_only");
            if (pDraft?.Terms == null || pDraft.Terms.Count == 0 ||
                pWarGoalId < 0L)
                return new WarPeaceExecutionResult(false, -1,
                    "de_jure_goal_bundle_incomplete");
            if (!_world.TryGetAuthoritativeSignedWarScore(pDraft,
                    out int signedWarScore, out string scoreReason))
                return new WarPeaceExecutionResult(false, -1,
                    string.IsNullOrEmpty(scoreReason)
                        ? "war_score_unavailable"
                        : scoreReason);
            pDraft.SignedWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                signedWarScore);
            var database = LineageArchiveManager.Instance?.OperatingDB;
            if (!WarGoalPersistence.TryReadOpenDeJureRegionGoal(database,
                    pDraft.WarId, pWarGoalId, out long regionId) ||
                !WarTerritoryService.TryGetDeJureRegion(regionId,
                    out var region))
                return new WarPeaceExecutionResult(false, -1,
                    "invalid_de_jure_region_target");
            var regionCities = new HashSet<long>(region.MemberCityIds ??
                new List<long>());
            int requested = 0;
            var cities = new HashSet<long>();
            for (int i = 0; i < pDraft.Terms.Count; i++)
            {
                WarPeaceSettlementTermDraft term = pDraft.Terms[i];
                if (term == null || term.Kind != WarPeaceTermKind.CedeCity ||
                    term.WarGoalId != pWarGoalId || term.CityId < 0L ||
                    term.RequestedCost <= 0 || !cities.Add(term.CityId))
                    return new WarPeaceExecutionResult(false, -1,
                        "de_jure_goal_bundle_incomplete");
                if (!regionCities.Contains(term.CityId))
                    return new WarPeaceExecutionResult(false, -1,
                        "city_outside_de_jure_region");
                requested += term.RequestedCost;
            }
            if (requested > pDraft.SignedWarScore)
                return new WarPeaceExecutionResult(false, -1,
                    "war_goal_score_insufficient");
            if (!TryClearPendingTerminalPredecessor(pDraft,
                    "superseded_by_de_jure_goal_settlement",
                    out WarPeaceExecutionResult predecessor))
                return predecessor;
            WarPeacePrepareResult prepared = Prepare(pDraft);
            if (!prepared.Success || prepared.Proposal == null)
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, prepared.Reason);
            if (prepared.Proposal.TotalCost >
                prepared.Proposal.SignedWarScore)
            {
                Cancel(prepared.DetailId, "war_goal_score_changed");
                return new WarPeaceExecutionResult(false,
                    prepared.ProposalId, "war_goal_score_changed");
            }
            return AcceptAndExecuteOrResume(prepared.ProposalId);
        }

        private bool TryClearPendingTerminalPredecessor(
            WarPeaceSettlementDraft pDraft, string pCancelReason,
            out WarPeaceExecutionResult pResult)
        {
            pResult = new WarPeaceExecutionResult(true, -1, "");
            if (!(_store is IWarPeaceSettlementActionableStore lookup) ||
                !lookup.TryReadActionableWinnerProposalForWar(pDraft.WarId,
                    pDraft.RequesterKingdomId, pDraft.ResponderKingdomId,
                    out long proposalId)) return true;
            if (!_store.TryRead(proposalId,
                    out WarPeaceSettlementProposal existing))
            {
                pResult = new WarPeaceExecutionResult(false, proposalId,
                    "settlement_not_found");
                return false;
            }
            if (existing.Status == WarPeaceSettlementStatus.Executing ||
                existing.Status == WarPeaceSettlementStatus.TermsApplied ||
                existing.Status == WarPeaceSettlementStatus.Executed)
            {
                pResult = AcceptAndExecuteOrResume(proposalId);
                return false;
            }
            WarPeaceDecisionResult cancelled = Cancel(existing.DetailId,
                pCancelReason);
            if (cancelled.Success) return true;
            pResult = new WarPeaceExecutionResult(false, proposalId,
                string.IsNullOrEmpty(cancelled.Reason)
                    ? "terminal_predecessor_cancel_failed"
                    : cancelled.Reason);
            return false;
        }

        private WarPeacePrepareResult PrepareWithForcedScore(
            WarPeaceSettlementDraft pDraft, int pSignedScore)
        {
            if (!_world.TryPrepareScope(pDraft, out string reason))
                return new WarPeacePrepareResult(false, null,
                    string.IsNullOrEmpty(reason)
                        ? "invalid_settlement_scope"
                        : reason);
            pDraft.SignedWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                pSignedScore);
            if (!WarPeaceSettlementValidationRules.TryMaterialize(pDraft,
                    _world, out var terms, out reason))
                return new WarPeacePrepareResult(false, null, reason);
            if (!_world.TryPrepareScope(pDraft, out reason))
                return new WarPeacePrepareResult(false, null,
                    string.IsNullOrEmpty(reason)
                        ? "participant_roster_changed"
                        : reason);
            if (!WarPeaceSettlementValidationRules.TryMaterialize(pDraft,
                    _world, out terms, out reason))
                return new WarPeacePrepareResult(false, null, reason);
            if (!_store.TryCreate(pDraft, terms,
                    out WarPeaceSettlementProposal proposal, out reason))
                return new WarPeacePrepareResult(false, null,
                    string.IsNullOrEmpty(reason)
                        ? "settlement_persistence_failed"
                        : reason);
            return new WarPeacePrepareResult(true, proposal, "");
        }

        private static bool SameDecision(
            WarForceEliminationDecision pExpected,
            WarForceEliminationDecision pCurrent)
        {
            return pExpected.Kind == pCurrent.Kind &&
                   pExpected.Beneficiary == pCurrent.Beneficiary &&
                   pExpected.Score == pCurrent.Score;
        }

        private static bool HasExactGoalTermBindings(
            WarPeaceSettlementDraft pDraft,
            IReadOnlyList<WarGoalSettlementFacts> pGoalFacts,
            int pExpectedGoalCount)
        {
            if (pDraft?.Terms == null || pGoalFacts == null ||
                pDraft.Terms.Count != pExpectedGoalCount ||
                pGoalFacts.Count != pExpectedGoalCount) return false;
            var bindings = new HashSet<long>();
            for (int i = 0; i < pDraft.Terms.Count; i++)
            {
                WarPeaceSettlementTermDraft term = pDraft.Terms[i];
                if (term == null || term.Kind == WarPeaceTermKind.WhitePeace ||
                    term.WarGoalId < 0 || !bindings.Add(term.WarGoalId))
                    return false;
                bool matched = false;
                for (int goalIndex = 0; goalIndex < pGoalFacts.Count;
                     goalIndex++)
                    if (pGoalFacts[goalIndex].WarGoalId == term.WarGoalId &&
                        pGoalFacts[goalIndex].RequestedGoalTermWarGoalId ==
                        term.WarGoalId)
                    {
                        if (term.RequestedCost !=
                            pGoalFacts[goalIndex].RequiredScore)
                            return false;
                        matched = true;
                        break;
                    }
                if (!matched) return false;
            }
            return true;
        }

        private static bool IsWinningDecisiveScore(int pSignedWarScore)
        {
            return pSignedWarScore == WarPeaceTermsRules.MaximumWarScore;
        }

        public bool HasActionableSettlement(long pWarId)
        {
            return _store is IWarPeaceSettlementExecutionGuardStore guard &&
                   guard.HasActionableSettlement(pWarId);
        }

        public WarPeaceValidationResult Validate(string detailId)
        {
            return WarPeaceSettlementValidationRules.TryParseDetailId(
                detailId, out long proposalId)
                ? Validate(proposalId)
                : new WarPeaceValidationResult(false, -1,
                    "invalid_settlement_detail_id");
        }

        public WarPeaceValidationResult Validate(long proposalId)
        {
            if (!_store.TryRead(proposalId, out var proposal))
                return new WarPeaceValidationResult(false, proposalId,
                    "settlement_not_found");
            if (proposal.Status == WarPeaceSettlementStatus.Rejected ||
                proposal.Status == WarPeaceSettlementStatus.Cancelled ||
                proposal.Status == WarPeaceSettlementStatus.Executed)
                return new WarPeaceValidationResult(false, proposalId,
                    "settlement_not_actionable");
            if (!WarPeaceSettlementValidationRules.ValidatePersisted(
                    proposal, out string reason))
                return new WarPeaceValidationResult(false, proposalId,
                    reason);
            if (!TryValidateScopeAndBackfillLegacy(proposal, out reason))
                return new WarPeaceValidationResult(false, proposalId,
                    string.IsNullOrEmpty(reason)
                        ? "settlement_scope_changed"
                        : reason);
            if (!_world.TryValidate(proposal, out reason))
                return new WarPeaceValidationResult(false, proposalId,
                    string.IsNullOrEmpty(reason) ?
                        "settlement_world_changed" : reason);
            return new WarPeaceValidationResult(true, proposalId, "");
        }

        public WarPeaceDecisionResult Respond(long proposalId, bool accept)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceDecisionResult(false, proposalId,
                    WarPeaceSettlementStatus.Pending, "replica_read_only");
            WarPeaceSettlementStatus next = accept
                ? WarPeaceSettlementStatus.Accepted
                : WarPeaceSettlementStatus.Rejected;
            if (!_store.TrySetStatus(proposalId,
                    WarPeaceSettlementStatus.Pending, next,
                    accept ? "accepted" : "rejected"))
                return new WarPeaceDecisionResult(false, proposalId,
                    WarPeaceSettlementStatus.Pending,
                    "settlement_not_pending");
            return new WarPeaceDecisionResult(true, proposalId, next, "");
        }

        public WarPeaceDecisionResult Respond(string detailId, bool accept)
        {
            return WarPeaceSettlementValidationRules.TryParseDetailId(
                detailId, out long proposalId)
                ? Respond(proposalId, accept)
                : new WarPeaceDecisionResult(false, -1,
                    WarPeaceSettlementStatus.Pending,
                    "invalid_settlement_detail_id");
        }

        public WarPeaceDecisionResult Cancel(string detailId, string reason)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceDecisionResult(false, -1,
                    WarPeaceSettlementStatus.Pending, "replica_read_only");
            if (!WarPeaceSettlementValidationRules.TryParseDetailId(
                    detailId, out long proposalId))
                return new WarPeaceDecisionResult(false, -1,
                    WarPeaceSettlementStatus.Pending,
                    "invalid_settlement_detail_id");
            if (!_store.TryRead(proposalId, out var proposal))
                return new WarPeaceDecisionResult(false, proposalId,
                    WarPeaceSettlementStatus.Pending,
                    "settlement_not_found");
            if (proposal.Status == WarPeaceSettlementStatus.Cancelled)
                return new WarPeaceDecisionResult(true, proposalId,
                    proposal.Status, "");
            if (proposal.Status != WarPeaceSettlementStatus.Pending &&
                proposal.Status != WarPeaceSettlementStatus.Accepted)
                return new WarPeaceDecisionResult(false, proposalId,
                    proposal.Status, "settlement_cannot_cancel");
            if (!_store.TrySetStatus(proposalId, proposal.Status,
                    WarPeaceSettlementStatus.Cancelled,
                    string.IsNullOrEmpty(reason) ? "cancelled" : reason))
                return new WarPeaceDecisionResult(false, proposalId,
                    proposal.Status, "settlement_cancel_raced");
            return new WarPeaceDecisionResult(true, proposalId,
                WarPeaceSettlementStatus.Cancelled, "");
        }

        public WarPeaceExecutionResult Execute(string detailId)
        {
            return WarPeaceSettlementValidationRules.TryParseDetailId(
                detailId, out long proposalId)
                ? Execute(proposalId)
                : new WarPeaceExecutionResult(false, -1,
                    "invalid_settlement_detail_id");
        }

        public WarPeaceExecutionResult AcceptAndExecuteOrResume(
            string detailId)
        {
            return WarPeaceSettlementValidationRules.TryParseDetailId(
                detailId, out long proposalId)
                ? AcceptAndExecuteOrResume(proposalId)
                : new WarPeaceExecutionResult(false, -1,
                    "invalid_settlement_detail_id");
        }

        public WarPeaceExecutionResult AcceptAndExecuteOrResume(
            long proposalId)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceExecutionResult(false, proposalId,
                    "replica_read_only");
            if (!_store.TryRead(proposalId, out var proposal))
                return new WarPeaceExecutionResult(false, proposalId,
                    "settlement_not_found");
            bool validateAcceptedBeforeExecution = proposal.Status ==
                                                   WarPeaceSettlementStatus
                                                       .Accepted;
            if (proposal.Status == WarPeaceSettlementStatus.Pending ||
                proposal.Status == WarPeaceSettlementStatus.Accepted)
            {
                WarPeaceValidationResult validation = Validate(proposalId);
                if (!validation.Success)
                {
                    if (validation.Reason ==
                        RebellionDirectTerritoryTransferRules.
                            SettlementBlockedReason)
                        Cancel(proposal.DetailId,
                            RebellionDirectTerritoryTransferRules.
                                SettlementBlockedReason);
                    return new WarPeaceExecutionResult(false, proposalId,
                        validation.Reason);
                }
            }
            if (proposal.Status == WarPeaceSettlementStatus.Pending)
            {
                WarPeaceDecisionResult accepted = Respond(proposalId, true);
                if (!accepted.Success)
                    return new WarPeaceExecutionResult(false, proposalId,
                        accepted.Reason);
            }
            return ExecuteCore(proposalId,
                validateAcceptedBeforeExecution);
        }

        public bool RecoverOneForKingdom(long kingdomId)
        {
            return RecoverOneForKingdom(kingdomId, Array.Empty<long>());
        }

        public bool RecoverOneForKingdom(long kingdomId,
            IReadOnlyList<long> activeWarIds)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return false;
            if (kingdomId < 0) return false;
            if (_store is IWarPeaceSettlementOrphanRecoveryStore orphanStore &&
                orphanStore.TryCancelOneOrphanedPendingForKingdom(kingdomId,
                    out long orphanedProposalId) &&
                orphanedProposalId >= 0L) return true;
            const int recoveryBudget = 8;
            int inspected = 0;
            bool attempted = false;
            bool hasActiveWarCandidate = activeWarIds != null &&
                                         activeWarIds.Count > 0;
            int unfinishedBudget = recoveryBudget -
                                   (hasActiveWarCandidate ? 1 : 0);
            IReadOnlyList<long> candidates =
                _store.ReadRecoveryCandidatesForKingdom(kingdomId,
                    unfinishedBudget);
            for (int i = 0; i < candidates.Count &&
                            inspected < recoveryBudget; i++)
            {
                long proposalId = candidates[i];
                inspected++;
                if (!_store.TryMarkRecoveryAttempt(proposalId)) continue;
                RecoveryProgressSnapshot before =
                    CaptureRecoveryProgress(proposalId);
                WarPeaceExecutionResult result =
                    AcceptAndExecuteOrResume(proposalId);
                attempted = true;
                if (result.Success || RecoveryProgressChanged(before,
                        CaptureRecoveryProgress(proposalId))) return true;
            }
            if (activeWarIds == null) return attempted;
            for (int i = 0; i < activeWarIds.Count &&
                            inspected < recoveryBudget; i++)
            {
                inspected++;
                long warId = activeWarIds[i];
                if (warId < 0 ||
                    !_store.TryReadExecutedProposalForWar(warId,
                        out long proposalId)) continue;
                if (!_store.TryMarkRecoveryAttempt(proposalId)) continue;
                RecoveryProgressSnapshot before =
                    CaptureRecoveryProgress(proposalId);
                WarPeaceExecutionResult result =
                    AcceptAndExecuteOrResume(proposalId);
                attempted = true;
                if (result.Success || RecoveryProgressChanged(before,
                        CaptureRecoveryProgress(proposalId))) return true;
            }
            return attempted;
        }

        private RecoveryProgressSnapshot CaptureRecoveryProgress(
            long proposalId)
        {
            if (!_store.TryRead(proposalId, out var proposal)) return null;
            var termStates = new WarPeaceSettlementTermApplyStatus[
                proposal.Terms.Count];
            for (int i = 0; i < proposal.Terms.Count; i++)
                termStates[i] = proposal.Terms[i].ApplyStatus;
            return new RecoveryProgressSnapshot(proposal.Status, termStates);
        }

        private static bool RecoveryProgressChanged(
            RecoveryProgressSnapshot before,
            RecoveryProgressSnapshot after)
        {
            if (before == null || after == null) return false;
            if (before.Status != after.Status ||
                before.TermStates.Length != after.TermStates.Length)
                return true;
            for (int i = 0; i < before.TermStates.Length; i++)
                if (before.TermStates[i] != after.TermStates[i]) return true;
            return false;
        }

        private sealed class RecoveryProgressSnapshot
        {
            public RecoveryProgressSnapshot(
                WarPeaceSettlementStatus status,
                WarPeaceSettlementTermApplyStatus[] termStates)
            {
                Status = status;
                TermStates = termStates;
            }

            public WarPeaceSettlementStatus Status { get; }
            public WarPeaceSettlementTermApplyStatus[] TermStates { get; }
        }

        public WarPeaceExecutionResult Execute(long proposalId)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return new WarPeaceExecutionResult(false, proposalId,
                    "replica_read_only");
            return ExecuteCore(proposalId,
                validateAcceptedBeforeExecution: true);
        }

        private WarPeaceExecutionResult ExecuteCore(long proposalId,
            bool validateAcceptedBeforeExecution)
        {
            if (!_store.TryRead(proposalId, out var proposal))
                return new WarPeaceExecutionResult(false, proposalId,
                    "settlement_not_found");
            if (!WarPeaceSettlementValidationRules.ValidatePersisted(
                    proposal, out string reason))
                return new WarPeaceExecutionResult(false, proposalId,
                    reason);

            if (proposal.Status == WarPeaceSettlementStatus.Executed)
            {
                if (!TryInspectAppliedTerms(proposal,
                        out bool hasMissingExecutedEffect, out reason))
                    return new WarPeaceExecutionResult(false, proposalId,
                        reason);
                if (!hasMissingExecutedEffect &&
                    _world.IsSettlementFinalized(proposal))
                    return new WarPeaceExecutionResult(true, proposalId,
                        "");
                if (!_store.TrySetStatus(proposalId,
                        WarPeaceSettlementStatus.Executed,
                        WarPeaceSettlementStatus.TermsApplied,
                        "recovering_active_war"))
                    return new WarPeaceExecutionResult(false, proposalId,
                        "settlement_recovery_raced");
                proposal.Status = WarPeaceSettlementStatus.TermsApplied;
            }

            if (proposal.Status == WarPeaceSettlementStatus.TermsApplied)
            {
                if (!TryInspectAppliedTerms(proposal,
                        out bool hasMissingEffect, out reason))
                    return new WarPeaceExecutionResult(false, proposalId,
                        reason);
                if (!hasMissingEffect)
                    return FinalizeTermsApplied(proposal, null);
                if (!_store.TrySetStatus(proposalId,
                        WarPeaceSettlementStatus.TermsApplied,
                        WarPeaceSettlementStatus.Executing,
                        "recovering_missing_term_effect"))
                    return new WarPeaceExecutionResult(false, proposalId,
                        "settlement_recovery_raced");
                proposal.Status = WarPeaceSettlementStatus.Executing;
            }
            if (proposal.Status == WarPeaceSettlementStatus.Accepted)
            {
                if (validateAcceptedBeforeExecution &&
                    !TryValidateScopeAndBackfillLegacy(proposal,
                        out reason))
                {
                    reason = string.IsNullOrEmpty(reason)
                        ? "settlement_scope_changed"
                        : reason;
                    if (reason == RebellionDirectTerritoryTransferRules.
                            SettlementBlockedReason)
                        Cancel(proposal.DetailId,
                            RebellionDirectTerritoryTransferRules.
                                SettlementBlockedReason);
                    return new WarPeaceExecutionResult(false, proposalId,
                        reason);
                }
                if (validateAcceptedBeforeExecution &&
                    !_world.TryValidate(proposal, out reason))
                {
                    reason = string.IsNullOrEmpty(reason)
                        ? "settlement_world_changed"
                        : reason;
                    if (reason == RebellionDirectTerritoryTransferRules.
                            SettlementBlockedReason)
                        Cancel(proposal.DetailId,
                            RebellionDirectTerritoryTransferRules.
                                SettlementBlockedReason);
                    return new WarPeaceExecutionResult(false, proposalId,
                        reason);
                }
                if (!TryPreflightPendingBaselines(proposal, out reason))
                    return new WarPeaceExecutionResult(false, proposalId,
                        string.IsNullOrEmpty(reason)
                            ? "term_baseline_capture_failed"
                            : reason);
                if (!_store.TrySetStatus(proposalId,
                        WarPeaceSettlementStatus.Accepted,
                        WarPeaceSettlementStatus.Executing, ""))
                    return new WarPeaceExecutionResult(false, proposalId,
                        "settlement_execution_raced");
                proposal.Status = WarPeaceSettlementStatus.Executing;
            }
            else if (proposal.Status !=
                     WarPeaceSettlementStatus.Executing)
            {
                return new WarPeaceExecutionResult(false, proposalId,
                    "settlement_not_accepted");
            }

            IWarPeaceSettlementExecution execution =
                _world.BeginExecution(proposal);
            if (execution == null)
                return new WarPeaceExecutionResult(false, proposalId,
                    "settlement_transaction_unavailable");
            try
            {
                List<WarPeaceSettlementTerm> ordered =
                    OrderedForExecution(proposal.Terms);
                for (int i = 0; i < ordered.Count; i++)
                {
                    WarPeaceSettlementTerm term = ordered[i];
                    if (WarPeaceSettlementValidationRules.
                            IsWaivedResourceTerm(term.Kind,
                                term.ApplyReason))
                    {
                        if (term.ApplyStatus ==
                                WarPeaceSettlementTermApplyStatus.Applying &&
                            !SetTermStatus(proposal, term,
                                WarPeaceSettlementTermApplyStatus.Applying,
                                WarPeaceSettlementTermApplyStatus.Applied,
                                WarPeaceSettlementValidationRules.
                                    WaivedResourceReason))
                            return new WarPeaceExecutionResult(false,
                                proposalId, "term_waiver_marker_failed");
                        if (term.ApplyStatus ==
                            WarPeaceSettlementTermApplyStatus.Applied)
                            continue;
                    }
                    if (term.ApplyStatus ==
                        WarPeaceSettlementTermApplyStatus.Applied)
                    {
                        WarPeaceTermApplicationState appliedState =
                            InspectTermFinalState(proposal, ordered, i,
                                out string appliedReason);
                        if (appliedState ==
                            WarPeaceTermApplicationState.Applied)
                            continue;
                        if (appliedState ==
                            WarPeaceTermApplicationState.Ambiguous)
                            return new WarPeaceExecutionResult(false,
                                proposalId,
                                string.IsNullOrEmpty(appliedReason)
                                    ? "term_recovery_ambiguous"
                                    : appliedReason);
                        if (!SetTermStatus(proposal, term,
                                WarPeaceSettlementTermApplyStatus.Applied,
                                WarPeaceSettlementTermApplyStatus.Pending,
                                "verified_applied_effect_missing"))
                            return new WarPeaceExecutionResult(false,
                                proposalId,
                                "term_recovery_reset_failed");
                    }
                    if (term.ApplyStatus ==
                        WarPeaceSettlementTermApplyStatus.Applying)
                    {
                        WarPeaceTermApplicationState state =
                            InspectTermFinalState(proposal, ordered, i,
                                out string recoveryReason);
                        if (state == WarPeaceTermApplicationState.Applied)
                        {
                            if (!SetTermStatus(proposal, term,
                                    WarPeaceSettlementTermApplyStatus
                                        .Applying,
                                    WarPeaceSettlementTermApplyStatus
                                        .Applied,
                                    "recovered_verified_applied"))
                                return new WarPeaceExecutionResult(false,
                                    proposalId,
                                    "term_recovery_marker_failed");
                            continue;
                        }
                        if (state ==
                            WarPeaceTermApplicationState.Ambiguous)
                            return new WarPeaceExecutionResult(false,
                                proposalId,
                                string.IsNullOrEmpty(recoveryReason)
                                    ? "term_recovery_ambiguous"
                                    : recoveryReason);
                        if (!SetTermStatus(proposal, term,
                                WarPeaceSettlementTermApplyStatus.Applying,
                                WarPeaceSettlementTermApplyStatus.Pending,
                                "recovered_verified_not_applied"))
                            return new WarPeaceExecutionResult(false,
                                proposalId,
                                "term_recovery_reset_failed");
                    }
                    if (term.ApplyStatus ==
                            WarPeaceSettlementTermApplyStatus.Pending &&
                        term.Kind == WarPeaceTermKind.CedeCity)
                    {
                        if (!TryRecoverPendingCede(proposal, term,
                                out bool recovered,
                                out string pendingCedeReason))
                            return new WarPeaceExecutionResult(false,
                                proposalId,
                                string.IsNullOrEmpty(pendingCedeReason)
                                    ? "term_recovery_ambiguous"
                                    : pendingCedeReason);
                        if (recovered) continue;
                    }
                    if (!_world.TryCaptureExecutionBaseline(proposal, term,
                            out WarPeaceTermExecutionBaseline baseline,
                            out string baselineReason))
                    {
                        if (WarPeaceSettlementValidationRules.
                                ShouldWaiveUnavailableResourceTerm(term.Kind,
                                    baselineReason))
                        {
                            if (!TryWaiveUnavailableResourceTerm(proposal,
                                    term, baselineReason,
                                    out string waiverReason))
                                return new WarPeaceExecutionResult(false,
                                    proposalId, waiverReason);
                            continue;
                        }
                        return new WarPeaceExecutionResult(false,
                            proposalId,
                            string.IsNullOrEmpty(baselineReason)
                                ? "term_baseline_capture_failed"
                                : baselineReason);
                    }
                    if (!_store.TryBeginTermApplication(proposalId,
                            term.TermId, baseline, "applying"))
                        return new WarPeaceExecutionResult(false,
                            proposalId, "term_execution_raced");
                    term.ApplyStatus =
                        WarPeaceSettlementTermApplyStatus.Applying;
                    term.ApplyReason = "applying";
                    term.BaselineCaptured = baseline.Captured;
                    term.SourceAmountBefore = baseline.SourceAmount;
                    term.TargetAmountBefore = baseline.TargetAmount;
                    term.SourceCityId = baseline.SourceCityId;
                    term.TargetCityId = baseline.TargetCityId;

                    bool applied;
                    string applyReason;
                    try
                    {
                        applied = execution.TryApply(term,
                            out applyReason);
                    }
                    catch (Exception error)
                    {
                        string failure =
                            "settlement_execution_exception:" +
                            error.GetType().Name;
                        if (ResolveFailedAttempt(proposal, term, execution,
                                failure, out WarPeaceExecutionResult failed))
                            continue;
                        return failed;
                    }
                    if (!applied)
                    {
                        string failure = string.IsNullOrEmpty(applyReason)
                            ? "peace_term_apply_failed"
                            : applyReason;
                        if (WarPeaceSettlementValidationRules.
                                ShouldWaiveUnavailableResourceTerm(term.Kind,
                                    failure))
                        {
                            execution.Rollback();
                            if (!TryWaiveUnavailableResourceTerm(proposal,
                                    term, failure,
                                    out string waiverReason))
                                return new WarPeaceExecutionResult(false,
                                    proposalId, waiverReason);
                            execution.CommitTerm();
                            continue;
                        }
                        if (ResolveFailedAttempt(proposal, term, execution,
                                failure, out WarPeaceExecutionResult failed))
                            continue;
                        return failed;
                    }

                    if (!SetTermStatus(proposal, term,
                            WarPeaceSettlementTermApplyStatus.Applying,
                            WarPeaceSettlementTermApplyStatus.Applied,
                            "applied"))
                    {
                        execution.CommitTerm();
                        return new WarPeaceExecutionResult(false,
                            proposalId, "term_applied_marker_failed");
                    }
                    execution.CommitTerm();
                }

                if (!_store.TrySetStatus(proposalId,
                        WarPeaceSettlementStatus.Executing,
                        WarPeaceSettlementStatus.TermsApplied, ""))
                    return new WarPeaceExecutionResult(false, proposalId,
                        "terms_applied_marker_failed");
                proposal.Status = WarPeaceSettlementStatus.TermsApplied;
                return FinalizeTermsApplied(proposal, execution);
            }
            finally
            {
                execution.Dispose();
            }
        }

        private bool TryRecoverPendingCede(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term, out bool recovered,
            out string reason)
        {
            recovered = false;
            WarPeaceTermApplicationState state =
                _world.InspectTermApplication(proposal, term, out reason);
            if (state == WarPeaceTermApplicationState.NotApplied)
            {
                reason = "";
                return true;
            }
            if (state == WarPeaceTermApplicationState.Ambiguous)
                return false;

            var baseline = new WarPeaceTermExecutionBaseline(true,
                -1, -1, -1, -1);
            if (!_store.TryBeginTermApplication(proposal.ProposalId,
                    term.TermId, baseline,
                    "recovering_pending_cede_applied"))
            {
                reason = "term_execution_raced";
                return false;
            }
            term.ApplyStatus = WarPeaceSettlementTermApplyStatus.Applying;
            term.ApplyReason = "recovering_pending_cede_applied";
            term.BaselineCaptured = true;
            term.SourceAmountBefore = -1;
            term.TargetAmountBefore = -1;
            term.SourceCityId = -1;
            term.TargetCityId = -1;
            if (!SetTermStatus(proposal, term,
                    WarPeaceSettlementTermApplyStatus.Applying,
                    WarPeaceSettlementTermApplyStatus.Applied,
                    "recovered_pending_cede_applied"))
            {
                reason = "term_recovery_marker_failed";
                return false;
            }
            recovered = true;
            reason = "";
            return true;
        }

        private bool TryPreflightPendingBaselines(
            WarPeaceSettlementProposal proposal, out string reason)
        {
            reason = "";
            var sourceReservations = new Dictionary<string, long>(
                StringComparer.Ordinal);
            var targetReservations = new Dictionary<string, long>(
                StringComparer.Ordinal);
            List<WarPeaceSettlementTerm> ordered =
                OrderedForExecution(proposal.Terms);
            for (int i = 0; i < ordered.Count; i++)
            {
                WarPeaceSettlementTerm term = ordered[i];
                if (term.ApplyStatus !=
                    WarPeaceSettlementTermApplyStatus.Pending) continue;
                if (!_world.TryCaptureExecutionBaseline(proposal, term,
                        out WarPeaceTermExecutionBaseline baseline,
                        out reason))
                {
                    if (!WarPeaceSettlementValidationRules.
                            ShouldWaiveUnavailableResourceTerm(term.Kind,
                                reason)) return false;
                    if (!TryWaiveUnavailableResourceTerm(proposal, term,
                            reason, out reason)) return false;
                    continue;
                }
                if (!IsResourceTransfer(term)) continue;
                string resource = ResourceKey(term);
                string sourceKey = ResourceReservationKey(
                    baseline.SourceCityId, resource);
                string targetKey = ResourceReservationKey(
                    baseline.TargetCityId, resource);
                sourceReservations.TryGetValue(sourceKey,
                    out long sourceAlreadyReserved);
                targetReservations.TryGetValue(targetKey,
                    out long targetAlreadyReserved);
                long sourceReserved = sourceAlreadyReserved +
                                      Math.Max(0, term.Amount);
                long targetReserved = targetAlreadyReserved +
                                      Math.Max(0, term.Amount);
                if (baseline.SourceAmount < 0 ||
                    sourceReserved > baseline.SourceAmount)
                {
                    reason = "payment_no_longer_available";
                    if (!TryWaiveUnavailableResourceTerm(proposal, term,
                            reason, out reason)) return false;
                    continue;
                }
                if (baseline.TargetCapacity < 0 ||
                    targetReserved > baseline.TargetCapacity)
                {
                    reason = "recipient_storage_full";
                    if (!TryWaiveUnavailableResourceTerm(proposal, term,
                            reason, out reason)) return false;
                    continue;
                }
                sourceReservations[sourceKey] = sourceReserved;
                targetReservations[targetKey] = targetReserved;
            }
            return true;
        }

        private bool TryWaiveUnavailableResourceTerm(
            WarPeaceSettlementProposal pProposal,
            WarPeaceSettlementTerm pTerm, string pFailure,
            out string pReason)
        {
            pReason = "";
            if (!WarPeaceSettlementValidationRules.
                    ShouldWaiveUnavailableResourceTerm(pTerm.Kind,
                        pFailure) ||
                !WarPeaceSettlementValidationRules.
                    HasIndependentSettlementTerm(pProposal.Terms))
            {
                pReason = string.IsNullOrEmpty(pFailure)
                    ? "resource_term_not_waivable"
                    : pFailure;
                return false;
            }

            if (pTerm.ApplyStatus ==
                WarPeaceSettlementTermApplyStatus.Pending)
            {
                var baseline = new WarPeaceTermExecutionBaseline(true,
                    -1, -1, -1, -1, -1);
                if (!_store.TryBeginTermApplication(pProposal.ProposalId,
                        pTerm.TermId, baseline,
                        WarPeaceSettlementValidationRules.
                            WaivedResourceReason))
                {
                    pReason = "term_waiver_begin_failed";
                    return false;
                }
                pTerm.ApplyStatus =
                    WarPeaceSettlementTermApplyStatus.Applying;
                pTerm.ApplyReason = WarPeaceSettlementValidationRules.
                    WaivedResourceReason;
                pTerm.BaselineCaptured = true;
                pTerm.SourceAmountBefore = -1;
                pTerm.TargetAmountBefore = -1;
                pTerm.SourceCityId = -1;
                pTerm.TargetCityId = -1;
            }
            if (pTerm.ApplyStatus !=
                WarPeaceSettlementTermApplyStatus.Applying)
            {
                pReason = "term_waiver_state_invalid";
                return false;
            }
            if (!SetTermStatus(pProposal, pTerm,
                    WarPeaceSettlementTermApplyStatus.Applying,
                    WarPeaceSettlementTermApplyStatus.Applied,
                    WarPeaceSettlementValidationRules.
                        WaivedResourceReason))
            {
                pReason = "term_waiver_marker_failed";
                return false;
            }
            return true;
        }

        private static string ResourceReservationKey(long pCityId,
            string pResource)
        {
            return pCityId + "\u001f" + (pResource ?? "");
        }

        public WarPeaceAcceptanceResult EvaluateAi(long proposalId,
            WarPeaceAcceptanceFacts facts)
        {
            return _store.TryRead(proposalId, out _)
                ? WarPeaceTermsRules.EvaluateAcceptance(facts)
                : new WarPeaceAcceptanceResult(false, false,
                    int.MinValue);
        }

        public WarPeaceAcceptanceResult EvaluateAi(string detailId,
            WarPeaceAcceptanceFacts facts)
        {
            return WarPeaceSettlementValidationRules.TryParseDetailId(
                detailId, out long proposalId)
                ? EvaluateAi(proposalId, facts)
                : new WarPeaceAcceptanceResult(false, false,
                    int.MinValue);
        }

        public bool HasExecutedSettlement(long warId)
        {
            return HasExecutedCoalitionSettlement(warId);
        }

        public bool HasExecutedCoalitionSettlement(long warId)
        {
            return TryHasExecutedCoalitionSettlement(warId,
                out bool executed) && executed;
        }

        public bool TryHasExecutedCoalitionSettlement(long warId,
            out bool executed)
        {
            executed = false;
            return warId >= 0 &&
                   _store.TryHasExecutedCoalitionSettlement(warId,
                       out executed);
        }

        public IReadOnlyList<WarPeaceSettlementTerm> ReadExecutedTerms(
            long warId)
        {
            return ReadExecutedCoalitionTerms(warId);
        }

        public IReadOnlyList<WarPeaceSettlementTerm>
            ReadExecutedCoalitionTerms(long warId)
        {
            return TryReadExecutedCoalitionTerms(warId, out var terms)
                ? terms
                : Array.Empty<WarPeaceSettlementTerm>();
        }

        public bool TryReadExecutedCoalitionTerms(long warId,
            out IReadOnlyList<WarPeaceSettlementTerm> terms)
        {
            terms = Array.Empty<WarPeaceSettlementTerm>();
            return warId >= 0 &&
                   _store.TryReadExecutedCoalitionTerms(warId,
                       out terms) && terms != null;
        }

        private bool SetTermStatus(WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term,
            WarPeaceSettlementTermApplyStatus expected,
            WarPeaceSettlementTermApplyStatus next, string reason)
        {
            if (!_store.TrySetTermApplyStatus(proposal.ProposalId,
                    term.TermId, expected, next, reason)) return false;
            term.ApplyStatus = next;
            term.ApplyReason = reason ?? "";
            return true;
        }

        private bool ResolveFailedAttempt(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term,
            IWarPeaceSettlementExecution execution, string failure,
            out WarPeaceExecutionResult result)
        {
            execution.Rollback();
            WarPeaceTermApplicationState state =
                _world.InspectTermApplication(proposal, term,
                    out string recoveryReason);
            if (state == WarPeaceTermApplicationState.Applied)
            {
                if (SetTermStatus(proposal, term,
                        WarPeaceSettlementTermApplyStatus.Applying,
                        WarPeaceSettlementTermApplyStatus.Applied,
                        "verified_applied_after_failure"))
                {
                    execution.CommitTerm();
                    result = null;
                    return true;
                }
                result = new WarPeaceExecutionResult(false,
                    proposal.ProposalId, "term_recovery_marker_failed");
                return false;
            }
            if (state == WarPeaceTermApplicationState.NotApplied)
            {
                string reason = string.IsNullOrEmpty(failure)
                    ? "peace_term_apply_failed"
                    : failure;
                if (!SetTermStatus(proposal, term,
                        WarPeaceSettlementTermApplyStatus.Applying,
                        WarPeaceSettlementTermApplyStatus.Pending,
                        reason))
                    reason = "term_recovery_reset_failed";
                result = new WarPeaceExecutionResult(false,
                    proposal.ProposalId, reason);
                return false;
            }
            result = new WarPeaceExecutionResult(false,
                proposal.ProposalId,
                string.IsNullOrEmpty(recoveryReason)
                    ? "term_recovery_ambiguous"
                    : recoveryReason);
            return false;
        }

        private WarPeaceExecutionResult FinalizeTermsApplied(
            WarPeaceSettlementProposal proposal,
            IWarPeaceSettlementExecution existingExecution)
        {
            if (proposal.Scope == WarPeaceSettlementScopeKind.Coalition &&
                proposal.Participants.Count == 0 &&
                !_world.IsWarEnded(proposal.WarId) &&
                !TryValidateScopeAndBackfillLegacy(proposal,
                    out string backfillReason))
                return new WarPeaceExecutionResult(false,
                    proposal.ProposalId, backfillReason);
            if (!TryInspectAppliedTerms(proposal,
                    out bool hasMissingEffect, out string inspectReason))
                return new WarPeaceExecutionResult(false,
                    proposal.ProposalId, inspectReason);
            if (hasMissingEffect)
                return new WarPeaceExecutionResult(false,
                    proposal.ProposalId,
                    "term_postcondition_missing");
            if (_world.IsSettlementFinalized(proposal))
            {
                _store.TrySetStatus(proposal.ProposalId,
                    WarPeaceSettlementStatus.TermsApplied,
                    WarPeaceSettlementStatus.Executed, "executed");
                return new WarPeaceExecutionResult(true,
                    proposal.ProposalId, "");
            }

            bool ownsExecution = existingExecution == null;
            IWarPeaceSettlementExecution execution = existingExecution ??
                _world.BeginExecution(proposal);
            if (execution == null)
                return new WarPeaceExecutionResult(false,
                    proposal.ProposalId,
                    "settlement_transaction_unavailable");
            try
            {
                if (!execution.TryFinalizeSettlement(out string reason))
                    return new WarPeaceExecutionResult(false,
                        proposal.ProposalId,
                        string.IsNullOrEmpty(reason)
                            ? "settlement_finalization_failed"
                            : reason);
                execution.Commit();
                if (!_store.TrySetStatus(proposal.ProposalId,
                        WarPeaceSettlementStatus.TermsApplied,
                        WarPeaceSettlementStatus.Executed, "executed"))
                    return new WarPeaceExecutionResult(true,
                        proposal.ProposalId,
                        "settlement_completion_deferred");
                return new WarPeaceExecutionResult(true,
                    proposal.ProposalId, "");
            }
            finally
            {
                if (ownsExecution) execution.Dispose();
            }
        }

        private bool TryValidateScopeAndBackfillLegacy(
            WarPeaceSettlementProposal proposal, out string reason)
        {
            bool backfill = proposal != null && proposal.Scope ==
                WarPeaceSettlementScopeKind.Coalition &&
                proposal.Participants.Count == 0;
            if (!_world.TryValidateScope(proposal, out reason)) return false;
            if (!backfill) return true;
            if (proposal.Participants.Count == 0 ||
                !_store.TryBackfillParticipants(proposal.ProposalId,
                    proposal.Participants))
            {
                reason = "legacy_participant_backfill_failed";
                return false;
            }
            reason = "";
            return true;
        }

        private bool TryInspectAppliedTerms(
            WarPeaceSettlementProposal proposal,
            out bool hasMissingEffect, out string reason)
        {
            hasMissingEffect = false;
            reason = "";
            List<WarPeaceSettlementTerm> ordered =
                OrderedForExecution(proposal.Terms);
            for (int i = 0; i < ordered.Count; i++)
            {
                WarPeaceSettlementTerm term = ordered[i];
                if (term.ApplyStatus !=
                    WarPeaceSettlementTermApplyStatus.Applied)
                {
                    reason = "incomplete_persisted_term_state";
                    return false;
                }
                if (IsResourceTransfer(term)) continue;
                WarPeaceTermApplicationState state =
                    InspectTermFinalState(proposal, ordered, i,
                        out string inspectReason);
                if (state == WarPeaceTermApplicationState.Ambiguous)
                {
                    reason = string.IsNullOrEmpty(inspectReason)
                        ? "term_recovery_ambiguous"
                        : inspectReason;
                    return false;
                }
                if (state == WarPeaceTermApplicationState.NotApplied)
                    hasMissingEffect = true;
            }
            return true;
        }

        private WarPeaceTermApplicationState InspectTermFinalState(
            WarPeaceSettlementProposal proposal,
            IReadOnlyList<WarPeaceSettlementTerm> ordered, int index,
            out string reason)
        {
            reason = "";
            WarPeaceSettlementTerm current = ordered[index];
            if (!IsResourceTransfer(current) ||
                current.SourceCityId < 0 || current.TargetCityId < 0)
                return _world.InspectTermApplication(proposal, current,
                    out reason);
            string resource = ResourceKey(current);
            bool sourceCovered = HasLaterStartedResourceEndpoint(ordered,
                index, current.SourceCityId, resource);
            bool targetCovered = HasLaterStartedResourceEndpoint(ordered,
                index, current.TargetCityId, resource);
            if (sourceCovered && targetCovered)
                return WarPeaceTermApplicationState.Applied;

            int inspectedEndpoints = 0;
            int appliedEndpoints = 0;
            int notAppliedEndpoints = 0;
            if (!sourceCovered)
            {
                WarPeaceTermApplicationState sourceState =
                    _world.InspectResourceEndpoint(proposal, current, true,
                        out string sourceReason);
                if (sourceState == WarPeaceTermApplicationState.Ambiguous)
                {
                    reason = sourceReason;
                    return sourceState;
                }
                inspectedEndpoints++;
                if (sourceState == WarPeaceTermApplicationState.Applied)
                    appliedEndpoints++;
                else
                    notAppliedEndpoints++;
            }
            if (!targetCovered)
            {
                WarPeaceTermApplicationState targetState =
                    _world.InspectResourceEndpoint(proposal, current, false,
                        out string targetReason);
                if (targetState == WarPeaceTermApplicationState.Ambiguous)
                {
                    reason = targetReason;
                    return targetState;
                }
                inspectedEndpoints++;
                if (targetState == WarPeaceTermApplicationState.Applied)
                    appliedEndpoints++;
                else
                    notAppliedEndpoints++;
            }
            if (appliedEndpoints > 0 && notAppliedEndpoints > 0)
            {
                reason = "payment_endpoint_state_mixed";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            if (notAppliedEndpoints > 0 &&
                (sourceCovered || targetCovered ||
                 notAppliedEndpoints != inspectedEndpoints))
            {
                reason = "payment_endpoint_chain_incomplete";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            return notAppliedEndpoints == inspectedEndpoints
                ? WarPeaceTermApplicationState.NotApplied
                : WarPeaceTermApplicationState.Applied;
        }

        private static bool HasLaterStartedResourceEndpoint(
            IReadOnlyList<WarPeaceSettlementTerm> ordered, int index,
            long cityId, string resource)
        {
            for (int i = index + 1; i < ordered.Count; i++)
            {
                WarPeaceSettlementTerm later = ordered[i];
                if (later.ApplyStatus ==
                        WarPeaceSettlementTermApplyStatus.Pending ||
                    !IsResourceTransfer(later) ||
                    !string.Equals(ResourceKey(later), resource,
                        StringComparison.Ordinal)) continue;
                if (later.SourceCityId == cityId ||
                    later.TargetCityId == cityId) return true;
            }
            return false;
        }

        private static bool IsResourceTransfer(
            WarPeaceSettlementTerm term)
        {
            return term.Kind == WarPeaceTermKind.GoldPayment ||
                   term.Kind == WarPeaceTermKind.MaterialPayment;
        }

        private static string ResourceKey(WarPeaceSettlementTerm term)
        {
            return term.Kind == WarPeaceTermKind.GoldPayment
                ? "gold"
                : term.ResourceId ?? "";
        }

        private static List<WarPeaceSettlementTerm> OrderedForExecution(
            IReadOnlyList<WarPeaceSettlementTerm> terms)
        {
            var result = new List<WarPeaceSettlementTerm>(terms.Count);
            for (int i = 0; i < terms.Count; i++) result.Add(terms[i]);
            result.Sort((left, right) =>
            {
                int priority = ExecutionPriority(left.Kind).CompareTo(
                    ExecutionPriority(right.Kind));
                return priority != 0
                    ? priority
                    : left.Position.CompareTo(right.Position);
            });
            return result;
        }

        private static int ExecutionPriority(WarPeaceTermKind kind)
        {
            switch (kind)
            {
                case WarPeaceTermKind.ForceTributary:
                case WarPeaceTermKind.ForceVassal:
                    return 1;
                case WarPeaceTermKind.CedeCity:
                    return 2;
                default:
                    return 0;
            }
        }
    }
}
