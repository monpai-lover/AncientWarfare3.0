using System;
using AncientWarfare3.core.court;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowVacancyCard : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public const float Width = 132f;
        public const float Height = 104f;
        private const float SlotSize = 52f;

        private Text _name;
        private Text _subtitle;
        private Action<CourtWorkflowVacancyCard> _clicked;
        private Action<CourtWorkflowVacancyCard> _deleteRequested;
        private Action<CourtWorkflowVacancyCard> _dragEnded;
        private Vector2 _pointerOffset;

        public CustomCourtOffice Office { get; private set; }

        public void Bind(CustomCourtOffice office,
            Action<CourtWorkflowVacancyCard> clicked,
            Action<CourtWorkflowVacancyCard> deleteRequested,
            Action<CourtWorkflowVacancyCard> dragEnded)
        {
            Office = office;
            _clicked = clicked;
            _deleteRequested = deleteRequested;
            _dragEnded = dragEnded;
            if (office == null)
            {
                _name.text = string.Empty;
                _subtitle.text = string.Empty;
                return;
            }

            string name = office.Name?.Chinese;
            if (string.IsNullOrWhiteSpace(name)) name = office.Name?.English;
            if (string.IsNullOrWhiteSpace(name)) name = office.Id;
            _name.text = name ?? string.Empty;
            _subtitle.text = AW_L10n.Text("aw_court_no_officer", "Vacant");
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            transform.SetAsLastSibling();
            RectTransform parent = transform.parent as RectTransform;
            RectTransform rect = transform as RectTransform;
            if (parent == null || rect == null) return;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera,
                    out local))
                _pointerOffset = rect.anchoredPosition - local;
        }

        public void OnDrag(PointerEventData eventData)
        {
            RectTransform parent = transform.parent as RectTransform;
            RectTransform rect = transform as RectTransform;
            if (parent == null || rect == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera,
                    out local)) return;
            rect.anchoredPosition = local + _pointerOffset;
            if (Office == null) return;
            if (Office.Layout == null)
                Office.Layout = new CustomCourtOfficeLayout();
            Office.Layout.X = rect.anchoredPosition.x;
            Office.Layout.Y = -rect.anchoredPosition.y;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragEnded?.Invoke(this);
        }

        public static CourtWorkflowVacancyCard Create(Transform parent,
            CustomCourtOffice office, Action<CourtWorkflowVacancyCard> clicked,
            Action<CourtWorkflowVacancyCard> deleteRequested,
            Action<CourtWorkflowVacancyCard> dragEnded)
        {
            GameObject obj = new GameObject("CourtWorkflowVacancyCard",
                typeof(RectTransform), typeof(Image), typeof(Outline),
                typeof(Button), typeof(CourtWorkflowVacancyCard));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(Width, Height);

            CourtWorkflowVacancyCard card =
                obj.GetComponent<CourtWorkflowVacancyCard>();
            card.BuildUi();
            card.GetComponent<Button>().onClick.AddListener(
                () => clicked?.Invoke(card));
            CreateDeleteButton(obj.transform,
                () => deleteRequested?.Invoke(card));
            card.Bind(office, clicked, deleteRequested, dragEnded);
            return card;
        }

        private void BuildUi()
        {
            Image background = GetComponent<Image>();
            AW_UIStyle.ApplyButton(background, 0.96f);
            Outline outline = GetComponent<Outline>();
            outline.effectColor = new Color(0.04f, 0.06f, 0.06f, 0.92f);
            outline.effectDistance = new Vector2(2f, -2f);

            GameObject avatarObject = new GameObject("empty_slot",
                typeof(RectTransform), typeof(Image));
            avatarObject.transform.SetParent(transform, false);
            RectTransform avatarRect = avatarObject.GetComponent<RectTransform>();
            avatarRect.anchorMin = avatarRect.anchorMax = new Vector2(0f, 1f);
            avatarRect.pivot = new Vector2(0f, 1f);
            avatarRect.anchoredPosition = new Vector2(10f, -6f);
            avatarRect.sizeDelta = new Vector2(SlotSize, SlotSize);
            Image avatar = avatarObject.GetComponent<Image>();
            avatar.sprite = SpriteTextureLoader.getSprite(
                              "civ/icons/minimap_figure") ??
                          SpriteTextureLoader.getSprite("ui/icons/iconClan");
            avatar.color = new Color(0.72f, 0.74f, 0.68f, 0.9f);
            avatar.preserveAspect = true;
            avatar.raycastTarget = false;

            _name = CreateText("Name", new Vector2(6f, -62f),
                new Vector2(Width - 12f, 16f), 10, TextAnchor.MiddleCenter);
            _name.color = new Color(0.76f, 0.76f, 0.72f, 1f);
            _name.resizeTextForBestFit = true;
            _name.resizeTextMinSize = 7;
            _name.resizeTextMaxSize = 10;
            _subtitle = CreateText("Subtitle", new Vector2(6f, -79f),
                new Vector2(Width - 12f, 18f), 8, TextAnchor.UpperCenter);
            _subtitle.color = new Color(0.95f, 0.86f, 0.58f, 1f);
        }

        private Text CreateText(string name, Vector2 position, Vector2 size,
            int fontSize, TextAnchor alignment)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateDeleteButton(Transform parent,
            Action deleteRequested)
        {
            GameObject obj = new GameObject("DeleteButton", typeof(RectTransform),
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
            GameObject textObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(obj.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>();
            text.text = "X";
            text.font = LocalizedTextManager.current_font;
            text.fontSize = 10;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return button;
        }
    }
}
