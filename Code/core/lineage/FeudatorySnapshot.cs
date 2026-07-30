using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct FeudatoryCityDisplayRow
    {
        public FeudatoryCityDisplayRow(long pCityId, string pCityName,
            long pGovernorActorId, string pGovernorName)
        {
            CityId = pCityId;
            CityName = pCityName ?? "";
            GovernorActorId = pGovernorActorId;
            GovernorName = pGovernorName ?? "";
        }

        public long CityId { get; }
        public string CityName { get; }
        public long GovernorActorId { get; }
        public string GovernorName { get; }
    }

    public sealed class FeudatorySnapshot
    {
        private readonly long[] _cityIds;
        private readonly FeudatoryCityDisplayRow[] _cityRows;

        public FeudatorySnapshot(long pFeudatoryId, long pEmpireKingdomId,
            long pPrinceActorId, long pSeatCityId, int pAutonomy, int pLoyalty,
            IReadOnlyList<long> pCityIds, long pGarrisonArmyId = -1,
            long pGarrisonCaptainActorId = -1, string pEmpireName = "",
            string pPrinceName = "", string pSeatName = "",
            string pFeudatoryName = "", string pParentColor = "",
            string pPrinceShiLabel = "", int pGarrisonSize = 0,
            long pSuccessorActorId = -1, string pSuccessorName = "",
            IReadOnlyList<FeudatoryCityDisplayRow> pCityRows = null,
            long pShiBranchId = -1)
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
            FeudatoryName = pFeudatoryName ?? "";
            ParentColor = pParentColor ?? "";
            PrinceShiLabel = pPrinceShiLabel ?? "";
            GarrisonSize = Math.Max(0, pGarrisonSize);
            SuccessorActorId = pSuccessorActorId;
            SuccessorName = pSuccessorName ?? "";
            ShiBranchId = pShiBranchId;
            int count = Math.Min(FeudatoryRules.MaximumCities, pCityIds?.Count ?? 0);
            _cityIds = new long[count];
            for (int i = 0; i < count; i++) _cityIds[i] = pCityIds[i];
            int rowCount = Math.Min(FeudatoryRules.MaximumCities,
                pCityRows?.Count ?? 0);
            _cityRows = new FeudatoryCityDisplayRow[rowCount];
            for (int i = 0; i < rowCount; i++) _cityRows[i] = pCityRows[i];
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
        public string FeudatoryName { get; }
        public string ParentColor { get; }
        public string PrinceShiLabel { get; }
        public int GarrisonSize { get; }
        public long SuccessorActorId { get; }
        public string SuccessorName { get; }
        public long ShiBranchId { get; }
        public IReadOnlyList<long> CityIds => _cityIds;
        public IReadOnlyList<FeudatoryCityDisplayRow> CityRows => _cityRows;

        public FeudatorySnapshot WithGarrison(long pArmyId,
            long pCaptainActorId, int pGarrisonSize = -1)
        {
            return new FeudatorySnapshot(FeudatoryId, EmpireKingdomId,
                PrinceActorId, SeatCityId, Autonomy, Loyalty, _cityIds,
                pArmyId, pCaptainActorId, EmpireName, PrinceName, SeatName,
                FeudatoryName, ParentColor, PrinceShiLabel,
                pGarrisonSize < 0 ? GarrisonSize : pGarrisonSize,
                SuccessorActorId, SuccessorName, _cityRows, ShiBranchId);
        }

        public FeudatorySnapshot WithCitiesAndSeat(
            IReadOnlyList<long> pCityIds, long pSeatCityId,
            string pSeatName = null,
            IReadOnlyList<FeudatoryCityDisplayRow> pCityRows = null,
            string pFeudatoryName = null)
        {
            return new FeudatorySnapshot(FeudatoryId, EmpireKingdomId,
                PrinceActorId, pSeatCityId, Autonomy, Loyalty, pCityIds,
                GarrisonArmyId, GarrisonCaptainActorId, EmpireName, PrinceName,
                pSeatName ?? SeatName, pFeudatoryName ?? FeudatoryName,
                ParentColor,
                PrinceShiLabel, GarrisonSize, SuccessorActorId, SuccessorName,
                pCityRows ?? _cityRows, ShiBranchId);
        }

        public FeudatorySnapshot WithPrince(long princeActorId,
            string princeName, long shiBranchId, string princeShiLabel,
            long successorActorId, string successorName)
        {
            return new FeudatorySnapshot(FeudatoryId, EmpireKingdomId,
                princeActorId, SeatCityId, Autonomy, Loyalty, _cityIds,
                GarrisonArmyId, GarrisonCaptainActorId, EmpireName,
                princeName, SeatName, FeudatoryName, ParentColor,
                princeShiLabel, GarrisonSize, successorActorId,
                successorName, _cityRows, shiBranchId);
        }

        public FeudatorySnapshot WithAutonomyLoyalty(int autonomy,
            int loyalty)
        {
            return new FeudatorySnapshot(FeudatoryId, EmpireKingdomId,
                PrinceActorId, SeatCityId, autonomy, loyalty, _cityIds,
                GarrisonArmyId, GarrisonCaptainActorId, EmpireName,
                PrinceName, SeatName, FeudatoryName, ParentColor,
                PrinceShiLabel, GarrisonSize, SuccessorActorId,
                SuccessorName, _cityRows, ShiBranchId);
        }
    }
}
