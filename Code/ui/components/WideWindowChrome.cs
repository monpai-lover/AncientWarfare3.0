using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class WideWindowChrome : MonoBehaviour
    {
        private Func<Vector2> _getSize;
        private Action<Vector2> _setSize;
        private Vector2 _defaultSize;
        private Vector2 _minimumSize;
        private Vector2 _maximumSize;
        private RectTransform _resizeHandle;

        public static WideWindowChrome Attach(Transform pBackgroundTransform,
            Func<Vector2> pGetSize, Action<Vector2> pSetSize,
            Vector2 pDefaultSize, Vector2 pMinimumSize, Vector2 pMaximumSize)
        {
            if (pBackgroundTransform == null) return null;
            WideWindowChrome chrome =
                pBackgroundTransform.GetComponent<WideWindowChrome>() ??
                pBackgroundTransform.gameObject.AddComponent<WideWindowChrome>();
            chrome.Configure(pBackgroundTransform, pGetSize, pSetSize,
                pDefaultSize, pMinimumSize, pMaximumSize);
            return chrome;
        }

        public void RepositionResizeHandle()
        {
            if (_resizeHandle != null)
                _resizeHandle.anchoredPosition = new Vector2(-2f, 2f);
        }

        private void Configure(Transform pBackgroundTransform,
            Func<Vector2> pGetSize, Action<Vector2> pSetSize,
            Vector2 pDefaultSize, Vector2 pMinimumSize, Vector2 pMaximumSize)
        {
            _getSize = pGetSize;
            _setSize = pSetSize;
            _defaultSize = pDefaultSize;
            _minimumSize = pMinimumSize;
            _maximumSize = pMaximumSize;

            RectTransform root = pBackgroundTransform.parent?.GetComponent<RectTransform>();
            Transform title = pBackgroundTransform.Find("TitleBackground");
            if (title != null && root != null)
            {
                Image titleImage = title.GetComponent<Image>();
                if (titleImage != null) titleImage.raycastTarget = true;
                WideWindowDragHandler drag =
                    title.GetComponent<WideWindowDragHandler>() ??
                    title.gameObject.AddComponent<WideWindowDragHandler>();
                drag.Setup(root);
            }

            Transform existing = pBackgroundTransform.Find("WideWindowResizeHandle");
            GameObject handle = existing != null
                ? existing.gameObject
                : new GameObject("WideWindowResizeHandle", typeof(RectTransform),
                    typeof(Image), typeof(WideWindowResizeHandler),
                    typeof(TipButton));
            if (existing == null) handle.transform.SetParent(pBackgroundTransform, false);
            _resizeHandle = handle.GetComponent<RectTransform>();
            _resizeHandle.anchorMin = new Vector2(1f, 0f);
            _resizeHandle.anchorMax = new Vector2(1f, 0f);
            _resizeHandle.pivot = new Vector2(1f, 0f);
            _resizeHandle.sizeDelta = new Vector2(26f, 26f);
            RepositionResizeHandle();

            Image image = handle.GetComponent<Image>();
            AW_UIStyle.ApplyButton(image, 0.98f);
            image.color = new Color(0.24f, 0.17f, 0.10f, 0.98f);
            image.raycastTarget = true;

            Transform iconTransform = handle.transform.Find("WideWindowResizeIcon");
            GameObject iconObject = iconTransform != null
                ? iconTransform.gameObject
                : new GameObject("WideWindowResizeIcon", typeof(RectTransform),
                    typeof(Image));
            if (iconTransform == null) iconObject.transform.SetParent(handle.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(15f, 15f);
            iconRect.localRotation = Quaternion.Euler(0f, 0f, -45f);

            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(
                              "ui/icons/iconArrowMetaRight") ??
                          SpriteTextureLoader.getSprite(
                              "ui/icons/genes/gene_scale_plus");
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            WideWindowResizeHandler resize = handle.GetComponent<WideWindowResizeHandler>();
            resize.Setup(CurrentSize, ApplySize, image, icon);

            TipButton tip = handle.GetComponent<TipButton>() ??
                            handle.AddComponent<TipButton>();
            tip.enabled = true;
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(handle, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text("aw_window_resize", "Resize window"),
                    tip_description = AW_L10n.Text("aw_window_resize_desc",
                        "Drag diagonally to resize this window.")
                });
        }

        private Vector2 CurrentSize()
        {
            return _getSize?.Invoke() ?? _defaultSize;
        }

        private void ApplySize(Vector2 pSize)
        {
            Vector2 size = new Vector2(
                Mathf.Clamp(pSize.x, _minimumSize.x, _maximumSize.x),
                Mathf.Clamp(pSize.y, _minimumSize.y, _maximumSize.y));
            _setSize?.Invoke(size);
        }

        private sealed class WideWindowDragHandler : MonoBehaviour,
            IBeginDragHandler, IDragHandler
        {
            private RectTransform _target;
            private Vector2 _startPointer;
            private Vector2 _startPosition;

            public void Setup(RectTransform pTarget)
            {
                _target = pTarget;
            }

            public void OnBeginDrag(PointerEventData pEventData)
            {
                if (_target == null) return;
                _startPointer = pEventData.position;
                _startPosition = _target.anchoredPosition;
            }

            public void OnDrag(PointerEventData pEventData)
            {
                if (_target == null) return;
                _target.anchoredPosition = _startPosition +
                                           pEventData.position - _startPointer;
            }
        }

        private sealed class WideWindowResizeHandler : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IPointerEnterHandler,
            IPointerExitHandler
        {
            private static readonly Color BackplateNormal =
                new Color(0.24f, 0.17f, 0.10f, 0.98f);
            private static readonly Color BackplateHovered =
                new Color(0.38f, 0.27f, 0.12f, 1f);
            private static readonly Color IconNormal =
                new Color(0.92f, 0.78f, 0.42f, 0.98f);
            private static readonly Color IconHovered =
                new Color(1f, 0.93f, 0.62f, 1f);

            private Func<Vector2> _getSize;
            private Action<Vector2> _setSize;
            private Image _backplate;
            private Image _icon;
            private Vector2 _startPointer;
            private Vector2 _startSize;

            public void Setup(Func<Vector2> pGetSize, Action<Vector2> pSetSize,
                Image pBackplate, Image pIcon)
            {
                _getSize = pGetSize;
                _setSize = pSetSize;
                _backplate = pBackplate;
                _icon = pIcon;
                SetHovered(false);
            }

            public void OnBeginDrag(PointerEventData pEventData)
            {
                _startPointer = pEventData.position;
                _startSize = _getSize?.Invoke() ?? Vector2.zero;
            }

            public void OnDrag(PointerEventData pEventData)
            {
                Vector2 delta = pEventData.position - _startPointer;
                _setSize?.Invoke(new Vector2(
                    _startSize.x + delta.x, _startSize.y - delta.y));
            }

            public void OnPointerEnter(PointerEventData pEventData)
            {
                SetHovered(true);
            }

            public void OnPointerExit(PointerEventData pEventData)
            {
                SetHovered(false);
            }

            private void SetHovered(bool pHovered)
            {
                if (_backplate != null)
                    _backplate.color = pHovered
                        ? BackplateHovered
                        : BackplateNormal;
                if (_icon != null)
                    _icon.color = pHovered ? IconHovered : IconNormal;
            }
        }
    }
}
