using System;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolInfluenceBar : MonoBehaviour
    {
        public const float Height = 22f;
        private RectTransform _fillRect;
        private Image _fill;
        private Text _label;
        private Button _button;

        public static SchoolInfluenceBar Create(Transform pParent)
        {
            var obj = new GameObject("SchoolInfluenceBar", typeof(RectTransform), typeof(Image),
                typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(0f, Height);
            obj.GetComponent<Image>().color = new Color(.06f, .06f, .055f, .92f);
            SchoolInfluenceBar bar = obj.AddComponent<SchoolInfluenceBar>();
            bar.Build();
            return bar;
        }

        public void Bind(CourtSchoolDefinition pDefinition, float pShare, Action<string> pOnClick)
        {
            Bind(pDefinition, pShare, pShare, pOnClick);
        }

        public void Bind(CourtSchoolDefinition pDefinition, float pScore, float pShare,
            Action<string> pOnClick)
        {
            if (pDefinition == null) return;
            float share = Mathf.Clamp01(pShare);
            _fillRect.anchorMax = new Vector2(share, 1f);
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;
            _fill.color = ColorUtility.TryParseHtmlString(pDefinition.ColorHex, out Color color)
                ? color
                : Color.gray;
            _label.font = LocalizedTextManager.current_font;
            _label.text = SchoolInfluenceLabelRules.Build(
                AW_L10n.Text(pDefinition.NameKey, pDefinition.Id), pScore, share);
            _label.enabled = true;
            _label.gameObject.SetActive(true);
            _label.transform.SetAsLastSibling();
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => pOnClick?.Invoke(pDefinition.Id));
        }

        private void Build()
        {
            _button = GetComponent<Button>();
            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(transform, false);
            _fillRect = fillObject.GetComponent<RectTransform>();
            _fillRect.anchorMin = Vector2.zero;
            _fillRect.anchorMax = Vector2.one;
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;
            _fill = fillObject.GetComponent<Image>();
            _fill.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-4f, 0f);
            _label = labelObject.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 9;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.color = Color.white;
            _label.supportRichText = true;
            _label.resizeTextForBestFit = true;
            _label.resizeTextMinSize = 7;
            _label.resizeTextMaxSize = 9;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Truncate;
            _label.raycastTarget = false;
            var outline = labelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, .9f);
            outline.effectDistance = new Vector2(1f, -1f);
            Canvas labelCanvas = labelObject.AddComponent<Canvas>();
            labelCanvas.overrideSorting = true;
            labelCanvas.sortingOrder = 100;
            labelObject.transform.SetAsLastSibling();
        }
    }
}
