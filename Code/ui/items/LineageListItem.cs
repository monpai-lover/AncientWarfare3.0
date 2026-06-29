using AncientWarfare3.core.lineage;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>姓族总览的一行:姓名 + 总/存活/贵族/氏支数。点击进入该姓氏支列表。</summary>
    internal class LineageListItem : AbstractListWindowItem<SurnameOverview>
    {
        private Text _label;
        private Button _button;
        private string _familyName;

        public override void Setup(SurnameOverview pObject)
        {
            EnsureUi();
            _familyName = pObject.family_name;
            _label.text =
                $"{pObject.family_name}   总{pObject.total} 活{pObject.alive} 贵{pObject.noble} 氏{pObject.shi_count}";
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
            if (string.IsNullOrEmpty(_familyName)) return;
            // Task 4 接好氏支列表窗后改为 windows.ShiBranchListWindow.OpenFor(_familyName)
            ModClass.LogInfo("[AW3] 点击姓:" + _familyName + "(氏支列表待 Task 4)");
        }
    }
}
