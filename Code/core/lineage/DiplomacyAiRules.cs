using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    internal static class DiplomacyAiRules
    {
        internal const string TogglePowerId = "aw_diplomacy_ai";

        internal static bool AllowsAiInitiation(
            DiplomacyProposalType pType)
        {
            switch (pType)
            {
                case DiplomacyProposalType.Alliance:
                    return AWPerformanceSettings.EnableAiAllianceActions;
                case DiplomacyProposalType.Vassalize:
                case DiplomacyProposalType.Tributary:
                    return AWPerformanceSettings.EnableAiVassalActions;
                default:
                    return true;
            }
        }

        internal static bool ShouldRun(bool pEnabled)
        {
            return pEnabled;
        }
    }
}
