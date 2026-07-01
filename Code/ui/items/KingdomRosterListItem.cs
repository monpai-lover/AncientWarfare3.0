using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     全王国列表一行:旗帜(存档重建,亡国也能画)+ 国名(国家色)+ 存活/亡国标记。
    ///     点击 → 打开该国朝代分段历史(HistoryListWindow.OpenKingdom)。
    /// </summary>
    internal class KingdomRosterListItem : AbstractListWindowItem<KingdomArchiveInfo>
    {
        private const float ROW_W = 220f;
        private Image _flagBg;
        private Image _flagIcon;
        private Text _label;
        private Button _button;
        private TipButton _tip;
        private long _kingdomId = -1;

        public override void Setup(KingdomArchiveInfo pObject)
        {
            EnsureUi();
            _kingdomId = pObject.kingdom_id;

            // 旗帜:存档值重建(不引用活 Kingdom,亡国安全)。
            KingdomFlagBuilder.Build(pObject.banner_id, pObject.banner_icon_id, pObject.banner_background_id,
                pObject.color_text, pObject.color_id, _flagBg, _flagIcon);

            // 国名(国家色)+ 亡国标记。
            string mark = pObject.is_alive ? "" : "  " + AW_L10n.Text("aw_dead_kingdom_mark", "[\u5DF2\u4EA1]");
            _label.text = (string.IsNullOrEmpty(pObject.kingdom_name) ? "?" : pObject.kingdom_name) +
                          mark + "  " + BuildSpanText(pObject);
            var color = KingdomFlagBuilder.ResolveColor(pObject.color_text, pObject.color_id);
            _label.color = color != null ? color.getColorText() : Color.white;
            if (!pObject.is_alive) _label.color *= new Color(0.7f, 0.7f, 0.7f, 1f); // 亡国名字偏暗
            SetTip(pObject);
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ROW_W, 30);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 30; le.preferredHeight = 30;

            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(bg, 0.95f);

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);
            _tip = gameObject.GetComponent<TipButton>();
            if (_tip == null) _tip = gameObject.AddComponent<TipButton>();

            // 左侧旗帜:背景 Image + 其上图标 Image(对齐原版 KingdomBanner 两层)。
            var flagObj = new GameObject("Flag", typeof(RectTransform), typeof(Image));
            flagObj.transform.SetParent(transform, false);
            var frect = flagObj.GetComponent<RectTransform>();
            frect.anchorMin = new Vector2(0f, 0.5f); frect.anchorMax = new Vector2(0f, 0.5f);
            frect.pivot = new Vector2(0f, 0.5f);
            frect.sizeDelta = new Vector2(24, 24); frect.anchoredPosition = new Vector2(4, 0);
            _flagBg = flagObj.GetComponent<Image>();
            _flagBg.preserveAspect = true;

            var iconObj = new GameObject("FlagIcon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(flagObj.transform, false);
            var irect = iconObj.GetComponent<RectTransform>();
            irect.anchorMin = Vector2.zero; irect.anchorMax = Vector2.one;
            irect.offsetMin = Vector2.zero; irect.offsetMax = Vector2.zero;
            _flagIcon = iconObj.GetComponent<Image>();
            _flagIcon.preserveAspect = true;
            _flagIcon.raycastTarget = false;

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero; trect.anchorMax = Vector2.one;
            trect.offsetMin = new Vector2(34, 0); trect.offsetMax = new Vector2(-4, 0);
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void SetTip(KingdomArchiveInfo pObject)
        {
            if (_tip == null) return;
            string title = string.IsNullOrEmpty(pObject.kingdom_name) ? AW_L10n.Text("aw_kingdom", "\u738B\u56FD") : pObject.kingdom_name;
            string desc = BuildTip(pObject);
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            _tip.hoverAction = () =>
                Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }

        private static string BuildTip(KingdomArchiveInfo pObject)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(AW_L10n.Text("aw_founder_label", "\u5F00\u521B\u8005:") + Fallback(pObject.founder_name));
            sb.AppendLine(AW_L10n.Text("aw_capital_label", "\u9996\u90FD:") + Fallback(pObject.capital_city_name));
            sb.AppendLine(AW_L10n.Text("aw_founded_label", "\u5EFA\u7ACB:") + FormatDate(pObject.founded_time));
            sb.AppendLine(AW_L10n.Text("aw_destroyed_label", "\u706D\u4EA1:") + (pObject.is_alive ? AW_L10n.Text("aw_until_now", "\u81F3\u4ECA") : FormatDate(pObject.destroyed_time)));
            sb.AppendLine(AW_L10n.Text("aw_duration_label", "\u5B58\u7EED:") + BuildDurationText(pObject));
            sb.Append(AW_L10n.Text("aw_status_label", "\u72B6\u6001:") + (pObject.is_alive ? AW_L10n.Text("aw_surviving", "\u5B58\u7EED") : AW_L10n.Text("aw_extinct", "\u5DF2\u4EA1")));
            return sb.ToString();
        }

        private static string BuildSpanText(KingdomArchiveInfo pObject)
        {
            int start = SafeYear(pObject.founded_time);
            string end = pObject.is_alive ? AW_L10n.Text("aw_until_now", "\u81F3\u4ECA") : SafeYear(pObject.destroyed_time).ToString();
            return start + "-" + end + " " + BuildDurationText(pObject);
        }

        private static string BuildDurationText(KingdomArchiveInfo pObject)
        {
            double endTime = pObject.is_alive
                ? (World.world != null ? World.world.getCurWorldTime() : pObject.founded_time)
                : pObject.destroyed_time;
            int years = System.Math.Max(1, SafeYear(endTime) - SafeYear(pObject.founded_time) + 1);
            return AW_L10n.Text("aw_total_prefix", "\u5171") + years + AW_L10n.Text("aw_year_suffix", "\u5E74");
        }

        private static int SafeYear(double pTime)
        {
            return pTime > 0 ? Date.getYear(pTime) : 0;
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
            if (_kingdomId < 0) return;
            windows.HistoryListWindow.OpenKingdom(_kingdomId);
        }
    }
}
