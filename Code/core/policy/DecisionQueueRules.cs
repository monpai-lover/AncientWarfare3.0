namespace AncientWarfare3.core.policy
{
    public static class DecisionQueueRules
    {
        public const string FabricateCoreDecisionId = "aw_decision_fabricate_core";

        public static bool ShouldPreemptCurrentDecisionForCore(string currentDecisionId, bool coreDecisionAvailable)
        {
            return false;
        }

        public static bool ShouldQueueDecisionWhenBusy(string currentDecisionId, string nextDecisionId)
        {
            return !string.IsNullOrEmpty(currentDecisionId) && !string.IsNullOrEmpty(nextDecisionId);
        }
    }
}
