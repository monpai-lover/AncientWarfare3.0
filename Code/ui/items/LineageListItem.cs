using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>姓族总览的一行:姓名 + 总/存活/贵族/氏支数。点击进入该姓氏支列表。</summary>
    internal class LineageListItem : AbstractListWindowItem<SurnameOverview>
    {
        private const float ROW_W = 220f;
        private Text _label;
        private Button _button;
        private TipButton _tip;
        private string _familyName;

        public override void Setup(SurnameOverview pObject)
        {
            EnsureUi();
            _familyName = pObject.family_name;
            _label.text =
                $"{pObject.family_name}   总{pObject.total} 活{pObject.alive} 贵{pObject.noble} 氏{pObject.shi_count}" +
                BuildOriginText(pObject);
            SetTip(pObject);
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ROW_W, 28);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 28;
            le.preferredHeight = 28;

            // 背景框(sliced 按钮底,带视觉层次)
            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(bg, 0.95f);

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);
            _tip = gameObject.GetComponent<TipButton>();
            if (_tip == null) _tip = gameObject.AddComponent<TipButton>();

            // 左侧氏族图标
            var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(transform, false);
            var irect = iconObj.GetComponent<RectTransform>();
            irect.anchorMin = new Vector2(0f, 0.5f); irect.anchorMax = new Vector2(0f, 0.5f);
            irect.pivot = new Vector2(0f, 0.5f);
            irect.sizeDelta = new Vector2(22, 22); irect.anchoredPosition = new Vector2(4, 0);
            var icon = iconObj.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconClan");
            icon.preserveAspect = true;

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.offsetMin = new Vector2(30, 0); trect.offsetMax = new Vector2(-4, 0);
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.supportRichText = true;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void SetTip(SurnameOverview pObject)
        {
            if (_tip == null) return;
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            string title = (string.IsNullOrEmpty(pObject.family_name) ? "姓族" : pObject.family_name + "姓");
            string desc = BuildTip(pObject);
            _tip.hoverAction = () =>
                Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }

        private static string BuildOriginText(SurnameOverview pObject)
        {
            if (pObject.created_time <= 0 && string.IsNullOrEmpty(pObject.origin_kingdom_name)) return "";
            string kingdom = string.IsNullOrEmpty(pObject.origin_kingdom_name)
                ? "?国"
                : HistoryText.Colored(pObject.origin_kingdom_name, pObject.origin_kingdom_color).Rich;
            string year = pObject.created_time > 0 ? Date.getYear(pObject.created_time) + "年" : "?年";
            return "   于" + kingdom + " " + year + " 建立";
        }

        private static string BuildTip(SurnameOverview pObject)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("姓:" + Fallback(pObject.family_name));
            sb.AppendLine("始祖:" + Fallback(pObject.founder_name));
            sb.AppendLine("建立国:" + Fallback(pObject.origin_kingdom_name));
            sb.AppendLine("建立城:" + Fallback(pObject.origin_city_name));
            sb.AppendLine("建立时间:" + FormatDate(pObject.created_time));
            sb.AppendLine("存续:" + Duration(pObject.created_time));
            sb.Append("当前:" + pObject.alive + " 人在世，" + pObject.shi_count + " 个氏支");
            return sb.ToString();
        }

        private static string Duration(double pStart)
        {
            if (pStart <= 0) return "?";
            double end = World.world != null ? World.world.getCurWorldTime() : pStart;
            int years = System.Math.Max(1, Date.getYear(end) - Date.getYear(pStart) + 1);
            return "共" + years + "年";
        }

        private static string FormatDate(double pTime)
        {
            return pTime > 0 ? HistoryWriter.FormatDate(pTime) : "?";
        }

        private static string Fallback(string pText)
        {
            return string.IsNullOrEmpty(pText) ? "?" : pText;
        }

        private void OnClick()
        {
            if (string.IsNullOrEmpty(_familyName)) return;
            windows.ShiBranchListWindow.OpenFor(_familyName);
        }
    }
}
