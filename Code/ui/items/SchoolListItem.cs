using System;
using AncientWarfare3.core.court;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolListItem : MonoBehaviour
    {
        public const float Height = 36f;
        private Image _background;
        private Image _stripe;
        private Image _icon;
        private Text _name;
        private Text _metrics;
        private Button _button;

        public static SchoolListItem Create(Transform pParent)
        {
            var obj = new GameObject("SchoolListItem", typeof(RectTransform), typeof(Image),
                typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(0f, Height);
            SchoolListItem item = obj.AddComponent<SchoolListItem>();
            item.Build();
            return item;
        }

        public void Bind(CourtSchoolDefinition pDefinition, int pMembers, int pCities,
            float pInfluence, bool pSelected, Action<string> pOnClick)
        {
            if (pDefinition == null) return;
            _background.color = pSelected
                ? new Color(.30f, .25f, .16f, .98f)
                : new Color(.09f, .08f, .065f, .94f);
            _stripe.color = Parse(pDefinition.ColorHex, Color.gray);
            _icon.sprite = SpriteTextureLoader.getSprite(pDefinition.IconPath);
            _icon.enabled = _icon.sprite != null;
            _name.text = AW_L10n.Text(pDefinition.NameKey, pDefinition.Id);
            _metrics.text = AW_L10n.Text("aw_school_members_short", "People") + " " + pMembers +
                            "  " + AW_L10n.Text("aw_school_cities_short", "Cities") + " " + pCities +
                            "  " + Mathf.RoundToInt(pInfluence);
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => pOnClick?.Invoke(pDefinition.Id));
        }

        private void Build()
        {
            _background = GetComponent<Image>();
            _button = GetComponent<Button>();

            _stripe = ChildImage("Color", new Vector2(0f, 1f), new Vector2(5f, Height), Vector2.zero);
            _icon = ChildImage("Icon", new Vector2(0f, 1f), new Vector2(28f, 28f), new Vector2(9f, -4f));
            _icon.preserveAspect = true;
            _name = ChildText("Name", new Vector2(42f, -3f), new Vector2(150f, 15f), 10,
                TextAnchor.UpperLeft);
            _metrics = ChildText("Metrics", new Vector2(42f, -19f), new Vector2(164f, 13f), 8,
                TextAnchor.UpperLeft);
            _metrics.color = new Color(.78f, .74f, .65f, 1f);
        }

        private Image ChildImage(string pName, Vector2 pAnchor, Vector2 pSize, Vector2 pPosition)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = pAnchor;
            rect.anchorMax = pAnchor;
            rect.pivot = pAnchor;
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPosition;
            Image image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private Text ChildText(string pName, Vector2 pPosition, Vector2 pSize, int pFontSize,
            TextAnchor pAlignment)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAlignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        private static Color Parse(string pHex, Color pFallback)
        {
            return ColorUtility.TryParseHtmlString(pHex, out Color color) ? color : pFallback;
        }
    }
}
