using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum WarPeaceSettlementStatus
    {
        Pending,
        Accepted,
        Rejected,
        Executing,
        TermsApplied,
        Executed,
        Cancelled
    }

    public enum WarPeaceSettlementTermApplyStatus
    {
        Pending,
        Applying,
        Applied
    }

    public enum WarPeaceTermApplicationState
    {
        NotApplied,
        Applied,
        Ambiguous
    }

    public struct WarPeaceTermExecutionBaseline
    {
        public WarPeaceTermExecutionBaseline(bool captured,
            int sourceAmount, int targetAmount)
            : this(captured, sourceAmount, targetAmount, -1, -1, -1)
        {
        }

        public WarPeaceTermExecutionBaseline(bool captured,
            int sourceAmount, int targetAmount, long sourceCityId,
            long targetCityId)
            : this(captured, sourceAmount, targetAmount, sourceCityId,
                targetCityId, -1)
        {
        }

        public WarPeaceTermExecutionBaseline(bool captured,
            int sourceAmount, int targetAmount, long sourceCityId,
            long targetCityId, int targetCapacity)
        {
            Captured = captured;
            SourceAmount = sourceAmount;
            TargetAmount = targetAmount;
            SourceCityId = sourceCityId;
            TargetCityId = targetCityId;
            TargetCapacity = targetCapacity;
        }

        public bool Captured;
        public int SourceAmount;
        public int TargetAmount;
        public long SourceCityId;
        public long TargetCityId;
        public int TargetCapacity;
    }

    public enum WarPeaceDefaultOfferMode
    {
        WhitePeace,
        Surrender,
        EnforceDemands,
        ExhaustionMaximumBenefit
    }

    public sealed class WarPeaceSettlementDraft
    {
        public long WarId { get; set; } = -1;
        public long RequesterKingdomId { get; set; } = -1;
        public long ResponderKingdomId { get; set; } = -1;
        public WarPeaceSettlementScopeKind Scope { get; set; } =
            WarPeaceSettlementScopeKind.Coalition;
        public long ExitRootKingdomId { get; set; } = -1;
        public int SignedWarScore { get; set; }
        public bool PlayerInitiated { get; set; }
        public bool AutomaticExhaustionSettlement { get; internal set; }
        public List<WarPeaceSettlementTermDraft> Terms { get; } =
            new List<WarPeaceSettlementTermDraft>();
        public List<WarPeaceSettlementParticipantSnapshot> Participants
            { get; } = new List<WarPeaceSettlementParticipantSnapshot>();
    }

    public sealed class WarPeaceSettlementParticipantSnapshot
    {
        public long ParticipantId { get; set; } = -1;
        public long KingdomId { get; set; } = -1;
        public string SideKind { get; set; } = "";
        public string ParticipantRole { get; set; } = "";
        public long ExitParentId { get; set; } = -1;
        public long VassalRelationId { get; set; } = -1;
        public WarParticipantEntrySourceKind EntrySourceKind { get; set; }
        public string EntrySourceFingerprint { get; set; } = "unknown";
        public bool IncludedInExitGroup { get; set; }

        public WarPeaceSettlementParticipantSnapshot Clone()
        {
            return (WarPeaceSettlementParticipantSnapshot)MemberwiseClone();
        }
    }

    public sealed class WarPeaceSettlementTermDraft
    {
        public WarPeaceTermKind Kind { get; set; }
        public int RequestedCost { get; set; }
        public long FromKingdomId { get; set; } = -1;
        public long ToKingdomId { get; set; } = -1;
        public string ResourceId { get; set; } = "";
        public int Amount { get; set; }
        public int DurationYears { get; set; }
        public long CityId { get; set; } = -1;
        public long CaptiveActorId { get; set; } = -1;
        public long ClaimId { get; set; } = -1;
        public long WarGoalId { get; set; } = -1;

        public WarPeaceSettlementTermDraft Clone()
        {
            return (WarPeaceSettlementTermDraft)MemberwiseClone();
        }
    }

    public sealed class WarPeaceSettlementTermFacts
    {
        public bool OccupiedByDemandingSide { get; set; }
        public bool HasCoreOrClaim { get; set; }
        public WarPeaceCityValueFacts CityValue { get; set; }
        public int SourceKingdomCityCount { get; set; } = -1;
    }

    public sealed class WarPeaceSettlementTerm
    {
        public long TermId { get; set; } = -1;
        public int Position { get; set; }
        public WarPeaceTermKind Kind { get; set; }
        public int Cost { get; set; }
        public long FromKingdomId { get; set; } = -1;
        public long ToKingdomId { get; set; } = -1;
        public string ResourceId { get; set; } = "";
        public int Amount { get; set; }
        public int DurationYears { get; set; }
        public long CityId { get; set; } = -1;
        public long CaptiveActorId { get; set; } = -1;
        public long ClaimId { get; set; } = -1;
        public long WarGoalId { get; set; } = -1;
        public bool FrozenOccupation { get; set; }
        public bool CoreOrClaimBasis { get; set; }
        public WarPeaceSettlementTermApplyStatus ApplyStatus { get; set; }
        public string ApplyReason { get; set; } = "";
        public bool BaselineCaptured { get; set; }
        public int SourceAmountBefore { get; set; } = -1;
        public int TargetAmountBefore { get; set; } = -1;
        public long SourceCityId { get; set; } = -1;
        public long TargetCityId { get; set; } = -1;

        public WarPeaceSettlementTerm Clone()
        {
            return (WarPeaceSettlementTerm)MemberwiseClone();
        }

        internal static WarPeaceSettlementTerm FromDraft(
            WarPeaceSettlementTermDraft draft,
            WarPeaceSettlementTermFacts facts, int cost, int position)
        {
            return new WarPeaceSettlementTerm
            {
                Position = position,
                Kind = draft.Kind,
                Cost = cost,
                FromKingdomId = draft.FromKingdomId,
                ToKingdomId = draft.ToKingdomId,
                ResourceId = draft.ResourceId ?? "",
                Amount = draft.Amount,
                DurationYears = draft.DurationYears,
                CityId = draft.CityId,
                CaptiveActorId = draft.CaptiveActorId,
                ClaimId = draft.ClaimId,
                WarGoalId = draft.WarGoalId,
                FrozenOccupation = facts?.OccupiedByDemandingSide == true,
                CoreOrClaimBasis = facts?.HasCoreOrClaim == true
            };
        }
    }

    public sealed class WarPeaceSettlementProposal
    {
        public long ProposalId { get; set; } = -1;
        public string DetailId =>
            WarPeaceSettlementValidationRules.DetailId(ProposalId);
        public long WarId { get; set; } = -1;
        public long RequesterKingdomId { get; set; } = -1;
        public long ResponderKingdomId { get; set; } = -1;
        public WarPeaceSettlementScopeKind Scope { get; set; } =
            WarPeaceSettlementScopeKind.Coalition;
        public long ExitRootKingdomId { get; set; } = -1;
        public int SignedWarScore { get; set; }
        public int TotalCost { get; set; }
        public bool PlayerInitiated { get; set; }
        public bool AutomaticExhaustionSettlement { get; internal set; }
        public WarPeaceSettlementStatus Status { get; set; }
        public string ResponseReason { get; set; } = "";
        public int RecoveryAttempts { get; set; }
        public int CreatedYear { get; set; } = -1;
        public int ResponseYear { get; set; } = -1;
        public List<WarPeaceSettlementTerm> Terms { get; } =
            new List<WarPeaceSettlementTerm>();
        public List<WarPeaceSettlementParticipantSnapshot> Participants
            { get; } = new List<WarPeaceSettlementParticipantSnapshot>();

        public static WarPeaceSettlementProposal Create(long proposalId,
            WarPeaceSettlementDraft draft,
            IReadOnlyList<WarPeaceSettlementTerm> terms)
        {
            var proposal = new WarPeaceSettlementProposal
            {
                ProposalId = proposalId,
                WarId = draft.WarId,
                RequesterKingdomId = draft.RequesterKingdomId,
                ResponderKingdomId = draft.ResponderKingdomId,
                Scope = draft.Scope,
                ExitRootKingdomId = draft.ExitRootKingdomId,
                SignedWarScore = WarPeaceTermsRules.ClampSignedWarScore(
                    draft.SignedWarScore),
                PlayerInitiated = draft.PlayerInitiated,
                AutomaticExhaustionSettlement =
                    draft.AutomaticExhaustionSettlement,
                Status = WarPeaceSettlementStatus.Pending
            };
            for (int i = 0; i < terms.Count; i++)
            {
                WarPeaceSettlementTerm term = terms[i].Clone();
                proposal.Terms.Add(term);
                if (WarPeaceSettlementValidationRules.
                        TryResolveRecipientSide(draft.RequesterKingdomId,
                            draft.ResponderKingdomId, draft.Participants,
                            term.ToKingdomId, out bool requesterSide) &&
                    requesterSide)
                    proposal.TotalCost += term.Cost;
            }
            for (int i = 0; i < draft.Participants.Count; i++)
            {
                WarPeaceSettlementParticipantSnapshot participant =
                    draft.Participants[i];
                if (participant != null)
                    proposal.Participants.Add(participant.Clone());
            }
            return proposal;
        }
    }

    public sealed class WarPeacePrepareResult
    {
        internal WarPeacePrepareResult(bool success,
            WarPeaceSettlementProposal proposal, string reason)
        {
            Success = success;
            Proposal = proposal;
            Reason = reason ?? "";
        }

        public bool Success { get; }
        public WarPeaceSettlementProposal Proposal { get; }
        public long ProposalId => Proposal?.ProposalId ?? -1;
        public string DetailId => Proposal?.DetailId ?? "";
        public string Reason { get; }
    }

    public sealed class WarPeaceValidationResult
    {
        internal WarPeaceValidationResult(bool success, long proposalId,
            string reason)
        {
            Success = success;
            ProposalId = proposalId;
            Reason = reason ?? "";
        }

        public bool Success { get; }
        public long ProposalId { get; }
        public string Reason { get; }
    }

    public sealed class WarPeaceExecutionResult
    {
        internal WarPeaceExecutionResult(bool success, long proposalId,
            string reason)
        {
            Success = success;
            ProposalId = proposalId;
            Reason = reason ?? "";
        }

        public bool Success { get; }
        public long ProposalId { get; }
        public string Reason { get; }
    }

    public sealed class WarPeaceDecisionResult
    {
        internal WarPeaceDecisionResult(bool success, long proposalId,
            WarPeaceSettlementStatus status, string reason)
        {
            Success = success;
            ProposalId = proposalId;
            Status = status;
            Reason = reason ?? "";
        }

        public bool Success { get; }
        public long ProposalId { get; }
        public WarPeaceSettlementStatus Status { get; }
        public string Reason { get; }
    }

    public sealed class WarPeaceDefaultTermCandidate
    {
        public WarPeaceDefaultTermCandidate(
            WarPeaceSettlementTermDraft term, bool isWarGoal, int priority,
            bool eligible)
        {
            Term = term;
            IsWarGoal = isWarGoal;
            Priority = priority;
            Eligible = eligible;
        }

        public WarPeaceSettlementTermDraft Term { get; }
        public bool IsWarGoal { get; }
        public int Priority { get; }
        public bool Eligible { get; }
    }

    public interface IWarPeaceSettlementStore
    {
        bool TryCreate(WarPeaceSettlementDraft draft,
            IReadOnlyList<WarPeaceSettlementTerm> terms,
            out WarPeaceSettlementProposal proposal, out string reason);
        bool TryRead(long proposalId,
            out WarPeaceSettlementProposal proposal);
        bool TryBackfillParticipants(long proposalId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot>
                participants);
        bool TrySetStatus(long proposalId,
            WarPeaceSettlementStatus expected,
            WarPeaceSettlementStatus next, string reason);
        bool TrySetTermApplyStatus(long proposalId, long termId,
            WarPeaceSettlementTermApplyStatus expected,
            WarPeaceSettlementTermApplyStatus next, string reason);
        bool TryBeginTermApplication(long proposalId, long termId,
            WarPeaceTermExecutionBaseline baseline, string reason);
        bool TryHasExecutedCoalitionSettlement(long warId,
            out bool executed);
        bool TryReadExecutedCoalitionTerms(long warId,
            out IReadOnlyList<WarPeaceSettlementTerm> terms);
        bool HasExecutedCoalitionSettlement(long warId);
        IReadOnlyList<WarPeaceSettlementTerm> ReadExecutedCoalitionTerms(
            long warId);
        bool HasExecutedSettlement(long warId);
        IReadOnlyList<WarPeaceSettlementTerm> ReadExecutedTerms(long warId);
        IReadOnlyList<long> ReadRecoveryCandidatesForKingdom(
            long kingdomId, int limit);
        bool TryMarkRecoveryAttempt(long proposalId);
        bool TryReadExecutedProposalForWar(long warId,
            out long proposalId);
    }

    public interface IWarPeaceSettlementActionableStore
    {
        bool TryReadActionableWinnerProposalForWar(long warId,
            long requesterKingdomId, long responderKingdomId,
            out long proposalId);
    }

    public interface IWarPeaceSettlementOrphanRecoveryStore
    {
        bool TryCancelOneOrphanedPendingForKingdom(long kingdomId,
            out long cancelledProposalId);
    }

    public interface IWarPeaceSettlementExecutionGuardStore
    {
        bool HasActionableSettlement(long warId);
    }

    public interface IWarPeaceSettlementWorld
    {
        bool TryPrepareScope(WarPeaceSettlementDraft draft,
            out string reason);
        bool TryValidateScope(WarPeaceSettlementProposal proposal,
            out string reason);
        bool TryGetAuthoritativeSignedWarScore(
            WarPeaceSettlementDraft draft, out int score,
            out string reason);
        bool TryInspect(WarPeaceSettlementDraft draft,
            WarPeaceSettlementTermDraft term,
            out WarPeaceSettlementTermFacts facts, out string reason);
        bool TryValidate(WarPeaceSettlementProposal proposal,
            out string reason);
        bool IsWarEnded(long warId);
        bool IsSettlementFinalized(WarPeaceSettlementProposal proposal);
        bool TryCaptureExecutionBaseline(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term,
            out WarPeaceTermExecutionBaseline baseline,
            out string reason);
        WarPeaceTermApplicationState InspectTermApplication(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term, out string reason);
        WarPeaceTermApplicationState InspectResourceEndpoint(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term, bool sourceEndpoint,
            out string reason);
        IWarPeaceSettlementExecution BeginExecution(
            WarPeaceSettlementProposal proposal);
    }

    public interface IWarPeaceSettlementExecution : IDisposable
    {
        bool TryApply(WarPeaceSettlementTerm term, out string reason);
        bool TryEndWar(out string reason);
        bool TryFinalizeSettlement(out string reason);
        void CommitTerm();
        void Commit();
        void Rollback();
    }
}
