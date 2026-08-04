namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝 Xia 内容注册总入口。由 <c>ModClass.OnModLoad</c> 调用。
    ///     注册顺序:特质组 → 特质 → 状态 → 物品 → 王国 → 建筑 → 种族
    ///     (状态先于物品[qingAttack 引用];特质组先于特质;王国先于种族[kingdom_id 引用];
    ///      建筑[architecture]先于种族[race 引用 architecture_id="Xia"])。
    /// </summary>
    public static class XiaContent
    {
        public static void Init()
        {
            // 批B:特质组 + 特质
            RecentFeatureBenchmarkContent.Init();
            XiaTraitGroups.Init();
            XiaTraits.Init();
            XiaCultureTraits.Init();

            // 批C:状态 + 物品
            XiaStatus.Init();
            XiaItems.Init();
            XiaClanBanners.Init();

            // 批A:王国 + 船 + 建筑 + 种族
            XiaKingdom.Init();
            DiplomacyContent.Init();
            WarLoyaltyContent.Init();
            XiaBoats.Init();        // 船先于建筑(架构 actor_asset_id_trading/transport 引用船 id)
            XiaArchitecture.Init();
            XiaNameSets.Init();
            XiaRace.Init();
            ClassicCivLifespanContent.Init();
            SlaveryContent.Init();
            GuardContent.Init();
            BorderGuardContent.Init();
            RoyalAsylumContent.Init();
            FeudatoryContent.Init();
            WarMobilizationContent.Init();
            WartimeGarrisonContent.Init();
            StandingArmyPeacetimeContent.Init();
            XiaReproductionDecisionContent.Init();
            core.lineage.WarPlotRedirectService.Init();

            schools.HistoricalSchoolContent.Init();

            // 历代开国君主降临:注册开关 toggle + 世界日志资产。
            // 依赖 figure/first 特质(XiaTraits 已注册在前),放最后。
            figures.HistoricalFigureService.Init();
        }
    }
}
