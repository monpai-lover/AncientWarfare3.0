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

        /// <summary>
        ///     正在造核心的时候,还要不要去扫全国找下一个目标城。
        ///
        ///     不要。槽位被占着时扫出来的城只能进队列,而每次扫描要按王国查一遍
        ///     核心表和在建项目表、再逐城判定 —— 这个循环每王国每月跑两遍,是
        ///     KingdomDecisionMonthly 的主要开销。造好了槽位空出来,那一次再查,
        ///     接着造下一个。
        ///
        ///     显式排队(玩家或决议指定某座城)不走这条路,不受影响。
        /// </summary>
        public static bool ShouldScanForNextTarget(long currentCoreCityId)
        {
            return currentCoreCityId < 0;
        }

        /// <summary>
        ///     上次扫描一个可造目标都没找到之后,这次还要不要再扫。
        ///
        ///     不要 —— 除非「可能冒出新目标」的信号变了。全国都已核心化是稳定
        ///     终局,成熟帝国会在这个状态停留很久;没有这道闸,月推进会永远每月
        ///     空扫一遍(每次两条查询加一趟遍历),纯空转。
        ///
        ///     两个信号一起看:
        ///       代际号   得到/失去城、核心落成、项目开工完工时递增(精确信号)
        ///       城市数   兜底 —— 万一某条建城/灭城路径没接上代际号,数目也会变
        ///
        ///     两者都没变才跳过。任一变化都重新开扫,所以漏接信号最多让「本该重扫
        ///     却没重扫」再等一个变化,而不会永久卡死:失去/得到城一定会动城市数。
        /// </summary>
        public static bool ShouldScanForTargets(bool pHasEmptyResultLatch,
            int pLatchedRevision, int pLatchedCityCount,
            int pCurrentRevision, int pCurrentCityCount)
        {
            if (!pHasEmptyResultLatch) return true;
            return pLatchedRevision != pCurrentRevision ||
                   pLatchedCityCount != pCurrentCityCount;
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
