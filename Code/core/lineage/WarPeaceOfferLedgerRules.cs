namespace AncientWarfare3.core.lineage
{
    public sealed class WarPeaceOfferLedger
    {
        public const int MaximumGross = 100;

        public int DemandGross { get; private set; }
        public int ConcessionGross { get; private set; }
        public int NetDemand { get { return DemandGross - ConcessionGross; } }
        public int DemandRemaining { get { return MaximumGross - DemandGross; } }
        public int ConcessionRemaining
        {
            get { return MaximumGross - ConcessionGross; }
        }

        public bool TryAddDemand(int cost, out string reason)
        {
            return TryAdd(cost, true, out reason);
        }

        public bool TryAddConcession(int cost, out string reason)
        {
            return TryAdd(cost, false, out reason);
        }

        public bool TryAddForRecipient(long requesterKingdomId,
            long responderKingdomId, long toKingdomId, int cost,
            out string reason)
        {
            if (cost == 0)
            {
                reason = string.Empty;
                return true;
            }
            if (toKingdomId == requesterKingdomId)
                return TryAddDemand(cost, out reason);
            if (toKingdomId == responderKingdomId)
                return TryAddConcession(cost, out reason);
            reason = "invalid_term_participants";
            return false;
        }

        private bool TryAdd(int cost, bool demand, out string reason)
        {
            reason = string.Empty;
            if (cost < 0)
            {
                reason = "invalid_term_cost";
                return false;
            }
            long next = (long)(demand ? DemandGross : ConcessionGross) +
                        cost;
            if (next > MaximumGross)
            {
                reason = demand
                    ? "demand_gross_exceeds_cap"
                    : "concession_gross_exceeds_cap";
                return false;
            }
            if (demand) DemandGross = (int)next;
            else ConcessionGross = (int)next;
            return true;
        }
    }
}
