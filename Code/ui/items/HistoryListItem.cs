using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     编年史一行:两态——
    ///     - 朝代段头(is_header):可点击折叠/展开,左侧 +/− ,标题=年号(纪年)/无王时间区间。
    ///     - 事件行:年份(年号+纪年)在前 + 内容在后。
    ///     段头点击回调由 window 注入(toggle 对应 reign 后 Rebuild)。
    /// </summary>
    internal class HistoryListItem : AbstractListWindowItem<HistoryRow>
    {
        private const float ROW_W = 220f;
        private const float CHARS_PER_LINE = 22f;

        public static Action<int>    OnHeaderToggle;  // window 注入:点王段段头 → toggle reign_index
        public static Action<int>    OnDynastyToggle; // window 注入:点朝代段头 → toggle dynasty_index
        public static Action<string> OnFilterToggle;  // window 注入:点分类按钮 → toggle category
        public static Action<long>   OnActorBiography; // window 注入:点操作行 → 打开人物传记

        private Text _label;
        private LayoutElement _layout;
        private TipButton _tip;
        private int  _reignIndex   = -1;
        private int  _dynastyIndex = -1;
        private long _actionActorId = -1;
        private string _targetType = "";
        private long _targetId = -1;
        private bool _isHeader;
        private bool _isFilter;
        private bool _isAction;

        public override void Setup(HistoryRow pObject)
        {
            EnsureUi();
            _isHeader   = pObject.is_header;
            _isFilter   = pObject.is_filter;
            _isAction   = pObject.is_action;
            _reignIndex   = pObject.reign_index;
            _dynastyIndex = pObject.dynasty_index;
            _actionActorId = pObject.action_actor_id;
            _targetType = pObject.target_type ?? "";
            _targetId = pObject.target_id;
            SetTip(pObject.tooltip_title, pObject.tooltip_desc);

            var bg = gameObject.GetComponent<Image>();

            if (pObject.is_filter)
            {
                _label.text = pObject.text;
                _label.fontStyle = FontStyle.Normal;
                _label.color = Color.white;
                _label.alignment = TextAnchor.MiddleCenter;
                AW_UIStyle.ApplyButton(bg, 0.92f);
                ApplyRowHeight(false);
                return;
            }

            if (pObject.is_action)
            {
                _label.text = pObject.text;
                _label.fontStyle = FontStyle.Bold;
                _label.color = Color.white;
                _label.alignment = TextAnchor.MiddleCenter;
                AW_UIStyle.ApplyButton(bg, 0.95f);
                ApplyRowHeight(false);
                return;
            }

            if (pObject.is_header)
            {
                bool isDynasty = pObject.dynasty_index >= 0;
                string arrow = pObject.expanded ? "▼ " : "▶ ";
                // 朝代段头缩进=0，王段段头缩进1个空格
                string indent = isDynasty ? "" : "  ";
                _label.text = indent + arrow + pObject.text;
                _label.fontStyle = FontStyle.Bold;
                // 朝代段头橙色，王段段头金色
                _label.color = isDynasty
                    ? new Color(1f, 0.7f, 0.3f, 1f)
                    : new Color(1f, 0.92f, 0.6f, 1f);
                _label.alignment = TextAnchor.MiddleLeft;
                AW_UIStyle.ApplyPanel(bg, isDynasty ? 0.95f : 0.85f);
                ApplyRowHeight(true);
            }
            else
            {
                // 事件行：dim = 王段内事件（多缩进）
                string indent = pObject.dim ? "      " : "";
                _label.text = indent + pObject.text;
                _label.fontStyle = FontStyle.Normal;
                _label.color = Color.white;
                _label.alignment = TextAnchor.MiddleLeft;
                AW_UIStyle.ApplyListRow(bg, pObject.dim ? 0.78f : 0.9f);
                ApplyRowHeight(true);
            }
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ROW_W, 24);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            _layout = le;
            le.minHeight = 24; le.preferredHeight = 24;

            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(bg, 0.9f);

            var btn = gameObject.GetComponent<Button>();
            if (btn == null) btn = gameObject.AddComponent<Button>();
            btn.onClick.AddListener(OnClick);

            _tip = gameObject.GetComponent<TipButton>();
            if (_tip == null) _tip = gameObject.AddComponent<TipButton>();

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero; trect.anchorMax = Vector2.one;
            trect.offsetMin = new Vector2(6, 0); trect.offsetMax = new Vector2(-6, 0);
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 10;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.supportRichText = true;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;
        }

        private void ApplyRowHeight(bool pAllowWrap)
        {
            float height = pAllowWrap ? EstimateHeight(_label.text) : 24f;
            var rect = gameObject.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(ROW_W, height);
            if (_layout != null)
            {
                _layout.minHeight = height;
                _layout.preferredHeight = height;
            }

            var trect = _label.GetComponent<RectTransform>();
            if (trect != null)
            {
                bool padded = height > 24.5f;
                trect.offsetMin = new Vector2(6, padded ? 4 : 0);
                trect.offsetMax = new Vector2(-6, padded ? -4 : 0);
            }
            if (pAllowWrap && height > 24.5f && _label.alignment == TextAnchor.MiddleLeft)
                _label.alignment = TextAnchor.UpperLeft;
        }

        private static float EstimateHeight(string pText)
        {
            string plain = StripRich(pText ?? "");
            int lines = 0;
            string[] parts = plain.Split('\n');
            foreach (string part in parts)
            {
                int len = string.IsNullOrEmpty(part) ? 1 : part.Length;
                lines += Mathf.Max(1, Mathf.CeilToInt(len / CHARS_PER_LINE));
            }
            return Mathf.Max(24f, lines * 14f + 8f);
        }

        private static string StripRich(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return "";
            var sb = new System.Text.StringBuilder(pText.Length);
            bool inTag = false;
            foreach (char c in pText)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        private void OnClick()
        {
            if (_isFilter) { OnFilterToggle?.Invoke(ExtractClickedCategory()); return; }
            if (_isAction) { OnActorBiography?.Invoke(_actionActorId); return; }
            if (_isHeader && _dynastyIndex >= 0) { OnDynastyToggle?.Invoke(_dynastyIndex); return; }
            if (_isHeader && _reignIndex >= 0)   { OnHeaderToggle?.Invoke(_reignIndex); }
            if (!_isHeader) JumpTarget();
        }

        private void JumpTarget()
        {
            if (_targetId < 0 || string.IsNullOrEmpty(_targetType)) return;
            if (_targetType == "actor")
            {
                Actor actor = World.world?.units?.get(_targetId);
                if (actor != null && !actor.isRekt()) ActionLibrary.openUnitWindow(actor);
                return;
            }
            if (_targetType == "kingdom")
            {
                Kingdom kingdom = World.world?.kingdoms?.get(_targetId);
                if (kingdom != null && !kingdom.isRekt()) MetaType.Kingdom.getAsset().selectAndInspect(kingdom);
                return;
            }
            if (_targetType == "city")
            {
                City city = World.world?.cities?.get(_targetId);
                if (city != null && !city.isRekt()) MetaType.City.getAsset().selectAndInspect(city);
            }
        }

        private void SetTip(string pTitle, string pDesc)
        {
            if (_tip == null) return;
            string title = pTitle ?? "";
            string desc = pDesc ?? "";
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(desc))
            {
                _tip.enabled = false;
                _tip.hoverAction = null;
                return;
            }
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            _tip.hoverAction = () =>
                Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }

        // 从文本 "全部 人生 [荣耀] 氏族..." 中，根据点击位置找到被点的分类。
        // 因没有像素定位，简化为：直接把整行 text 传给 OnFilterToggle，由 window 解析。
        // 这里传""触发 window 用自己的状态来 cycling，实际工程做法：传文本让 window 匹配。
        private string ExtractClickedCategory()
        {
            // 筛选条整行点击：window 在 OnFilterToggle 里用 cycling 切下一个分类。
            return "__cycle__";
        }
    }
}
