using System;

namespace AncientWarfare3.core.court
{
    public readonly struct OfficialCareerHistoryScope
    {
        public OfficialCareerHistoryScope(long kingdomId, long cityId,
            string layer, string officeId, long countyId = -1L)
        {
            KingdomId = kingdomId;
            CityId = cityId;
            CountyId = countyId;
            Layer = layer ?? "";
            OfficeId = officeId ?? "";
        }

        public long KingdomId { get; }
        public long CityId { get; }
        public long CountyId { get; }
        public string Layer { get; }
        public string OfficeId { get; }
        public bool HasCity => CityId >= 0L;
        public bool HasCounty => CountyId >= 0L;
        public bool IsValid => KingdomId >= 0L &&
                               !string.IsNullOrWhiteSpace(Layer) &&
                               !string.IsNullOrWhiteSpace(OfficeId);
    }

    public sealed class OfficialCareerHistoryRow
    {
        public OfficialCareerHistoryRow(long kingdomId, long officerId,
            long actorId, long cityId, string layer, string officeId,
            string actorName, int startYear, int endYear, bool isCurrent,
            string endReason, double appointedTime = -1d,
            string kingdomName = "", string cityName = "",
            string rankId = "", int grade = -1, long countyId = -1L)
        {
            KingdomId = kingdomId;
            OfficerId = officerId;
            ActorId = actorId;
            CityId = cityId;
            CountyId = countyId;
            Layer = layer ?? "";
            OfficeId = officeId ?? "";
            ActorName = actorName ?? "";
            StartYear = startYear;
            EndYear = endYear;
            IsCurrent = isCurrent;
            EndReason = endReason ?? "";
            AppointedTime = appointedTime;
            KingdomName = kingdomName ?? "";
            CityName = cityName ?? "";
            RankId = rankId ?? "";
            Grade = grade;
        }

        public long KingdomId { get; }
        public long OfficerId { get; }
        public long ActorId { get; }
        public long CityId { get; }
        public long CountyId { get; }
        public string Layer { get; }
        public string OfficeId { get; }
        public string ActorName { get; }
        public int StartYear { get; }
        public int EndYear { get; }
        public bool IsCurrent { get; }
        public string EndReason { get; }
        public double AppointedTime { get; }
        public string KingdomName { get; }
        public string CityName { get; }
        public string RankId { get; }
        public int Grade { get; }

        public OfficialCareerHistoryRow WithEnd(int pEndYear,
            string pEndReason)
        {
            return new OfficialCareerHistoryRow(KingdomId, OfficerId,
                ActorId, CityId, Layer, OfficeId, ActorName, StartYear,
                pEndYear, isCurrent: false, pEndReason, AppointedTime,
                KingdomName, CityName, RankId, Grade, CountyId);
        }
    }
}
