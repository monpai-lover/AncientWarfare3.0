using AncientWarfare3.core.historyapi;

namespace AncientWarfare3.api.history
{
    public static class AW3DiplomacyHistoryApi
    {
        public static AW3HistoryPage<AW3DiplomacyEvent> GetEvents(
            long kingdomId, AW3HistoryQuery query = null)
        {
            return AW3HistoryReadService.ReadDiplomacy(kingdomId, null, query);
        }

        public static AW3HistoryPage<AW3DiplomacyEvent> GetEventsBetween(
            long firstId, long secondId, AW3HistoryQuery query = null)
        {
            return AW3HistoryReadService.ReadDiplomacy(firstId, secondId, query);
        }
    }
}
