using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class FeudatorySnapshot
    {
        private readonly long[] _cityIds;

        public FeudatorySnapshot(long pFeudatoryId, long pEmpireKingdomId,
            long pPrinceActorId, long pSeatCityId, int pAutonomy, int pLoyalty,
            IReadOnlyList<long> pCityIds)
        {
            FeudatoryId = pFeudatoryId;
            EmpireKingdomId = pEmpireKingdomId;
            PrinceActorId = pPrinceActorId;
            SeatCityId = pSeatCityId;
            Autonomy = Math.Max(0, Math.Min(100, pAutonomy));
            Loyalty = Math.Max(0, Math.Min(100, pLoyalty));
            int count = Math.Min(FeudatoryRules.MaximumCities, pCityIds?.Count ?? 0);
            _cityIds = new long[count];
            for (int i = 0; i < count; i++) _cityIds[i] = pCityIds[i];
        }

        public long FeudatoryId { get; }
        public long EmpireKingdomId { get; }
        public long PrinceActorId { get; }
        public long SeatCityId { get; }
        public int Autonomy { get; }
        public int Loyalty { get; }
        public IReadOnlyList<long> CityIds => _cityIds;
    }
}
