using System;
using AncientWarfare3.core.lineage;
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
        public static Action<int> OnHeaderToggle; // window 注入:点段头 → toggle 该 reign_index

        private Text _label;
        private int _reignIndex = -1;
        private bool _isHeader;

        public override void Setup(HistoryRow pObject)
        {
            EnsureUi();
            _isHeader = pObject.is_header;
            _reignIndex = pObject.reign_index;

            var bg = gameObject.GetComponent<Image>();
            if (pObject.is_header)
            {
                string arrow = pObject.expanded ? "▼ " : "▶ ";
                _label.text = arrow + pObject.text;
                _label.fontStyle = FontStyle.Bold;
                _label.color = new Color(1f, 0.92f, 0.6f, 1f); // 段头金色
                _label.alignment = TextAnchor.MiddleLeft;
                if (bg != null) bg.color = new Color(0.25f, 0.22f, 0.12f, 0.85f);
            }
            else
            {
                _label.text = (pObject.dim ? "   " : "") + pObject.text;
                _label.fontStyle = FontStyle.Normal;
                _label.color = Color.white;
                _label.alignment = TextAnchor.MiddleLeft;
                if (bg != null) bg.color = new Color(1f, 1f, 1f, 0.30f);
            }
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220, 24);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24; le.preferredHeight = 24;

            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
            bg.type = Image.Type.Sliced;

            // 整行点击(仅段头有效)
            var btn = gameObject.GetComponent<Button>();
            if (btn == null) btn = gameObject.AddComponent<Button>();
            btn.onClick.AddListener(OnClick);

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
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;
        }

        private void OnClick()
        {
            if (_isHeader && _reignIndex >= 0) OnHeaderToggle?.Invoke(_reignIndex);
        }
    }
}
