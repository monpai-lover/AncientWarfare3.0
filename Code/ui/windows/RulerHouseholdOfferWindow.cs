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
    internal sealed class RulerHouseholdOfferWindow :
        AbstractWindow<RulerHouseholdOfferWindow>
    {
        private static readonly Vector2 DefaultSize = new(560f, 400f);
        private static readonly Vector2 MinimumSize = new(500f, 340f);
        private static readonly Vector2 MaximumSize = new(820f, 620f);
        private static long _requesterKingdomId = -1L;
        private static long _recipientKingdomId = -1L;
        private static long _consortRequestProposalId = -1L;

        private readonly List<CandidateRow> _rows = new();
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private Button _principalMode;
        private Text _principalModeText;
        private Button _consortMode;
        private Text _consortModeText;
        private RectTransform _candidatePanel;
        private Text _candidateHeading;
        private GameObject _candidatePortraitRoot;
        private UiUnitAvatarElement _candidatePortrait;
        private Text _candidateName;
        private Text _candidateDetail;
        private RectTransform _candidateListRoot;
        private RectTransform _candidateListContent;
        private RectTransform _recipientPanel;
        private Text _recipientHeading;
        private GameObject _recipientPortraitRoot;
        private UiUnitAvatarElement _recipientPortrait;
        private Text _recipientName;
        private Text _recipientDetail;
        private Text _status;
        private Button _backButton;
        private Text _backText;
        private Button _confirmButton;
        private Text _confirmText;
        private WideWindowChrome _chrome;
        private RulerHouseholdOfferCandidatePool _pool;
        private RulerHouseholdKind _kind =
            RulerHouseholdKind.PrincipalWife;
        private long _selectedActorId = -1L;
        private bool _rebuildPool = true;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        private sealed class CandidateRow
        {
            public GameObject Root;
            public Image Background;
            public Button Button;
            public Text Text;
            public TipButton Tip;
        }

        public static void Open(long pRequesterKingdomId,
            long pRecipientKingdomId)
        {
            _requesterKingdomId = pRequesterKingdomId;
            _recipientKingdomId = pRecipientKingdomId;
            _consortRequestProposalId = -1L;
            if (Instance != null)
            {
                Instance._kind = RulerHouseholdKind.PrincipalWife;
                Instance._rebuildPool = true;
            }
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.HOUSEHOLD_OFFER);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.HOUSEHOLD_OFFER,
                () => Instance?.Refresh());
        }

        public static bool OpenForConsortRequest(long pProposalId)
        {
            DiplomacyProposal proposal = DiplomacyProposalService
                .ReadProposalById(pProposalId);
            if (proposal == null ||
                proposal.Status != DiplomacyProposalStatus.Pending ||
                proposal.Type != DiplomacyProposalType.HouseholdOffering ||
                !RulerHouseholdRules.IsConsortRequestDetail(
                    proposal.DetailId)) return false;
            _requesterKingdomId = proposal.ResponderKingdomId;
            _recipientKingdomId = proposal.RequesterKingdomId;
            _consortRequestProposalId = pProposalId;
            if (Instance != null)
            {
                Instance._kind = RulerHouseholdKind.Consort;
                Instance._selectedActorId = -1L;
                Instance._rebuildPool = true;
            }
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.HOUSEHOLD_OFFER);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.HOUSEHOLD_OFFER,
                () => Instance?.Refresh());
            return true;
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize =>
                {
                    _windowSize = pSize;
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
            _rebuildPool = true;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            _commandRefreshRequested = true;
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            _root = new GameObject("HouseholdOfferRoot",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(ContentTransform, false);

            _principalMode = CreateButton(_root, "PrincipalWifeMode",
                () => SelectKind(RulerHouseholdKind.PrincipalWife),
                out _principalModeText);
            _consortMode = CreateButton(_root, "ConsortMode",
                () => SelectKind(RulerHouseholdKind.Consort),
                out _consortModeText);

            _candidatePanel = CreatePanel(_root, "CandidatePanel");
            _candidateHeading = CreateText(_candidatePanel,
                "CandidateHeading", 10, TextAnchor.UpperLeft,
                FontStyle.Bold);
            _candidatePortraitRoot = new GameObject("CandidatePortrait",
                typeof(RectTransform));
            _candidatePortraitRoot.transform.SetParent(_candidatePanel,
                false);
            _candidateName = CreateText(_candidatePanel, "CandidateName",
                10, TextAnchor.UpperLeft, FontStyle.Bold);
            _candidateDetail = CreateText(_candidatePanel,
                "CandidateDetail", 8, TextAnchor.UpperLeft,
                FontStyle.Normal);
            CreateScrollArea(_candidatePanel, "CandidateList",
                out _candidateListRoot, out _candidateListContent);

            _recipientPanel = CreatePanel(_root, "FixedRecipientRuler");
            _recipientHeading = CreateText(_recipientPanel,
                "RecipientHeading", 10, TextAnchor.UpperLeft,
                FontStyle.Bold);
            _recipientPortraitRoot = new GameObject("RecipientPortrait",
                typeof(RectTransform));
            _recipientPortraitRoot.transform.SetParent(_recipientPanel,
                false);
            _recipientName = CreateText(_recipientPanel, "RecipientName",
                11, TextAnchor.UpperCenter, FontStyle.Bold);
            _recipientDetail = CreateText(_recipientPanel,
                "RecipientDetail", 8, TextAnchor.UpperCenter,
                FontStyle.Normal);

            _status = CreateText(_root, "Status", 8,
                TextAnchor.MiddleCenter, FontStyle.Normal);
            _backButton = CreateButton(_root, "BackToDiplomacy",
                BackToDiplomacy, out _backText);
            _confirmButton = CreateButton(_root, "ConfirmOffer",
                Confirm, out _confirmText);
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            SetWindowText();
            bool requestMode = _consortRequestProposalId >= 0L;
            if (requestMode)
            {
                DiplomacyProposal request = DiplomacyProposalService
                    .ReadProposalById(_consortRequestProposalId);
                if (request == null ||
                    request.Status != DiplomacyProposalStatus.Pending ||
                    !RulerHouseholdRules.IsConsortRequestDetail(
                        request.DetailId) ||
                    request.ResponderKingdomId != _requesterKingdomId ||
                    request.RequesterKingdomId != _recipientKingdomId)
                {
                    BindUnavailable("already_responded");
                    return;
                }
            }
            _principalMode.gameObject.SetActive(!requestMode);
            _consortMode.gameObject.SetActive(true);
            if (requestMode) _kind = RulerHouseholdKind.Consort;
            Kingdom requester = FindKingdom(_requesterKingdomId);
            Kingdom recipient = FindKingdom(_recipientKingdomId);
            if (requester?.data == null || recipient?.data == null ||
                requester.isRekt() || recipient.isRekt())
            {
                BindUnavailable("invalid_household_realms");
                return;
            }
            if (_rebuildPool || _pool == null)
            {
                _pool = RulerHouseholdService.BuildOfferCandidatePool(
                    requester, recipient, _kind);
                _rebuildPool = false;
                if (!Contains(_pool.Candidates, _selectedActorId))
                    _selectedActorId = _pool.Candidates.Count > 0
                        ? _pool.Candidates[0].ActorId
                        : -1L;
            }

            BindModeButton(_principalMode, _principalModeText,
                _kind == RulerHouseholdKind.PrincipalWife);
            BindModeButton(_consortMode, _consortModeText,
                _kind == RulerHouseholdKind.Consort);
            BindCandidatePool(requester);
            BindFixedRecipientRuler(recipient);
            _confirmButton.interactable = _selectedActorId >= 0L &&
                                          !_commandPending;
            _status.text = _pool.Candidates.Count > 0
                ? AW_L10n.Text(requestMode
                        ? "aw_household_request_select_ready"
                        : "aw_household_offer_ready",
                    requestMode
                        ? "Select a noblewoman to answer the request"
                        : "Select a noblewoman and send the proposal")
                : DiplomacyConversationWindow.ProposalFailure(_pool.Reason);
            Canvas.ForceUpdateCanvases();
        }

        private void SelectKind(RulerHouseholdKind pKind)
        {
            if (_consortRequestProposalId >= 0L || _commandPending ||
                _kind == pKind) return;
            _kind = pKind;
            _selectedActorId = -1L;
            _rebuildPool = true;
            Refresh();
        }

        private static void BindModeButton(Button pButton, Text pText,
            bool pSelected)
        {
            pButton.GetComponent<Image>().color = pSelected
                ? new Color(.64f, .43f, .16f, 1f)
                : new Color(.30f, .28f, .24f, 1f);
            pText.color = pSelected
                ? new Color(1f, .88f, .48f, 1f)
                : Color.white;
        }

        private void BindCandidatePool(Kingdom pRequester)
        {
            _candidateHeading.text = AW_L10n.Text(
                "aw_household_offer_our_noblewomen", "Our noblewomen") +
                " - " + RulerAppellationService.GetProjectedStateName(
                    pRequester);
            for (int i = 0; i < _pool.Candidates.Count; i++)
            {
                while (_rows.Count <= i)
                    _rows.Add(CreateCandidateRow(_candidateListContent));
                CandidateRow row = _rows[i];
                RulerHouseholdOfferCandidate candidate =
                    _pool.Candidates[i];
                bool selected = candidate.ActorId == _selectedActorId;
                row.Background.color = selected
                    ? new Color(.38f, .29f, .15f, .98f)
                    : new Color(.14f, .13f, .11f, .96f);
                row.Text.text = candidate.ActorName + "  |  " +
                                candidate.LineageLabel + "  |  " +
                                AW_L10n.Text("aw_household_age", "Age") +
                                " " + candidate.Age;
                long actorId = candidate.ActorId;
                row.Button.onClick.RemoveAllListeners();
                row.Button.onClick.AddListener(() => SelectCandidate(actorId));
                row.Button.interactable = !_commandPending;
                row.Tip.hoverAction = () => Tooltip.show(row.Root,
                    AW_RawTooltip.TYPE, new TooltipData
                    {
                        tip_name = candidate.ActorName,
                        tip_description = candidate.LineageLabel
                    });
                row.Root.SetActive(true);
            }
            for (int i = _pool.Candidates.Count; i < _rows.Count; i++)
                _rows[i].Root.SetActive(false);

            RulerHouseholdOfferCandidate selectedCandidate = FindCandidate(
                _pool.Candidates, _selectedActorId);
            if (selectedCandidate == null)
            {
                _candidatePortraitRoot.SetActive(false);
                _candidateName.text = AW_L10n.Text(
                    "aw_household_offer_no_candidate", "No candidate");
                _candidateDetail.text = "";
                return;
            }
            _candidateName.text = selectedCandidate.ActorName;
            _candidateDetail.text = selectedCandidate.LineageLabel +
                "\n" + AW_L10n.Text("aw_household_age", "Age") + " " +
                selectedCandidate.Age;
            BindPortrait(_candidatePortraitRoot, ref _candidatePortrait,
                selectedCandidate.Actor);
        }

        private void BindFixedRecipientRuler(Kingdom pRecipient)
        {
            _recipientHeading.text = AW_L10n.Text(
                "aw_household_offer_recipient_ruler", "Recipient ruler");
            Actor ruler = pRecipient?.king;
            if (ruler?.data == null)
            {
                _recipientPortraitRoot.SetActive(false);
                _recipientName.text = "";
                _recipientDetail.text = DiplomacyConversationWindow
                    .ProposalFailure("invalid_household_ruler");
                return;
            }
            _recipientName.text = ruler.getName() ?? "";
            _recipientDetail.text =
                RulerAppellationService.GetFullLivingAppellation(
                    pRecipient) + "\n" +
                RulerAppellationService.GetProjectedStateName(pRecipient) +
                "\n" + AW_L10n.Text("aw_household_consort_capacity",
                    "Consorts") + " " + _pool.ActiveConsorts + " / " +
                _pool.ConsortCapacity;
            BindPortrait(_recipientPortraitRoot, ref _recipientPortrait,
                ruler);
        }

        private void SelectCandidate(long pActorId)
        {
            if (_commandPending ||
                !Contains(_pool?.Candidates, pActorId)) return;
            _selectedActorId = pActorId;
            Refresh();
        }

        private void Confirm()
        {
            if (_commandPending || _selectedActorId < 0L) return;
            Kingdom requester = FindKingdom(_requesterKingdomId);
            Kingdom recipient = FindKingdom(_recipientKingdomId);
            if (requester?.data == null || recipient?.king?.data == null)
                return;
            AW3CommandRequest command = _consortRequestProposalId >= 0L
                ? AW3CommandRequest.RespondDiplomacyProposal(
                    requester.id, recipient.id,
                    _consortRequestProposalId, accept: true,
                    actorId: _selectedActorId)
                : AW3CommandRequest.CreateDiplomacyProposal(
                    requester.id, recipient.id,
                    DiplomacyProposalType.HouseholdOffering.ToString(),
                    _selectedActorId, recipient.king.data.id,
                    detailId: RulerHouseholdRules.DetailId(_kind));
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(command);
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted)
            {
                WorldTip.showNow(DiplomacyConversationWindow
                    .ProposalFailure(result.MessageKey), false, "top");
                _rebuildPool = true;
                Refresh();
                return;
            }
            _consortRequestProposalId = -1L;
            DiplomacyConversationWindow.Open(requester.id);
        }

        private void BackToDiplomacy()
        {
            _consortRequestProposalId = -1L;
            DiplomacyConversationWindow.Open(_requesterKingdomId);
        }

        private void BindUnavailable(string pReason)
        {
            _pool = new RulerHouseholdOfferCandidatePool
            {
                Reason = pReason
            };
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].Root.SetActive(false);
            _candidatePortraitRoot.SetActive(false);
            _recipientPortraitRoot.SetActive(false);
            _candidateName.text = "";
            _candidateDetail.text = "";
            _recipientName.text = "";
            _recipientDetail.text = "";
            _confirmButton.interactable = false;
            _status.text = DiplomacyConversationWindow.ProposalFailure(
                pReason);
        }

        private void SetWindowText()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.text = AW_L10n.Text(
                    _consortRequestProposalId >= 0L
                        ? "aw_ruler_household_request_select"
                        : "aw_ruler_household_offer",
                    _consortRequestProposalId >= 0L
                        ? "Select Requested Consort"
                        : "Offer Consort");
            _principalModeText.text = AW_L10n.Text(
                "aw_household_kind_principal_wife", "Principal Wife");
            _consortModeText.text = AW_L10n.Text(
                "aw_household_kind_consort", "Consort");
            _backText.text = AW_L10n.Text(
                "aw_household_offer_back_diplomacy",
                "Back to Diplomacy");
            _confirmText.text = AW_L10n.Text(
                _consortRequestProposalId >= 0L
                    ? "aw_household_request_confirm"
                    : "aw_household_offer_confirm",
                _consortRequestProposalId >= 0L
                    ? "Provide Consort"
                    : "Send Proposal");
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background =
                BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
            float width = Math.Max(1f, _windowSize.x - 42f);
            float height = Math.Max(1f, _windowSize.y - 58f);
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            RectTransform title = BackgroundTransform?.Find(
                "TitleBackground")?.GetComponent<RectTransform>();
            if (title != null)
            {
                title.sizeDelta = new Vector2(_windowSize.x * .52f, 30f);
                title.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeRect != null)
            {
                nativeRect.sizeDelta = new Vector2(width, height);
                nativeRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect native = nativeScroll?.GetComponent<ScrollRect>();
            if (native != null)
            {
                native.horizontal = false;
                native.vertical = false;
            }
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null)
                viewport.sizeDelta = new Vector2(width, height);
            RectTransform content = ContentTransform as RectTransform;
            if (content != null)
                content.sizeDelta = new Vector2(width, height);
            Layout(_root, 0f, 0f, width, height);

            const float modeGap = 8f;
            float modeWidth = Math.Min(164f, (width - 24f) * .5f);
            float modeStart = (width - modeWidth * 2f - modeGap) * .5f;
            bool requestMode = _consortRequestProposalId >= 0L;
            Layout(_principalMode.GetComponent<RectTransform>(), modeStart,
                4f, modeWidth, 28f);
            Layout(_consortMode.GetComponent<RectTransform>(),
                requestMode ? (width - modeWidth) * .5f :
                modeStart + modeWidth + modeGap, 4f, modeWidth, 28f);

            float panelTop = 38f;
            float footerHeight = 64f;
            float panelHeight = Math.Max(1f,
                height - panelTop - footerHeight);
            float gap = 12f;
            float panelWidth = Math.Max(1f, width - gap - 8f);
            float candidateWidth = panelWidth * .60f;
            float recipientWidth = panelWidth - candidateWidth;
            Layout(_candidatePanel, 4f, panelTop, candidateWidth,
                panelHeight);
            Layout(_recipientPanel, 4f + candidateWidth + gap, panelTop,
                recipientWidth, panelHeight);
            LayoutCandidatePanel(candidateWidth, panelHeight);
            LayoutRecipientPanel(recipientWidth, panelHeight);
            Layout(_status.rectTransform, 8f, height - 60f,
                width - 16f, 22f);
            float commandWidth = Math.Min(132f, (width - 24f) * .5f);
            float commandStart = (width - commandWidth * 2f - 12f) * .5f;
            Layout(_backButton.GetComponent<RectTransform>(), commandStart,
                height - 34f, commandWidth, 28f);
            Layout(_confirmButton.GetComponent<RectTransform>(),
                commandStart + commandWidth + 12f, height - 34f,
                commandWidth, 28f);
            _chrome?.RepositionResizeHandle();
        }

        private void LayoutCandidatePanel(float pWidth, float pHeight)
        {
            float portraitSize = Math.Min(66f,
                Math.Max(44f, pHeight - 120f));
            float listTop = 42f + portraitSize;
            Layout(_candidateHeading.rectTransform, 8f, 7f,
                pWidth - 16f, 22f);
            Layout(_candidatePortraitRoot.GetComponent<RectTransform>(),
                8f, 34f, portraitSize, portraitSize);
            Layout(_candidateName.rectTransform, 16f + portraitSize, 34f,
                pWidth - portraitSize - 24f, 25f);
            Layout(_candidateDetail.rectTransform,
                16f + portraitSize, 61f,
                pWidth - portraitSize - 24f,
                Math.Max(25f, portraitSize - 27f));
            Layout(_candidateListRoot, 6f, listTop, pWidth - 12f,
                Math.Max(1f, pHeight - listTop - 6f));
        }

        private void LayoutRecipientPanel(float pWidth, float pHeight)
        {
            Layout(_recipientHeading.rectTransform, 8f, 7f,
                pWidth - 16f, 22f);
            float portraitSize = Math.Min(94f,
                Math.Min(pWidth - 28f,
                    Math.Max(44f, pHeight - 120f)));
            Layout(_recipientPortraitRoot.GetComponent<RectTransform>(),
                (pWidth - portraitSize) * .5f, 38f, portraitSize,
                portraitSize);
            Layout(_recipientName.rectTransform, 8f,
                44f + portraitSize, pWidth - 16f, 28f);
            Layout(_recipientDetail.rectTransform, 8f,
                74f + portraitSize, pWidth - 16f,
                Math.Max(1f, pHeight - portraitSize - 82f));
        }

        private static RectTransform CreatePanel(Transform pParent,
            string pName)
        {
            RectTransform panel = new GameObject(pName,
                typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            panel.SetParent(pParent, false);
            panel.GetComponent<Image>().color =
                new Color(.10f, .092f, .072f, .96f);
            return panel;
        }

        private static CandidateRow CreateCandidateRow(Transform pParent)
        {
            var row = new CandidateRow();
            row.Root = new GameObject("HouseholdCandidate",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement), typeof(TipButton));
            row.Root.transform.SetParent(pParent, false);
            row.Background = row.Root.GetComponent<Image>();
            row.Button = row.Root.GetComponent<Button>();
            row.Tip = row.Root.GetComponent<TipButton>();
            row.Tip.type = AW_RawTooltip.TYPE;
            LayoutElement layout = row.Root.GetComponent<LayoutElement>();
            layout.minHeight = 30f;
            layout.preferredHeight = 30f;
            row.Text = CreateText(row.Root.transform, "Text", 8,
                TextAnchor.MiddleLeft, FontStyle.Normal);
            row.Text.rectTransform.anchorMin = Vector2.zero;
            row.Text.rectTransform.anchorMax = Vector2.one;
            row.Text.rectTransform.offsetMin = new Vector2(7f, 2f);
            row.Text.rectTransform.offsetMax = new Vector2(-7f, -2f);
            return row;
        }

        private static void BindPortrait(GameObject pRoot,
            ref UiUnitAvatarElement pPortrait, Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() ||
                pActor.isRekt())
            {
                pRoot.SetActive(false);
                return;
            }
            pRoot.SetActive(true);
            if (pPortrait == null)
            {
                UiUnitAvatarElement prefab =
                    FamilyTreeNodeView.GetAvatarPrefab();
                if (prefab == null)
                {
                    pRoot.SetActive(false);
                    return;
                }
                pPortrait = UnityEngine.Object.Instantiate(prefab,
                    pRoot.transform);
                RectTransform rect =
                    pPortrait.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }
            pPortrait.show(pActor);
        }

        private static void CreateScrollArea(Transform pParent,
            string pName, out RectTransform pRoot,
            out RectTransform pContent)
        {
            pRoot = new GameObject(pName, typeof(RectTransform),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            pRoot.SetParent(pParent, false);
            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(Mask));
            viewport.transform.SetParent(pRoot, false);
            RectTransform viewportRect =
                viewport.GetComponent<RectTransform>();
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

        private static Button CreateButton(Transform pParent, string pName,
            UnityEngine.Events.UnityAction pAction, out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            pText = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleCenter, FontStyle.Normal);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(4f, 2f);
            pText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            return button;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor, FontStyle pStyle)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.fontStyle = pStyle;
            text.alignment = pAnchor;
            text.color = new Color(.96f, .94f, .86f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
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

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static RulerHouseholdOfferCandidate FindCandidate(
            IReadOnlyList<RulerHouseholdOfferCandidate> pCandidates,
            long pActorId)
        {
            if (pCandidates == null) return null;
            for (int i = 0; i < pCandidates.Count; i++)
                if (pCandidates[i]?.ActorId == pActorId)
                    return pCandidates[i];
            return null;
        }

        private static bool Contains(
            IReadOnlyList<RulerHouseholdOfferCandidate> pCandidates,
            long pActorId)
        {
            return FindCandidate(pCandidates, pActorId) != null;
        }
    }
}
