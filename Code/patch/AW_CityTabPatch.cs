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
        private const string LOCAL_COURT_BTN_NAME =
            "AW_CityLocalCourtTabButton";
        private const string RENAME_BTN_NAME = "AW_CityStateRenameTabButton";
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

            long kingdomId = city.kingdom?.id ?? -1L;
            Transform existingLocal = rail.Find(LOCAL_COURT_BTN_NAME);
            Button localCourt = existingLocal != null
                ? existingLocal.GetComponent<Button>()
                : BuildLocalCourtButton(rail);
            if (existingLocal != null)
                existingLocal.gameObject.SetActive(kingdomId >= 0);
            localCourt.gameObject.SetActive(kingdomId >= 0);
            localCourt.onClick.RemoveAllListeners();
            if (kingdomId >= 0)
                localCourt.onClick.AddListener(() =>
                    AncientWarfare3.ui.windows.CourtWindow.OpenCity(
                        kingdomId, cityId));

            Transform existingRename = rail.Find(RENAME_BTN_NAME);
            Button rename = existingRename != null
                ? existingRename.GetComponent<Button>()
                : BuildRenameButton(rail);
            if (existingRename != null)
                existingRename.gameObject.SetActive(kingdomId >= 0);
            rename.gameObject.SetActive(kingdomId >= 0);
            rename.onClick.RemoveAllListeners();
            if (kingdomId >= 0)
                rename.onClick.AddListener(() =>
                    AncientWarfare3.ui.windows.CityStateRenameWindow.Open(
                        cityId));
        }

        private static Button BuildRenameButton(Transform pRail)
        {
            var obj = new GameObject(RENAME_BTN_NAME,
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            obj.transform.SetParent(pRail, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(SIZE, SIZE);
            rect.localScale = Vector3.one;

            Image background = obj.GetComponent<Image>();
            background.sprite = SpriteTextureLoader.getSprite(
                "ui/special/button");
            background.type = Image.Type.Sliced;

            var iconObject = new GameObject("Icon", typeof(RectTransform),
                typeof(Image));
            iconObject.transform.SetParent(obj.transform, false);
            RectTransform iconRect =
                iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.sizeDelta = new Vector2(-8f, -8f);
            iconRect.anchoredPosition = Vector2.zero;
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(
                              "ui/icons/iconRename") ??
                          SpriteTextureLoader.getSprite(
                              "ui/icons/iconDocument") ??
                          SpriteTextureLoader.getSprite(
                              "ui/icons/iconCity");
            icon.preserveAspect = true;

            TipButton tip = obj.GetComponent<TipButton>();
            tip.type = "normal";
            tip.hoverAction = () => Tooltip.show(obj, "normal",
                new TooltipData
                {
                    tip_name = "aw_city_state_rename_entry",
                    tip_description = "aw_open_city_state_rename"
                });
            return obj.GetComponent<Button>();
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

        private static Button BuildLocalCourtButton(Transform pRail)
        {
            var obj = new GameObject(LOCAL_COURT_BTN_NAME,
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            obj.transform.SetParent(pRail, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(SIZE, SIZE);
            rect.localScale = Vector3.one;

            Image background = obj.GetComponent<Image>();
            background.sprite = SpriteTextureLoader.getSprite(
                "ui/special/button");
            background.type = Image.Type.Sliced;

            var iconObject = new GameObject("Icon", typeof(RectTransform),
                typeof(Image));
            iconObject.transform.SetParent(obj.transform, false);
            RectTransform iconRect =
                iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.sizeDelta = new Vector2(-8f, -8f);
            iconRect.anchoredPosition = Vector2.zero;
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(
                              "ui/icons/iconCity") ??
                          SpriteTextureLoader.getSprite(
                              "ui/icons/iconVillages") ??
                          SpriteTextureLoader.getSprite(
                              "ui/icons/iconDocument");
            icon.preserveAspect = true;

            TipButton tip = obj.GetComponent<TipButton>();
            tip.type = "normal";
            tip.hoverAction = () => Tooltip.show(obj, "normal",
                new TooltipData
                {
                    tip_name = "aw_city_local_court_entry",
                    tip_description = "aw_open_city_local_court"
                });
            return obj.GetComponent<Button>();
        }
    }
}
