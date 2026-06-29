using AncientWarfare3.core.lineage;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>氏支列表的一行:氏支名 + 总/存活/成立年/贵族数。点击进入该氏支大树。</summary>
    internal class ShiBranchListItem : AbstractListWindowItem<ShiBranchInfo>
    {
        private Text _label;
        private Button _button;
        private long _shiId = -1;

        public override void Setup(ShiBranchInfo pObject)
        {
            EnsureUi();
            _shiId = pObject.shi_id;
            int years = Date.getYearsSince(pObject.created_time);
            _label.text =
                $"{pObject.clan_name}   总{pObject.total} 活{pObject.alive} 立{years}年 贵{pObject.noble}";
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220, 24);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24;
            le.preferredHeight = 24;

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.sizeDelta = Vector2.zero;
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
        }

        private void OnClick()
        {
            if (_shiId < 0) return;
            windows.FamilyTreeWindow.OpenBigTree(_shiId);
        }
    }
}
