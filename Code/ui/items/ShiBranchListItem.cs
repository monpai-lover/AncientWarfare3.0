using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>氏支列表的一行:氏支名 + 总/存活/成立年/贵族数。点击进入该氏支大树。</summary>
    internal class ShiBranchListItem : AbstractListWindowItem<ShiBranchInfo>
    {
        private const float ROW_W = 220f;
        private Text _label;
        private Button _button;
        private TipButton _tip;
        private long _shiId = -1;

        public override void Setup(ShiBranchInfo pObject)
        {
            EnsureUi();
            _shiId = pObject.shi_id;
            int years = Date.getYearsSince(pObject.created_time);
            _label.text =
                $"{pObject.clan_name}   {AW_L10n.Text("aw_total", "\u603B")}{pObject.total} {AW_L10n.Text("aw_alive_short", "\u6D3B")}{pObject.alive} {AW_L10n.Text("aw_established_short", "\u7ACB")}{years}{AW_L10n.Text("aw_year_suffix", "\u5E74")} {AW_L10n.Text("aw_noble_short", "\u8D35")}{pObject.noble}" +
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

            // 背景框
            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(bg, 0.95f);

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);
            _tip = gameObject.GetComponent<TipButton>();
            if (_tip == null) _tip = gameObject.AddComponent<TipButton>();

            // 左侧氏支图标(用氏族图标)
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

        private void SetTip(ShiBranchInfo pObject)
        {
            if (_tip == null) return;
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            string title = string.IsNullOrEmpty(pObject.clan_name) ? AW_L10n.Text("aw_shi_branch", "\u6C0F\u652F") : pObject.clan_name + AW_L10n.Text("aw_shi_suffix", "\u6C0F");
            string desc = BuildTip(pObject);
            _tip.hoverAction = () =>
                Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }

        private static string BuildOriginText(ShiBranchInfo pObject)
        {
            if (pObject.created_time <= 0 && string.IsNullOrEmpty(pObject.origin_kingdom_name)) return "";
            string kingdom = string.IsNullOrEmpty(pObject.origin_kingdom_name)
                ? "?" + AW_L10n.Text("aw_kingdom_suffix", "\u56FD")
                : HistoryText.Colored(pObject.origin_kingdom_name, pObject.origin_kingdom_color).Rich;
            string year = pObject.created_time > 0 ? Date.getYear(pObject.created_time) + AW_L10n.Text("aw_year_suffix", "\u5E74") : "?" + AW_L10n.Text("aw_year_suffix", "\u5E74");
            return "   " + AW_L10n.Text("aw_at_prefix", "\u4E8E") + kingdom + " " + year + " " + AW_L10n.Text("aw_established", "\u5EFA\u7ACB");
        }

        private static string BuildTip(ShiBranchInfo pObject)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(AW_L10n.Text("aw_shi_label", "\u6C0F:") + Fallback(pObject.clan_name));
            sb.AppendLine(AW_L10n.Text("aw_founder_label", "\u521B\u5EFA\u8005:") + Fallback(pObject.founder_name));
            sb.AppendLine(AW_L10n.Text("aw_origin_kingdom_label", "\u521B\u5EFA\u56FD:") + Fallback(pObject.origin_kingdom_name));
            sb.AppendLine(AW_L10n.Text("aw_origin_city_label", "\u521B\u5EFA\u57CE:") + Fallback(pObject.origin_city_name));
            sb.AppendLine(AW_L10n.Text("aw_created_time_label", "\u521B\u5EFA\u65F6\u95F4:") + FormatDate(pObject.created_time));
            sb.AppendLine(AW_L10n.Text("aw_duration_label", "\u5B58\u7EED:") + Duration(pObject.created_time));
            sb.Append(AW_L10n.Text("aw_current_label", "\u5F53\u524D:") + pObject.alive + AW_L10n.Text("aw_alive_people_suffix", " \u4EBA\u5728\u4E16\uFF0C") + pObject.noble + AW_L10n.Text("aw_noble_people_suffix", " \u540D\u8D35\u65CF"));
            return sb.ToString();
        }

        private static string Duration(double pStart)
        {
            if (pStart <= 0) return "?";
            double end = World.world != null ? World.world.getCurWorldTime() : pStart;
            int years = System.Math.Max(1, Date.getYear(end) - Date.getYear(pStart) + 1);
            return AW_L10n.Text("aw_total_prefix", "\u5171") + years + AW_L10n.Text("aw_year_suffix", "\u5E74");
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
            if (_shiId < 0) return;
            windows.FamilyTreeWindow.OpenBigTree(_shiId);
        }
    }
}
