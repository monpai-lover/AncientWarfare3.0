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
        ///     年号:克隆 mottoInput(NameInput)放其正下方。复用 motto 的字体/背景框/布局,
        ///     只把文本设为年号、设国家色、禁用编辑。按名查重(刷新只更新文本/颜色/可见性)。
        /// </summary>
        private static void PlaceYearUnderMotto(KingdomWindow pWindow, Kingdom pKingdom)
        {
            NameInput motto = pWindow.mottoInput;
            if (motto == null) return;
            Transform parent = motto.transform.parent;
            if (parent == null) return;

            string yearName = YearNameService.GetYearName(pKingdom);

            Transform existing = parent.Find(YEAR_OBJ);
            NameInput yearInput;
            if (existing != null)
            {
                yearInput = existing.GetComponent<NameInput>();
            }
            else
            {
                // 克隆 motto 控件(连同 InputField + Text 子结构、背景框、布局)。
                var clone = Object.Instantiate(motto.gameObject, parent);
                clone.name = YEAR_OBJ;
                yearInput = clone.GetComponent<NameInput>();

                // 紧跟 motto 之后
                clone.transform.SetSiblingIndex(motto.transform.GetSiblingIndex() + 1);

                // motto 是锚定/绝对定位,克隆体与 motto 同位置 → 会重叠(用户反馈"年号和 motto 叠一起")。
                // 把年号克隆体的 RectTransform 在 motto 基础上**往下偏移一行**(motto 高度 + 间距),落到其下方。
                var mottoRect = motto.GetComponent<RectTransform>();
                var yearRect = clone.GetComponent<RectTransform>();
                if (mottoRect != null && yearRect != null)
                {
                    float drop = mottoRect.rect.height + 4f; // motto 高 + 4 间距
                    yearRect.anchorMin = mottoRect.anchorMin;
                    yearRect.anchorMax = mottoRect.anchorMax;
                    yearRect.pivot = mottoRect.pivot;
                    yearRect.sizeDelta = mottoRect.sizeDelta;
                    yearRect.anchoredPosition = mottoRect.anchoredPosition + new Vector2(0f, -drop);
                }

                // 禁用编辑:年号不可改(只作展示)。
                if (yearInput != null && yearInput.inputField != null)
                {
                    yearInput.inputField.interactable = false;
                    yearInput.inputField.readOnly = true;
                    // 去掉克隆继承来的任何输入监听(克隆体可能带 prefab 上的回调)
                    yearInput.inputField.onValueChanged.RemoveAllListeners();
                    yearInput.inputField.onEndEdit.RemoveAllListeners();
                }
            }

            if (yearInput == null) return;

            if (string.IsNullOrEmpty(yearName))
            {
                yearInput.gameObject.SetActive(false);
                return;
            }

            yearInput.gameObject.SetActive(true);
            yearInput.setText(yearName);
            if (yearInput.textField != null)
                yearInput.textField.color = pKingdom.getColor().getColorText();
        }
    }
}
