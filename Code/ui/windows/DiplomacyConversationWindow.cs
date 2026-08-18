using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class DiplomacyConversationWindow :
        AbstractWindow<DiplomacyConversationWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(580f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);
        private const float WarScoreRefreshInterval = .75f;
        private static long _baseKingdomId = -1L;
        private readonly List<DiplomacyKingdomListItem> _kingdomPool = new();
        private readonly List<DiplomacyBubbleItem> _bubblePool = new();
        private readonly List<AW3DiplomacyChatBubbleItem>
            _multiplayerBubblePool = new();
        private readonly List<ActionRow> _actionRows = new();
        private readonly List<long> _coalitionTargetIds = new();
        private readonly List<long> _forgeCityIds = new();
        private Vector2 _windowSize = DefaultSize;
        private long _selectedKingdomId = -1L;
        private RectTransform _root;
        private RectTransform _leftViewport;
        private RectTransform _leftContent;
        private ScrollRect _leftScroll;
        private Scrollbar _leftScrollbar;
        private RectTransform _chatViewport;
        private RectTransform _chatContent;
        private ScrollRect _chatScroll;
        private Image _divider;
        private Text _header;
        private Button _kingdomBack;
        private Text _emptyCountries;
        private Text _emptyEvents;
        private WideWindowChrome _chrome;
        private RectTransform _composer;
        private RectTransform _actionMenu;
        private RectTransform _actionContent;
        private ScrollRect _actionScroll;
        private Scrollbar _actionScrollbar;
        private Text _composerSummary;
        private InputField _chatInput;
        private Text _chatPlaceholder;
        private Text _toggleText;
        private Text _sendText;
        private Button _toggleButton;
        private Button _sendButton;
        private bool _actionsExpanded;
        private DiplomacyProposalType _selectedProposalType;
        private DiplomaticOperationType _selectedOperationType;
        private long _selectedCoalitionTargetId = -1L;
        private long _selectedForgeCityId = -1L;
        private bool _strongForgery;
        private long _chatPairBaseCountryId = -1L;
        private long _chatPairTargetCountryId = -1L;
        private RectTransform _selectionPanel;
        private Text _selectionTitle;
        private Text _selectionDetail;
        private Button _selectionPrevious;
        private Button _selectionNext;
        private Button _selectionMode;
        private Text _selectionModeText;
        private Image _selectionFlagBackground;
        private Image _selectionFlagIcon;
        private LivePortraitSlot _selectionPortraitLeft;
        private LivePortraitSlot _selectionPortraitRight;
        private DiplomaticCoalitionPreview _coalitionPreview;
        private DiplomaticMarriagePreview _marriagePreview;
        private DiplomaticOperationPreview _operationPreview;
        private bool _commandPending;
        private bool _commandRefreshRequested;
        private float _nextWarScoreRefreshTime;
        private long _lastWarNegotiationRequesterId = -1L;
        private long _lastWarNegotiationResponderId = -1L;
        private bool _lastWarNegotiationAvailable;
        private bool _lastWarNegotiationPending;
        private bool _hasWarNegotiationIndicator;
        private int _lastWarNegotiationScore;
        private string _lastWarNegotiationReason = string.Empty;

        private sealed class ActionRow
        {
            public DiplomacyProposalType Type;
            public DiplomaticOperationType OperationType;
            public bool DeclareWar;
            public bool WarNegotiation;
            public Button Button;
            public Text Text;
            public Text StateText;
            public TipButton Tip;
        }

        private sealed class LivePortraitSlot
        {
            public GameObject Root;
            public UiUnitAvatarElement Avatar;
            public long ActorId = -1L;
        }

        public static void Open(long pKingdomId)
        {
            Kingdom kingdom = FindKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt()) return;
            _baseKingdomId = pKingdomId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.DIPLOMACY_CONVERSATIONS);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.DIPLOMACY_CONVERSATIONS,
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
            DiplomacyProposalService.PairChanged += OnProposalPairChanged;
            AW3MultiplayerDiplomacyChatFacade.Changed += OnMultiplayerChatChanged;
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private void OnDestroy()
        {
            DiplomacyProposalService.PairChanged -= OnProposalPairChanged;
            AW3MultiplayerDiplomacyChatFacade.Changed -= OnMultiplayerChatChanged;
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
                bool wasPending = _commandPending;
                _commandPending = false;
                if (wasPending) ClearSelectedAction();
                if (isActiveAndEnabled) Refresh();
            }
            if (!isActiveAndEnabled || !_actionsExpanded ||
                Time.unscaledTime < _nextWarScoreRefreshTime) return;
            _nextWarScoreRefreshTime = Time.unscaledTime +
                                       WarScoreRefreshInterval;
            RefreshWarNegotiationScore();
        }

        private void RefreshWarNegotiationScore()
        {
            ActionRow row = _actionRows.Find(pRow =>
                pRow.WarNegotiation);
            Kingdom requester = FindKingdom(_baseKingdomId);
            Kingdom responder = FindKingdom(_selectedKingdomId);
            if (row?.Button == null || requester?.data == null ||
                responder?.data == null) return;
            bool available = WarPeaceNegotiationController
                .TryGetMenuWarScore(requester, responder, out int score,
                    out string reason);
            if (WarNegotiationIndicatorUnchanged(requester.id, responder.id,
                    available, score, reason)) return;
            string title = AW_L10n.Text(
                "aw_diplomacy_action_war_negotiation",
                "War negotiation");
            row.Text.text = title;
            row.Button.interactable = available && !_commandPending;
            AW_UIStyle.ApplyButton(row.Button.GetComponent<Image>(),
                available ? .96f : .48f);
            ApplyWarNegotiationScore(row, available, score);
            string detail = available
                ? AW_L10n.Text(
                    "aw_diplomacy_action_war_negotiation_desc",
                    "Review bilateral demands and concessions") +
                  "\n" + AW_L10n.Text("aw_diplomacy_war_score",
                      "War score") + ": " +
                  DiplomacyWarScoreIndicatorRules.Format(score)
                : ProposalFailure(reason);
            row.Tip.enabled = true;
            row.Tip.hoverAction = () => Tooltip.show(
                row.Tip.gameObject, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = title,
                    tip_description = detail
                });
            RememberWarNegotiationIndicator(requester.id, responder.id,
                available, score, reason);
        }

        private bool WarNegotiationIndicatorUnchanged(long pRequesterId,
            long pResponderId, bool pAvailable, int pScore, string pReason)
        {
            return _hasWarNegotiationIndicator &&
                   _lastWarNegotiationRequesterId == pRequesterId &&
                   _lastWarNegotiationResponderId == pResponderId &&
                   _lastWarNegotiationAvailable == pAvailable &&
                   _lastWarNegotiationScore == pScore &&
                   _lastWarNegotiationPending == _commandPending &&
                   string.Equals(_lastWarNegotiationReason,
                       pReason ?? string.Empty, StringComparison.Ordinal);
        }

        private void RememberWarNegotiationIndicator(long pRequesterId,
            long pResponderId, bool pAvailable, int pScore, string pReason)
        {
            _hasWarNegotiationIndicator = true;
            _lastWarNegotiationRequesterId = pRequesterId;
            _lastWarNegotiationResponderId = pResponderId;
            _lastWarNegotiationAvailable = pAvailable;
            _lastWarNegotiationScore = pScore;
            _lastWarNegotiationPending = _commandPending;
            _lastWarNegotiationReason = pReason ?? string.Empty;
        }

        private void OnCommandStateChanged()
        {
            _commandRefreshRequested = true;
        }

        public void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            Kingdom baseKingdom = FindKingdom(_baseKingdomId);
            if (baseKingdom?.data == null || baseKingdom.isRekt()) return;

            List<Kingdom> others = BuildOtherKingdoms(baseKingdom,
                out Dictionary<long, long> capitalDistances);
            Kingdom selected = null;
            for (int i = 0; i < others.Count; i++)
                if (others[i].id == _selectedKingdomId)
                {
                    selected = others[i];
                    break;
                }
            if (selected == null && others.Count > 0)
            {
                selected = others[0];
                _selectedKingdomId = selected.id;
            }

            for (int i = 0; i < others.Count; i++)
            {
                while (_kingdomPool.Count <= i)
                    _kingdomPool.Add(
                        DiplomacyKingdomListItem.Create(_leftContent));
                Kingdom other = others[i];
                capitalDistances.TryGetValue(other.id,
                    out long capitalDistanceSquared);
                string relationDetail = BuildRelationDetail(baseKingdom,
                    other, capitalDistanceSquared, out int opinion);
                _kingdomPool[i].Bind(other,
                    RulerAppellationService.GetProjectedStateName(other),
                    relationDetail, opinion, KingdomColor(other),
                    other.id == _selectedKingdomId,
                    SelectKingdom);
            }
            for (int i = others.Count; i < _kingdomPool.Count; i++)
                _kingdomPool[i].Unbind();
            _emptyCountries.gameObject.SetActive(others.Count == 0);

            if (selected == null)
            {
                _header.text = "";
                _emptyEvents.gameObject.SetActive(false);
                EnsureChatPair(baseKingdom.id, -1L);
                HideBubbles();
                if (_chatInput != null) _chatInput.gameObject.SetActive(false);
                if (_sendButton != null) _sendButton.interactable = false;
                return;
            }

            capitalDistances.TryGetValue(selected.id,
                out long selectedCapitalDistanceSquared);
            _header.text = RulerAppellationService.GetProjectedStateName(
                               baseKingdom) + "  -  " +
                           RulerAppellationService.GetProjectedStateName(selected) +
                           "\n<size=8><color=#BBB5A7>" +
                           BuildRelationDetail(baseKingdom, selected,
                               selectedCapitalDistanceSquared, out _) +
                           "</color></size>";
            IReadOnlyList<DiplomacyConversationEvent> events =
                DiplomacyConversationService.ReadEvents(baseKingdom.id,
                    selected.id);
            EnsureChatPair(baseKingdom.id, selected.id);
            IAW3DiplomacyChatProvider provider =
                AW3MultiplayerDiplomacyChatFacade.Current;
            IReadOnlyList<AW3DiplomacyChatEntry> multiplayerEntries =
                ReadMultiplayerChat(provider, baseKingdom, selected);
            float rowWidth = Mathf.Max(180f, _chatViewport.sizeDelta.x - 12f);
            for (int i = 0; i < events.Count; i++)
            {
                while (_bubblePool.Count <= i)
                    _bubblePool.Add(DiplomacyBubbleItem.Create(_chatContent));
                _bubblePool[i].Bind(events[i], baseKingdom.id, selected.id,
                    rowWidth, RespondToProposal);
                _bubblePool[i].transform.SetSiblingIndex(i);
            }
            for (int i = events.Count; i < _bubblePool.Count; i++)
                _bubblePool[i].Unbind();
            for (int i = 0; i < multiplayerEntries.Count; i++)
            {
                while (_multiplayerBubblePool.Count <= i)
                    _multiplayerBubblePool.Add(
                        AW3DiplomacyChatBubbleItem.Create(_chatContent));
                _multiplayerBubblePool[i].Bind(multiplayerEntries[i],
                    baseKingdom.id, rowWidth);
                _multiplayerBubblePool[i].transform.SetSiblingIndex(
                    events.Count + i);
            }
            for (int i = multiplayerEntries.Count;
                 i < _multiplayerBubblePool.Count; i++)
                _multiplayerBubblePool[i].Unbind();
            _emptyEvents.gameObject.SetActive(events.Count == 0 &&
                                              multiplayerEntries.Count == 0);
            RefreshComposer(baseKingdom, selected);
            Canvas.ForceUpdateCanvases();
            if (_chatScroll != null) _chatScroll.verticalNormalizedPosition = 0f;
        }

        private void SelectKingdom(long pKingdomId)
        {
            if (_commandPending) return;
            if (_selectedKingdomId == pKingdomId) return;
            _selectedKingdomId = pKingdomId;
            _selectedProposalType = DiplomacyProposalType.None;
            _selectedOperationType = DiplomaticOperationType.None;
            _selectedCoalitionTargetId = -1L;
            _selectedForgeCityId = -1L;
            _actionsExpanded = false;
            if (_chatInput != null) _chatInput.text = string.Empty;
            Refresh();
        }

        private void ToggleActions()
        {
            if (_commandPending) return;
            _actionsExpanded = !_actionsExpanded;
            ApplyLayout();
            Refresh();
        }

        private void SelectProposal(DiplomacyProposalType pType)
        {
            if (_commandPending) return;
            if (pType == DiplomacyProposalType.RoyalMarriage)
            {
                Kingdom requester = FindKingdom(_baseKingdomId);
                Kingdom responder = FindKingdom(_selectedKingdomId);
                if (requester?.data != null && responder?.data != null)
                    DiplomaticMarriageWindow.Open(requester.id, responder.id);
                return;
            }
            if (pType == DiplomacyProposalType.HouseholdOffering)
            {
                Kingdom requester = FindKingdom(_baseKingdomId);
                Kingdom responder = FindKingdom(_selectedKingdomId);
                if (requester?.data != null && responder?.data != null)
                    RulerHouseholdOfferWindow.Open(requester.id,
                        responder.id);
                return;
            }
            if (DiplomacyProposalRules.IsPeaceProposal(pType))
            {
                OpenWarNegotiation();
                return;
            }
            _selectedProposalType = pType;
            _selectedOperationType = DiplomaticOperationType.None;
            _actionsExpanded = pType == DiplomacyProposalType.Coalition ||
                               pType == DiplomacyProposalType.RoyalMarriage;
            ApplyLayout();
            Refresh();
        }

        private void SelectOperation(DiplomaticOperationType pType)
        {
            if (_commandPending) return;
            _selectedProposalType = DiplomacyProposalType.None;
            _selectedOperationType = pType;
            _actionsExpanded = true;
            ApplyLayout();
            Refresh();
        }

        private void OpenWarActions()
        {
            if (_commandPending) return;
            Kingdom requester = FindKingdom(_baseKingdomId);
            Kingdom responder = FindKingdom(_selectedKingdomId);
            if (requester?.data == null || responder?.data == null) return;
            _actionsExpanded = false;
            DiplomaticWarDeclarationWindow.Open(requester.id, responder.id);
        }

        private void OpenWarNegotiation()
        {
            if (_commandPending) return;
            Kingdom requester = FindKingdom(_baseKingdomId);
            Kingdom responder = FindKingdom(_selectedKingdomId);
            if (requester?.data == null || responder?.data == null) return;
            RefreshWarNegotiationScore();
            if (!WarPeaceNegotiationController.Open(requester, responder))
            {
                WarPeaceNegotiationController.TryGetMenuWarScore(requester,
                    responder, out _, out string reason);
                WorldTip.showNow(ProposalFailure(reason), false, "top");
            }
        }

        private void BackToKingdom()
        {
            AW_LineageWindowIds.ShowKingdom(_baseKingdomId);
        }

        private void SendComposer()
        {
            if (HasSelectedAction())
            {
                SendSelectedProposal();
                return;
            }
            SendMultiplayerChat(AW3MultiplayerDiplomacyChatFacade.Current);
        }

        private void SendMultiplayerChat(
            IAW3DiplomacyChatProvider pProvider)
        {
            if (pProvider == null || _chatInput == null) return;
            Kingdom requester = FindKingdom(_baseKingdomId);
            Kingdom responder = FindKingdom(_selectedKingdomId);
            if (requester?.data == null || responder?.data == null) return;
            AW3DiplomacyChatSendResult result;
            try
            {
                result = pProvider.Send(requester.id, responder.id,
                    _chatInput.text);
            }
            catch (Exception error)
            {
                WorldTip.showNow(error.Message, false, "top");
                return;
            }
            if (result?.Accepted != true)
            {
                string detail = result?.Detail;
                WorldTip.showNow(string.IsNullOrWhiteSpace(detail)
                        ? AW_L10n.Text("aw_diplomacy_chat_send_failed",
                            "Message could not be sent")
                        : detail,
                    false, "top");
                return;
            }
            _chatInput.text = string.Empty;
            Refresh();
        }

        private void SendSelectedProposal()
        {
            if (_commandPending) return;
            Kingdom requester = FindKingdom(_baseKingdomId);
            Kingdom responder = FindKingdom(_selectedKingdomId);
            if (requester?.data == null || responder?.data == null) return;
            AW3CommandRequest request;
            if (_selectedOperationType == DiplomaticOperationType.SpyNetwork)
            {
                if (IsAnnexationTarget(requester, responder))
                    request = AW3CommandRequest.StartTargetedDecision(
                        requester.id, responder.id,
                        "aw_decision_absorb_vassal");
                else
                    request = AW3CommandRequest.StartSpyNetwork(requester.id,
                        responder.id);
            }
            else if (_selectedOperationType ==
                     DiplomaticOperationType.ForgeDocuments)
                request = AW3CommandRequest.StartForgeDocuments(requester.id,
                    responder.id, _selectedForgeCityId,
                    _strongForgery
                        ? WarTerritoryService.PROJECT_STRONG_CLAIM
                        : WarTerritoryService.PROJECT_WEAK_CLAIM);
            else if (_selectedProposalType == DiplomacyProposalType.Coalition)
                request = AW3CommandRequest.CreateDiplomacyProposal(
                    requester.id, responder.id,
                    _selectedProposalType.ToString(),
                    selectionTargetCountryId: _selectedCoalitionTargetId);
            else if (_selectedProposalType != DiplomacyProposalType.None)
                request = AW3CommandRequest.CreateDiplomacyProposal(
                    requester.id, responder.id,
                    _selectedProposalType.ToString());
            else return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(request);
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted)
            {
                WorldTip.showNow(ProposalFailure(result.MessageKey), false,
                    "top");
                return;
            }
            ClearSelectedAction();
            Refresh();
        }

        private void RespondToProposal(long pProposalId, bool pAccept)
        {
            if (_commandPending) return;
            DiplomacyProposal proposal = pAccept
                ? DiplomacyProposalService.ReadProposalById(pProposalId)
                : null;
            if (proposal != null &&
                RulerHouseholdRules.IsConsortRequestDetail(
                    proposal.DetailId))
            {
                if (!RulerHouseholdOfferWindow.OpenForConsortRequest(
                        pProposalId))
                    WorldTip.showNow(ProposalFailure(
                        "household_candidate_selection_required"), false,
                        "top");
                return;
            }
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.RespondDiplomacyProposal(
                        _baseKingdomId, _selectedKingdomId, pProposalId,
                        pAccept));
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted)
            {
                WorldTip.showNow(ProposalFailure(result.MessageKey), false,
                    "top");
                return;
            }
            Refresh();
        }

        private void ClearSelectedAction()
        {
            _selectedProposalType = DiplomacyProposalType.None;
            _selectedOperationType = DiplomaticOperationType.None;
            _actionsExpanded = false;
        }

        private void OnProposalPairChanged(long pKingdomA, long pKingdomB)
        {
            bool samePair = (pKingdomA == _baseKingdomId &&
                             pKingdomB == _selectedKingdomId) ||
                            (pKingdomB == _baseKingdomId &&
                             pKingdomA == _selectedKingdomId);
            if (samePair && ScrollWindow.isCurrentWindow(
                    AW_LineageWindowIds.DIPLOMACY_CONVERSATIONS))
                Refresh();
        }

        private void OnMultiplayerChatChanged()
        {
            if (ScrollWindow.isCurrentWindow(
                    AW_LineageWindowIds.DIPLOMACY_CONVERSATIONS))
                Refresh();
        }

        private static IReadOnlyList<AW3DiplomacyChatEntry>
            ReadMultiplayerChat(IAW3DiplomacyChatProvider provider,
                Kingdom baseKingdom, Kingdom selected)
        {
            if (provider == null) return Array.Empty<AW3DiplomacyChatEntry>();
            try
            {
                IReadOnlyList<AW3DiplomacyChatEntry> source =
                    provider.Read(baseKingdom.id, selected.id);
                if (source == null || source.Count == 0)
                    return Array.Empty<AW3DiplomacyChatEntry>();
                var entries = new List<AW3DiplomacyChatEntry>(source.Count);
                var sequences = new HashSet<long>();
                for (var index = 0; index < source.Count; index++)
                {
                    AW3DiplomacyChatEntry entry = source[index];
                    if (entry == null ||
                        entry.SenderCountryId != baseKingdom.id &&
                        entry.SenderCountryId != selected.id ||
                        !sequences.Add(entry.HostSequence)) continue;
                    entries.Add(entry);
                }
                entries.Sort((left, right) =>
                    left.HostSequence.CompareTo(right.HostSequence));
                return entries.AsReadOnly();
            }
            catch
            {
                return Array.Empty<AW3DiplomacyChatEntry>();
            }
        }

        private void EnsureChatPair(long pBaseCountryId,
            long pTargetCountryId)
        {
            if (_chatPairBaseCountryId == pBaseCountryId &&
                _chatPairTargetCountryId == pTargetCountryId) return;
            _chatPairBaseCountryId = pBaseCountryId;
            _chatPairTargetCountryId = pTargetCountryId;
            if (_chatInput != null) _chatInput.text = string.Empty;
        }

        private static AW3DiplomacyChatAvailability GetChatAvailability(
            IAW3DiplomacyChatProvider pProvider, long pBaseCountryId,
            long pTargetCountryId)
        {
            try
            {
                return pProvider.GetAvailability(pBaseCountryId,
                           pTargetCountryId) ??
                       new AW3DiplomacyChatAvailability(
                           AW3DiplomacyChatAvailabilityStatus.SessionUnavailable,
                           string.Empty);
            }
            catch (Exception error)
            {
                return new AW3DiplomacyChatAvailability(
                    AW3DiplomacyChatAvailabilityStatus.SessionUnavailable,
                    error.Message);
            }
        }

        private bool HasSelectedAction()
        {
            return _selectedProposalType != DiplomacyProposalType.None ||
                   _selectedOperationType != DiplomaticOperationType.None;
        }

        private void OnChatInputChanged(string pValue)
        {
            if (HasSelectedAction() || _sendButton == null) return;
            IAW3DiplomacyChatProvider provider =
                AW3MultiplayerDiplomacyChatFacade.Current;
            Kingdom requester = FindKingdom(_baseKingdomId);
            Kingdom responder = FindKingdom(_selectedKingdomId);
            if (provider == null || requester?.data == null ||
                responder?.data == null)
            {
                _sendButton.interactable = false;
                return;
            }
            AW3DiplomacyChatAvailability availability =
                GetChatAvailability(provider, requester.id, responder.id);
            _sendButton.interactable = availability.CanSend &&
                                       !string.IsNullOrWhiteSpace(pValue);
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            var root = new GameObject("DiplomacyConversationRoot",
                typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();

            CreateScrollArea(_root, "KingdomList", true,
                out _leftViewport, out _leftContent, out _leftScroll);
            _leftScrollbar = CreateVerticalScrollbar(_leftViewport,
                _leftScroll);
            CreateScrollArea(_root, "Conversation", true,
                out _chatViewport, out _chatContent, out _chatScroll);
            var divider = new GameObject("Divider", typeof(RectTransform),
                typeof(Image));
            divider.transform.SetParent(_root, false);
            _divider = divider.GetComponent<Image>();
            _divider.color = new Color(.70f, .57f, .31f, .72f);
            _divider.raycastTarget = false;

            _header = CreateText(_root, "ConversationHeader", 11,
                TextAnchor.MiddleLeft);
            _header.supportRichText = true;
            _header.color = Color.white;
            _kingdomBack = CreateCommandButton(_root, "BackToKingdom",
                BackToKingdom, out Text kingdomBackText);
            kingdomBackText.text = AW_L10n.Text("aw_back_to_kingdom",
                "Back to Kingdom");
            _emptyCountries = CreateText(_leftContent, "NoCountries", 9,
                TextAnchor.MiddleCenter);
            _emptyCountries.text = AW_L10n.Text(
                "aw_diplomacy_no_countries", "No other kingdoms");
            LayoutElement countryEmptyLayout =
                _emptyCountries.gameObject.AddComponent<LayoutElement>();
            countryEmptyLayout.preferredHeight = 44f;
            _emptyEvents = CreateText(_chatContent, "NoEvents", 9,
                TextAnchor.MiddleCenter);
            _emptyEvents.text = AW_L10n.Text("aw_diplomacy_no_events",
                "No recorded diplomatic events");
            _emptyEvents.color = new Color(.70f, .68f, .62f, 1f);
            LayoutElement eventEmptyLayout =
                _emptyEvents.gameObject.AddComponent<LayoutElement>();
            eventEmptyLayout.preferredHeight = 56f;
            CreateComposer();
            CreateActionMenu();
            CreateSelectionPanel();
            SetWindowTitle();
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
                nativeScrollRect.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
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
                nativeViewport.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(contentWidth,
                    contentHeight);

            Layout(_root, 0f, 0f, contentWidth, contentHeight);
            float leftWidth = Mathf.Clamp(contentWidth * .30f, 132f, 220f);
            float rightX = leftWidth + 12f;
            float rightWidth = Mathf.Max(220f, contentWidth - rightX);
            Layout(_leftViewport, 0f, 0f, leftWidth, contentHeight);
            Layout(_divider.rectTransform, leftWidth + 5f, 0f, 2f,
                contentHeight);
            Layout(_header.rectTransform, rightX, 0f,
                Mathf.Max(80f, rightWidth - 80f), 40f);
            Layout(_kingdomBack?.GetComponent<RectTransform>(),
                rightX + rightWidth - 76f, 6f, 76f, 28f);
            float composerHeight = 32f;
            float actionHeight = _actionsExpanded
                ? Mathf.Clamp(contentHeight * .46f, 110f, 190f)
                : 0f;
            float chatHeight = Mathf.Max(70f, contentHeight - 44f -
                composerHeight - actionHeight -
                (_actionsExpanded ? 4f : 0f));
            Layout(_chatViewport, rightX, 44f, rightWidth, chatHeight);
            Layout(_actionMenu, rightX, 44f + chatHeight + 4f,
                rightWidth, actionHeight);
            _actionMenu.gameObject.SetActive(_actionsExpanded);
            Layout(_composer, rightX, contentHeight - composerHeight,
                rightWidth, composerHeight);
            Layout(_toggleButton?.GetComponent<RectTransform>(), 0f, 2f,
                88f, 28f);
            Layout(_sendButton?.GetComponent<RectTransform>(),
                rightWidth - 62f, 2f, 62f, 28f);
            Layout(_composerSummary?.rectTransform, 94f, 2f,
                Mathf.Max(40f, rightWidth - 162f), 28f);
            Layout(_chatInput?.GetComponent<RectTransform>(), 94f, 2f,
                Mathf.Max(40f, rightWidth - 162f), 28f);
            _leftContent.sizeDelta = new Vector2(leftWidth - 8f,
                _leftContent.sizeDelta.y);
            _chatContent.sizeDelta = new Vector2(rightWidth - 8f,
                _chatContent.sizeDelta.y);
            _chrome?.RepositionResizeHandle();
        }

        private void RefreshComposer(Kingdom pRequester,
            Kingdom pResponder)
        {
            _coalitionPreview = null;
            _marriagePreview = null;
            _operationPreview = null;
            if (_toggleText != null)
                _toggleText.text = _actionsExpanded
                    ? AW_L10n.Text("aw_diplomacy_actions_collapse", "Close")
                    : AW_L10n.Text("aw_diplomacy_actions_expand", "Actions");
            if (_sendText != null)
                _sendText.text = AW_L10n.Text("aw_diplomacy_send", "Send");

            bool selectedAvailable = false;
            string selectedReason = "";
            bool annexationTarget = IsAnnexationTarget(
                pRequester, pResponder);
            for (int i = 0; i < _actionRows.Count; i++)
            {
                ActionRow row = _actionRows[i];
                bool available;
                string reason;
                string title;
                bool expectedAccepted = false;
                int negotiationWarScore = 0;
                DiplomacyProposalAssessment acceptance = null;
                bool rowSelected = !row.DeclareWar &&
                    !row.WarNegotiation &&
                    (row.OperationType != DiplomaticOperationType.None
                        ? row.OperationType == _selectedOperationType
                        : row.Type == _selectedProposalType);
                if (row.DeclareWar)
                {
                    title = AW_L10n.Text("aw_diplomacy_action_declare_war",
                        "Declare war");
                    DiplomaticWarAvailabilityResult warAvailability =
                        DiplomaticWarDeclarationService.
                            ResolvePairAvailability(pRequester, pResponder);
                    available = warAvailability.Available;
                    reason = warAvailability.FailureReason;
                    expectedAccepted = available;
                }
                else if (row.WarNegotiation)
                {
                    title = AW_L10n.Text(
                        "aw_diplomacy_action_war_negotiation",
                        "War negotiation");
                    available = WarPeaceNegotiationController
                        .TryGetMenuWarScore(pRequester, pResponder,
                            out negotiationWarScore, out reason);
                    expectedAccepted = available;
                }
                else if (row.OperationType != DiplomaticOperationType.None)
                {
                    bool annexationOperation = annexationTarget &&
                        row.OperationType ==
                        DiplomaticOperationType.SpyNetwork;
                    title = annexationOperation
                        ? AW_L10n.Text(
                            "aw_diplomacy_action_plan_annexation",
                            "Plan annexation")
                        : OperationTypeName(row.OperationType);
                    if (!rowSelected)
                    {
                        available = true;
                        reason = "";
                        expectedAccepted = true;
                    }
                    else if (annexationOperation)
                    {
                        available = VassalService.CanAbsorbVassalByDecision(
                            pRequester, pResponder, out reason);
                        expectedAccepted = available;
                    }
                    else
                    {
                        if (row.OperationType ==
                            DiplomaticOperationType.ForgeDocuments)
                            EnsureForgeCities(pRequester, pResponder);
                        _operationPreview = row.OperationType ==
                                            DiplomaticOperationType.SpyNetwork
                            ? DiplomaticOperationService.PrepareSpyNetwork(
                                pRequester, pResponder)
                            : DiplomaticOperationService.PrepareForgeDocuments(
                                pRequester, pResponder,
                                FindCity(_selectedForgeCityId),
                                _strongForgery
                                    ? WarTerritoryService.PROJECT_STRONG_CLAIM
                                    : WarTerritoryService.PROJECT_WEAK_CLAIM);
                        available = _operationPreview.Available;
                        reason = _operationPreview.Reason;
                        expectedAccepted = available;
                    }
                }
                else
                {
                    title = DiplomacyConversationService.ProposalTypeName(
                        row.Type);
                    DiplomacyActionAssessment assessment;
                    if (row.Type == DiplomacyProposalType.Coalition &&
                        rowSelected)
                    {
                        EnsureCoalitionTargets(pRequester, pResponder);
                        assessment = DiplomacyProposalService
                            .AssessWithSelection(pRequester, pResponder,
                                row.Type, -1L,
                                new DiplomacyProposalSelection(
                                    _selectedCoalitionTargetId, -1L, -1L,
                                    -1L, ""));
                        _coalitionPreview = DiplomaticCoalitionService.Prepare(
                            pRequester, pResponder,
                            FindKingdom(_selectedCoalitionTargetId));
                    }
                    else if (row.Type == DiplomacyProposalType.Coalition)
                    {
                        assessment = new DiplomacyActionAssessment
                        {
                            Allowed = true,
                            UnavailableReason = ""
                        };
                    }
                    else if (row.Type ==
                             DiplomacyProposalType.RoyalMarriage &&
                             rowSelected)
                    {
                        assessment = DiplomacyProposalService
                            .AssessRoyalMarriageWithPreview(pRequester,
                                pResponder, out _marriagePreview);
                    }
                    else if (row.Type ==
                             DiplomacyProposalType.RoyalMarriage)
                    {
                        assessment = new DiplomacyActionAssessment
                        {
                            Allowed = true,
                            UnavailableReason = ""
                        };
                    }
                    else if (row.Type ==
                             DiplomacyProposalType.HouseholdOffering)
                    {
                        assessment = new DiplomacyActionAssessment
                        {
                            Allowed = true,
                            UnavailableReason = ""
                        };
                    }
                    else
                    {
                        assessment = DiplomacyProposalService.Assess(
                            pRequester, pResponder, row.Type, -1L);
                    }
                    available = assessment.Allowed;
                    reason = assessment.UnavailableReason;
                    acceptance = assessment.Acceptance;
                    expectedAccepted = acceptance?.ExpectedAccepted == true;
                }

                row.Button.interactable = available && !_commandPending;
                row.Text.text = title;
                AW_UIStyle.ApplyButton(row.Button.GetComponent<Image>(),
                    available ? .96f : .48f);
                if (row.StateText != null)
                {
                    if (row.WarNegotiation)
                    {
                        ApplyWarNegotiationScore(row, available,
                            negotiationWarScore);
                    }
                    else
                    {
                        bool requiresSecondarySelection =
                            row.OperationType !=
                            DiplomaticOperationType.None ||
                            row.Type == DiplomacyProposalType.Coalition ||
                            row.Type == DiplomacyProposalType.RoyalMarriage ||
                            row.Type ==
                            DiplomacyProposalType.HouseholdOffering;
                        DiplomacySelectionIndicator indicator =
                            DiplomacyActionExpansionRules
                                .ResolveSelectionIndicator(available,
                                    rowSelected,
                                    requiresSecondarySelection,
                                    expectedAccepted);
                        row.StateText.text = indicator switch
                        {
                            DiplomacySelectionIndicator.Accept => "\u2713",
                            DiplomacySelectionIndicator.Reject => "\u2717",
                            DiplomacySelectionIndicator.Neutral => "\u00b7",
                            _ => "-"
                        };
                        row.StateText.color = indicator switch
                        {
                            DiplomacySelectionIndicator.Accept =>
                                new Color(.42f, .90f, .46f, 1f),
                            DiplomacySelectionIndicator.Reject =>
                                new Color(.94f, .38f, .34f, 1f),
                            _ => new Color(.62f, .60f, .55f, 1f)
                        };
                    }
                }
                if (row.Tip != null)
                {
                    string detail = row.WarNegotiation
                        ? available
                            ? AW_L10n.Text(
                                "aw_diplomacy_action_war_negotiation_desc",
                                "Review bilateral demands and concessions") +
                              "\n" + AW_L10n.Text(
                                  "aw_diplomacy_war_score",
                                  "War score") + ": " +
                              DiplomacyWarScoreIndicatorRules.Format(
                                  negotiationWarScore)
                            : ProposalFailure(reason)
                        : available
                        ? row.DeclareWar
                            ? AW_L10n.Text("aw_diplomacy_action_available",
                                "Available")
                            : row.OperationType !=
                              DiplomaticOperationType.None
                                ? annexationTarget && row.OperationType ==
                                  DiplomaticOperationType.SpyNetwork
                                    ? AnnexationDetail(available, reason)
                                    : CovertPreviewDetail(_operationPreview)
                            : DiplomacyProposalRules.IsUnilateral(row.Type)
                                ? UnilateralActionDetail(row.Type)
                                : AssessmentDetail(row.Type, acceptance)
                        : ProposalFailure(reason);
                    row.Tip.enabled = true;
                    row.Tip.hoverAction = () => Tooltip.show(
                        row.Tip.gameObject, AW_RawTooltip.TYPE,
                        new TooltipData
                        {
                            tip_name = title,
                            tip_description = detail
                        });
                }
                if (row.WarNegotiation)
                    RememberWarNegotiationIndicator(pRequester.id,
                        pResponder.id, available, negotiationWarScore,
                        reason);
                if (rowSelected)
                {
                    selectedAvailable = available;
                    selectedReason = reason;
                }
            }

            bool hasSelection = _selectedProposalType !=
                                DiplomacyProposalType.None ||
                                _selectedOperationType !=
                                DiplomaticOperationType.None;
            IAW3DiplomacyChatProvider provider =
                AW3MultiplayerDiplomacyChatFacade.Current;
            bool chatMode = provider != null && !hasSelection;
            AW3DiplomacyChatAvailability chatAvailability = chatMode
                ? GetChatAvailability(provider, pRequester.id, pResponder.id)
                : null;
            bool canSend = chatMode
                ? chatAvailability?.CanSend == true &&
                  !string.IsNullOrWhiteSpace(_chatInput?.text)
                : hasSelection && selectedAvailable && !_commandPending;
            if (_sendButton != null) _sendButton.interactable = canSend;
            if (_sendText != null)
                _sendText.text = canSend &&
                                 DiplomacyProposalRules.IsUnilateral(
                                     _selectedProposalType)
                    ? AW_L10n.Text("aw_diplomacy_execute", "Execute")
                    : AW_L10n.Text("aw_diplomacy_send", "Send");
            if (_composerSummary != null)
            {
                _composerSummary.gameObject.SetActive(!chatMode);
                _composerSummary.text = hasSelection
                    ? SelectionSummary() + (canSend ? "" : ": " +
                        ProposalFailure(selectedReason))
                    : AW_L10n.Text("aw_diplomacy_no_action_selected",
                        "No action selected");
            }
            if (_chatInput != null)
            {
                _chatInput.gameObject.SetActive(chatMode);
                _chatInput.interactable = chatAvailability?.CanSend == true;
            }
            if (_chatPlaceholder != null && chatMode)
                _chatPlaceholder.text = chatAvailability?.CanSend == true ||
                                        string.IsNullOrWhiteSpace(
                                            chatAvailability?.Detail)
                    ? AW_L10n.Text("aw_diplomacy_chat_placeholder",
                        "Message")
                    : chatAvailability.Detail;
            RefreshSelectionPanel(pRequester, pResponder);
        }

        private static void ApplyWarNegotiationScore(ActionRow pRow,
            bool pAvailable, int pScore)
        {
            if (pRow?.StateText == null) return;
            if (!pAvailable)
            {
                pRow.StateText.text = "-";
                pRow.StateText.color = new Color(.62f, .60f, .55f, 1f);
                return;
            }
            pRow.StateText.text = DiplomacyWarScoreIndicatorRules.Format(
                pScore);
            DiplomacyWarScoreTone tone =
                DiplomacyWarScoreIndicatorRules.Tone(pScore);
            pRow.StateText.color = tone switch
            {
                DiplomacyWarScoreTone.Positive =>
                    new Color(.42f, .90f, .46f, 1f),
                DiplomacyWarScoreTone.Negative =>
                    new Color(.94f, .38f, .34f, 1f),
                DiplomacyWarScoreTone.Neutral =>
                    new Color(.94f, .76f, .30f, 1f),
                _ => new Color(.62f, .60f, .55f, 1f)
            };
        }

        internal static string ProposalFailure(string pReason)
        {
            string reason = DiplomacyFailureReasonRules.StableKey(pReason);
            return reason switch
            {
                "pending_exists" => AW_L10n.Text(
                    "aw_diplomacy_failure_pending",
                    "A request is already awaiting a response"),
                "expired" => AW_L10n.Text(
                    "aw_diplomacy_failure_expired", "The request expired"),
                "write_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_write", "The request could not be recorded"),
                "execution_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_execution",
                    "The agreed action could not be completed"),
                "not_found" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_found", "The request no longer exists"),
                "already_responded" => AW_L10n.Text(
                    "aw_diplomacy_failure_already_responded",
                    "The request has already been answered"),
                "no_longer_available" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_longer_available",
                    "Changed circumstances prevent this agreement"),
                "at_war" => AW_L10n.Text(
                    "aw_diplomacy_failure_at_war", "The realms are at war"),
                "not_at_war" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_at_war", "The realms are not at war"),
                "not_war_leader" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_war_leader",
                    "Only the principal belligerents may negotiate peace"),
                "war_no_longer_active" => AW_L10n.Text(
                    "aw_diplomacy_failure_war_no_longer_active",
                    "The war has already ended"),
                "war_score_unavailable" => AW_L10n.Text(
                    "aw_diplomacy_failure_war_score_unavailable",
                    "The current war score could not be read"),
                "invalid_peace_draft" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid_peace_draft",
                    "The peace terms no longer match this war"),
                "rebellion_uses_direct_territory_transfer" => AW_L10n.Text(
                    "aw_diplomacy_failure_rebellion_direct_transfer",
                    "Rebellion territory transfers by direct capture and " +
                    "cannot use ordinary peace talks"),
                "replica_read_only" => AW_L10n.Text(
                    "aw_diplomacy_failure_replica_read_only",
                    "Only the multiplayer host may submit peace terms"),
                "peace_submit_exception" => AW_L10n.Text(
                    "aw_diplomacy_failure_peace_submit_exception",
                    "The peace request encountered an internal error"),
                "invalid_settlement_participants" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid_settlement_participants",
                    "The negotiating realms no longer match this war"),
                "invalid_term_count" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid_term_count",
                    "Select a valid number of peace terms"),
                "invalid_term_participants" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid_term_participants",
                    "One peace term names a realm outside this negotiation"),
                "no_territorial_basis" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_territorial_basis",
                    "The selected city is no longer occupied or claimed"),
                "not_losing_war" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_losing_war",
                    "Our realm is not currently losing this war"),
                "not_winning_war" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_winning_war",
                    "Our realm is not currently winning this war"),
                "already_allied" => AW_L10n.Text(
                    "aw_diplomacy_failure_already_allied", "The realms are already allied"),
                "not_allied" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_allied", "The realms are not allies"),
                "requester_subject" => AW_L10n.Text(
                    "aw_diplomacy_failure_requester_subject", "A subject cannot conduct this diplomacy"),
                "responder_subject" => AW_L10n.Text(
                    "aw_diplomacy_failure_responder_subject", "The target is already subject to another realm"),
                "no_joinable_war" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_joinable_war", "There is no war this ally can join"),
                "requires_mandate" => AW_L10n.Text(
                    "aw_diplomacy_failure_requires_mandate", "Only the Mandate realm may demand tribute"),
                "active_non_aggression" => AW_L10n.Text(
                    "aw_diplomacy_failure_active_non_aggression", "A non-aggression pact is already active"),
                "subject_non_aggression" => AW_L10n.Text(
                    "aw_diplomacy_failure_subject_non_aggression",
                    "A direct subject relationship already guarantees non-aggression"),
                "no_active_non_aggression" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_active_non_aggression",
                    "There is no active non-aggression pact to break"),
                "invalid_participants" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid_participants",
                    "One of the realms is no longer valid"),
                "invalid" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid",
                    "These realms cannot declare war on each other"),
                "non_aggression_pact" => AW_L10n.Text(
                    "aw_diplomacy_failure_non_aggression_pact",
                    "A treaty or truce currently prevents war"),
                "active_tributary_protection" => AW_L10n.Text(
                    "aw_diplomacy_failure_active_tributary_protection",
                    "An active tributary protection relation prevents this war"),
                "same_alliance" => AW_L10n.Text(
                    "aw_diplomacy_failure_same_alliance",
                    "Members of the same alliance cannot declare war"),
                "vassal_external_war_blocked" => AW_L10n.Text(
                    "aw_diplomacy_failure_vassal_external_war_blocked",
                    "This subject lacks independent external war powers"),
                "already_at_war" => AW_L10n.Text(
                    "aw_diplomacy_failure_already_at_war",
                    "The realms are already at war"),
                "war_preparation" => AW_L10n.Text(
                    "aw_diplomacy_failure_war_preparation",
                    "A war declaration or military preparation is active between these realms"),
                "no_war_reasons" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_war_reasons",
                    "There are no usable war reasons"),
                "missing_mandate_cb" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_mandate_cb",
                    "There is no valid claim to contest the Mandate"),
                "missing_mandate_conquest_cb" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_mandate_conquest_cb",
                    "The conditions for Mandate conquest are not met"),
                "missing_core_target" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_core_target",
                    "There is no occupied core city to reclaim"),
                "missing_claim_target" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_claim_target",
                    "There is no valid territorial claim to press"),
                "cannot_force_vassal" => AW_L10n.Text(
                    "aw_diplomacy_failure_cannot_force_vassal",
                    "The target cannot be forced into vassalage"),
                "cannot_force_tributary" => AW_L10n.Text(
                    "aw_diplomacy_failure_cannot_force_tributary",
                    "The target cannot be forced to pay tribute"),
                "not_suzerain" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_suzerain",
                    "The target is not this realm's suzerain"),
                "missing_restoration_target" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_restoration_target",
                    "There is no valid realm restoration target"),
                "missing_reunification_claim" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_reunification_claim",
                    "There is no valid succession reunification claim"),
                "missing_zhulu_cb" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_zhulu_cb",
                    "The conditions for a contest war are not met"),
                "cannot_force_no_cb" => AW_L10n.Text(
                    "aw_diplomacy_failure_cannot_force_no_cb",
                    "A punitive war cannot currently be forced"),
                "unknown_goal" => AW_L10n.Text(
                    "aw_diplomacy_failure_unknown_goal",
                    "The selected war goal is unknown"),
                "only_origin_can_suppress_bandit" => AW_L10n.Text(
                    "aw_diplomacy_failure_only_origin_can_suppress_bandit",
                    "Only the bandit's origin realm may suppress it"),
                "missing_bandit_stronghold" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_bandit_stronghold",
                    "The bandit stronghold is no longer active"),
                "suppression_start_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_suppression_start_failed",
                    "The suppression war could not be started"),
                "no_vassal_relation" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_vassal_relation", "There is no direct subject relationship to end"),
                "title_too_low" => AW_L10n.Text(
                    "aw_diplomacy_failure_title_too_low",
                    "The suzerain must hold a higher rank than the target"),
                "not_adjacent" => AW_L10n.Text(
                    "aw_diplomacy_failure_not_adjacent",
                    "A subject request requires a shared border"),
                "cycle" or "vassal_cycle" => AW_L10n.Text(
                    "aw_diplomacy_failure_vassal_cycle",
                    "This agreement would create a circular subject relationship"),
                "rebel_no_vassal" => AW_L10n.Text(
                    "aw_diplomacy_failure_rebel_vassal",
                    "A rebel realm cannot become a subject through diplomacy"),
                "rebel_no_suzerain" => AW_L10n.Text(
                    "aw_diplomacy_failure_rebel_suzerain",
                    "A rebel realm cannot receive diplomatic subjects"),
                "alliance_conflict" => AW_L10n.Text(
                    "aw_diplomacy_failure_alliance_conflict",
                    "Both realms already belong to different alliances"),
                "alliance_members_refuse" => AW_L10n.Text(
                    "aw_diplomacy_failure_alliance_members_refuse",
                    "The existing alliance will not admit this realm"),
                "alliance_too_distant" => AW_L10n.Text(
                    "aw_diplomacy_failure_alliance_too_distant",
                    "The realms are too distant to form an alliance"),
                "alliance_unavailable" or "alliance_execution_failed" =>
                    AW_L10n.Text("aw_diplomacy_failure_alliance_unavailable",
                        "The alliance cannot currently be formed"),
                "join_war_execution_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_join_war_execution",
                    "The realm could not be added to the requested war"),
                "join_war_stale" => AW_L10n.Text(
                    "aw_diplomacy_failure_join_war_stale",
                    "The selected war is no longer available"),
                "protector_war_conflict" => AW_L10n.Text(
                    "aw_diplomacy_failure_protector_war_conflict",
                    "The proposed protector is already involved in a conflicting war"),
                "protector_too_weak" => AW_L10n.Text(
                    "aw_diplomacy_failure_protector_too_weak",
                    "The proposed protector is not strong enough"),
                "protector_relations_low" => AW_L10n.Text(
                    "aw_diplomacy_failure_protector_relations_low",
                    "Relations are too poor to request protection"),
                "protection_threat_stale" => AW_L10n.Text(
                    "aw_diplomacy_failure_protection_threat_stale",
                    "The threat used to justify protection is no longer valid"),
                "protection_war_entry_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_protection_war_entry",
                    "The protector could not enter the defensive war"),
                "internalization_target" or
                    "source_relation_missing" or
                    "source_relation_ambiguous" => AW_L10n.Text(
                    "aw_diplomacy_failure_internalization_target",
                    "This tributary can no longer enter the formal subject system"),
                "conversion_database_unavailable" or
                    "conversion_table_invalid" or
                    "conversion_target_invalid" or
                    "conversion_write_failed" => AW_L10n.Text(
                        "aw_diplomacy_failure_internalization_write",
                        "The tributary conversion could not be recorded"),
                "target_title_too_high" => AW_L10n.Text(
                    "aw_diplomacy_failure_internalization_title",
                    "The tributary's title is too high for internalization"),
                "rebel_blocked" => AW_L10n.Text(
                    "aw_diplomacy_failure_internalization_rebel",
                    "A rebel realm cannot be internalized"),
                "alliance_truce_write_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_alliance_truce_write",
                    "The alliance ended but its truce could not be recorded"),
                "invalid_vassalize_direction" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid_vassalize_direction",
                    "The requested subject relationship direction is invalid"),
                "invalid_end_vassal_direction" => AW_L10n.Text(
                    "aw_diplomacy_failure_invalid_end_vassal_direction",
                    "The requested release direction is invalid"),
                "subject_write_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_subject_write",
                    "The subject agreement could not be recorded"),
                "coalition_target_required" or "invalid_coalition_target" =>
                    AW_L10n.Text("aw_diplomacy_failure_coalition_target",
                        "Select a valid common threat"),
                "coalition_limit" => AW_L10n.Text(
                    "aw_diplomacy_failure_coalition_limit",
                    "One member already has two active coalitions"),
                "active_coalition" => AW_L10n.Text(
                    "aw_diplomacy_failure_active_coalition",
                    "This coalition is already active"),
                "missing_royal_house" => AW_L10n.Text(
                    "aw_diplomacy_failure_missing_royal_house",
                    "Both realms need a royal house"),
                "active_royal_marriage" => AW_L10n.Text(
                    "aw_diplomacy_failure_active_royal_marriage",
                    "The two royal houses are already joined by marriage"),
                "no_requester_royal_candidate" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_requester_royal_candidate",
                    "Our realm has no eligible royal candidate"),
                "no_responder_royal_candidate" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_responder_royal_candidate",
                    "The other realm has no eligible royal candidate"),
                "no_compatible_royal_pair" => AW_L10n.Text(
                    "aw_diplomacy_failure_no_compatible_royal_pair",
                    "The available royal candidates cannot form a valid marriage"),
                "no_royal_candidate" or "marriage_candidate_stale" =>
                    AW_L10n.Text("aw_diplomacy_failure_no_royal_candidate",
                        "No eligible fixed royal pair remains"),
                "marriage_write_failed" or "coalition_write_failed" or
                    "covert_operation_write_failed" => AW_L10n.Text(
                        "aw_diplomacy_failure_write",
                        "The request could not be recorded"),
                "cannot_spy_on_suzerain" => AW_L10n.Text(
                    "aw_diplomacy_failure_spy_suzerain",
                    "A subject cannot spy on its suzerain"),
                "covert_operation_pending" => AW_L10n.Text(
                    "aw_diplomacy_failure_covert_pending",
                    "Another covert operation is already underway"),
                "spy_network_active" => AW_L10n.Text(
                    "aw_diplomacy_failure_network_active",
                    "A spy network is already active"),
                "spy_network_required" => AW_L10n.Text(
                    "aw_diplomacy_failure_network_required",
                    "An active spy network is required"),
                "target_city_changed" => AW_L10n.Text(
                    "aw_diplomacy_failure_target_city_changed",
                    "The target city changed hands"),
                "war_target_option_changed" => AW_L10n.Text(
                    "aw_diplomacy_failure_war_target_option_changed",
                    "The basis for this war goal changed; select it again"),
                "fabrication_unavailable" => AW_L10n.Text(
                    "aw_diplomacy_failure_fabrication_unavailable",
                    "No claim can be fabricated for this city"),
                "network_too_weak" => AW_L10n.Text(
                    "aw_diplomacy_failure_network_too_weak",
                    "The spy network is too weak for a strong claim"),
                "insufficient_spy_points" => AW_L10n.Text(
                    "aw_diplomacy_failure_insufficient_spy_points",
                    "The spy network has insufficient points"),
                "claim_already_purchased" => AW_L10n.Text(
                    "aw_diplomacy_failure_claim_already_purchased",
                    "This claim has already been purchased"),
                "claim_creation_failed" => AW_L10n.Text(
                    "aw_diplomacy_failure_claim_creation_failed",
                    "The claim could not be recorded; no points were spent"),
                "household_not_ready" => AW_L10n.Text(
                    "aw_household_failure_not_ready",
                    "The household archive is not ready"),
                "invalid_household_realms" => AW_L10n.Text(
                    "aw_household_failure_realms",
                    "The two realms cannot make this household offer"),
                "invalid_household_ruler" or "household_ruler_stale" =>
                    AW_L10n.Text("aw_household_failure_ruler",
                        "The recipient ruler is no longer eligible"),
                "candidate_not_domestic" => AW_L10n.Text(
                    "aw_household_failure_candidate_domestic",
                    "The offered woman must belong to our realm"),
                "candidate_not_female" => AW_L10n.Text(
                    "aw_household_failure_candidate_female",
                    "Only a woman may be offered"),
                "candidate_not_adult" or
                    "candidate_not_household_age" => AW_L10n.Text(
                        "aw_household_failure_candidate_age",
                        "The offered noblewoman must be 18 to 33 years old"),
                "candidate_not_noble" or
                    "candidate_not_noble_lineage" => AW_L10n.Text(
                        "aw_household_failure_candidate_not_noble_lineage",
                        "The offered woman must belong to an established noble clan"),
                "candidate_is_ruler" => AW_L10n.Text(
                    "aw_household_failure_candidate_ruler",
                    "A reigning ruler cannot be offered"),
                "candidate_is_slave" => AW_L10n.Text(
                    "aw_household_failure_candidate_slave",
                    "A slave cannot be offered"),
                "candidate_married" => AW_L10n.Text(
                    "aw_household_failure_candidate_married",
                    "The offered noblewoman is already married"),
                "candidate_in_household" => AW_L10n.Text(
                    "aw_household_failure_candidate_household",
                    "The offered noblewoman already belongs to a ruler household"),
                "household_close_relative" => AW_L10n.Text(
                    "aw_household_failure_related",
                    "The offered woman is too closely related to the ruler"),
                "principal_wife_exists" => AW_L10n.Text(
                    "aw_household_failure_principal_wife_exists",
                    "The ruler already has a principal wife"),
                "consort_capacity_full" => AW_L10n.Text(
                    "aw_household_failure_consort_capacity_full",
                    "The ruler household has no open consort position"),
                "consort_request_requires_independence" => AW_L10n.Text(
                    "aw_household_failure_request_independence",
                    "Only independent realms may exchange a requested consort"),
                "consort_request_relation_low" => AW_L10n.Text(
                    "aw_household_failure_request_relation",
                    "Relations are not close enough for this request"),
                "household_candidate_selection_required" => AW_L10n.Text(
                    "aw_household_failure_request_selection",
                    "Select a noblewoman before accepting this request"),
                "household_candidate_already_selected" => AW_L10n.Text(
                    "aw_household_failure_candidate_selected",
                    "Another candidate has already been selected"),
                "no_household_candidate" or
                    "invalid_household_candidate" => AW_L10n.Text(
                        "aw_household_failure_no_candidate",
                        "Our realm has no eligible noblewoman"),
                "household_migration_failed" or
                    "invalid_recipient_capital" => AW_L10n.Text(
                        "aw_household_failure_migration",
                        "The noblewoman could not enter the recipient capital"),
                _ => AW_L10n.Text("aw_diplomacy_failure_unavailable",
                         "This action is currently unavailable") +
                     " (" + reason + ")"
            };
        }

        private static string AssessmentDetail(DiplomacyProposalType pType,
            DiplomacyProposalAssessment pAssessment)
        {
            if (pAssessment == null)
                return AW_L10n.Text("aw_diplomacy_failure_unavailable",
                    "This action is currently unavailable");
            var text = new System.Text.StringBuilder();
            string outcome = PeaceOutcomeDetail(pType);
            if (!string.IsNullOrEmpty(outcome))
                text.AppendLine(outcome);
            text.AppendLine(pAssessment.ExpectedAccepted
                ? "\u2713 " + AW_L10n.Text("aw_diplomacy_expected_accept",
                    "Expected to accept")
                : "\u2717 " + AW_L10n.Text("aw_diplomacy_expected_reject",
                    "Expected to reject"));
            text.Append(AW_L10n.Text("aw_diplomacy_acceptance_score",
                    "Acceptance score"))
                .Append(": ").Append(pAssessment.Score).Append(" / ")
                .Append(pAssessment.Threshold);
            for (int i = 0; i < pAssessment.Parts.Count; i++)
            {
                DiplomacyProposalScorePart part = pAssessment.Parts[i];
                text.Append('\n').Append(AssessmentPartLabel(part.Key))
                    .Append(": ");
                if (part.Value >= 0) text.Append('+');
                text.Append(part.Value);
            }
            return text.ToString();
        }

        private static string PeaceOutcomeDetail(DiplomacyProposalType pType)
        {
            return pType switch
            {
                DiplomacyProposalType.Peace => AW_L10n.Text(
                    "aw_diplomacy_action_peace_desc",
                    "End the war without a victor; neither side enforces its demands."),
                DiplomacyProposalType.Surrender => AW_L10n.Text(
                    "aw_diplomacy_action_surrender_desc",
                    "Admit defeat; the other realm wins and enforces the war result."),
                DiplomacyProposalType.EnforceDemands => AW_L10n.Text(
                    "aw_diplomacy_action_enforce_demands_desc",
                    "Demand the other realm concede; our realm wins and enforces the war result."),
                _ => ""
            };
        }

        private static string UnilateralActionDetail(
            DiplomacyProposalType pType)
        {
            return pType == DiplomacyProposalType.BreakNonAggression
                ? AW_L10n.Text(
                    "aw_diplomacy_action_break_non_aggression_desc",
                    "Unilaterally end the pact and enter a five-year truce; " +
                    "ordinary wars remain blocked during the truce.")
                : AW_L10n.Text("aw_diplomacy_action_available", "Available");
        }

        private static string AssessmentPartLabel(string pKey)
        {
            return AW_L10n.Text("aw_diplomacy_score_" + pKey, pKey switch
            {
                "base" => "Base willingness",
                "opinion" => "Opinion",
                "shared_enemy" => "Shared enemy",
                "mandate" => "Mandate prestige",
                "diplomacy" => "Ruler diplomacy",
                "war_situation" => "War situation",
                "power" => "Relative military power",
                "alliance" => "Alliance obligation",
                "direct_royal_marriage" => "Direct royal children",
                _ => pKey
            });
        }

        private void CreateComposer()
        {
            _composer = new GameObject("DiplomacyComposer",
                typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _composer.SetParent(_root, false);
            Image background = _composer.GetComponent<Image>();
            background.color = new Color(.09f, .085f, .07f, .98f);
            _toggleButton = CreateCommandButton(_composer, "ToggleActions",
                ToggleActions, out _toggleText);
            _sendButton = CreateCommandButton(_composer, "SendProposal",
                SendComposer, out _sendText);
            _composerSummary = CreateText(_composer, "Summary", 8,
                TextAnchor.MiddleLeft);
            _chatInput = CreateChatInput(_composer);
            _chatInput.gameObject.SetActive(false);
        }

        private InputField CreateChatInput(Transform pParent)
        {
            var inputObject = new GameObject("MultiplayerChatInput",
                typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(inputObject.GetComponent<Image>(), .90f);

            Text value = CreateText(inputObject.transform, "Text", 9,
                TextAnchor.MiddleLeft);
            value.supportRichText = false;
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = new Vector2(5f, 1f);
            value.rectTransform.offsetMax = new Vector2(-5f, -1f);

            _chatPlaceholder = CreateText(inputObject.transform,
                "Placeholder", 8, TextAnchor.MiddleLeft);
            _chatPlaceholder.color = new Color(1f, 1f, 1f, .42f);
            _chatPlaceholder.text = AW_L10n.Text(
                "aw_diplomacy_chat_placeholder", "Message");
            _chatPlaceholder.rectTransform.anchorMin = Vector2.zero;
            _chatPlaceholder.rectTransform.anchorMax = Vector2.one;
            _chatPlaceholder.rectTransform.offsetMin = new Vector2(5f, 1f);
            _chatPlaceholder.rectTransform.offsetMax = new Vector2(-5f, -1f);

            _chatInput = inputObject.GetComponent<InputField>();
            _chatInput.textComponent = value;
            _chatInput.placeholder = _chatPlaceholder;
            _chatInput.characterLimit = 256;
            _chatInput.lineType = InputField.LineType.SingleLine;
            _chatInput.onValueChanged.AddListener(OnChatInputChanged);
            return _chatInput;
        }

        private void CreateActionMenu()
        {
            CreateScrollArea(_root, "DiplomacyActions", true,
                out _actionMenu, out _actionContent, out _actionScroll);
            _actionScrollbar = CreateVerticalScrollbar(_actionMenu,
                _actionScroll);
            AddActionHeader("aw_diplomacy_group_friendly", "Friendly");
            AddAction(DiplomacyProposalType.Alliance, false,
                "ui/icons/iconAlliance");
            AddAction(DiplomacyProposalType.NonAggression, false,
                "ui/icons/actor_traits/iconPeaceful");
            AddAction(DiplomacyProposalType.JoinWar, false,
                "ui/icons/iconWar");
            AddActionHeader("aw_diplomacy_group_coalition_marriage",
                "Coalitions and marriage");
            AddAction(DiplomacyProposalType.Coalition, false,
                "ui/icons/iconAlliance");
            AddAction(DiplomacyProposalType.RoyalMarriage, false,
                "ui/icons/iconFavorite");
            AddAction(DiplomacyProposalType.HouseholdOffering, false,
                "ui/icons/iconKing");
            AddActionHeader("aw_diplomacy_group_strategy", "Strategy");
            AddOperation(DiplomaticOperationType.SpyNetwork,
                "ui/icons/iconDiplomacy");
            AddOperation(DiplomaticOperationType.ForgeDocuments,
                "ui/icons/iconBook");
            AddActionHeader("aw_diplomacy_group_war", "War");
            AddAction(DiplomacyProposalType.None, true,
                "ui/wars/war_conquest");
            AddWarNegotiationAction(
                "ui/icons/actor_traits/iconPeaceful");
            AddActionHeader("aw_diplomacy_group_vassal", "Vassalage");
            AddAction(DiplomacyProposalType.Vassalize, false,
                "ui/wars/war_vassal");
            AddAction(DiplomacyProposalType.Tributary, false,
                "ui/Icons/traits/iconTianming");
            AddAction(DiplomacyProposalType.EndVassal, false,
                "ui/wars/war_independent");
            AddActionHeader("aw_diplomacy_group_treaty", "Treaties");
            AddAction(DiplomacyProposalType.BreakNonAggression, false,
                "ui/icons/iconAllianceDissolved");
            AddAction(DiplomacyProposalType.EndAlliance, false,
                "ui/icons/iconAllianceDissolved");
            _actionMenu.gameObject.SetActive(false);
        }

        private void CreateSelectionPanel()
        {
            _selectionPanel = new GameObject("DiplomacySecondarySelector",
                typeof(RectTransform), typeof(Image),
                typeof(LayoutElement)).GetComponent<RectTransform>();
            _selectionPanel.SetParent(_actionContent, false);
            Image background = _selectionPanel.GetComponent<Image>();
            background.color = new Color(.12f, .105f, .075f, .96f);
            LayoutElement layout = _selectionPanel.GetComponent<LayoutElement>();
            layout.minHeight = DiplomacyConversationRules.SecondarySelectorHeight;
            layout.preferredHeight =
                DiplomacyConversationRules.SecondarySelectorHeight;
            _selectionPrevious = CreateCommandButton(_selectionPanel,
                "Previous", () => CycleSecondarySelection(-1), out Text previous);
            previous.text = "<";
            _selectionNext = CreateCommandButton(_selectionPanel,
                "Next", () => CycleSecondarySelection(1), out Text next);
            next.text = ">";
            _selectionMode = CreateCommandButton(_selectionPanel,
                "ForgeryMode", ToggleForgeryMode, out _selectionModeText);
            Layout(_selectionPrevious.GetComponent<RectTransform>(), 3f, 9f,
                24f, DiplomacyConversationRules.SecondaryCommandHeight);
            RectTransform nextRect = _selectionNext.GetComponent<RectTransform>();
            nextRect.anchorMin = nextRect.anchorMax = new Vector2(1f, 1f);
            nextRect.pivot = new Vector2(1f, 1f);
            nextRect.anchoredPosition = new Vector2(-3f, -9f);
            nextRect.sizeDelta = new Vector2(24f,
                DiplomacyConversationRules.SecondaryCommandHeight);
            RectTransform modeRect = _selectionMode.GetComponent<RectTransform>();
            modeRect.anchorMin = modeRect.anchorMax = new Vector2(1f, 1f);
            modeRect.pivot = new Vector2(1f, 1f);
            modeRect.anchoredPosition = new Vector2(-30f, -9f);
            modeRect.sizeDelta = new Vector2(58f,
                DiplomacyConversationRules.SecondaryCommandHeight);

            _selectionTitle = CreateText(_selectionPanel, "SelectionTitle", 9,
                TextAnchor.UpperLeft);
            _selectionTitle.rectTransform.anchorMin = Vector2.zero;
            _selectionTitle.rectTransform.anchorMax = Vector2.one;
            _selectionTitle.rectTransform.offsetMin = new Vector2(82f, 22f);
            _selectionTitle.rectTransform.offsetMax = new Vector2(-82f, -3f);
            _selectionDetail = CreateText(_selectionPanel, "SelectionDetail", 7,
                TextAnchor.LowerLeft);
            _selectionDetail.rectTransform.anchorMin = Vector2.zero;
            _selectionDetail.rectTransform.anchorMax = Vector2.one;
            _selectionDetail.rectTransform.offsetMin = new Vector2(82f, 3f);
            _selectionDetail.rectTransform.offsetMax = new Vector2(-82f, -22f);
            _selectionDetail.color = new Color(.78f, .74f, .65f, 1f);

            _selectionFlagBackground = CreateSelectorImage("TargetFlag", 28f);
            _selectionFlagIcon = CreateSelectorImage("TargetFlagIcon", 28f);
            _selectionFlagIcon.transform.SetParent(
                _selectionFlagBackground.transform, false);
            RectTransform flagIconRect = _selectionFlagIcon.rectTransform;
            flagIconRect.anchorMin = Vector2.zero;
            flagIconRect.anchorMax = Vector2.one;
            flagIconRect.offsetMin = flagIconRect.offsetMax = Vector2.zero;
            _selectionPortraitLeft = CreateSelectorPortrait("PortraitLeft", 28f);
            _selectionPortraitRight = CreateSelectorPortrait("PortraitRight", 28f);
            Layout(_selectionFlagBackground.rectTransform, 31f, 9f, 28f, 28f);
            Layout(_selectionPortraitLeft.Root.GetComponent<RectTransform>(),
                29f, 9f, 28f, 28f);
            Layout(_selectionPortraitRight.Root.GetComponent<RectTransform>(),
                57f, 9f, 28f, 28f);
            _selectionPanel.gameObject.SetActive(false);
        }

        private Image CreateSelectorImage(string pName, float pSize)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(_selectionPanel, false);
            Image image = obj.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = new Vector2(pSize, pSize);
            return image;
        }

        private LivePortraitSlot CreateSelectorPortrait(string pName,
            float pSize)
        {
            var root = new GameObject(pName, typeof(RectTransform));
            root.transform.SetParent(_selectionPanel, false);
            root.GetComponent<RectTransform>().sizeDelta =
                new Vector2(pSize, pSize);
            root.SetActive(false);
            return new LivePortraitSlot { Root = root };
        }

        private void RefreshSelectionPanel(Kingdom pRequester,
            Kingdom pResponder)
        {
            bool visible = _actionsExpanded &&
                (_selectedProposalType == DiplomacyProposalType.Coalition ||
                 _selectedProposalType == DiplomacyProposalType.RoyalMarriage ||
                 _selectedOperationType != DiplomaticOperationType.None);
            _selectionPanel.gameObject.SetActive(visible);
            if (!visible) return;
            ActionRow selectedRow = _actionRows.Find(pRow =>
                pRow.OperationType != DiplomaticOperationType.None
                    ? pRow.OperationType == _selectedOperationType
                    : pRow.Type == _selectedProposalType);
            if (selectedRow?.Button != null)
                _selectionPanel.SetSiblingIndex(
                    selectedRow.Button.transform.GetSiblingIndex() + 1);
            ClearSelectionImages();
            _selectionPrevious.gameObject.SetActive(false);
            _selectionNext.gameObject.SetActive(false);
            _selectionMode.gameObject.SetActive(false);

            if (_selectedProposalType == DiplomacyProposalType.Coalition)
            {
                SetSelectionTextInsets(64f, 30f);
                EnsureCoalitionTargets(pRequester, pResponder);
                Kingdom target = FindKingdom(_selectedCoalitionTargetId);
                _selectionTitle.text = AW_L10n.Text(
                        "aw_diplomacy_selector_coalition_prompt",
                        "Coalition target") + " · " + (target?.data == null
                    ? AW_L10n.Text("aw_diplomacy_no_coalition_target",
                        "No valid target")
                    : RulerAppellationService.GetProjectedStateName(target));
                _selectionDetail.text = _coalitionPreview?.Available == true
                    ? AW_L10n.Text("aw_diplomacy_action_available", "Available")
                    : ProposalFailure(_coalitionPreview?.Reason ??
                                      "invalid_coalition_target");
                BindSelectionFlag(target);
                bool many = _coalitionTargetIds.Count > 1;
                _selectionPrevious.gameObject.SetActive(many);
                _selectionNext.gameObject.SetActive(many);
                return;
            }
            if (_selectedProposalType == DiplomacyProposalType.RoyalMarriage)
            {
                SetSelectionTextInsets(90f, 8f);
                _marriagePreview ??= DiplomaticMarriageService.Prepare(
                    pRequester, pResponder);
                _selectionTitle.text = AW_L10n.Text(
                        "aw_diplomacy_selector_marriage_prompt",
                        "Marriage pair") + " · " + (_marriagePreview.Available
                    ? _marriagePreview.RequesterActorName + "  -  " +
                      _marriagePreview.ResponderActorName
                    : AW_L10n.Text("aw_diplomacy_no_marriage_pair",
                        "No eligible royal pair"));
                _selectionDetail.text = _marriagePreview.Available
                    ? AW_L10n.Text(_marriagePreview.DirectRoyalMarriage
                            ? "aw_diplomacy_marriage_direct"
                            : "aw_diplomacy_marriage_collateral",
                        _marriagePreview.DirectRoyalMarriage
                            ? "Rulers or direct royal children"
                            : "Collateral royal kin")
                    : ProposalFailure(_marriagePreview.Reason);
                BindPortrait(_selectionPortraitLeft,
                    _marriagePreview.RequesterActorId);
                BindPortrait(_selectionPortraitRight,
                    _marriagePreview.ResponderActorId);
                return;
            }

            bool annexationOperation = _selectedOperationType ==
                DiplomaticOperationType.SpyNetwork &&
                IsAnnexationTarget(pRequester, pResponder);
            string operationName = annexationOperation
                ? AW_L10n.Text("aw_diplomacy_action_plan_annexation",
                    "Plan annexation")
                : OperationTypeName(_selectedOperationType);
            SetSelectionTextInsets(8f, 8f);
            _selectionTitle.text = (_selectedOperationType ==
                    DiplomaticOperationType.SpyNetwork
                    ? AW_L10n.Text("aw_diplomacy_selector_spy_prompt",
                        "Spy target") + " · "
                    : "") + operationName;
            _selectionDetail.text = annexationOperation
                ? AnnexationDetail(VassalService.CanAbsorbVassalByDecision(
                    pRequester, pResponder, out string annexReason),
                    annexReason)
                : CovertPreviewDetail(_operationPreview);
            if (annexationOperation)
                _selectionTitle.text = AW_L10n.Text(
                    "aw_diplomacy_selector_annexation_prompt",
                    "Annexation target") + " - " + operationName;
            if (_selectedOperationType ==
                DiplomaticOperationType.ForgeDocuments)
            {
                SetSelectionTextInsets(30f, 92f);
                EnsureForgeCities(pRequester, pResponder);
                City city = FindCity(_selectedForgeCityId);
                _selectionTitle.text = AW_L10n.Text(
                        "aw_diplomacy_selector_forgery_prompt",
                        "Forgery target") + " · " + operationName + "  -  " +
                    (city?.data?.name ?? AW_L10n.Text(
                        "aw_diplomacy_no_forgery_city", "No target city"));
                bool many = _forgeCityIds.Count > 1;
                _selectionPrevious.gameObject.SetActive(many);
                _selectionNext.gameObject.SetActive(many);
                _selectionMode.gameObject.SetActive(true);
                _selectionModeText.text = AW_L10n.Text(_strongForgery
                        ? "aw_diplomacy_forgery_strong"
                        : "aw_diplomacy_forgery_weak",
                    _strongForgery ? "Strong claim" : "Weak claim");
            }
        }

        private void ClearSelectionImages()
        {
            _selectionFlagBackground.gameObject.SetActive(false);
            HidePortrait(_selectionPortraitLeft);
            HidePortrait(_selectionPortraitRight);
        }

        private void SetSelectionTextInsets(float pLeft, float pRight)
        {
            DiplomacySelectorInsets fitted =
                DiplomacyConversationRules.FitSelectorInsets(
                    _selectionPanel.rect.width, pLeft, pRight);
            _selectionTitle.rectTransform.offsetMin =
                new Vector2(fitted.Left, 22f);
            _selectionTitle.rectTransform.offsetMax =
                new Vector2(-fitted.Right, -3f);
            _selectionDetail.rectTransform.offsetMin =
                new Vector2(fitted.Left, 3f);
            _selectionDetail.rectTransform.offsetMax =
                new Vector2(-fitted.Right, -22f);
        }

        private void BindSelectionFlag(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            try
            {
                string bannerId = pKingdom.getActorAsset()?.banner_id ?? "";
                KingdomFlagBuilder.Build(bannerId,
                    pKingdom.data.banner_icon_id,
                    pKingdom.data.banner_background_id,
                    HistoryColors.FromKingdom(pKingdom),
                    pKingdom.data.color_id, _selectionFlagBackground,
                    _selectionFlagIcon);
                _selectionFlagBackground.gameObject.SetActive(true);
            }
            catch { }
        }

        private static void BindPortrait(LivePortraitSlot pSlot, long pActorId)
        {
            if (pSlot?.Root == null) return;
            Actor actor = FindActor(pActorId);
            if (actor?.data == null || !actor.isAlive() || actor.isRekt())
            {
                HidePortrait(pSlot);
                return;
            }
            if (pSlot.Avatar == null)
            {
                UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
                if (prefab == null)
                {
                    HidePortrait(pSlot);
                    return;
                }
                pSlot.Avatar = UnityEngine.Object.Instantiate(prefab,
                    pSlot.Root.transform);
                RectTransform rect = pSlot.Avatar.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }
            pSlot.ActorId = pActorId;
            pSlot.Root.SetActive(true);
            pSlot.Avatar.enabled = true;
            if (pSlot.Avatar.avatarLoader != null)
                pSlot.Avatar.avatarLoader.enabled = true;
            pSlot.Avatar.show(actor);
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static void HidePortrait(LivePortraitSlot pSlot)
        {
            if (pSlot == null) return;
            pSlot.ActorId = -1L;
            pSlot.Root?.SetActive(false);
        }

        private void CycleSecondarySelection(int pDirection)
        {
            if (_commandPending) return;
            if (_selectedProposalType == DiplomacyProposalType.Coalition)
                _selectedCoalitionTargetId = CycleId(_coalitionTargetIds,
                    _selectedCoalitionTargetId, pDirection);
            else if (_selectedOperationType ==
                     DiplomaticOperationType.ForgeDocuments)
                _selectedForgeCityId = CycleId(_forgeCityIds,
                    _selectedForgeCityId, pDirection);
            Refresh();
        }

        private void ToggleForgeryMode()
        {
            if (_commandPending) return;
            _strongForgery = !_strongForgery;
            Refresh();
        }

        private static long CycleId(List<long> pIds, long pCurrent,
            int pDirection)
        {
            if (pIds == null || pIds.Count == 0) return -1L;
            int index = pIds.IndexOf(pCurrent);
            if (index < 0) index = 0;
            index = (index + pDirection) % pIds.Count;
            if (index < 0) index += pIds.Count;
            return pIds[index];
        }

        private void EnsureCoalitionTargets(Kingdom pRequester,
            Kingdom pResponder)
        {
            _coalitionTargetIds.Clear();
            float memberPower = Mathf.Max(pRequester?.power ?? 0f,
                pResponder?.power ?? 0f);
            if (World.world?.kingdoms != null)
                foreach (Kingdom target in World.world.kingdoms)
                {
                    if (_coalitionTargetIds.Count >=
                        DiplomacyActionExpansionRules.MaximumCoalitionTargets)
                        break;
                    if (target?.data == null || target == pRequester ||
                        target == pResponder || target.isRekt() ||
                        target.isNeutral() || !target.isCiv()) continue;
                    if (!MandateService.IsMandateKingdom(target) &&
                        target.power < Mathf.Max(1f, memberPower) * 1.25f)
                        continue;
                    _coalitionTargetIds.Add(target.id);
                }
            if (!_coalitionTargetIds.Contains(_selectedCoalitionTargetId))
                _selectedCoalitionTargetId = _coalitionTargetIds.Count > 0
                    ? _coalitionTargetIds[0]
                    : -1L;
        }

        private void EnsureForgeCities(Kingdom pRequester,
            Kingdom pResponder)
        {
            _forgeCityIds.Clear();
            int scanned = 0;
            try
            {
                foreach (City city in pResponder.getCities())
                {
                    if (scanned++ >= 32) break;
                    if (city?.data == null || city.isRekt()) continue;
                    if (!WarTerritoryService.CanFabricateAgainst(pRequester,
                            pResponder, city, out _)) continue;
                    _forgeCityIds.Add(city.data.id);
                }
            }
            catch { }
            if (!_forgeCityIds.Contains(_selectedForgeCityId))
                _selectedForgeCityId = _forgeCityIds.Count > 0
                    ? _forgeCityIds[0]
                    : -1L;
        }

        private string SelectionSummary()
        {
            if (_selectedOperationType != DiplomaticOperationType.None)
            {
                if (_selectedOperationType ==
                        DiplomaticOperationType.SpyNetwork &&
                    IsAnnexationTarget(FindKingdom(_baseKingdomId),
                        FindKingdom(_selectedKingdomId)))
                    return AW_L10n.Text(
                        "aw_diplomacy_action_plan_annexation",
                        "Plan annexation");
                return OperationTypeName(_selectedOperationType);
            }
            return DiplomacyConversationService.ProposalTypeName(
                _selectedProposalType);
        }

        private static bool IsAnnexationTarget(Kingdom pRequester,
            Kingdom pResponder)
        {
            bool directSuzerain = pRequester?.data != null &&
                                  pResponder?.data != null &&
                                  VassalService.GetSuzerain(pResponder) ==
                                  pRequester;
            bool hasActiveSpyNetwork = DiplomaticOperationService.
                HasActiveSpyNetwork(pRequester, pResponder, out _, out _);
            return DiplomacyActionExpansionRules.
                ShouldUseAnnexationOperation(directSuzerain,
                    hasActiveSpyNetwork);
        }

        private static string AnnexationDetail(bool pAvailable,
            string pReason)
        {
            return pAvailable
                ? AW_L10n.Text("aw_diplomacy_action_plan_annexation_desc",
                    "Begin the targeted decision to absorb this direct vassal.")
                : ProposalFailure(pReason);
        }

        private static string OperationTypeName(
            DiplomaticOperationType pType)
        {
            return pType == DiplomaticOperationType.SpyNetwork
                ? AW_L10n.Text("aw_diplomacy_action_spy_network",
                    "Establish spy network")
                : AW_L10n.Text("aw_diplomacy_action_forge_documents",
                    "Purchase war claim");
        }

        private static string CovertPreviewDetail(
            DiplomaticOperationPreview pPreview)
        {
            if (pPreview == null)
                return AW_L10n.Text("aw_diplomacy_select_action_detail",
                    "Select the action to inspect its details");
            if (!pPreview.Available)
            {
                if (pPreview.Reason == "spy_network_active")
                    return SpyPointDetail(pPreview);
                if (pPreview.SpyPointsPerYear > 0)
                    return SpyPointDetail(pPreview) + "\n" +
                           ProposalFailure(pPreview.Reason);
                return ProposalFailure(pPreview.Reason);
            }
            if (pPreview.Type == DiplomaticOperationType.ForgeDocuments)
                return SpyPointDetail(pPreview);
            string detail = string.Format(AW_L10n.Text(
                    "aw_diplomacy_covert_preview",
                    "Duration {0} years | Success {1}% | Discovery {2}%"),
                pPreview.DurationYears, pPreview.SuccessChance,
                pPreview.DiscoveryChance) + "\n" +
                AW_L10n.Text("aw_diplomacy_discovery_chance",
                    "Discovery risk");
            if (pPreview.NetworkStrength > 0)
                detail += "\n" + string.Format(AW_L10n.Text(
                        "aw_diplomacy_network_status",
                        "Network strength {0} | {1} years remaining"),
                    pPreview.NetworkStrength,
                    Math.Max(0, pPreview.NetworkUntilYear -
                        Date.getCurrentYear()));
            return detail;
        }

        private static string SpyPointDetail(
            DiplomaticOperationPreview pPreview)
        {
            string balance = string.Format(AW_L10n.Text(
                    "aw_diplomacy_spy_points_status",
                    "Spy points {0}/{1} | +{2}/year"),
                pPreview.SpyPoints, SpyNetworkPointRules.MaximumPoints,
                pPreview.SpyPointsPerYear > 0
                    ? pPreview.SpyPointsPerYear
                    : SpyNetworkPointRules.PointsPerYear);
            string costs = string.Format(AW_L10n.Text(
                    "aw_diplomacy_spy_claim_costs",
                    "Weak claim {0} | Strong claim {1}"),
                SpyNetworkPointRules.WeakClaimCost,
                SpyNetworkPointRules.StrongClaimCost);
            if (pPreview.PointCost <= 0) return balance + "\n" + costs;
            return balance + "\n" + costs + "\n" +
                   string.Format(AW_L10n.Text(
                           "aw_diplomacy_spy_selected_cost",
                           "Selected cost: {0}"), pPreview.PointCost);
        }

        private void AddActionHeader(string pKey, string pFallback)
        {
            Text header = CreateText(_actionContent, pKey, 8,
                TextAnchor.MiddleLeft);
            header.text = AW_L10n.Text(pKey, pFallback);
            header.color = new Color(.76f, .62f, .34f, 1f);
            LayoutElement layout = header.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 18f;
            layout.preferredHeight = 18f;
        }

        private void AddAction(DiplomacyProposalType pType,
            bool pDeclareWar, string pIconPath)
        {
            var obj = new GameObject("Action_" + pType,
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement), typeof(TipButton));
            obj.transform.SetParent(_actionContent, false);
            LayoutElement layout = obj.GetComponent<LayoutElement>();
            layout.minHeight = 27f;
            layout.preferredHeight = 27f;
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            var iconObject = new GameObject("Icon", typeof(RectTransform),
                typeof(Image));
            iconObject.transform.SetParent(obj.transform, false);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(pIconPath) ??
                          SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Layout(icon.rectTransform, 5f, 6f, 15f, 15f);
            Text text = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(25f, 1f);
            text.rectTransform.offsetMax = new Vector2(-24f, -1f);
            Text stateText = CreateText(obj.transform, "Assessment", 10,
                TextAnchor.MiddleCenter);
            Layout(stateText.rectTransform, 0f, 3f, 20f, 21f);
            stateText.rectTransform.anchorMin = new Vector2(1f, 1f);
            stateText.rectTransform.anchorMax = new Vector2(1f, 1f);
            stateText.rectTransform.pivot = new Vector2(1f, 1f);
            stateText.rectTransform.anchoredPosition = new Vector2(-3f, -3f);
            Button button = obj.GetComponent<Button>();
            if (pDeclareWar) button.onClick.AddListener(OpenWarActions);
            else button.onClick.AddListener(() => SelectProposal(pType));
            TipButton tip = obj.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            _actionRows.Add(new ActionRow
            {
                Type = pType,
                DeclareWar = pDeclareWar,
                Button = button,
                Text = text,
                StateText = stateText,
                Tip = tip
            });
        }

        private void AddWarNegotiationAction(string pIconPath)
        {
            AddAction(DiplomacyProposalType.None, false, pIconPath);
            ActionRow row = _actionRows[_actionRows.Count - 1];
            row.WarNegotiation = true;
            row.Button.gameObject.name = "Action_WarNegotiation";
            row.Button.onClick.RemoveAllListeners();
            row.Button.onClick.AddListener(OpenWarNegotiation);
            row.Text.rectTransform.offsetMax = new Vector2(-40f, -1f);
            RectTransform state = row.StateText.rectTransform;
            state.sizeDelta = new Vector2(36f, 21f);
            state.anchoredPosition = new Vector2(-3f, -3f);
            row.StateText.fontSize = 9;
        }

        private void AddOperation(DiplomaticOperationType pType,
            string pIconPath)
        {
            var obj = new GameObject("Operation_" + pType,
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement), typeof(TipButton));
            obj.transform.SetParent(_actionContent, false);
            LayoutElement layout = obj.GetComponent<LayoutElement>();
            layout.minHeight = 27f;
            layout.preferredHeight = 27f;
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            var iconObject = new GameObject("Icon", typeof(RectTransform),
                typeof(Image));
            iconObject.transform.SetParent(obj.transform, false);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(pIconPath) ??
                          SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Layout(icon.rectTransform, 5f, 6f, 15f, 15f);
            Text text = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(25f, 1f);
            text.rectTransform.offsetMax = new Vector2(-24f, -1f);
            Text stateText = CreateText(obj.transform, "Assessment", 10,
                TextAnchor.MiddleCenter);
            stateText.rectTransform.anchorMin = new Vector2(1f, 1f);
            stateText.rectTransform.anchorMax = new Vector2(1f, 1f);
            stateText.rectTransform.pivot = new Vector2(1f, 1f);
            stateText.rectTransform.anchoredPosition = new Vector2(-3f, -3f);
            stateText.rectTransform.sizeDelta = new Vector2(20f, 21f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => SelectOperation(pType));
            TipButton tip = obj.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            _actionRows.Add(new ActionRow
            {
                Type = DiplomacyProposalType.None,
                OperationType = pType,
                Button = button,
                Text = text,
                StateText = stateText,
                Tip = tip
            });
        }

        private static Button CreateCommandButton(Transform pParent,
            string pName, UnityEngine.Events.UnityAction pAction,
            out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            pText = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleCenter);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(3f, 1f);
            pText.rectTransform.offsetMax = new Vector2(-3f, -1f);
            obj.GetComponent<Button>().onClick.AddListener(pAction);
            return obj.GetComponent<Button>();
        }

        private static void CreateScrollArea(Transform pParent, string pName,
            bool pUseVerticalLayout, out RectTransform pRoot,
            out RectTransform pContent, out ScrollRect pScroll)
        {
            var root = new GameObject(pName, typeof(RectTransform),
                typeof(ScrollRect));
            root.transform.SetParent(pParent, false);
            pRoot = root.GetComponent<RectTransform>();
            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(Mask));
            viewport.transform.SetParent(root.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            Image image = viewport.GetComponent<Image>();
            image.color = new Color(.055f, .052f, .045f, .50f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            pContent = content.GetComponent<RectTransform>();
            pContent.anchorMin = new Vector2(0f, 1f);
            pContent.anchorMax = new Vector2(0f, 1f);
            pContent.pivot = new Vector2(0f, 1f);
            pContent.anchoredPosition = Vector2.zero;
            if (pUseVerticalLayout)
            {
                VerticalLayoutGroup layout = content.AddComponent<
                    VerticalLayoutGroup>();
                layout.spacing = 3f;
                layout.padding = new RectOffset(3, 3, 3, 3);
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                ContentSizeFitter fitter = content.AddComponent<
                    ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            pScroll = root.GetComponent<ScrollRect>();
            pScroll.viewport = viewportRect;
            pScroll.content = pContent;
            pScroll.horizontal = false;
            pScroll.vertical = true;
            pScroll.movementType = ScrollRect.MovementType.Clamped;
            pScroll.scrollSensitivity = 22f;
        }

        private static Scrollbar CreateVerticalScrollbar(
            RectTransform pRoot, ScrollRect pScroll)
        {
            var track = new GameObject("Scrollbar Vertical",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            track.transform.SetParent(pRoot, false);
            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(1f, 0f);
            trackRect.anchorMax = new Vector2(1f, 1f);
            trackRect.pivot = new Vector2(1f, .5f);
            trackRect.anchoredPosition = Vector2.zero;
            trackRect.sizeDelta = new Vector2(6f, 0f);
            Image trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(.08f, .075f, .06f, .92f);

            var sliding = new GameObject("Sliding Area",
                typeof(RectTransform));
            sliding.transform.SetParent(track.transform, false);
            RectTransform slidingRect = sliding.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(1f, 1f);
            slidingRect.offsetMax = new Vector2(-1f, -1f);
            var handle = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handle.transform.SetParent(sliding.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            handle.GetComponent<Image>().color =
                new Color(.72f, .57f, .28f, .95f);
            Scrollbar scrollbar = track.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            if (pScroll.viewport != null)
                pScroll.viewport.offsetMax = new Vector2(-8f,
                    pScroll.viewport.offsetMax.y);
            return scrollbar;
        }

        private static List<Kingdom> BuildOtherKingdoms(Kingdom pBase,
            out Dictionary<long, long> pCapitalDistances)
        {
            var result = new List<Kingdom>();
            pCapitalDistances = new Dictionary<long, long>();
            var relationPriorities =
                new Dictionary<long, DiplomacyPrimaryRelation>();
            if (World.world?.kingdoms == null) return result;
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && !kingdom.isRekt() &&
                    !kingdom.isNeutral() && kingdom != pBase)
                {
                    result.Add(kingdom);
                    pCapitalDistances[kingdom.id] =
                        CapitalDistanceSquared(pBase, kingdom);
                    bool atWar = false;
                    bool allied = false;
                    try
                    {
                        atWar = pBase.isEnemy(kingdom);
                        allied = Alliance.isSame(pBase.getAlliance(),
                            kingdom.getAlliance());
                    }
                    catch { }
                    relationPriorities[kingdom.id] =
                        DiplomacyConversationRules.ResolvePrimaryRelation(
                            atWar,
                            baseIsVassal: VassalService.GetSuzerain(pBase) ==
                                kingdom,
                            otherIsVassal: VassalService.GetSuzerain(kingdom) ==
                                pBase,
                            baseIsTributary:
                                VassalService.GetTributarySuzerain(pBase) ==
                                kingdom,
                            otherIsTributary:
                                VassalService.GetTributarySuzerain(kingdom) ==
                                pBase,
                            allied: allied);
                }
            Dictionary<long, long> distances = pCapitalDistances;
            Dictionary<long, DiplomacyPrimaryRelation> priorities =
                relationPriorities;
            result.Sort((pLeft, pRight) =>
            {
                int relation =
                    DiplomacyConversationRules.CompareRelationPriority(
                        priorities[pLeft.id], priorities[pRight.id]);
                return relation != 0
                    ? relation
                    : DiplomacyConversationRules.CompareCapitalDistance(
                        distances[pLeft.id], pLeft.id,
                        distances[pRight.id], pRight.id);
            });
            return result;
        }

        private static string BuildRelationDetail(Kingdom pBase,
            Kingdom pOther, long pCapitalDistanceSquared,
            out int pOpinion)
        {
            string relation = RelationLabel(pBase, pOther);
            pOpinion = DiplomacyOpinionService.Read(pBase, pOther);
            return relation + "  " +
                   AW_L10n.Text("aw_diplomacy_opinion", "Opinion") +
                   " " + (pOpinion > 0 ? "+" : "") + pOpinion +
                   "  " + DistanceLabel(pCapitalDistanceSquared);
        }

        private static long CapitalDistanceSquared(Kingdom pBase,
            Kingdom pOther)
        {
            try
            {
                WorldTile first = pBase?.capital?.getTile();
                WorldTile second = pOther?.capital?.getTile();
                if (first == null || second == null) return long.MaxValue;
                return DiplomacyConversationRules.CapitalDistanceSquared(
                    first.x, first.y, second.x, second.y);
            }
            catch
            {
                return long.MaxValue;
            }
        }

        private static string DistanceLabel(long pDistanceSquared)
        {
            int distance = DiplomacyConversationRules.DisplayCapitalDistance(
                pDistanceSquared);
            return distance < 0
                ? AW_L10n.Text("aw_diplomacy_distance_unknown",
                    "Distance unknown")
                : string.Format(AW_L10n.Text("aw_diplomacy_distance",
                        "Distance {0}"), distance);
        }

        private static string RelationLabel(Kingdom pBase, Kingdom pOther)
        {
            bool atWar = false;
            bool allied = false;
            try
            {
                atWar = pBase.isEnemy(pOther);
                allied = Alliance.isSame(pBase.getAlliance(),
                    pOther.getAlliance());
            }
            catch { }
            DiplomacyPrimaryRelation primary =
                DiplomacyConversationRules.ResolvePrimaryRelation(atWar,
                    baseIsVassal: VassalService.GetSuzerain(pBase) == pOther,
                    otherIsVassal: VassalService.GetSuzerain(pOther) == pBase,
                    baseIsTributary:
                    VassalService.GetTributarySuzerain(pBase) == pOther,
                    otherIsTributary:
                    VassalService.GetTributarySuzerain(pOther) == pBase,
                    allied: allied);
            switch (primary)
            {
                case DiplomacyPrimaryRelation.War:
                    return AW_L10n.Text("aw_diplomacy_relation_war", "War");
                case DiplomacyPrimaryRelation.OurSuzerain:
                    return AW_L10n.Text(
                        "aw_diplomacy_relation_our_suzerain", "Our suzerain");
                case DiplomacyPrimaryRelation.OurVassal:
                    return AW_L10n.Text(
                        "aw_diplomacy_relation_our_vassal", "Our vassal");
                case DiplomacyPrimaryRelation.OurTributarySuzerain:
                    return AW_L10n.Text(
                        "aw_diplomacy_relation_our_tributary_suzerain",
                        "Tributary suzerain");
                case DiplomacyPrimaryRelation.OurTributary:
                    return AW_L10n.Text(
                        "aw_diplomacy_relation_our_tributary",
                        "Our tributary");
                case DiplomacyPrimaryRelation.Alliance:
                    return AW_L10n.Text(
                        "aw_diplomacy_relation_alliance", "Alliance");
            }
            if (DiplomacyProposalService.TryGetActiveNonAggression(
                    pBase, pOther, out int untilYear))
            {
                int remaining = DiplomacyProposalRules.TreatyYearsRemaining(
                    Date.getCurrentYear(), untilYear);
                if (remaining == 0)
                    return string.Format(AW_L10n.Text(
                        "aw_diplomacy_relation_non_aggression_expiring",
                        "Non-aggression pact expires this year ({0})"),
                        untilYear);
                return string.Format(AW_L10n.Text(
                        "aw_diplomacy_relation_non_aggression",
                        "Non-aggression pact: {0} years remaining (until {1})"),
                    remaining, untilYear);
            }
            if (DiplomacyProposalService.TryGetActiveTruce(
                    pBase, pOther, out int truceUntilYear))
            {
                int remaining = DiplomacyProposalRules.TreatyYearsRemaining(
                    Date.getCurrentYear(), truceUntilYear);
                if (remaining == 0)
                    return string.Format(AW_L10n.Text(
                        "aw_diplomacy_relation_truce_expiring",
                        "Truce expires this year ({0})"), truceUntilYear);
                return string.Format(AW_L10n.Text(
                        "aw_diplomacy_relation_truce",
                        "Truce: {0} years remaining (until {1})"),
                    remaining, truceUntilYear);
            }
            return AW_L10n.Text("aw_diplomacy_relation_normal", "Normal");
        }

        private void HideBubbles()
        {
            for (int i = 0; i < _bubblePool.Count; i++)
                _bubblePool[i].Unbind();
            for (int i = 0; i < _multiplayerBubblePool.Count; i++)
                _multiplayerBubblePool[i].Unbind();
        }

        private void SetWindowTitle()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText == null) return;
            window.titleText.text = AW_L10n.Text("aw_diplomacy_window_title",
                "Diplomacy");
            window.titleText.raycastTarget = false;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            string hex = HistoryColors.FromKingdom(pKingdom);
            return !string.IsNullOrEmpty(hex) &&
                   ColorUtility.TryParseHtmlString(hex, out Color color)
                ? new Color(color.r, color.g, color.b, 1f)
                : Color.white;
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

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
