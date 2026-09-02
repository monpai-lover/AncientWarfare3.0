using System;

namespace AncientWarfare3.api.history
{
    public sealed class AW3HistoryQuery
    {
        public const int MaximumLimit = 512;

        private AW3HistoryQuery(double worldTimeFrom, double worldTimeTo,
            long actorId, long kingdomId, long cityId, long countyId,
            string officeId, string domain, string eventType, int limit,
            string cursor)
        {
            WorldTimeFrom = NormalizeTime(worldTimeFrom);
            WorldTimeTo = NormalizeTime(worldTimeTo);
            ActorId = actorId;
            KingdomId = kingdomId;
            CityId = cityId;
            CountyId = countyId;
            OfficeId = officeId ?? "";
            Domain = domain ?? "";
            EventType = eventType ?? "";
            Limit = NormalizeLimit(limit);
            Cursor = cursor ?? "";
        }

        public double WorldTimeFrom { get; }
        public double WorldTimeTo { get; }
        public long ActorId { get; }
        public long KingdomId { get; }
        public long CityId { get; }
        public long CountyId { get; }
        public string OfficeId { get; }
        public string Domain { get; }
        public string EventType { get; }
        public int Limit { get; }
        public string Cursor { get; }

        public static AW3HistoryQuery ForActor(long actorId)
        {
            return Create(actorId: actorId);
        }

        public static AW3HistoryQuery ForKingdom(long kingdomId)
        {
            return Create(kingdomId: kingdomId);
        }

        public static AW3HistoryQuery Create(double worldTimeFrom = -1d,
            double worldTimeTo = -1d, long actorId = -1L,
            long kingdomId = -1L, long cityId = -1L, long countyId = -1L,
            string officeId = "", string domain = "", string eventType = "",
            int limit = 64, string cursor = "")
        {
            return new AW3HistoryQuery(worldTimeFrom, worldTimeTo, actorId,
                kingdomId, cityId, countyId, officeId, domain, eventType,
                limit, cursor);
        }

        private static double NormalizeTime(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? -1d : value;
        }

        private static int NormalizeLimit(int value)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            return Math.Min(MaximumLimit, value);
        }
    }
}
