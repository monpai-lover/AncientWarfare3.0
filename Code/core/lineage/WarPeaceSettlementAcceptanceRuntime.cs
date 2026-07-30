using System;

namespace AncientWarfare3.core.lineage
{
    public sealed partial class WarPeaceSettlementService
    {
        public System.Collections.Generic.IReadOnlyList<
            WarPeaceSettlementTerm> ReadTerms(string pDetailId)
        {
            if (!WarPeaceSettlementValidationRules.TryParseDetailId(
                    pDetailId, out long proposalId) ||
                !_store.TryRead(proposalId,
                    out WarPeaceSettlementProposal proposal))
                return Array.Empty<WarPeaceSettlementTerm>();
            return new System.Collections.Generic.List<
                WarPeaceSettlementTerm>(proposal.Terms).AsReadOnly();
        }

        public WarPeaceAcceptanceResult EvaluateAi(string pDetailId,
            int pRecipientResolve, bool pSurrenderOffer = false)
        {
            if (!WarPeaceSettlementValidationRules.TryParseDetailId(
                    pDetailId, out long proposalId) ||
                !_store.TryRead(proposalId,
                    out WarPeaceSettlementProposal proposal))
                return new WarPeaceAcceptanceResult(false, false,
                    int.MinValue);
            War war = WarPeaceSettlementWorld.FindWar(proposal.WarId);
            Kingdom requester = WarPeaceSettlementWorld.FindKingdom(
                proposal.RequesterKingdomId);
            Kingdom recipient = WarPeaceSettlementWorld.FindKingdom(
                proposal.ResponderKingdomId);
            if (war?.data == null || requester?.data == null ||
                recipient?.data == null ||
                !WarScoreService.TryGetSnapshot(war, recipient,
                    out WarScoreSnapshot score))
                return new WarPeaceAcceptanceResult(false, false,
                    int.MinValue);

            int netValue = 0;
            var ledger = new WarPeaceOfferLedger();
            for (int i = 0; i < proposal.Terms.Count; i++)
            {
                WarPeaceSettlementTerm term = proposal.Terms[i];
                if (term.Kind == WarPeaceTermKind.WhitePeace) continue;
                if (!WarPeaceSettlementValidationRules.
                        TryResolveRecipientSide(
                        proposal.RequesterKingdomId,
                        proposal.ResponderKingdomId,
                        proposal.Participants, term.ToKingdomId,
                        out bool recipientOnRequesterSide) ||
                    !WarPeaceSettlementValidationRules.
                        TryAddForSettlementRecipient(ledger,
                            proposal.RequesterKingdomId,
                            proposal.ResponderKingdomId,
                            proposal.Participants, term.ToKingdomId,
                            term.Cost, out _))
                    return new WarPeaceAcceptanceResult(false, false,
                        int.MinValue);
                netValue += recipientOnRequesterSide
                    ? -term.Cost
                    : term.Cost;
            }
            bool completeSurrender = pSurrenderOffer &&
                IsCompleteSurrender(proposal, war, requester, recipient,
                    score.Score, ledger);
            int exhaustion = war.isAttacker(recipient)
                ? score.AttackerExhaustion
                : score.DefenderExhaustion;
            var facts = new WarPeaceAcceptanceFacts(score.Score,
                Math.Max(-100, Math.Min(100, netValue)),
                Math.Max(0, Math.Min(100, pRecipientResolve)),
                Math.Max(0, Math.Min(100, exhaustion)),
                Math.Max(0, -score.Score), completeSurrender);
            return EvaluateAi(proposalId, facts);
        }

        private bool IsCompleteSurrender(
            WarPeaceSettlementProposal pProposal, War pWar,
            Kingdom pRequester, Kingdom pRecipient, int pRecipientScore,
            WarPeaceOfferLedger pOfferedLedger)
        {
            WarPeaceSettlementDraft maximumDraft = BuildDefaultDraft(pWar,
                pRequester, pRecipient, -pRecipientScore,
                WarPeaceDefaultOfferMode.Surrender);
            for (int i = 0; i < pProposal.Participants.Count; i++)
                maximumDraft.Participants.Add(
                    pProposal.Participants[i].Clone());
            if (!WarPeaceSettlementValidationRules.TryMaterialize(
                    maximumDraft, _world,
                    out System.Collections.Generic.IReadOnlyList<
                        WarPeaceSettlementTerm> maximumTerms, out _))
                return false;

            var maximumLedger = new WarPeaceOfferLedger();
            for (int i = 0; i < maximumTerms.Count; i++)
            {
                WarPeaceSettlementTerm term = maximumTerms[i];
                if (term.Kind == WarPeaceTermKind.WhitePeace) continue;
                if (!WarPeaceSettlementValidationRules.
                        TryAddForSettlementRecipient(maximumLedger,
                            pProposal.RequesterKingdomId,
                            pProposal.ResponderKingdomId,
                            pProposal.Participants, term.ToKingdomId,
                            term.Cost, out _))
                    return false;
            }
            return WarPeaceDefaultOfferRules.IsCompleteSurrenderOffer(
                pRecipientScore, pOfferedLedger.DemandGross,
                pOfferedLedger.ConcessionGross,
                maximumLedger.ConcessionGross);
        }
    }
}
