using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     KingdomWindow 右侧栏加"国家历史"按钮(编年史入口)。
    ///     Postfix showStatsRows(每次开窗刷新跑)。找 "Tabs Right" 容器,兜底用窗体根。
    ///     点击 → HistoryListWindow.OpenKingdom(kingdom.id)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_KingdomTabPatch
    {
        private const string BTN_NAME = "AW_KingdomHistoryTabButton";
        private const int SIZE = 40;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(KingdomWindow __instance)
        {
            if (__instance == null) return;
            var kingdom = __instance.meta_object;
            if (kingdom == null || kingdom.data == null) return;

            Transform rail = __instance.transform.Find("Tabs Right");
            if (rail == null) return; // 无右栏:不强插,避免乱位(unit 窗有,kingdom 窗运行时核实)

            long kingdomId = kingdom.id;
            Transform existing = rail.Find(BTN_NAME);
            Button btn = existing != null ? existing.GetComponent<Button>() : BuildButton(rail);
            if (existing != null) existing.gameObject.SetActive(true);

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => AncientWarfare3.ui.windows.HistoryListWindow.OpenKingdom(kingdomId));
        }

        private static Button BuildButton(Transform pRail)
        {
            var obj = new GameObject(BTN_NAME, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pRail, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(SIZE, SIZE);
            rect.localScale = Vector3.one;

            var bg = obj.GetComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/button");
            bg.type = Image.Type.Sliced;

            var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(obj.transform, false);
            var irect = iconObj.GetComponent<RectTransform>();
            irect.anchorMin = Vector2.zero; irect.anchorMax = Vector2.one;
            irect.sizeDelta = new Vector2(-8, -8); irect.anchoredPosition = Vector2.zero;
            var icon = iconObj.GetComponent<Image>();
            // 国家历史图标:AW2 风格 iconKingdomList。
            icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                          ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias")
                          ?? SpriteTextureLoader.getSprite("ui/icons/iconClan");
            icon.preserveAspect = true;

            var tip = obj.GetComponent<TipButton>();
            tip.type = "normal";
            tip.hoverAction = () => Tooltip.show(obj, "normal",
                new TooltipData { tip_name = "aw_kingdom_history_entry", tip_description = "aw_view_kingdom_history" });

            return obj.GetComponent<Button>();
        }
    }
}
