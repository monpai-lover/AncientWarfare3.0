using System.Collections.Generic;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    public static class WarDecisionTargetTextRules
    {
        public static string BuildSummary(string pReason, string pTargetKingdom, string pTargetCity)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(pReason))
                lines.Add(AW_L10n.Text("aw_war_declaration_reason", "War reason: ") + pReason);
            if (!string.IsNullOrEmpty(pTargetKingdom))
                lines.Add(AW_L10n.Text("aw_war_target_realm", "Target realm: ") + pTargetKingdom);
            if (!string.IsNullOrEmpty(pTargetCity))
                lines.Add(AW_L10n.Text("aw_war_target_goal", "War target: ") + pTargetCity);
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
            string stats = AW_L10n.Text("aw_war_stat_core", "Core ") + pCoreCount + " " +
                           AW_L10n.Text("aw_war_stat_strong", "Strong ") + pStrongClaimCount + " " +
                           AW_L10n.Text("aw_war_stat_weak", "Weak ") + pWeakClaimCount + " " +
                           AW_L10n.Text("aw_war_stat_pending", "Pending ") + pPendingCount;
            if (!string.IsNullOrEmpty(pTargetCityRich))
                stats += " " + AW_L10n.Text("aw_war_target_goal", "War target: ") + pTargetCityRich;
            return stats;
        }
    }
}
