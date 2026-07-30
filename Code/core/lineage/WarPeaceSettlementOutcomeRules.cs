using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceSettlementOutcomeRules
    {
        public static long ResolveWinnerKingdomId(long requesterKingdomId,
            long responderKingdomId,
            IReadOnlyList<WarPeaceSettlementTerm> terms)
        {
            return ResolveWinnerKingdomId(requesterKingdomId,
                responderKingdomId, terms, null);
        }

        public static long ResolveWinnerKingdomId(long requesterKingdomId,
            long responderKingdomId,
            IReadOnlyList<WarPeaceSettlementTerm> terms,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> participants)
        {
            if (requesterKingdomId < 0 || responderKingdomId < 0 ||
                requesterKingdomId == responderKingdomId || terms == null)
                return -1L;

            long netValueForResponder = 0L;
            for (int i = 0; i < terms.Count; i++)
            {
                WarPeaceSettlementTerm term = terms[i];
                if (term == null || term.Kind == WarPeaceTermKind.WhitePeace ||
                    term.Cost <= 0) continue;
                if (WarPeaceSettlementValidationRules.IsWaivedResourceTerm(
                        term.Kind, term.ApplyReason)) continue;
                if (!WarPeaceSettlementValidationRules.
                        TryResolveRecipientSide(requesterKingdomId,
                            responderKingdomId, participants,
                            term.ToKingdomId,
                            out bool recipientOnRequesterSide)) continue;
                netValueForResponder += recipientOnRequesterSide
                    ? -term.Cost
                    : term.Cost;
            }

            if (netValueForResponder > 0L) return responderKingdomId;
            if (netValueForResponder < 0L) return requesterKingdomId;
            return -1L;
        }
    }
}
