using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_UnitTabPatch
    {
        private const string BTN_NAME = "AW_FamilyTreeTabButton";
        private const string BIO_BTN_NAME = "AW_BiographyTabButton";
        private const string ANCESTRY_BTN_NAME = "AW_AncestryTabButton";
        private const int SIZE = 40;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitWindow), nameof(UnitWindow.showMainInfo))]
        public static void ShowMainInfo_Postfix(UnitWindow __instance)
        {
            Transform rail = __instance.transform.Find("Tabs Right")
                             ?? (__instance.tabs != null ? __instance.tabs.transform : null);
            if (rail == null) return;

            Actor actor = __instance.actor;
            bool hasActor = actor?.data != null;
            bool showFamily = hasActor && LineageService.HasTraceableFamily(actor);
            bool showBio = showFamily;
            bool showAncestry = hasActor && AncestryAnalysisService.HasAnalyzableAncestry(actor);

            SetButtonActive(rail, BTN_NAME, showFamily);
            SetButtonActive(rail, BIO_BTN_NAME, showBio);
            SetButtonActive(rail, ANCESTRY_BTN_NAME, showAncestry);
            if (!hasActor) return;

            long centerId = actor.data.id;

            if (showFamily)
            {
                actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                LineageService.EnsureOriginalClanArchived(actor);
                Button familyBtn = GetOrCreateButton(rail, BTN_NAME, BuildFamilyButton);
                familyBtn.onClick.RemoveAllListeners();
                familyBtn.onClick.AddListener(() => FamilyTreeWindow.OpenFamilyTree(centerId, shiId));
            }

            if (showBio)
            {
                Button bioBtn = GetOrCreateButton(rail, BIO_BTN_NAME, BuildBioButton);
                bioBtn.onClick.RemoveAllListeners();
                bioBtn.onClick.AddListener(() => HistoryListWindow.OpenPerson(centerId));
            }

            if (showAncestry)
            {
                Button ancestryBtn = GetOrCreateButton(rail, ANCESTRY_BTN_NAME, BuildAncestryButton);
                ancestryBtn.onClick.RemoveAllListeners();
                ancestryBtn.onClick.AddListener(() => AncestryAnalysisWindow.Open(centerId));
            }
        }

        private static Button GetOrCreateButton(Transform pRail, string pName, System.Func<Transform, Button> pBuilder)
        {
            Transform existing = pRail.Find(pName);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return existing.GetComponent<Button>();
            }

            return pBuilder(pRail);
        }

        private static void SetButtonActive(Transform pRail, string pName, bool pActive)
        {
            Transform existing = pRail.Find(pName);
            if (existing != null) existing.gameObject.SetActive(pActive);
        }

        private static Button BuildFamilyButton(Transform pRail)
        {
            return BuildIconButton(
                pRail,
                BTN_NAME,
                SpriteTextureLoader.getSprite("ui/Icons/icon_family_tree")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconClan"),
                "aw_family_tree_entry",
                "aw_view_family_tree");
        }

        private static Button BuildBioButton(Transform pRail)
        {
            return BuildIconButton(
                pRail,
                BIO_BTN_NAME,
                SpriteTextureLoader.getSprite("ui/icons/iconDocument")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconClan"),
                "aw_biography_entry",
                "aw_view_biography");
        }

        private static Button BuildAncestryButton(Transform pRail)
        {
            return BuildIconButton(
                pRail,
                ANCESTRY_BTN_NAME,
                SpriteTextureLoader.getSprite("ui/icons/iconFamily")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconClan")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias"),
                "aw_ancestry_entry",
                "aw_view_ancestry");
        }

        private static Button BuildIconButton(Transform pRail, string pName, Sprite pIcon,
            string pTipName, string pTipDescription)
        {
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
            icon.sprite = pIcon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var tip = obj.GetComponent<TipButton>();
            tip.type = "normal";
            tip.hoverAction = () => Tooltip.show(obj, "normal",
                new TooltipData { tip_name = pTipName, tip_description = pTipDescription });

            return obj.GetComponent<Button>();
        }
    }
}
