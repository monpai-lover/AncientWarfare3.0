using AncientWarfare3.core.court;
using AncientWarfare3.core.historyapi;

namespace AncientWarfare3.api.history
{
    public static class AW3OfficialCareerApi
    {
        public static AW3HistoryPage<AW3OfficialCareerEntry> GetHistory(
            long actorId, AW3HistoryQuery query = null)
        {
            return AW3HistoryReadService.ReadCareer(actorId,
                default(OfficialCareerHistoryScope), query);
        }

        public static AW3HistoryPage<AW3OfficialCareerEntry> GetOfficeHistory(
            long kingdomId, string layer, string officeId,
            AW3HistoryQuery query = null)
        {
            long cityId = query?.CityId ?? -1L;
            long countyId = query?.CountyId ?? -1L;
            var scope = new OfficialCareerHistoryScope(kingdomId, cityId,
                layer, officeId, countyId);
            return AW3HistoryReadService.ReadCareer(null, scope, query);
        }
    }
}
