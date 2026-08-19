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
        private Text _selectionBadge;
        private Outline _outline;
        private Action<CourtWorkflowVacancyCard> _clicked;
        private Action<CourtWorkflowVacancyCard> _deleteRequested;
        private Action<CourtWorkflowVacancyCard> _dragEnded;
        private Vector2 _pointerOffset;
        private bool _suppressClick;

        public CustomCourtOffice Office { get; private set; }
        public bool IsRegionalLayerCard => Office?.Id ==
            "regional_government_layer";

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

            RefreshText();
        }

        public void RefreshText()
        {
            if (Office == null) return;
            string name = Office.Name?.Chinese;
            if (string.IsNullOrWhiteSpace(name)) name = Office.Name?.English;
            if (string.IsNullOrWhiteSpace(name)) name = Office.Id;
            _name.text = name ?? string.Empty;
            _subtitle.text = IsRegionalLayerCard
                ? AW_L10n.Text("aw_custom_court_regional_dynamic", "Dynamic layer")
                : AW_L10n.Text("aw_court_no_officer", "Vacant");
        }

        public void SetSelectionState(int step)
        {
            if (_outline == null || _selectionBadge == null) return;
            bool selected = step == 1 || step == 2;
            _selectionBadge.transform.parent.gameObject.SetActive(selected);
            _selectionBadge.text = selected ? step.ToString() : string.Empty;
            _outline.effectColor = step == 1
                ? new Color(0.18f, 0.86f, 1f, 1f)
                : step == 2
                    ? new Color(1f, 0.68f, 0.16f, 1f)
                    : IsRegionalLayerCard
                        ? new Color(0.25f, 0.72f, 0.78f, 0.96f)
                        : new Color(0.04f, 0.06f, 0.06f, 0.92f);
            _outline.effectDistance = selected
                ? new Vector2(4f, -4f)
                : new Vector2(2f, -2f);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _suppressClick = false;
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
            _suppressClick = true;
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
            Invoke(nameof(ClearDragSuppression), 0f);
        }

        private void HandleClick()
        {
            if (_suppressClick) return;
            _clicked?.Invoke(this);
        }

        private void ClearDragSuppression()
        {
            _suppressClick = false;
        }

        public static CourtWorkflowVacancyCard Create(Transform parent,
            CustomCourtOffice office, Action<CourtWorkflowVacancyCard> clicked,
            Action<CourtWorkflowVacancyCard> settingsRequested,
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
            card.GetComponent<Button>().onClick.AddListener(card.HandleClick);
            CreateSettingsButton(obj.transform,
                () => settingsRequested?.Invoke(card));
            CreateDeleteButton(obj.transform,
                () => deleteRequested?.Invoke(card));
            card.Bind(office, clicked, deleteRequested, dragEnded);
            if (card.IsRegionalLayerCard)
            {
                Transform settings = obj.transform.Find("SettingsButton");
                Transform delete = obj.transform.Find("DeleteButton");
                if (settings != null) settings.gameObject.SetActive(false);
                if (delete != null) delete.gameObject.SetActive(false);
                card.GetComponent<Outline>().effectColor =
                    new Color(0.25f, 0.72f, 0.78f, 0.96f);
                card.CreateRegionalDashedBorder();
                TipButton tip = obj.AddComponent<TipButton>();
                tip.type = AW_RawTooltip.TYPE;
                tip.hoverAction = () => Tooltip.show(obj,
                    AW_RawTooltip.TYPE, new TooltipData
                    {
                        tip_name = AW_L10n.Text(
                            "aw_custom_court_regional_dynamic",
                            "Dynamic layer"),
                        tip_description = AW_L10n.Text(
                            "aw_custom_court_regional_dynamic_desc",
                            "A runtime regional projection. It creates no vacancy or separate appointment.")
                    });
            }
            return card;
        }

        private void CreateRegionalDashedBorder()
        {
            const int horizontalCount = 9;
            const int verticalCount = 6;
            Color color = new Color(0.25f, 0.72f, 0.78f, 0.9f);
            for (int index = 0; index < horizontalCount; index++)
            {
                float x = 4f + index * ((Width - 8f) / horizontalCount);
                CreateDash(new Vector2(x, -2f), new Vector2(8f, 2f), color);
                CreateDash(new Vector2(x, -Height + 2f),
                    new Vector2(8f, 2f), color);
            }
            for (int index = 0; index < verticalCount; index++)
            {
                float y = -5f - index * ((Height - 10f) / verticalCount);
                CreateDash(new Vector2(2f, y), new Vector2(2f, 8f), color);
                CreateDash(new Vector2(Width - 2f, y),
                    new Vector2(2f, 8f), color);
            }
        }

        private void CreateDash(Vector2 pPosition, Vector2 pSize,
            Color pColor)
        {
            Image dash = new GameObject("RegionalBorderDash",
                typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            dash.transform.SetParent(transform, false);
            RectTransform rect = dash.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            dash.color = pColor;
            dash.raycastTarget = false;
        }

        private void BuildUi()
        {
            Image background = GetComponent<Image>();
            AW_UIStyle.ApplyButton(background, 0.96f);
            _outline = GetComponent<Outline>();
            _outline.effectColor = new Color(0.04f, 0.06f, 0.06f, 0.92f);
            _outline.effectDistance = new Vector2(2f, -2f);

            GameObject avatarObject = new GameObject("empty_slot",
                typeof(RectTransform), typeof(Image));
            avatarObject.transform.SetParent(transform, false);
            RectTransform avatarRect = avatarObject.GetComponent<RectTransform>();
            avatarRect.anchorMin = avatarRect.anchorMax = new Vector2(0f, 1f);
            avatarRect.pivot = new Vector2(0f, 1f);
            avatarRect.anchoredPosition = new Vector2(26f, -6f);
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
            CreateSelectionBadge();
        }

        private void CreateSelectionBadge()
        {
            GameObject badge = new GameObject("SelectionBadge",
                typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(transform, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(60f, -5f);
            badgeRect.sizeDelta = new Vector2(20f, 18f);
            badge.GetComponent<Image>().color =
                new Color(0.04f, 0.04f, 0.035f, 0.96f);
            _selectionBadge = CreateText("Text", Vector2.zero,
                badgeRect.sizeDelta, 11, TextAnchor.MiddleCenter);
            _selectionBadge.transform.SetParent(badge.transform, false);
            RectTransform textRect = _selectionBadge.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;
            _selectionBadge.color = Color.white;
            badge.SetActive(false);
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
                typeof(Image), typeof(Button), typeof(TipButton));
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
            TipButton tip = obj.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(obj, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text(
                        "aw_custom_court_delete_office", "Delete office"),
                    tip_description = AW_L10n.Text(
                        "aw_custom_court_delete_office_desc",
                        "Remove this office card and its connections.")
                });
            return button;
        }

        private static Button CreateSettingsButton(Transform parent,
            Action settingsRequested)
        {
            GameObject obj = new GameObject("SettingsButton",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(3f, -3f);
            rect.sizeDelta = new Vector2(18f, 18f);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => settingsRequested?.Invoke());

            GameObject iconObject = new GameObject("Icon",
                typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(obj.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(2f, 2f);
            iconRect.offsetMax = new Vector2(-2f, -2f);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconSettings") ??
                          SpriteTextureLoader.getSprite("ui/icons/iconKingdomList");
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TipButton tip = obj.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(obj, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text(
                        "aw_custom_court_office_settings", "Office settings"),
                    tip_description = AW_L10n.Text(
                        "aw_custom_court_office_settings_desc",
                        "Edit office attributes requirements and effects.")
                });
            return button;
        }
    }
}
