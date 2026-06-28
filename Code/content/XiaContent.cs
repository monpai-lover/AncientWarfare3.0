namespace AncientWarfare3.content
{
    /// <summary>
    ///     批A — 夏朝 Xia 内容注册总入口。由 <c>ModClass.OnModLoad</c> 调用。
    ///     注册顺序:王国 → 种族(种族的 kingdom_id_* 引用王国 id,故王国先行)。
    /// </summary>
    public static class XiaContent
    {
        public static void Init()
        {
            XiaKingdom.Init();
            XiaRace.Init();
        }
    }
}
