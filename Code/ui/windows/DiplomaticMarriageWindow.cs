using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class DiplomaticMarriageWindow :
        AbstractWindow<DiplomaticMarriageWindow>
    {
        private static readonly Vector2 DefaultSize = new(520f, 410f);
        private static readonly Vector2 MinimumSize = new(520f, 410f);
        private static readonly Vector2 MaximumSize = new(820f, 620f);
        private static long _requesterKingdomId = -1L;
        private static long _responderKingdomId = -1L;

        private readonly List<DiplomaticMarriageCandidate>
            _visibleRequesterCandidates = new();
        private readonly List<DiplomaticMarriageCandidate>
            _visibleResponderCandidates = new();
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private CandidatePanel _requesterPanel;
        private CandidatePanel _responderPanel;
        private Text _status;
        private Button _confirmButton;
        private Text _confirmText;
        private Button _cancelButton;
        private Button _diplomacyBackButton;
        private Button _requesterMaleDirectionButton;
        private Button _requesterFemaleDirectionButton;
        private Text _requesterMaleDirectionText;
        private Text _requesterFemaleDirectionText;
        private WideWindowChrome _chrome;
        private DiplomaticMarriageCandidatePools _pools;
        private long _selectedRequesterActorId = -1L;
        private long _selectedResponderActorId = -1L;
        private bool _rebuildPools = true;
        private bool _commandPending;
        private bool _commandRefreshRequested;
        private RoyalMarriageDirection _direction =
            RoyalMarriageDirection.RequesterMaleResponderFemale;

        private sealed class CandidatePanel
        {
            public RectTransform Root;
            public bool RequesterSide;
            public Image FlagBackground;
            public Image FlagIcon;
            public TipButton FlagTip;
            public GameObject AvatarRoot;
            public UiUnitAvatarElement Avatar;
            public Text KingdomName;
            public Text CandidateName;
            public Text CandidateDetail;
            public Text Counter;
            public Button Previous;
            public Button Next;
            public RectTransform ListRoot;
            public RectTransform ListContent;
            public readonly List<CandidateRow> Rows = new();
        }

        private sealed class CandidateRow
        {
            public GameObject Root;
            public Image Background;
            public Button Button;
            public Text Label;
            public TipButton Tip;
        }

        public static void Open(long pRequesterKingdomId,
            long pResponderKingdomId)
        {
            _requesterKingdomId = pRequesterKingdomId;
            _responderKingdomId = pResponderKingdomId;
            if (Instance != null)
                Instance._direction = RoyalMarriageDirection
                    .RequesterMaleResponderFemale;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.DIPLOMATIC_MARRIAGE);
            if (Instance != null) Instance._rebuildPools = true;
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.DIPLOMATIC_MARRIAGE,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize => { _windowSize = pSize; ApplyLayout(); Refresh(); },
                DefaultSize, MinimumSize, MaximumSize);
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
        }

        public override void OnNormalEnable() => Refresh();

        private void Update()
        {
            if (!_commandRefreshRequested) return;
            _commandRefreshRequested = false;
            _commandPending = false;
            _rebuildPools = true;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            _commandRefreshRequested = true;
        }

        public void Refresh()
        {
            EnsureUi();
            SetWindowTitle();
            ApplyLayout();
            Kingdom requester = FindKingdom(_requesterKingdomId);
            Kingdom responder = FindKingdom(_responderKingdomId);
            if (requester?.data == null || responder?.data == null ||
                requester.isRekt() || responder.isRekt())
            {
                BindUnavailableRealms();
                return;
            }
            if (_rebuildPools || _pools == null)
                RebuildPools(requester, responder);
            BindDirectionButtons();
            BindPanel(_requesterPanel, requester,
                FindCandidate(_pools?.RequesterCandidates,
                    _selectedRequesterActorId),
                _visibleRequesterCandidates, _selectedRequesterActorId,
                SelectRequester);
            BindPanel(_responderPanel, responder,
                FindCandidate(_pools?.ResponderCandidates,
                    _selectedResponderActorId),
                _visibleResponderCandidates, _selectedResponderActorId,
                SelectResponder);
            DiplomaticMarriageCandidate selectedRequester = FindCandidate(
                _pools?.RequesterCandidates, _selectedRequesterActorId);
            DiplomaticMarriageCandidate selectedResponder = FindCandidate(
                _pools?.ResponderCandidates, _selectedResponderActorId);
            bool validPair = DiplomaticMarriageService.CanPairInDirection(
                selectedRequester, selectedResponder, _direction);
            _confirmButton.interactable = validPair && !_commandPending;
            _confirmText.text = AW_L10n.Text(
                "aw_diplomatic_marriage_confirm", "Send marriage proposal");
            if (validPair)
                _status.text = AW_L10n.Text(
                    "aw_diplomatic_marriage_pair_valid",
                    "Valid royal marriage pair");
            else if (!string.IsNullOrEmpty(_pools?.Reason))
                _status.text = DiplomacyConversationWindow.ProposalFailure(
                    _pools.Reason);
            else
                _status.text = AW_L10n.Text(
                    "aw_diplomatic_marriage_direction_unavailable",
                    "No eligible pair in this direction");
            Canvas.ForceUpdateCanvases();
        }

        private void BindUnavailableRealms()
        {
            _pools = null;
            _visibleRequesterCandidates.Clear();
            _visibleResponderCandidates.Clear();
            _selectedRequesterActorId = -1L;
            _selectedResponderActorId = -1L;
            BindDirectionButtons();
            _requesterMaleDirectionButton.interactable = false;
            _requesterFemaleDirectionButton.interactable = false;
            BindUnavailablePanel(_requesterPanel);
            BindUnavailablePanel(_responderPanel);
            _confirmButton.interactable = false;
            _confirmText.text = AW_L10n.Text(
                "aw_diplomatic_marriage_confirm", "Send marriage proposal");
            _status.text = AW_L10n.Text(
                "aw_diplomatic_marriage_realms_unavailable",
                "One of the realms is no longer available");
            Canvas.ForceUpdateCanvases();
        }

        private static void BindUnavailablePanel(CandidatePanel pPanel)
        {
            if (pPanel == null) return;
            pPanel.FlagBackground.gameObject.SetActive(false);
            pPanel.AvatarRoot.SetActive(false);
            pPanel.KingdomName.text = "";
            pPanel.CandidateName.text = AW_L10n.Text(
                "aw_diplomatic_marriage_no_candidate", "No candidate");
            pPanel.CandidateDetail.text = "";
            pPanel.Counter.text = "0 / 0";
            pPanel.Previous.interactable = false;
            pPanel.Next.interactable = false;
            for (int i = 0; i < pPanel.Rows.Count; i++)
                pPanel.Rows[i].Root.SetActive(false);
        }

        private void RebuildPools(Kingdom pRequester, Kingdom pResponder)
        {
            _rebuildPools = false;
            _pools = DiplomaticMarriageService.BuildCandidatePools(
                pRequester, pResponder);
            RebuildDirectionSelection();
        }

        private void RebuildDirectionSelection()
        {
            _visibleRequesterCandidates.Clear();
            _visibleResponderCandidates.Clear();
            _selectedRequesterActorId = -1L;
            _selectedResponderActorId = -1L;
            if (_pools == null) return;
            for (int i = 0; i < _pools.RequesterCandidates.Count; i++)
            {
                DiplomaticMarriageCandidate candidate =
                    _pools.RequesterCandidates[i];
                if (MatchesDirection(candidate, requesterSide: true))
                    _visibleRequesterCandidates.Add(candidate);
            }
            for (int i = 0; i < _pools.ResponderCandidates.Count; i++)
            {
                DiplomaticMarriageCandidate candidate =
                    _pools.ResponderCandidates[i];
                if (MatchesDirection(candidate, requesterSide: false))
                    _visibleResponderCandidates.Add(candidate);
            }
            for (int i = 0; i < _visibleRequesterCandidates.Count; i++)
            for (int j = 0; j < _visibleResponderCandidates.Count; j++)
            {
                DiplomaticMarriageCandidate requester =
                    _visibleRequesterCandidates[i];
                DiplomaticMarriageCandidate responder =
                    _visibleResponderCandidates[j];
                if (!DiplomaticMarriageService.CanPairInDirection(requester,
                        responder, _direction))
                    continue;
                _selectedRequesterActorId = requester.ActorId;
                _selectedResponderActorId = responder.ActorId;
                FilterResponders(requester);
                return;
            }
            if (_visibleRequesterCandidates.Count > 0)
                _selectedRequesterActorId =
                    _visibleRequesterCandidates[0].ActorId;
            if (_visibleResponderCandidates.Count > 0)
                _selectedResponderActorId =
                    _visibleResponderCandidates[0].ActorId;
        }

        private void SelectDirection(RoyalMarriageDirection pDirection)
        {
            if (_commandPending || _direction == pDirection) return;
            _direction = pDirection;
            RebuildDirectionSelection();
            Refresh();
        }

        private void BindDirectionButtons()
        {
            if (_requesterMaleDirectionButton == null ||
                _requesterFemaleDirectionButton == null)
                return;
            _requesterMaleDirectionText.text = AW_L10n.Text(
                "aw_diplomatic_marriage_direction_our_male",
                "Our male + their female");
            _requesterFemaleDirectionText.text = AW_L10n.Text(
                "aw_diplomatic_marriage_direction_our_female",
                "Our female + their male");
            bool requesterMale = _direction == RoyalMarriageDirection
                .RequesterMaleResponderFemale;
            BindDirectionButton(_requesterMaleDirectionButton,
                _requesterMaleDirectionText, requesterMale);
            BindDirectionButton(_requesterFemaleDirectionButton,
                _requesterFemaleDirectionText, !requesterMale);
        }

        private void BindDirectionButton(Button pButton, Text pText,
            bool pSelected)
        {
            pButton.interactable = !_commandPending;
            pButton.GetComponent<Image>().color = pSelected
                ? new Color(.64f, .43f, .16f, 1f)
                : new Color(.30f, .28f, .24f, 1f);
            pText.color = pSelected
                ? new Color(1f, .86f, .42f, 1f)
                : Color.white;
        }

        private bool MatchesDirection(DiplomaticMarriageCandidate pCandidate,
            bool requesterSide)
        {
            return pCandidate != null &&
                   DiplomacyActionExpansionRules.MatchesMarriageDirection(
                       _direction, requesterSide, pCandidate.Facts.Male);
        }

        private void SelectRequester(long pActorId)
        {
            if (_commandPending) return;
            DiplomaticMarriageCandidate candidate = FindCandidate(
                _pools?.RequesterCandidates, pActorId);
            if (!MatchesDirection(candidate, requesterSide: true)) return;
            _selectedRequesterActorId = candidate.ActorId;
            FilterResponders(candidate);
            Refresh();
        }

        private void SelectResponder(long pActorId)
        {
            if (_commandPending) return;
            DiplomaticMarriageCandidate candidate = FindCandidate(
                _pools?.ResponderCandidates, pActorId);
            if (!MatchesDirection(candidate, requesterSide: false)) return;
            _selectedResponderActorId = candidate.ActorId;
            FilterRequesters(candidate);
            Refresh();
        }

        private void FilterResponders(DiplomaticMarriageCandidate pRequester)
        {
            _visibleResponderCandidates.Clear();
            if (_pools != null)
                for (int i = 0; i < _pools.ResponderCandidates.Count; i++)
                {
                    DiplomaticMarriageCandidate responder =
                        _pools.ResponderCandidates[i];
                    if (MatchesDirection(responder, requesterSide: false) &&
                        DiplomaticMarriageService.CanPairInDirection(pRequester,
                            responder, _direction))
                        _visibleResponderCandidates.Add(responder);
                }
            if (!Contains(_visibleResponderCandidates,
                    _selectedResponderActorId))
                _selectedResponderActorId = _visibleResponderCandidates.Count > 0
                    ? _visibleResponderCandidates[0].ActorId
                    : -1L;
        }

        private void FilterRequesters(DiplomaticMarriageCandidate pResponder)
        {
            _visibleRequesterCandidates.Clear();
            if (_pools != null)
                for (int i = 0; i < _pools.RequesterCandidates.Count; i++)
                {
                    DiplomaticMarriageCandidate requester =
                        _pools.RequesterCandidates[i];
                    if (MatchesDirection(requester, requesterSide: true) &&
                        DiplomaticMarriageService.CanPairInDirection(requester,
                            pResponder, _direction))
                        _visibleRequesterCandidates.Add(requester);
                }
            if (!Contains(_visibleRequesterCandidates,
                    _selectedRequesterActorId))
                _selectedRequesterActorId = _visibleRequesterCandidates.Count > 0
                    ? _visibleRequesterCandidates[0].ActorId
                    : -1L;
        }

        private void CycleRequester(int pDirection)
        {
            if (_commandPending) return;
            long actorId = Cycle(_visibleRequesterCandidates,
                _selectedRequesterActorId, pDirection);
            if (actorId >= 0) SelectRequester(actorId);
        }

        private void CycleResponder(int pDirection)
        {
            if (_commandPending) return;
            long actorId = Cycle(_visibleResponderCandidates,
                _selectedResponderActorId, pDirection);
            if (actorId >= 0) SelectResponder(actorId);
        }

        private void Confirm()
        {
            if (_commandPending) return;
            Kingdom requester = FindKingdom(_requesterKingdomId);
            Kingdom responder = FindKingdom(_responderKingdomId);
            long requesterActorId = _selectedRequesterActorId;
            long responderActorId = _selectedResponderActorId;
            if (requester?.data == null || responder?.data == null) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.CreateDiplomacyProposal(requester.id,
                        responder.id,
                        DiplomacyProposalType.RoyalMarriage.ToString(),
                        requesterActorId, responderActorId));
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted)
            {
                WorldTip.showNow(
                    DiplomacyConversationWindow.ProposalFailure(
                        result.MessageKey),
                    false, "top");
                _rebuildPools = true;
                Refresh();
                return;
            }
            DiplomacyConversationWindow.Open(requester.id);
        }

        private void BackToDiplomacy()
        {
            DiplomacyConversationWindow.Open(_requesterKingdomId);
        }

        private void Cancel()
        {
            ScrollWindow window = ScrollWindow.getCurrentWindow();
            if (window != null && window.screen_id ==
                AW_LineageWindowIds.DIPLOMATIC_MARRIAGE)
                window.clickCloseButton();
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            _root = new GameObject("DiplomaticMarriageRoot",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(ContentTransform, false);
            _requesterPanel = CreateCandidatePanel(_root, "Requester",
                requesterSide: true,
                () => CycleRequester(-1), () => CycleRequester(1));
            _responderPanel = CreateCandidatePanel(_root, "Responder",
                requesterSide: false,
                () => CycleResponder(-1), () => CycleResponder(1));
            _requesterMaleDirectionButton = CreateTextButton(_root,
                "RequesterMaleDirection",
                () => SelectDirection(RoyalMarriageDirection
                    .RequesterMaleResponderFemale),
                out _requesterMaleDirectionText);
            _requesterFemaleDirectionButton = CreateTextButton(_root,
                "RequesterFemaleDirection",
                () => SelectDirection(RoyalMarriageDirection
                    .RequesterFemaleResponderMale),
                out _requesterFemaleDirectionText);
            _status = CreateText(_root, "Status", 9,
                TextAnchor.MiddleCenter);
            _status.color = new Color(.84f, .78f, .66f, 1f);
            _confirmButton = CreateTextButton(_root, "Confirm", Confirm,
                out _confirmText);
            _diplomacyBackButton = CreateTextButton(_root,
                "BackToDiplomacy", BackToDiplomacy, out Text backText);
            backText.text = AW_L10n.Text(
                "aw_diplomatic_marriage_back", "Back to diplomacy");
            _cancelButton = CreateTextButton(_root, "Cancel", Cancel,
                out Text cancelText);
            cancelText.text = AW_L10n.Text(
                "aw_diplomatic_marriage_cancel", "Cancel");
            SetWindowTitle();
        }

        private void SetWindowTitle()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_diplomatic_marriage_title", "Royal Marriage");
                window.titleText.raycastTarget = false;
            }
        }

        private CandidatePanel CreateCandidatePanel(Transform pParent,
            string pName, bool requesterSide,
            UnityEngine.Events.UnityAction pPrevious,
            UnityEngine.Events.UnityAction pNext)
        {
            var panel = new CandidatePanel
            {
                RequesterSide = requesterSide
            };
            panel.Root = new GameObject(pName, typeof(RectTransform),
                typeof(Image)).GetComponent<RectTransform>();
            panel.Root.SetParent(pParent, false);
            panel.Root.GetComponent<Image>().color =
                new Color(.09f, .085f, .072f, .96f);
            panel.FlagBackground = CreateImage(panel.Root, "Flag",
                Color.white);
            panel.FlagIcon = CreateImage(panel.FlagBackground.transform,
                "FlagIcon", Color.white);
            panel.FlagIcon.rectTransform.anchorMin = Vector2.zero;
            panel.FlagIcon.rectTransform.anchorMax = Vector2.one;
            panel.FlagIcon.rectTransform.offsetMin = Vector2.zero;
            panel.FlagIcon.rectTransform.offsetMax = Vector2.zero;
            panel.FlagTip = panel.FlagBackground.gameObject
                .AddComponent<TipButton>();
            panel.FlagTip.type = AW_RawTooltip.TYPE;
            panel.AvatarRoot = new GameObject("Portrait",
                typeof(RectTransform), typeof(TipButton));
            panel.AvatarRoot.transform.SetParent(panel.Root, false);
            panel.KingdomName = CreateText(panel.Root, "Kingdom", 9,
                TextAnchor.MiddleLeft);
            panel.CandidateName = CreateText(panel.Root, "Candidate", 11,
                TextAnchor.MiddleLeft);
            panel.CandidateDetail = CreateText(panel.Root, "Detail", 8,
                TextAnchor.UpperLeft);
            panel.Counter = CreateText(panel.Root, "Counter", 8,
                TextAnchor.MiddleCenter);
            panel.Previous = CreateIconButton(panel.Root, "Previous",
                pPrevious, previous: true,
                AW_L10n.Text("aw_diplomatic_marriage_previous",
                    "Previous candidate"));
            panel.Next = CreateIconButton(panel.Root, "Next", pNext,
                previous: false,
                AW_L10n.Text("aw_diplomatic_marriage_next",
                    "Next candidate"));
            CreateScrollArea(panel.Root, out panel.ListRoot,
                out panel.ListContent);
            return panel;
        }

        private void BindPanel(CandidatePanel pPanel, Kingdom pKingdom,
            DiplomaticMarriageCandidate pCandidate,
            IReadOnlyList<DiplomaticMarriageCandidate> pVisibleCandidates,
            long pSelectedActorId, Action<long> pSelect)
        {
            BindFlag(pPanel, pKingdom);
            pPanel.KingdomName.text = AW_L10n.Text(
                    pPanel.RequesterSide
                        ? "aw_diplomatic_marriage_our_house"
                        : "aw_diplomatic_marriage_other_house",
                    pPanel.RequesterSide
                        ? "Our royal house"
                        : "Other royal house") + "  |  " +
                RulerAppellationService.GetProjectedStateName(pKingdom);
            bool hasCandidate = pCandidate?.Actor?.data != null;
            pPanel.AvatarRoot.SetActive(hasCandidate);
            if (hasCandidate)
            {
                EnsureAvatar(pPanel);
                if (pPanel.Avatar != null)
                    pPanel.Avatar.show(pCandidate.Actor);
                pPanel.CandidateName.text = pCandidate.Actor.getName() ?? "";
                pPanel.CandidateDetail.text = CandidateDetail(pCandidate);
                TipButton portraitTip = pPanel.AvatarRoot
                    .GetComponent<TipButton>();
                string title = pPanel.CandidateName.text;
                string detail = pPanel.CandidateDetail.text + "\n" +
                                pPanel.KingdomName.text;
                portraitTip.type = AW_RawTooltip.TYPE;
                portraitTip.hoverAction = () => Tooltip.show(
                    pPanel.AvatarRoot, AW_RawTooltip.TYPE,
                    new TooltipData
                    {
                        tip_name = title,
                        tip_description = detail
                    });
            }
            else
            {
                pPanel.CandidateName.text = AW_L10n.Text(
                    "aw_diplomatic_marriage_no_candidate", "No candidate");
                pPanel.CandidateDetail.text = "";
            }
            int selectedIndex = IndexOf(pVisibleCandidates,
                pSelectedActorId);
            pPanel.Counter.text = pVisibleCandidates.Count == 0
                ? "0 / 0"
                : (Math.Max(0, selectedIndex) + 1) + " / " +
                  pVisibleCandidates.Count;
            bool many = pVisibleCandidates.Count > 1;
            pPanel.Previous.interactable = many && !_commandPending;
            pPanel.Next.interactable = many && !_commandPending;
            BindRows(pPanel, pVisibleCandidates, pSelectedActorId, pSelect,
                pKingdom);
        }

        private void BindRows(CandidatePanel pPanel,
            IReadOnlyList<DiplomaticMarriageCandidate> pCandidates,
            long pSelectedActorId, Action<long> pSelect, Kingdom pKingdom)
        {
            for (int i = 0; i < pCandidates.Count; i++)
            {
                while (pPanel.Rows.Count <= i)
                    pPanel.Rows.Add(CreateCandidateRow(pPanel.ListContent));
                DiplomaticMarriageCandidate candidate = pCandidates[i];
                CandidateRow row = pPanel.Rows[i];
                bool selected = candidate.ActorId == pSelectedActorId;
                row.Background.color = selected
                    ? new Color(.38f, .29f, .15f, .98f)
                    : new Color(.14f, .13f, .11f, .96f);
                row.Label.text = (candidate.Actor.getName() ?? "") + "  " +
                                 ShortCandidateDetail(candidate);
                row.Button.onClick.RemoveAllListeners();
                long actorId = candidate.ActorId;
                row.Button.onClick.AddListener(() => pSelect(actorId));
                row.Button.interactable = !_commandPending;
                string title = candidate.Actor.getName() ?? "";
                string description = CandidateDetail(candidate) + "\n" +
                                     RulerAppellationService
                                         .GetProjectedStateName(pKingdom);
                row.Tip.hoverAction = () => Tooltip.show(row.Root,
                    AW_RawTooltip.TYPE, new TooltipData
                    {
                        tip_name = title,
                        tip_description = description
                    });
                row.Root.SetActive(true);
            }
            for (int i = pCandidates.Count; i < pPanel.Rows.Count; i++)
                pPanel.Rows[i].Root.SetActive(false);
        }

        private static void EnsureAvatar(CandidatePanel pPanel)
        {
            if (pPanel.Avatar != null) return;
            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab == null) return;
            pPanel.Avatar = UnityEngine.Object.Instantiate(prefab,
                pPanel.AvatarRoot.transform);
            RectTransform rect = pPanel.Avatar.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void BindFlag(CandidatePanel pPanel, Kingdom pKingdom)
        {
            pPanel.FlagBackground.gameObject.SetActive(true);
            string bannerId = "";
            try { bannerId = pKingdom.getActorAsset()?.banner_id ?? ""; }
            catch { }
            KingdomFlagBuilder.Build(bannerId,
                pKingdom.data.banner_icon_id,
                pKingdom.data.banner_background_id,
                HistoryColors.FromKingdom(pKingdom), pKingdom.data.color_id,
                pPanel.FlagBackground, pPanel.FlagIcon);
            string kingdomName =
                RulerAppellationService.GetProjectedStateName(pKingdom);
            pPanel.FlagTip.hoverAction = () => Tooltip.show(
                pPanel.FlagBackground.gameObject, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = kingdomName,
                    tip_description = AW_L10n.Text(
                        "aw_diplomatic_marriage_royal_house",
                        "Royal house")
                });
        }

        private static string CandidateDetail(
            DiplomaticMarriageCandidate pCandidate)
        {
            string kinshipKey = pCandidate.Kinship switch
            {
                RoyalMarriageKinship.Ruler =>
                    "aw_diplomatic_marriage_ruler",
                RoyalMarriageKinship.DirectChild =>
                    "aw_diplomatic_marriage_direct_child",
                _ => "aw_diplomatic_marriage_collateral_kin"
            };
            string kinshipFallback = pCandidate.Kinship switch
            {
                RoyalMarriageKinship.Ruler => "Reigning ruler",
                RoyalMarriageKinship.DirectChild => "Ruler's direct child",
                _ => "Collateral royal kin"
            };
            return AW_L10n.Text(pCandidate.Facts.Male
                    ? "aw_diplomatic_marriage_male"
                    : "aw_diplomatic_marriage_female",
                pCandidate.Facts.Male ? "Male" : "Female") + "  |  " +
                AW_L10n.Text("aw_diplomatic_marriage_age", "Age") + ": " +
                Math.Max(0, pCandidate.Actor.getAge()) + "\n" +
                AW_L10n.Text(kinshipKey, kinshipFallback);
        }

        private static string ShortCandidateDetail(
            DiplomaticMarriageCandidate pCandidate)
        {
            return AW_L10n.Text(pCandidate.Facts.Male
                    ? "aw_diplomatic_marriage_male_short"
                    : "aw_diplomatic_marriage_female_short",
                pCandidate.Facts.Male ? "M" : "F") + "  " +
                Math.Max(0, pCandidate.Actor.getAge());
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            float width = Math.Max(1f, _windowSize.x - 42f);
            float height = Math.Max(1f, _windowSize.y - 58f);
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            RectTransform titleRect = BackgroundTransform
                ?.Find("TitleBackground")?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * .52f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(width, height);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect native = nativeScroll?.GetComponent<ScrollRect>();
            if (native != null)
            {
                native.horizontal = false;
                native.vertical = false;
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
                nativeViewport.sizeDelta = new Vector2(width, height);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(width, height);
            Layout(_root, 0f, 0f, width, height);
            float gap = 14f;
            float sideWidth = (width - gap - 16f) * .5f;
            const float directionWidth = 156f;
            const float directionGap = 8f;
            float directionStart = (width - directionWidth * 2f -
                                    directionGap) * .5f;
            Layout(_requesterMaleDirectionButton.GetComponent<RectTransform>(),
                directionStart, 4f, directionWidth, 28f);
            Layout(_requesterFemaleDirectionButton
                    .GetComponent<RectTransform>(),
                directionStart + directionWidth + directionGap, 4f,
                directionWidth, 28f);
            float sideHeight = Math.Max(240f, height - 102f);
            Layout(_requesterPanel.Root, 4f, 38f, sideWidth, sideHeight);
            Layout(_responderPanel.Root, 4f + sideWidth + gap, 38f,
                sideWidth, sideHeight);
            LayoutPanel(_requesterPanel, sideWidth, sideHeight);
            LayoutPanel(_responderPanel, sideWidth, sideHeight);
            Layout(_status.rectTransform, 8f, height - 62f,
                width - 16f, 22f);
            const float commandWidth = 112f;
            const float commandGap = 10f;
            float commandStart = (width - commandWidth * 3f -
                                  commandGap * 2f) * .5f;
            Layout(_diplomacyBackButton.GetComponent<RectTransform>(),
                commandStart, height - 36f, commandWidth, 30f);
            Layout(_cancelButton.GetComponent<RectTransform>(),
                commandStart + commandWidth + commandGap, height - 36f,
                commandWidth, 30f);
            Layout(_confirmButton.GetComponent<RectTransform>(),
                commandStart + (commandWidth + commandGap) * 2f,
                height - 36f, commandWidth, 30f);
            _chrome?.RepositionResizeHandle();
        }

        private static void LayoutPanel(CandidatePanel pPanel, float pWidth,
            float pHeight)
        {
            Layout(pPanel.FlagBackground.rectTransform, 8f, 8f, 34f, 34f);
            Layout(pPanel.KingdomName.rectTransform, 48f, 8f,
                pWidth - 56f, 20f);
            Layout(pPanel.AvatarRoot.GetComponent<RectTransform>(), 8f, 48f,
                76f, 76f);
            Layout(pPanel.CandidateName.rectTransform, 92f, 44f,
                pWidth - 100f, 26f);
            Layout(pPanel.CandidateDetail.rectTransform, 92f, 72f,
                pWidth - 100f, 48f);
            Layout(pPanel.Previous.GetComponent<RectTransform>(),
                8f, 130f, 32f, 32f);
            Layout(pPanel.Counter.rectTransform, 44f, 130f,
                pWidth - 88f, 32f);
            Layout(pPanel.Next.GetComponent<RectTransform>(),
                pWidth - 40f, 130f, 32f, 32f);
            Layout(pPanel.ListRoot, 6f, 168f, pWidth - 12f,
                Math.Max(64f, pHeight - 174f));
        }

        private static CandidateRow CreateCandidateRow(Transform pParent)
        {
            var row = new CandidateRow();
            row.Root = new GameObject("MarriageCandidate",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement), typeof(TipButton));
            row.Root.transform.SetParent(pParent, false);
            row.Background = row.Root.GetComponent<Image>();
            row.Button = row.Root.GetComponent<Button>();
            row.Tip = row.Root.GetComponent<TipButton>();
            row.Tip.type = AW_RawTooltip.TYPE;
            LayoutElement layout = row.Root.GetComponent<LayoutElement>();
            layout.minHeight = 28f;
            layout.preferredHeight = 28f;
            row.Label = CreateText(row.Root.transform, "Label", 8,
                TextAnchor.MiddleLeft);
            row.Label.rectTransform.anchorMin = Vector2.zero;
            row.Label.rectTransform.anchorMax = Vector2.one;
            row.Label.rectTransform.offsetMin = new Vector2(8f, 2f);
            row.Label.rectTransform.offsetMax = new Vector2(-8f, -2f);
            return row;
        }

        private static Button CreateIconButton(Transform pParent,
            string pName, UnityEngine.Events.UnityAction pAction,
            bool previous, string pTooltip)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.sprite = SpriteTextureLoader.getSprite(
                "ui/icons/iconArrowMetaRight");
            image.preserveAspect = true;
            image.color = Color.white;
            if (previous) image.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            TipButton tip = obj.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(obj, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = pTooltip,
                    tip_description = ""
                });
            return button;
        }

        private static Button CreateTextButton(Transform pParent,
            string pName, UnityEngine.Events.UnityAction pAction,
            out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            pText = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleCenter);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(4f, 2f);
            pText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            return button;
        }

        private static Image CreateImage(Transform pParent, string pName,
            Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.color = pColor;
            image.preserveAspect = true;
            return image;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static void CreateScrollArea(Transform pParent,
            out RectTransform pRoot, out RectTransform pContent)
        {
            pRoot = new GameObject("CandidateList", typeof(RectTransform),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            pRoot.SetParent(pParent, false);
            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(Mask));
            viewport.transform.SetParent(pRoot, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-7f, 0f);
            viewport.GetComponent<Image>().color =
                new Color(.045f, .043f, .038f, .72f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            pContent = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter))
                .GetComponent<RectTransform>();
            pContent.SetParent(viewport.transform, false);
            pContent.anchorMin = new Vector2(0f, 1f);
            pContent.anchorMax = new Vector2(1f, 1f);
            pContent.pivot = new Vector2(.5f, 1f);
            pContent.anchoredPosition = Vector2.zero;
            pContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup group =
                pContent.GetComponent<VerticalLayoutGroup>();
            group.spacing = 2f;
            group.padding = new RectOffset(2, 2, 2, 2);
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            pContent.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = pRoot.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = pContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;
            DiplomacyConversationWindowScrollbar.Attach(pRoot, scroll);
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

        private static DiplomaticMarriageCandidate FindCandidate(
            IReadOnlyList<DiplomaticMarriageCandidate> pCandidates,
            long pActorId)
        {
            if (pCandidates == null) return null;
            for (int i = 0; i < pCandidates.Count; i++)
                if (pCandidates[i]?.ActorId == pActorId)
                    return pCandidates[i];
            return null;
        }

        private static bool Contains(
            IReadOnlyList<DiplomaticMarriageCandidate> pCandidates,
            long pActorId)
        {
            return IndexOf(pCandidates, pActorId) >= 0;
        }

        private static int IndexOf(
            IReadOnlyList<DiplomaticMarriageCandidate> pCandidates,
            long pActorId)
        {
            if (pCandidates == null) return -1;
            for (int i = 0; i < pCandidates.Count; i++)
                if (pCandidates[i]?.ActorId == pActorId) return i;
            return -1;
        }

        private static long Cycle(
            IReadOnlyList<DiplomaticMarriageCandidate> pCandidates,
            long pCurrentActorId, int pDirection)
        {
            if (pCandidates == null || pCandidates.Count == 0) return -1L;
            int index = IndexOf(pCandidates, pCurrentActorId);
            if (index < 0) index = 0;
            index = (index + pDirection) % pCandidates.Count;
            if (index < 0) index += pCandidates.Count;
            return pCandidates[index].ActorId;
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
