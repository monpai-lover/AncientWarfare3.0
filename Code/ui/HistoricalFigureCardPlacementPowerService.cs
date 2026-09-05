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
        /// <param name="pMinisterOnly">
        ///     大臣卡(含武将)只能进已有的文明城市 —— 它们要入朝或入军,
        ///     没有朝廷可入就无从安置,部署会以
        ///     <c>minister_requires_existing_city</c> 失败。提示里必须提前说清,
        ///     否则玩家会一直点无主地然后看着它静默失败。
        /// </param>
        internal static void ShowPlacementHint(bool pMinisterOnly = false)
        {
            try
            {
                string text = pMinisterOnly
                    ? AW_L10n.Text(
                        "aw_historical_figure_cards_placement_hint_minister",
                        "请点击地图选择一座文明城市（大臣只能进入已有国家）")
                    : AW_L10n.Text(
                        "aw_historical_figure_cards_placement_hint",
                        "请点击地图选择部署位置（文明城市或无主陆地）");
                WorldTip.showNow(text, pTranslate: false, "top", 4f);
            }
            catch { }
        }
    }
}
