using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class AncestryRow
    {
        public string text = "";
        public bool is_header;
        public bool dim;
        public string tooltip_title = "";
        public string tooltip_desc = "";
    }

    internal sealed class AncestryListItem : AbstractListWindowItem<AncestryRow>
    {
        private const float ROW_W = 220f;
        private const float CHARS_PER_LINE = 22f;
        private Text _label;
        private LayoutElement _layout;
        private TipButton _tip;

        public override void Setup(AncestryRow pObject)
        {
            EnsureUi();
            _label.text = pObject?.text ?? "";
            _label.fontStyle = pObject != null && pObject.is_header ? FontStyle.Bold : FontStyle.Normal;
            _label.color = pObject != null && pObject.is_header
                ? new Color(1f, 0.7f, 0.3f, 1f)
                : (pObject != null && pObject.dim ? new Color(0.78f, 0.78f, 0.78f, 1f) : Color.white);
            _label.alignment = pObject != null && pObject.is_header ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;

            var bg = gameObject.GetComponent<Image>();
            if (pObject != null && pObject.is_header) AW_UIStyle.ApplyPanel(bg, 0.95f);
            else AW_UIStyle.ApplyListRow(bg, pObject != null && pObject.dim ? 0.78f : 0.9f);

            ApplyRowHeight(pAllowWrap: pObject == null || !pObject.is_header);
            SetTip(pObject?.tooltip_title, pObject?.tooltip_desc);
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ROW_W, 24f);

            _layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            _layout.minHeight = 24f;
            _layout.preferredHeight = 24f;

            var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(bg, 0.9f);

            _tip = gameObject.GetComponent<TipButton>() ?? gameObject.AddComponent<TipButton>();

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.offsetMin = new Vector2(6f, 0f);
            trect.offsetMax = new Vector2(-6f, 0f);

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
            if (trect == null) return;
            bool padded = height > 24.5f;
            trect.offsetMin = new Vector2(6f, padded ? 4f : 0f);
            trect.offsetMax = new Vector2(-6f, padded ? -4f : 0f);
            if (pAllowWrap && padded && _label.alignment == TextAnchor.MiddleLeft)
                _label.alignment = TextAnchor.UpperLeft;
        }

        private static float EstimateHeight(string pText)
        {
            string plain = StripRich(pText ?? "");
            int lines = 0;
            foreach (string part in plain.Split('\n'))
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
    }
}
