using System;
using AncientWarfare3.core.court;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowOfficeCard : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private Text _label;
        private Action<CourtWorkflowOfficeCard> _clicked;
        private Action<CourtWorkflowOfficeCard> _deleteRequested;
        private Action<CourtWorkflowOfficeCard> _dragEnded;

        public CustomCourtOffice Office { get; private set; }

        public void Bind(CustomCourtOffice office,
            Action<CourtWorkflowOfficeCard> clicked,
            Action<CourtWorkflowOfficeCard> deleteRequested,
            Action<CourtWorkflowOfficeCard> dragEnded)
        {
            Office = office;
            _clicked = clicked;
            _deleteRequested = deleteRequested;
            _dragEnded = dragEnded;
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
                if (Office.Layout == null)
                    Office.Layout = new CustomCourtOfficeLayout();
                Office.Layout.X = local.x;
                Office.Layout.Y = -local.y;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragEnded?.Invoke(this);
        }

        public static CourtWorkflowOfficeCard Create(Transform parent,
            CustomCourtOffice office, Action<CourtWorkflowOfficeCard> clicked,
            Action<CourtWorkflowOfficeCard> deleteRequested,
            Action<CourtWorkflowOfficeCard> dragEnded)
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
            Button deleteButton = CreateDeleteButton(gameObject.transform,
                () => deleteRequested?.Invoke(card));
            deleteButton.gameObject.name = "DeleteButton";
            card.Bind(office, clicked, deleteRequested, dragEnded);
            return card;
        }

        private static Button CreateDeleteButton(Transform parent,
            Action deleteRequested)
        {
            var obj = new GameObject("DeleteButton", typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-3f, -3f);
            rect.sizeDelta = new Vector2(16f, 16f);
            obj.GetComponent<Image>().color = new Color(0.7f, 0.12f, 0.12f,
                0.96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => deleteRequested?.Invoke());
            var textObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(obj.transform, false);
            Text text = textObject.GetComponent<Text>();
            text.text = "X";
            text.fontSize = 10;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
    }
}
