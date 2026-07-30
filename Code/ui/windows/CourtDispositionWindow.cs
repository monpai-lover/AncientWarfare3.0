using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CourtDispositionWindow :
        AbstractWindow<CourtDispositionWindow>
    {
        private const float DefaultWidth = 420f;
        private const float DefaultHeight = 280f;
        private const int MaximumCityChoices = 32;
        private static readonly Vector2 MinimumSize =
            new Vector2(DefaultWidth, DefaultHeight);
        private static readonly Vector2 MaximumSize =
            new Vector2(760f, 560f);

        private sealed class ActionControl
        {
            public CourtDispositionAction Action;
            public GameObject Object;
            public Button Button;
            public Text Text;
            public TipButton Tip;
        }

        private sealed class SelectionControl
        {
            public GameObject Object;
            public Button Button;
            public Text Text;
            public TipButton Tip;
        }

        private static readonly CourtDispositionAction[][] Sections =
        {
            new[]
            {
                CourtDispositionAction.PromoteRank,
                CourtDispositionAction.DemoteRank,
                CourtDispositionAction.DismissOffice
            },
            new[]
            {
                CourtDispositionAction.GrantNobleRank,
                CourtDispositionAction.GrantFief,
                CourtDispositionAction.RevokeFief
            },
            new[]
            {
                CourtDispositionAction.GrantSurname,
                CourtDispositionAction.ExpelLineage
            },
            new[]
            {
                CourtDispositionAction.RelocateFeudatory,
                CourtDispositionAction.ReclaimFeudatoryCity
            }
        };

        private static long _kingdomId = -1L;
        private static long _targetActorId = -1L;
        private readonly List<ActionControl> _actions =
            new List<ActionControl>();
        private readonly List<SelectionControl> _selectionPool =
            new List<SelectionControl>();
        private readonly List<City> _cityCandidates = new List<City>();
        private readonly List<Text> _sectionLabels = new List<Text>();
        private Vector2 _windowSize = new Vector2(DefaultWidth, DefaultHeight);
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private RectTransform _header;
        private Image _flagBackground;
        private Image _flagIcon;
        private RectTransform _rulerPortraitRoot;
        private RectTransform _targetPortraitRoot;
        private UiUnitAvatarElement _rulerPortrait;
        private UiUnitAvatarElement _targetPortrait;
        private Text _identity;
        private Text _points;
        private Button _kingdomBack;
        private RectTransform _actionViewport;
        private RectTransform _actionContent;
        private ScrollRect _actionScroll;
        private Scrollbar _actionScrollbar;
        private RectTransform _selectionPanel;
        private Text _selectionTitle;
        private RectTransform _selectionViewport;
        private RectTransform _selectionContent;
        private ScrollRect _selectionScroll;
        private Scrollbar _selectionScrollbar;
        private Button _selectionBack;
        private Text _feedback;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId, long pTargetActorId)
        {
            _kingdomId = pKingdomId;
            _targetActorId = pTargetActorId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.COURT_DISPOSITION);
            if (Instance?._feedback != null) Instance._feedback.text = "";
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.COURT_DISPOSITION,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyWindowLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize =>
                {
                    _windowSize = pSize;
                    ApplyWindowLayout();
                }, new Vector2(DefaultWidth, DefaultHeight), MinimumSize,
                MaximumSize);
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
            if (_commandRefreshRequested)
            {
                _commandRefreshRequested = false;
                _commandPending = false;
                _feedback.text = "";
                if (isActiveAndEnabled) Refresh();
                return;
            }
            if (!isActiveAndEnabled ||
                _rulerPortrait != null && _targetPortrait != null) return;
            EnsurePortraits();
            Kingdom kingdom = FindKingdom(_kingdomId);
            SetPortrait(_rulerPortraitRoot, _rulerPortrait, kingdom?.king);
            SetPortrait(_targetPortraitRoot, _targetPortrait,
                FindActor(_targetActorId));
        }

        private void OnCommandStateChanged()
        {
            if (_commandPending) _commandRefreshRequested = true;
        }

        private void Refresh()
        {
            EnsureUi();
            EnsurePortraits();
            ApplyWindowLayout();
            ShowActions();
            Kingdom kingdom = FindKingdom(_kingdomId);
            Actor target = FindActor(_targetActorId);
            Actor ruler = kingdom?.king;
            SetPortrait(_rulerPortraitRoot, _rulerPortrait, ruler);
            SetPortrait(_targetPortraitRoot, _targetPortrait, target);
            if (kingdom?.data == null || kingdom.isRekt() ||
                target?.data == null || target.isRekt())
            {
                _identity.text = AW_L10n.Text(
                    "aw_court_disposition_reason_invalid_target",
                    "The target is no longer available.");
                _points.text = "";
                DisableActions(CourtDispositionService.ReasonInvalidTarget);
                return;
            }

            RefreshFlag(kingdom);
            _identity.color = KingdomColor(kingdom);
            _identity.text = kingdom.name + "  |  " +
                             (ruler?.getName() ?? "?") + " -> " +
                             target.getName();
            _points.text = AW_L10n.Text("aw_court_disposition_points",
                               "Political points") + ": " +
                           KingdomPolicyService.GetPoliticalPoints(kingdom)
                               .ToString("0.#") + "  |  " +
                           AW_L10n.Text("aw_policy_yearly_gain",
                               "Yearly Gain") + " +" +
                           KingdomPolicyService.GetPoliticalPointGain(kingdom)
                               .ToString("0.#");
            LoadCityCandidates(kingdom);
            RenderActions(kingdom, ruler, target);
        }

        private void RenderActions(Kingdom pKingdom, Actor pRuler,
            Actor pTarget)
        {
            for (int i = 0; i < _actions.Count; i++)
            {
                ActionControl control = _actions[i];
                CourtDispositionCommand command = PreviewCommand(
                    control.Action, pKingdom, pRuler, pTarget);
                CourtDispositionPreview preview = command == null
                    ? new CourtDispositionPreview(false,
                        CourtDispositionService.ReasonIneligible,
                        CourtDispositionRules.Cost(control.Action))
                    : CourtDispositionService.Preview(command);
                control.Text.text = ActionText(control.Action);
                control.Button.onClick.RemoveAllListeners();
                control.Button.interactable = preview.Allowed &&
                                             !_commandPending;
                if (preview.Allowed && !_commandPending)
                {
                    CourtDispositionAction action = control.Action;
                    control.Button.onClick.AddListener(() =>
                        OnActionSelected(action));
                }
                BindTip(control.Tip, control.Object, control.Text.text,
                    PreviewDescription(preview));
            }
        }

        private CourtDispositionCommand PreviewCommand(
            CourtDispositionAction pAction, Kingdom pKingdom, Actor pRuler,
            Actor pTarget)
        {
            int intParameter = pAction ==
                               CourtDispositionAction.GrantNobleRank
                ? 1
                : pAction == CourtDispositionAction.PromoteRank ? 1 : 0;
            long cityId = -1L;
            if (CourtDispositionRules.RequiresCityParameter(pAction))
            {
                for (int i = 0; i < _cityCandidates.Count; i++)
                {
                    CourtDispositionCommand candidate = NewCommand(pAction,
                        intParameter, _cityCandidates[i].id, pKingdom,
                        pRuler, pTarget, "preview_city_" + i);
                    if (CourtDispositionService.Preview(candidate).Allowed)
                        return candidate;
                }
                return null;
            }
            return NewCommand(pAction, intParameter, cityId, pKingdom,
                pRuler, pTarget, "preview");
        }

        private void OnActionSelected(CourtDispositionAction pAction)
        {
            if (pAction == CourtDispositionAction.GrantNobleRank)
            {
                Actor target = FindActor(_targetActorId);
                if (target?.data != null && !target.isSexMale())
                {
                    Submit(pAction, 1, -1L);
                    return;
                }
                OpenRankSelection();
                return;
            }
            if (CourtDispositionRules.RequiresCityParameter(pAction))
            {
                OpenCitySelection(pAction);
                return;
            }
            Submit(pAction,
                pAction == CourtDispositionAction.PromoteRank ? 1 : 0, -1L);
        }

        private void OpenRankSelection()
        {
            Kingdom kingdom = FindKingdom(_kingdomId);
            Actor ruler = kingdom?.king;
            Actor target = FindActor(_targetActorId);
            BeginSelection(AW_L10n.Text(
                "aw_court_disposition_select_rank", "Select noble rank"));
            int index = 0;
            int maximumRank = NobleRankRules.MaximumGrantableRank(
                (int)KingdomTitleService.GetTitle(kingdom));
            for (int rank = 1; rank <= maximumRank; rank++)
            {
                CourtDispositionCommand command = NewCommand(
                    CourtDispositionAction.GrantNobleRank, rank, -1L,
                    kingdom, ruler, target, "preview_rank_" + rank);
                CourtDispositionPreview preview =
                    CourtDispositionService.Preview(command);
                int selectedRank = rank;
                BindSelection(GetSelection(index++), NobleRankName(rank),
                    preview, () => Submit(
                        CourtDispositionAction.GrantNobleRank,
                        selectedRank, -1L));
            }
            FinishSelection(index);
        }

        private void OpenCitySelection(CourtDispositionAction pAction)
        {
            Kingdom kingdom = FindKingdom(_kingdomId);
            Actor ruler = kingdom?.king;
            Actor target = FindActor(_targetActorId);
            BeginSelection(AW_L10n.Text(
                "aw_court_disposition_select_city", "Select city"));
            int index = 0;
            for (int i = 0; i < _cityCandidates.Count &&
                            index < MaximumCityChoices; i++)
            {
                City city = _cityCandidates[i];
                CourtDispositionCommand command = NewCommand(pAction, 0,
                    city.id, kingdom, ruler, target,
                    "preview_city_" + city.id);
                CourtDispositionPreview preview =
                    CourtDispositionService.Preview(command);
                if (!preview.Allowed) continue;
                long cityId = city.id;
                string label = city.data.name + "  |  " +
                               AW_L10n.Text("aw_population", "Population") +
                               " " + SafePopulation(city);
                BindSelection(GetSelection(index++), label, preview,
                    () => Submit(pAction, 0, cityId));
            }
            FinishSelection(index);
        }

        private void Submit(CourtDispositionAction pAction, int pIntParameter,
            long pLongParameter)
        {
            if (_commandPending) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.SetCourtDisposition(_kingdomId,
                        _targetActorId, pAction.ToString(), pIntParameter,
                        pLongParameter, Guid.NewGuid().ToString("N")));
            if (result.Status == AW3CommandStatus.Pending)
            {
                _commandPending = true;
                _feedback.text = AW_L10n.Text("aw3_command_pending",
                    "Waiting for host");
                Refresh();
                return;
            }
            CourtDispositionOutcome outcome = Enum.IsDefined(
                    typeof(CourtDispositionOutcome), result.DetailCode)
                ? (CourtDispositionOutcome)result.DetailCode
                : CourtDispositionOutcome.Unknown;
            _feedback.text = OutcomeText(outcome) +
                             (string.IsNullOrEmpty(result.MessageKey)
                                 ? ""
                                 : " | " + AW_L10n.Text(result.MessageKey,
                                     result.MessageKey));
            ShowActions();
            if (result.Accepted &&
                CourtDispositionRules.ShouldRefreshCourt(pAction))
            {
                CourtWindow.OpenAndRefresh(_kingdomId);
                return;
            }
            Refresh();
        }

        private CourtDispositionCommand NewCommand(
            CourtDispositionAction pAction, int pIntParameter,
            long pLongParameter, Kingdom pKingdom, Actor pRuler,
            Actor pTarget, string pSuffix)
        {
            return new CourtDispositionCommand(pKingdom?.id ?? _kingdomId,
                pRuler?.data?.id ?? -1L, pTarget?.data?.id ?? _targetActorId,
                pAction, pIntParameter, pLongParameter,
                "court_disposition:" + _kingdomId + ":" + _targetActorId +
                ":" + pAction + ":" + (pSuffix ?? ""));
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            ContentSizeFitter fitter =
                ContentTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            _root = CreateObject("CourtDispositionRoot", ContentTransform)
                .GetComponent<RectTransform>();
            _header = CreatePanel("Header", _root);
            _flagBackground = CreateImage("FlagBackground", _header);
            _flagIcon = CreateImage("FlagIcon", _header);
            _rulerPortrait = CreatePortrait("RulerPortrait", _header,
                out _rulerPortraitRoot);
            _targetPortrait = CreatePortrait("TargetPortrait", _header,
                out _targetPortraitRoot);
            _identity = CreateText(_header, "Identity", 11,
                TextAnchor.MiddleLeft, Color.white);
            _identity.fontStyle = FontStyle.Bold;
            _points = CreateText(_header, "PoliticalPoints", 9,
                TextAnchor.MiddleLeft,
                new Color(0.95f, 0.78f, 0.34f, 1f));
            _kingdomBack = CreateButton(_header, "BackToKingdom",
                AW_L10n.Text("aw_back_to_kingdom", "Back to Kingdom"),
                BackToKingdom, out _);

            BuildActionScroller();
            BuildSelectionPanel();
            _feedback = CreateText(_root, "Feedback", 8,
                TextAnchor.MiddleLeft,
                new Color(0.96f, 0.76f, 0.38f, 1f));
            BuildActions();
        }

        private void BuildActionScroller()
        {
            GameObject viewport = new GameObject("ActionViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect));
            viewport.transform.SetParent(_root, false);
            _actionViewport = viewport.GetComponent<RectTransform>();
            Image panel = viewport.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(panel, 0.82f);
            _actionContent = CreateObject("ActionContent", _actionViewport)
                .GetComponent<RectTransform>();
            SetupTopLeft(_actionContent);
            _actionScroll = viewport.GetComponent<ScrollRect>();
            _actionScroll.viewport = _actionViewport;
            _actionScroll.content = _actionContent;
            _actionScroll.horizontal = false;
            _actionScroll.vertical = true;
            _actionScroll.movementType = ScrollRect.MovementType.Clamped;
            _actionScroll.scrollSensitivity = 18f;
            _actionScrollbar = CreateScrollbar(_root, _actionScroll,
                "ActionScrollbar");
        }

        private void BuildSelectionPanel()
        {
            _selectionPanel = CreatePanel("SelectionPanel", _root);
            _selectionTitle = CreateText(_selectionPanel, "SelectionTitle",
                10, TextAnchor.MiddleLeft,
                new Color(0.96f, 0.80f, 0.38f, 1f));
            _selectionTitle.fontStyle = FontStyle.Bold;
            _selectionBack = CreateButton(_selectionPanel, "Back",
                AW_L10n.Text("aw_court_disposition_back", "Back"),
                ShowActions, out _);

            GameObject viewport = new GameObject("SelectionViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect));
            viewport.transform.SetParent(_selectionPanel, false);
            _selectionViewport = viewport.GetComponent<RectTransform>();
            AW_UIStyle.ApplyPanel(viewport.GetComponent<Image>(), 0.74f);
            _selectionContent = CreateObject("SelectionContent",
                _selectionViewport).GetComponent<RectTransform>();
            SetupTopLeft(_selectionContent);
            _selectionScroll = viewport.GetComponent<ScrollRect>();
            _selectionScroll.viewport = _selectionViewport;
            _selectionScroll.content = _selectionContent;
            _selectionScroll.horizontal = false;
            _selectionScroll.vertical = true;
            _selectionScroll.movementType = ScrollRect.MovementType.Clamped;
            _selectionScroll.scrollSensitivity = 18f;
            _selectionScrollbar = CreateScrollbar(_selectionPanel,
                _selectionScroll, "SelectionScrollbar");
            _selectionPanel.gameObject.SetActive(false);
        }

        private void BuildActions()
        {
            for (int section = 0; section < Sections.Length; section++)
            {
                _sectionLabels.Add(CreateText(_actionContent,
                    "Section" + section, 9, TextAnchor.MiddleLeft,
                    new Color(0.94f, 0.78f, 0.38f, 1f)));
                for (int i = 0; i < Sections[section].Length; i++)
                {
                    CourtDispositionAction action = Sections[section][i];
                    Button button = CreateButton(_actionContent,
                        "Action_" + action, ActionText(action), null,
                        out Text text);
                    _actions.Add(new ActionControl
                    {
                        Action = action,
                        Object = button.gameObject,
                        Button = button,
                        Text = text,
                        Tip = button.GetComponent<TipButton>()
                    });
                }
            }
        }

        private void ApplyWindowLayout()
        {
            if (_root == null) return;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background =
                BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find(
                "TitleBackground");
            RectTransform titleRect =
                titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(
                    _windowSize.x * 0.56f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_court_disposition_window_title",
                    "Court Disposition");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            DisableNativeScroll(width, height);
            SetRect(_root, 0f, 0f, width, height);
            SetRect(_header, 6f, 5f, width - 12f, 58f);
            SetRect(_flagBackground.rectTransform, 6f, 7f, 42f, 42f);
            SetRect(_flagIcon.rectTransform, 6f, 7f, 42f, 42f);
            SetRect(_rulerPortraitRoot, 54f, 7f, 42f, 42f);
            SetRect(_targetPortraitRoot, 101f, 7f, 42f, 42f);
            SetRect(_identity.rectTransform, 150f, 6f,
                Mathf.Max(1f, width - 238f), 27f);
            SetRect(_points.rectTransform, 150f, 32f,
                Mathf.Max(1f, width - 238f), 19f);
            SetRect(_kingdomBack.GetComponent<RectTransform>(),
                width - 80f, 7f, 68f, 42f);

            float bodyTop = 68f;
            float bodyHeight = Mathf.Max(80f, height - bodyTop - 31f);
            SetRect(_actionViewport, 6f, bodyTop,
                width - 22f, bodyHeight);
            SetRect(_actionScrollbar.GetComponent<RectTransform>(),
                width - 14f, bodyTop, 8f, bodyHeight);
            LayoutActions(width - 34f, bodyHeight);

            SetRect(_selectionPanel, 6f, bodyTop,
                width - 12f, bodyHeight);
            SetRect(_selectionTitle.rectTransform, 7f, 3f,
                width - 92f, 22f);
            SetRect(_selectionBack.GetComponent<RectTransform>(),
                width - 83f, 3f, 62f, 21f);
            SetRect(_selectionViewport, 6f, 29f,
                width - 34f, Mathf.Max(42f, bodyHeight - 35f));
            SetRect(_selectionScrollbar.GetComponent<RectTransform>(),
                width - 22f, 29f, 8f,
                Mathf.Max(42f, bodyHeight - 35f));
            LayoutSelectionRows(width - 46f,
                Mathf.Max(42f, bodyHeight - 35f));
            SetRect(_feedback.rectTransform, 8f, height - 27f,
                width - 16f, 22f);
            _chrome?.RepositionResizeHandle();
        }

        private void LayoutActions(float pWidth, float pViewportHeight)
        {
            int columns = pWidth >= 520f ? 3 : 2;
            float gap = 4f;
            float buttonWidth = (pWidth - gap * (columns - 1)) / columns;
            float y = 5f;
            int actionIndex = 0;
            for (int section = 0; section < Sections.Length; section++)
            {
                Text label = _sectionLabels[section];
                label.text = SectionText(section);
                SetRect(label.rectTransform, 5f, y, pWidth - 10f, 17f);
                y += 19f;
                int count = Sections[section].Length;
                for (int i = 0; i < count; i++)
                {
                    int row = i / columns;
                    int column = i % columns;
                    SetRect(_actions[actionIndex++].Object
                            .GetComponent<RectTransform>(),
                        5f + column * (buttonWidth + gap),
                        y + row * 27f, buttonWidth, 23f);
                }
                y += ((count + columns - 1) / columns) * 27f + 3f;
            }
            _actionContent.sizeDelta = new Vector2(pWidth,
                Mathf.Max(pViewportHeight, y + 3f));
        }

        private void LayoutSelectionRows(float pWidth,
            float pViewportHeight)
        {
            for (int i = 0; i < _selectionPool.Count; i++)
                SetRect(_selectionPool[i].Object.GetComponent<RectTransform>(),
                    4f, 4f + i * 27f, pWidth - 8f, 23f);
            _selectionContent.sizeDelta = new Vector2(pWidth,
                Mathf.Max(pViewportHeight,
                    8f + _selectionPool.Count * 27f));
        }

        private void BeginSelection(string pTitle)
        {
            _selectionTitle.text = pTitle ?? "";
            _actionViewport.gameObject.SetActive(false);
            _actionScrollbar.gameObject.SetActive(false);
            _selectionPanel.gameObject.SetActive(true);
            for (int i = 0; i < _selectionPool.Count; i++)
                _selectionPool[i].Object.SetActive(false);
        }

        private void FinishSelection(int pCount)
        {
            if (pCount == 0)
            {
                _feedback.text = AW_L10n.Text(
                    "aw_court_disposition_no_options",
                    "No eligible options are available.");
                ShowActions();
                return;
            }
            LayoutSelectionRows(_selectionViewport.rect.width,
                _selectionViewport.rect.height);
            _selectionScroll.verticalNormalizedPosition = 1f;
        }

        private SelectionControl GetSelection(int pIndex)
        {
            while (_selectionPool.Count <= pIndex)
            {
                Button button = CreateButton(_selectionContent,
                    "Selection_" + _selectionPool.Count, "", null,
                    out Text text);
                _selectionPool.Add(new SelectionControl
                {
                    Object = button.gameObject,
                    Button = button,
                    Text = text,
                    Tip = button.GetComponent<TipButton>()
                });
            }
            SelectionControl control = _selectionPool[pIndex];
            control.Object.SetActive(true);
            return control;
        }

        private static void BindSelection(SelectionControl pControl,
            string pLabel, CourtDispositionPreview pPreview, Action pAction)
        {
            pControl.Text.text = pLabel ?? "";
            pControl.Button.onClick.RemoveAllListeners();
            pControl.Button.interactable = pPreview.Allowed;
            if (pPreview.Allowed)
                pControl.Button.onClick.AddListener(() => pAction?.Invoke());
            BindTip(pControl.Tip, pControl.Object, pControl.Text.text,
                PreviewDescription(pPreview));
        }

        private void ShowActions()
        {
            if (_actionViewport != null) _actionViewport.gameObject.SetActive(true);
            if (_actionScrollbar != null) _actionScrollbar.gameObject.SetActive(true);
            if (_selectionPanel != null)
                _selectionPanel.gameObject.SetActive(false);
        }

        private void BackToKingdom()
        {
            AW_LineageWindowIds.ShowKingdom(_kingdomId);
        }

        private void LoadCityCandidates(Kingdom pKingdom)
        {
            _cityCandidates.Clear();
            if (pKingdom?.data == null) return;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && !city.isRekt())
                    _cityCandidates.Add(city);
            _cityCandidates.Sort((left, right) =>
            {
                int population = SafePopulation(right).CompareTo(
                    SafePopulation(left));
                return population != 0
                    ? population
                    : left.id.CompareTo(right.id);
            });
        }

        private void DisableActions(string pReason)
        {
            for (int i = 0; i < _actions.Count; i++)
            {
                ActionControl control = _actions[i];
                control.Button.interactable = false;
                control.Button.onClick.RemoveAllListeners();
                BindTip(control.Tip, control.Object,
                    ActionText(control.Action), ReasonText(pReason));
            }
        }

        private void RefreshFlag(Kingdom pKingdom)
        {
            string bannerId = "";
            try { bannerId = pKingdom.getActorAsset()?.banner_id ?? ""; }
            catch { }
            KingdomFlagBuilder.Build(bannerId,
                pKingdom.data.banner_icon_id,
                pKingdom.data.banner_background_id,
                HistoryColors.FromKingdom(pKingdom),
                pKingdom.data.color_id, _flagBackground, _flagIcon);
        }

        private static void SetPortrait(RectTransform pRoot,
            UiUnitAvatarElement pPortrait, Actor pActor)
        {
            bool visible = pRoot != null && pPortrait != null &&
                           pActor?.data != null && !pActor.isRekt() &&
                           pActor.isAlive();
            if (pRoot != null) pRoot.gameObject.SetActive(visible);
            if (!visible) return;
            pPortrait.enabled = true;
            if (pPortrait.avatarLoader != null)
                pPortrait.avatarLoader.enabled = true;
            pPortrait.show(pActor);
        }

        private void EnsurePortraits()
        {
            if (_rulerPortrait == null)
                _rulerPortrait = CreatePortrait(_rulerPortraitRoot);
            if (_targetPortrait == null)
                _targetPortrait = CreatePortrait(_targetPortraitRoot);
        }

        private static string PreviewDescription(
            CourtDispositionPreview pPreview)
        {
            string cost = string.Format(AW_L10n.Text(
                "aw_court_disposition_cost", "Political cost: {0}"),
                pPreview.Cost);
            return pPreview.Allowed
                ? cost
                : ReasonText(pPreview.Reason) + "\n" + cost;
        }

        private static string ActionText(CourtDispositionAction pAction)
        {
            return AW_L10n.Text("aw_court_disposition_action_" +
                                ActionKey(pAction), pAction.ToString());
        }

        private static string ActionKey(CourtDispositionAction pAction)
        {
            return pAction switch
            {
                CourtDispositionAction.PromoteRank => "promote_rank",
                CourtDispositionAction.DemoteRank => "demote_rank",
                CourtDispositionAction.DismissOffice => "dismiss_office",
                CourtDispositionAction.GrantNobleRank => "grant_noble_rank",
                CourtDispositionAction.GrantFief => "grant_fief",
                CourtDispositionAction.RevokeFief => "revoke_fief",
                CourtDispositionAction.GrantSurname => "grant_surname",
                CourtDispositionAction.ExpelLineage => "expel_lineage",
                CourtDispositionAction.RelocateFeudatory =>
                    "relocate_feudatory",
                CourtDispositionAction.ReclaimFeudatoryCity =>
                    "reclaim_feudatory_city",
                _ => "unknown"
            };
        }

        private static string OutcomeText(CourtDispositionOutcome pOutcome)
        {
            string key = pOutcome switch
            {
                CourtDispositionOutcome.CleanFailure => "clean_failure",
                _ => pOutcome.ToString().ToLowerInvariant()
            };
            return AW_L10n.Text("aw_court_disposition_outcome_" + key,
                pOutcome.ToString());
        }

        private static string ReasonText(string pReason)
        {
            if (string.IsNullOrEmpty(pReason)) return "";
            return AW_L10n.Text("aw_court_disposition_reason_" + pReason,
                pReason);
        }

        private static string SectionText(int pSection)
        {
            string[] keys = { "office", "title_fief", "lineage", "feudatory" };
            string[] fallbacks =
                { "Office", "Title and Fief", "Lineage", "Feudatory" };
            return AW_L10n.Text("aw_court_disposition_section_" +
                                keys[pSection], fallbacks[pSection]);
        }

        private static string NobleRankName(int pRank)
        {
            return AW_L10n.Text(NobleRankRules.TitleKey(pRank,
                    NobleTitleStyle.Male),
                NobleRankRules.TitleFallback(pRank, NobleTitleStyle.Male));
        }

        private static void BindTip(TipButton pTip, GameObject pObject,
            string pTitle, string pDescription)
        {
            pTip.enabled = true;
            pTip.showOnClick = false;
            pTip.type = AW_RawTooltip.TYPE;
            pTip.hoverAction = () => Tooltip.show(pObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = pTitle ?? "",
                    tip_description = pDescription ?? ""
                });
        }

        private void DisableNativeScroll(float pWidth, float pHeight)
        {
            Transform scroll = BackgroundTransform?.Find("Scroll View");
            RectTransform scrollRect = scroll?.GetComponent<RectTransform>();
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(pWidth, pHeight);
                scrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect native = scroll?.GetComponent<ScrollRect>();
            if (native != null)
            {
                native.horizontal = false;
                native.vertical = false;
            }
            Transform nativeBar = scroll?.Find("Scrollbar Vertical");
            if (nativeBar != null) nativeBar.gameObject.SetActive(false);
            RectTransform content =
                ContentTransform?.GetComponent<RectTransform>();
            if (content != null) content.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static GameObject CreateObject(string pName,
            Transform pParent)
        {
            var obj = new GameObject(pName, typeof(RectTransform));
            obj.transform.SetParent(pParent, false);
            return obj;
        }

        private static RectTransform CreatePanel(string pName,
            Transform pParent)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyPanel(obj.GetComponent<Image>(), 0.84f);
            return obj.GetComponent<RectTransform>();
        }

        private static Image CreateImage(string pName, Transform pParent)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            return obj.GetComponent<Image>();
        }

        private static UiUnitAvatarElement CreatePortrait(string pName,
            Transform pParent, out RectTransform pRoot)
        {
            pRoot = CreateObject(pName, pParent).GetComponent<RectTransform>();
            return CreatePortrait(pRoot);
        }

        private static UiUnitAvatarElement CreatePortrait(
            RectTransform pRoot)
        {
            if (pRoot == null) return null;
            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab == null)
            {
                pRoot.gameObject.SetActive(false);
                return null;
            }

            UiUnitAvatarElement portrait =
                UnityEngine.Object.Instantiate(prefab, pRoot);
            RectTransform rect = portrait.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            pRoot.gameObject.SetActive(false);
            return portrait;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor, Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = pColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
            return text;
        }

        private static Button CreateButton(Transform pParent, string pName,
            string pLabel, Action pAction, out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.96f);
            Button button = obj.GetComponent<Button>();
            if (pAction != null) button.onClick.AddListener(() => pAction());
            pText = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleCenter, Color.white);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(3f, 1f);
            pText.rectTransform.offsetMax = new Vector2(-3f, -1f);
            pText.resizeTextForBestFit = true;
            pText.resizeTextMinSize = 6;
            pText.resizeTextMaxSize = 8;
            pText.text = pLabel ?? "";
            return button;
        }

        private static Scrollbar CreateScrollbar(Transform pParent,
            ScrollRect pScroll, string pName)
        {
            var bar = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Scrollbar));
            bar.transform.SetParent(pParent, false);
            bar.GetComponent<Image>().color =
                new Color(0.08f, 0.075f, 0.065f, 0.98f);
            var area = new GameObject("Sliding Area",
                typeof(RectTransform));
            area.transform.SetParent(bar.transform, false);
            RectTransform areaRect = area.GetComponent<RectTransform>();
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(1f, 1f);
            areaRect.offsetMax = new Vector2(-1f, -1f);
            var handle = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handle.transform.SetParent(area.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            Image image = handle.GetComponent<Image>();
            image.color = new Color(0.76f, 0.61f, 0.28f, 1f);
            Scrollbar scrollbar = bar.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = image;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static void SetupTopLeft(RectTransform pRect)
        {
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = Vector2.zero;
        }

        private static void SetRect(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            SetupTopLeft(pRect);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth),
                Mathf.Max(1f, pHeight));
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            string hex = HistoryColors.FromKingdom(pKingdom);
            return !string.IsNullOrEmpty(hex) &&
                   ColorUtility.TryParseHtmlString(hex, out Color color)
                ? new Color(color.r, color.g, color.b, 1f)
                : Color.white;
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
