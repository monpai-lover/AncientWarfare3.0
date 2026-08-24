using System;

namespace AncientWarfare3.core.lineage
{
    internal enum TributaryDiplomacyDirection
    {
        None = 0,
        BasePays = 1,
        OtherPays = 2
    }

    internal readonly struct TributaryDiplomacyDetails
    {
        internal TributaryDiplomacyDetails(
            TributaryDiplomacyDirection pDirection,
            long pRelationId, long pTributaryId, long pSuzerainId,
            int pTributeRate, int pNextDueYear, int pLastPaidYear,
            int pLastFactorPercent, float pForecastPolitical,
            int pForecastGold, string pSettlementState,
            bool pHasCurrentYearOffering)
        {
            Direction = pDirection;
            RelationId = pRelationId;
            TributaryId = pTributaryId;
            SuzerainId = pSuzerainId;
            TributeRate = Math.Max(0, pTributeRate);
            NextDueYear = pNextDueYear;
            LastPaidYear = pLastPaidYear;
            LastFactorPercent = pLastFactorPercent;
            ForecastPolitical = Math.Max(0f, pForecastPolitical);
            ForecastGold = Math.Max(0, pForecastGold);
            SettlementState = pSettlementState ?? "no_record";
            HasCurrentYearOffering = pHasCurrentYearOffering;
        }

        internal TributaryDiplomacyDirection Direction { get; }
        internal long RelationId { get; }
        internal long TributaryId { get; }
        internal long SuzerainId { get; }
        internal int TributeRate { get; }
        internal int NextDueYear { get; }
        internal int LastPaidYear { get; }
        internal int LastFactorPercent { get; }
        internal float ForecastPolitical { get; }
        internal int ForecastGold { get; }
        internal string SettlementState { get; }
        internal bool HasCurrentYearOffering { get; }

        internal static TributaryDiplomacyDirection ResolveDirection(
            bool pBaseIsTributary, bool pOtherIsTributary)
        {
            if (pBaseIsTributary == pOtherIsTributary)
                return TributaryDiplomacyDirection.None;
            return pBaseIsTributary
                ? TributaryDiplomacyDirection.BasePays
                : TributaryDiplomacyDirection.OtherPays;
        }
    }
}
