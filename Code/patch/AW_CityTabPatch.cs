using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     CityWindow 右侧栏加"城市历史"按钮(编年史入口,记城市易主)。
    ///     Postfix showStatsRows(每次开窗刷新跑)。找 "Tabs Right" 容器,无则不插。
    ///     点击 → HistoryListWindow.OpenCity(city.id)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_CityTabPatch
    {
        private const string BTN_NAME = "AW_CityHistoryTabButton";
        private const int SIZE = 40;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityWindow), nameof(CityWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(CityWindow __instance)
        {
            if (__instance == null) return;
            var city = __instance.meta_object;
            if (city == null || city.data == null) return;

            Transform rail = __instance.transform.Find("Tabs Right");
            if (rail == null) return;

            long cityId = city.id;
            Transform existing = rail.Find(BTN_NAME);
            Button btn = existing != null ? existing.GetComponent<Button>() : BuildButton(rail);
            if (existing != null) existing.gameObject.SetActive(true);

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => AncientWarfare3.ui.windows.HistoryListWindow.OpenCity(cityId));
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
            // 城市历史图标:AW2 风格 iconVillages。
            icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconVillages")
                          ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias")
                          ?? SpriteTextureLoader.getSprite("ui/icons/iconClan");
            icon.preserveAspect = true;

            var tip = obj.GetComponent<TipButton>();
            tip.type = "normal";
            tip.hoverAction = () => Tooltip.show(obj, "normal",
                new TooltipData { tip_name = "aw_city_history_entry", tip_description = "aw_view_city_history" });

            return obj.GetComponent<Button>();
        }
    }
}
