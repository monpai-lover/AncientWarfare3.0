using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    public static class DecisionTargetTextRules
    {
        public static string TargetLine(string pTargetName)
        {
            return string.IsNullOrEmpty(pTargetName)
                ? ""
                : AW_L10n.Text("aw_decision_target_prefix", "Target: ") + pTargetName;
        }
    }
}
