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
        private static long _seatCityId = -1;
        private static string _feedbackKey = "";
        private static bool _feedbackError;

        public static void OpenCreation(City pCity)
        {
            _seatCityId = pCity?.id ?? -1L;
            _feedbackKey = "";
            _feedbackError = false;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.MILITARY_GOVERNORATE);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.MILITARY_GOVERNORATE,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            ClearList();
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
                    text = actor.getName(),
                    stats = string.Format(AW_L10n.Text(
                            "aw_military_governorate_general_stats",
                            "Merit {0}  Loyalty {1}  Ambition {2}"),
                        merit, loyalty, ambition),
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
