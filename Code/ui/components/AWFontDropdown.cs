using System;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    /// <summary>
    /// Small, scrollable font picker shared by the atlas and mod settings.
    /// The popup lives on the active canvas so it is not clipped by a settings row.
    /// </summary>
    internal sealed class AWFontDropdown : MonoBehaviour
    {
        private const float ItemHeight = 22f;
        private const float MinimumPopupWidth = 180f;
        private const float MaximumPopupWidth = 360f;
        private const float MaximumPopupHeight = 198f;
        private const float ScreenPadding = 8f;

        private static AWFontDropdown _openDropdown;

        private Text _caption;
        private Text _arrow;
        private GameObject _overlay;
        private RectTransform _popup;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Action<int> _onSelected;
        private int _selectedIndex;
        private float _popupOffsetX;
        private bool _configured;

        internal RectTransform RectTransform =>
            transform as RectTransform;

        internal static AWFontDropdown Create(Transform pParent,
            string pName, float pWidth, float pHeight,
            Action<int> pOnSelected, float pPopupOffsetX = 0f)
        {
            if (pParent == null) return null;
            Transform existing = pParent.Find(pName);
            GameObject dropdownObject = existing != null
                ? existing.gameObject
                : new GameObject(pName, typeof(RectTransform),
                    typeof(Image), typeof(Button), typeof(LayoutElement),
                    typeof(AWFontDropdown));
            if (existing == null)
                dropdownObject.transform.SetParent(pParent, false);
            AWFontDropdown dropdown = dropdownObject.GetComponent<
                AWFontDropdown>();
            if (dropdown == null)
                dropdown = dropdownObject.AddComponent<AWFontDropdown>();
            dropdown.Configure(pWidth, pHeight, pOnSelected, pPopupOffsetX);
            return dropdown;
        }

        internal void Refresh()
        {
            if (!_configured) return;
            _selectedIndex = HierarchicalVassalMapFontRules.ClampIndex(
                HierarchicalVassalMapFontSettings.SelectedIndex,
                Math.Max(1, HierarchicalVassalMapFontSettings.FontCount));
            UpdateCaption();
            if (_popup != null) BuildOptions();
        }

        private void Configure(float pWidth, float pHeight,
            Action<int> pOnSelected, float pPopupOffsetX)
        {
            _onSelected = pOnSelected;
            _popupOffsetX = pPopupOffsetX;
            RectTransform rect = RectTransform;
            if (rect == null) return;
            float width = Mathf.Max(1f, pWidth);
            float height = Mathf.Max(1f, pHeight);
            rect.sizeDelta = new Vector2(width, height);
            LayoutElement layout = GetComponent<LayoutElement>();
            if (layout == null) layout = gameObject.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;

            Image image = GetComponent<Image>();
            AW_UIStyle.ApplyButton(image, 0.96f);
            Button button = GetComponent<Button>();
            if (button != null)
            {
                button.targetGraphic = image;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(ToggleDropdown);
            }

            _caption = EnsureText("Caption");
            ConfigureCaption(_caption, false);
            _arrow = EnsureText("Arrow");
            ConfigureCaption(_arrow, true);
            _selectedIndex = HierarchicalVassalMapFontRules.ClampIndex(
                HierarchicalVassalMapFontSettings.SelectedIndex,
                Math.Max(1, HierarchicalVassalMapFontSettings.FontCount));
            UpdateCaption();
            _configured = true;
        }

        private Text EnsureText(string pName)
        {
            Transform child = transform.Find(pName);
            GameObject textObject = child != null
                ? child.gameObject
                : new GameObject(pName, typeof(RectTransform), typeof(Text));
            if (child == null) textObject.transform.SetParent(transform, false);
            Text text = textObject.GetComponent<Text>();
            if (text == null) text = textObject.AddComponent<Text>();
            return text;
        }

        private static void ConfigureCaption(Text pText, bool pArrow)
        {
            if (pText == null) return;
            pText.font = ResolveFont(pText);
            pText.fontSize = 9;
            pText.color = Color.white;
            pText.alignment = pArrow
                ? TextAnchor.MiddleCenter
                : TextAnchor.MiddleLeft;
            pText.horizontalOverflow = HorizontalWrapMode.Overflow;
            pText.verticalOverflow = VerticalWrapMode.Truncate;
            pText.resizeTextForBestFit = !pArrow;
            pText.resizeTextMinSize = 6;
            pText.resizeTextMaxSize = 9;
            pText.raycastTarget = false;
            RectTransform rect = pText.rectTransform;
            if (pArrow)
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(16f, 0f);
                rect.anchoredPosition = new Vector2(-3f, 0f);
            }
            else
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(5f, 1f);
                rect.offsetMax = new Vector2(-20f, -1f);
            }
        }

        private void UpdateCaption()
        {
            if (_caption != null)
                _caption.text = HierarchicalVassalMapFontSettings.GetFontName(
                    _selectedIndex);
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
            if (canvas == null) return;
            if (_openDropdown != null && _openDropdown != this)
                _openDropdown.CloseDropdown();
            _openDropdown = this;

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;
            _overlay = new GameObject("AWFontDropdownOverlay",
                typeof(RectTransform), typeof(Image), typeof(DismissArea));
            _overlay.transform.SetParent(canvasRect, false);
            RectTransform overlayRect = _overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            Image overlayImage = _overlay.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0f);
            overlayImage.raycastTarget = true;
            _overlay.GetComponent<DismissArea>().Owner = this;
            _overlay.transform.SetAsLastSibling();

            _popup = new GameObject("AWFontDropdownPopup",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            _popup.SetParent(overlayRect, false);
            Image popupImage = _popup.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(popupImage, 0.98f);
            _scroll = _popup.GetComponent<ScrollRect>();
            _scroll.viewport = _popup;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 24f;
            _popup.anchorMin = _popup.anchorMax = Vector2.zero;
            _popup.pivot = new Vector2(0f, 1f);
            BuildOptions();
            PositionPopup(canvas);
            _popup.SetAsLastSibling();
        }

        private void BuildOptions()
        {
            if (_popup == null) return;
            if (_content != null) Destroy(_content.gameObject);
            _content = new GameObject("Content", typeof(RectTransform)).
                GetComponent<RectTransform>();
            _content.SetParent(_popup, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);

            int count = Math.Max(1, HierarchicalVassalMapFontSettings.FontCount);
            float popupWidth = Mathf.Clamp(
                Mathf.Max(MinimumPopupWidth, RectTransform.rect.width),
                MinimumPopupWidth, MaximumPopupWidth);
            float popupHeight = Mathf.Min(MaximumPopupHeight,
                Math.Max(ItemHeight, count * ItemHeight));
            _popup.sizeDelta = new Vector2(popupWidth, popupHeight);
            _content.sizeDelta = new Vector2(0f, count * ItemHeight);
            for (int index = 0; index < count; index++)
            {
                int optionIndex = index;
                GameObject itemObject = new GameObject("FontOption_" + index,
                    typeof(RectTransform), typeof(Image), typeof(Button));
                itemObject.transform.SetParent(_content, false);
                RectTransform itemRect = itemObject.GetComponent<RectTransform>();
                itemRect.anchorMin = new Vector2(0f, 1f);
                itemRect.anchorMax = new Vector2(1f, 1f);
                itemRect.pivot = new Vector2(0f, 1f);
                itemRect.offsetMin = new Vector2(0f,
                    -(index + 1) * ItemHeight);
                itemRect.offsetMax = new Vector2(0f, -index * ItemHeight);
                Image itemImage = itemObject.GetComponent<Image>();
                AW_UIStyle.ApplyListRow(itemImage,
                    index == _selectedIndex ? 0.98f : 0.82f);
                Button itemButton = itemObject.GetComponent<Button>();
                itemButton.targetGraphic = itemImage;
                itemButton.onClick.AddListener(() => Select(optionIndex));
                Text itemText = EnsureItemText(itemObject.transform);
                itemText.text = HierarchicalVassalMapFontSettings.GetFontName(
                    index);
            }
            _scroll.content = _content;
            _scroll.verticalNormalizedPosition = _selectedIndex >= 0 && count > 1
                ? 1f - Mathf.Clamp01(_selectedIndex / (float)(count - 1))
                : 1f;
        }

        private static Text EnsureItemText(Transform pParent)
        {
            GameObject textObject = new GameObject("Text",
                typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(pParent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = ResolveFont(text);
            text.fontSize = 9;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 1f);
            rect.offsetMax = new Vector2(-6f, -1f);
            return text;
        }

        private static Font ResolveFont(Text pReference)
        {
            if (pReference?.font != null) return pReference.font;
            try
            {
                Font current = LocalizedTextManager.current_font;
                if (current != null) return current;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private void PositionPopup(Canvas pCanvas)
        {
            if (_popup == null || pCanvas == null) return;
            RectTransform canvasRect = pCanvas.transform as RectTransform;
            Camera camera = pCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : pCanvas.worldCamera;
            Vector3[] corners = new Vector3[4];
            RectTransform.GetWorldCorners(corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(
                camera, corners[0]);
            Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(
                camera, corners[1]);
            float scale = Mathf.Max(0.01f, pCanvas.scaleFactor);
            float popupWidthPixels = _popup.rect.width * scale;
            float popupHeightPixels = _popup.rect.height * scale;
            float x = Mathf.Clamp(bottomLeft.x + _popupOffsetX * scale,
                ScreenPadding,
                Mathf.Max(ScreenPadding, Screen.width - popupWidthPixels -
                    ScreenPadding));
            bool above = bottomLeft.y - popupHeightPixels < ScreenPadding;
            Vector2 screenAnchor = above
                ? new Vector2(x, topLeft.y)
                : new Vector2(x, bottomLeft.y);
            _popup.pivot = above ? new Vector2(0f, 0f) :
                new Vector2(0f, 1f);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenAnchor, camera, out Vector2 local))
                _popup.anchoredPosition = local;
        }

        private void Select(int pIndex)
        {
            int count = Math.Max(1, HierarchicalVassalMapFontSettings.FontCount);
            _selectedIndex = HierarchicalVassalMapFontRules.ClampIndex(
                pIndex, count);
            HierarchicalVassalMapFontSettings.SelectFont(_selectedIndex);
            CloseDropdown();
            UpdateCaption();
            _onSelected?.Invoke(_selectedIndex);
        }

        internal void SetInteractable(bool pInteractable)
        {
            Button button = GetComponent<Button>();
            if (button != null) button.interactable = pInteractable;
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

        private void OnDestroy()
        {
            CloseDropdown();
        }

        private sealed class DismissArea : MonoBehaviour,
            IPointerClickHandler
        {
            internal AWFontDropdown Owner { get; set; }

            public void OnPointerClick(PointerEventData pEventData)
            {
                Owner?.CloseDropdown();
            }
        }
    }
}
