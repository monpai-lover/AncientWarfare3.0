namespace AncientWarfare3.ui
{
    /// <summary>姓族系统所有自定义窗口 id 常量,集中一处避免硬编码散落。</summary>
    internal static class AW_LineageWindowIds
    {
        public const string OVERVIEW = "aw_lineage_overview";   // 姓族总览(列所有姓)
        public const string SHI_LIST = "aw_shi_list";           // 某姓的氏支列表
        public const string FAMILY_TREE = "aw_family_tree";     // 家族树/氏族大树(双模式)
        public const string HISTORY = "aw_history";             // 编年史(人物传记/国家历史/城市易主,三来源)
        public const string KINGDOM_ROSTER = "aw_kingdom_roster"; // 全王国列表(含亡国)

        /// <summary>
        ///     安全打开窗口,避免重复入栈(用户反馈"反复点击开多次、Esc 退不完")。
        ///     原版 ScrollWindow.showWindow 默认 pBlockSame=false,每次 show 都把当前窗压进 WindowHistory,
        ///     同一窗反复点 → 历史栈叠多层。这里:已是当前窗 → 只跑刷新回调(不再 show、不入栈);
        ///     否则用 pBlockSame=true 打开(同窗再点会被原版拦成 shake 而非叠栈)。
        /// </summary>
        public static void SafeShow(string pWindowId, System.Action pRefreshIfCurrent = null)
        {
            if (ScrollWindow.isCurrentWindow(pWindowId))
            {
                pRefreshIfCurrent?.Invoke();
                return;
            }

            // 【首次打不开根治】CreateAndInit→create(true)→hide 会启动 0.1s 关闭 moveTween 填进 _animations_list,
            // 而 create 里调的 finishTween() 不清列表 → 首次 showWindow 被 isAnimationActive() 闸门
            // (ScrollWindow.cs:345)整体跳过 → 窗口不显示(用户"第一次点打不开,第二次才开")。
            // showWindow 前强制完成所有在途窗口动画(finishAnimations 是 public static,Kill(complete:true)
            // 触发各 tween OnComplete 自我移除)→ 闸门放行,首次即可打开。
            ScrollWindow.finishAnimations();

            // pBlockSame=true 时原版 showWindow 会解引用 _current_window(ScrollWindow.cs:349),
            // 若当前无任何窗口(_current_window==null,首次打开)→ NullRef。故仅在确有当前窗口时才传 true。
            bool hasCurrent = ScrollWindow.getCurrentWindow() != null;
            ScrollWindow.showWindow(pWindowId, false, hasCurrent);
        }
    }
}
