using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     在 unit 信息窗右侧竖排按钮栏(WindowMetaTabButtonsContainer = scroll_window.tabs)加一个
    ///     "查看家族树"按钮(用户要求从 stats 行挪到侧栏)。
    ///     Postfix UnitWindow.showMainInfo(每次开窗刷新时跑),仅 Xia 有谱系者显示;同名按钮已存在则只更新点击目标。
    ///     用纯 GameObject+Image+Button+TipButton 挂到 tabs.transform(不走 WindowMetaTab 内容切换机制,
    ///     纯动作按钮:点→开本人家族树)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_UnitTabPatch
    {
        private const string BTN_NAME = "AW_FamilyTreeTabButton";
        private const int SIZE = 40;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitWindow), nameof(UnitWindow.showMainInfo))]
        public static void ShowMainInfo_Postfix(UnitWindow __instance)
        {
            var tabs = __instance.tabs;
            if (tabs == null) return;
            Transform rail = tabs.transform;

            var actor = __instance.actor;
            bool show = actor != null && actor.data != null && LineageService.IsXia(actor) && HasLineage(actor);

            Transform existing = rail.Find(BTN_NAME);
            if (!show)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            long centerId = actor.data.id;
            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);

            Button btn;
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                btn = existing.GetComponent<Button>();
            }
            else
            {
                btn = BuildButton(rail);
            }

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => FamilyTreeWindow.OpenFamilyTree(centerId, shiId));
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

            // 图标(氏族图标)
            var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(obj.transform, false);
            var irect = iconObj.GetComponent<RectTransform>();
            irect.anchorMin = Vector2.zero; irect.anchorMax = Vector2.one;
            irect.sizeDelta = new Vector2(-8, -8); irect.anchoredPosition = Vector2.zero;
            var icon = iconObj.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconClan");
            icon.preserveAspect = true;

            // tooltip(用本地化键,避免中文被当键查刷 missing text。键已注册在 Locales/others.csv)
            var tip = obj.GetComponent<TipButton>();
            tip.type = "normal";
            tip.hoverAction = () => Tooltip.show(obj, "normal",
                new TooltipData { tip_name = "aw_family_tree_entry", tip_description = "aw_view_family_tree" });

            return obj.GetComponent<Button>();
        }

        private static bool HasLineage(Actor pActor)
        {
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
            return lid >= 0;
        }
    }
}
