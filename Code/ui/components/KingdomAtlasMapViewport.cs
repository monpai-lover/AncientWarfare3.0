using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class KingdomAtlasMapViewport : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IScrollHandler
    {
        private RectTransform _content;
        private Vector2 _startPosition;
        private Vector2 _startPointer;
        private float _zoom = 1f;

        internal void Setup(RectTransform pContent)
        {
            _content = pContent;
            _zoom = 1f;
        }

        public void OnBeginDrag(PointerEventData pEventData)
        {
            if (_content == null) return;
            _startPointer = pEventData.position;
            _startPosition = _content.anchoredPosition;
        }

        public void OnDrag(PointerEventData pEventData)
        {
            if (_content == null) return;
            _content.anchoredPosition = _startPosition +
                pEventData.position - _startPointer;
        }

        public void OnScroll(PointerEventData pEventData)
        {
            if (_content == null) return;
            _zoom = Mathf.Clamp(_zoom + pEventData.scrollDelta.y * 0.08f,
                0.35f, 4f);
            _content.localScale = new Vector3(_zoom, _zoom, 1f);
        }
    }
}
