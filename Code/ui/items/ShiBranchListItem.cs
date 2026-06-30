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
            rect.sizeDelta = new Vector2(220, 28);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 28;
            le.preferredHeight = 28;

            // 背景框
            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/buttonRed");
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.5f);

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);

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
        }

        private void OnClick()
        {
            if (_shiId < 0) return;
            windows.FamilyTreeWindow.OpenBigTree(_shiId);
        }
    }
}
