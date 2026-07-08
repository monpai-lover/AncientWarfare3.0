using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class WarDecisionTargetTextRules
    {
        public static string BuildSummary(string pReason, string pTargetKingdom, string pTargetCity)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(pReason)) lines.Add("\u5ba3\u6218\u7406\u7531\uff1a" + pReason);
            if (!string.IsNullOrEmpty(pTargetKingdom)) lines.Add("\u76ee\u6807\u56fd\uff1a" + pTargetKingdom);
            if (!string.IsNullOrEmpty(pTargetCity)) lines.Add("\u6218\u4e89\u76ee\u6807\uff1a" + pTargetCity);
            return string.Join("\n", lines.ToArray());
        }

        public static string BuildRowLabel(string pTargetKingdomRich, string pLabel)
        {
            if (string.IsNullOrEmpty(pTargetKingdomRich)) return pLabel ?? "";
            if (string.IsNullOrEmpty(pLabel)) return pTargetKingdomRich;
            return pTargetKingdomRich + "\uff1a" + pLabel;
        }

        public static string BuildStatsLine(int pCoreCount, int pStrongClaimCount, int pWeakClaimCount,
            int pPendingCount, string pTargetCityRich)
        {
            string stats = "\u6838" + pCoreCount + " \u5f3a" + pStrongClaimCount +
                           " \u5f31" + pWeakClaimCount + " \u9020" + pPendingCount;
            if (!string.IsNullOrEmpty(pTargetCityRich)) stats += " \u76ee\u6807\uff1a" + pTargetCityRich;
            return stats;
        }
    }
}
