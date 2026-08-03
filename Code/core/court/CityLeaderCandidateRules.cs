using System;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// Pure guards for the native city-leader candidate boundary.  The
    /// vanilla city/unit collections can briefly retain an actor after it has
    /// moved, died, or been removed from the world actor index.
    /// </summary>
    public static class CityLeaderCandidateRules
    {
        public static bool CanUseCandidate(bool actorHasData,
            bool actorAlive, bool actorRekt, bool actorKingdomMatches,
            bool actorCityMatches, bool sourceCityValid,
            bool sourceCityKingdomMatches, bool isKing, bool isCityLeader,
            bool isLeaderProfession)
        {
            return actorHasData && actorAlive && !actorRekt &&
                   actorKingdomMatches && actorCityMatches &&
                   sourceCityValid && sourceCityKingdomMatches &&
                   !isKing && !isCityLeader && !isLeaderProfession;
        }

        public static bool ShouldRetry(long pCurrentDay, long pFailedDay,
            int pRetryDays)
        {
            if (pFailedDay < 0L) return true;
            long retryDays = Math.Max(1, pRetryDays);
            return pCurrentDay >= pFailedDay &&
                   pCurrentDay - pFailedDay >= retryDays;
        }
    }
}
