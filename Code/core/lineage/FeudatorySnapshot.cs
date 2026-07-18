using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class FeudatorySnapshot
    {
        private readonly long[] _cityIds;

        public FeudatorySnapshot(long pFeudatoryId, long pEmpireKingdomId,
            long pPrinceActorId, long pSeatCityId, int pAutonomy, int pLoyalty,
            IReadOnlyList<long> pCityIds, long pGarrisonArmyId = -1,
            long pGarrisonCaptainActorId = -1, string pEmpireName = "",
            string pPrinceName = "", string pSeatName = "")
        {
            FeudatoryId = pFeudatoryId;
            EmpireKingdomId = pEmpireKingdomId;
            PrinceActorId = pPrinceActorId;
            SeatCityId = pSeatCityId;
            Autonomy = Math.Max(0, Math.Min(100, pAutonomy));
            Loyalty = Math.Max(0, Math.Min(100, pLoyalty));
            GarrisonArmyId = pGarrisonArmyId;
            GarrisonCaptainActorId = pGarrisonCaptainActorId;
            EmpireName = pEmpireName ?? "";
            PrinceName = pPrinceName ?? "";
            SeatName = pSeatName ?? "";
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
        public long GarrisonArmyId { get; }
        public long GarrisonCaptainActorId { get; }
        public string EmpireName { get; }
        public string PrinceName { get; }
        public string SeatName { get; }
        public IReadOnlyList<long> CityIds => _cityIds;

        public FeudatorySnapshot WithGarrison(long pArmyId, long pCaptainActorId)
        {
            return new FeudatorySnapshot(FeudatoryId, EmpireKingdomId,
                PrinceActorId, SeatCityId, Autonomy, Loyalty, _cityIds,
                pArmyId, pCaptainActorId, EmpireName, PrinceName, SeatName);
        }

        public FeudatorySnapshot WithCitiesAndSeat(
            IReadOnlyList<long> pCityIds, long pSeatCityId,
            string pSeatName = null)
        {
            return new FeudatorySnapshot(FeudatoryId, EmpireKingdomId,
                PrinceActorId, pSeatCityId, Autonomy, Loyalty, pCityIds,
                GarrisonArmyId, GarrisonCaptainActorId, EmpireName, PrinceName,
                pSeatName ?? SeatName);
        }
    }
}
