using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class AWStringDropdownOption
    {
        internal string Id { get; set; } = string.Empty;
        internal string Label { get; set; } = string.Empty;
        internal bool Enabled { get; set; } = true;
        internal string DisabledMessage { get; set; } = string.Empty;
    }

    internal sealed class AWStringDropdown : MonoBehaviour
    {
        private const float ItemHeight = 22f;
        private const float MinimumPopupWidth = 148f;
        private const float MaximumPopupWidth = 380f;
        private const float MaximumPopupHeight = 198f;
        private const float ScreenPadding = 8f;

        private static AWStringDropdown _openDropdown;

        private readonly List<AWStringDropdownOption> _options =
            new List<AWStringDropdownOption>();
        private Text _caption;
        private Text _arrow;
        private GameObject _overlay;
        private RectTransform _popup;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Action<AWStringDropdownOption> _onSelected;
        private Action<AWStringDropdownOption> _onUnavailable;
        private string _selectedId = string.Empty;
        private string _emptyCaption = string.Empty;
        private float _popupOffsetX;

        internal RectTransform RectTransform => transform as RectTransform;

        internal static AWStringDropdown Create(Transform parent, string name,
            float width, float height,
            Action<AWStringDropdownOption> onSelected,
            Action<AWStringDropdownOption> onUnavailable = null,
            float popupOffsetX = 0f)
        {
            if (parent == null) return null;
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(AWStringDropdown));
            obj.transform.SetParent(parent, false);
            AWStringDropdown dropdown = obj.GetComponent<AWStringDropdown>();
            dropdown.Configure(width, height, onSelected, onUnavailable,
                popupOffsetX);
            return dropdown;
        }

        internal void SetOptions(IEnumerable<AWStringDropdownOption> options,
            string selectedId, string emptyCaption)
        {
            _options.Clear();
            _options.AddRange((options ??
                Array.Empty<AWStringDropdownOption>()).Where(option =>
                option != null));
            _selectedId = selectedId ?? string.Empty;
            _emptyCaption = emptyCaption ?? string.Empty;
            UpdateCaption();
            if (_popup != null) BuildOptions();
        }

        internal void SetInteractable(bool interactable)
        {
            Button button = GetComponent<Button>();
            if (button != null) button.interactable = interactable;
        }

        private void Configure(float width, float height,
            Action<AWStringDropdownOption> onSelected,
            Action<AWStringDropdownOption> onUnavailable,
            float popupOffsetX)
        {
            _onSelected = onSelected;
            _onUnavailable = onUnavailable;
            _popupOffsetX = popupOffsetX;
            RectTransform rect = RectTransform;
            rect.sizeDelta = new Vector2(Mathf.Max(1f, width),
                Mathf.Max(1f, height));
            Image image = GetComponent<Image>();
            AW_UIStyle.ApplyButton(image, 0.96f);
            image.raycastTarget = true;
            Button button = GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(ToggleDropdown);
            _caption = CreateCaption("Caption", false);
            _arrow = CreateCaption("Arrow", true);
            UpdateCaption();
        }

        private Text CreateCaption(string name, bool arrow)
        {
            Text text = new GameObject(name, typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(transform, false);
            text.font = ResolveFont();
            text.fontSize = 9;
            text.color = Color.white;
            text.alignment = arrow ? TextAnchor.MiddleCenter :
                TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = !arrow;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            if (arrow)
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(16f, 0f);
                rect.anchoredPosition = new Vector2(-3f, 0f);
                text.text = "v";
            }
            else
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(5f, 1f);
                rect.offsetMax = new Vector2(-20f, -1f);
            }
            return text;
        }

        private void UpdateCaption()
        {
            AWStringDropdownOption selected = _options.FirstOrDefault(option =>
                string.Equals(option.Id, _selectedId,
                    StringComparison.Ordinal));
            if (_caption != null)
                _caption.text = selected?.Label ?? _emptyCaption;
            if (_arrow != null) _arrow.text = "v";
        }

        private void ToggleDropdown()
        {
            if (_popup != null) CloseDropdown();
            else OpenDropdown();
        }

        private void OpenDropdown()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas?.transform as RectTransform;
            if (canvasRect == null) return;
            if (_openDropdown != null && _openDropdown != this)
                _openDropdown.CloseDropdown();
            _openDropdown = this;

            _overlay = new GameObject("AWStringDropdownOverlay",
                typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(Image),
                typeof(DismissArea));
            _overlay.transform.SetParent(canvasRect, false);
            RectTransform overlayRect = _overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            Canvas overlayCanvas = _overlay.GetComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingLayerID = canvas.sortingLayerID;
            overlayCanvas.sortingOrder = canvas.sortingOrder + 1;
            Image overlayImage = _overlay.GetComponent<Image>();
            overlayImage.color = Color.clear;
            overlayImage.raycastTarget = true;
            _overlay.GetComponent<DismissArea>().Owner = this;
            _overlay.transform.SetAsLastSibling();

            _popup = new GameObject("AWStringDropdownPopup",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            _popup.SetParent(overlayRect, false);
            Image popupImage = _popup.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(popupImage, 0.98f);
            popupImage.raycastTarget = true;
            _scroll = _popup.GetComponent<ScrollRect>();
            _scroll.viewport = _popup;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 24f;
            _popup.anchorMin = _popup.anchorMax = Vector2.zero;
            BuildOptions();
            PositionPopup();
            _popup.SetAsLastSibling();
        }

        private void BuildOptions()
        {
            if (_popup == null) return;
            if (_content != null) Destroy(_content.gameObject);
            _content = new GameObject("Content", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _content.SetParent(_popup, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0f, 1f);
            int count = Math.Max(1, _options.Count);
            float popupWidth = Mathf.Clamp(Mathf.Max(MinimumPopupWidth,
                    RectTransform.rect.width), MinimumPopupWidth,
                MaximumPopupWidth);
            float popupHeight = Mathf.Min(MaximumPopupHeight,
                count * ItemHeight);
            _popup.sizeDelta = new Vector2(popupWidth, popupHeight);
            _content.sizeDelta = new Vector2(0f, count * ItemHeight);

            if (_options.Count == 0)
                CreateOptionRow(null, 0);
            else
                for (int index = 0; index < _options.Count; index++)
                    CreateOptionRow(_options[index], index);
            _scroll.content = _content;
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void CreateOptionRow(AWStringDropdownOption option, int index)
        {
            GameObject row = new GameObject("StringOption_" + index,
                typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(_content, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(0f, -(index + 1) * ItemHeight);
            rect.offsetMax = new Vector2(0f, -index * ItemHeight);
            Image image = row.GetComponent<Image>();
            bool selected = option != null && string.Equals(option.Id,
                _selectedId, StringComparison.Ordinal);
            AW_UIStyle.ApplyListRow(image, selected ? 0.98f : 0.82f);
            if (option != null && !option.Enabled)
                image.color = new Color(image.color.r, image.color.g,
                    image.color.b, 0.52f);
            Button button = row.GetComponent<Button>();
            button.targetGraphic = image;
            if (option != null)
                button.onClick.AddListener(() => Select(option));
            Text text = new GameObject("Text", typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(row.transform, false);
            text.font = ResolveFont();
            text.fontSize = 9;
            text.color = option != null && !option.Enabled
                ? new Color(1f, 1f, 1f, 0.62f)
                : Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
            text.raycastTarget = false;
            text.text = option?.Label ?? _emptyCaption;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 1f);
            textRect.offsetMax = new Vector2(-6f, -1f);
        }

        private void Select(AWStringDropdownOption option)
        {
            CloseDropdown();
            if (!option.Enabled)
            {
                _onUnavailable?.Invoke(option);
                return;
            }
            _selectedId = option.Id ?? string.Empty;
            UpdateCaption();
            _onSelected?.Invoke(option);
        }

        private void PositionPopup()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform overlayRect = _overlay?.transform as RectTransform;
            if (canvas == null || overlayRect == null || _popup == null)
                return;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            Vector3[] corners = new Vector3[4];
            RectTransform.GetWorldCorners(corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(
                camera, corners[0]);
            Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(camera,
                corners[1]);
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            float width = _popup.rect.width * scale;
            float height = _popup.rect.height * scale;
            float x = Mathf.Clamp(bottomLeft.x + _popupOffsetX * scale,
                ScreenPadding, Mathf.Max(ScreenPadding,
                    Screen.width - width - ScreenPadding));
            bool above = bottomLeft.y - height < ScreenPadding;
            Vector2 anchor = above ? new Vector2(x, topLeft.y) :
                new Vector2(x, bottomLeft.y);
            _popup.pivot = above ? new Vector2(0f, 0f) :
                new Vector2(0f, 1f);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayRect, anchor, camera, out Vector2 local))
                _popup.localPosition = local;
        }

        private static Font ResolveFont()
        {
            try
            {
                if (LocalizedTextManager.current_font != null)
                    return LocalizedTextManager.current_font;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private void CloseDropdown()
        {
            if (_content != null) Destroy(_content.gameObject);
            _content = null;
            if (_popup != null) Destroy(_popup.gameObject);
            _popup = null;
            _scroll = null;
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            if (_openDropdown == this) _openDropdown = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) CloseDropdown();
        }

        private void LateUpdate()
        {
            if (_popup == null) return;
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy ||
                transform.parent == null)
            {
                CloseDropdown();
                return;
            }
            PositionPopup();
        }

        private void OnDisable()
        {
            CloseDropdown();
        }

        private void OnDestroy()
        {
            CloseDropdown();
        }

        private sealed class DismissArea : MonoBehaviour,
            IPointerClickHandler
        {
            internal AWStringDropdown Owner { get; set; }

            public void OnPointerClick(PointerEventData eventData)
            {
                Owner?.CloseDropdown();
            }
        }
    }
}
