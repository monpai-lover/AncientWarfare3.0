using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class WarRefugeeJourneyService
    {
        // The journey owns the destination reservation for its active household;
        // release it only on arrival, cancellation, or destination invalidation.
        internal sealed class JourneyInput
        {
            public long JourneyId { get; set; }
            public int HouseholdBudget { get; set; }
            public int Distance { get; set; }
            public bool CrossSea { get; set; }
            public bool Reachable { get; set; }
            public int RouteRetries { get; set; }
            public int DepartureMonth { get; set; }
        }

        internal sealed class JourneyProgress
        {
            public long JourneyId;
            public bool UseAbstract;
            public int ArrivalMonth;
        }

        internal static IReadOnlyList<JourneyProgress> ProcessMonthly(
            IEnumerable<JourneyInput> pJourneys)
        {
            var result = new List<JourneyProgress>();
            if (pJourneys == null) return result;
            int processed = 0;
            foreach (JourneyInput journey in pJourneys)
            {
                if (journey == null || journey.JourneyId < 0L) continue;
                if (journey.HouseholdBudget <= 0) break;
                journey.HouseholdBudget--;
                bool abstractJourney = WarRefugeeRules.ShouldUseAbstractJourney(
                    journey.CrossSea, journey.Reachable, journey.RouteRetries);
                result.Add(new JourneyProgress
                {
                    JourneyId = journey.JourneyId,
                    UseAbstract = abstractJourney,
                    ArrivalMonth = abstractJourney
                        ? WarRefugeeRules.AbstractArrivalMonth(
                            journey.DepartureMonth, journey.Distance)
                        : -1
                });
                if (++processed >= 256) break;
            }
            return result;
        }

        internal static bool CanReserveDestination(
            WarRefugeeDestinationFacts pDestination, int pBatchSize)
        {
            return WarRefugeeRules.CanReceive(pDestination, pBatchSize);
        }

        internal static bool ShouldUseAbstractJourney(bool pCrossSea,
            bool pReachable, int pRetries)
        {
            return WarRefugeeRules.ShouldUseAbstractJourney(pCrossSea,
                pReachable, pRetries);
        }
    }
}
