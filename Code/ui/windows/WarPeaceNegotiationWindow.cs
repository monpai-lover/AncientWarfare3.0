using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class WarPeaceNegotiationWindow :
        AbstractWindow<WarPeaceNegotiationWindow>
    {
        public new const string WindowId = "aw_war_peace_negotiation";

        private static readonly Vector2 DefaultSize =
            new Vector2(580f, 360f);
        private static readonly Vector2 MinimumSize =
            new Vector2(520f, 340f);
        private static readonly Vector2 MaximumSize =
            new Vector2(900f, 650f);
        private const float LiveRefreshInterval = .75f;
        private static WarPeaceNegotiationPresentation _pendingPresentation;

        public static event Action<WarPeaceNegotiationPresentation,
            IReadOnlyList<string>> SubmitRequested;

        private readonly HashSet<string> _selectedTermIds =
            new HashSet<string>(StringComparer.Ordinal);
        private Vector2 _windowSize = DefaultSize;
        private WarPeaceNegotiationPresentation _presentation;
        private RectTransform _root;
        private RectTransform _header;
        private RectTransform _scopePanel;
        private Text _scopeTitle;
        private Text _scopeDetail;
        private PartyPanel _requesterPanel;
        private PartyPanel _responderPanel;
        private TermColumn _demandsColumn;
        private TermColumn _concessionsColumn;
        private RectTransform _summaryPanel;
        private Text _summaryTitle;
        private Text _budgetCapacity;
        private Text _budgetSpent;
        private Text _budgetRemaining;
        private Text _netDemand;
        private Text _bilateralExhaustion;
        private Text _acceptance;
        private Text _acceptanceMargin;
        private Text _acceptanceFactors;
        private Text _status;
        private Button _backButton;
        private Button _submitButton;
        private Text _backText;
        private Text _submitText;
        private WideWindowChrome _chrome;
        private bool _binding;
        private bool _resetTermScrollAfterLayout;
        private float _nextLiveRefreshTime;
        private string _submitFailure = string.Empty;

        private sealed class PartyPanel
        {
            public RectTransform Root;
            public Image FlagBackground;
            public Image FlagIcon;
            public TipButton FlagTip;
            public GameObject PortraitRoot;
            public Image PortraitFallback;
            public UiUnitAvatarElement Avatar;
            public Text KingdomName;
            public Text RulerName;
            public Text ArmyStrength;
            public Text Casualties;
            public Text Score;
            public Text ScoreDetail;
        }

        private sealed class TermRow
        {
            public GameObject Root;
            public Image Background;
            public Toggle Toggle;
            public Image Checkmark;
            public Text Title;
            public Text Cost;
            public Text Description;
            public Text DisabledReason;
            public TipButton Tip;
            public WarPeaceTermPresentation Term;
        }

        private sealed class TermSection
        {
            public RectTransform Root;
            public Text Header;
            public readonly List<TermRow> Rows = new List<TermRow>();
        }

        private sealed class TermColumn
        {
            public RectTransform Root;
            public Text Title;
            public RectTransform Viewport;
            public RectTransform Content;
            public readonly Dictionary<WarPeaceTermCategory, TermSection>
                Sections = new Dictionary<WarPeaceTermCategory,
                    TermSection>();
        }

        public static void Open(WarPeaceNegotiationPresentation pPresentation)
        {
            if (pPresentation == null) return;
            _pendingPresentation = pPresentation;
            if (Instance == null) CreateAndInit(WindowId);
            if (Instance != null) Instance.BindPresentation(pPresentation);
            AW_LineageWindowIds.SafeShow(WindowId,
                delegate { Instance?.Refresh(); });
        }

        internal static void ShowSubmitFailure(string pMessage)
        {
            if (Instance == null) return;
            Instance._submitFailure = pMessage ?? string.Empty;
            Instance.Refresh();
        }

        internal static void ClearSubmitFailure()
        {
            if (Instance == null) return;
            Instance._submitFailure = string.Empty;
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                delegate { return _windowSize; },
                delegate(Vector2 size)
                {
                    _windowSize = size;
                    ApplyLayout();
                    Refresh();
                }, DefaultSize, MinimumSize, MaximumSize);
            if (_pendingPresentation != null)
                BindPresentation(_pendingPresentation);
        }

        public override void OnNormalEnable()
        {
            if (_pendingPresentation != null &&
                !ReferenceEquals(_presentation, _pendingPresentation))
                BindPresentation(_pendingPresentation);
            Refresh();
        }

        private void Update()
        {
            if (!isActiveAndEnabled ||
                Time.unscaledTime < _nextLiveRefreshTime) return;
            _nextLiveRefreshTime = Time.unscaledTime + LiveRefreshInterval;
            RefreshLiveNegotiation();
        }

        private void RefreshLiveNegotiation()
        {
            if (!WarPeaceNegotiationController.TryRefreshLivePresentation(
                    _presentation, out WarPeaceNegotiationPresentation next))
                return;
            _presentation = next;
            _pendingPresentation = next;
            var validIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < next.Terms.Count; i++)
                if (next.Terms[i] != null) validIds.Add(next.Terms[i].Id);
            _selectedTermIds.RemoveWhere(pId => !validIds.Contains(pId));
            Refresh();
        }

        private void BindPresentation(
            WarPeaceNegotiationPresentation pPresentation)
        {
            _presentation = pPresentation;
            _submitFailure = string.Empty;
            _selectedTermIds.Clear();
            for (int i = 0; i < pPresentation.Terms.Count; i++)
            {
                WarPeaceTermPresentation term = pPresentation.Terms[i];
                if (term?.InitiallySelected == true)
                    SelectInitialTerm(term);
            }
            ResetTermScrollPositions();
            if (isActiveAndEnabled) Refresh();
        }

        private void SelectInitialTerm(WarPeaceTermPresentation pTerm)
        {
            if (pTerm.Kind == WarPeaceTermKind.WhitePeace)
                _selectedTermIds.Clear();
            else
                RemoveWhitePeace();
            _selectedTermIds.Add(pTerm.Id);
        }

        public void Refresh()
        {
            EnsureUi();
            SetWindowTitle();
            ApplyLayout();
            if (_presentation == null)
            {
                BindUnavailable();
                return;
            }

            _binding = true;
            BindParty(_requesterPanel, _presentation.Requester,
                _presentation.RequesterScore);
            BindParty(_responderPanel, _presentation.Responder,
                _presentation.ResponderScore);
            BindScope();

            WarPeaceNegotiationSelectionSummary summary =
                WarPeaceNegotiationSelectionRules.Summarize(_presentation,
                    _selectedTermIds);
            BindTerms(summary);
            BindSummary(summary);
            _binding = false;
            Canvas.ForceUpdateCanvases();
            ApplyPendingTermScrollReset();
        }

        private void ResetTermScrollPositions()
        {
            _resetTermScrollAfterLayout = true;
        }

        private void ApplyPendingTermScrollReset()
        {
            if (!_resetTermScrollAfterLayout) return;
            _resetTermScrollAfterLayout = false;
            ResetTermScrollPosition(_demandsColumn);
            ResetTermScrollPosition(_concessionsColumn);
        }

        private static void ResetTermScrollPosition(TermColumn pColumn)
        {
            ScrollRect scroll = pColumn?.Viewport?.GetComponent<ScrollRect>();
            if (scroll == null) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f;
            if (pColumn.Content != null)
                pColumn.Content.anchoredPosition = new Vector2(
                    pColumn.Content.anchoredPosition.x, 0f);
        }

        private void BindUnavailable()
        {
            _binding = true;
            HideParty(_requesterPanel);
            HideParty(_responderPanel);
            _scopeTitle.text = string.Empty;
            _scopeDetail.text = string.Empty;
            HideTermColumn(_demandsColumn);
            HideTermColumn(_concessionsColumn);
            _budgetCapacity.text = string.Empty;
            _budgetSpent.text = string.Empty;
            _budgetRemaining.text = string.Empty;
            _netDemand.text = string.Empty;
            _bilateralExhaustion.text = string.Empty;
            _acceptance.text = string.Empty;
            _acceptanceMargin.text = string.Empty;
            _acceptanceFactors.text = string.Empty;
            _status.text = AW_L10n.Text("aw_war_peace_unavailable",
                "Negotiation data is unavailable");
            _submitButton.interactable = false;
            _binding = false;
        }

        private void BindTerms(WarPeaceNegotiationSelectionSummary pSummary)
        {
            _demandsColumn.Title.text = AW_L10n.Text(
                "aw_war_peace_our_demands", "Our demands");
            _concessionsColumn.Title.text = AW_L10n.Text(
                "aw_war_peace_our_concessions", "Our concessions");
            BindTermColumn(_demandsColumn, WarPeaceOfferSide.Demand,
                pSummary);
            BindTermColumn(_concessionsColumn,
                WarPeaceOfferSide.Concession, pSummary);
        }

        private void BindTermColumn(TermColumn pColumn,
            WarPeaceOfferSide pSide,
            WarPeaceNegotiationSelectionSummary pSummary)
        {
            foreach (KeyValuePair<WarPeaceTermCategory, TermSection> pair in
                     pColumn.Sections)
            {
                TermSection section = pair.Value;
                int used = 0;
                for (int i = 0; i < _presentation.Terms.Count; i++)
                {
                    WarPeaceTermPresentation term = _presentation.Terms[i];
                    if (term == null ||
                        WarPeaceTermPresentationRules.ResolveSide(
                            term.RecipientValue) != pSide ||
                        WarPeaceTermPresentationRules.ResolveCategory(
                            term.Kind) != pair.Key) continue;
                    while (section.Rows.Count <= used)
                        section.Rows.Add(CreateTermRow(section.Root));
                    BindTermRow(section.Rows[used++], term, pSummary);
                }
                for (int i = used; i < section.Rows.Count; i++)
                    section.Rows[i].Root.SetActive(false);
                section.Root.gameObject.SetActive(used > 0);
            }
        }

        private void BindTermRow(TermRow pRow,
            WarPeaceTermPresentation pTerm,
            WarPeaceNegotiationSelectionSummary pSummary)
        {
            pRow.Term = pTerm;
            bool selected = _selectedTermIds.Contains(pTerm.Id);
            int cost = WarPeaceTermsRules.NormalizeTermCost(pTerm.Kind,
                pTerm.RequestedCost);
            bool concession = pTerm.RecipientValue > 0;
            int remaining = concession
                ? pSummary.ConcessionRemaining
                : pSummary.DemandRemaining;
            int availableBudget = selected
                ? remaining + cost
                : remaining;
            WarPeaceTermAvailability availability =
                WarPeaceTermAvailabilityRules.Resolve(pTerm.Kind,
                    availableBudget,
                    pTerm.RequestedCost, pTerm.PrerequisiteFailure);
            pRow.Toggle.isOn = selected;
            pRow.Toggle.interactable = selected || availability.Enabled;
            pRow.Title.text = AW_L10n.Text(pTerm.TitleKey,
                pTerm.TitleFallback);
            pRow.Cost.text = string.Format(AW_L10n.Text(
                "aw_war_peace_term_cost", "Cost {0}"), cost);
            pRow.Description.text = AW_L10n.Text(pTerm.DescriptionKey,
                pTerm.DescriptionFallback);
            if (!string.IsNullOrWhiteSpace(pTerm.Detail))
                pRow.Description.text += "\n" + pTerm.Detail;
            pRow.DisabledReason.text = availability.Enabled || selected
                ? string.Empty
                : DisabledTermReason(pTerm, availability,
                    remaining);
            pRow.DisabledReason.gameObject.SetActive(
                !string.IsNullOrEmpty(pRow.DisabledReason.text));
            pRow.Background.color = selected
                ? new Color(.34f, .27f, .14f, .98f)
                : availability.Enabled
                    ? new Color(.13f, .12f, .10f, .96f)
                    : new Color(.10f, .095f, .085f, .72f);
            pRow.Title.color = availability.Enabled || selected
                ? Color.white
                : new Color(.64f, .62f, .57f, 1f);
            pRow.Checkmark.color = new Color(.96f, .78f, .34f, 1f);
            string tipTitle = pRow.Title.text;
            string tipDescription = pRow.Description.text;
            if (!string.IsNullOrEmpty(pRow.DisabledReason.text))
                tipDescription += "\n" + pRow.DisabledReason.text;
            pRow.Tip.hoverAction = delegate
            {
                Tooltip.show(pRow.Root, AW_RawTooltip.TYPE,
                    new TooltipData
                    {
                        tip_name = tipTitle,
                        tip_description = tipDescription
                    });
            };
            pRow.Root.SetActive(true);
        }

        private static void HideTermColumn(TermColumn pColumn)
        {
            if (pColumn == null) return;
            foreach (TermSection section in pColumn.Sections.Values)
                for (int i = 0; i < section.Rows.Count; i++)
                    section.Rows[i].Root.SetActive(false);
        }

        private void BindSummary(
            WarPeaceNegotiationSelectionSummary pSummary)
        {
            _summaryTitle.text = AW_L10n.Text("aw_war_peace_summary",
                "Offer summary");
            _budgetCapacity.text = string.Format(AW_L10n.Text(
                    "aw_war_peace_score_total", "War score: {0}"),
                Signed(pSummary.WarScore));
            _budgetSpent.text = string.Format(AW_L10n.Text(
                    "aw_war_peace_demand_gross", "Demands: {0}/100"),
                pSummary.DemandGross);
            _budgetRemaining.text = string.Format(AW_L10n.Text(
                    "aw_war_peace_concession_gross",
                    "Concessions: {0}/100"),
                pSummary.ConcessionGross);
            _netDemand.text = string.Format(AW_L10n.Text(
                    "aw_war_peace_net_demand", "Net demand: {0}"),
                Signed(pSummary.NetDemand));
            _netDemand.color = ScoreColor(pSummary.NetDemand);
            _bilateralExhaustion.text = string.Format(AW_L10n.Text(
                    "aw_war_peace_bilateral_exhaustion",
                    "Exhaustion: {0} {1}/100 | {2} {3}/100"),
                _presentation.Requester.KingdomName,
                _presentation.RequesterExhaustion,
                _presentation.Responder.KingdomName,
                _presentation.ResponderExhaustion);
            bool accepts = pSummary.Acceptance.Accept;
            _acceptance.text = AW_L10n.Text(accepts
                    ? "aw_war_peace_expected_accept"
                    : "aw_war_peace_expected_reject",
                accepts ? "Expected to accept" : "Expected to reject");
            if (pSummary.Acceptance.Forced)
                _acceptance.text += "  " + AW_L10n.Text(
                    "aw_war_peace_forced", "Forced by total defeat");
            _acceptance.color = accepts
                ? new Color(.44f, .88f, .48f, 1f)
                : new Color(.94f, .44f, .38f, 1f);
            _acceptanceMargin.text = string.Format(AW_L10n.Text(
                    "aw_war_peace_acceptance_margin",
                    "Acceptance margin: {0}"),
                Signed(pSummary.Acceptance.Margin));
            WarPeaceAcceptanceContext context = _presentation.Acceptance;
            _acceptanceFactors.text =
                AW_L10n.Text("aw_war_peace_recipient_score",
                    "Recipient war score") + ": " +
                Signed(_presentation.ResponderScore.Total) + "  |  " +
                AW_L10n.Text("aw_war_peace_recipient_term_value",
                    "Value of selected terms") + ": " +
                Signed(pSummary.NetTermValueForRecipient) + "\n" +
                AW_L10n.Text("aw_war_peace_recipient_resolve",
                    "Resolve") + ": " + context.RecipientResolve +
                "  |  " +
                AW_L10n.Text("aw_war_peace_recipient_exhaustion",
                    "War exhaustion") + ": " +
                context.RecipientWarExhaustion + "\n" +
                AW_L10n.Text("aw_war_peace_recipient_pressure",
                    "Military pressure") + ": " +
                context.RecipientMilitaryPressure;

            _submitButton.interactable = pSummary.SubmitEnabled;
            _submitText.text = AW_L10n.Text("aw_war_peace_submit",
                "Send offer");
            _backText.text = AW_L10n.Text("aw_war_peace_back_diplomacy",
                "Back to diplomacy");
            if (!string.IsNullOrEmpty(_submitFailure))
            {
                _status.text = _submitFailure;
                _status.color = new Color(.96f, .34f, .30f, 1f);
            }
            else
            {
                _status.text = pSummary.SubmitEnabled
                    ? AW_L10n.Text("aw_war_peace_offer_ready",
                        "Offer is ready to send")
                    : SubmitDisabledReason(pSummary.SubmitDisabledReason);
                _status.color = pSummary.SubmitEnabled
                    ? new Color(.78f, .74f, .64f, 1f)
                    : new Color(.96f, .58f, .44f, 1f);
            }
        }

        private void OnTermChanged(string pTermId, bool pSelected)
        {
            if (_binding || _presentation == null) return;
            _submitFailure = string.Empty;
            WarPeaceTermPresentation term = FindTerm(pTermId);
            if (term == null) return;
            if (!pSelected)
                _selectedTermIds.Remove(pTermId);
            else
            {
                if (term.Kind == WarPeaceTermKind.WhitePeace)
                    _selectedTermIds.Clear();
                else
                {
                    RemoveWhitePeace();
                    RemoveOtherSubjectTerms(term.Id);
                    RemoveOtherCityRecipientTerms(term.Id);
                }
                _selectedTermIds.Add(pTermId);
            }
            Refresh();
        }

        private void RemoveWhitePeace()
        {
            if (_presentation == null) return;
            for (int i = 0; i < _presentation.Terms.Count; i++)
            {
                WarPeaceTermPresentation term = _presentation.Terms[i];
                if (term?.Kind == WarPeaceTermKind.WhitePeace)
                    _selectedTermIds.Remove(term.Id);
            }
        }

        private void RemoveOtherSubjectTerms(string pKeepTermId)
        {
            WarPeaceTermPresentation selected = FindTerm(pKeepTermId);
            if (selected == null ||
                selected.Kind != WarPeaceTermKind.ForceVassal &&
                selected.Kind != WarPeaceTermKind.ForceTributary) return;
            for (int i = 0; i < _presentation.Terms.Count; i++)
            {
                WarPeaceTermPresentation term = _presentation.Terms[i];
                if (term != null && term.Id != pKeepTermId &&
                    (term.Kind == WarPeaceTermKind.ForceVassal ||
                     term.Kind == WarPeaceTermKind.ForceTributary))
                    _selectedTermIds.Remove(term.Id);
            }
        }

        private void RemoveOtherCityRecipientTerms(string pKeepTermId)
        {
            WarPeaceTermPresentation selected = FindTerm(pKeepTermId);
            if (selected == null) return;
            for (int i = 0; i < _presentation.Terms.Count; i++)
            {
                WarPeaceTermPresentation term = _presentation.Terms[i];
                if (WarPeaceRecipientChoiceRules.Conflicts(selected, term))
                    _selectedTermIds.Remove(term.Id);
            }
        }

        private WarPeaceTermPresentation FindTerm(string pTermId)
        {
            for (int i = 0; i < _presentation.Terms.Count; i++)
            {
                WarPeaceTermPresentation term = _presentation.Terms[i];
                if (term != null && string.Equals(term.Id, pTermId,
                        StringComparison.Ordinal))
                    return term;
            }
            return null;
        }

        private void Submit()
        {
            if (_presentation == null) return;
            WarPeaceNegotiationSelectionSummary summary =
                WarPeaceNegotiationSelectionRules.Summarize(_presentation,
                    _selectedTermIds);
            if (!summary.SubmitEnabled)
            {
                _status.text = SubmitDisabledReason(
                    summary.SubmitDisabledReason);
                return;
            }
            var selected = new List<string>(_selectedTermIds);
            selected.Sort(StringComparer.Ordinal);
            SubmitRequested?.Invoke(_presentation, selected.AsReadOnly());
        }

        private void BackToDiplomacy()
        {
            if (_presentation == null) return;
            DiplomacyConversationWindow.Open(
                _presentation.Requester.KingdomId);
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            _root = new GameObject("WarPeaceNegotiationRoot",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(ContentTransform, false);

            _header = CreatePanel(_root, "Belligerents",
                new Color(.105f, .095f, .075f, .98f));
            _requesterPanel = CreatePartyPanel(_header, "Requester");
            _responderPanel = CreatePartyPanel(_header, "Responder");
            _scopePanel = CreatePanel(_root, "SettlementScope",
                new Color(.12f, .105f, .075f, .98f));
            _scopeTitle = CreateText(_scopePanel, "ScopeTitle", 9,
                TextAnchor.MiddleLeft);
            _scopeTitle.color = new Color(.96f, .79f, .36f, 1f);
            _scopeDetail = CreateText(_scopePanel, "ScopeDetail", 7,
                TextAnchor.MiddleLeft);
            _scopeDetail.color = new Color(.82f, .79f, .71f, 1f);

            _demandsColumn = CreateTermColumn(_root, "DemandsPanel");
            _concessionsColumn = CreateTermColumn(_root,
                "ConcessionsPanel");

            _summaryPanel = CreatePanel(_root, "SummaryPanel",
                new Color(.095f, .085f, .065f, .98f));
            _summaryTitle = CreateText(_summaryPanel, "SummaryTitle", 10,
                TextAnchor.MiddleLeft);
            _budgetCapacity = CreateText(_summaryPanel, "Capacity", 9,
                TextAnchor.MiddleLeft);
            _budgetSpent = CreateText(_summaryPanel, "Spent", 9,
                TextAnchor.MiddleLeft);
            _budgetRemaining = CreateText(_summaryPanel, "Remaining", 10,
                TextAnchor.MiddleLeft);
            _budgetRemaining.color = new Color(.96f, .79f, .36f, 1f);
            _netDemand = CreateText(_summaryPanel, "NetDemand", 9,
                TextAnchor.MiddleLeft);
            _bilateralExhaustion = CreateText(_summaryPanel,
                "BilateralExhaustion", 8, TextAnchor.MiddleLeft);
            _bilateralExhaustion.color = new Color(.88f, .78f, .55f, 1f);
            _acceptance = CreateText(_summaryPanel, "Acceptance", 10,
                TextAnchor.MiddleLeft);
            _acceptanceMargin = CreateText(_summaryPanel,
                "AcceptanceMargin", 9, TextAnchor.MiddleLeft);
            _acceptanceFactors = CreateText(_summaryPanel,
                "AcceptanceFactors", 8, TextAnchor.UpperLeft);
            _acceptanceFactors.color = new Color(.76f, .73f, .66f, 1f);

            _status = CreateText(_root, "DisabledReason", 8,
                TextAnchor.MiddleLeft);
            _backButton = CreateTextButton(_root, "BackToDiplomacy",
                BackToDiplomacy, out _backText);
            _submitButton = CreateTextButton(_root, "SubmitOffer", Submit,
                out _submitText);
            SetWindowTitle();
        }

        private PartyPanel CreatePartyPanel(Transform pParent,
            string pName)
        {
            var panel = new PartyPanel();
            panel.Root = CreatePanel(pParent, pName,
                new Color(.13f, .115f, .085f, .94f));
            panel.FlagBackground = CreateImage(panel.Root, "Flag",
                Color.white);
            panel.FlagBackground.preserveAspect = true;
            panel.FlagTip = panel.FlagBackground.gameObject
                .AddComponent<TipButton>();
            panel.FlagTip.type = AW_RawTooltip.TYPE;
            panel.FlagIcon = CreateImage(panel.FlagBackground.transform,
                "FlagIcon", Color.white);
            Stretch(panel.FlagIcon.rectTransform);
            panel.PortraitRoot = new GameObject("RulerPortrait",
                typeof(RectTransform)).gameObject;
            panel.PortraitRoot.transform.SetParent(panel.Root, false);
            panel.PortraitFallback = CreateImage(
                panel.PortraitRoot.transform, "PortraitFallback",
                new Color(.72f, .70f, .65f, 1f));
            panel.PortraitFallback.sprite = SpriteTextureLoader.getSprite(
                "ui/Icons/iconKings");
            panel.PortraitFallback.preserveAspect = true;
            Stretch(panel.PortraitFallback.rectTransform);
            panel.KingdomName = CreateText(panel.Root, "KingdomName", 11,
                TextAnchor.MiddleLeft);
            panel.RulerName = CreateText(panel.Root, "RulerName", 8,
                TextAnchor.MiddleLeft);
            panel.RulerName.color = new Color(.78f, .74f, .66f, 1f);
            panel.ArmyStrength = CreateText(panel.Root, "ArmyStrength", 7,
                TextAnchor.MiddleLeft);
            panel.ArmyStrength.color = new Color(.84f, .81f, .74f, 1f);
            panel.Casualties = CreateText(panel.Root, "Casualties", 7,
                TextAnchor.MiddleLeft);
            panel.Casualties.color = new Color(.84f, .72f, .64f, 1f);
            panel.Score = CreateText(panel.Root, "WarScore", 18,
                TextAnchor.MiddleRight);
            panel.ScoreDetail = CreateText(panel.Root, "ScoreDetail", 7,
                TextAnchor.UpperLeft);
            panel.ScoreDetail.color = new Color(.80f, .76f, .68f, 1f);
            return panel;
        }

        private TermRow CreateTermRow(Transform pParent)
        {
            var row = new TermRow();
            row.Root = new GameObject("PeaceTerm", typeof(RectTransform),
                typeof(Image), typeof(Toggle), typeof(LayoutElement),
                typeof(TipButton));
            row.Root.transform.SetParent(pParent, false);
            row.Background = row.Root.GetComponent<Image>();
            row.Toggle = row.Root.GetComponent<Toggle>();
            row.Tip = row.Root.GetComponent<TipButton>();
            row.Tip.type = AW_RawTooltip.TYPE;
            LayoutElement layout = row.Root.GetComponent<LayoutElement>();
            layout.minHeight = 72f;
            layout.preferredHeight = 72f;

            Image box = CreateImage(row.Root.transform, "ToggleBox",
                new Color(.22f, .19f, .13f, 1f));
            Layout(box.rectTransform, 8f, 19f, 16f, 16f);
            row.Checkmark = CreateImage(box.transform, "Checkmark",
                new Color(.96f, .78f, .34f, 1f));
            row.Checkmark.sprite = SpriteTextureLoader.getSprite(
                "ui/icons/iconCheckmark") ??
                SpriteTextureLoader.getSprite("ui/icons/iconFavorite");
            row.Checkmark.preserveAspect = true;
            row.Checkmark.rectTransform.anchorMin = new Vector2(.5f, .5f);
            row.Checkmark.rectTransform.anchorMax = new Vector2(.5f, .5f);
            row.Checkmark.rectTransform.pivot = new Vector2(.5f, .5f);
            row.Checkmark.rectTransform.anchoredPosition = Vector2.zero;
            row.Checkmark.rectTransform.sizeDelta = new Vector2(11f, 11f);
            row.Toggle.targetGraphic = box;
            row.Toggle.graphic = row.Checkmark;

            row.Title = CreateText(row.Root.transform, "Title", 9,
                TextAnchor.MiddleLeft);
            LayoutStretch(row.Title.rectTransform, 32f, 3f, 55f, 18f);
            row.Cost = CreateText(row.Root.transform, "Cost", 8,
                TextAnchor.MiddleRight);
            LayoutRight(row.Cost.rectTransform, 4f, 3f, 50f, 18f);
            row.Description = CreateText(row.Root.transform, "Description",
                7, TextAnchor.MiddleLeft);
            LayoutStretch(row.Description.rectTransform, 32f, 21f, 5f, 29f);
            row.Description.color = new Color(.74f, .71f, .65f, 1f);
            row.DisabledReason = CreateText(row.Root.transform,
                "DisabledReason", 7, TextAnchor.MiddleLeft);
            LayoutStretch(row.DisabledReason.rectTransform, 32f, 52f, 5f,
                16f);
            row.DisabledReason.color = new Color(.98f, .58f, .43f, 1f);
            row.Toggle.onValueChanged.AddListener(delegate(bool selected)
            {
                if (row.Term != null)
                    OnTermChanged(row.Term.Id, selected);
            });
            return row;
        }

        private TermColumn CreateTermColumn(Transform pParent,
            string pName)
        {
            var column = new TermColumn();
            column.Root = CreatePanel(pParent, pName,
                new Color(.08f, .075f, .065f, .98f));
            column.Title = CreateText(column.Root, "ColumnTitle", 10,
                TextAnchor.MiddleLeft);
            CreateTermsScrollArea(column.Root, out column.Viewport,
                out column.Content);
            CreateTermSection(column, WarPeaceTermCategory.City,
                "aw_war_peace_category_cities", "Cities");
            CreateTermSection(column, WarPeaceTermCategory.Resource,
                "aw_war_peace_category_resources", "Resources");
            CreateTermSection(column, WarPeaceTermCategory.Treaty,
                "aw_war_peace_category_treaties", "Treaties");
            return column;
        }

        private void CreateTermSection(TermColumn pColumn,
            WarPeaceTermCategory pCategory, string pTitleKey,
            string pTitleFallback)
        {
            var section = new TermSection();
            section.Root = new GameObject(pCategory + "Section",
                typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            section.Root.SetParent(pColumn.Content, false);
            VerticalLayoutGroup group =
                section.Root.GetComponent<VerticalLayoutGroup>();
            group.spacing = 3f;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            section.Root.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            section.Header = CreateText(section.Root, pCategory + "Header",
                8, TextAnchor.MiddleLeft);
            section.Header.text = AW_L10n.Text(pTitleKey, pTitleFallback);
            section.Header.color = new Color(.82f, .68f, .38f, 1f);
            LayoutElement headerLayout = section.Header.gameObject
                .AddComponent<LayoutElement>();
            headerLayout.minHeight = 18f;
            headerLayout.preferredHeight = 18f;
            pColumn.Sections[pCategory] = section;
        }

        private static void CreateTermsScrollArea(Transform pParent,
            out RectTransform pScrollRoot, out RectTransform pContent)
        {
            pScrollRoot = new GameObject("TermsScroll",
                typeof(RectTransform), typeof(ScrollRect))
                .GetComponent<RectTransform>();
            pScrollRoot.SetParent(pParent, false);
            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(Mask));
            viewport.transform.SetParent(pScrollRoot, false);
            RectTransform viewportRect =
                viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            viewportRect.offsetMax = new Vector2(-7f, 0f);
            viewport.GetComponent<Image>().color =
                new Color(.04f, .038f, .033f, .68f);
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
            group.spacing = 3f;
            group.padding = new RectOffset(3, 3, 3, 3);
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            pContent.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = pScrollRoot.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = pContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22f;
            CreateScrollbar(pScrollRoot, scroll);
        }

        private static void CreateScrollbar(RectTransform pRoot,
            ScrollRect pScroll)
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
            track.GetComponent<Image>().color =
                new Color(.07f, .065f, .055f, .94f);
            var handle = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handle.transform.SetParent(track.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            Stretch(handleRect);
            handle.GetComponent<Image>().color =
                new Color(.72f, .57f, .28f, .95f);
            Scrollbar bar = track.GetComponent<Scrollbar>();
            bar.handleRect = handleRect;
            bar.targetGraphic = handle.GetComponent<Image>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = bar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
        }

        private void BindParty(PartyPanel pPanel,
            WarPeacePartyPresentation pParty,
            WarPeaceScoreBreakdown pScore)
        {
            pPanel.Root.gameObject.SetActive(true);
            pPanel.KingdomName.text = pParty.KingdomName;
            pPanel.RulerName.text = pParty.RulerName;
            pPanel.ArmyStrength.text = string.Format(AW_L10n.Text(
                "aw_war_peace_army_strength", "Army: {0}"),
                pParty.ArmyStrength);
            pPanel.Casualties.text = string.Format(AW_L10n.Text(
                "aw_war_peace_casualties", "Casualties: {0}"),
                pParty.Casualties);
            pPanel.Score.text = Signed(pScore.Total);
            pPanel.Score.color = ScoreColor(pScore.Total);
            pPanel.ScoreDetail.text =
                AW_L10n.Text("aw_war_peace_score_occupation",
                    "Occupation") + " " + Signed(pScore.Occupation) +
                "  |  " + AW_L10n.Text("aw_war_peace_score_battle",
                    "Battle") + " " + Signed(pScore.Battle) +
                "  |  " + AW_L10n.Text("aw_war_peace_score_objective",
                    "Objective") + " " + Signed(pScore.Objective);
            if (pScore.Decisive != 0)
                pPanel.ScoreDetail.text += "  |  " + AW_L10n.Text(
                    "aw_war_peace_score_decisive", "Decisive") + " " +
                    Signed(pScore.Decisive);
            BindFlag(pPanel, pParty);
            BindPortrait(pPanel, pParty);
        }

        private static void BindFlag(PartyPanel pPanel,
            WarPeacePartyPresentation pParty)
        {
            Kingdom kingdom = FindKingdom(pParty.KingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                pPanel.FlagBackground.gameObject.SetActive(false);
                return;
            }
            string bannerId = string.Empty;
            try { bannerId = kingdom.getActorAsset()?.banner_id ?? ""; }
            catch { }
            KingdomFlagBuilder.Build(bannerId, kingdom.data.banner_icon_id,
                kingdom.data.banner_background_id,
                HistoryColors.FromKingdom(kingdom), kingdom.data.color_id,
                pPanel.FlagBackground, pPanel.FlagIcon);
            pPanel.FlagBackground.gameObject.SetActive(true);
            string name = pParty.KingdomName;
            pPanel.FlagTip.hoverAction = delegate
            {
                Tooltip.show(pPanel.FlagBackground.gameObject,
                    AW_RawTooltip.TYPE, new TooltipData
                    {
                        tip_name = name,
                        tip_description = AW_L10n.Text(
                            "aw_war_peace_belligerent", "Belligerent")
                    });
            };
        }

        private static void BindPortrait(PartyPanel pPanel,
            WarPeacePartyPresentation pParty)
        {
            Actor actor = FindActor(pParty.RulerActorId);
            if (actor?.data == null || !actor.isAlive() || actor.isRekt())
            {
                pPanel.PortraitFallback.gameObject.SetActive(true);
                if (pPanel.Avatar != null)
                    pPanel.Avatar.gameObject.SetActive(false);
                return;
            }
            if (pPanel.Avatar == null)
            {
                UiUnitAvatarElement prefab =
                    FamilyTreeNodeView.GetAvatarPrefab();
                if (prefab != null)
                {
                    pPanel.Avatar = UnityEngine.Object.Instantiate(prefab,
                        pPanel.PortraitRoot.transform);
                    RectTransform rect =
                        pPanel.Avatar.GetComponent<RectTransform>();
                    Stretch(rect);
                    rect.localScale = Vector3.one;
                }
            }
            if (pPanel.Avatar == null)
            {
                pPanel.PortraitFallback.gameObject.SetActive(true);
                return;
            }
            pPanel.PortraitFallback.gameObject.SetActive(false);
            pPanel.Avatar.gameObject.SetActive(true);
            pPanel.Avatar.enabled = true;
            if (pPanel.Avatar.avatarLoader != null)
                pPanel.Avatar.avatarLoader.enabled = true;
            pPanel.Avatar.show(actor);
        }

        private static void HideParty(PartyPanel pPanel)
        {
            if (pPanel == null) return;
            pPanel.Root.gameObject.SetActive(false);
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            LayoutNativeWindow(width, height);
            Layout(_root, 0f, 0f, width, height);
            WarPeaceNegotiationLayout layout =
                WarPeaceNegotiationLayoutRules.Calculate(width, height);
            ApplyRect(_header, layout.Header);
            ApplyRect(_scopePanel, layout.Scope);
            Layout(_scopeTitle.rectTransform, 8f, 1f,
                Math.Max(0f, layout.Scope.Width - 16f), 14f);
            Layout(_scopeDetail.rectTransform, 8f, 15f,
                Math.Max(0f, layout.Scope.Width - 16f), 17f);
            float partyGap = 8f;
            float partyWidth = (layout.Header.Width - partyGap) * .5f;
            Layout(_requesterPanel.Root, 0f, 0f, partyWidth,
                layout.Header.Height);
            Layout(_responderPanel.Root, partyWidth + partyGap, 0f,
                partyWidth, layout.Header.Height);
            LayoutParty(_requesterPanel, partyWidth);
            LayoutParty(_responderPanel, partyWidth);
            ApplyRect(_demandsColumn.Root, layout.Demands);
            ApplyRect(_summaryPanel, layout.Summary);
            ApplyRect(_concessionsColumn.Root, layout.Concessions);
            LayoutTermColumn(_demandsColumn, layout.Demands);
            LayoutTermColumn(_concessionsColumn, layout.Concessions);
            LayoutSummary(layout.Summary.Width, layout.Summary.Height);
            ApplyRect(_status.rectTransform, layout.Status);
            _status.rectTransform.offsetMin += new Vector2(4f, 0f);
            _status.rectTransform.sizeDelta -= new Vector2(8f, 0f);
            ApplyRect(_backButton.GetComponent<RectTransform>(),
                layout.BackButton);
            ApplyRect(_submitButton.GetComponent<RectTransform>(),
                layout.SubmitButton);
            _chrome?.RepositionResizeHandle();
        }

        private void BindScope()
        {
            bool separate = string.Equals(_presentation.Scope,
                "separate_participant", StringComparison.Ordinal);
            _scopeTitle.text = AW_L10n.Text(separate
                    ? "aw_war_peace_scope_separate"
                    : "aw_war_peace_scope_coalition",
                separate ? "Separate peace" : "Comprehensive peace");
            if (!separate)
            {
                _scopeDetail.text = AW_L10n.Text(
                    "aw_war_peace_coalition_settlement_notice",
                    "All belligerents settle and the war ends");
                return;
            }
            string notice = AW_L10n.Text(
                "aw_war_peace_main_war_continues",
                "The main war continues");
            string participants = string.Join(", ",
                _presentation.ExitParticipantNames);
            _scopeDetail.text = string.IsNullOrWhiteSpace(participants)
                ? notice
                : notice + " | " + string.Format(AW_L10n.Text(
                    "aw_war_peace_exit_participants",
                    "Leaving: {0}"), participants);
        }

        private static void LayoutTermColumn(TermColumn pColumn,
            WarPeaceNegotiationRect pLayout)
        {
            Layout(pColumn.Title.rectTransform, 8f, 2f,
                Math.Max(0f, pLayout.Width - 16f), 22f);
            Layout(pColumn.Viewport, 4f, 25f,
                Math.Max(0f, pLayout.Width - 8f),
                Math.Max(0f, pLayout.Height - 29f));
        }

        private void LayoutNativeWindow(float pWidth, float pHeight)
        {
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            RectTransform titleRect = BackgroundTransform
                ?.Find("TitleBackground")?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * .58f, 30f);
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
                nativeScrollRect.sizeDelta = new Vector2(pWidth, pHeight);
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
            RectTransform nativeViewport = ContentTransform?.parent as
                RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(pWidth, pHeight);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static void LayoutParty(PartyPanel pPanel, float pWidth)
        {
            Layout(pPanel.FlagBackground.rectTransform, 6f, 7f, 32f, 32f);
            Layout(pPanel.PortraitRoot.GetComponent<RectTransform>(), 43f, 5f,
                52f, 52f);
            Layout(pPanel.KingdomName.rectTransform, 101f, 5f,
                Math.Max(45f, pWidth - 158f), 23f);
            Layout(pPanel.RulerName.rectTransform, 101f, 28f,
                Math.Max(45f, pWidth - 158f), 19f);
            Layout(pPanel.ArmyStrength.rectTransform, 101f, 47f,
                Math.Max(45f, pWidth - 158f), 14f);
            Layout(pPanel.Casualties.rectTransform, 101f, 61f,
                Math.Max(45f, pWidth - 158f), 14f);
            Layout(pPanel.Score.rectTransform, pWidth - 55f, 4f, 49f, 43f);
            Layout(pPanel.ScoreDetail.rectTransform, 8f, 76f,
                pWidth - 16f, 10f);
        }

        private void LayoutSummary(float pWidth, float pHeight)
        {
            WarPeaceNegotiationSummaryLayout layout =
                WarPeaceNegotiationSummaryLayoutRules.Calculate(pWidth,
                    pHeight);
            ApplyRect(_summaryTitle.rectTransform, layout.Title);
            ApplyRect(_budgetCapacity.rectTransform, layout.Capacity);
            ApplyRect(_budgetSpent.rectTransform, layout.Spent);
            ApplyRect(_budgetRemaining.rectTransform, layout.Remaining);
            ApplyRect(_netDemand.rectTransform, layout.NetDemand);
            ApplyRect(_bilateralExhaustion.rectTransform,
                layout.Exhaustion);
            ApplyRect(_acceptance.rectTransform, layout.Acceptance);
            ApplyRect(_acceptanceMargin.rectTransform, layout.Margin);
            ApplyRect(_acceptanceFactors.rectTransform, layout.Factors);
        }

        private void SetWindowTitle()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText == null) return;
            string title = AW_L10n.Text("aw_war_peace_negotiation_title",
                "Peace Negotiation");
            if (!string.IsNullOrEmpty(_presentation?.WarName))
                title += " - " + _presentation.WarName;
            window.titleText.text = title;
            window.titleText.raycastTarget = false;
        }

        private static string DisabledTermReason(
            WarPeaceTermPresentation pTerm,
            WarPeaceTermAvailability pAvailability, int pRemaining)
        {
            switch (pAvailability.DisabledReason)
            {
                case WarPeaceTermDisabledReason.InsufficientCapacity:
                    return string.Format(AW_L10n.Text(
                            "aw_war_peace_disabled_side_capacity",
                            "Needs {0}; this side has {1} capacity left"),
                        pAvailability.Cost, Math.Max(0, pRemaining));
                case WarPeaceTermDisabledReason.PrerequisiteFailed:
                    return PrerequisiteReason(pAvailability.DetailReason);
                default:
                    return string.Empty;
            }
        }

        private static string SubmitDisabledReason(string pReason)
        {
            switch (pReason)
            {
                case "no_terms_selected":
                    return AW_L10n.Text("aw_war_peace_disabled_no_terms",
                        "Select at least one peace term");
                case "not_war_leader":
                    return AW_L10n.Text("aw_war_peace_disabled_war_leader",
                        "Only the principal belligerents may negotiate");
                case "treaty_side_capacity_exceeded":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_side_capacity_short",
                        "Demands or concessions exceed their 100-point cap");
                case "demand_gross_exceeds_cap":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_demand_cap",
                        "Demands exceed the 100-point cap");
                case "concession_gross_exceeds_cap":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_concession_cap",
                        "Concessions exceed the 100-point cap");
                case "invalid_term_selection":
                    return AW_L10n.Text("aw_war_peace_disabled_invalid",
                        "The selected terms are no longer valid");
                case "invalid_term_count":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_term_count",
                        "Too many peace terms are selected");
                case "white_peace_must_stand_alone":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_white_peace_mixed",
                        "White peace cannot be combined with other terms");
                case "conflicting_subject_terms":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_subject_conflict",
                        "Vassalage and tributary status cannot both be selected");
                case WarPeaceTreatySurvivalRules.FailureReason:
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_annexation_survival",
                        "Full annexation cannot be combined with vassalage, tributary status, or war reparations");
                case "war_no_longer_active":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_war_ended",
                        "The war has already ended");
                case "war_score_unavailable":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_score_unavailable",
                        "Current war score is unavailable");
                default:
                    return PrerequisiteReason(pReason);
            }
        }

        private static string PrerequisiteReason(string pReason)
        {
            switch (pReason)
            {
                case "no_territorial_basis":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_territorial_basis",
                        "Requires occupation or a valid core or claim");
                case "payment_amount_exceeds_limit":
                case "invalid_payment_amount":
                case "invalid_material_payment":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_payment",
                        "The requested payment is no longer available");
                case "reparations_exceed_limit":
                case "invalid_reparations":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_reparations",
                        "The requested reparations are no longer valid");
                case "invalid_or_duplicate_captive":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_captive",
                        "The selected captive is no longer available");
                case "invalid_or_duplicate_claim":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_claim",
                        "The selected claim is no longer available");
                case "invalid_or_duplicate_city":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_city",
                        "The selected city is no longer available");
                case "force_subject_failed":
                    return AW_L10n.Text(
                        "aw_war_peace_disabled_subject",
                        "The requested subject relationship is no longer valid");
            }
            if (string.IsNullOrEmpty(pReason))
                return AW_L10n.Text("aw_war_peace_unavailable",
                    "This term is currently unavailable");
            return AW_L10n.Text("aw_war_peace_disabled_prerequisite",
                       "Prerequisite not met") + ": " + pReason;
        }

        private static string Signed(int pValue)
        {
            return pValue > 0 ? "+" + pValue : pValue.ToString();
        }

        private static Color ScoreColor(int pScore)
        {
            if (pScore > 0) return new Color(.45f, .88f, .49f, 1f);
            if (pScore < 0) return new Color(.94f, .44f, .38f, 1f);
            return new Color(.92f, .76f, .36f, 1f);
        }

        private static RectTransform CreatePanel(Transform pParent,
            string pName, Color pColor)
        {
            RectTransform rect = new GameObject(pName,
                typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            rect.SetParent(pParent, false);
            rect.GetComponent<Image>().color = pColor;
            return rect;
        }

        private static Image CreateImage(Transform pParent, string pName,
            Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.color = pColor;
            image.raycastTarget = false;
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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateTextButton(Transform pParent,
            string pName, UnityEngine.Events.UnityAction pAction,
            out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            pText = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleCenter);
            Stretch(pText.rectTransform);
            pText.rectTransform.offsetMin = new Vector2(4f, 2f);
            pText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            return button;
        }

        private static void ApplyRect(RectTransform pRect,
            WarPeaceNegotiationRect pLayout)
        {
            Layout(pRect, pLayout.X, pLayout.Y, pLayout.Width,
                pLayout.Height);
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Math.Max(0f, pWidth),
                Math.Max(0f, pHeight));
        }

        private static void LayoutStretch(RectTransform pRect, float pLeft,
            float pTop, float pRight, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(1f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pLeft, -pTop);
            pRect.sizeDelta = new Vector2(-pLeft - pRight, pHeight);
        }

        private static void LayoutRight(RectTransform pRect, float pRight,
            float pTop, float pWidth, float pHeight)
        {
            pRect.anchorMin = pRect.anchorMax = new Vector2(1f, 1f);
            pRect.pivot = new Vector2(1f, 1f);
            pRect.anchoredPosition = new Vector2(-pRight, -pTop);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static void Stretch(RectTransform pRect)
        {
            if (pRect == null) return;
            pRect.anchorMin = Vector2.zero;
            pRect.anchorMax = Vector2.one;
            pRect.offsetMin = Vector2.zero;
            pRect.offsetMax = Vector2.zero;
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
