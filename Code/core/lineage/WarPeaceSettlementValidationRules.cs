using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceSettlementValidationRules
    {
        public const int MaximumTerms = 16;
        public const string DetailPrefix = "war_peace_settlement:";
        public const string WaivedResourceReason =
            "waived_resource_unavailable";

        public static string DetailId(long proposalId)
        {
            return proposalId < 0 ? "" : DetailPrefix + proposalId;
        }

        public static bool TryParseDetailId(string detailId,
            out long proposalId)
        {
            proposalId = -1;
            if (string.IsNullOrEmpty(detailId) ||
                !detailId.StartsWith(DetailPrefix,
                    StringComparison.Ordinal)) return false;
            return long.TryParse(detailId.Substring(DetailPrefix.Length),
                       out proposalId) && proposalId >= 0;
        }

        public static bool TryMaterialize(WarPeaceSettlementDraft draft,
            IWarPeaceSettlementWorld world,
            out IReadOnlyList<WarPeaceSettlementTerm> terms,
            out string reason)
        {
            var result = new List<WarPeaceSettlementTerm>();
            terms = result;
            reason = "";
            if (draft == null || world == null || draft.WarId < 0 ||
                draft.RequesterKingdomId < 0 ||
                draft.ResponderKingdomId < 0 ||
                draft.RequesterKingdomId == draft.ResponderKingdomId)
            {
                reason = "invalid_settlement_participants";
                return false;
            }
            if (draft.Terms.Count == 0 ||
                draft.Terms.Count > MaximumTerms)
            {
                reason = "invalid_term_count";
                return false;
            }
            if (draft.Terms.Count > 1 &&
                ContainsKind(draft.Terms, WarPeaceTermKind.WhitePeace))
            {
                reason = "white_peace_must_stand_alone";
                return false;
            }
            if (CountSubjectTerms(draft.Terms) > 1)
            {
                reason = "conflicting_subject_terms";
                return false;
            }

            var cities = new HashSet<long>();
            var captives = new HashSet<long>();
            var claims = new HashSet<long>();
            var ledger = new WarPeaceOfferLedger();
            var survival = new WarPeaceTreatySurvivalLedger();
            for (int i = 0; i < draft.Terms.Count; i++)
            {
                WarPeaceSettlementTermDraft term = draft.Terms[i];
                if (term == null || !TryValidateShape(draft, term,
                        cities, captives, claims, out reason)) return false;
                if (!world.TryInspect(draft, term, out var facts,
                        out reason)) return false;
                if (term.Kind == WarPeaceTermKind.CedeCity &&
                    !IsNegotiator(draft.RequesterKingdomId,
                        draft.ResponderKingdomId, term.ToKingdomId) &&
                    facts?.OccupiedByDemandingSide != true)
                {
                    reason = "invalid_term_participants";
                    return false;
                }

                int cost;
                if (term.Kind == WarPeaceTermKind.CedeCity)
                {
                    int liveCost = WarPeaceTermsRules.CityCessionCost(
                        facts?.CityValue ?? default);
                    cost = WarGoalSettlementRules.ResolveCedeCityCost(
                        !draft.PlayerInitiated && term.WarGoalId >= 0,
                        term.RequestedCost, liveCost);
                    if (facts?.OccupiedByDemandingSide != true &&
                        facts?.HasCoreOrClaim != true)
                    {
                        reason = "no_territorial_basis";
                        return false;
                    }
                }
                else
                {
                    cost = WarPeaceTermsRules.CanonicalTermCost(term.Kind,
                        term.Amount, term.DurationYears,
                        facts?.CityValue ?? default);
                }
                if (!TryAddForSettlementRecipient(ledger,
                        draft.RequesterKingdomId,
                        draft.ResponderKingdomId, draft.Participants,
                        term.ToKingdomId, cost, out reason)) return false;
                survival.Observe(term.Kind, term.FromKingdomId,
                    facts?.SourceKingdomCityCount ?? -1);
                result.Add(WarPeaceSettlementTerm.FromDraft(term, facts,
                    cost, i));
            }
            return survival.Validate(out reason);
        }

        public static bool ValidatePersisted(
            WarPeaceSettlementProposal proposal, out string reason)
        {
            reason = "";
            if (proposal == null || proposal.ProposalId < 0 ||
                proposal.WarId < 0 || proposal.Terms.Count == 0 ||
                proposal.Terms.Count > MaximumTerms)
            {
                reason = "invalid_persisted_settlement";
                return false;
            }
            var ledger = new WarPeaceOfferLedger();
            for (int i = 0; i < proposal.Terms.Count; i++)
            {
                WarPeaceSettlementTerm term = proposal.Terms[i];
                if (term == null || term.TermId < 0 ||
                    term.Position != i || term.Cost < 0 ||
                    !Enum.IsDefined(typeof(
                        WarPeaceSettlementTermApplyStatus),
                        term.ApplyStatus) ||
                    term.ApplyStatus !=
                        WarPeaceSettlementTermApplyStatus.Pending &&
                    !term.BaselineCaptured)
                {
                    reason = "invalid_persisted_term";
                    return false;
                }
                if (term.Kind != WarPeaceTermKind.WhitePeace &&
                    (!IsAssetOwnerAllowed(term.Kind, proposal.Scope,
                         proposal.RequesterKingdomId,
                         proposal.ResponderKingdomId,
                         proposal.Participants, term.FromKingdomId,
                         term.ToKingdomId) ||
                      !IsTermRecipientAllowed(term.Kind, proposal.Scope,
                           proposal.RequesterKingdomId,
                           proposal.ResponderKingdomId,
                           proposal.Participants, term.ToKingdomId) ||
                      term.Kind == WarPeaceTermKind.CedeCity &&
                      !IsNegotiator(proposal.RequesterKingdomId,
                          proposal.ResponderKingdomId,
                          term.ToKingdomId) &&
                      !term.FrozenOccupation))
                {
                    reason = "invalid_persisted_term_participants";
                    return false;
                }
                if ((proposal.Status ==
                         WarPeaceSettlementStatus.TermsApplied ||
                     proposal.Status ==
                         WarPeaceSettlementStatus.Executed) &&
                    term.ApplyStatus !=
                        WarPeaceSettlementTermApplyStatus.Applied)
                {
                    reason = "incomplete_persisted_term_state";
                    return false;
                }
                if (!TryAddForSettlementRecipient(ledger,
                        proposal.RequesterKingdomId,
                        proposal.ResponderKingdomId, proposal.Participants,
                        term.ToKingdomId, term.Cost, out reason)) return false;
            }
            if (ledger.DemandGross != proposal.TotalCost)
            {
                reason = "invalid_persisted_budget";
                return false;
            }
            return true;
        }

        public static WarPeaceTermApplicationState ClassifyResourceTransfer(
            int sourceBefore, int targetBefore, int amount,
            int sourceNow, int targetNow)
        {
            if (sourceBefore < 0 || targetBefore < 0 || amount <= 0 ||
                sourceNow < 0 || targetNow < 0 || sourceBefore < amount)
                return WarPeaceTermApplicationState.Ambiguous;
            if (sourceNow == sourceBefore && targetNow == targetBefore)
                return WarPeaceTermApplicationState.NotApplied;
            long sourceAfter = (long)sourceBefore - amount;
            long targetAfter = (long)targetBefore + amount;
            if (sourceNow == sourceAfter && targetNow == targetAfter)
                return WarPeaceTermApplicationState.Applied;
            return WarPeaceTermApplicationState.Ambiguous;
        }

        public static bool CanExecutePendingCede(long currentOwnerId,
            long fromKingdomId)
        {
            return currentOwnerId >= 0 &&
                   currentOwnerId == fromKingdomId;
        }

        public static bool CanExecuteFrozenOccupationCede(
            bool cityAvailable, bool frozenOccupationForRecipient,
            bool currentOwnerMatchesPayer,
            bool currentOwnerMatchesRecipient, bool coreOrClaimBasis)
        {
            return CanExecuteFrozenOccupationCede(cityAvailable,
                frozenOccupationForRecipient, currentOwnerMatchesPayer,
                currentOwnerMatchesRecipient,
                currentOwnerMatchesController: false, coreOrClaimBasis);
        }

        public static bool CanExecuteFrozenOccupationCede(
            bool cityAvailable, bool frozenOccupationForRecipient,
            bool currentOwnerMatchesPayer,
            bool currentOwnerMatchesRecipient,
            bool currentOwnerMatchesController, bool coreOrClaimBasis)
        {
            if (!cityAvailable) return false;
            if (currentOwnerMatchesPayer)
                return frozenOccupationForRecipient || coreOrClaimBasis;
            return frozenOccupationForRecipient &&
                   (currentOwnerMatchesRecipient ||
                    currentOwnerMatchesController);
        }

        public static bool CanReceiveFrozenOccupation(
            bool recipientIsController, bool recipientIsWarLeader,
            bool recipientOnControllerSide)
        {
            return recipientOnControllerSide &&
                   (recipientIsController || recipientIsWarLeader);
        }

        public static bool CanOfferWarGoalCityCandidate(
            bool cityAvailable, bool ownerMatchesPayer,
            bool frozenOccupation, bool coreOrClaimBasis)
        {
            return cityAvailable &&
                   (ownerMatchesPayer || frozenOccupation) &&
                   (frozenOccupation || coreOrClaimBasis);
        }

        public static bool CanUseFrozenOccupationCandidate(
            bool cityAvailable, bool homeMatchesPayer,
            bool controllerOnBeneficiarySide,
            bool currentOwnerMatchesPayer,
            bool currentOwnerOnBeneficiarySide)
        {
            return cityAvailable && homeMatchesPayer &&
                   controllerOnBeneficiarySide &&
                   (currentOwnerMatchesPayer ||
                    currentOwnerOnBeneficiarySide);
        }

        public static bool HasExecutionTerritorialBasis(
            bool capturedFrozenOccupation, bool capturedCoreOrClaim,
            bool liveFrozenOccupation, bool liveCoreOrClaim)
        {
            return capturedFrozenOccupation || capturedCoreOrClaim ||
                   liveFrozenOccupation || liveCoreOrClaim;
        }

        public static bool ShouldWaiveUnavailableResourceTerm(
            WarPeaceTermKind pKind, string pReason)
        {
            if (pKind != WarPeaceTermKind.GoldPayment &&
                pKind != WarPeaceTermKind.MaterialPayment) return false;
            return pReason == "insufficient_payment_stock" ||
                   pReason == "payment_no_longer_available" ||
                   pReason == "recipient_storage_full" ||
                   pReason == "payment_baseline_unavailable" ||
                   pReason == "payment_debit_failed" ||
                   pReason == "invalid_resource_payment";
        }

        public static bool IsWaivedResourceTerm(
            WarPeaceTermKind pKind, string pApplyReason)
        {
            return (pKind == WarPeaceTermKind.GoldPayment ||
                    pKind == WarPeaceTermKind.MaterialPayment) &&
                   string.Equals(pApplyReason, WaivedResourceReason,
                       StringComparison.Ordinal);
        }

        public static bool HasIndependentSettlementTerm(
            IReadOnlyList<WarPeaceSettlementTerm> pTerms)
        {
            if (pTerms == null) return false;
            for (int i = 0; i < pTerms.Count; i++)
            {
                WarPeaceSettlementTerm term = pTerms[i];
                if (term == null || term.Kind ==
                        WarPeaceTermKind.GoldPayment ||
                    term?.Kind == WarPeaceTermKind.MaterialPayment)
                    continue;
                return true;
            }
            return false;
        }

        private static bool TryValidateShape(WarPeaceSettlementDraft draft,
            WarPeaceSettlementTermDraft term, HashSet<long> cities,
            HashSet<long> captives, HashSet<long> claims,
            out string reason)
        {
            reason = "";
            if (term.Kind != WarPeaceTermKind.WhitePeace &&
                (term.FromKingdomId < 0 || term.ToKingdomId < 0 ||
                 term.FromKingdomId == term.ToKingdomId ||
                 !IsAssetOwnerAllowed(term.Kind, draft.Scope,
                     draft.RequesterKingdomId,
                     draft.ResponderKingdomId, draft.Participants,
                     term.FromKingdomId, term.ToKingdomId) ||
                  !IsTermRecipientAllowed(term.Kind, draft.Scope,
                      draft.RequesterKingdomId,
                      draft.ResponderKingdomId, draft.Participants,
                      term.ToKingdomId)))
            {
                reason = "invalid_term_participants";
                return false;
            }
            switch (term.Kind)
            {
                case WarPeaceTermKind.WhitePeace:
                    return true;
                case WarPeaceTermKind.GoldPayment:
                    if (term.Amount >
                        WarPeaceTermsRules.MaximumImmediatePaymentAmount)
                    {
                        reason = "payment_amount_exceeds_limit";
                        return false;
                    }
                    if (term.Amount > 0) return true;
                    reason = "invalid_payment_amount";
                    return false;
                case WarPeaceTermKind.MaterialPayment:
                    if (term.Amount >
                        WarPeaceTermsRules.MaximumImmediatePaymentAmount)
                    {
                        reason = "payment_amount_exceeds_limit";
                        return false;
                    }
                    if (term.Amount > 0 &&
                        !string.IsNullOrWhiteSpace(term.ResourceId))
                        return true;
                    reason = "invalid_material_payment";
                    return false;
                case WarPeaceTermKind.Reparations:
                    if (term.Amount >
                            WarPeaceTermsRules
                                .MaximumReparationsAnnualAmount ||
                        term.DurationYears >
                            WarPeaceTermsRules
                                .MaximumReparationsDurationYears)
                    {
                        reason = "reparations_exceed_limit";
                        return false;
                    }
                    if (term.Amount > 0 && term.DurationYears > 0)
                        return true;
                    reason = "invalid_reparations";
                    return false;
                case WarPeaceTermKind.ReleaseCaptives:
                    if (term.CaptiveActorId >= 0 &&
                        captives.Add(term.CaptiveActorId)) return true;
                    reason = "invalid_or_duplicate_captive";
                    return false;
                case WarPeaceTermKind.RenounceClaims:
                    if (term.ClaimId >= 0 && claims.Add(term.ClaimId))
                        return true;
                    reason = "invalid_or_duplicate_claim";
                    return false;
                case WarPeaceTermKind.CedeCity:
                    if (term.CityId >= 0 && cities.Add(term.CityId))
                        return true;
                    reason = "invalid_or_duplicate_city";
                    return false;
                case WarPeaceTermKind.ForceTributary:
                case WarPeaceTermKind.ForceVassal:
                    return true;
                case WarPeaceTermKind.TakeMandate:
                case WarPeaceTermKind.Independence:
                case WarPeaceTermKind.ReunifySuccession:
                case WarPeaceTermKind.NoCbOutcome:
                    if (!draft.PlayerInitiated && term.WarGoalId >= 0 &&
                        term.CityId >= 0) return true;
                    reason = "invalid_automatic_goal_term";
                    return false;
                case WarPeaceTermKind.RestoreKingdom:
                    if (!draft.PlayerInitiated && term.WarGoalId >= 0 &&
                        term.CityId >= 0 && term.ClaimId >= 0) return true;
                    reason = "invalid_restoration_goal_term";
                    return false;
                default:
                    reason = "unsupported_peace_term";
                    return false;
            }
        }

        private static bool IsNegotiator(long requesterKingdomId,
            long responderKingdomId, long kingdomId)
        {
            return kingdomId == requesterKingdomId ||
                   kingdomId == responderKingdomId;
        }

        public static bool TryResolveRecipientSide(
            long pRequesterKingdomId, long pResponderKingdomId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> pParticipants,
            long pRecipientKingdomId, out bool pRecipientOnRequesterSide)
        {
            pRecipientOnRequesterSide = false;
            if (pRecipientKingdomId == pRequesterKingdomId)
            {
                pRecipientOnRequesterSide = true;
                return true;
            }
            if (pRecipientKingdomId == pResponderKingdomId) return true;
            WarPeaceSettlementParticipantSnapshot requester =
                FindParticipant(pParticipants, pRequesterKingdomId);
            WarPeaceSettlementParticipantSnapshot responder =
                FindParticipant(pParticipants, pResponderKingdomId);
            WarPeaceSettlementParticipantSnapshot recipient =
                FindParticipant(pParticipants, pRecipientKingdomId);
            if (requester == null || responder == null || recipient == null ||
                string.IsNullOrEmpty(requester.SideKind) ||
                string.IsNullOrEmpty(responder.SideKind) ||
                string.IsNullOrEmpty(recipient.SideKind) ||
                string.Equals(requester.SideKind, responder.SideKind,
                    StringComparison.Ordinal)) return false;
            if (string.Equals(recipient.SideKind, requester.SideKind,
                    StringComparison.Ordinal))
            {
                pRecipientOnRequesterSide = true;
                return true;
            }
            return string.Equals(recipient.SideKind, responder.SideKind,
                StringComparison.Ordinal);
        }

        public static bool TryAddForSettlementRecipient(
            WarPeaceOfferLedger pLedger, long pRequesterKingdomId,
            long pResponderKingdomId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> pParticipants,
            long pRecipientKingdomId, int pCost, out string pReason)
        {
            if (pLedger == null)
            {
                pReason = "invalid_term_participants";
                return false;
            }
            if (pCost == 0)
            {
                pReason = string.Empty;
                return true;
            }
            if (!TryResolveRecipientSide(
                    pRequesterKingdomId, pResponderKingdomId, pParticipants,
                    pRecipientKingdomId, out bool requesterSide))
            {
                pReason = "invalid_term_participants";
                return false;
            }
            return requesterSide
                ? pLedger.TryAddDemand(pCost, out pReason)
                : pLedger.TryAddConcession(pCost, out pReason);
        }

        private static bool IsTermRecipientAllowed(
            WarPeaceTermKind pTermKind, WarPeaceSettlementScopeKind pScope,
            long pRequesterKingdomId, long pResponderKingdomId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> pParticipants,
            long pRecipientKingdomId)
        {
            if (IsNegotiator(pRequesterKingdomId, pResponderKingdomId,
                    pRecipientKingdomId)) return true;
            return pScope == WarPeaceSettlementScopeKind.Coalition &&
                   pTermKind == WarPeaceTermKind.CedeCity &&
                   TryResolveRecipientSide(pRequesterKingdomId,
                       pResponderKingdomId, pParticipants,
                       pRecipientKingdomId, out _);
        }

        private static bool IsAssetOwnerAllowed(
            WarPeaceTermKind termKind,
            WarPeaceSettlementScopeKind scope, long requesterKingdomId,
            long responderKingdomId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> participants,
            long ownerKingdomId, long recipientKingdomId)
        {
            if (participants == null || participants.Count == 0)
                return scope == WarPeaceSettlementScopeKind.Coalition &&
                       IsNegotiator(requesterKingdomId,
                           responderKingdomId, ownerKingdomId) &&
                       IsNegotiator(requesterKingdomId,
                           responderKingdomId, recipientKingdomId);
            WarPeaceSettlementParticipantSnapshot owner =
                FindParticipant(participants, ownerKingdomId);
            if (owner == null) return false;
            if (scope == WarPeaceSettlementScopeKind.SeparateParticipant &&
                !owner.IncludedInExitGroup &&
                (!IsNegotiator(requesterKingdomId, responderKingdomId,
                     ownerKingdomId) ||
                 termKind != WarPeaceTermKind.GoldPayment &&
                 termKind != WarPeaceTermKind.MaterialPayment)) return false;

            WarPeaceSettlementParticipantSnapshot recipient =
                FindParticipant(participants, recipientKingdomId);
            return recipient != null &&
                   !string.IsNullOrEmpty(owner.SideKind) &&
                   !string.IsNullOrEmpty(recipient.SideKind) &&
                   !string.Equals(owner.SideKind, recipient.SideKind,
                       StringComparison.Ordinal);
        }

        private static WarPeaceSettlementParticipantSnapshot FindParticipant(
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> participants,
            long kingdomId)
        {
            if (participants == null) return null;
            for (int i = 0; i < participants.Count; i++)
            {
                WarPeaceSettlementParticipantSnapshot participant =
                    participants[i];
                if (participant?.KingdomId == kingdomId) return participant;
            }
            return null;
        }

        private static bool ContainsKind(
            IReadOnlyList<WarPeaceSettlementTermDraft> terms,
            WarPeaceTermKind kind)
        {
            for (int i = 0; i < terms.Count; i++)
                if (terms[i]?.Kind == kind) return true;
            return false;
        }

        private static int CountSubjectTerms(
            IReadOnlyList<WarPeaceSettlementTermDraft> terms)
        {
            int count = 0;
            for (int i = 0; i < terms.Count; i++)
                if (terms[i]?.Kind == WarPeaceTermKind.ForceVassal ||
                    terms[i]?.Kind == WarPeaceTermKind.ForceTributary)
                    count++;
            return count;
        }
    }
}
