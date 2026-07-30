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
    internal sealed class InheritanceLawWindow :
        AbstractWindow<InheritanceLawWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(
            InheritanceLawRules.DefaultWindowWidth,
            InheritanceLawRules.DefaultWindowHeight);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);

        private static long _kingdomId = -1L;
        private readonly List<LawRow> _rows = new List<LawRow>(4);
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private Text _summary;
        private Text _candidate;
        private Text _scores;
        private Text _dispute;
        private Text _feedback;
        private Button _courtBack;
        private RectTransform _portraitRoot;
        private UiUnitAvatarElement _portrait;
        private WideWindowChrome _chrome;
        private string _feedbackKey = "";
        private bool _feedbackError;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.INHERITANCE_LAWS);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.INHERITANCE_LAWS,
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

            _root = new GameObject("InheritanceLawRoot",
                typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            _root.SetParent(ContentTransform, false);
            Image panel = _root.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(panel, 0.98f);
            panel.color = new Color(0.075f, 0.07f, 0.058f, 0.98f);

            _portraitRoot = new GameObject("CandidatePortrait",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _portraitRoot.SetParent(_root, false);
            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab != null)
            {
                _portrait = UnityEngine.Object.Instantiate(prefab,
                    _portraitRoot);
                RectTransform portraitRect =
                    _portrait.GetComponent<RectTransform>();
                portraitRect.anchorMin = Vector2.zero;
                portraitRect.anchorMax = Vector2.one;
                portraitRect.offsetMin = Vector2.zero;
                portraitRect.offsetMax = Vector2.zero;
                portraitRect.localScale = Vector3.one;
            }

            _summary = CreateText(_root, "Summary", 10,
                TextAnchor.UpperLeft);
            _candidate = CreateText(_root, "Candidate", 9,
                TextAnchor.UpperLeft);
            _scores = CreateText(_root, "Scores", 8,
                TextAnchor.UpperLeft);
            _dispute = CreateText(_root, "Dispute", 8,
                TextAnchor.UpperLeft);
            _feedback = CreateText(_root, "Feedback", 8,
                TextAnchor.MiddleLeft);
            _courtBack = CreateButton(_root, "BackToCourt",
                AW_L10n.Text("aw_back_to_kingdom", "Back to Kingdom"),
                BackToKingdom);

            _rows.Add(CreateRow("Automatic", null));
            _rows.Add(CreateRow("Primogeniture",
                InheritanceLaw.Primogeniture));
            _rows.Add(CreateRow("Military",
                InheritanceLaw.MilitaryAcclaim));
            _rows.Add(CreateRow("Civil", InheritanceLaw.CivilAcclaim));
        }

        private LawRow CreateRow(string pName, InheritanceLaw? pLaw)
        {
            InheritanceLaw? captured = pLaw;
            Button button = CreateButton(_root, pName, "",
                () => Apply(captured));
            var row = new LawRow
            {
                Law = pLaw,
                Button = button,
                Text = button.transform.Find("Text").GetComponent<Text>(),
                Tip = button.GetComponent<TipButton>()
            };
            if (pLaw.HasValue)
            {
                row.Text.rectTransform.offsetMax = new Vector2(-42f, -2f);
                row.PortraitRoot = new GameObject(pName + "CandidatePortrait",
                    typeof(RectTransform)).GetComponent<RectTransform>();
                row.PortraitRoot.SetParent(_root, false);
                UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
                if (prefab != null)
                {
                    row.Portrait = UnityEngine.Object.Instantiate(prefab,
                        row.PortraitRoot);
                    RectTransform portrait =
                        row.Portrait.GetComponent<RectTransform>();
                    portrait.anchorMin = Vector2.zero;
                    portrait.anchorMax = Vector2.one;
                    portrait.offsetMin = Vector2.zero;
                    portrait.offsetMax = Vector2.zero;
                    portrait.localScale = Vector3.one;
                }
                row.PortraitRoot.gameObject.SetActive(false);
            }
            return row;
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                _summary.text = AW_L10n.Text(
                    "aw_inheritance_result_invalid_kingdom",
                    "Kingdom unavailable");
                SetRowsInteractable(false);
                if (_portraitRoot != null)
                    _portraitRoot.gameObject.SetActive(false);
                foreach (LawRow row in _rows)
                    RefreshRowPortrait(row, null);
                return;
            }

            InheritanceLaw effective =
                InheritanceLawService.GetEffectiveLaw(kingdom);
            InheritanceLaw? locked =
                InheritanceLawService.GetLockedLaw(kingdom);
            kingdom.data.get(LineageKeys.INHERITANCE_MILITARY_UNLOCKED,
                out bool militaryUnlocked, false);
            kingdom.data.get(LineageKeys.INHERITANCE_CIVIL_UNLOCKED,
                out bool civilUnlocked, false);
            kingdom.data.get(LineageKeys.INHERITANCE_SCORE_PRIMOGENITURE,
                out int hereditaryScore, 0);
            kingdom.data.get(LineageKeys.INHERITANCE_SCORE_MILITARY,
                out int militaryScore, 0);
            kingdom.data.get(LineageKeys.INHERITANCE_SCORE_CIVIL,
                out int civilScore, 0);
            kingdom.data.get(LineageKeys.INHERITANCE_RULER_COURT_INFLUENCE,
                out int rulerCourtInfluence, 0);
            kingdom.data.get(LineageKeys.INHERITANCE_LAW_LAST_CHANGE_YEAR,
                out int lastChangeYear, -1);
            int cooldown = lastChangeYear < 0 ? 0 : Math.Max(0,
                lastChangeYear + InheritanceLawRules.LockCooldownYears -
                Date.getCurrentYear());
            int points = Mathf.FloorToInt(
                KingdomPolicyService.GetPoliticalPoints(kingdom));

            _summary.text = kingdom.name + "    " +
                AW_L10n.Text("aw_inheritance_effective", "Effective") +
                ": " + LawName(effective) + "    " +
                (locked.HasValue
                    ? AW_L10n.Text("aw_inheritance_locked", "Locked")
                    : AW_L10n.Text("aw_inheritance_control_automatic",
                        "Automatic"));
            Actor heir = HeirService.PeekStoredHeirForMinimap(kingdom);
            InheritanceFactionSupport factionSupport =
                InheritanceCandidateService.ResolveFactionSupport(kingdom,
                    kingdom.king, heir);
            kingdom.data.get(LineageKeys.INHERITANCE_CANDIDATE_MODE,
                out string candidateMode, SuccessionMode.NONE);
            string title = HeirTitleRules.DefaultTitleText(
                kingdom, candidateMode);
            _candidate.text = title + ": " +
                (heir?.getName() ?? AW_L10n.Text(
                    "aw_inheritance_no_candidate", "No candidate"));
            _scores.text = AW_L10n.Text(
                    "aw_inheritance_political_points", "Political points") +
                " " + points + "    " +
                AW_L10n.Text("aw_inheritance_scores", "Scores") +
                "  " + AW_L10n.Text("aw_inheritance_faction_imperial",
                    "Imperial faction") + " " + hereditaryScore +
                "  " + AW_L10n.Text("aw_inheritance_faction_military",
                    "Military faction") + " " + militaryScore +
                "  " + AW_L10n.Text("aw_inheritance_faction_civil",
                    "Civil faction") + " " + civilScore + "  " +
                AW_L10n.Text("aw_inheritance_ruler_court_influence",
                    "Ruler court influence") + " " +
                (rulerCourtInfluence > 0 ? "+" : "") +
                rulerCourtInfluence;
            IReadOnlyDictionary<InheritanceLaw,
                InheritanceCandidateSelection> selections =
                factionSupport.Selections;
            RefreshDispute(kingdom, factionSupport);
            _feedback.text = string.IsNullOrEmpty(_feedbackKey)
                ? AW_L10n.Text("aw_inheritance_hint",
                    "Lock one law or return succession to automatic evaluation.")
                : AW_L10n.Text(_feedbackKey, _feedbackKey);
            _feedback.color = _feedbackError
                ? new Color(1f, 0.50f, 0.42f, 1f)
                : new Color(0.82f, 0.90f, 0.68f, 1f);

            bool monarchy = !RepublicGovernmentService.IsRepublic(kingdom);
            foreach (LawRow row in _rows)
            {
                bool unlocked = !row.Law.HasValue ||
                                row.Law == InheritanceLaw.Primogeniture ||
                                row.Law == InheritanceLaw.MilitaryAcclaim &&
                                militaryUnlocked ||
                                row.Law == InheritanceLaw.CivilAcclaim &&
                                civilUnlocked;
                bool current = row.Law == locked;
                int cost = InheritanceLawRules.ChangeCost(row.Law);
                string state = current
                    ? AW_L10n.Text("aw_inheritance_current_lock",
                        "Current lock")
                    : !unlocked
                        ? AW_L10n.Text("aw_inheritance_unavailable",
                            "Unavailable")
                        : cooldown > 0
                            ? string.Format(AW_L10n.Text(
                                    "aw_inheritance_cooldown_years",
                                    "Cooldown: {0} years"), cooldown)
                            : cost > points
                                ? AW_L10n.Text(
                                    "aw_inheritance_insufficient_points",
                                    "Insufficient political points")
                                : cost > 0
                                    ? string.Format(AW_L10n.Text(
                                            "aw_inheritance_cost_points",
                                            "Cost: {0}"), cost)
                                    : AW_L10n.Text(
                                        "aw_inheritance_free", "Free");
                InheritanceCandidateSelection selection = row.Law.HasValue &&
                    selections.TryGetValue(row.Law.Value,
                        out InheritanceCandidateSelection found)
                    ? found
                    : null;
                string candidateSummary = row.Law.HasValue
                    ? CandidateSummary(row.Law.Value, selection)
                    : "";
                row.Text.text = (current ? "* " : "") +
                                ControlName(row.Law) + "    " + state +
                                (candidateSummary.Length == 0
                                    ? ""
                                    : "\n" + candidateSummary);
                row.Button.interactable = !_commandPending && monarchy &&
                                          !current && unlocked &&
                                          cooldown == 0 && points >= cost;
                row.Button.GetComponent<Image>().color = current
                    ? new Color(0.20f, 0.35f, 0.22f, 1f)
                    : unlocked
                        ? new Color(0.20f, 0.18f, 0.14f, 0.98f)
                        : new Color(0.12f, 0.11f, 0.10f, 0.82f);
                SetTip(row.Tip,
                    row.Law.HasValue
                        ? LawName(row.Law.Value)
                        : ControlName(row.Law),
                    UnlockDescription(row.Law, unlocked) + "\n" + state +
                    (candidateSummary.Length == 0
                        ? ""
                        : "\n" + candidateSummary));
                RefreshRowPortrait(row, selection);
            }

            bool hasHeir = heir?.data != null && heir.isAlive() &&
                           !heir.isRekt();
            _portraitRoot.gameObject.SetActive(hasHeir && _portrait != null);
            if (hasHeir && _portrait != null) _portrait.show(heir);
        }

        private void RefreshDispute(Kingdom pKingdom,
            InheritanceFactionSupport pSupport)
        {
            if (_dispute == null) return;
            if (!SuccessionDisputeService.TryGetMaterializedByKingdom(
                    pKingdom?.id ?? -1L,
                    out SuccessionDisputeSnapshot dispute))
            {
                long leaderId = pSupport?.LeaderActor?.data?.id ?? -1L;
                bool decisiveLead = pSupport != null &&
                    InheritanceLawRules.HasDecisiveCandidateLead(
                        pSupport.LeaderSupport, pSupport.RunnerUpSupport);
                bool designatedHeirLeads = decisiveLead &&
                    leaderId == pSupport.DesignatedHeirId;
                bool disputePending = decisiveLead &&
                    InheritanceLawRules.ShouldStartSuccessionDispute(
                        leaderId, pSupport.DesignatedHeirId,
                        pSupport.LeaderSupport, pSupport.RunnerUpSupport,
                        pKingdom?.countCities() > 1,
                        pHasActiveDispute: false);
                if (leaderId >= 0)
                {
                    _dispute.text = string.Format(AW_L10n.Text(
                            "aw_inheritance_support_ranking",
                            "Leader {0} {1}; runner-up {2} {3}; " +
                            "designated heir {4} {5}"),
                        ActorName(pSupport.LeaderActor),
                        pSupport.LeaderSupport,
                        ActorName(pSupport.RunnerUpActor),
                        pSupport.RunnerUpSupport,
                        ActorName(HeirService.PeekStoredHeirForMinimap(
                            pKingdom)), pSupport.DesignatedHeirSupport) +
                        "  " + AW_L10n.Text(designatedHeirLeads
                                ? "aw_inheritance_dispute_stable"
                                : disputePending
                                    ? "aw_inheritance_dispute_pending"
                                    : "aw_inheritance_dispute_not_decisive",
                            designatedHeirLeads
                                ? "The designated heir leads by at least 20; succession is stable."
                                : disputePending
                                    ? "The leader will rise after the heir accedes."
                                    : "No candidate leads second place by 20.");
                    _dispute.color = designatedHeirLeads
                        ? new Color(0.46f, 0.80f, 0.48f, 1f)
                        : disputePending
                        ? new Color(0.94f, 0.62f, 0.30f, 1f)
                        : new Color(0.68f, 0.70f, 0.64f, 1f);
                    return;
                }
                _dispute.text = AW_L10n.Text(
                    "aw_inheritance_dispute_none",
                    "No active succession dispute");
                _dispute.color = new Color(0.68f, 0.70f, 0.64f, 1f);
                return;
            }

            (string originalName, string rivalName) =
                SuccessionDisputeDisplayRules.BuildDistinctPair(
                    dispute.OriginalStateName,
                    dispute.OriginalQualifier, dispute.RivalQualifier,
                    active: true,
                    HistoryLocalizationRules.CurrentLanguage());
            if (dispute.Status == SuccessionDisputeStatus.PermanentSplit)
            {
                int generation = SuccessionDisputeService
                    .GetReunificationClaimGeneration(pKingdom, dispute);
                bool valid = generation >= 0 &&
                             generation <= dispute.ClaimGenerationBoundary;
                _dispute.text = string.Format(AW_L10n.Text(
                        "aw_inheritance_dispute_permanent",
                        "Permanent split: {0} / {1}; generation {2}; {3}"),
                    originalName, rivalName, Math.Max(0, generation),
                    AW_L10n.Text(valid
                            ? "aw_inheritance_reunification_valid"
                            : "aw_inheritance_reunification_expired",
                        valid ? "free reunification claim" :
                        "reunification claim expired"));
                _dispute.color = valid
                    ? new Color(0.94f, 0.74f, 0.32f, 1f)
                    : new Color(0.80f, 0.48f, 0.42f, 1f);
                return;
            }

            _dispute.text = string.Format(AW_L10n.Text(
                    "aw_inheritance_dispute_active",
                    "Succession dispute: {0} / {1}; deadline {2}"),
                originalName, rivalName, dispute.DeadlineYear);
            _dispute.color = new Color(0.94f, 0.62f, 0.30f, 1f);
        }

        private static string ActorName(Actor pActor)
        {
            return pActor?.data != null
                ? pActor.getName()
                : AW_L10n.Text("aw_inheritance_no_candidate",
                    "No candidate");
        }

        private void Apply(InheritanceLaw? pLaw)
        {
            if (_commandPending) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.ChangeInheritanceLaw(_kingdomId,
                        pLaw?.ToString() ?? "automatic"));
            if (result.Status == AW3CommandStatus.Pending)
            {
                _commandPending = true;
                _feedbackKey = "aw3_command_pending";
                _feedbackError = false;
                Refresh();
                return;
            }
            InheritanceLawChangeResult domain = Enum.IsDefined(
                    typeof(InheritanceLawChangeResult), result.DetailCode)
                ? (InheritanceLawChangeResult)result.DetailCode
                : InheritanceLawChangeResult.PersistenceFailed;
            _feedbackKey = ResultKey(domain);
            _feedbackError = !result.Accepted;
            Refresh();
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_inheritance_window_title", "Inheritance Law");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                window.titleText.raycastTarget = false;
            }
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(width, height);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            bool requiresScroll = InheritanceLawRules.RequiresVerticalScroll(
                height);
            if (nativeScroll != null)
            {
                ScrollRect native = nativeScroll.GetComponent<ScrollRect>();
                if (native != null)
                {
                    native.horizontal = false;
                    native.vertical = requiresScroll;
                }
            }
            RectTransform nativeViewport = ContentTransform?.parent as
                RectTransform;
            if (nativeViewport != null)
            {
                nativeViewport.sizeDelta = new Vector2(width, height);
                if (nativeViewport.GetComponent<RectMask2D>() == null)
                    nativeViewport.gameObject.AddComponent<RectMask2D>();
            }
            RectTransform nativeContent = ContentTransform as RectTransform;
            float contentHeight = requiresScroll
                ? Mathf.Max(height,
                    InheritanceLawRules.MinimumContentViewportHeight)
                : height;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(width, contentHeight);

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, contentHeight);
            Layout(_portraitRoot, 10f, 10f, 54f, 54f);
            Layout(_summary.rectTransform, 72f, 8f,
                Mathf.Max(1f, width - 158f), 22f);
            Layout(_courtBack.GetComponent<RectTransform>(),
                width - 78f, 8f, 68f, 22f);
            Layout(_candidate.rectTransform, 72f, 30f,
                Mathf.Max(1f, width - 82f), 20f);
            Layout(_scores.rectTransform, 72f, 51f,
                Mathf.Max(1f, width - 82f), 20f);
            Layout(_dispute.rectTransform, 10f, 72f,
                Mathf.Max(1f, width - 20f), 20f);

            float rowTop = 96f;
            float rowHeight = Mathf.Max(40f,
                Math.Min(44f, (contentHeight - rowTop - 28f) / 4f));
            for (int index = 0; index < _rows.Count; index++)
            {
                float rowY = rowTop + index * rowHeight;
                Layout(_rows[index].Button.GetComponent<RectTransform>(),
                    10f, rowY,
                    Mathf.Max(1f, width - 20f), rowHeight - 3f);
                if (_rows[index].PortraitRoot != null)
                    Layout(_rows[index].PortraitRoot,
                        width - 43f, rowY + 4f, 30f, 30f);
            }
            Layout(_feedback.rectTransform, 10f,
                rowTop + 4f * rowHeight,
                Mathf.Max(1f, width - 20f), 22f);
            _chrome?.RepositionResizeHandle();
        }

        private void BackToKingdom()
        {
            AW_LineageWindowIds.ShowKingdom(_kingdomId);
        }

        private void SetRowsInteractable(bool pValue)
        {
            foreach (LawRow row in _rows)
                row.Button.interactable = pValue;
        }

        private static string ControlName(InheritanceLaw? pLaw)
        {
            return pLaw.HasValue
                ? FactionName(pLaw.Value)
                : AW_L10n.Text("aw_inheritance_control_automatic",
                    "Automatic evaluation");
        }

        private static string FactionName(InheritanceLaw pLaw)
        {
            return pLaw switch
            {
                InheritanceLaw.MilitaryAcclaim => AW_L10n.Text(
                    "aw_inheritance_faction_military", "Military faction"),
                InheritanceLaw.CivilAcclaim => AW_L10n.Text(
                    "aw_inheritance_faction_civil", "Civil faction"),
                _ => AW_L10n.Text("aw_inheritance_faction_imperial",
                    "Imperial faction")
            };
        }

        private static string LawName(InheritanceLaw pLaw)
        {
            return pLaw switch
            {
                InheritanceLaw.MilitaryAcclaim => AW_L10n.Text(
                    "aw_inheritance_law_military", "Military acclaim"),
                InheritanceLaw.CivilAcclaim => AW_L10n.Text(
                    "aw_inheritance_law_civil", "Civil acclaim"),
                _ => AW_L10n.Text("aw_inheritance_law_primogeniture",
                    "Primogeniture")
            };
        }

        private static string UnlockDescription(InheritanceLaw? pLaw,
            bool pUnlocked)
        {
            if (!pLaw.HasValue)
                return AW_L10n.Text("aw_inheritance_auto_desc",
                    "Evaluate the three laws every six years.");
            if (pLaw == InheritanceLaw.Primogeniture)
                return AW_L10n.Text("aw_inheritance_primogeniture_desc",
                    "The eldest legitimate son and then lawful collateral inherit.");
            if (pLaw == InheritanceLaw.MilitaryAcclaim)
                return AW_L10n.Text(pUnlocked
                        ? "aw_inheritance_military_desc"
                        : "aw_inheritance_military_locked_desc",
                    pUnlocked
                        ? "Generals support an adult male royal dynast."
                        : "Needs an adult male royal dynast, an active general and an army.");
            return AW_L10n.Text(pUnlocked
                    ? "aw_inheritance_civil_desc"
                    : "aw_inheritance_civil_locked_desc",
                pUnlocked
                    ? "Officials support an adult male royal dynast."
                    : "Needs the official court, three departments, finite terms and three officials.");
        }

        private static string CandidateSummary(InheritanceLaw pLaw,
            InheritanceCandidateSelection pSelection)
        {
            string candidate = pSelection?.Actor?.data == null
                ? AW_L10n.Text("aw_inheritance_no_candidate", "No candidate")
                : pSelection.Actor.getName();
            string score = pSelection == null ||
                           pSelection.Score == int.MinValue
                ? "-"
                : pSelection.Score.ToString();
            string support = pLaw == InheritanceLaw.Primogeniture
                ? AW_L10n.Text("aw_inheritance_support_ritual",
                    "ritual order")
                : (pSelection?.SupporterCount ?? 0).ToString();
            return AW_L10n.Text("aw_inheritance_candidate_backing",
                       "Backs") + ": " + candidate + "    " +
                   AW_L10n.Text("aw_inheritance_candidate_score",
                       "Candidate score") + " " + score + "    " +
                   AW_L10n.Text("aw_inheritance_supporters",
                       "Supporters") + " " + support;
        }

        private static void RefreshRowPortrait(LawRow row,
            InheritanceCandidateSelection pSelection)
        {
            if (row?.PortraitRoot == null) return;
            Actor actor = pSelection?.Actor;
            bool live = actor?.data != null && actor.isAlive() &&
                        !actor.isRekt() && row.Portrait != null;
            row.PortraitRoot.gameObject.SetActive(live);
            if (!live) return;
            row.Portrait.enabled = true;
            if (row.Portrait.avatarLoader != null)
                row.Portrait.avatarLoader.enabled = true;
            row.Portrait.show(actor);
        }

        private static string ResultKey(InheritanceLawChangeResult pResult)
        {
            return pResult switch
            {
                InheritanceLawChangeResult.Success =>
                    "aw_inheritance_result_success",
                InheritanceLawChangeResult.NoChange =>
                    "aw_inheritance_result_no_change",
                InheritanceLawChangeResult.Cooldown =>
                    "aw_inheritance_result_cooldown",
                InheritanceLawChangeResult.InsufficientPoliticalPoints =>
                    "aw_inheritance_result_points",
                InheritanceLawChangeResult.Unavailable =>
                    "aw_inheritance_result_unavailable",
                InheritanceLawChangeResult.InvalidKingdom =>
                    "aw_inheritance_result_invalid_kingdom",
                _ => "aw_inheritance_result_failed"
            };
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            Text text = new GameObject(pName, typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(pParent, false);
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

        private static Button CreateButton(Transform pParent, string pName,
            string pText, Action pClick)
        {
            GameObject root = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            root.transform.SetParent(pParent, false);
            Image image = root.GetComponent<Image>();
            Sprite sprite = SpriteTextureLoader.getSprite(
                "ui/special/windowInnerSliced");
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            image.color = new Color(0.20f, 0.18f, 0.14f, 0.98f);
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => pClick?.Invoke());
            Text text = CreateText(root.transform, "Text", 9,
                TextAnchor.MiddleLeft);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);
            text.text = pText ?? "";
            root.AddComponent<TipButton>().type = AW_RawTooltip.TYPE;
            return button;
        }

        private static void SetTip(TipButton pTip, string pTitle,
            string pDescription)
        {
            if (pTip == null) return;
            pTip.type = AW_RawTooltip.TYPE;
            pTip.textOnClick = pTitle ?? "";
            pTip.text_description_2 = pDescription ?? "";
        }

        private static void Layout(RectTransform pRect, float pX,
            float pY, float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth),
                Mathf.Max(1f, pHeight));
        }

        private sealed class LawRow
        {
            public InheritanceLaw? Law;
            public Button Button;
            public Text Text;
            public TipButton Tip;
            public RectTransform PortraitRoot;
            public UiUnitAvatarElement Portrait;
        }
    }
}
