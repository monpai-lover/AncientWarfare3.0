using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    public sealed partial class WarPeaceSettlementService
    {
        public static WarPeaceSettlementService Instance { get; } =
            new WarPeaceSettlementService(new WarPeaceSettlementStore(),
                new WarPeaceSettlementWorld());

        public WarPeaceSettlementDraft BuildDefaultDraft(War war,
            Kingdom requester, Kingdom responder, int signedScore,
            WarPeaceDefaultOfferMode mode)
        {
            return BuildDefaultDraft(war, requester, responder, signedScore,
                mode, pLiquidCompensationOnly: false);
        }

        public WarPeaceSettlementDraft BuildDefaultSeparateParticipantDraft(
            War war, Kingdom requester, Kingdom responder, int signedScore,
            WarPeaceDefaultOfferMode mode)
        {
            return BuildDefaultDraft(war, requester, responder, signedScore,
                mode, pLiquidCompensationOnly: true);
        }

        private static WarPeaceSettlementDraft BuildDefaultDraft(War war,
            Kingdom requester, Kingdom responder, int signedScore,
            WarPeaceDefaultOfferMode mode, bool pLiquidCompensationOnly)
        {
            var draft = new WarPeaceSettlementDraft
            {
                WarId = war?.data?.id ?? -1,
                RequesterKingdomId = requester?.id ?? -1,
                ResponderKingdomId = responder?.id ?? -1,
                SignedWarScore = signedScore,
                PlayerInitiated = false
            };
            if (mode == WarPeaceDefaultOfferMode.WhitePeace)
            {
                draft.Terms.Add(new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.WhitePeace
                });
                return draft;
            }

            Kingdom beneficiary = mode ==
                WarPeaceDefaultOfferMode.Surrender ? responder : requester;
            Kingdom payer = beneficiary == requester ? responder : requester;
            IReadOnlyList<WarPeaceDefaultTermCandidate> candidates =
                pLiquidCompensationOnly
                    ? WarPeaceSettlementWorld.
                        BuildLiquidCompensationCandidates(payer, beneficiary)
                    : WarPeaceSettlementWorld.BuildDefaultCandidates(war,
                        payer, beneficiary);
            draft.Terms.AddRange(WarPeaceDefaultOfferRules.SelectTerms(
                signedScore, mode, candidates,
                SafeKingdomCityCount(payer)));
            return draft;
        }

        private static int SafeKingdomCityCount(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1;
            try { return Math.Max(0, pKingdom.countCities()); }
            catch { return -1; }
        }

        public IReadOnlyList<WarPeaceDefaultTermCandidate>
            BuildDirectedTermCandidates(War war, Kingdom payer,
                Kingdom beneficiary)
        {
            IReadOnlyList<WarPeaceDefaultTermCandidate> generated =
                WarPeaceSettlementWorld.BuildDefaultCandidates(war, payer,
                    beneficiary);
            return WarPeaceBilateralCandidateRules.WithWhitePeace(generated,
                WarPeaceDefaultOfferRules.MaximumCandidates);
        }

        public int ProcessReparations(Kingdom payer)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return 0;
            return WarReparationsService.Process(payer);
        }
    }

    internal sealed class WarPeaceSettlementWorld :
        IWarPeaceSettlementWorld
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public bool TryPrepareScope(WarPeaceSettlementDraft draft,
            out string reason)
        {
            reason = "";
            if (draft == null || !TryContext(draft.WarId,
                    draft.RequesterKingdomId, draft.ResponderKingdomId,
                    draft.AutomaticExhaustionSettlement,
                    out War war, out Kingdom requester,
                    out Kingdom responder, out reason)) return false;
            if (!TryBuildAuthorizedScope(war, requester, responder,
                    draft.Scope, draft.ExitRootKingdomId,
                    out WarParticipantRosterContext context,
                    out long exitRootId, out reason)) return false;
            if (draft.Participants.Count > 0 &&
                !context.ValidateParticipantSnapshots(draft.Participants,
                    out reason)) return false;

            draft.ExitRootKingdomId = exitRootId;
            draft.Participants.Clear();
            draft.Participants.AddRange(
                context.BuildParticipantSnapshots());
            return true;
        }

        public bool TryValidateScope(WarPeaceSettlementProposal proposal,
            out string reason)
        {
            reason = "";
            if (proposal == null || !TryContext(proposal.WarId,
                    proposal.RequesterKingdomId,
                    proposal.ResponderKingdomId,
                    proposal.AutomaticExhaustionSettlement, out War war,
                    out Kingdom requester, out Kingdom responder,
                    out reason)) return false;
            if (!TryBuildAuthorizedScope(war, requester, responder,
                    proposal.Scope, proposal.ExitRootKingdomId,
                    out WarParticipantRosterContext context,
                    out long exitRootId, out reason)) return false;
            if (exitRootId != proposal.ExitRootKingdomId)
            {
                reason = "participant_roster_changed";
                return false;
            }
            if (proposal.Scope == WarPeaceSettlementScopeKind.Coalition &&
                proposal.Participants.Count == 0)
            {
                proposal.Participants.AddRange(
                    context.BuildParticipantSnapshots());
                return true;
            }
            return context.ValidateParticipantSnapshots(
                proposal.Participants, out reason);
        }

        public bool TryGetAuthoritativeSignedWarScore(
            WarPeaceSettlementDraft draft, out int score, out string reason)
        {
            score = 0;
            reason = "";
            if (draft == null || !TryContext(draft.WarId,
                    draft.RequesterKingdomId, draft.ResponderKingdomId,
                    draft.AutomaticExhaustionSettlement,
                    out War war, out Kingdom requester, out _, out reason))
                return false;
            if (!WarScoreService.TryGetSnapshot(war, requester,
                    out WarScoreSnapshot snapshot))
            {
                reason = "war_score_unavailable";
                return false;
            }
            score = snapshot.Score;
            reason = "";
            return true;
        }

        public bool TryInspect(WarPeaceSettlementDraft draft,
            WarPeaceSettlementTermDraft term,
            out WarPeaceSettlementTermFacts facts, out string reason)
        {
            facts = new WarPeaceSettlementTermFacts();
            if (!TryContext(draft.WarId, draft.RequesterKingdomId,
                    draft.ResponderKingdomId,
                    draft.AutomaticExhaustionSettlement,
                    out _, out _, out _,
                    out reason)) return false;
            if (!TryValidateTerm(draft.WarId, term.Kind,
                    term.FromKingdomId,
                    term.ToKingdomId, term.ResourceId, term.Amount,
                    term.DurationYears, term.CityId,
                    term.CaptiveActorId, term.ClaimId,
                    frozenOccupation: false, coreOrClaimBasis: false,
                    preparing: true, out facts, out reason)) return false;
            return true;
        }

        public bool TryValidate(WarPeaceSettlementProposal proposal,
            out string reason)
        {
            reason = "";
            if (!TryContext(proposal.WarId, proposal.RequesterKingdomId,
                    proposal.ResponderKingdomId,
                    proposal.AutomaticExhaustionSettlement,
                    out _, out _, out _,
                    out reason)) return false;
            var survival = new WarPeaceTreatySurvivalLedger();
            for (int i = 0; i < proposal.Terms.Count; i++)
            {
                WarPeaceSettlementTerm term = proposal.Terms[i];
                if (!TryValidateTerm(proposal.WarId, term.Kind,
                        term.FromKingdomId,
                        term.ToKingdomId, term.ResourceId, term.Amount,
                        term.DurationYears, term.CityId,
                        term.CaptiveActorId, term.ClaimId,
                        term.FrozenOccupation, term.CoreOrClaimBasis,
                        preparing: false, out WarPeaceSettlementTermFacts facts,
                        out reason))
                {
                    if (WarPeaceSettlementValidationRules.
                            ShouldWaiveUnavailableResourceTerm(term.Kind,
                                reason) &&
                        WarPeaceSettlementValidationRules.
                            HasIndependentSettlementTerm(proposal.Terms))
                        continue;
                    return false;
                }
                survival.Observe(term.Kind, term.FromKingdomId,
                    facts?.SourceKingdomCityCount ?? -1);
            }
            return survival.Validate(out reason);
        }

        public IWarPeaceSettlementExecution BeginExecution(
            WarPeaceSettlementProposal proposal)
        {
            War war = FindWar(proposal.WarId);
            if (war == null && (proposal.Scope !=
                    WarPeaceSettlementScopeKind.Coalition ||
                !IsWarEnded(proposal.WarId))) return null;
            return new WarPeaceSettlementExecution(proposal, war);
        }

        public bool IsWarEnded(long warId)
        {
            War war = FindWar(warId);
            if (war == null) return true;
            try { return war.hasEnded(); }
            catch { return false; }
        }

        public bool IsSettlementFinalized(
            WarPeaceSettlementProposal proposal)
        {
            if (proposal == null) return false;
            if (proposal.Scope == WarPeaceSettlementScopeKind.Coalition)
                return IsWarEnded(proposal.WarId) &&
                       DiplomacyProposalService.
                           HasCoalitionSettlementTruces(proposal);
            if (IsWarEnded(proposal.WarId)) return true;

            War war = FindWar(proposal.WarId);
            bool foundExitMember = false;
            for (int i = 0; i < proposal.Participants.Count; i++)
            {
                WarPeaceSettlementParticipantSnapshot participant =
                    proposal.Participants[i];
                if (participant == null ||
                    !participant.IncludedInExitGroup) continue;
                foundExitMember = true;
                Kingdom kingdom = FindKingdom(participant.KingdomId);
                try
                {
                    if (kingdom?.data != null && war.hasKingdom(kingdom))
                        return false;
                }
                catch { return false; }
                if (!WarParticipantEntrySourceService.Instance.
                        TryHasSeparatePeaceExit(proposal.WarId,
                            participant.KingdomId, out bool exited) ||
                    !exited) return false;
            }
            return foundExitMember;
        }

        public bool TryCaptureExecutionBaseline(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term,
            out WarPeaceTermExecutionBaseline baseline,
            out string reason)
        {
            baseline = new WarPeaceTermExecutionBaseline(false, -1, -1);
            reason = "";
            if (proposal == null || term == null)
            {
                reason = "invalid_term_baseline";
                return false;
            }
            if (term.Kind == WarPeaceTermKind.CedeCity)
            {
                City pendingCity = FindCity(term.CityId);
                long ownerId = pendingCity?.kingdom?.id ?? -1;
                bool liveFrozen = TryResolveFrozenOccupationRecipient(
                    proposal.WarId, term.FromKingdomId, term.CityId,
                    term.ToKingdomId, out long controllerId);
                bool frozen = term.FrozenOccupation || liveFrozen;
                bool basis = term.CoreOrClaimBasis || HasCoreOrClaim(
                    term.ToKingdomId, term.FromKingdomId, term.CityId);
                if (!WarPeaceSettlementValidationRules.
                        CanExecuteFrozenOccupationCede(
                            pendingCity?.data != null &&
                            !pendingCity.isRekt(), frozen,
                            ownerId == term.FromKingdomId,
                            ownerId == term.ToKingdomId,
                            ownerId == controllerId, basis))
                {
                    reason = "city_owner_changed_during_recovery";
                    return false;
                }
                baseline = new WarPeaceTermExecutionBaseline(true,
                    -1, -1, -1, -1);
                return true;
            }
            if (term.Kind != WarPeaceTermKind.GoldPayment &&
                term.Kind != WarPeaceTermKind.MaterialPayment)
            {
                baseline = new WarPeaceTermExecutionBaseline(true, -1, -1);
                return true;
            }

            Kingdom from = FindKingdom(term.FromKingdomId);
            Kingdom to = FindKingdom(term.ToKingdomId);
            City source = from?.capital;
            City target = to?.capital;
            string resourceId = term.Kind ==
                WarPeaceTermKind.GoldPayment ? "gold" : term.ResourceId;
            if (source == null || target == null || term.Amount <= 0 ||
                string.IsNullOrWhiteSpace(resourceId))
            {
                reason = "payment_baseline_unavailable";
                return false;
            }
            try
            {
                int sourceBefore = source.getResourcesAmount(resourceId);
                int targetBefore = target.getResourcesAmount(resourceId);
                int targetCapacity = WarPeaceResourceTransferService
                    .AvailableStockpileCapacity(target, resourceId);
                int transferable = WarPeaceResourceTransferRules
                    .TransferableAmount(term.Amount, sourceBefore,
                        targetCapacity);
                long targetAfter = (long)targetBefore + transferable;
                if (sourceBefore < term.Amount || targetBefore < 0 ||
                    transferable != term.Amount ||
                    targetAfter > int.MaxValue)
                {
                    reason = sourceBefore < term.Amount
                        ? "payment_no_longer_available"
                        : "recipient_storage_full";
                    return false;
                }
                baseline = new WarPeaceTermExecutionBaseline(true,
                    sourceBefore, targetBefore, source.data.id,
                    target.data.id, targetCapacity);
                return true;
            }
            catch
            {
                reason = "payment_baseline_unavailable";
                return false;
            }
        }

        public WarPeaceTermApplicationState InspectTermApplication(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term, out string reason)
        {
            reason = "";
            if (proposal == null || term == null ||
                !term.BaselineCaptured &&
                term.Kind != WarPeaceTermKind.CedeCity)
            {
                reason = "term_baseline_missing";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            switch (term.Kind)
            {
                case WarPeaceTermKind.WhitePeace:
                    return WarPeaceTermApplicationState.Applied;
                case WarPeaceTermKind.GoldPayment:
                    return InspectResourceTransfer(term, "gold",
                        out reason);
                case WarPeaceTermKind.MaterialPayment:
                    return InspectResourceTransfer(term, term.ResourceId,
                        out reason);
                case WarPeaceTermKind.Reparations:
                    return InspectReparations(proposal, term, out reason);
                case WarPeaceTermKind.ReleaseCaptives:
                    Actor captive = FindActor(term.CaptiveActorId);
                    if (captive?.data == null || !captive.isAlive() ||
                        captive.isRekt())
                    {
                        reason = "captive_state_ambiguous";
                        return WarPeaceTermApplicationState.Ambiguous;
                    }
                    return SlaveService.IsSlave(captive)
                        ? WarPeaceTermApplicationState.NotApplied
                        : WarPeaceTermApplicationState.Applied;
                case WarPeaceTermKind.RenounceClaims:
                    return InspectRenouncedClaim(term, out reason);
                case WarPeaceTermKind.ForceTributary:
                    return InspectSubjectRelation(term, true, out reason);
                case WarPeaceTermKind.ForceVassal:
                    return InspectSubjectRelation(term, false, out reason);
                case WarPeaceTermKind.CedeCity:
                    City city = FindCity(term.CityId);
                    Kingdom from = FindKingdom(term.FromKingdomId);
                    Kingdom to = FindKingdom(term.ToKingdomId);
                    if (city?.data == null || from?.data == null ||
                        to?.data == null)
                    {
                        reason = "city_state_ambiguous";
                        return WarPeaceTermApplicationState.Ambiguous;
                    }
                    if (city.kingdom == to)
                        return WarPeaceTermApplicationState.Applied;
                    if (city.kingdom == from)
                        return WarPeaceTermApplicationState.NotApplied;
                    if (TryResolveFrozenOccupationRecipient(proposal.WarId,
                            term.FromKingdomId, term.CityId,
                            term.ToKingdomId, out long controllerId) &&
                        city.kingdom?.id == controllerId)
                        return WarPeaceTermApplicationState.NotApplied;
                    reason = "city_owner_changed_during_recovery";
                    return WarPeaceTermApplicationState.Ambiguous;
                case WarPeaceTermKind.TakeMandate:
                case WarPeaceTermKind.Independence:
                case WarPeaceTermKind.ReunifySuccession:
                case WarPeaceTermKind.NoCbOutcome:
                    return WarPeaceTermApplicationState.Applied;
                case WarPeaceTermKind.RestoreKingdom:
                    RoyalClaimService.WarGoalRestorationApplicationState
                        restoration = RoyalClaimService.
                            InspectWarGoalRestoration(
                                FindKingdom(term.ToKingdomId),
                                FindKingdom(term.FromKingdomId),
                                term.ClaimId, FindCity(term.CityId),
                                out reason);
                    return restoration == RoyalClaimService.
                        WarGoalRestorationApplicationState.Applied
                        ? WarPeaceTermApplicationState.Applied
                        : restoration == RoyalClaimService.
                            WarGoalRestorationApplicationState.NotApplied
                            ? WarPeaceTermApplicationState.NotApplied
                            : WarPeaceTermApplicationState.Ambiguous;
                default:
                    reason = "unsupported_peace_term";
                    return WarPeaceTermApplicationState.Ambiguous;
            }
        }

        public WarPeaceTermApplicationState InspectResourceEndpoint(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term, bool sourceEndpoint,
            out string reason)
        {
            reason = "";
            if (proposal == null || term == null ||
                !term.BaselineCaptured || term.Amount <= 0)
            {
                reason = "payment_endpoint_baseline_missing";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            string resourceId = term.Kind ==
                WarPeaceTermKind.GoldPayment ? "gold" : term.ResourceId;
            long cityId = sourceEndpoint
                ? term.SourceCityId
                : term.TargetCityId;
            int before = sourceEndpoint
                ? term.SourceAmountBefore
                : term.TargetAmountBefore;
            long expectedAfter = sourceEndpoint
                ? (long)before - term.Amount
                : (long)before + term.Amount;
            City city = FindCity(cityId);
            if (city?.data == null || before < 0 || expectedAfter < 0 ||
                expectedAfter > int.MaxValue ||
                string.IsNullOrWhiteSpace(resourceId))
            {
                reason = "payment_endpoint_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            try
            {
                int current = city.getResourcesAmount(resourceId);
                if (current == expectedAfter)
                    return WarPeaceTermApplicationState.Applied;
                if (current == before)
                    return WarPeaceTermApplicationState.NotApplied;
                reason = "payment_endpoint_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            catch
            {
                reason = "payment_endpoint_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
        }

        private static WarPeaceTermApplicationState InspectResourceTransfer(
            WarPeaceSettlementTerm term, string resourceId,
            out string reason)
        {
            reason = "";
            City source = FindCity(term.SourceCityId);
            City target = FindCity(term.TargetCityId);
            if (source == null || target == null || term.Amount <= 0 ||
                string.IsNullOrWhiteSpace(resourceId) ||
                term.SourceAmountBefore < term.Amount ||
                term.TargetAmountBefore < 0)
            {
                reason = "payment_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            try
            {
                int sourceNow = source.getResourcesAmount(resourceId);
                int targetNow = target.getResourcesAmount(resourceId);
                WarPeaceTermApplicationState state =
                    WarPeaceSettlementValidationRules
                        .ClassifyResourceTransfer(
                            term.SourceAmountBefore,
                            term.TargetAmountBefore, term.Amount,
                            sourceNow, targetNow);
                if (state != WarPeaceTermApplicationState.Ambiguous)
                    return state;
                reason = "payment_state_mixed_during_recovery";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            catch
            {
                reason = "payment_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
        }

        private static WarPeaceTermApplicationState InspectReparations(
            WarPeaceSettlementProposal proposal,
            WarPeaceSettlementTerm term, out string reason)
        {
            reason = "";
            SQLiteConnection db = DB;
            if (db == null)
            {
                reason = "reparations_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT 1 FROM " +
                    WarReparationsObligationTableItem.GetTableName() +
                    " WHERE PROPOSAL_ID=@proposal AND TERM_ID=@term " +
                    "LIMIT 1";
                command.Parameters.AddWithValue("@proposal",
                    proposal.ProposalId);
                command.Parameters.AddWithValue("@term", term.TermId);
                return command.ExecuteScalar() != null
                    ? WarPeaceTermApplicationState.Applied
                    : WarPeaceTermApplicationState.NotApplied;
            }
            catch
            {
                reason = "reparations_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
        }

        private static WarPeaceTermApplicationState InspectRenouncedClaim(
            WarPeaceSettlementTerm term, out string reason)
        {
            reason = "";
            SQLiteConnection db = DB;
            if (db == null)
            {
                reason = "claim_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT ACTIVE,CONSUMED FROM " +
                    WarClaimTableItem.GetTableName() +
                    " WHERE CLAIM_ID=@id LIMIT 1";
                command.Parameters.AddWithValue("@id", term.ClaimId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    reason = "claim_state_ambiguous";
                    return WarPeaceTermApplicationState.Ambiguous;
                }
                int active = reader.GetInt32(0);
                int consumed = reader.GetInt32(1);
                if (active == 0 && consumed == 1)
                    return WarPeaceTermApplicationState.Applied;
                if (active == 1)
                    return WarPeaceTermApplicationState.NotApplied;
                reason = "claim_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            catch
            {
                reason = "claim_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
        }

        private static WarPeaceTermApplicationState InspectSubjectRelation(
            WarPeaceSettlementTerm term, bool tributary,
            out string reason)
        {
            reason = "";
            Kingdom subject = FindKingdom(term.FromKingdomId);
            Kingdom suzerain = FindKingdom(term.ToKingdomId);
            if (subject?.data == null || suzerain?.data == null)
            {
                reason = "subject_state_ambiguous";
                return WarPeaceTermApplicationState.Ambiguous;
            }
            Kingdom currentVassal = VassalService.GetSuzerain(subject);
            Kingdom currentTributary =
                VassalService.GetTributarySuzerain(subject);
            if (tributary && currentTributary == suzerain ||
                !tributary && currentVassal == suzerain)
                return WarPeaceTermApplicationState.Applied;
            if (currentVassal == null && currentTributary == null)
                return WarPeaceTermApplicationState.NotApplied;
            reason = "subject_relation_changed_during_recovery";
            return WarPeaceTermApplicationState.Ambiguous;
        }

        internal static IReadOnlyList<WarPeaceDefaultTermCandidate>
            BuildDefaultCandidates(War war, Kingdom payer,
                Kingdom beneficiary)
        {
            var result = new List<WarPeaceDefaultTermCandidate>();
            if (war?.data == null || payer?.data == null ||
                beneficiary?.data == null) return result;
            AddWarGoalCandidate(result, war, payer, beneficiary);

            var candidateCityIds = new HashSet<long>();
            try
            {
                IReadOnlyList<WarScoreOccupiedCitySnapshot> occupied =
                    WarScoreService.ReadFrozenOccupationsForHomeKingdom(
                        war.data.id, payer.id, 64);
                WarScoreSide beneficiarySide = ResolveWarScoreSide(war,
                    beneficiary);
                for (int i = 0; i < occupied.Count &&
                                candidateCityIds.Count < 8; i++)
                {
                    WarScoreOccupiedCitySnapshot control = occupied[i];
                    City city = FindCity(control.CityId);
                    Kingdom recipient = FindKingdom(
                        control.ControllerKingdomId);
                    bool controllerOnBeneficiarySide =
                        recipient?.data != null &&
                        WarScoreRules.IsParticipantSide(beneficiarySide) &&
                        control.ControllerSide == beneficiarySide &&
                        ResolveWarScoreSide(war, recipient) ==
                        beneficiarySide;
                    if (!WarPeaceSettlementValidationRules.
                            CanUseFrozenOccupationCandidate(
                                city?.data != null && !city.isRekt(),
                                control.HomeKingdomId == payer.id,
                                controllerOnBeneficiarySide,
                                city?.kingdom == payer,
                                city?.kingdom == recipient) ||
                        candidateCityIds.Contains(city.data.id)) continue;
                    bool beneficiaryIsWarLeader =
                        IsWarLeaderForSide(war, beneficiary,
                            beneficiarySide);
                    Kingdom warLeaderRecipient = beneficiaryIsWarLeader &&
                        recipient.id != beneficiary.id ? beneficiary : null;
                    AddFrozenOccupationCandidates(result, city, payer,
                        recipient, warLeaderRecipient,
                        controllerPriority: 76, warLeaderPriority: 75);
                    candidateCityIds.Add(city.data.id);
                }
            }
            catch { }

            AddTerritorialBasisCandidates(result, candidateCityIds, payer,
                beneficiary);

            result.Add(new WarPeaceDefaultTermCandidate(
                new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.ForceVassal,
                    RequestedCost =
                        WarPeaceTermsRules.MinimumTermCost(
                            WarPeaceTermKind.ForceVassal),
                    FromKingdomId = payer.id,
                    ToKingdomId = beneficiary.id
                }, false, 100,
                CanForceVassalTransfer(payer, beneficiary)));
            result.Add(new WarPeaceDefaultTermCandidate(
                new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.ForceTributary,
                    RequestedCost =
                        WarPeaceTermsRules.MinimumTermCost(
                            WarPeaceTermKind.ForceTributary),
                    FromKingdomId = payer.id,
                    ToKingdomId = beneficiary.id
                }, false, 80, CanForceTributary(payer, beneficiary)));
            result.Add(new WarPeaceDefaultTermCandidate(
                new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.Reparations,
                    RequestedCost = 20,
                    FromKingdomId = payer.id,
                    ToKingdomId = beneficiary.id,
                    ResourceId = "gold",
                    Amount = 10,
                    DurationYears = 5
                }, false, 60, true));
            AddImmediatePaymentCandidates(result, payer, beneficiary);
            AddRenounceClaimCandidates(result, payer, beneficiary);
            // SlaveState has no KINGDOM_ID/ACTIVE index. Deliberately do not
            // scan every actor or the whole slave table for captive options.
            if (result.Count > WarPeaceDefaultOfferRules.MaximumCandidates)
                result.RemoveRange(
                    WarPeaceDefaultOfferRules.MaximumCandidates,
                    result.Count -
                    WarPeaceDefaultOfferRules.MaximumCandidates);
            return result;
        }

        private static void AddFrozenOccupationCandidates(
            List<WarPeaceDefaultTermCandidate> pResult, City pCity,
            Kingdom pPayer, Kingdom pController, Kingdom pWarLeader,
            int controllerPriority, int warLeaderPriority)
        {
            WarPeaceCityValueFacts facts = CityFacts(pCity,
                pController.id, pPayer.id);
            int cost = WarPeaceTermsRules.CityCessionCost(facts);
            AddFrozenOccupationCandidate(pResult, pCity, pPayer,
                pController, cost, controllerPriority);
            if (pWarLeader?.data != null)
                AddFrozenOccupationCandidate(pResult, pCity, pPayer,
                    pWarLeader, cost, warLeaderPriority);
        }

        private static void AddFrozenOccupationCandidate(
            List<WarPeaceDefaultTermCandidate> pResult, City pCity,
            Kingdom pPayer, Kingdom pRecipient, int pCost, int pPriority)
        {
            pResult.Add(new WarPeaceDefaultTermCandidate(
                new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.CedeCity,
                    RequestedCost = pCost,
                    FromKingdomId = pPayer.id,
                    ToKingdomId = pRecipient.id,
                    CityId = pCity.data.id
                }, false, pPriority, true));
        }

        private static bool IsWarLeaderForSide(War pWar,
            Kingdom pKingdom, WarScoreSide pSide)
        {
            if (pWar?.data == null || pKingdom?.data == null)
                return false;
            Kingdom leader = pSide == WarScoreSide.Attackers
                ? pWar.main_attacker
                : pSide == WarScoreSide.Defenders
                    ? pWar.main_defender
                    : null;
            return leader?.data != null && leader.id == pKingdom.id;
        }

        private static WarScoreSide ResolveWarScoreSide(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null)
                return WarScoreSide.None;
            try
            {
                if (pWar.isAttacker(pKingdom))
                    return WarScoreSide.Attackers;
                if (pWar.isDefender(pKingdom))
                    return WarScoreSide.Defenders;
            }
            catch { }
            return WarScoreSide.None;
        }

        private static bool IsOnSameWarSide(War pWar, Kingdom pLeft,
            Kingdom pRight)
        {
            if (pWar?.data == null || pLeft?.data == null ||
                pRight?.data == null) return false;
            try
            {
                return pWar.onTheSameSide(pLeft, pRight);
            }
            catch { return false; }
        }

        internal static IReadOnlyList<WarPeaceDefaultTermCandidate>
            BuildLiquidCompensationCandidates(Kingdom payer,
                Kingdom beneficiary)
        {
            var result = new List<WarPeaceDefaultTermCandidate>();
            if (payer?.data == null || beneficiary?.data == null)
                return result;
            AddImmediatePaymentCandidates(result, payer, beneficiary);
            if (result.Count > WarPeaceDefaultOfferRules.MaximumCandidates)
                result.RemoveRange(
                    WarPeaceDefaultOfferRules.MaximumCandidates,
                    result.Count -
                    WarPeaceDefaultOfferRules.MaximumCandidates);
            return result;
        }

        private static void AddImmediatePaymentCandidates(
            List<WarPeaceDefaultTermCandidate> result, Kingdom payer,
            Kingdom beneficiary)
        {
            City source = payer?.capital;
            if (source == null || beneficiary?.capital == null) return;
            try
            {
                int gold = Math.Max(0,
                    source.getResourcesAmount("gold"));
                if (gold > 0)
                {
                    int proposed = Math.Min(500, Math.Max(1, gold / 4));
                    int amount = WarPeaceResourceTransferRules
                        .TransferableAmount(proposed, gold,
                            WarPeaceResourceTransferService
                                .AvailableStockpileCapacity(
                                    beneficiary.capital, "gold"));
                    if (amount > 0)
                        result.Add(new WarPeaceDefaultTermCandidate(
                            PaymentTerm(WarPeaceTermKind.GoldPayment,
                                payer, beneficiary, "gold", amount),
                            false, 55, true));
                }

                int foodAdded = 0;
                using ListPool<CityStorageSlot> slots =
                    source.getTotalResourceSlots(new[]
                    {
                        ResType.Food,
                        ResType.Ingredient_Food
                    });
                for (int i = 0; i < slots.Count && foodAdded < 3; i++)
                {
                    CityStorageSlot slot = slots[i];
                    if (slot?.asset == null || !slot.asset.food ||
                        slot.amount <= 0) continue;
                    int proposed = Math.Min(300,
                        Math.Max(1, slot.amount / 4));
                    int amount = WarPeaceResourceTransferRules
                        .TransferableAmount(proposed, slot.amount,
                            WarPeaceResourceTransferService
                                .AvailableStockpileCapacity(
                                    beneficiary.capital, slot.asset.id));
                    if (amount <= 0) continue;
                    result.Add(new WarPeaceDefaultTermCandidate(
                        PaymentTerm(WarPeaceTermKind.MaterialPayment,
                            payer, beneficiary, slot.asset.id, amount),
                        false, 50 - foodAdded, true));
                    foodAdded++;
                }
            }
            catch { }
        }

        private static WarPeaceSettlementTermDraft PaymentTerm(
            WarPeaceTermKind kind, Kingdom payer, Kingdom beneficiary,
            string resourceId, int amount)
        {
            return new WarPeaceSettlementTermDraft
            {
                Kind = kind,
                RequestedCost = Math.Max(5,
                    Math.Min(25, 5 + amount / 50)),
                FromKingdomId = payer.id,
                ToKingdomId = beneficiary.id,
                ResourceId = resourceId ?? "",
                Amount = amount
            };
        }

        private static void AddRenounceClaimCandidates(
            List<WarPeaceDefaultTermCandidate> result, Kingdom payer,
            Kingdom beneficiary)
        {
            SQLiteConnection db = DB;
            if (db == null) return;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT CLAIM_ID FROM " +
                    WarClaimTableItem.GetTableName() +
                    " WHERE SOURCE_KINGDOM_ID=@payer AND " +
                    "TARGET_KINGDOM_ID=@beneficiary AND ACTIVE=1 " +
                    "AND CONSUMED=0 ORDER BY CLAIM_ID ASC LIMIT 4";
                command.Parameters.AddWithValue("@payer", payer.id);
                command.Parameters.AddWithValue("@beneficiary",
                    beneficiary.id);
                using SQLiteDataReader reader = command.ExecuteReader();
                int priority = 45;
                while (reader.Read())
                {
                    result.Add(new WarPeaceDefaultTermCandidate(
                        new WarPeaceSettlementTermDraft
                        {
                            Kind = WarPeaceTermKind.RenounceClaims,
                            RequestedCost =
                                WarPeaceTermsRules.MinimumTermCost(
                                    WarPeaceTermKind.RenounceClaims),
                            FromKingdomId = payer.id,
                            ToKingdomId = beneficiary.id,
                            ClaimId = reader.GetInt64(0)
                        }, false, priority--, true));
                }
            }
            catch { }
        }

        private static void AddTerritorialBasisCandidates(
            List<WarPeaceDefaultTermCandidate> result,
            HashSet<long> candidateCityIds, Kingdom payer,
            Kingdom beneficiary)
        {
            SQLiteConnection db = DB;
            if (db == null) return;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT CITY_ID FROM (" +
                    "SELECT CITY_ID FROM " +
                    KingdomCoreTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@beneficiary AND " +
                    "OWNER_KINGDOM_ID=@payer AND ACTIVE=1 UNION " +
                    "SELECT TARGET_CITY_ID AS CITY_ID FROM " +
                    WarClaimTableItem.GetTableName() +
                    " WHERE SOURCE_KINGDOM_ID=@beneficiary AND " +
                    "TARGET_KINGDOM_ID=@payer AND ACTIVE=1 AND " +
                    "CONSUMED=0 AND TARGET_CITY_ID>=0) " +
                    "ORDER BY CITY_ID ASC LIMIT 8";
                command.Parameters.AddWithValue("@beneficiary",
                    beneficiary.id);
                command.Parameters.AddWithValue("@payer", payer.id);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long cityId = reader.GetInt64(0);
                    if (!candidateCityIds.Add(cityId)) continue;
                    City city = FindCity(cityId);
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != payer) continue;
                    WarPeaceCityValueFacts facts = CityFacts(city,
                        beneficiary.id, payer.id);
                    result.Add(new WarPeaceDefaultTermCandidate(
                        new WarPeaceSettlementTermDraft
                        {
                            Kind = WarPeaceTermKind.CedeCity,
                            RequestedCost =
                                WarPeaceTermsRules.CityCessionCost(facts),
                            FromKingdomId = payer.id,
                            ToKingdomId = beneficiary.id,
                            CityId = cityId
                        }, false, 70, true));
                }
            }
            catch { }
        }

        private static bool TryValidateTerm(long warId,
            WarPeaceTermKind kind,
            long fromId, long toId, string resourceId, int amount,
            int durationYears, long cityId, long captiveActorId,
            long claimId, bool frozenOccupation, bool coreOrClaimBasis,
            bool preparing, out WarPeaceSettlementTermFacts facts,
            out string reason)
        {
            facts = new WarPeaceSettlementTermFacts();
            reason = "";
            if (kind == WarPeaceTermKind.WhitePeace) return true;
            Kingdom from = FindKingdom(fromId);
            Kingdom to = FindKingdom(toId);
            if (!ValidKingdom(from) || !ValidKingdom(to) || from == to)
            {
                reason = "invalid_term_participants";
                return false;
            }
            facts.SourceKingdomCityCount = CountLiveCities(from);
            switch (kind)
            {
                case WarPeaceTermKind.GoldPayment:
                    return ValidateResource(from, to, "gold", amount,
                        out reason);
                case WarPeaceTermKind.MaterialPayment:
                    return ValidateResource(from, to, resourceId, amount,
                        out reason);
                case WarPeaceTermKind.Reparations:
                    if (DB != null && amount > 0 && durationYears > 0)
                        return true;
                    reason = "invalid_reparations";
                    return false;
                case WarPeaceTermKind.ReleaseCaptives:
                    Actor captive = FindActor(captiveActorId);
                    if (captive?.data != null && captive.isAlive() &&
                        !captive.isRekt() && captive.kingdom == from &&
                        SlaveService.IsSlave(captive)) return true;
                    reason = "captive_no_longer_held";
                    return false;
                case WarPeaceTermKind.RenounceClaims:
                    if (HasRenounceableClaim(claimId, fromId, toId))
                        return true;
                    reason = "claim_no_longer_active";
                    return false;
                case WarPeaceTermKind.ForceTributary:
                    if (CanForceTributary(from, to)) return true;
                    reason = "subject_relation_unavailable";
                    return false;
                case WarPeaceTermKind.ForceVassal:
                    if (CanForceVassalTransfer(from, to)) return true;
                    reason = "subject_relation_unavailable";
                    return false;
                case WarPeaceTermKind.CedeCity:
                    City city = FindCity(cityId);
                    long ownerId = city?.kingdom?.id ?? -1;
                    bool liveOccupied =
                        TryResolveFrozenOccupationRecipient(warId, fromId,
                            cityId, toId, out long controllerId);
                    bool occupied = frozenOccupation || liveOccupied;
                    bool basis = coreOrClaimBasis ||
                        HasCoreOrClaim(toId, fromId, cityId);
                    facts = new WarPeaceSettlementTermFacts
                    {
                        OccupiedByDemandingSide = occupied,
                        HasCoreOrClaim = basis,
                        CityValue = CityFacts(city, toId, fromId)
                    };
                    if (WarPeaceSettlementValidationRules.
                            CanExecuteFrozenOccupationCede(
                                city?.data != null && !city.isRekt(),
                                occupied, ownerId == fromId,
                                ownerId == toId,
                                ownerId == controllerId, basis))
                        return true;
                    reason = city?.data == null || city.isRekt() ||
                             ownerId != fromId && ownerId != toId &&
                             ownerId != controllerId
                        ? "city_no_longer_available"
                        : "no_territorial_basis";
                    return false;
                case WarPeaceTermKind.TakeMandate:
                case WarPeaceTermKind.Independence:
                case WarPeaceTermKind.ReunifySuccession:
                case WarPeaceTermKind.NoCbOutcome:
                    return ValidateAutomaticGoalCityControl(warId, cityId,
                        from, to, out reason);
                case WarPeaceTermKind.RestoreKingdom:
                    if (!ValidateAutomaticGoalCityControl(warId, cityId,
                            from, to, out reason)) return false;
                    RoyalClaimService.WarGoalRestorationApplicationState
                        restoration = RoyalClaimService.
                            InspectWarGoalRestoration(to, from, claimId,
                                FindCity(cityId), out reason);
                    if (restoration == RoyalClaimService.
                            WarGoalRestorationApplicationState.NotApplied ||
                        restoration == RoyalClaimService.
                            WarGoalRestorationApplicationState.Applied)
                        return true;
                    return false;
                default:
                    reason = "unsupported_peace_term";
                    return false;
            }
        }

        private static bool ValidateAutomaticGoalCityControl(long pWarId,
            long pCityId, Kingdom pDefender, Kingdom pAttacker,
            out string pReason)
        {
            pReason = "";
            City city = FindCity(pCityId);
            if (city?.data == null || city.isRekt() ||
                city.kingdom != pDefender)
            {
                pReason = "goal_city_no_longer_available";
                return false;
            }
            if (HasFrozenOccupation(pWarId, pCityId, pAttacker.id))
                return true;
            pReason = "goal_city_not_controlled";
            return false;
        }

        private static bool ValidateResource(Kingdom from, Kingdom to,
            string resourceId, int amount, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(resourceId) || amount <= 0 ||
                from?.capital == null || to?.capital == null ||
                !to.capital.hasStockpiles() ||
                AssetManager.resources.get(resourceId) == null)
            {
                reason = "invalid_resource_payment";
                return false;
            }
            if (from.capital.getResourcesAmount(resourceId) < amount)
            {
                reason = "insufficient_payment_stock";
                return false;
            }
            if (WarPeaceResourceTransferService
                    .AvailableStockpileCapacity(to.capital, resourceId) <
                amount)
            {
                reason = "recipient_storage_full";
                return false;
            }
            return true;
        }

        private static bool TryContext(long warId, long requesterId,
            long responderId, bool automaticExhaustionSettlement,
            out War war, out Kingdom requester,
            out Kingdom responder, out string reason)
        {
            war = FindWar(warId);
            requester = FindKingdom(requesterId);
            responder = FindKingdom(responderId);
            reason = "";
            if (war?.data == null || war.hasEnded())
            {
                reason = "war_no_longer_active";
                return false;
            }
            if (WarExhaustionSettlementRules.RespectsOrdinarySettlementBlock(
                    ZhuluPeaceGuard.BlocksOrdinarySettlement(war),
                    automaticExhaustionSettlement))
            {
                reason = ZhuluPeaceGuard.Reason(war);
                return false;
            }
            if (WarExhaustionSettlementRules.RespectsOrdinarySettlementBlock(
                RebellionDirectTerritoryTransferService.
                        BlocksOrdinarySettlement(war),
                    automaticExhaustionSettlement))
            {
                reason = RebellionDirectTerritoryTransferRules.
                    SettlementBlockedReason;
                return false;
            }
            if (!ValidKingdom(requester) || !ValidKingdom(responder))
            {
                reason = "invalid_settlement_participants";
                return false;
            }
            try
            {
                bool requesterAttacker = war.isAttacker(requester);
                bool requesterDefender = war.isDefender(requester);
                bool responderAttacker = war.isAttacker(responder);
                bool responderDefender = war.isDefender(responder);
                if (!(requesterAttacker && responderDefender ||
                      requesterDefender && responderAttacker))
                {
                    reason = "settlement_participants_not_opponents";
                    return false;
                }
            }
            catch
            {
                reason = "participant_roster_unavailable";
                return false;
            }
            return true;
        }

        private static bool TryBuildAuthorizedScope(War war,
            Kingdom requester, Kingdom responder,
            WarPeaceSettlementScopeKind scope, long claimedExitRootId,
            out WarParticipantRosterContext context,
            out long exitRootId, out string reason)
        {
            context = null;
            exitRootId = -1L;
            reason = "";
            if (!Enum.IsDefined(typeof(WarPeaceSettlementScopeKind), scope))
            {
                reason = "invalid_settlement_scope";
                return false;
            }

            long requesterId = requester?.data?.id ?? -1L;
            long responderId = responder?.data?.id ?? -1L;
            long mainAttackerId = war?.getMainAttacker()?.data?.id ?? -1L;
            long mainDefenderId = war?.getMainDefender()?.data?.id ?? -1L;
            bool requesterLeader = requesterId == mainAttackerId ||
                                   requesterId == mainDefenderId;
            bool responderLeader = responderId == mainAttackerId ||
                                   responderId == mainDefenderId;
            if (scope == WarPeaceSettlementScopeKind.SeparateParticipant &&
                requesterLeader != responderLeader)
                exitRootId = requesterLeader ? responderId : requesterId;

            if (claimedExitRootId >= 0 &&
                claimedExitRootId != exitRootId ||
                scope == WarPeaceSettlementScopeKind.Coalition &&
                claimedExitRootId >= 0)
            {
                reason = "invalid_exit_root";
                return false;
            }
            if (!WarParticipantRosterService.TryBuild(war, exitRootId,
                    out context, out reason)) return false;
            if (!context.TryGet(requesterId,
                    out WarParticipantRosterEntry requesterEntry) ||
                !context.TryGet(responderId,
                    out WarParticipantRosterEntry responderEntry))
            {
                reason = "participant_roster_changed";
                return false;
            }

            WarParticipantRoleKind exitRole =
                WarParticipantRoleKind.Unknown;
            if (exitRootId >= 0 && context.TryGet(exitRootId,
                    out WarParticipantRosterEntry exitRoot))
                exitRole = exitRoot.Role;
            var authority = new WarPeaceNegotiationAuthorityFacts(
                sameWar: context.WarId == war.data.id,
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
            if (WarPeaceSettlementScopeRules.CanNegotiate(scope, authority))
                return true;
            reason = scope == WarPeaceSettlementScopeKind.Coalition
                ? "settlement_requires_war_leaders"
                : "separate_peace_not_authorized";
            return false;
        }

        private static void AddWarGoalCandidate(
            List<WarPeaceDefaultTermCandidate> result, War war,
            Kingdom payer, Kingdom beneficiary)
        {
            SQLiteConnection db = DB;
            if (db == null) return;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT WAR_GOAL_ID,GOAL_TYPE," +
                    "TARGET_CITY_ID,ATTACKER_KINGDOM_ID," +
                    "SOURCE_DE_JURE_REGION_ID " +
                    "FROM " + WarGoalTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND RESOLVED=0 " +
                    "ORDER BY POSITION,WAR_GOAL_ID LIMIT 3";
                command.Parameters.AddWithValue("@war", war.data.id);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long goalId = reader.GetInt64(0);
                    string goal = reader.GetString(1);
                    long cityId = reader.GetInt64(2);
                    if (reader.GetInt64(3) != beneficiary.id) continue;
                    if (goal == WarTerritoryService
                            .GOAL_TAKE_DE_JURE_REGION)
                    {
                        if (DeJureWarGoalSettlementService.TryBuildDraft(war,
                                out WarPeaceSettlementDraft regionDraft,
                                out long regionGoalId) &&
                            regionGoalId == goalId)
                            for (int termIndex = 0;
                                 termIndex < regionDraft.Terms.Count;
                                 termIndex++)
                                result.Add(new WarPeaceDefaultTermCandidate(
                                    regionDraft.Terms[termIndex].Clone(),
                                    true, 1000 - termIndex, true));
                        continue;
                    }
                    WarPeaceSettlementTermDraft term = null;
                    if (goal == WarTerritoryService.GOAL_FORCE_VASSAL &&
                        CanForceVassalTransfer(payer, beneficiary))
                        term = SubjectTerm(WarPeaceTermKind.ForceVassal,
                            payer, beneficiary);
                    else if (goal ==
                             WarTerritoryService.GOAL_FORCE_TRIBUTARY &&
                             CanForceTributary(payer, beneficiary))
                        term = SubjectTerm(WarPeaceTermKind.ForceTributary,
                            payer, beneficiary);
                     else if (cityId >= 0)
                     {
                         City city = FindCity(cityId);
                         bool cityAvailable = city?.data != null &&
                                              !city.isRekt();
                         bool ownerMatches = cityAvailable &&
                                             city.kingdom == payer;
                         bool occupied = cityAvailable &&
                             HasFrozenOccupation(war.data.id, cityId,
                                 beneficiary.id);
                         bool basis = cityAvailable &&
                             HasCoreOrClaim(beneficiary.id, payer.id,
                                 cityId);
                         if (!WarPeaceSettlementValidationRules
                                 .CanOfferWarGoalCityCandidate(
                                     cityAvailable, ownerMatches,
                                     occupied, basis)) continue;
                         term = new WarPeaceSettlementTermDraft
                         {
                             Kind = WarPeaceTermKind.CedeCity,
                             RequestedCost =
                                 WarPeaceTermsRules.CityCessionCost(
                                     CityFacts(city, beneficiary.id,
                                         payer.id)),
                             FromKingdomId = payer.id,
                             ToKingdomId = beneficiary.id,
                             CityId = cityId
                         };
                     }
                    if (term == null) continue;
                    term.WarGoalId = goalId;
                    result.Add(new WarPeaceDefaultTermCandidate(term, true,
                        1000, true));
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Default peace goal read failed: " +
                                    error.Message);
            }
        }

        private static WarPeaceSettlementTermDraft SubjectTerm(
            WarPeaceTermKind kind, Kingdom payer, Kingdom beneficiary)
        {
            return new WarPeaceSettlementTermDraft
            {
                Kind = kind,
                RequestedCost = WarPeaceTermsRules.MinimumTermCost(kind),
                FromKingdomId = payer.id,
                ToKingdomId = beneficiary.id
            };
        }

        internal static bool HasCoreOrClaim(long kingdomId,
            long ownerKingdomId, long cityId)
        {
            ReadCityTerritorialFacts(kingdomId, ownerKingdomId, cityId,
                out bool hasCore, out bool hasClaim, out _);
            return hasCore || hasClaim;
        }

        private static void ReadCityTerritorialFacts(long kingdomId,
            long ownerKingdomId, long cityId, out bool hasCore,
            out bool hasClaim, out bool ownerHasCore)
        {
            hasCore = false;
            hasClaim = false;
            ownerHasCore = false;
            SQLiteConnection db = DB;
            if (db == null || kingdomId < 0 || ownerKingdomId < 0 ||
                cityId < 0) return;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT " +
                    "EXISTS(SELECT 1 FROM " +
                    KingdomCoreTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND CITY_ID=@city " +
                    "AND ACTIVE=1), EXISTS(SELECT 1 FROM " +
                    WarClaimTableItem.GetTableName() +
                    " WHERE SOURCE_KINGDOM_ID=@kingdom " +
                    "AND TARGET_KINGDOM_ID=@owner AND " +
                    "TARGET_CITY_ID=@city AND ACTIVE=1 AND CONSUMED=0), " +
                    "EXISTS(SELECT 1 FROM " +
                    KingdomCoreTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@owner AND CITY_ID=@city " +
                    "AND ACTIVE=1)";
                command.Parameters.AddWithValue("@kingdom", kingdomId);
                command.Parameters.AddWithValue("@owner", ownerKingdomId);
                command.Parameters.AddWithValue("@city", cityId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return;
                hasCore = reader.GetInt32(0) != 0;
                hasClaim = reader.GetInt32(1) != 0;
                ownerHasCore = reader.GetInt32(2) != 0;
            }
            catch { }
        }

        private static bool HasRenounceableClaim(long claimId, long fromId,
            long toId)
        {
            SQLiteConnection db = DB;
            if (db == null || claimId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT 1 FROM " +
                    WarClaimTableItem.GetTableName() +
                    " WHERE CLAIM_ID=@claim AND SOURCE_KINGDOM_ID=@from " +
                    "AND TARGET_KINGDOM_ID=@to AND ACTIVE=1 LIMIT 1";
                command.Parameters.AddWithValue("@claim", claimId);
                command.Parameters.AddWithValue("@from", fromId);
                command.Parameters.AddWithValue("@to", toId);
                return command.ExecuteScalar() != null;
            }
            catch { return false; }
        }

        private static bool CanForceVassalTransfer(Kingdom subject,
            Kingdom suzerain)
        {
            if (!ValidKingdom(subject) || !ValidKingdom(suzerain) ||
                subject == suzerain || !subject.hasCities() ||
                MandateRebelService.IsRebelKingdom(subject) ||
                MandateRebelService.IsRebelKingdom(suzerain)) return false;
            Kingdom currentSuzerain = VassalService.GetDiplomaticSuzerain(
                subject);
            bool cycle = VassalService.WouldCreateVassalCycle(subject,
                suzerain);
            bool participantsValid =
                VassalService.CanEnforceVassalWarVictory(subject, suzerain);
            return WarPeaceSubjectTransferRules.CanOfferForceVassal(
                participantsValid,
                alreadySubjectToRecipient: currentSuzerain == suzerain,
                wouldCreateCycle: cycle,
                hasThirdPartySuzerain: currentSuzerain?.data != null &&
                    currentSuzerain != suzerain);
        }

        private static bool CanForceTributary(Kingdom subject,
            Kingdom suzerain)
        {
            bool valid = ValidKingdom(subject) && ValidKingdom(suzerain) &&
                         subject != suzerain && subject.hasCities() &&
                         !MandateRebelService.IsRebelKingdom(subject) &&
                         !MandateRebelService.IsRebelKingdom(suzerain) &&
                         VassalService.GetRootSuzerain(suzerain) != subject;
            bool independent = valid &&
                VassalService.GetDiplomaticSuzerain(subject)?.data == null;
            return WarPeaceSubjectTransferRules.CanOfferForceTributary(
                valid, independent);
        }

        internal static bool HasFrozenOccupation(long warId, long cityId,
            long demandingKingdomId)
        {
            try
            {
                return WarScoreService.TryGetFrozenOccupation(warId,
                           cityId, out long occupierKingdomId) &&
                       occupierKingdomId == demandingKingdomId;
            }
            catch { return false; }
        }

        internal static bool TryResolveFrozenOccupationRecipient(
            long pWarId, long pSourceKingdomId, long pCityId,
            long pRecipientKingdomId, out long pControllerKingdomId)
        {
            pControllerKingdomId = -1;
            War war = FindWar(pWarId);
            Kingdom recipient = FindKingdom(pRecipientKingdomId);
            if (war?.data == null || recipient?.data == null ||
                pSourceKingdomId < 0 || pCityId < 0) return false;
            try
            {
                IReadOnlyList<WarScoreOccupiedCitySnapshot> controls =
                    WarScoreService.ReadFrozenOccupationsForHomeKingdom(
                        pWarId, pSourceKingdomId, 64);
                for (int i = 0; i < controls.Count; i++)
                {
                    WarScoreOccupiedCitySnapshot control = controls[i];
                    if (control.CityId != pCityId ||
                        control.HomeKingdomId != pSourceKingdomId) continue;
                    Kingdom controller = FindKingdom(
                        control.ControllerKingdomId);
                    bool recipientOnControllerSide =
                        controller?.data != null &&
                        WarScoreRules.IsParticipantSide(
                            control.ControllerSide) &&
                        ResolveWarScoreSide(war, controller) ==
                        control.ControllerSide &&
                        ResolveWarScoreSide(war, recipient) ==
                        control.ControllerSide;
                    bool authorized = WarPeaceSettlementValidationRules.
                        CanReceiveFrozenOccupation(
                            recipient.id == controller?.id,
                            IsWarLeaderForSide(war, recipient,
                                control.ControllerSide),
                            recipientOnControllerSide);
                    if (!authorized) return false;
                    pControllerKingdomId = control.ControllerKingdomId;
                    return true;
                }
            }
            catch { }
            return false;
        }

        internal static WarPeaceCityValueFacts CityFacts(City city,
            long demandingKingdomId, long ownerKingdomId)
        {
            float development = 0f;
            int population = 0;
            int zones = 0;
            int buildings = 0;
            bool capital = false;
            try
            {
                development = DevelopmentMapModeService.GetCityScore(city);
            }
            catch { }
            try
            {
                population = Math.Max(0, city.status?.population ??
                    city.getPopulationPeople());
                zones = city.zones?.Count ?? 0;
                buildings = city.buildings?.Count ?? 0;
                capital = city.isCapitalCity();
            }
            catch { }
            ReadCityTerritorialFacts(demandingKingdomId, ownerKingdomId,
                city?.data?.id ?? -1, out bool demandingCore,
                out bool demandingClaim, out bool ownerCore);
            return new WarPeaceCityValueFacts(development, population,
                zones, buildings, capital, demandingCore, demandingClaim,
                ownerCore);
        }

        private static bool ValidKingdom(Kingdom kingdom)
        {
            return kingdom?.data != null && !kingdom.isRekt() &&
                   kingdom.isCiv();
        }

        private static int CountLiveCities(Kingdom kingdom)
        {
            if (kingdom?.data == null) return -1;
            try { return Math.Max(0, kingdom.countCities()); }
            catch { return -1; }
        }

        internal static War FindWar(long id)
        {
            try { return id >= 0 ? World.world?.wars?.get(id) : null; }
            catch { return null; }
        }

        internal static Kingdom FindKingdom(long id)
        {
            try { return id >= 0 ? World.world?.kingdoms?.get(id) : null; }
            catch { return null; }
        }

        internal static City FindCity(long id)
        {
            try { return id >= 0 ? World.world?.cities?.get(id) : null; }
            catch { return null; }
        }

        internal static Actor FindActor(long id)
        {
            try { return id >= 0 ? World.world?.units?.get(id) : null; }
            catch { return null; }
        }
    }

    internal sealed class WarPeaceSettlementExecution :
        IWarPeaceSettlementExecution
    {
        private readonly WarPeaceSettlementProposal _proposal;
        private readonly War _war;
        private readonly List<Action> _rollbacks = new List<Action>();
        private bool _committed;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public WarPeaceSettlementExecution(
            WarPeaceSettlementProposal proposal, War war)
        {
            _proposal = proposal;
            _war = war;
        }

        public bool TryApply(WarPeaceSettlementTerm term,
            out string reason)
        {
            reason = "";
            switch (term.Kind)
            {
                case WarPeaceTermKind.WhitePeace:
                    return true;
                case WarPeaceTermKind.GoldPayment:
                    return TryTransferResource(term, "gold", out reason);
                case WarPeaceTermKind.MaterialPayment:
                    return TryTransferResource(term, term.ResourceId,
                        out reason);
                case WarPeaceTermKind.Reparations:
                    return TryCreateReparations(term, out reason);
                case WarPeaceTermKind.ReleaseCaptives:
                    return TryReleaseCaptive(term, out reason);
                case WarPeaceTermKind.RenounceClaims:
                    return TryRenounceClaim(term, out reason);
                case WarPeaceTermKind.ForceTributary:
                    return TryForceSubject(term, tributary: true,
                        out reason);
                case WarPeaceTermKind.CedeCity:
                    return TryCedeCity(term, out reason);
                case WarPeaceTermKind.ForceVassal:
                    return TryForceSubject(term, tributary: false,
                        out reason);
                case WarPeaceTermKind.TakeMandate:
                case WarPeaceTermKind.Independence:
                case WarPeaceTermKind.ReunifySuccession:
                case WarPeaceTermKind.NoCbOutcome:
                    return true;
                case WarPeaceTermKind.RestoreKingdom:
                    return RoyalClaimService.TryApplyWarGoalRestoration(
                        WarPeaceSettlementWorld.FindKingdom(
                            term.ToKingdomId),
                        WarPeaceSettlementWorld.FindKingdom(
                            term.FromKingdomId), _proposal.WarId,
                        term.ClaimId, WarPeaceSettlementWorld.FindCity(
                            term.CityId), out reason);
                default:
                    reason = "unsupported_peace_term";
                    return false;
            }
        }

        public bool TryEndWar(out string reason)
        {
            reason = "";
            if (_war?.data == null || _war.hasEnded())
            {
                reason = "war_no_longer_active";
                return false;
            }
            WarWinner winner = ResolveWinner();
            try
            {
                World.world.wars.endWar(_war, winner);
            }
            catch (Exception error)
            {
                bool ended;
                try { ended = _war.hasEnded(); }
                catch { ended = false; }
                if (!ended)
                {
                    reason = "end_war_exception:" +
                             error.GetType().Name;
                    return false;
                }
                ModClass.LogWarning("War ended but a peace postfix failed: " +
                                    error.Message);
            }
            try
            {
                if (_war.hasEnded()) return true;
            }
            catch { }
            reason = "end_war_not_committed";
            return false;
        }

        public bool TryFinalizeSettlement(out string reason)
        {
            if (_proposal.Scope == WarPeaceSettlementScopeKind.Coalition)
                return TryFinalizeCoalitionPeace(out reason);
            return TryFinalizeSeparatePeace(out reason);
        }

        private bool TryFinalizeCoalitionPeace(out string reason)
        {
            reason = "";
            bool ended;
            try { ended = _war == null || _war.hasEnded(); }
            catch { ended = false; }
            if (!ended && !TryEndWar(out reason)) return false;
            if (DiplomacyProposalService.EnsureCoalitionSettlementTruces(
                    _proposal)) return true;
            reason = "coalition_truce_write_failed";
            return false;
        }

        private bool TryFinalizeSeparatePeace(out string reason)
        {
            reason = "";
            if (_war?.data == null || _war.hasEnded())
            {
                reason = "war_no_longer_active";
                return false;
            }

            var exitFacts = new List<WarPeaceExitPlanParticipantFacts>(
                _proposal.Participants.Count);
            for (int i = 0; i < _proposal.Participants.Count; i++)
            {
                WarPeaceSettlementParticipantSnapshot participant =
                    _proposal.Participants[i];
                if (participant == null)
                {
                    reason = "separate_peace_exit_group_invalid";
                    return false;
                }
                exitFacts.Add(new WarPeaceExitPlanParticipantFacts(
                    participant.KingdomId, participant.SideKind,
                    participant.ParticipantRole == "main_belligerent",
                    participant.IncludedInExitGroup));
            }
            if (!WarPeaceSettlementScopeRules.TryBuildSeparateExitPlan(
                    _proposal.ExitRootKingdomId, exitFacts,
                    out WarPeaceSeparateExitPlan plan, out reason))
                return false;
            var exitIds = new HashSet<long>(plan.ExitKingdomIds);

            if (!WarScoreService.ClearSeparatePeaceParticipantControls(
                    _proposal.WarId, exitIds, out reason))
                return false;

            foreach (long exitId in exitIds)
            {
                Kingdom kingdom = WarPeaceSettlementWorld.FindKingdom(exitId);
                if (kingdom?.data == null) continue;
                try
                {
                    if (!_war.hasKingdom(kingdom)) continue;
                    if (_war.isMainAttacker(kingdom) ||
                        _war.isMainDefender(kingdom))
                    {
                        reason = "separate_peace_cannot_remove_war_leader";
                        return false;
                    }
                    _war.leaveWar(kingdom);
                    if (_war.hasKingdom(kingdom))
                    {
                        reason = "separate_peace_participant_exit_failed";
                        return false;
                    }
                }
                catch (Exception error)
                {
                    try
                    {
                        if (!_war.hasKingdom(kingdom)) continue;
                    }
                    catch { }
                    reason = "separate_peace_participant_exit_exception:" +
                             error.GetType().Name;
                    return false;
                }
            }

            var remainingOpponents = new HashSet<long>();
            for (int i = 0; i < plan.OpposingKingdomIds.Count; i++)
            {
                long opponentId = plan.OpposingKingdomIds[i];
                Kingdom opponent = WarPeaceSettlementWorld.FindKingdom(
                    opponentId);
                try
                {
                    if (opponent?.data != null && _war.hasKingdom(opponent))
                        remainingOpponents.Add(opponentId);
                }
                catch { }
            }

            foreach (long exitId in exitIds)
            {
                Kingdom exited = WarPeaceSettlementWorld.FindKingdom(exitId);
                if (exited?.data == null) continue;
                foreach (long opponentId in remainingOpponents)
                {
                    Kingdom opponent = WarPeaceSettlementWorld.FindKingdom(
                        opponentId);
                    if (opponent?.data == null ||
                        !DiplomacyProposalService.RegisterSeparatePeaceTruce(
                            _war, exited, opponent,
                            SettlementTruceStartYear()))
                    {
                        reason = "separate_peace_truce_write_failed";
                        return false;
                    }
                }
            }

            double now = LineageService.CurTime();
            foreach (long exitId in exitIds)
            {
                if (!WarParticipantEntrySourceService.Instance.
                        TryEndAllActiveSources(_proposal.WarId, exitId, now))
                {
                    reason = "participant_source_close_failed";
                    return false;
                }
                if (!WarParticipantEntrySourceService.Instance.
                        TryMarkSeparatePeaceExit(_proposal.WarId, exitId, now))
                {
                    reason = "separate_peace_exit_marker_failed";
                    return false;
                }
            }
            return true;
        }

        private int SettlementTruceStartYear()
        {
            if (_proposal.ResponseYear >= 0) return _proposal.ResponseYear;
            if (_proposal.CreatedYear >= 0) return _proposal.CreatedYear;
            return 0;
        }

        public void Commit()
        {
            _committed = true;
            _rollbacks.Clear();
        }

        public void CommitTerm()
        {
            _rollbacks.Clear();
        }

        public void Rollback()
        {
            if (_committed) return;
            for (int i = _rollbacks.Count - 1; i >= 0; i--)
            {
                try { _rollbacks[i](); }
                catch (Exception error)
                {
                    ModClass.LogWarning("Peace rollback step failed: " +
                                        error.Message);
                }
            }
            _rollbacks.Clear();
        }

        public void Dispose()
        {
            if (!_committed) Rollback();
        }

        private bool TryTransferResource(WarPeaceSettlementTerm term,
            string resourceId, out string reason)
        {
            reason = "";
            City source = WarPeaceSettlementWorld.FindCity(
                term.SourceCityId);
            City target = WarPeaceSettlementWorld.FindCity(
                term.TargetCityId);
            if (source == null || target == null || term.Amount <= 0 ||
                source.getResourcesAmount(resourceId) < term.Amount)
            {
                reason = "payment_no_longer_available";
                return false;
            }
            int sourceBefore = source.getResourcesAmount(resourceId);
            int targetBefore = target.getResourcesAmount(resourceId);
            _rollbacks.Add(() =>
            {
                RestoreResourceAmount(target, resourceId, targetBefore);
                RestoreResourceAmount(source, resourceId, sourceBefore);
            });
            return WarPeaceResourceTransferService.TryTransferExact(
                source, target, resourceId, term.Amount, out reason);
        }

        private bool TryCreateReparations(WarPeaceSettlementTerm term,
            out string reason)
        {
            reason = "";
            SQLiteConnection db = DB;
            if (db == null)
            {
                reason = "lineage_archive_unavailable";
                return false;
            }
            long id = TableIdAllocator.Next(db,
                WarReparationsObligationTableItem.GetTableName(),
                "OBLIGATION_ID");
            if (!WarPeaceTermsRules.TryReparationsSchedule(SafeYear(),
                    term.DurationYears, out int start, out int end))
            {
                reason = "reparations_schedule_overflow";
                return false;
            }
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "INSERT INTO " +
                    WarReparationsObligationTableItem.GetTableName() +
                    " (OBLIGATION_ID,PROPOSAL_ID,TERM_ID,WAR_ID," +
                    "PAYER_KINGDOM_ID,RECIPIENT_KINGDOM_ID,RESOURCE_ID," +
                    "ANNUAL_AMOUNT,START_YEAR,END_YEAR,NEXT_DUE_YEAR," +
                    "TOTAL_PAID,ACTIVE) VALUES (@id,@proposal,@term," +
                    "@war,@payer,@recipient,@resource,@amount,@start," +
                    "@end,@start,0,1)";
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@proposal",
                    _proposal.ProposalId);
                command.Parameters.AddWithValue("@term", term.TermId);
                command.Parameters.AddWithValue("@war", _proposal.WarId);
                command.Parameters.AddWithValue("@payer",
                    term.FromKingdomId);
                command.Parameters.AddWithValue("@recipient",
                    term.ToKingdomId);
                command.Parameters.AddWithValue("@resource",
                    string.IsNullOrWhiteSpace(term.ResourceId)
                        ? "gold"
                        : term.ResourceId);
                command.Parameters.AddWithValue("@amount", term.Amount);
                command.Parameters.AddWithValue("@start", start);
                command.Parameters.AddWithValue("@end",
                    end);
                command.ExecuteNonQuery();
                _rollbacks.Add(() => DeleteById(
                    WarReparationsObligationTableItem.GetTableName(),
                    "OBLIGATION_ID", id));
                return true;
            }
            catch (Exception error)
            {
                reason = "reparations_insert_failed:" +
                         error.GetType().Name;
                return false;
            }
        }

        private bool TryReleaseCaptive(WarPeaceSettlementTerm term,
            out string reason)
        {
            reason = "";
            Actor actor = WarPeaceSettlementWorld.FindActor(
                term.CaptiveActorId);
            if (actor?.data == null || !SlaveService.IsSlave(actor))
            {
                reason = "captive_release_failed";
                return false;
            }
            _rollbacks.Add(() =>
            {
                if (!SlaveService.IsSlave(actor))
                    SlaveService.Enslave(actor, "peace_rollback",
                        pContextCity: actor.city,
                        pContextKingdom: actor.kingdom,
                        pForceRecord: true);
            });
            if (!SlaveService.FreeSlave(actor, "peace_release"))
            {
                reason = "captive_release_failed";
                return false;
            }
            return true;
        }

        private bool TryRenounceClaim(WarPeaceSettlementTerm term,
            out string reason)
        {
            reason = "";
            SQLiteConnection db = DB;
            if (db == null)
            {
                reason = "lineage_archive_unavailable";
                return false;
            }
            try
            {
                int consumed;
                using (var read = new SQLiteCommand(db))
                {
                    read.CommandText = "SELECT CONSUMED FROM " +
                        WarClaimTableItem.GetTableName() +
                        " WHERE CLAIM_ID=@id AND ACTIVE=1 LIMIT 1";
                    read.Parameters.AddWithValue("@id", term.ClaimId);
                    object value = read.ExecuteScalar();
                    if (value == null)
                    {
                        reason = "claim_no_longer_active";
                        return false;
                    }
                    consumed = Convert.ToInt32(value);
                }
                using (var update = new SQLiteCommand(db))
                {
                    update.CommandText = "UPDATE " +
                        WarClaimTableItem.GetTableName() +
                        " SET ACTIVE=0,CONSUMED=1 WHERE CLAIM_ID=@id " +
                        "AND ACTIVE=1";
                    update.Parameters.AddWithValue("@id", term.ClaimId);
                    if (update.ExecuteNonQuery() != 1)
                    {
                        reason = "claim_renunciation_failed";
                        return false;
                    }
                }
                _rollbacks.Add(() => RestoreClaim(term.ClaimId, consumed));
                return true;
            }
            catch (Exception error)
            {
                reason = "claim_renunciation_exception:" +
                         error.GetType().Name;
                return false;
            }
        }

        private bool TryForceSubject(WarPeaceSettlementTerm term,
            bool tributary, out string reason)
        {
            reason = "";
            Kingdom subject = WarPeaceSettlementWorld.FindKingdom(
                term.FromKingdomId);
            Kingdom suzerain = WarPeaceSettlementWorld.FindKingdom(
                term.ToKingdomId);
            if (!VassalService.TryReadActiveRelationIdentity(subject?.id ??
                    -1L, out ActiveVassalRelationIdentity previous,
                    out bool previousExists) || previous.Ambiguous)
            {
                reason = "subject_relation_snapshot_failed";
                return false;
            }
            _rollbacks.Add(() =>
            {
                if (VassalService.GetSuzerain(subject) == suzerain ||
                    VassalService.GetTributarySuzerain(subject) == suzerain)
                    VassalService.EndVassal(subject, "peace_rollback");
                if (!previousExists) return;
                Kingdom previousSuzerain = WarPeaceSettlementWorld
                    .FindKingdom(previous.SuzerainId);
                if (previousSuzerain?.data == null) return;
                VassalService.SetVassal(subject, previousSuzerain,
                    previous.RelationType, pContractTier:
                    previous.ContractTier);
            });
            bool success = tributary
                ? VassalService.SetTributary(subject, suzerain,
                    "peace_force_tributary", _proposal.WarId,
                    pEnforceWarVictory: true)
                : VassalService.SetVassal(subject, suzerain,
                    "peace_force_vassal", _proposal.WarId,
                    pEnforceWarVictory: true,
                    pContractTier: VassalContractTierRules.Inner);
            if (!success)
            {
                reason = "force_subject_failed";
                return false;
            }
            return true;
        }

        private bool TryCedeCity(WarPeaceSettlementTerm term,
            out string reason)
        {
            reason = "";
            City city = WarPeaceSettlementWorld.FindCity(term.CityId);
            Kingdom recipient = WarPeaceSettlementWorld.FindKingdom(
                term.ToKingdomId);
            Kingdom original = city?.kingdom;
            if (city?.data == null || recipient?.data == null)
            {
                reason = "city_no_longer_available";
                return false;
            }
            long ownerId = original?.id ?? -1;
            bool occupied = WarPeaceSettlementWorld.
                TryResolveFrozenOccupationRecipient(_proposal.WarId,
                    term.FromKingdomId, term.CityId, term.ToKingdomId,
                    out long controllerId);
            bool basis = WarPeaceSettlementWorld.HasCoreOrClaim(
                term.ToKingdomId, term.FromKingdomId, term.CityId);
            if (!WarPeaceSettlementValidationRules
                    .HasExecutionTerritorialBasis(term.FrozenOccupation,
                        term.CoreOrClaimBasis, occupied, basis))
            {
                reason = "no_territorial_basis";
                return false;
            }
            if (!WarPeaceSettlementValidationRules.
                    CanExecuteFrozenOccupationCede(
                        cityAvailable: true,
                        term.FrozenOccupation || occupied,
                        ownerId == term.FromKingdomId,
                        ownerId == term.ToKingdomId,
                        ownerId == controllerId, basis))
            {
                reason = "city_owner_changed_during_recovery";
                return false;
            }
            if (!PeasantRebelRouteService.CanAcquireCity(recipient, city))
            {
                reason = "bandit_single_city";
                return false;
            }
            RoyalClaimService.TreatyAnnexationMarker annexation = default;
            if (original?.data != null && original != recipient)
            {
                int remainingCities;
                try { remainingCities = original.countCities(); }
                catch { remainingCities = -1; }
                if (remainingCities == 1)
                    annexation = RoyalClaimService.PrepareTreatyAnnexation(
                        original);
            }
            try
            {
                city.joinAnotherKingdom(recipient, pCaptured: false,
                    pRebellion: false);
                if (city.kingdom != recipient)
                {
                    RoyalClaimService.RollbackTreatyAnnexation(annexation);
                    reason = "city_transfer_not_committed";
                    return false;
                }
                return true;
            }
            catch (Exception error)
            {
                bool transferCommitted = city.kingdom == recipient;
                if (!RoyalRestorationRules
                        .ShouldRollbackTreatyAnnexationMarker(
                            transferCommitted)) return true;
                RoyalClaimService.RollbackTreatyAnnexation(annexation);
                reason = "city_transfer_exception:" +
                         error.GetType().Name;
                return false;
            }
        }

        private WarWinner ResolveWinner()
        {
            long winnerId = WarPeaceSettlementOutcomeRules
                .ResolveWinnerKingdomId(_proposal.RequesterKingdomId,
                    _proposal.ResponderKingdomId, _proposal.Terms,
                    _proposal.Participants);
            if (winnerId < 0) return WarWinner.Peace;
            Kingdom winner = WarPeaceSettlementWorld.FindKingdom(winnerId);
            if (winner != null && _war.isMainAttacker(winner))
                return WarWinner.Attackers;
            if (winner != null && _war.isMainDefender(winner))
                return WarWinner.Defenders;
            return WarWinner.Peace;
        }

        private static void DeleteById(string table, string column,
            long id)
        {
            SQLiteConnection db = DB;
            if (db == null) return;
            using var command = new SQLiteCommand(db);
            command.CommandText = "DELETE FROM " + table + " WHERE " +
                                  column + "=@id";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        private static void RestoreClaim(long claimId, int consumed)
        {
            SQLiteConnection db = DB;
            if (db == null) return;
            using var command = new SQLiteCommand(db);
            command.CommandText = "UPDATE " +
                WarClaimTableItem.GetTableName() +
                " SET ACTIVE=1,CONSUMED=@consumed WHERE CLAIM_ID=@id";
            command.Parameters.AddWithValue("@consumed", consumed);
            command.Parameters.AddWithValue("@id", claimId);
            command.ExecuteNonQuery();
        }

        private static void RestoreResourceAmount(City city,
            string resourceId, int expected)
        {
            WarPeaceResourceTransferService.RestoreAmount(city,
                resourceId, expected);
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }
    }
}
