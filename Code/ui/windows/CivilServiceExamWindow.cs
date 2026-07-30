using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CivilServiceExamWindow :
        AbstractWindow<CivilServiceExamWindow>
    {
        private const float DefaultWidth = 560f;
        private const float DefaultHeight = 360f;
        private const float MinWidth = 420f;
        private const float MinHeight = 280f;
        private const float MaxWidth = 900f;
        private const float MaxHeight = 650f;
        private const float WindowMarginX = 36f;
        private const float WindowMarginY = 58f;
        private const float HeaderHeight = 66f;
        private const float TabsHeight = 28f;
        private const float FooterHeight = 34f;
        private const int PortraitsPerFrame = 8;
        private const string AllTabId = "all";
        private const string HistoryTabId = "history";

        private sealed class TabControl
        {
            public string Id;
            public Button Button;
            public Text Label;
        }

        private static long _requestedKingdomId = -1L;
        private static long _requestedSessionId = -1L;

        private readonly List<TabControl> _tabs = new List<TabControl>();
        private readonly List<CivilServiceExamCandidateRow> _rowPool =
            new List<CivilServiceExamCandidateRow>();
        private readonly Queue<CivilServiceExamCandidateRow> _portraitQueue =
            new Queue<CivilServiceExamCandidateRow>();
        private readonly List<long> _rankingOrder = new List<long>();

        private Vector2 _windowSize = new Vector2(DefaultWidth, DefaultHeight);
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private RectTransform _header;
        private Image _flagBackground;
        private Image _flagIcon;
        private Text _headerTitle;
        private Text _headerBody;
        private Button _courtBack;
        private RectTransform _tabsRoot;
        private Button _historyPrevious;
        private Button _historyNext;
        private RectTransform _listRoot;
        private RectTransform _listViewport;
        private RectTransform _listContent;
        private ScrollRect _listScroll;
        private Scrollbar _listScrollbar;
        private Text _empty;
        private RectTransform _footer;
        private Text _rankingSummary;
        private Text _message;
        private Button _submitRanking;
        private Text _submitRankingText;
        private long _kingdomId = -1L;
        private long _selectedSessionId = -1L;
        private long _rankingSessionId = -1L;
        private string _selectedTab = AllTabId;
        private CivilServiceExamSnapshot _snapshot;

        public static void Open(long pKingdomId, long pSessionId = -1L)
        {
            _requestedKingdomId = pKingdomId;
            _requestedSessionId = pSessionId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.CIVIL_SERVICE_EXAM);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.CIVIL_SERVICE_EXAM,
                () => Instance?.ApplyRequestAndRefresh());
        }

        protected override void Init()
        {
            EnsureUi();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size =>
                {
                    _windowSize = size;
                    ApplyLayout();
                    RenderRows();
                },
                new Vector2(DefaultWidth, DefaultHeight),
                new Vector2(MinWidth, MinHeight),
                new Vector2(MaxWidth, MaxHeight));
            ApplyLayout();
        }

        public override void OnNormalEnable()
        {
            ApplyRequestAndRefresh();
        }

        public override void OnNormalDisable()
        {
            _portraitQueue.Clear();
        }

        private void Update()
        {
            if (!isActiveAndEnabled || _portraitQueue.Count == 0) return;
            int budget = Math.Min(PortraitsPerFrame, _portraitQueue.Count);
            while (budget-- > 0)
            {
                CivilServiceExamCandidateRow row = _portraitQueue.Dequeue();
                if (row == null || !row.gameObject.activeSelf ||
                    !row.NeedsPortrait) continue;
                if (!row.TryEnsurePortrait()) _portraitQueue.Enqueue(row);
            }
        }

        private void ApplyRequestAndRefresh()
        {
            bool kingdomChanged = _kingdomId != _requestedKingdomId;
            _kingdomId = _requestedKingdomId;
            if (_requestedSessionId >= 0L)
                _selectedSessionId = _requestedSessionId;
            else if (kingdomChanged)
                _selectedSessionId = -1L;
            if (kingdomChanged)
            {
                _selectedTab = AllTabId;
                _rankingSessionId = -1L;
                _rankingOrder.Clear();
            }
            Refresh();
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            Kingdom kingdom = FindKingdom(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                _snapshot = null;
                _headerTitle.text = AW_L10n.Text("aw_policy_no_kingdom",
                    "Kingdom missing");
                _headerBody.text = "";
                RenderRows();
                return;
            }

            _snapshot = CivilServiceExamReadModel.Load(_kingdomId,
                _selectedSessionId);
            if (_selectedTab == HistoryTabId)
                SelectHistorySession();
            if (_snapshot?.SelectedSession != null)
                _selectedSessionId = _snapshot.SelectedSession.SessionId;
            UpdateFlag(kingdom);
            UpdateHeader(kingdom);
            BuildStageTabs(kingdom);
            EnsureRankingOrder();
            RenderRows();
            UpdateFooter();
        }

        private void UpdateHeader(Kingdom pKingdom)
        {
            string mode = AW_L10n.Text(
                CivilServiceExamReadModel.ModeLocalizationKey(
                    _snapshot?.SelectedSession, pKingdom),
                "Examination");
            CivilServiceExamSessionView session = _snapshot?.SelectedSession;
            _headerTitle.color = KingdomColor(pKingdom);
            _headerTitle.text = pKingdom.name + "  |  " + mode;
            if (session == null)
            {
                _headerBody.text = AW_L10n.Text(
                    "aw_civil_service_exam_no_session",
                    "No examination session has opened yet.");
            }
            else
            {
                string stage = LocalizedStage(session.Stage);
                string due = session.IsCompleted
                    ? AW_L10n.Text("aw_civil_service_exam_completed",
                        "Completed")
                    : FormatWorldDay(session.NextDueWorldDay);
                string sessionSummary = string.Format(AW_L10n.Text(
                        "aw_civil_service_exam_session_summary",
                        "World year {0}  |  {1}  |  Next: {2}  |  Candidates: {3}"),
                    session.CycleYear, stage, due,
                    _snapshot.Candidates.Count);
                int vacancyCount = Math.Max(0,
                    session.CentralVacancies + session.CityVacancies);
                string vacancies = AW_L10n.Text(
                    "aw_civil_service_vacancies", "Vacancies") + " " +
                    vacancyCount;
                string admission = AW_L10n.Text(
                    "aw_civil_service_admission", "Admitted") + " " +
                    Math.Max(0, session.AdmissionQuota);
                string reserve = CivilServiceExamRules.ShouldShowReserveSummary(
                        session.WaitingCandidateCount,
                        session.ReserveTarget)
                    ? "  |  " + AW_L10n.Text(
                        "aw_civil_service_reserve", "Waiting reserve") +
                      " " + session.WaitingCandidateCount + "/" +
                      session.ReserveTarget
                    : "";
                _headerBody.text = sessionSummary + "\n" + vacancies +
                    reserve + "  |  " + admission;
            }
            ScrollWindow scrollWindow = GetComponent<ScrollWindow>();
            if (scrollWindow?.titleText != null)
                scrollWindow.titleText.text = AW_L10n.Text(
                    "aw_civil_service_exam_title", mode);
        }

        private void BuildStageTabs(Kingdom pKingdom)
        {
            CivilServiceExamMode mode = CivilServiceExamReadModel.ResolveMode(_snapshot?.SelectedSession, pKingdom);
            string[] ids = mode == CivilServiceExamMode.Imperial
                ? new[] { AllTabId, "local", "metropolitan", "palace",
                    HistoryTabId }
                : new[] { AllTabId, "prefectural", "national",
                    HistoryTabId };
            for (int index = 0; index < ids.Length; index++)
            {
                while (_tabs.Count <= index) _tabs.Add(CreateTab());
                TabControl tab = _tabs[index];
                tab.Id = ids[index];
                tab.Label.text = TabLabel(ids[index]);
                tab.Button.onClick.RemoveAllListeners();
                string id = ids[index];
                tab.Button.onClick.AddListener(() => SelectTab(id));
                AW_UIStyle.ApplyButton(tab.Button.GetComponent<Image>(),
                    id == _selectedTab ? 1f : .76f);
                tab.Label.color = id == _selectedTab
                    ? new Color(1f, .82f, .38f, 1f)
                    : Color.white;
                tab.Button.gameObject.SetActive(true);
            }
            for (int index = ids.Length; index < _tabs.Count; index++)
                _tabs[index].Button.gameObject.SetActive(false);
            LayoutTabs(ids.Length);

            bool history = _selectedTab == HistoryTabId;
            _historyPrevious.gameObject.SetActive(history);
            _historyNext.gameObject.SetActive(history);
            UpdateHistoryNavigation();
        }

        private void SelectTab(string pTabId)
        {
            if (string.IsNullOrEmpty(pTabId) || pTabId == _selectedTab)
                return;
            _selectedTab = pTabId;
            if (pTabId == HistoryTabId) SelectHistorySession();
            else SelectActiveSession();
            Refresh();
        }

        private void SelectActiveSession()
        {
            CivilServiceExamSessionView active = _snapshot?.Sessions?
                .FirstOrDefault(pSession => pSession.IsActive);
            if (active != null) _selectedSessionId = active.SessionId;
        }

        private void SelectHistorySession()
        {
            if (_snapshot?.Sessions == null) return;
            CivilServiceExamSessionView selected = _snapshot.Sessions
                .FirstOrDefault(pSession => pSession.SessionId ==
                                             _selectedSessionId &&
                                             pSession.IsCompleted);
            selected ??= _snapshot.Sessions.FirstOrDefault(
                pSession => pSession.IsCompleted);
            if (selected == null) return;
            if (_snapshot.SelectedSession?.SessionId == selected.SessionId)
                return;
            _selectedSessionId = selected.SessionId;
            _snapshot = CivilServiceExamReadModel.Load(_kingdomId,
                _selectedSessionId);
        }

        private void ChangeHistorySession(int pDelta)
        {
            List<CivilServiceExamSessionView> history = _snapshot?.Sessions?
                .Where(pSession => pSession.IsCompleted).ToList() ??
                new List<CivilServiceExamSessionView>();
            int index = history.FindIndex(pSession => pSession.SessionId ==
                                                       _selectedSessionId);
            int next = index + pDelta;
            if (index < 0 || next < 0 || next >= history.Count) return;
            _selectedSessionId = history[next].SessionId;
            Refresh();
        }

        private void UpdateHistoryNavigation()
        {
            if (_historyPrevious == null || _historyNext == null) return;
            List<CivilServiceExamSessionView> history = _snapshot?.Sessions?
                .Where(pSession => pSession.IsCompleted).ToList() ??
                new List<CivilServiceExamSessionView>();
            int index = history.FindIndex(pSession => pSession.SessionId ==
                                                       _selectedSessionId);
            _historyPrevious.interactable = index > 0;
            _historyNext.interactable = index >= 0 && index + 1 < history.Count;
        }

        private void EnsureRankingOrder()
        {
            CivilServiceExamSessionView session = _snapshot?.SelectedSession;
            if (session == null || !session.PlayerRankingPending ||
                session.Stage != "ranking")
            {
                _rankingSessionId = -1L;
                _rankingOrder.Clear();
                return;
            }
            if (_rankingSessionId == session.SessionId &&
                _rankingOrder.Count > 0) return;
            _rankingSessionId = session.SessionId;
            _rankingOrder.Clear();
            foreach (CivilServiceExamCandidateView candidate in
                     PalaceFinalists())
                _rankingOrder.Add(candidate.CandidateId);
        }

        private List<CivilServiceExamCandidateView> PalaceFinalists()
        {
            return _snapshot?.Candidates?
                .Where(candidate => candidate.Qualification == "jinshi" &&
                                    candidate.StageResult == "passed")
                .OrderByDescending(candidate => candidate.PalaceScore)
                .ThenBy(candidate => candidate.ActorId)
                .ToList() ?? new List<CivilServiceExamCandidateView>();
        }

        private void MoveRankingCandidate(long pCandidateId, int pDelta)
        {
            int index = _rankingOrder.IndexOf(pCandidateId);
            int next = index + pDelta;
            if (index < 0 || next < 0 || next >= _rankingOrder.Count) return;
            (_rankingOrder[index], _rankingOrder[next]) =
                (_rankingOrder[next], _rankingOrder[index]);
            RenderRows();
            UpdateFooter();
        }

        private void SubmitPalaceRanking()
        {
            CivilServiceExamSessionView session = _snapshot?.SelectedSession;
            if (session == null) return;
            int count = Math.Min(3, _rankingOrder.Count);
            long[] top = _rankingOrder.Take(count).ToArray();
            if (top.Length == 0) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.SubmitCivilServiceRanking(_kingdomId,
                        session.SessionId, top[0],
                        top.Length > 1 ? top[1] : -1L,
                        top.Length > 2 ? top[2] : -1L));
            _message.text = AW_L10n.Text(result.MessageKey,
                result.Accepted ? "Ranking confirmed" :
                "Ranking unavailable");
            if (result.Accepted)
            {
                _selectedSessionId = session.SessionId;
                Refresh();
            }
        }

        private void RenderRows()
        {
            _portraitQueue.Clear();
            List<CivilServiceExamCandidateView> candidates =
                VisibleCandidates();
            float width = CivilServiceExamRules.CandidateRowWidth(
                _windowSize.x, WindowMarginX, 14f);
            bool editable = CanEditRanking();
            for (int index = 0; index < candidates.Count; index++)
            {
                while (_rowPool.Count <= index)
                    _rowPool.Add(CivilServiceExamCandidateRow.Create(
                        _listContent));
                CivilServiceExamCandidateRow row = _rowPool[index];
                RectTransform rect = row.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(0f,
                    -index * (CivilServiceExamCandidateRow.Height + 2f));
                CivilServiceExamCandidateView candidate = candidates[index];
                string visibleResult = candidate.StageResult;
                if (TryResolveStageTab(_selectedTab,
                        out CivilServiceExamStage visibleStage))
                    visibleResult = CivilServiceExamRules.ResolveStageResult(
                        visibleStage, candidate.LocalResult,
                        candidate.MetropolitanResult,
                        candidate.PalaceResult, candidate.NationalResult,
                        candidate.StageResult);
                row.Bind(candidate, index + 1, width, visibleResult,
                    editable && index > 0,
                    editable && index + 1 < candidates.Count,
                    MoveRankingCandidate);
                if (row.NeedsPortrait) _portraitQueue.Enqueue(row);
            }
            for (int index = candidates.Count; index < _rowPool.Count;
                 index++)
                _rowPool[index].Unbind();
            if (_listContent != null)
                _listContent.sizeDelta = new Vector2(width,
                    Math.Max(_listViewport?.rect.height ?? 1f,
                        candidates.Count *
                        (CivilServiceExamCandidateRow.Height + 2f)));
            _empty.gameObject.SetActive(candidates.Count == 0);
        }

        private List<CivilServiceExamCandidateView> VisibleCandidates()
        {
            if (_snapshot?.Candidates == null)
                return new List<CivilServiceExamCandidateView>();
            if (CanEditRanking())
            {
                Dictionary<long, CivilServiceExamCandidateView> byId =
                    PalaceFinalists().ToDictionary(p => p.CandidateId);
                return _rankingOrder.Where(byId.ContainsKey)
                    .Select(id => byId[id]).ToList();
            }
            IEnumerable<CivilServiceExamCandidateView> query =
                _snapshot.Candidates;
            if (TryResolveStageTab(_selectedTab,
                    out CivilServiceExamStage stage))
                query = query.Where(p =>
                    CivilServiceExamRules.IsStageParticipant(stage,
                        p.LocalScore, p.MetropolitanScore, p.PalaceScore,
                        p.NationalScore));
            return query.ToList();
        }

        private static bool TryResolveStageTab(string pTabId,
            out CivilServiceExamStage pStage)
        {
            pStage = pTabId switch
            {
                "local" => CivilServiceExamStage.Local,
                "prefectural" => CivilServiceExamStage.Prefectural,
                "metropolitan" => CivilServiceExamStage.Metropolitan,
                "palace" => CivilServiceExamStage.Palace,
                "national" => CivilServiceExamStage.National,
                _ => CivilServiceExamStage.Scheduled
            };
            return pTabId == "local" || pTabId == "prefectural" ||
                   pTabId == "metropolitan" || pTabId == "palace" ||
                   pTabId == "national";
        }

        private bool CanEditRanking()
        {
            CivilServiceExamSessionView session = _snapshot?.SelectedSession;
            return session != null && session.Mode == "imperial_exam" &&
                   session.Stage == "ranking" &&
                   session.Status == "ranking_pending" &&
                   session.PlayerRankingPending &&
                   !AW3MultiplayerReplicaScope.IsReplicaSession;
        }

        private void UpdateFooter()
        {
            bool editable = CanEditRanking();
            _footer.gameObject.SetActive(editable ||
                                         !string.IsNullOrEmpty(_message.text));
            _submitRanking.gameObject.SetActive(editable);
            _submitRanking.interactable = editable && _rankingOrder.Count > 0;
            if (editable)
            {
                Dictionary<long, string> names = PalaceFinalists()
                    .ToDictionary(p => p.CandidateId,
                        p => p.ActorName ?? "");
                _rankingSummary.text = AW_L10n.Text(
                    "aw_civil_service_exam_current_top_three",
                    "Current top three") + ": " +
                    string.Join(" / ", _rankingOrder.Take(3)
                        .Where(names.ContainsKey).Select(id => names[id]));
            }
            else _rankingSummary.text = "";
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            _root = Panel(ContentTransform, "CivilServiceExamRoot",
                new Color(.035f, .032f, .027f, .98f));
            _header = Panel(_root, "Header",
                new Color(.09f, .075f, .05f, .98f));
            BuildFlag();
            _headerTitle = Text(_header, "Title", 12,
                TextAnchor.UpperLeft, Color.white);
            _headerBody = Text(_header, "Body", 9,
                TextAnchor.UpperLeft, new Color(.83f, .79f, .69f, 1f));
            _courtBack = Button(_header, "BackToCourt",
                AW_L10n.Text("aw_civil_service_exam_back_to_court",
                    "Back to Court"), BackToCourt, out _);
            _tabsRoot = Panel(_root, "StageTabs",
                new Color(.055f, .048f, .038f, .98f));
            _historyPrevious = IconButton(_tabsRoot, "HistoryPrevious",
                180f, () => ChangeHistorySession(-1),
                "aw_civil_service_exam_previous_session", "Previous sitting");
            _historyNext = IconButton(_tabsRoot, "HistoryNext", 0f,
                () => ChangeHistorySession(1),
                "aw_civil_service_exam_next_session", "Next sitting");
            BuildList();
            _footer = Panel(_root, "RankingFooter",
                new Color(.085f, .067f, .042f, .98f));
            _rankingSummary = Text(_footer, "RankingSummary", 8,
                TextAnchor.MiddleLeft, new Color(1f, .82f, .38f, 1f));
            _message = Text(_footer, "Message", 8,
                TextAnchor.MiddleCenter, new Color(.9f, .75f, .42f, 1f));
            _submitRanking = Button(_footer, "SubmitRanking",
                AW_L10n.Text("aw_civil_service_exam_confirm_ranking",
                    "Confirm Roll"), SubmitPalaceRanking,
                out _submitRankingText);
        }

        private void BuildFlag()
        {
            var flagObject = new GameObject("KingdomFlag",
                typeof(RectTransform), typeof(Image));
            flagObject.transform.SetParent(_header, false);
            _flagBackground = flagObject.GetComponent<Image>();
            _flagBackground.preserveAspect = true;
            var iconObject = new GameObject("Icon", typeof(RectTransform),
                typeof(Image));
            iconObject.transform.SetParent(flagObject.transform, false);
            _flagIcon = iconObject.GetComponent<Image>();
            _flagIcon.preserveAspect = true;
            Fill(_flagIcon.rectTransform, 4f);
        }

        private void BuildList()
        {
            _listRoot = Panel(_root, "CandidateList",
                new Color(.025f, .023f, .02f, .98f));
            _listScroll = _listRoot.gameObject.AddComponent<ScrollRect>();
            var viewportObject = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(_listRoot, false);
            _listViewport = viewportObject.GetComponent<RectTransform>();
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(.03f, .028f, .024f, .92f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;
            var contentObject = new GameObject("Content",
                typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            _listContent = contentObject.GetComponent<RectTransform>();
            _listContent.anchorMin = _listContent.anchorMax = new Vector2(0f, 1f);
            _listContent.pivot = new Vector2(0f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listScroll.viewport = _listViewport;
            _listScroll.content = _listContent;
            _listScroll.horizontal = false;
            _listScroll.vertical = true;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScrollbar = CreateScrollbar(_listRoot, _listScroll);
            _empty = Text(_listViewport, "Empty", 10,
                TextAnchor.MiddleCenter, new Color(.68f, .65f, .58f, 1f));
            _empty.text = AW_L10n.Text("aw_civil_service_exam_no_candidates",
                "No candidates in this view");
        }

        private void ApplyLayout()
        {
            float contentWidth = Mathf.Max(1f,
                _windowSize.x - WindowMarginX);
            float contentHeight = Mathf.Max(1f,
                _windowSize.y - WindowMarginY);
            RectTransform background =
                BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            Transform title = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = title?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * .52f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow nativeWindow = GetComponent<ScrollWindow>();
            if (nativeWindow?.titleText != null)
                nativeWindow.titleText.transform.localPosition = new Vector3(
                    0f, _windowSize.y * .5f - 16f, 0f);
            RectTransform nativeScroll = BackgroundTransform?
                .Find("Scroll View")?.GetComponent<RectTransform>();
            if (nativeScroll != null)
            {
                nativeScroll.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
                nativeScroll.localPosition = new Vector3(0f, -20f, 0f);
                ScrollRect component = nativeScroll.GetComponent<ScrollRect>();
                if (component != null)
                {
                    component.horizontal = false;
                    component.vertical = false;
                }
            }
            Transform nativeBar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (nativeBar != null)
                foreach (Graphic graphic in
                         nativeBar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            RectTransform nativeViewport = ContentTransform?.parent as
                RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
            if (_root == null) return;
            Place(_root, 0f, 0f, contentWidth, contentHeight);
            Place(_header, 0f, 0f, contentWidth, HeaderHeight);
            Place(_flagBackground.rectTransform, 7f, 8f, 42f, 42f);
            Place(_headerTitle.rectTransform, 56f, 6f,
                contentWidth - 150f, 20f);
            Place(_headerBody.rectTransform, 56f, 28f,
                contentWidth - 64f, 32f);
            Place(_courtBack.GetComponent<RectTransform>(),
                contentWidth - 88f, 5f, 80f, 22f);
            Place(_tabsRoot, 0f, HeaderHeight, contentWidth, TabsHeight);
            Place(_historyPrevious.GetComponent<RectTransform>(),
                contentWidth - 50f, 3f, 21f, 21f);
            Place(_historyNext.GetComponent<RectTransform>(),
                contentWidth - 25f, 3f, 21f, 21f);
            float footer = CanEditRanking() ||
                           !string.IsNullOrEmpty(_message?.text)
                ? FooterHeight
                : 0f;
            float listHeight = Math.Max(60f, contentHeight - HeaderHeight -
                TabsHeight - footer);
            Place(_listRoot, 0f, HeaderHeight + TabsHeight,
                contentWidth, listHeight);
            Fill(_listViewport, 0f, 12f, 0f, 0f);
            Place(_listScrollbar.GetComponent<RectTransform>(),
                contentWidth - 11f, 2f, 9f, listHeight - 4f);
            Fill(_empty.rectTransform, 8f);
            Place(_footer, 0f, contentHeight - FooterHeight,
                contentWidth, FooterHeight);
            Place(_rankingSummary.rectTransform, 7f, 3f,
                contentWidth - 230f, 28f);
            Place(_message.rectTransform, contentWidth - 218f, 3f,
                118f, 28f);
            Place(_submitRanking.GetComponent<RectTransform>(),
                contentWidth - 96f, 5f, 88f, 24f);
            LayoutTabs(_tabs.Count(p => p.Button.gameObject.activeSelf));
            _chrome?.RepositionResizeHandle();
        }

        private void LayoutTabs(int pCount)
        {
            if (_tabsRoot == null || pCount <= 0) return;
            float available = Math.Max(120f, _tabsRoot.rect.width - 58f);
            float width = Mathf.Clamp(available / pCount, 58f, 104f);
            for (int index = 0; index < _tabs.Count; index++)
            {
                if (!_tabs[index].Button.gameObject.activeSelf) continue;
                Place(_tabs[index].Button.GetComponent<RectTransform>(),
                    4f + index * (width + 3f), 3f, width, 21f);
            }
        }

        private TabControl CreateTab()
        {
            Button button = Button(_tabsRoot, "StageTab", "", null,
                out Text label);
            return new TabControl { Button = button, Label = label };
        }

        private void UpdateFlag(Kingdom pKingdom)
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

        private void BackToCourt()
        {
            CourtWindow.Open(_kingdomId);
        }

        private static RectTransform Panel(Transform pParent, string pName,
            Color pColor)
        {
            var panel = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(pParent, false);
            panel.GetComponent<Image>().color = pColor;
            return panel.GetComponent<RectTransform>();
        }

        private static Text Text(Transform pParent, string pName, int pSize,
            TextAnchor pAnchor, Color pColor)
        {
            var textObject = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(pParent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = pColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static Button Button(Transform pParent, string pName,
            string pLabel, Action pAction, out Text pText)
        {
            var buttonObject = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(buttonObject.GetComponent<Image>(), .96f);
            Button button = buttonObject.GetComponent<Button>();
            if (pAction != null)
                button.onClick.AddListener(() => pAction());
            pText = Text(buttonObject.transform, "Text", 8,
                TextAnchor.MiddleCenter, Color.white);
            Fill(pText.rectTransform, 3f);
            pText.text = pLabel ?? "";
            return button;
        }

        private static Button IconButton(Transform pParent, string pName,
            float pRotation, Action pAction, string pTipKey,
            string pTipFallback)
        {
            var buttonObject = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            buttonObject.transform.SetParent(pParent, false);
            Image image = buttonObject.GetComponent<Image>();
            AW_UIStyle.ApplyButton(image, .96f);
            image.sprite = SpriteTextureLoader.getSprite(
                "ui/icons/iconArrowMetaRight");
            image.preserveAspect = true;
            image.transform.localRotation = Quaternion.Euler(0f, 0f,
                pRotation);
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => pAction?.Invoke());
            TipButton tip = buttonObject.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(buttonObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = AW_L10n.Text(pTipKey, pTipFallback),
                    tip_description = AW_L10n.Text(
                        "aw_civil_service_exam_history_navigation_desc",
                        "Browse completed examination rolls.")
                });
            return button;
        }

        private static Scrollbar CreateScrollbar(Transform pParent,
            ScrollRect pScroll)
        {
            var barObject = new GameObject("Scrollbar Vertical",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(pParent, false);
            barObject.GetComponent<Image>().color =
                new Color(.08f, .075f, .065f, .98f);
            var slidingObject = new GameObject("Sliding Area",
                typeof(RectTransform));
            slidingObject.transform.SetParent(barObject.transform, false);
            Fill(slidingObject.GetComponent<RectTransform>(), 1f);
            var handleObject = new GameObject("Handle",
                typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(slidingObject.transform, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            Fill(handle, 0f);
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(.76f, .61f, .28f, 1f);
            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static void Place(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth),
                Mathf.Max(1f, pHeight));
        }

        private static void Fill(RectTransform pRect, float pInset)
        {
            Fill(pRect, pInset, pInset, pInset, pInset);
        }

        private static void Fill(RectTransform pRect, float pLeft,
            float pRight, float pTop, float pBottom)
        {
            pRect.anchorMin = Vector2.zero;
            pRect.anchorMax = Vector2.one;
            pRect.offsetMin = new Vector2(pLeft, pBottom);
            pRect.offsetMax = new Vector2(-pRight, -pTop);
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            try { return pKingdom?.getColor()?.getColorText() ?? Color.white; }
            catch { return Color.white; }
        }

        private static string TabLabel(string pId)
        {
            return pId switch
            {
                AllTabId => AW_L10n.Text(
                    "aw_civil_service_exam_tab_all", "Current Roll"),
                HistoryTabId => AW_L10n.Text(
                    "aw_civil_service_exam_history", "Past Rolls"),
                _ => LocalizedStage(pId)
            };
        }

        private static string LocalizedStage(string pStage)
        {
            return AW_L10n.Text("aw_civil_service_stage_" +
                               (pStage ?? "scheduled"),
                pStage ?? "scheduled");
        }

        private static string FormatWorldDay(long pWorldDay)
        {
            if (pWorldDay < 0L) return "-";
            long year = pWorldDay / 360L + 1L;
            long day = pWorldDay % 360L + 1L;
            return string.Format(AW_L10n.Text(
                "aw_civil_service_exam_world_date",
                "World year {0}, day {1}"), year, day);
        }
    }
}
