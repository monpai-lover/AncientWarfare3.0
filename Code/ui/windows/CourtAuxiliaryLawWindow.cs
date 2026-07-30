using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CourtAuxiliaryLawWindow :
        AbstractWindow<CourtAuxiliaryLawWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(580f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);

        private static long _kingdomId = -1L;
        private static bool _resetSelections = true;
        private readonly List<LawSection> _sections = new List<LawSection>(3);
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RectTransform _viewport;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Scrollbar _scrollbar;
        private Text _summary;
        private Text _feedback;
        private string _feedbackKey = "";
        private bool _feedbackError;
        private WideWindowChrome _chrome;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            _resetSelections = true;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.COURT_AUXILIARY_LAWS);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.COURT_AUXILIARY_LAWS,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size =>
                {
                    _windowSize = size;
                    ApplyLayout();
                }, DefaultSize, MinimumSize, MaximumSize);
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (!_commandRefreshRequested) return;
            _commandRefreshRequested = false;
            _commandPending = false;
            _resetSelections = true;
            _feedbackKey = "";
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            if (_commandPending) _commandRefreshRequested = true;
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;

            var rootObject = new GameObject("CourtAuxiliaryLawRoot",
                typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();

            var viewportObject = new GameObject("CourtAuxiliaryLawViewport",
                typeof(RectTransform), typeof(Image), typeof(Mask),
                typeof(ScrollRect));
            viewportObject.transform.SetParent(_root, false);
            _viewport = viewportObject.GetComponent<RectTransform>();
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0.055f, 0.052f, 0.045f, 0.98f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("CourtAuxiliaryLawContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(_viewport, false);
            _content = contentObject.GetComponent<RectTransform>();
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = Vector2.zero;

            _scroll = viewportObject.GetComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 22f;
            _scrollbar = CreateScrollbar(_root, _scroll);

            _summary = CreateText(_content, "Summary", 10,
                TextAnchor.MiddleLeft);
            _feedback = CreateText(_content, "Feedback", 9,
                TextAnchor.MiddleLeft);

            _sections.Add(CreateLawSection(CourtAuxiliaryLawKind.Term, 4));
            _sections.Add(CreateLawSection(CourtAuxiliaryLawKind.BorderCommand, 3));
            _sections.Add(CreateLawSection(CourtAuxiliaryLawKind.AppointmentCulture, 3));
        }

        private LawSection CreateLawSection(CourtAuxiliaryLawKind pKind,
            int pOptionCount)
        {
            var sectionObject = new GameObject("LawSection_" + pKind,
                typeof(RectTransform), typeof(Image));
            sectionObject.transform.SetParent(_content, false);
            Image background = sectionObject.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(background, 0.98f);
            background.color = new Color(0.11f, 0.105f, 0.09f, 0.98f);

            var section = new LawSection
            {
                Kind = pKind,
                Rect = sectionObject.GetComponent<RectTransform>(),
                Background = background,
                Title = CreateText(sectionObject.transform, "Title", 10,
                    TextAnchor.MiddleLeft),
                Status = CreateText(sectionObject.transform, "Status", 8,
                    TextAnchor.MiddleRight),
                Description = CreateText(sectionObject.transform,
                    "Description", 8, TextAnchor.UpperLeft)
            };
            for (int value = 0; value < pOptionCount; value++)
            {
                int captured = value;
                Button option = CreateButton(sectionObject.transform,
                    "Option_" + value, "", () => Select(section, captured));
                section.OptionButtons.Add(option);
                section.OptionTexts.Add(
                    option.transform.Find("Text").GetComponent<Text>());
                SetTip(option.gameObject, ValueName(pKind, value),
                    ValueDescription(pKind, value));
            }
            section.ApplyButton = CreateButton(sectionObject.transform,
                "Apply", AW_L10n.Text("aw_court_aux_apply", "\u65BD\u884C"),
                () => Apply(section));
            section.ApplyTip = section.ApplyButton.GetComponent<TipButton>();
            return section;
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                _summary.text = AW_L10n.Text(
                    "aw_court_aux_result_invalid_kingdom",
                    "Kingdom unavailable");
                _feedback.text = "";
                SetSectionsInteractable(false);
                return;
            }

            float points = KingdomPolicyService.GetPoliticalPoints(kingdom);
            _summary.text = kingdom.name + "    " +
                AW_L10n.Text("aw_court_aux_points", "Political points") +
                ": " + Mathf.FloorToInt(points) + "    " +
                AW_L10n.Text("aw_court_aux_cost", "Change cost") +
                ": " + Mathf.RoundToInt(CourtAuxiliaryLawRules.ChangeCost);
            _feedback.text = string.IsNullOrEmpty(_feedbackKey)
                ? AW_L10n.Text("aw_court_aux_hint",
                    "Select one option in a row and apply that law.")
                : AW_L10n.Text(_feedbackKey, _feedbackKey);
            _feedback.color = _feedbackError
                ? new Color(1f, 0.50f, 0.42f, 1f)
                : new Color(0.82f, 0.90f, 0.68f, 1f);

            for (int index = 0; index < _sections.Count; index++)
                BindSection(_sections[index], kingdom, points);
            if (_commandPending) SetSectionsInteractable(false);
            _resetSelections = false;
        }

        private void BindSection(LawSection pSection, Kingdom pKingdom,
            float pPoints)
        {
            int current = CurrentValue(pSection.Kind, pKingdom);
            if (_resetSelections || !pSection.HasSelection)
            {
                pSection.SelectedValue = current;
                pSection.HasSelection = true;
            }
            pSection.CurrentValue = current;
            int cooldown = CourtAuxiliaryLawService.GetCooldownRemaining(
                pKingdom, pSection.Kind);

            pSection.Title.text = KindName(pSection.Kind);
            pSection.Status.text = AW_L10n.Text("aw_court_aux_current",
                "Current") + ": " + ValueName(pSection.Kind, current) +
                "    " + CooldownText(cooldown);
            string block = BlockText(pSection.SelectedValue == current,
                cooldown, pPoints);
            pSection.Description.text = ValueDescription(pSection.Kind,
                pSection.SelectedValue) +
                (string.IsNullOrEmpty(block) ? "" : "    " + block);

            for (int value = 0; value < pSection.OptionButtons.Count; value++)
            {
                bool selected = value == pSection.SelectedValue;
                bool live = value == current;
                string marker = selected ? "> " : live ? "* " : "";
                pSection.OptionTexts[value].text = marker +
                    ValueName(pSection.Kind, value);
                Image image = pSection.OptionButtons[value].GetComponent<Image>();
                image.color = selected
                    ? new Color(0.48f, 0.35f, 0.13f, 1f)
                    : live
                        ? new Color(0.20f, 0.35f, 0.22f, 1f)
                        : new Color(0.20f, 0.18f, 0.14f, 0.98f);
                pSection.OptionButtons[value].interactable = true;
            }

            bool canApply = pSection.SelectedValue != current &&
                            cooldown == 0 &&
                            pPoints + 0.001f >=
                            CourtAuxiliaryLawRules.ChangeCost;
            pSection.ApplyButton.interactable = canApply;
            SetTip(pSection.ApplyButton.gameObject,
                AW_L10n.Text("aw_court_aux_apply", "Apply"),
                string.IsNullOrEmpty(block)
                    ? ValueDescription(pSection.Kind,
                        pSection.SelectedValue)
                    : block);
        }

        private static void Select(LawSection pSection, int pValue)
        {
            if (pSection == null || Instance?._commandPending == true) return;
            pSection.SelectedValue = pValue;
            pSection.HasSelection = true;
            Instance?.Refresh();
        }

        private void Apply(LawSection pSection)
        {
            if (_commandPending || pSection == null) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.ChangeCourtAuxiliaryLaw(_kingdomId,
                        pSection.Kind.ToString(), pSection.SelectedValue));
            if (result.Status == AW3CommandStatus.Pending)
            {
                _commandPending = true;
                _feedbackKey = "aw3_command_pending";
                _feedbackError = false;
                Refresh();
                return;
            }
            CourtAuxiliaryLawChangeResult domain = Enum.IsDefined(
                    typeof(CourtAuxiliaryLawChangeResult), result.DetailCode)
                ? (CourtAuxiliaryLawChangeResult)result.DetailCode
                : CourtAuxiliaryLawChangeResult.PersistenceFailed;
            _feedbackKey = ResultKey(domain);
            _feedbackError = !result.Accepted;
            if (result.Accepted)
                _resetSelections = true;
            Refresh();
        }

        private void SetSectionsInteractable(bool pInteractable)
        {
            for (int sectionIndex = 0; sectionIndex < _sections.Count;
                 sectionIndex++)
            {
                LawSection section = _sections[sectionIndex];
                for (int optionIndex = 0;
                     optionIndex < section.OptionButtons.Count; optionIndex++)
                    section.OptionButtons[optionIndex].interactable =
                        pInteractable;
                section.ApplyButton.interactable = pInteractable;
            }
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float contentWidth = Mathf.Max(1f, _windowSize.x - 42f);
            float contentHeight = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;

            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            RectTransform titleRect = BackgroundTransform?
                .Find("TitleBackground")?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.52f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_court_auxiliary_laws_title", "Auxiliary Laws");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                window.titleText.raycastTarget = false;
            }

            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeScrollComponent =
                nativeScroll?.GetComponent<ScrollRect>();
            if (nativeScrollComponent != null)
            {
                nativeScrollComponent.horizontal = false;
                nativeScrollComponent.vertical = false;
            }
            Transform nativeScrollbar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (nativeScrollbar != null)
                foreach (Graphic graphic in
                         nativeScrollbar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }

            RectTransform nativeViewport =
                ContentTransform?.parent as RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(contentWidth,
                    contentHeight);

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(contentWidth, contentHeight);
            Layout(_viewport, 6f, 6f,
                Mathf.Max(1f, contentWidth - 20f),
                Mathf.Max(1f, contentHeight - 12f));
            Layout(_scrollbar.GetComponent<RectTransform>(),
                Mathf.Max(0f, contentWidth - 12f), 6f, 8f,
                Mathf.Max(1f, contentHeight - 12f));

            float innerWidth = Mathf.Max(1f, _viewport.sizeDelta.x - 8f);
            const float fixedHeight = 390f;
            _content.sizeDelta = new Vector2(innerWidth,
                Mathf.Max(_viewport.sizeDelta.y, fixedHeight));
            Layout(_summary.rectTransform, 8f, 6f,
                Mathf.Max(1f, innerWidth - 16f), 24f);
            Layout(_feedback.rectTransform, 8f, 31f,
                Mathf.Max(1f, innerWidth - 16f), 22f);
            for (int index = 0; index < _sections.Count; index++)
            {
                LawSection section = _sections[index];
                Layout(section.Rect, 8f, 58f + index * 108f,
                    Mathf.Max(1f, innerWidth - 16f), 102f);
                LayoutLawSection(section);
            }
            _chrome?.RepositionResizeHandle();
        }

        private static void LayoutLawSection(LawSection pSection)
        {
            float width = Mathf.Max(1f, pSection.Rect.sizeDelta.x);
            Layout(pSection.Title.rectTransform, 8f, 4f,
                width * 0.34f, 20f);
            Layout(pSection.Status.rectTransform, width * 0.35f, 4f,
                width * 0.63f - 8f, 20f);
            Layout(pSection.Description.rectTransform, 8f, 25f,
                width - 16f, 21f);

            float gap = 4f;
            int optionCount = pSection.OptionButtons.Count;
            float optionWidth = Mathf.Max(42f,
                (width - 16f - gap * (optionCount - 1)) / optionCount);
            for (int index = 0; index < optionCount; index++)
                Layout(pSection.OptionButtons[index]
                        .GetComponent<RectTransform>(),
                    8f + index * (optionWidth + gap), 49f,
                    optionWidth, 22f);
            Layout(pSection.ApplyButton.GetComponent<RectTransform>(),
                width - 82f, 76f, 74f, 20f);
        }

        private static Scrollbar CreateScrollbar(Transform pParent,
            ScrollRect pScroll)
        {
            var barObject = new GameObject("CourtAuxiliaryLawScrollbar",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(pParent, false);
            Image track = barObject.GetComponent<Image>();
            track.color = new Color(0.08f, 0.075f, 0.065f, 0.98f);

            var slidingObject = new GameObject("Sliding Area",
                typeof(RectTransform));
            slidingObject.transform.SetParent(barObject.transform, false);
            RectTransform sliding = slidingObject.GetComponent<RectTransform>();
            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = new Vector2(1f, 1f);
            sliding.offsetMax = new Vector2(-1f, -1f);

            var handleObject = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handleObject.transform.SetParent(sliding, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.76f, 0.61f, 0.28f, 1f);

            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static Button CreateButton(Transform pParent, string pName,
            string pLabel, UnityEngine.Events.UnityAction pAction)
        {
            var buttonObject = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            buttonObject.transform.SetParent(pParent, false);
            Image image = buttonObject.GetComponent<Image>();
            AW_UIStyle.ApplyButton(image, 0.98f);
            image.color = new Color(0.20f, 0.18f, 0.14f, 0.98f);
            Button button = buttonObject.GetComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(pAction);
            Text text = CreateText(buttonObject.transform, "Text", 8,
                TextAnchor.MiddleCenter);
            text.text = pLabel ?? "";
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 8;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(3f, 1f);
            text.rectTransform.offsetMax = new Vector2(-3f, -1f);
            TipButton tip = buttonObject.GetComponent<TipButton>();
            tip.showOnClick = false;
            tip.type = AW_RawTooltip.TYPE;
            return button;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var textObject = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(pParent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void SetTip(GameObject pObject, string pName,
            string pDescription)
        {
            TipButton tip = pObject?.GetComponent<TipButton>();
            if (tip == null) return;
            tip.enabled = true;
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(pObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = pName ?? "",
                    tip_description = pDescription ?? ""
                });
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static int CurrentValue(CourtAuxiliaryLawKind pKind,
            Kingdom pKingdom)
        {
            return pKind switch
            {
                CourtAuxiliaryLawKind.Term =>
                    (int)CourtAuxiliaryLawService.GetTermLaw(pKingdom),
                CourtAuxiliaryLawKind.BorderCommand =>
                    (int)CourtAuxiliaryLawService.GetBorderCommandLaw(pKingdom),
                _ => (int)CourtAuxiliaryLawService
                    .GetAppointmentCultureLaw(pKingdom)
            };
        }

        private static string KindName(CourtAuxiliaryLawKind pKind)
        {
            return AW_L10n.Text(pKind switch
            {
                CourtAuxiliaryLawKind.Term => "aw_court_aux_law_term",
                CourtAuxiliaryLawKind.BorderCommand =>
                    "aw_court_aux_law_border",
                _ => "aw_court_aux_law_appointment"
            }, pKind.ToString());
        }

        private static string ValueName(CourtAuxiliaryLawKind pKind,
            int pValue)
        {
            string key = pKind switch
            {
                CourtAuxiliaryLawKind.Term => pValue switch
                {
                    0 => "aw_court_term_lifetime",
                    1 => "aw_court_term_three",
                    3 => "aw_court_term_nine",
                    _ => "aw_court_term_dynamic"
                },
                CourtAuxiliaryLawKind.BorderCommand => pValue switch
                {
                    0 => "aw_court_border_discretionary",
                    2 => "aw_court_border_centralized",
                    _ => "aw_court_border_petition"
                },
                _ => pValue switch
                {
                    0 => "aw_court_appointment_merit",
                    2 => "aw_court_appointment_centered",
                    _ => "aw_court_appointment_preference"
                }
            };
            return AW_L10n.Text(key, key);
        }

        private static string ValueDescription(CourtAuxiliaryLawKind pKind,
            int pValue)
        {
            string key = pKind switch
            {
                CourtAuxiliaryLawKind.Term => pValue switch
                {
                    0 => "aw_court_term_lifetime_desc",
                    1 => "aw_court_term_three_desc",
                    3 => "aw_court_term_nine_desc",
                    _ => "aw_court_term_dynamic_desc"
                },
                CourtAuxiliaryLawKind.BorderCommand => pValue switch
                {
                    0 => "aw_court_border_discretionary_desc",
                    2 => "aw_court_border_centralized_desc",
                    _ => "aw_court_border_petition_desc"
                },
                _ => pValue switch
                {
                    0 => "aw_court_appointment_merit_desc",
                    2 => "aw_court_appointment_centered_desc",
                    _ => "aw_court_appointment_preference_desc"
                }
            };
            return AW_L10n.Text(key, key);
        }

        private static string CooldownText(int pCooldown)
        {
            return pCooldown <= 0
                ? AW_L10n.Text("aw_court_aux_ready", "Ready")
                : AW_L10n.Text("aw_court_aux_cooldown", "Cooldown") +
                  ": " + pCooldown + " " +
                  AW_L10n.Text("aw_court_aux_years", "years");
        }

        private static string BlockText(bool pUnchanged, int pCooldown,
            float pPoints)
        {
            if (pUnchanged)
                return AW_L10n.Text("aw_court_aux_result_unchanged",
                    "This law is already active");
            if (pCooldown > 0)
                return AW_L10n.Text("aw_court_aux_result_cooldown",
                           "Law is cooling down") + " " + pCooldown;
            if (pPoints + 0.001f < CourtAuxiliaryLawRules.ChangeCost)
                return AW_L10n.Text(
                    "aw_court_aux_result_insufficient_points",
                    "Insufficient political points");
            return "";
        }

        private static string ResultKey(
            CourtAuxiliaryLawChangeResult pResult)
        {
            return pResult switch
            {
                CourtAuxiliaryLawChangeResult.Success =>
                    "aw_court_aux_result_success",
                CourtAuxiliaryLawChangeResult.InvalidKingdom => "aw_court_aux_result_invalid_kingdom",
                CourtAuxiliaryLawChangeResult.InvalidChoice =>
                    "aw_court_aux_result_invalid_choice",
                CourtAuxiliaryLawChangeResult.Unchanged =>
                    "aw_court_aux_result_unchanged",
                CourtAuxiliaryLawChangeResult.InsufficientPoints =>
                    "aw_court_aux_result_insufficient_points",
                CourtAuxiliaryLawChangeResult.Cooldown =>
                    "aw_court_aux_result_cooldown",
                _ => "aw_court_aux_result_persistence_failed"
            };
        }

        private sealed class LawSection
        {
            public CourtAuxiliaryLawKind Kind;
            public RectTransform Rect;
            public Image Background;
            public Text Title;
            public Text Status;
            public Text Description;
            public readonly List<Button> OptionButtons = new List<Button>(4);
            public readonly List<Text> OptionTexts = new List<Text>(4);
            public Button ApplyButton;
            public TipButton ApplyTip;
            public int CurrentValue;
            public int SelectedValue;
            public bool HasSelection;
        }
    }
}
