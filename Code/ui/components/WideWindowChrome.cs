using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class WideWindowChrome : MonoBehaviour
    {
        private static Sprite _whiteSprite;

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
                    typeof(Image), typeof(WideWindowResizeHandler));
            if (existing == null) handle.transform.SetParent(pBackgroundTransform, false);
            _resizeHandle = handle.GetComponent<RectTransform>();
            _resizeHandle.anchorMin = new Vector2(1f, 0f);
            _resizeHandle.anchorMax = new Vector2(1f, 0f);
            _resizeHandle.pivot = new Vector2(1f, 0f);
            _resizeHandle.sizeDelta = new Vector2(18f, 18f);
            RepositionResizeHandle();

            Image image = handle.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = new Color(0.84f, 0.68f, 0.34f, 0.72f);
            image.raycastTarget = true;

            WideWindowResizeHandler resize = handle.GetComponent<WideWindowResizeHandler>();
            resize.Setup(CurrentSize, ApplySize);
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

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            return _whiteSprite;
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
            IBeginDragHandler, IDragHandler
        {
            private Func<Vector2> _getSize;
            private Action<Vector2> _setSize;
            private Vector2 _startPointer;
            private Vector2 _startSize;

            public void Setup(Func<Vector2> pGetSize, Action<Vector2> pSetSize)
            {
                _getSize = pGetSize;
                _setSize = pSetSize;
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
        }
    }
}
