using AncientWarfare3.core.historyapi;

namespace AncientWarfare3.api.history
{
    public static class AW3BiographyApi
    {
        public static AW3HistoryPage<AW3BiographyEntry> GetEntries(
            long actorId, AW3HistoryQuery query = null)
        {
            return AW3HistoryReadService.ReadBiography(actorId, query);
        }
    }
}
