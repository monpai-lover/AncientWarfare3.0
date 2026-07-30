using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.uiquery;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CourtAppointmentWindow :
        AbstractListWindow<CourtAppointmentWindow, CourtAppointmentCandidateRow>
    {
        private static long _kingdomId = -1L;
        private static string _officeId = "";
        private static long _expectedIncumbentActorId = -1L;
        private static CourtManualAppointmentResult? _feedback;
        private static bool _commandPending;

        private readonly List<CourtAppointmentCandidateView> _candidateResults =
            new List<CourtAppointmentCandidateView>();
        private readonly AWUiQueryState _queryState =
            new AWUiQueryState(AW_LineageWindowIds.COURT_APPOINTMENT);
        private AWUiQueryKey _candidateQueryKey;
        private CourtAppointmentCandidateScan _candidateScan;
        private int _candidateScanCursor;
        private bool _candidateScanRunning;
        private int _candidatePage;
        private int _candidateRenderCursor;
        private int _candidateRenderEnd;
        private bool _candidateRenderRunning;
        private string _contextTitle = "";
        private string _contextBody = "";
        private CourtManualAppointmentResult? _visibleFeedback;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId, string pOfficeId)
        {
            Open(pKingdomId, pOfficeId, -1L);
        }

        public static void Open(long pKingdomId, string pOfficeId,
            long pExpectedIncumbentActorId)
        {
            _kingdomId = pKingdomId;
            _officeId = pOfficeId ?? "";
            _expectedIncumbentActorId = pExpectedIncumbentActorId;
            _feedback = null;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.COURT_APPOINTMENT);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.COURT_APPOINTMENT,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            ApplyTitle();
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

        public override void OnNormalDisable()
        {
            _queryState.Close();
            ResetCandidateWork();
        }

        internal static void Appoint(long pActorId)
        {
            if (_commandPending) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.AppointCourtOfficer(_kingdomId,
                        pActorId, _officeId,
                        _expectedIncumbentActorId));
            if (result.Status == AW3CommandStatus.Accepted)
            {
                _feedback = null;
                CourtWindow.OpenAndRefresh(_kingdomId);
                return;
            }
            if (result.Status == AW3CommandStatus.Pending)
            {
                _commandPending = true;
                Instance?.Refresh();
                return;
            }

            _feedback = Enum.IsDefined(typeof(CourtManualAppointmentResult),
                    result.DetailCode)
                ? (CourtManualAppointmentResult)result.DetailCode
                : CourtManualAppointmentResult.PersistenceFailed;
            Instance?.Refresh();
        }

        public void Refresh()
        {
            ResetCandidateWork();
            _candidateQueryKey = _queryState.Begin(_kingdomId,
                _officeId + ":" + _expectedIncumbentActorId,
                KingdomStrategyRevisionService.Current(_kingdomId));
            ClearList();
            ApplyTitle();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                AddMessage(ResultText(CourtManualAppointmentResult.InvalidKingdom), "",
                    pError: true);
                return;
            }

            string officeName = OfficeName(kingdom, _officeId);
            _contextTitle = kingdom.name + " - " + officeName;
            _contextBody = AW_L10n.Text("aw_court_appointment_instruction",
                "Choose any eligible adult. School is not a requirement.");

            CourtManualAppointmentResult target =
                CourtService.BeginManualAppointmentScan(kingdom, _officeId,
                    _expectedIncumbentActorId, out _candidateScan);
            _visibleFeedback = _feedback ??
                (target == CourtManualAppointmentResult.Success
                    ? (CourtManualAppointmentResult?)null
                    : target);
            AddContextRows();
            if (_commandPending)
            {
                AddMessage(AW_L10n.Text("aw3_command_pending",
                    "Waiting for host"), "");
                return;
            }
            if (target != CourtManualAppointmentResult.Success) return;

            _candidateScanRunning = true;
            AddMessage(string.Format(AW_L10n.Text(
                    "aw_court_appointment_loading",
                    "Reviewing {0} subjects..."),
                _candidateScan.actor_ids.Count), "");
            if (_candidateScan.actor_ids.Count == 0) CompleteCandidateScan();
        }

        private void Update()
        {
            if (_commandRefreshRequested)
            {
                _commandRefreshRequested = false;
                _commandPending = false;
                _feedback = null;
                if (isActiveAndEnabled) Refresh();
                return;
            }
            if (_candidateScanRunning) ProcessCandidateScanFrame();
            else if (_candidateRenderRunning) ProcessCandidateRowsFrame();
        }

        private void OnCommandStateChanged()
        {
            if (_commandPending) _commandRefreshRequested = true;
        }

        private void ProcessCandidateScanFrame()
        {
            if (_candidateScan == null)
            {
                _candidateScanRunning = false;
                return;
            }

            long started = Stopwatch.GetTimestamp();
            int processed = 0;
            while (_candidateScanCursor < _candidateScan.actor_ids.Count &&
                   processed < CourtManualAppointmentRules.CandidateScanPerFrame)
            {
                if (CourtService.TryProjectManualAppointmentCandidate(
                        _candidateScan, _candidateScanCursor,
                        out CourtAppointmentCandidateView candidate))
                    _candidateResults.Add(candidate);
                _candidateScanCursor++;
                processed++;
                if (ElapsedMilliseconds(started) >=
                    CourtManualAppointmentRules.CandidateFrameBudgetMilliseconds) break;
            }

            if (_candidateScanCursor >= _candidateScan.actor_ids.Count)
                CompleteCandidateScan();
        }

        private void CompleteCandidateScan()
        {
            _candidateScanRunning = false;
            if (AWAsyncRuntime.ShadowEnabled)
            {
                ScheduleCandidateSort(pShadow: true);
                SortCandidatesSynchronously();
                return;
            }
            if (AWAsyncRuntime.UiEnabled)
            {
                ScheduleCandidateSort(pShadow: false);
                return;
            }
            SortCandidatesSynchronously();
        }

        private void SortCandidatesSynchronously()
        {
            _candidateResults.Sort((left, right) =>
                CourtManualAppointmentRules.CompareCandidates(
                    left.score, left.actor_id, right.score, right.actor_id));
            BeginCandidatePage(0);
        }

        private void ScheduleCandidateSort(bool pShadow)
        {
            var rows = new AWUiCandidateRow[_candidateResults.Count];
            for (int index = 0; index < _candidateResults.Count; index++)
            {
                CourtAppointmentCandidateView candidate =
                    _candidateResults[index];
                rows[index] = new AWUiCandidateRow(candidate.actor_id,
                    candidate.score, 0d, candidate.actor_name);
            }
            var execution = new AWUiCandidateRankExecution(rows);
            long[] expectedShadow = pShadow
                ? BuildSynchronousCandidateIds()
                : null;
            var commit = new CourtCandidateSortCommit(this,
                _candidateQueryKey, expectedShadow);
            var request = new AWAsyncWorkRequest(
                "ui:court-appointment:" + _kingdomId,
                AWAsyncLane.Ui,
                new AWAsyncStamp(AWAsyncRuntime.WorldGeneration,
                    Time.frameCount, _candidateQueryKey.Revision),
                execution.Execute, commit.Commit);
            if (!AWAsyncRuntime.TrySchedule(request))
            {
                AWUiCandidateRow[] result = execution.Execute(
                    System.Threading.CancellationToken.None) as
                    AWUiCandidateRow[];
                if (pShadow)
                    CompareCandidateShadow(_candidateQueryKey,
                        expectedShadow, result);
                else
                    ApplyCandidateResult(_candidateQueryKey, result);
            }
        }

        private long[] BuildSynchronousCandidateIds()
        {
            var candidates = new List<CourtAppointmentCandidateView>(
                _candidateResults);
            candidates.Sort((left, right) =>
                CourtManualAppointmentRules.CompareCandidates(
                    left.score, left.actor_id, right.score, right.actor_id));
            var result = new long[candidates.Count];
            for (int index = 0; index < candidates.Count; index++)
                result[index] = candidates[index].actor_id;
            return result;
        }

        private void CompareCandidateShadow(AWUiQueryKey pKey,
            IReadOnlyList<long> pExpected, AWUiCandidateRow[] pRanked)
        {
            if (!AcceptCandidateResult(pKey)) return;
            var actual = new long[pRanked?.Length ?? 0];
            for (int index = 0; index < actual.Length; index++)
                actual[index] = pRanked[index].ActorId;
            AWAsyncShadowRuntime.CompareIds("ui_court",
                "court:" + _kingdomId + ":" + _officeId,
                pExpected, actual);
        }

        private void ApplyCandidateResult(AWUiQueryKey pKey,
            AWUiCandidateRow[] pRanked)
        {
            if (!AcceptCandidateResult(pKey) || pRanked == null) return;
            var byActor = new Dictionary<long,
                CourtAppointmentCandidateView>();
            foreach (CourtAppointmentCandidateView candidate in
                     _candidateResults)
                if (candidate != null) byActor[candidate.actor_id] = candidate;
            _candidateResults.Clear();
            foreach (AWUiCandidateRow row in pRanked)
                if (byActor.TryGetValue(row.ActorId,
                        out CourtAppointmentCandidateView candidate))
                    _candidateResults.Add(candidate);
            BeginCandidatePage(0);
        }

        private bool AcceptCandidateResult(AWUiQueryKey pKey)
        {
            if (!_queryState.Accept(pKey)) return false;
            if (_queryState.Accept(pKey,
                KingdomStrategyRevisionService.Current(_kingdomId))) return true;
            if (isActiveAndEnabled) Refresh();
            return false;
        }

        private sealed class CourtCandidateSortCommit
        {
            private readonly CourtAppointmentWindow _owner;
            private readonly AWUiQueryKey _key;
            private readonly long[] _expectedShadow;

            public CourtCandidateSortCommit(CourtAppointmentWindow pOwner,
                AWUiQueryKey pKey, long[] pExpectedShadow)
            {
                _owner = pOwner;
                _key = pKey;
                _expectedShadow = pExpectedShadow;
            }

            public void Commit(object pResult)
            {
                AWUiCandidateRow[] result = pResult as AWUiCandidateRow[];
                if (_expectedShadow != null)
                    _owner.CompareCandidateShadow(_key, _expectedShadow,
                        result);
                else
                    _owner.ApplyCandidateResult(_key, result);
            }
        }

        internal static void ChangePage(int pDelta)
        {
            if (Instance == null || pDelta == 0) return;
            Instance.BeginCandidatePage(Instance._candidatePage + pDelta);
        }

        private void BeginCandidatePage(int pPage)
        {
            _candidateRenderRunning = false;
            int pageCount = CourtManualAppointmentRules.PageCount(
                _candidateResults.Count);
            _candidatePage = Math.Max(0, Math.Min(pageCount - 1, pPage));
            ClearList();
            AddContextRows();

            if (_candidateResults.Count == 0)
            {
                AddMessage(AW_L10n.Text("aw_court_appointment_empty",
                    "No eligible actors are available."), "", pError: false);
                return;
            }

            AddMessage(string.Format(AW_L10n.Text("aw_court_appointment_page",
                    "Page {0}/{1} - {2} eligible"),
                _candidatePage + 1, pageCount, _candidateResults.Count), "");
            _candidateRenderCursor =
                _candidatePage * CourtManualAppointmentRules.CandidatePageSize;
            _candidateRenderEnd = Math.Min(_candidateResults.Count,
                _candidateRenderCursor +
                CourtManualAppointmentRules.CandidatePageSize);
            _candidateRenderRunning = _candidateRenderCursor < _candidateRenderEnd;
            if (!_candidateRenderRunning) AddNavigationRows();
        }

        private void ProcessCandidateRowsFrame()
        {
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                _candidateRenderRunning = false;
                Refresh();
                return;
            }

            int added = 0;
            while (_candidateRenderCursor < _candidateRenderEnd &&
                   added < CourtManualAppointmentRules.CandidateRowsPerFrame)
            {
                CourtAppointmentCandidateView candidate =
                    _candidateResults[_candidateRenderCursor++];
                AddItemToList(new CourtAppointmentCandidateRow
                {
                    candidate = candidate,
                    role_text = RoleText(candidate, kingdom),
                    school_text = SchoolText(candidate.school_id),
                    grade_text = CandidateGradeText(candidate)
                });
                added++;
            }

            if (_candidateRenderCursor < _candidateRenderEnd) return;
            _candidateRenderRunning = false;
            AddNavigationRows();
        }

        private void AddNavigationRows()
        {
            int pageCount = CourtManualAppointmentRules.PageCount(
                _candidateResults.Count);
            if (_candidatePage > 0)
                AddNavigationRow(CourtAppointmentNavigationAction.Previous,
                    "aw_court_appointment_previous", "Previous page");
            if (_candidatePage + 1 < pageCount)
                AddNavigationRow(CourtAppointmentNavigationAction.Next,
                    "aw_court_appointment_next", "Next page");
        }

        private void AddNavigationRow(CourtAppointmentNavigationAction pAction,
            string pKey, string pFallback)
        {
            AddItemToList(new CourtAppointmentCandidateRow
            {
                navigation_action = pAction,
                message_title = AW_L10n.Text(pKey, pFallback)
            });
        }

        private void AddContextRows()
        {
            AddMessage(_contextTitle, _contextBody, pHeader: true);
            if (_visibleFeedback.HasValue)
                AddMessage(ResultText(_visibleFeedback.Value), "", pError: true);
        }

        private void ResetCandidateWork()
        {
            _candidateScan = null;
            _candidateResults.Clear();
            _candidateScanCursor = 0;
            _candidateScanRunning = false;
            _candidatePage = 0;
            _candidateRenderCursor = 0;
            _candidateRenderEnd = 0;
            _candidateRenderRunning = false;
            _contextTitle = "";
            _contextBody = "";
            _visibleFeedback = null;
        }

        private static double ElapsedMilliseconds(long pStarted)
        {
            return (Stopwatch.GetTimestamp() - pStarted) * 1000d /
                   Stopwatch.Frequency;
        }

        private void ApplyTitle()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText == null) return;
            string titleKey = _expectedIncumbentActorId >= 0
                ? "aw_court_replacement_title"
                : "aw_court_appointment_title";
            string titleFallback = _expectedIncumbentActorId >= 0
                ? "Replace Officer"
                : "Appoint Officer";
            window.titleText.text = AW_L10n.Text(titleKey, titleFallback) +
                (string.IsNullOrEmpty(_officeId)
                ? ""
                : " - " + OfficeName(
                    World.world?.kingdoms?.get(_kingdomId), _officeId));
        }

        private void AddMessage(string pTitle, string pBody, bool pHeader = false,
            bool pError = false)
        {
            AddItemToList(new CourtAppointmentCandidateRow
            {
                is_message = true,
                is_header = pHeader,
                is_error = pError,
                message_title = pTitle ?? "",
                message_body = pBody ?? ""
            });
        }

        private static string RoleText(CourtAppointmentCandidateView pCandidate,
            Kingdom pKingdom)
        {
            var roles = new List<string>();
            if (pCandidate.is_heir)
                roles.Add(AW_L10n.Text(GovernmentTitleRules.SuccessorKey(
                    RepublicGovernmentService.IsRepublic(pKingdom),
                    KingdomTitleService.IsEmperor(pKingdom) ||
                    MandateService.GetCurrentMandateKingdom() == pKingdom), "Heir"));
            if (pCandidate.is_city_leader)
                roles.Add(AW_L10n.Text("aw_court_candidate_city_leader", "City Leader"));
            if (pCandidate.is_general)
                roles.Add(AW_L10n.Text("aw_court_general", "General"));
            if (roles.Count == 0)
                roles.Add(AW_L10n.Text("aw_court_candidate_subject", "Subject"));
            return string.Join(" / ", roles.ToArray());
        }

        private static string SchoolText(string pSchoolId)
        {
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(pSchoolId);
            return definition == null
                ? AW_L10n.Text("aw_court_school_none", "No school")
                : AW_L10n.Text(definition.NameKey, definition.Id);
        }

        private static string CandidateGradeText(
            CourtAppointmentCandidateView pCandidate)
        {
            if (pCandidate == null) return "";
            var values = new List<string>();
            if (pCandidate.official_rank > 0)
                values.Add(AW_L10n.Text("aw_court_official_rank", "Official rank") +
                           ": " + AW_L10n.Text(
                               OfficialCareerRankRules.RankNameKey(
                                   pCandidate.official_rank),
                               OfficialCareerRankRules.RankFallbackEnglish(
                                   pCandidate.official_rank)));
            if (pCandidate.local_grade > 0)
                values.Add(AW_L10n.Text("aw_court_local_grade", "Local grade") +
                           ": " + AW_L10n.Text(
                               NineRankRules.GradeNameKey(pCandidate.local_grade),
                               NineRankRules.GradeFallbackEnglish(
                                   pCandidate.local_grade)));
            return string.Join(" / ", values.ToArray());
        }

        private static string OfficeName(Kingdom pKingdom, string pOfficeId)
        {
            return CourtInstitutionService.OfficeName(pKingdom, pOfficeId);
        }

        private static string ResultText(CourtManualAppointmentResult pResult)
        {
            switch (pResult)
            {
                case CourtManualAppointmentResult.InvalidKingdom:
                    return AW_L10n.Text("aw_court_appointment_invalid_kingdom",
                        "The kingdom no longer exists.");
                case CourtManualAppointmentResult.InvalidOffice:
                    return AW_L10n.Text("aw_court_appointment_invalid_office",
                        "This office is no longer part of the current court.");
                case CourtManualAppointmentResult.OfficeOccupied:
                    return AW_L10n.Text("aw_court_appointment_occupied",
                        "This office has already been filled.");
                case CourtManualAppointmentResult.OfficeChanged:
                    return AW_L10n.Text("aw_court_appointment_office_changed",
                        "The incumbent has changed. Reopen the office list.");
                case CourtManualAppointmentResult.InvalidActor:
                    return AW_L10n.Text("aw_court_appointment_invalid_actor",
                        "This actor is no longer available.");
                case CourtManualAppointmentResult.CandidateIneligible:
                    return AW_L10n.Text("aw_court_appointment_ineligible",
                        "This actor no longer meets the office requirements.");
                case CourtManualAppointmentResult.PersistenceFailed:
                    return AW_L10n.Text("aw_court_appointment_failed",
                        "The appointment could not be committed.");
                default:
                    return AW_L10n.Text("aw_court_appointment_success",
                        "Appointment completed.");
            }
        }

        protected override AbstractListWindowItem<CourtAppointmentCandidateRow>
            CreateItemPrefab()
        {
            var obj = new GameObject("CourtAppointmentCandidateListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<CourtAppointmentCandidateListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
