using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
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

        private readonly List<CourtAppointmentCandidateView> _candidateResults =
            new List<CourtAppointmentCandidateView>();
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
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        internal static void Appoint(long pActorId)
        {
            CourtManualAppointmentResult result = CourtService.TryManualAppointment(
                _kingdomId, _officeId, pActorId, _expectedIncumbentActorId);
            if (result == CourtManualAppointmentResult.Success)
            {
                _feedback = null;
                CourtWindow.Open(_kingdomId);
                return;
            }

            _feedback = result;
            Instance?.Refresh();
        }

        public void Refresh()
        {
            ResetCandidateWork();
            ClearList();
            ApplyTitle();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                AddMessage(ResultText(CourtManualAppointmentResult.InvalidKingdom), "",
                    pError: true);
                return;
            }

            string officeName = OfficeName(_officeId);
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
            if (_candidateScanRunning) ProcessCandidateScanFrame();
            else if (_candidateRenderRunning) ProcessCandidateRowsFrame();
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
            _candidateResults.Sort((left, right) =>
                CourtManualAppointmentRules.CompareCandidates(
                    left.score, left.actor_id, right.score, right.actor_id));
            BeginCandidatePage(0);
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
                    school_text = SchoolText(candidate.school_id)
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
                : " - " + OfficeName(_officeId));
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

        private static string OfficeName(string pOfficeId)
        {
            return AW_L10n.Text("aw_court_office_" + (pOfficeId ?? ""),
                pOfficeId ?? "");
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
