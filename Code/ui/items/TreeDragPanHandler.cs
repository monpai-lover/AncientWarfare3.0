using UnityEngine;
using UnityEngine.EventSystems;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     家族树/氏族大树画布拖动平移。挂在树画布(TreeCanvas)上 —— 节点上的 Button 不实现 IDragHandler,
    ///     拖动事件会沿 EventSystem 向上冒泡到本组件,故"点节点=点击、拖空白或拖节点=平移画布",互不冲突。
    ///     拖动直接改 target(_canvasRect)的 anchoredPosition;并做边界夹取避免把整株树拖出可视区太远。
    /// </summary>
    internal class TreeDragPanHandler : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        private RectTransform _target;     // 被平移的画布(节点都挂它下面)
        private RectTransform _viewport;   // 可视区(用于边界夹取)
        private Vector2 _startAnchored;
        private Vector2 _startPointer;

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
    }
}
