using System;

namespace AncientWarfare3.core.lineage
{
    public static class SuccessionCapitalVictoryRules
    {
        public static long ResolveWinner(long pOriginalKingdomId,
            long pRivalKingdomId, long pOriginalCapitalCityId,
            long pRivalCapitalCityId, Func<long, long> pControllerByCity,
            long pFallbackWinnerKingdomId)
        {
            if (pControllerByCity == null) return pFallbackWinnerKingdomId;
            long rivalController = ReadPositiveController(
                pRivalCapitalCityId, pControllerByCity);
            if (rivalController == pOriginalKingdomId)
                return pOriginalKingdomId;
            long originalController = ReadPositiveController(
                pOriginalCapitalCityId, pControllerByCity);
            if (originalController == pRivalKingdomId)
                return pRivalKingdomId;
            return pFallbackWinnerKingdomId;
        }

        private static long ReadPositiveController(long pCityId,
            Func<long, long> pControllerByCity)
        {
            if (pCityId < 0) return -1L;
            try
            {
                long controller = pControllerByCity(pCityId);
                return controller > 0 ? controller : -1L;
            }
            catch
            {
                return -1L;
            }
        }
    }
}
