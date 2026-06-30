using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     兜住原版 <c>WindowHistory.clickBack</c> 的 null 解引用崩溃(Player.log 高频 NullRef):
    ///     原版 clickBack:41 直接 <c>ScrollWindow.getCurrentWindow().historyActionEnabled</c>,
    ///     而 <c>updateRightClickBack</c>(每帧实例 Update)只检查 this.historyActionEnabled,**不检查 static
    ///     _current_window 是否为 null** 就调 clickBack。窗口关闭/打开动画期(activeToFalse 挂在 0.1s
    ///     moveTween 回调里,GameObject 仍 active、Update 仍跑,但 _current_window 已被 moveAll*Remove 置 null)
    ///     若此时右键 → getCurrentWindow() 返回 null → 解引用炸。
    ///
    ///     修法:Prefix clickBack,当前无窗时直接收口(等同原版 else 分支 hideAllEvent)并跳过原方法。
    ///     这是原版自身的潜在 bug(任何窗口的动画期右键都会触发),非我方窗口独有,故全局兜。
    /// </summary>
    [HarmonyPatch(typeof(WindowHistory), nameof(WindowHistory.clickBack))]
    public static class AW_WindowHistorySafePatch
    {
        [HarmonyPrefix]
        public static bool ClickBack_Prefix()
        {
            if (ScrollWindow.getCurrentWindow() == null)
            {
                ScrollWindow.hideAllEvent();
                return false; // 跳过原方法,避免 :41 解引用 null
            }
            return true; // 正常放行
        }
    }
}
