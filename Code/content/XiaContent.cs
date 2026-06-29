namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝 Xia 内容注册总入口。由 <c>ModClass.OnModLoad</c> 调用。
    ///     注册顺序:特质组 → 特质 → 状态 → 物品 → 王国 → 种族
    ///     (状态先于物品[qingAttack 引用];特质组先于特质;王国先于种族[kingdom_id 引用])。
    /// </summary>
    public static class XiaContent
    {
        public static void Init()
        {
            // 批B:特质组 + 特质
            XiaTraitGroups.Init();
            XiaTraits.Init();

            // 批C:状态 + 物品
            XiaStatus.Init();
            XiaItems.Init();

            // 批A:王国 + 种族
            XiaKingdom.Init();
            XiaRace.Init();
        }
    }
}
