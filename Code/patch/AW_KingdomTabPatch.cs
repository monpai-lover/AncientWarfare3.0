using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_KingdomTabPatch
    {
        private const string HISTORY_BTN_NAME = "AW_KingdomHistoryTabButton";
        private const string ATLAS_BTN_NAME = "AW_KingdomAtlasButton";
        private const string VASSAL_BTN_NAME = "AW_KingdomVassalTabButton";
        private const string WAR_BTN_NAME = "AW_KingdomWarTargetTabButton";
        private const int SIZE = 40;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(KingdomWindow __instance)
        {
            if (__instance == null) return;
            Kingdom kingdom = __instance.meta_object;
            if (kingdom?.data == null) return;

            Transform rail = __instance.transform.Find("Tabs Right");
            if (rail == null) return;

            Button historyBtn = EnsureButton(rail, HISTORY_BTN_NAME, "ui/icons/iconKingdomList",
                "aw_kingdom_history_entry", "aw_view_kingdom_history");
            historyBtn.onClick.RemoveAllListeners();
            historyBtn.onClick.AddListener(() =>
            {
                Kingdom current = __instance != null ? __instance.meta_object : null;
                if (current?.data == null || current.isRekt()) return;
                AncientWarfare3.ui.windows.HistoryListWindow.OpenKingdom(current.id);
            });

            Button atlasBtn = EnsureButton(rail, ATLAS_BTN_NAME,
                "ui/icons/iconKingdomList", "Kingdom atlas",
                "Historical territory changes; generated from saved events.");
            atlasBtn.transform.SetSiblingIndex(historyBtn.transform.GetSiblingIndex() + 1);
            atlasBtn.onClick.RemoveAllListeners();
            atlasBtn.onClick.AddListener(() =>
            {
                Kingdom current = __instance != null ? __instance.meta_object : null;
                if (current?.data == null || current.isRekt()) return;
                AncientWarfare3.ui.windows.KingdomAtlasWindow.Open(current.id);
            });

            Button vassalBtn = EnsureButton(rail, VASSAL_BTN_NAME, "ui/wars/war_vassal",
                "aw_vassal_relations", "aw_view_vassal_relations");
            vassalBtn.onClick.RemoveAllListeners();
            vassalBtn.onClick.AddListener(() =>
            {
                Kingdom current = __instance != null ? __instance.meta_object : null;
                if (current?.data == null || current.isRekt()) return;
                AncientWarfare3.ui.windows.VassalRelationWindow.Open(current.id);
            });

            Button warBtn = EnsureButton(rail, WAR_BTN_NAME, "ui/wars/war_reclaim",
                "aw_war_targets", "aw_view_war_targets");
            warBtn.onClick.RemoveAllListeners();
            warBtn.onClick.AddListener(() =>
            {
                Kingdom current = __instance != null ? __instance.meta_object : null;
                if (current?.data == null || current.isRekt()) return;
                AncientWarfare3.ui.windows.WarDecisionTargetWindow.Open(current.id);
            });
        }

        private static Button EnsureButton(Transform pRail, string pName, string pIconPath,
            string pTipName, string pTipDesc)
        {
            Transform existing = pRail.Find(pName);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return existing.GetComponent<Button>() ?? existing.gameObject.AddComponent<Button>();
            }

            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
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
            irect.anchorMin = Vector2.zero;
            irect.anchorMax = Vector2.one;
            irect.sizeDelta = new Vector2(-8, -8);
            irect.anchoredPosition = Vector2.zero;
            var icon = iconObj.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(pIconPath)
                          ?? SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                          ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias")
                          ?? SpriteTextureLoader.getSprite("ui/icons/iconClan");
            icon.preserveAspect = true;

            var tip = obj.GetComponent<TipButton>();
            tip.type = "normal";
            tip.hoverAction = () => Tooltip.show(obj, "normal",
                new TooltipData { tip_name = pTipName, tip_description = pTipDesc });

            return obj.GetComponent<Button>();
        }
    }
}
