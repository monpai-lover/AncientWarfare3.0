using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class MandatePoliticalCatalystService
    {
        public static int CourtDelta(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            CourtDirectionSnapshot direction =
                CourtDirectionService.ReadCached(pKingdom);
            pKingdom.data.get(LineageKeys.MINISTERIAL_POWER,
                out int generalPower, 0);
            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_POWER,
                out int premierPower, 0);
            return MandateFeudatoryCompletionRules.CourtCatalystDelta(
                direction.Order, direction.Livelihood,
                direction.Aggression, direction.Peace,
                System.Math.Max(generalPower, premierPower));
        }
    }
}
