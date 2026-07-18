using System;

namespace AncientWarfare3.core.lineage
{
    public enum FeudatoryRepairAction
    {
        Ignore = 0,
        RemoveCity = 1,
        MoveSeat = 2,
        Abolish = 3
    }

    public readonly struct FeudatoryRepairDecision
    {
        public FeudatoryRepairDecision(FeudatoryRepairAction pAction,
            long pNewSeatCityId = -1)
        {
            Action = pAction;
            NewSeatCityId = pNewSeatCityId;
        }

        public FeudatoryRepairAction Action { get; }
        public long NewSeatCityId { get; }
    }

    public static class FeudatoryRules
    {
        public const int MaximumCities = 5;
        public const int MaximumPrincesPerDecision = 8;
        public const int AnnualModulo = 4;

        public static bool IsEligiblePrince(bool pIsMandateDynast, bool pAdult,
            bool pMale, bool pKing, bool pHeir, bool pAlreadyPrince,
            bool pValidRestorationState)
        {
            return pIsMandateDynast && pAdult && pMale && !pKing && !pHeir &&
                   !pAlreadyPrince && pValidRestorationState;
        }

        public static bool CanAssignCity(bool pSameKingdom, bool pAlive,
            bool pCapital, bool pCapitalAdjacent, bool pAssigned,
            bool pConnected, int pSelectedCount)
        {
            return pSameKingdom && pAlive && !pCapital && !pCapitalAdjacent &&
                   !pAssigned && pConnected && pSelectedCount >= 0 &&
                   pSelectedCount < MaximumCities;
        }

        public static bool ShouldRunAnnualWork(int pYear, long pKingdomId,
            int pModulo = AnnualModulo)
        {
            if (pModulo <= 0) return false;
            int slot = (int)(Math.Abs(pKingdomId) % pModulo);
            int yearSlot = ((pYear % pModulo) + pModulo) % pModulo;
            return yearSlot == slot;
        }

        public static FeudatoryRepairDecision ResolveCityTransfer(
            bool pIsMember, bool pSameOwner, long pCityId, long pSeatCityId,
            System.Collections.Generic.IReadOnlyList<long> pRemainingCityIds)
        {
            if (!pIsMember || pSameOwner)
                return new FeudatoryRepairDecision(FeudatoryRepairAction.Ignore);
            int count = pRemainingCityIds?.Count ?? 0;
            if (count == 0)
                return new FeudatoryRepairDecision(FeudatoryRepairAction.Abolish);
            if (pCityId != pSeatCityId)
                return new FeudatoryRepairDecision(
                    FeudatoryRepairAction.RemoveCity);

            long newSeatId = long.MaxValue;
            for (int i = 0; i < count; i++)
                if (pRemainingCityIds[i] >= 0 &&
                    pRemainingCityIds[i] < newSeatId)
                    newSeatId = pRemainingCityIds[i];
            return newSeatId == long.MaxValue
                ? new FeudatoryRepairDecision(FeudatoryRepairAction.Abolish)
                : new FeudatoryRepairDecision(FeudatoryRepairAction.MoveSeat,
                    newSeatId);
        }

        public static long SelectOneCapitalCoreRepair(
            System.Collections.Generic.IReadOnlyList<long> pFeudatoryCityIds,
            System.Collections.Generic.IReadOnlyList<long> pCapitalCoreCityIds)
        {
            int cityCount = pFeudatoryCityIds?.Count ?? 0;
            int coreCount = pCapitalCoreCityIds?.Count ?? 0;
            long selected = long.MaxValue;
            for (int cityIndex = 0; cityIndex < cityCount; cityIndex++)
            {
                long cityId = pFeudatoryCityIds[cityIndex];
                for (int coreIndex = 0; coreIndex < coreCount; coreIndex++)
                {
                    if (cityId != pCapitalCoreCityIds[coreIndex]) continue;
                    if (cityId >= 0 && cityId < selected) selected = cityId;
                    break;
                }
            }
            return selected == long.MaxValue ? -1L : selected;
        }
    }
}
