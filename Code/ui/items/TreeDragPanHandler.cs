using UnityEngine;
using UnityEngine.EventSystems;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     家族树/氏族大树画布拖动平移。挂在树画布(TreeCanvas)上 —— 节点上的 Button 不实现 IDragHandler,
    ///     拖动事件会沿 EventSystem 向上冒泡到本组件,故"点节点=点击、拖空白或拖节点=平移画布",互不冲突。
    ///     拖动直接改 target(_canvasRect)的 anchoredPosition;并做边界夹取避免把整株树拖出可视区太远。
    /// </summary>
    internal class TreeDragPanHandler : MonoBehaviour, IDragHandler, IBeginDragHandler, IScrollHandler
    {
        private RectTransform _target;     // 被平移/缩放的画布(节点都挂它下面)
        private RectTransform _viewport;   // 可视区(用于边界夹取)
        private Vector2 _startAnchored;
        private Vector2 _startPointer;

        private const float MIN_SCALE = 0.25f;  // 最小缩放(人多时缩小看总览)
        private const float MAX_SCALE = 2.0f;   // 最大缩放(看细节)
        private const float SCALE_STEP = 0.1f;  // 每格滚轮缩放步长

        public void Setup(RectTransform pTarget, RectTransform pViewport)
        {
            _target = pTarget;
            _viewport = pViewport;
        }

        public void OnBeginDrag(PointerEventData pEventData)
        {
            if (_target == null) return;
            _startAnchored = _target.anchoredPosition;
            _startPointer = pEventData.position;
        }

        public void OnDrag(PointerEventData pEventData)
        {
            if (_target == null) return;
            Vector2 delta = pEventData.position - _startPointer;
            // 无边界自由平移(用户要求:顶部也能拖过去,不夹取)。
            _target.anchoredPosition = _startAnchored + delta;
        }

        // 鼠标滚轮缩放整棵树(等比 localScale,限幅 [MIN,MAX]),人多时缩小看总览、放大看细节。
        public void OnScroll(PointerEventData pEventData)
        {
            if (_target == null) return;
            float cur = _target.localScale.x;
            float next = Mathf.Clamp(cur + pEventData.scrollDelta.y * SCALE_STEP, MIN_SCALE, MAX_SCALE);
            if (!Mathf.Approximately(next, cur))
                _target.localScale = new Vector3(next, next, 1f);
        }
    }
}
