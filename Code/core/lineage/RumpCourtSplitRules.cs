namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     小朝廷分裂的触发规则。
    ///
    ///     首都被攻破之后，君主带着残余领土退守别处。这时朝中有继承权的人
    ///     不会安分 —— 都城丢了，正统性也就跟着松动，谁手上有城、有将、有
    ///     朝官支持，谁就敢自立。本规则只判断「这一刻该不该裂」，裂开之后
    ///     的建国、割城、内战全部复用既有的继承争议链路
    ///     （见 <see cref="SuccessionDisputeService"/>）。
    ///
    ///     纯函数，不碰世界状态，便于单独验证。
    /// </summary>
    internal static class RumpCourtSplitRules
    {
        /// <summary>
        ///     残余领土至少要有这么多城才可能分裂。
        ///
        ///     少于两座就无从分割 —— 既有的领土守恒规则
        ///     （<see cref="SuccessionDisputeRules.CanMaintainTerritorialInvariant"/>）
        ///     要求分裂后两边都还剩城，一座城的国家裂不出第二个政权。
        /// </summary>
        internal const int MinimumRumpCities = 2;

        /// <summary>
        ///     首都沦陷是否应当引发小朝廷分裂。
        ///
        ///     每一条都是「不满足就别裂」的硬条件：
        ///     <list type="bullet">
        ///     <item>王国还活着，且还剩得下两座城可分</item>
        ///     <item>还有临时都城可作锚点 —— 支持度是按城算的，
        ///           没有锚点就无从挑选归附方</item>
        ///     <item>君主还在（哪怕已弃城出逃）—— 留守朝廷这一方得有人</item>
        ///     <item>手上没有正在进行的继承争议 —— 一国同时只容一场</item>
        ///     </list>
        /// </summary>
        internal static bool ShouldSplit(bool pKingdomAlive,
            int pRemainingCityCount, bool pHasRumpCapital,
            bool pHasSittingRuler, bool pHasActiveDispute)
        {
            return pKingdomAlive && !pHasActiveDispute &&
                   pHasRumpCapital && pHasSittingRuler &&
                   pRemainingCityCount >= MinimumRumpCities;
        }
    }
}
