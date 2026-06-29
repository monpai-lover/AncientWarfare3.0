namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝 Xia 内容注册总入口。由 <c>ModClass.OnModLoad</c> 调用。
    ///     注册顺序:特质组 → 特质 → 王国 → 种族
    ///     (种族 kingdom_id_* 引用王国;特质组先于特质;均先于种族以备引用)。
    /// </summary>
    public static class XiaContent
    {
        public static void Init()
        {
            // 批B:特质组 + 特质
            XiaTraitGroups.Init();
            XiaTraits.Init();

            // 批A:王国 + 种族
            XiaKingdom.Init();
            XiaRace.Init();
        }
    }
}
