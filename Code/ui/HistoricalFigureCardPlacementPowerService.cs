using AncientWarfare3.content;

namespace AncientWarfare3.ui
{
    /// <summary>
    ///     部署选点期间的提示。
    ///
    ///     <para>
    ///     背景:原版 <c>PlayerControl.Update</c> 的分支是
    ///     <c>if (!World.world.isAnyPowerSelected()) checkEmptyClick(); else … clickedStart()</c>
    ///     —— 没有选中任何神力时,地图点击走的是 <c>checkEmptyClick</c>,
    ///     根本不会进 <c>clickedFinal</c>。<c>AW_HistoricalFigureCardPatch</c>
    ///     挂的是 <c>clickedFinal</c> 前缀,所以「部署」进入选点状态后
    ///     必须先选中一个神力,点击才会被我们接到。
    ///     </para>
    ///
    ///     <para>
    ///     不替玩家自动选:那会覆盖他手上的笔刷、也让「为什么突然换了神力」
    ///     变得莫名其妙。改为按原版的做法弹一条工具栏提示
    ///     (<c>WorldTip.showNow</c>,和 <c>PowerButton.clickShop</c> 用的是
    ///     同一个入口),告诉玩家先选放置夏人神力再点地图。
    ///     </para>
    /// </summary>
    internal static class HistoricalFigureCardPlacementPowerService
    {
        internal static void ShowPlacementHint()
        {
            try
            {
                // 已经选着夏人神力就不用再提示了。
                PowerButton selected = PowerButtonSelector.instance?.selectedButton;
                if (selected != null && selected.godPower != null &&
                    selected.godPower.id == GodPowerLibrary.SPAWN_XIA) return;
                WorldTip.showNow(AW_L10n.Text(
                        "aw_historical_figure_cards_placement_hint",
                        "请先在下方选中「放置夏人」神力，再点击地图选择部署位置"),
                    pTranslate: false, "top", 4f);
            }
            catch { }
        }
    }
}
