namespace AncientWarfare3.core.policy
{
    public static class CoreFabricationSlotRules
    {
        public const string ProjectCore = "fabricate_core";

        public static bool ShouldUseDedicatedSlot(string pProjectType)
        {
            return pProjectType == ProjectCore;
        }

        public static bool ShouldStartWhenEmpty(long currentCoreCityId, bool hasAvailableCoreTarget)
        {
            return currentCoreCityId < 0 && hasAvailableCoreTarget;
        }

        public static bool ShouldQueueWhenBusy(long currentCoreCityId, bool hasAvailableCoreTarget)
        {
            return currentCoreCityId >= 0 && hasAvailableCoreTarget;
        }

        public static bool ShouldShowDecisionSidebarButton(bool pIsDecisionPanel, bool pPolicyEnabled)
        {
            return pIsDecisionPanel && pPolicyEnabled;
        }

        public static string BuildSidebarLabel(string pCurrentCityName, int pProjectCount, int pProgressPercent)
        {
            return BuildSidebarLabel(pCurrentCityName, pProjectCount, pProgressPercent, "核心队列", "核心");
        }

        public static string BuildSidebarLabel(string pCurrentCityName, int pProjectCount, int pProgressPercent,
            string pIdleLabel, string pBusyLabel)
        {
            if (pProjectCount <= 0)
                return string.IsNullOrEmpty(pIdleLabel) ? "核心队列" : pIdleLabel;

            string prefix = string.IsNullOrEmpty(pCurrentCityName)
                ? string.IsNullOrEmpty(pIdleLabel) ? "核心队列" : pIdleLabel
                : string.IsNullOrEmpty(pBusyLabel) ? "核心" : pBusyLabel;
            return prefix + "\n" + pProgressPercent + "%/" + pProjectCount;
        }
    }
}
