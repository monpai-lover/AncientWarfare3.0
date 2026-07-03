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
        private long _cityId = -1;
        private bool _isCityOverview;

        public override void Setup(SurnameOverview pObject)
        {
            EnsureUi();
            _familyName = pObject.family_name;
            _cityId = pObject.city_id;
            _isCityOverview = pObject.is_city_overview;
            _label.text = pObject.is_city_overview ? BuildCityText(pObject) : BuildSurnameText(pObject);
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
            string title = pObject.is_city_overview
                ? PlainCityTitle(pObject)
                : (string.IsNullOrEmpty(pObject.family_name)
                    ? AW_L10n.Text("aw_surname_lineage", "\u59D3\u65CF")
                    : pObject.family_name + AW_L10n.Text("aw_family_suffix", "\u59D3"));
            string desc = BuildTip(pObject);
            _tip.hoverAction = () =>
                Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }

        private static string BuildCityText(SurnameOverview pObject)
        {
            return RichCityTitle(pObject) +
                   "   " + AW_L10n.Text("aw_total", "\u603B") + pObject.total +
                   " " + AW_L10n.Text("aw_alive_short", "\u6D3B") + pObject.alive +
                   " " + AW_L10n.Text("aw_family_short", "\u59D3") + pObject.family_count +
                   " " + AW_L10n.Text("aw_shi_short", "\u6C0F") + pObject.shi_count +
                   " " + AW_L10n.Text("aw_noble_short", "\u8D35") + pObject.noble;
        }

        private static string BuildSurnameText(SurnameOverview pObject)
        {
            return $"{pObject.family_name}   " +
                   AW_L10n.Text("aw_total", "\u603B") + pObject.total +
                   " " + AW_L10n.Text("aw_alive_short", "\u6D3B") + pObject.alive +
                   " " + AW_L10n.Text("aw_noble_short", "\u8D35") + pObject.noble +
                   " " + AW_L10n.Text("aw_shi_short", "\u6C0F") + pObject.shi_count +
                   BuildOriginText(pObject);
        }

        private static string BuildOriginText(SurnameOverview pObject)
        {
            if (pObject.created_time <= 0 && string.IsNullOrEmpty(pObject.origin_kingdom_name)) return "";
            string kingdom = string.IsNullOrEmpty(pObject.origin_kingdom_name)
                ? "?" + AW_L10n.Text("aw_kingdom_suffix", "\u56FD")
                : HistoryText.Colored(pObject.origin_kingdom_name, pObject.origin_kingdom_color).Rich;
            string year = pObject.created_time > 0 ? Date.getYear(pObject.created_time) + AW_L10n.Text("aw_year_suffix", "\u5E74") : "?" + AW_L10n.Text("aw_year_suffix", "\u5E74");
            return "   " + AW_L10n.Text("aw_at_prefix", "\u4E8E") + kingdom + " " + year + " " + AW_L10n.Text("aw_established", "\u5EFA\u7ACB");
        }

        private static string BuildTip(SurnameOverview pObject)
        {
            if (pObject.is_city_overview)
                return BuildCityTip(pObject);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(AW_L10n.Text("aw_family_label", "\u59D3:") + Fallback(pObject.family_name));
            sb.AppendLine(AW_L10n.Text("aw_founder_label", "\u5F00\u521B\u8005:") + Fallback(pObject.founder_name));
            sb.AppendLine(AW_L10n.Text("aw_origin_kingdom_label", "\u521B\u5EFA\u56FD:") + Fallback(pObject.origin_kingdom_name));
            sb.AppendLine(AW_L10n.Text("aw_origin_city_label", "\u521B\u5EFA\u57CE:") + Fallback(pObject.origin_city_name));
            sb.AppendLine(AW_L10n.Text("aw_created_time_label", "\u521B\u5EFA\u65F6\u95F4:") + FormatDate(pObject.created_time));
            sb.AppendLine(AW_L10n.Text("aw_duration_label", "\u5B58\u7EED:") + Duration(pObject.created_time));
            sb.Append(AW_L10n.Text("aw_current_label", "\u5F53\u524D:") + pObject.alive +
                      AW_L10n.Text("aw_alive_people_suffix", " \u4EBA\u5728\u4E16\uFF0C") + pObject.shi_count +
                      AW_L10n.Text("aw_branch_count_suffix", " \u4E2A\u6C0F\u652F"));
            return sb.ToString();
        }

        private static string BuildCityTip(SurnameOverview pObject)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(AW_L10n.Text("aw_kingdom", "\u738B\u56FD") + ":" + Fallback(KingdomNameWithSuffix(pObject)));
            sb.AppendLine(AW_L10n.Text("aw_city_label", "\u57CE\u5E02:") + Fallback(pObject.city_name));
            sb.AppendLine(AW_L10n.Text("aw_family_label", "\u59D3:") + pObject.family_count);
            sb.AppendLine(AW_L10n.Text("aw_shi_label", "\u6C0F:") + pObject.shi_count);
            sb.AppendLine(AW_L10n.Text("aw_earliest_record_label", "\u6700\u65E9\u8BB0\u5F55:") + FormatDate(pObject.earliest_time));
            sb.Append(AW_L10n.Text("aw_current_label", "\u5F53\u524D:") + pObject.alive +
                      AW_L10n.Text("aw_alive_people_suffix", " \u4EBA\u5728\u4E16\uFF0C") + pObject.noble +
                      AW_L10n.Text("aw_noble_people_suffix", " \u540D\u8D35\u65CF"));
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

        private static string RichCityTitle(SurnameOverview pObject)
        {
            string kingdom = KingdomNameWithSuffix(pObject);
            string richKingdom = HistoryText.Colored(kingdom, pObject.city_kingdom_color).Rich;
            return richKingdom + " " + HistoryColors.EscapeRich(Fallback(pObject.city_name));
        }

        private static string PlainCityTitle(SurnameOverview pObject)
        {
            return KingdomNameWithSuffix(pObject) + " " + Fallback(pObject.city_name);
        }

        private static string KingdomNameWithSuffix(SurnameOverview pObject)
        {
            string kingdom = Fallback(pObject.city_kingdom_name);
            return kingdom + AW_L10n.Text("aw_kingdom_suffix", "\u56FD");
        }

        private void OnClick()
        {
            if (_isCityOverview)
            {
                if (_cityId >= 0) windows.ShiBranchListWindow.OpenForCity(_cityId);
                return;
            }
            if (string.IsNullOrEmpty(_familyName)) return;
            windows.ShiBranchListWindow.OpenFor(_familyName);
        }
    }
}
