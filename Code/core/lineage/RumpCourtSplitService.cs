using System;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     首都沦陷之后的小朝廷分裂。
    ///
    ///     都城一破，君主退守残土，朝中有继承权的人便各自盘算 —— 谁手上有城、
    ///     有将、有朝官支持，谁就敢自立。这里只负责**判断该不该裂**并把它
    ///     交给既有的继承争议链路；建国、割城、内战一步都不自己实现：
    ///
    ///     <list type="number">
    ///     <item><see cref="SuccessionDisputeService.BuildPreparationFacts"/>
    ///           挑挑战者（<see cref="InheritanceCandidateService.ResolveFactionSupport"/>）
    ///           并按城主/将领/朝官的倾向选出归附的城</item>
    ///     <item><see cref="SuccessionDisputePersistenceService"/> 落库</item>
    ///     <item>争议泵推进：建国 → 割城 → 内战</item>
    ///     </list>
    ///
    ///     传参上做了一处取巧：把**在位君主同时当作前任与继任**传进去。
    ///     争议链路本来服务于「新君即位、旁支不服」，派系支持是相对于
    ///     「坐在位子上的那个人」算的 —— 首都沦陷这个语境里，坐在位子上的
    ///     正是那位弃都出逃的君主，口径天然一致，不必另造一套。
    /// </summary>
    internal static class RumpCourtSplitService
    {
        /// <summary>
        ///     待判定的有效期（年）。过了还没裂就作罢 —— 都城丢了很久还没
        ///     闹起来，说明这个朝廷稳住了。
        /// </summary>
        private const int PendingYears = 3;

        /// <summary>
        ///     首都失陷时记一笔待办，真正的判定推到年度那一拍。
        ///
        ///     破城当帧不能直接判：原版此时往往还没另立临时都城，而挑选归附
        ///     城池是以都城为锚点算方位与支持度的（见
        ///     <c>SuccessionDisputeService.SelectSupportCities</c>）。当场判
        ///     多半因为「没有都城」而直接放弃，且没有第二次机会。
        /// </summary>
        internal static void OnCapitalLost(Kingdom pFallenKingdom,
            City pLostCapital, Kingdom pConqueror)
        {
            if (pFallenKingdom?.data == null || pLostCapital?.data == null ||
                pFallenKingdom.isRekt()) return;
            try
            {
                pFallenKingdom.data.set(LineageKeys.RUMP_SPLIT_PENDING_YEAR,
                    Date.getCurrentYear());
                ModClass.LogInfo("[AW3] 小朝廷分裂待判: " +
                    (pFallenKingdom.name ?? "?") + " 失都 " +
                    (pLostCapital.data.name ?? "?") + " 于 " +
                    (pConqueror?.name ?? "?"));
            }
            catch { }
        }

        /// <summary>
        ///     年度那一拍：有待办就试着裂一次。
        /// </summary>
        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            pKingdom.data.get(LineageKeys.RUMP_SPLIT_PENDING_YEAR,
                out int pendingYear, int.MinValue);
            if (pendingYear == int.MinValue) return;
            int year = Date.getCurrentYear();
            if (year - pendingYear > PendingYears)
            {
                ClearPending(pKingdom);
                return;
            }
            if (TrySplit(pKingdom)) ClearPending(pKingdom);
        }

        private static void ClearPending(Kingdom pKingdom)
        {
            try
            {
                pKingdom.data.removeInt(
                    LineageKeys.RUMP_SPLIT_PENDING_YEAR);
            }
            catch { }
        }

        /// <summary>
        ///     成功开出一场分裂返回 true；条件不满足返回 false，
        ///     留着待办等下一年再看。
        /// </summary>
        private static bool TrySplit(Kingdom pFallenKingdom)
        {
            try
            {
                Actor ruler = pFallenKingdom.king;
                bool kingdomAlive = !pFallenKingdom.isRekt() &&
                                    pFallenKingdom.hasCities();
                int remaining = SafeCityCount(pFallenKingdom);
                // 临时都城:原版在丢掉都城之后会另立一座。它是支持度计算的
                // 锚点(SelectSupportCities 以 kingdom.capital 为基准判方位),
                // 还没立出来就等下一次,别硬裂。
                City rumpCapital = pFallenKingdom.capital;
                bool hasRumpCapital = rumpCapital?.data != null &&
                                      !rumpCapital.isRekt() &&
                                      rumpCapital.kingdom == pFallenKingdom;
                bool hasDispute = SuccessionDisputeService
                    .TryGetCachedByKingdom(pFallenKingdom.id, out _);

                if (!RumpCourtSplitRules.ShouldSplit(kingdomAlive, remaining,
                        hasRumpCapital, ruler?.data != null && ruler.isAlive(),
                        hasDispute))
                    return false;

                // 争议链路要一个「前任 + 在位者」。首都沦陷没有换人这一步,
                // 两边都传在位君主 —— 派系支持本来就是相对他算的。
                SuccessionDisputePreparationFacts facts =
                    SuccessionDisputeService.BuildPreparationFacts(
                        pFallenKingdom, ruler, ruler, SuccessionMode.NONE,
                        InheritanceLawService.GetEffectiveLaw(
                            pFallenKingdom));
                if (facts == null) return false;

                SuccessionDisputePersistenceService.QueueRumpCourtSplit(
                    facts);
                ModClass.LogInfo("[AW3] 小朝廷分裂: " +
                    (pFallenKingdom.name ?? "?") + " 残土 " + remaining +
                    " 城, 挑战者 " + facts.ClaimantActorId);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("[AW3] 小朝廷分裂判定失败: " +
                                    error.Message);
                return false;
            }
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom.countCities(); }
            catch { return 0; }
        }
    }
}
