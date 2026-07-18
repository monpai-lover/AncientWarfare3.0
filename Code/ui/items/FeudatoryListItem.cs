using System;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class FeudatoryListItem : MonoBehaviour
    {
        public const float Height = 52f;
        private Image _background;
        private Image _stripe;
        private Text _name;
        private Text _detail;
        private Button _button;
        private long _feudatoryId = -1L;
        private Action<long> _select;

        public static FeudatoryListItem Create(Transform pParent)
        {
            var obj = new GameObject("FeudatoryListItem", typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            var item = obj.AddComponent<FeudatoryListItem>();
            item.Build();
            return item;
        }

        public void Bind(FeudatorySnapshot pSnapshot, bool pSelected,
            Action<long> pSelect)
        {
            if (pSnapshot == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _feudatoryId = pSnapshot.FeudatoryId;
            _select = pSelect;
            _name.text = string.IsNullOrEmpty(pSnapshot.FeudatoryName)
                ? pSnapshot.SeatName
                : pSnapshot.FeudatoryName;
            _detail.text = pSnapshot.PrinceName + "  |  " +
                           pSnapshot.SeatName + "  |  " +
                           pSnapshot.CityIds.Count + "/" +
                           FeudatoryRules.MaximumCities;
            Color color = Parse(pSnapshot.ParentColor,
                new Color(.72f, .58f, .28f, 1f));
            _stripe.color = color;
            _name.color = Color.Lerp(color, Color.white, .35f);
            _background.color = pSelected
                ? new Color(.27f, .23f, .16f, .98f)
                : new Color(.13f, .12f, .10f, .92f);
            gameObject.SetActive(true);
        }

        public void Unbind()
        {
            _feudatoryId = -1L;
            _select = null;
            gameObject.SetActive(false);
        }

        private void Build()
        {
            RectTransform rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(190f, Height);
            var layout = gameObject.AddComponent<LayoutElement>();
            layout.minHeight = Height;
            layout.preferredHeight = Height;
            layout.flexibleWidth = 1f;
            _background = GetComponent<Image>();
            _button = GetComponent<Button>();
            _button.onClick.AddListener(() => _select?.Invoke(_feudatoryId));

            var stripe = new GameObject("CountryColor", typeof(RectTransform),
                typeof(Image));
            stripe.transform.SetParent(transform, false);
            _stripe = stripe.GetComponent<Image>();
            _stripe.raycastTarget = false;
            Layout(_stripe.rectTransform, 0f, 0f, 4f, Height);

            _name = CreateText("Name", 10, TextAnchor.UpperLeft);
            LayoutStretchWidth(_name.rectTransform, 10f, 5f, 6f, 19f);
            _detail = CreateText("Detail", 8, TextAnchor.UpperLeft);
            _detail.color = new Color(.78f, .76f, .70f, 1f);
            LayoutStretchWidth(_detail.rectTransform, 10f, 27f, 6f, 18f);
        }

        private Text CreateText(string pName, int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(transform, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static void LayoutStretchWidth(RectTransform pRect, float pX,
            float pY, float pRight, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(1f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(-pX - pRight, pHeight);
        }

        private static Color Parse(string pHex, Color pFallback)
        {
            return ColorUtility.TryParseHtmlString(pHex, out Color color)
                ? color
                : pFallback;
        }
    }
}
