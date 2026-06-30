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
            rect.sizeDelta = new Vector2(220, 28);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 28;
            le.preferredHeight = 28;

            // 背景框(sliced 按钮底,带视觉层次)
            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/buttonRed");
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.5f);

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);

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
        }

        private void OnClick()
        {
            if (string.IsNullOrEmpty(_familyName)) return;
            windows.ShiBranchListWindow.OpenFor(_familyName);
        }
    }
}
