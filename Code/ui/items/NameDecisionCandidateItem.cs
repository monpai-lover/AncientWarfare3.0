using System;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class NameDecisionCandidateItem : MonoBehaviour
    {
        private Image _background;
        private Button _button;
        private Text _label;
        private string _value = "";

        public void Setup(string pValue, bool pSelected, Action<string> pSelect)
        {
            EnsureUi();
            _value = pValue ?? "";
            _button.onClick.RemoveAllListeners();
            _button.interactable = !string.IsNullOrEmpty(_value);
            if (_button.interactable)
                _button.onClick.AddListener(() => pSelect?.Invoke(_value));
            SetSelected(pSelected);
            gameObject.SetActive(true);
        }

        public void SetSelected(bool pSelected)
        {
            if (_label == null) return;
            AW_UIStyle.ApplyButton(_background, pSelected ? 1f : 0.9f);
            _background.color = pSelected
                ? new Color(0.72f, 0.52f, 0.20f, 1f)
                : Color.white;
            _label.text = (pSelected ? "> " : "  ") + _value;
            _label.color = pSelected
                ? new Color(1f, 0.94f, 0.70f, 1f)
                : Color.white;
            _label.fontStyle = pSelected ? FontStyle.Bold : FontStyle.Normal;
        }

        public void SetLayout(Vector2 pPosition, Vector2 pSize)
        {
            RectTransform rect = GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
        }

        public void Clear()
        {
            if (_button != null) _button.onClick.RemoveAllListeners();
            _value = "";
            gameObject.SetActive(false);
        }

        private void EnsureUi()
        {
            if (_label != null) return;
            if (GetComponent<RectTransform>() == null)
                gameObject.AddComponent<RectTransform>();
            _background = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            _button = GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            AW_UIStyle.ApplyButton(_background, 0.9f);

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(3f, 1f);
            textRect.offsetMax = new Vector2(-3f, -1f);
            _label = textObject.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 9;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.resizeTextForBestFit = true;
            _label.resizeTextMinSize = 7;
            _label.resizeTextMaxSize = 9;
            _label.raycastTarget = false;
        }
    }
}
