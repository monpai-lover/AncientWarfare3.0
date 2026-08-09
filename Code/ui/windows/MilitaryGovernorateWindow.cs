using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    internal sealed class MilitaryGovernorateWindow :
        AbstractListWindow<MilitaryGovernorateWindow,
            WarDecisionTargetRow>
    {
        private enum WindowMode
        {
            Creation,
            Successor,
            Replacement
        }

        private static long _seatCityId = -1;
        private static long _suzerainKingdomId = -1;
        private static long _subjectKingdomId = -1;
        private static WindowMode _mode;
        private static bool _pendingReplacement;
        private static float _pendingReplacementSince;
        private bool _commandRefreshRequested;
        private static string _feedbackKey = "";
        private static bool _feedbackError;
        private bool _subscribed;

        public static void OpenCreation(City pCity)
        {
            ResetPendingContext();
            _mode = WindowMode.Creation;
            _seatCityId = pCity?.id ?? -1L;
            _suzerainKingdomId = -1L;
            _subjectKingdomId = -1L;
            _feedbackKey = "";
            _feedbackError = false;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.MILITARY_GOVERNORATE);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.MILITARY_GOVERNORATE,
                () => Instance?.Refresh());
        }

        public static void OpenSuccessorSelection(Kingdom pSuzerain,
            Kingdom pSubject)
        {
            OpenManagement(pSuzerain, pSubject, WindowMode.Successor);
        }

        public static void OpenGovernorReplacement(Kingdom pSuzerain,
            Kingdom pSubject)
        {
            OpenManagement(pSuzerain, pSubject, WindowMode.Replacement);
        }

        private static void OpenManagement(Kingdom pSuzerain,
            Kingdom pSubject, WindowMode pMode)
        {
            ResetPendingContext();
            _mode = pMode;
            _seatCityId = -1L;
            _suzerainKingdomId = pSuzerain?.id ?? -1L;
            _subjectKingdomId = pSubject?.id ?? -1L;
            _feedbackKey = "";
            _feedbackError = false;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.MILITARY_GOVERNORATE);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.MILITARY_GOVERNORATE,
                () => Instance?.Refresh());
        }

        private static void ResetPendingContext()
        {
            _pendingReplacement = false;
            _pendingReplacementSince = 0f;
            if (Instance != null) Instance._commandRefreshRequested = false;
        }

        protected override void Init()
        {
            Subscribe();
        }

        public override void OnNormalEnable()
        {
            Subscribe();
            Refresh();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (!_subscribed) return;
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
            _subscribed = false;
        }

        private void Refresh()
        {
            ClearList();
            if (_mode != WindowMode.Creation)
            {
                RefreshManagementCandidates();
                return;
            }
            City city = FindCity(_seatCityId);
            if (!string.IsNullOrEmpty(_feedbackKey))
            {
                AddMessage(AW_L10n.Text(_feedbackKey, _feedbackKey),
                    _feedbackError);
                if (!_feedbackError) return;
            }
            if (!MilitaryGovernorateCreationService.CanSelectSeat(city,
                    out string reason))
            {
                AddMessage(AW_L10n.Text(
                    "aw_military_governorate_failure_" + reason,
                    AW_L10n.Text("aw_military_governorate_failure",
                        "This city cannot become a military command.")),
                    true);
                return;
            }

            AddItemToList(new WarDecisionTargetRow
            {
                is_header = true,
                text = AW_L10n.Text("aw_military_governorate_choose_general",
                    "Choose a general for " + (city.data.name ?? ""))
            });
            List<MilitaryGovernorateGeneralCandidate> candidates =
                MilitaryGovernorateCreationService.GetGeneralCandidates(
                    city.kingdom,
                    MilitaryGovernorateRules.GeneralScanBudget);
            if (candidates.Count == 0)
            {
                AddMessage(AW_L10n.Text(
                    "aw_military_governorate_no_general",
                    "No eligible active general is available."), true);
                return;
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                MilitaryGovernorateGeneralCandidate candidate = candidates[i];
                Actor actor = candidate.Actor;
                long actorId = actor.getID();
                int merit = GeneralService.GetMerit(actor);
                int loyalty = GeneralService.GetLoyalty(actor);
                int ambition = GeneralService.GetAmbition(actor);
                AddItemToList(new WarDecisionTargetRow
                {
                    actor_id = actorId,
                    text = actor.getName(),
                    stats = string.Format(AW_L10n.Text(
                            "aw_military_governorate_general_stats_command",
                            "Merit {0}  Loyalty {1}  Ambition {2}  {3}"),
                        merit, loyalty, ambition, SafeCommand(actor)),
                    tooltip_title = actor.getName(),
                    tooltip_desc = AW_L10n.Text(
                        "aw_military_governorate_appoint_desc",
                        "Appoint this general to command the frontier."),
                    button_text = AW_L10n.Text(
                        "aw_military_governorate_appoint", "Appoint"),
                    icon_path = "ui/icons/iconKings",
                    enabled = true,
                    sort_order = i,
                    sort_name = actor.getName(),
                    action = () => Create(actorId)
                });
            }
        }

        private void RefreshManagementCandidates()
        {
            Kingdom suzerain = FindKingdom(_suzerainKingdomId);
            Kingdom subject = FindKingdom(_subjectKingdomId);
            if (suzerain?.data == null || subject?.data == null ||
                VassalService.GetSuzerain(subject) != suzerain ||
                VassalService.GetSubjectKind(subject) !=
                    VassalSubjectKind.MilitaryGovernorate)
            {
                AddMessage(AW_L10n.Text(
                    "aw_military_governorate_failure_invalid_governorate",
                    "Invalid military governorate."), true);
                return;
            }
            if (!string.IsNullOrEmpty(_feedbackKey))
            {
                AddMessage(AW_L10n.Text(_feedbackKey, _feedbackKey),
                    _feedbackError);
                if (!_feedbackError) return;
            }

            string headerKey = _mode == WindowMode.Successor
                ? "aw_military_governorate_designate_successor"
                : "aw_military_governorate_replace_governor";
            AddItemToList(new WarDecisionTargetRow
            {
                is_header = true,
                text = AW_L10n.Text(headerKey, headerKey)
            });

            List<GeneralReadModelEntry> candidates =
                GetManagementCandidates(subject, suzerain);
            if (candidates.Count == 0)
            {
                AddMessage(AW_L10n.Text(
                    "aw_military_governorate_no_general",
                    "No eligible active general is available."), true);
                return;
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                GeneralReadModelEntry entry = candidates[i];
                Actor actor = entry.Actor;
                if (actor?.data == null) continue;
                long actorId = actor.data.id;
                AddItemToList(new WarDecisionTargetRow
                {
                    actor_id = actorId,
                    text = actor.getName(),
                    stats = string.Format(AW_L10n.Text(
                            "aw_military_governorate_general_stats_command",
                            "Merit {0}  Loyalty {1}  Ambition {2}  {3}"),
                        entry.Merit, entry.Loyalty, entry.Ambition,
                        SafeCommand(actor)),
                    tooltip_title = actor.getName(),
                    tooltip_desc = AW_L10n.Text(headerKey, headerKey),
                    button_text = AW_L10n.Text(
                        "aw_military_governorate_confirm", "Confirm"),
                    enabled = true,
                    sort_order = i,
                    sort_name = actor.getName(),
                    action = () => DispatchManagement(actorId)
                });
            }
        }

        private static List<GeneralReadModelEntry> GetManagementCandidates(
            Kingdom pSubject, Kingdom pSuzerain)
        {
            int budget = MilitaryGovernorateRules.GeneralScanBudget;
            int localBudget = Math.Min(16, budget);
            List<GeneralReadModelEntry> result =
                GeneralService.GetActiveGeneralsForReadModel(pSubject,
                    pAllowUnitFallback: false, pLimit: localBudget);
            int remaining = budget - result.Count;
            if (remaining <= 0 || pSuzerain == pSubject) return result;
            List<GeneralReadModelEntry> parent =
                GeneralService.GetActiveGeneralsForReadModel(pSuzerain,
                    pAllowUnitFallback: false, pLimit: remaining);
            var seen = new HashSet<long>();
            for (int i = 0; i < result.Count; i++)
                if (result[i]?.Actor?.data != null)
                    seen.Add(result[i].Actor.data.id);
            for (int i = 0; i < parent.Count && result.Count < budget; i++)
            {
                GeneralReadModelEntry entry = parent[i];
                if (entry?.Actor?.data != null &&
                    seen.Add(entry.Actor.data.id)) result.Add(entry);
            }
            return result;
        }

        private static string SafeCommand(Actor pActor)
        {
            try
            {
                return pActor?.hasArmy() == true
                    ? AW_L10n.Text(
                        "aw_military_governorate_command_active", "Commanding")
                    : AW_L10n.Text(
                        "aw_military_governorate_command_idle", "Available");
            }
            catch { return "-"; }
        }

        private static void DispatchManagement(long pActorId)
        {
            AW3CommandRequest request = _mode == WindowMode.Successor
                ? AW3CommandRequest.DesignateMilitaryGovernorateSuccessor(
                    _suzerainKingdomId, _subjectKingdomId, pActorId)
                : AW3CommandRequest.ReplaceMilitaryGovernorateGovernor(
                    _suzerainKingdomId, _subjectKingdomId, pActorId);
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(request);
            if (_mode == WindowMode.Replacement &&
                result.Status == AW3CommandStatus.Accepted)
            {
                _mode = WindowMode.Successor;
                _feedbackKey = "";
                _feedbackError = false;
                Instance?.Refresh();
                return;
            }
            if (_mode == WindowMode.Replacement &&
                result.Status == AW3CommandStatus.Pending)
            {
                _pendingReplacement = true;
                _pendingReplacementSince = Time.realtimeSinceStartup;
            }
            _feedbackKey = result.MessageKey;
            _feedbackError = result.Status == AW3CommandStatus.Rejected;
            Instance?.Refresh();
        }

        private void OnCommandStateChanged()
        {
            if (!_pendingReplacement) return;
            _commandRefreshRequested = true;
        }

        private void Update()
        {
            if (!_pendingReplacement) return;
            bool timedOut = Time.realtimeSinceStartup -
                            _pendingReplacementSince >= 10f;
            if (!_commandRefreshRequested && !timedOut) return;
            _commandRefreshRequested = false;
            Kingdom subject = FindKingdom(_subjectKingdomId);
            if (subject?.data != null &&
                !MilitaryGovernorateSuccessionService.
                    CanReplaceGovernorForReadModel(subject))
            {
                _pendingReplacement = false;
                _mode = WindowMode.Successor;
                _feedbackKey = "";
                _feedbackError = false;
            }
            else if (timedOut)
            {
                _pendingReplacement = false;
                _feedbackKey =
                    "aw_military_governorate_failure_replacement_failed";
                _feedbackError = true;
            }
            if (!_pendingReplacement) Refresh();
        }

        private static void Create(long pActorId)
        {
            City city = FindCity(_seatCityId);
            Kingdom country = city?.kingdom;
            if (country?.data == null)
            {
                _feedbackKey = "aw_military_governorate_failure_invalid_city";
                _feedbackError = true;
                Instance?.Refresh();
                return;
            }
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.CreateMilitaryGovernorate(
                        country.id, city.id, pActorId));
            if (result.Status == AW3CommandStatus.Accepted)
            {
                _feedbackKey = "aw_military_governorate_success";
                _feedbackError = false;
            }
            else if (result.Status == AW3CommandStatus.Pending)
            {
                _feedbackKey = result.MessageKey;
                _feedbackError = false;
            }
            else
            {
                _feedbackKey = result.MessageKey;
                _feedbackError = true;
            }
            Instance?.Refresh();
        }

        private void AddMessage(string pText, bool pError)
        {
            AddItemToList(new WarDecisionTargetRow
            {
                text = pText ?? "",
                stats = "",
                dim = pError,
                enabled = false,
                sort_order = int.MinValue
            });
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        protected override AbstractListWindowItem<WarDecisionTargetRow>
            CreateItemPrefab()
        {
            var obj = new GameObject("MilitaryGovernorateGeneralListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<WarDecisionTargetListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
