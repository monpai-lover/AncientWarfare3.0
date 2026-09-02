using System.Collections.Generic;
using AncientWarfare3.core.historyapi;

namespace AncientWarfare3.api.history
{
    public static class AW3ChronicleApi
    {
        public static AW3HistoryPage<AW3ChronicleEntry> GetKingdomEvents(
            long kingdomId, AW3HistoryQuery query = null)
        {
            return AW3HistoryReadService.ReadKingdomEvents(kingdomId, query);
        }

        public static AW3HistoryPage<AW3ChronicleEntry> GetCityEvents(
            long cityId, AW3HistoryQuery query = null)
        {
            return AW3HistoryReadService.ReadCityEvents(cityId, query);
        }

        public static IReadOnlyList<AW3Reign> GetReigns(long kingdomId)
        {
            return AW3HistoryReadService.ReadReigns(kingdomId);
        }

        public static IReadOnlyList<AW3CityPeriod> GetCityPeriods(long cityId)
        {
            return AW3HistoryReadService.ReadCityPeriods(cityId);
        }
    }
}
