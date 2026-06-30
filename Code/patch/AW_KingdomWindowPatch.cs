using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     KingdomWindow 注入:
    ///     - **年号**:仿照原版 motto(mottoInput=NameInput 预制控件)—— 克隆 mottoInput 放其正下方,
    ///       只读、国家色、文本=年号。挂在 showTopPartInformation Postfix(motto setText 的真实时机,
    ///       此前用 showStatsRows + 手搓 GameObject 不生效)。
    ///     - 头衔 / 继承人:走 showStatsRows Postfix 注 stats 行(label 经本地化键,value 原样中文)。
    ///     kingdom 取 SelectedMetas.selected_kingdom。
    /// </summary>
    [HarmonyPatch]
    public static class AW_KingdomWindowPatch
    {
        private const string YEAR_OBJ = "AW_YearNameInput";

        // ── 年号:仿 motto,克隆 mottoInput 放其下方 ──
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), "showTopPartInformation")]
        public static void ShowTopPartInformation_Postfix(KingdomWindow __instance)
        {
            var kingdom = SelectedMetas.selected_kingdom;
            if (kingdom == null || kingdom.data == null) return;
            PlaceYearUnderMotto(__instance, kingdom);
        }

        // ── 头衔 / 继承人:stats 行 ──
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(KingdomWindow __instance)
        {
            var kingdom = SelectedMetas.selected_kingdom;
            if (kingdom == null || kingdom.data == null) return;

            // 头衔
            string title = KingdomTitleService.GetTitleString(KingdomTitleService.GetTitle(kingdom));
            if (!string.IsNullOrEmpty(title))
                __instance.showStatRow("aw_kingdom_title", title);

            // 继承人(可点击 → 选中并 inspect)
            var heir = HeirService.GetHeir(kingdom);
            if (heir != null && !heir.isRekt())
            {
                var kvf = __instance.showStatRow("aw_heir", heir.getName());
                if (kvf != null)
                {
                    var h = heir;
                    kvf.on_click_value = () =>
                    {
                        SelectedUnit.makeMainSelected(h);
                        ScrollWindow.showWindow("unit");
                    };
                }
            }
        }

        /// <summary>
        ///     年号:克隆 motto 的 **textField(那个纯 Text 子物体)** 放其正下方。
        ///     不克隆整个 NameInput(含 InputField/Mask,绝对定位+裁剪导致之前年号跑偏/消失);
        ///     textField 是已正确定位的 Text,克隆它、做兄弟、localPosition.y 下移一行最稳。
        ///     每次刷新重设位置/文本/颜色;无年号则隐藏。
        /// </summary>
        private static void PlaceYearUnderMotto(KingdomWindow pWindow, Kingdom pKingdom)
        {
            NameInput motto = pWindow.mottoInput;
            if (motto == null || motto.textField == null) return;
            Transform mottoTextTr = motto.textField.transform;
            Transform parent = mottoTextTr.parent;
            if (parent == null) return;

            string yearName = YearNameService.GetYearName(pKingdom);

            Transform existing = parent.Find(YEAR_OBJ);
            Text yearText;
            if (existing != null)
            {
                yearText = existing.GetComponent<Text>();
            }
            else
            {
                // 克隆 motto 的 textField(纯 Text,布局/字体/对齐天然继承)
                var clone = Object.Instantiate(motto.textField.gameObject, parent);
                clone.name = YEAR_OBJ;
                yearText = clone.GetComponent<Text>();
                // 去掉克隆可能带的 LocalizedText 组件(否则会把年号当本地化键)
                var loc = clone.GetComponent<LocalizedText>();
                if (loc != null) Object.Destroy(loc);
            }

            if (yearText == null) return;

            // 位置:每次刷新重设。复制 motto textField 的 RectTransform,localPosition.y 下移一行。
            var mottoRect = motto.textField.GetComponent<RectTransform>();
            var yearRect = yearText.GetComponent<RectTransform>();
            if (mottoRect != null && yearRect != null)
            {
                yearRect.anchorMin = mottoRect.anchorMin;
                yearRect.anchorMax = mottoRect.anchorMax;
                yearRect.pivot = mottoRect.pivot;
                yearRect.sizeDelta = mottoRect.sizeDelta;
                float drop = mottoRect.rect.height;
                if (drop < 12f) drop = 18f;
                Vector3 lp = mottoRect.localPosition;
                lp.y -= drop + 2f;
                yearRect.localPosition = lp;
            }

            if (string.IsNullOrEmpty(yearName))
            {
                yearText.gameObject.SetActive(false);
                return;
            }

            yearText.gameObject.SetActive(true);
            yearText.text = yearName;
            yearText.color = pKingdom.getColor().getColorText();
        }
    }
}
