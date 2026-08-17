using System;
using AncientWarfare3.core.court;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowOfficeCard : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        private Text _label;
        private Action<CourtWorkflowOfficeCard> _clicked;

        public CustomCourtOffice Office { get; private set; }

        public void Bind(CustomCourtOffice office,
            Action<CourtWorkflowOfficeCard> clicked)
        {
            Office = office;
            _clicked = clicked;
            if (_label != null)
                _label.text = office == null ? string.Empty : office.Id;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null) return;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera,
                    out local))
                ((RectTransform)transform).anchoredPosition = local;
            if (Office != null)
            {
                Office.Layout.X = local.x;
                Office.Layout.Y = -local.y;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _clicked?.Invoke(this);
        }

        public static CourtWorkflowOfficeCard Create(Transform parent,
            CustomCourtOffice office, Action<CourtWorkflowOfficeCard> clicked)
        {
            var gameObject = new GameObject("CourtOfficeCard",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(CourtWorkflowOfficeCard));
            gameObject.transform.SetParent(parent, false);
            var card = gameObject.GetComponent<CourtWorkflowOfficeCard>();
            card._label = new GameObject("Label", typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
            card._label.transform.SetParent(gameObject.transform, false);
            card._label.alignment = TextAnchor.MiddleCenter;
            card._label.color = Color.white;
            card._label.fontSize = 10;
            card._label.rectTransform.anchorMin = Vector2.zero;
            card._label.rectTransform.anchorMax = Vector2.one;
            card._label.rectTransform.offsetMin = Vector2.zero;
            card._label.rectTransform.offsetMax = Vector2.zero;
            gameObject.GetComponent<Image>().color =
                new Color(0.12f, 0.14f, 0.18f, 0.96f);
            gameObject.GetComponent<Button>().onClick.AddListener(
                () => clicked?.Invoke(card));
            card.Bind(office, clicked);
            return card;
        }
    }
}
