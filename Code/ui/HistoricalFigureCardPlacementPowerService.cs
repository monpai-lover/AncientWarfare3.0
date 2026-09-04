using AncientWarfare3.content;

namespace AncientWarfare3.ui
{
    /// <summary>
    ///     部署选点期间的提示。
    ///
    ///     <para>
    ///     窗口在选点时是隐藏的,屏幕上没有任何东西说明「现在该点地图了」——
    ///     实测中玩家点完「部署到城市」后会以为流程断了,转而重新打开窗口再点
    ///     一次。这条工具栏提示(<c>WorldTip.showNow</c>,与
    ///     <c>PowerButton.clickShop</c> 同一个入口)补上这段空白。
    ///     </para>
    ///
    ///     <para>
    ///     不再要求玩家先选神力:<c>AW_HistoricalFigureCardPatch</c> 现在连
    ///     <c>checkEmptyClick</c>(没选神力时原版走的那条路)一起拦,选不选
    ///     神力都能完成选点。
    ///     </para>
    /// </summary>
    internal static class HistoricalFigureCardPlacementPowerService
    {
        internal static void ShowPlacementHint()
        {
            try
            {
                WorldTip.showNow(AW_L10n.Text(
                        "aw_historical_figure_cards_placement_hint",
                        "请点击地图选择部署位置（文明城市或无主陆地）"),
                    pTranslate: false, "top", 4f);
            }
            catch { }
        }
    }
}
