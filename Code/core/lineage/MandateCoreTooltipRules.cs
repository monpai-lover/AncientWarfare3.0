using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateCoreTooltipRules
    {
        public static string BuildPointedKingdomControlLine(string pKingdomName, float pControlRatio)
        {
            return BuildPointedKingdomControlLine(pKingdomName, pControlRatio, "指向国家控制率: ");
        }

        public static string BuildPointedKingdomControlLine(string pKingdomName, float pControlRatio, string pPrefix)
        {
            if (string.IsNullOrWhiteSpace(pKingdomName)) return "";
            int percent = (int)Math.Round(Math.Max(0f, pControlRatio) * 100f, MidpointRounding.AwayFromZero);
            return (pPrefix ?? "") + pKingdomName + " " + percent + "%";
        }

        public static string BuildPointedKingdomCoreCountLine(string pKingdomName, int pControlledCount,
            int pTotalCount, string pPrefix)
        {
            if (string.IsNullOrWhiteSpace(pKingdomName)) return "";
            int total = Math.Max(0, pTotalCount);
            int controlled = Math.Max(0, Math.Min(Math.Max(0, pControlledCount), total));
            return (pPrefix ?? "") + pKingdomName + " " + controlled + "/" + total;
        }
    }
}
