using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    internal class WarDecisionTargetWindow : AbstractListWindow<WarDecisionTargetWindow, WarDecisionTargetRow>
    {
        private static long _kingdomId = -1;
        private static long _targetKingdomId = -1;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            _targetKingdomId = -1L;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.WAR_TARGETS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.WAR_TARGETS,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        public static void OpenForTarget(long pKingdomId,
            long pTargetKingdomId)
        {
            _kingdomId = pKingdomId;
            _targetKingdomId = pTargetKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.WAR_TARGETS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.WAR_TARGETS,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
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
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            _commandRefreshRequested = true;
        }

        public void Refresh()
        {
            ClearList();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                AddText(AW_L10n.Text("aw_policy_no_kingdom", "\u738B\u56FD\u4E0D\u5B58\u5728"), true, true);
                return;
            }

            AddText(kingdom.name + " " + AW_L10n.Text("aw_war_targets_desc",
                "\u5BA3\u6218\u7406\u7531\u4E0E\u6218\u4E89\u76EE\u6807"), true, false,
                AW_L10n.Text("aw_war_targets_title", "\u6218\u4E89\u76EE\u6807"),
                AW_L10n.Text("aw_war_targets_desc", "\u5BA3\u6218\u7406\u7531\u4E0E\u6218\u4E89\u76EE\u6807"));

            List<WarDecisionTargetRow> rows = BuildRows(kingdom,
                _targetKingdomId);
            if (rows.Count == 0)
            {
                AddText(AW_L10n.Text("aw_war_no_targets", "\u5F53\u524D\u6CA1\u6709\u53EF\u7528\u76EE\u6807"), false, true);
                return;
            }

            rows.Sort((a, b) =>
            {
                int cmp = a.sort_order.CompareTo(b.sort_order);
                return cmp != 0 ? cmp : string.Compare(a.sort_name, b.sort_name, StringComparison.Ordinal);
            });
            foreach (WarDecisionTargetRow row in rows) AddItemToList(row);
        }

        private List<WarDecisionTargetRow> BuildRows(Kingdom pKingdom,
            long pTargetKingdomId)
        {
            var rows = new List<WarDecisionTargetRow>();
            rows.Add(new WarDecisionTargetRow
            {
                sort_order = int.MinValue,
                sort_name = "",
                text = AW_L10n.Text(
                    "aw_war_fabrication_moved_to_diplomacy",
                    "Start new claim fabrication from the diplomacy window"),
                stats = "",
                icon_path = "ui/icons/iconDiplomacy",
                dim = true,
                enabled = false
            });

            long coreProjectCityId =
                KingdomPolicyService.GetCoreFabricationCityId(pKingdom);
            if (coreProjectCityId >= 0)
            {
                City coreProjectCity = World.world?.cities?.get(
                    coreProjectCityId);
                string cityName = coreProjectCity?.data?.name ??
                                  KingdomPolicyService
                                      .GetCoreFabricationCityName(pKingdom);
                float progress = KingdomPolicyService
                    .GetCoreFabricationProgressFraction(pKingdom);
                rows.Add(new WarDecisionTargetRow
                {
                    sort_order = WarDecisionTargetOrderRules.SortOrder(
                        WarTerritoryService.PROJECT_CORE),
                    sort_name = cityName,
                    text = AW_L10n.Text("aw_war_core_project_active",
                        "Core fabrication in progress") + "  " + cityName,
                    stats = Mathf.RoundToInt(progress * 100f) + "%",
                    icon_path = WarIconPathRules.ResolveTargetIconPath(
                        WarTerritoryService.PROJECT_CORE),
                    tooltip_title = AW_L10n.Text(
                        "aw_war_core_project_active",
                        "Core fabrication in progress"),
                    tooltip_desc = AW_L10n.Text(
                        "aw_war_project_status_read_only",
                        "This window only displays existing projects")
                });
            }

            foreach (WarTerritoryService.TargetReport report in WarTerritoryService.BuildTargetReports(pKingdom))
            {
                if (pTargetKingdomId >= 0 &&
                    report?.target?.id != pTargetKingdomId) continue;
                if (report?.target?.data == null) continue;
                string stats = BuildStats(report, "");
                if (report.restoration_claim_count > 0)
                    stats += "  " + AW_L10n.Text(
                        "aw_war_restoration_claim_count", "Restoration") +
                             ": " + report.restoration_claim_count;
                rows.Add(new WarDecisionTargetRow
                {
                    sort_order = 100,
                    sort_name = report.target.name ?? "",
                    text = RichKingdomName(report.target),
                    stats = stats,
                    icon_path = report.strong_claim_count > 0
                        ? WarIconPathRules.ResolveTargetIconPath(
                            WarTerritoryService.PROJECT_STRONG_CLAIM)
                        : report.weak_claim_count > 0
                            ? WarIconPathRules.ResolveTargetIconPath(
                                WarTerritoryService.PROJECT_WEAK_CLAIM)
                            : "ui/icons/iconDiplomacy",
                    tooltip_title = report.target.name ?? "",
                    tooltip_desc = AW_L10n.Text(
                        "aw_war_project_status_read_only",
                        "This window only displays existing projects")
                });
            }
            return rows;
        }

        private void AddReportRows(List<WarDecisionTargetRow> pRows, Kingdom pSource,
            WarTerritoryService.TargetReport pReport)
        {
            Kingdom target = pReport?.target;
            if (target?.data == null) return;
            string targetName = target.name ?? "?";
            string targetNameRich = RichKingdomName(target);

            AddProjectRows(pRows, pSource, pReport, target, targetName, targetNameRich);
            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_TAKE_MANDATE),
                    AW_L10n.Text("aw_war_take_mandate", "\u593A\u53D6\u5929\u547D"),
                    WarTerritoryService.GOAL_TAKE_MANDATE,
                    AW_L10n.Text("aw_war_take_mandate_desc", "\u5BF9\u5F53\u524D\u5929\u547D\u56FD\u53D1\u52A8\u5929\u547D\u6218\u4E89\uFF0C\u80DC\u5229\u540E\u8F6C\u79FB\u5929\u547D\u3002"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_MANDATE_CONQUEST),
                    AW_L10n.Text("aw_war_mandate_conquest", "\u5929\u547D\u5F81\u670D"),
                    WarTerritoryService.GOAL_MANDATE_CONQUEST,
                    AW_L10n.Text("aw_war_mandate_conquest_desc", "\u5929\u547D\u56FD\u5BF9\u5F31\u5C0F\u5916\u56FD\u53D1\u52A8\u5F81\u670D\u6218\u4E89\uFF0C\u4E0D\u9700\u5236\u9020\u5BA3\u79F0\u4E14\u4E0D\u53D7\u5F3A\u5BA3\u60E9\u7F5A\u3002"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_TAKE_CORE_CITY),
                    AW_L10n.Text("aw_war_reclaim", "\u6536\u590D\u6838\u5FC3"),
                    WarTerritoryService.GOAL_TAKE_CORE_CITY,
                    AW_L10n.Text("aw_war_reclaim_desc", "\u6536\u590D\u8BE5\u56FD\u5360\u636E\u7684\u6838\u5FC3\u57CE\u5E02"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_PRESS_CLAIM_CITY),
                    AW_L10n.Text("aw_war_press_claim", "\u6309\u5BA3\u79F0\u5BA3\u6218"),
                    WarTerritoryService.GOAL_PRESS_CLAIM_CITY,
                    AW_L10n.Text("aw_war_press_claim_desc", "\u6309\u5F3A/\u5F31\u5BA3\u79F0\u53D1\u52A8\u6218\u4E89"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_RESTORE_KINGDOM),
                    AW_L10n.Text("aw_war_restoration", "\u590D\u56FD\u6218\u4E89"),
                    WarTerritoryService.GOAL_RESTORE_KINGDOM,
                    AW_L10n.Text("aw_war_restoration_desc", "\u4EE5\u4EA1\u56FD\u738B\u5BA4\u8840\u7EDF\u53D1\u8D77\u590D\u56FD\u6218\u4E89"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_FORCE_VASSAL),
                    AW_L10n.Text("aw_war_force_vassal", "\u5F3A\u5236\u81E3\u670D"),
                    WarTerritoryService.GOAL_FORCE_VASSAL,
                    AW_L10n.Text("aw_war_force_vassal_desc", "\u8FEB\u4F7F\u76EE\u6807\u6210\u4E3A\u9644\u5EB8"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_FORCE_TRIBUTARY),
                    AW_L10n.Text("aw_war_force_tributary", "\u53E9\u5173\u7EB3\u8D21"),
                    WarTerritoryService.GOAL_FORCE_TRIBUTARY,
                    AW_L10n.Text("aw_war_force_tributary_desc",
                        "\u8FEB\u4F7F\u76F8\u90BB\u5F31\u56FD\u5C81\u65F6\u7EB3\u8D21\uFF0C\u4F46\u4E0D\u7EB3\u5165\u9644\u5EB8\u4F53\u7CFB"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_INDEPENDENCE),
                    AW_L10n.Text("aw_war_independence", "\u72EC\u7ACB\u6218\u4E89"),
                    WarTerritoryService.GOAL_INDEPENDENCE,
                    AW_L10n.Text("aw_war_independence_desc", "\u5BF9\u5F53\u524D\u5B97\u4E3B\u53D1\u52A8\u72EC\u7ACB\u6218\u4E89"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));

            AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_NO_CB),
                    AW_L10n.Text("aw_war_no_cb", "\u5F3A\u5BA3"),
                    WarTerritoryService.GOAL_NO_CB,
                    BuildNoCbTooltip(pSource),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"));
        }

        private void AddProjectRows(List<WarDecisionTargetRow> pRows, Kingdom pSource,
            WarTerritoryService.TargetReport pReport, Kingdom pTarget, string pTargetName, string pTargetNameRich)
        {
            if (!pReport.can_fabricate || pReport.fabrication_city?.data == null) return;
            City city = pReport.fabrication_city;
            string cityName = city.data.name ?? "?";
            string cityNameRich = RichCityName(city, pTarget);
            AddProjectRow(pRows, pSource, pTarget, city,
                WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.PROJECT_WEAK_CLAIM),
                AW_L10n.Text("aw_war_fabricate_weak_claim", "\u5236\u9020\u5F31\u5BA3\u79F0"),
                WarTerritoryService.PROJECT_WEAK_CLAIM, pTargetName, pTargetNameRich, cityName, cityNameRich);
            AddProjectRow(pRows, pSource, pTarget, city,
                WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.PROJECT_STRONG_CLAIM),
                AW_L10n.Text("aw_war_fabricate_strong_claim", "\u5236\u9020\u5F3A\u5BA3\u79F0"),
                WarTerritoryService.PROJECT_STRONG_CLAIM, pTargetName, pTargetNameRich, cityName, cityNameRich);
        }

        private void AddProjectRow(List<WarDecisionTargetRow> pRows, Kingdom pSource, Kingdom pTarget,
            City pCity, int pOrder, string pLabel, string pProjectType, string pTargetName, string pTargetNameRich,
            string pCityName, string pCityNameRich)
        {
            pRows.Add(new WarDecisionTargetRow
            {
                sort_order = pOrder,
                sort_name = pTargetName,
                text = WarDecisionTargetTextRules.BuildRowLabel(pTargetNameRich, pLabel),
                stats = AW_L10n.Text("aw_war_target_city", "\u76EE\u6807\u57CE\u5E02\uFF1A") + pCityNameRich,
                button_text = "",
                icon_path = WarIconPathRules.ResolveTargetIconPath(pProjectType),
                tooltip_title = pLabel,
                tooltip_desc = AW_L10n.Text("aw_war_target_selected_kingdom", "\u76EE\u6807\u56FD\uFF1A") + pTargetName +
                               "\n" + AW_L10n.Text("aw_war_target_selected_city", "\u9009\u4E2D\u57CE\u5E02\uFF1A") + pCityName,
                action = null
            });
        }

        private void AddWarRow(List<WarDecisionTargetRow> pRows, Kingdom pSource, Kingdom pTarget,
            WarTerritoryService.TargetReport pReport, int pOrder, string pLabel, string pGoalType, string pDesc,
            string pButtonText)
        {
            WarTerritoryService.WarTargetOption option =
                WarTerritoryService.FindBestTargetOption(pSource, pTarget, pGoalType);
            bool goalAllowed = DiplomaticWarDeclarationService.CanIssue(
                pSource, pTarget, pGoalType,
                DiplomaticWarDeclarationService.WarTypeForGoal(pGoalType),
                out string failureReason);
            bool available = option != null && goalAllowed;
            if (option == null && goalAllowed)
                failureReason = "war_target_option_changed";
            City city = option?.target_city;
            string targetName = pTarget?.name ?? "?";
            string targetNameRich = RichKingdomName(pTarget);
            string cityName = city?.data?.name ?? "";
            string cityNameRich = RichCityName(city, pTarget);
            pRows.Add(new WarDecisionTargetRow
            {
                sort_order = pOrder,
                sort_name = targetName,
                text = WarDecisionTargetTextRules.BuildRowLabel(targetNameRich, pLabel),
                stats = BuildStats(pReport, cityNameRich),
                button_text = pButtonText,
                icon_path = WarIconPathRules.ResolveTargetIconPath(pGoalType),
                tooltip_title = pLabel,
                tooltip_desc = BuildActionTooltip(pLabel, targetName,
                    cityName, pDesc) + (available ? "" : "\n\n" +
                    DiplomacyConversationWindow.
                        ProposalFailure(failureReason)),
                enabled = available && !_commandPending,
                action = option == null ? null : () => DispatchWar(pSource,
                    option)
            });
        }

        private void DispatchWar(Kingdom pSource,
            WarTerritoryService.WarTargetOption pOption)
        {
            if (_commandPending || pSource?.data == null ||
                pOption?.target_kingdom?.data == null)
                return;
            long cityId = pOption.target_city?.data?.id ?? -1L;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.DeclareWar(pSource.id,
                        pOption.target_kingdom.id, cityId,
                        pOption.goal_type,
                        DiplomaticWarDeclarationService.WarTypeForGoal(
                            pOption.goal_type),
                        DiplomaticWarDeclarationService.ReasonKeyForGoal(
                            pOption.goal_type),
                        string.IsNullOrWhiteSpace(pOption.label)
                            ? pOption.goal_type
                            : pOption.label));
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (result.Accepted)
                DiplomacyConversationWindow.Open(pSource.id);
            else
                WorldTip.showNow(DiplomacyConversationWindow.
                    ProposalFailure(result.MessageKey), false, "top");
        }

        private static string BuildStats(WarTerritoryService.TargetReport pReport, string pCityName)
        {
            string stats = "";
            if (!string.IsNullOrEmpty(pCityName)) stats += " · " + pCityName;
            return WarDecisionTargetTextRules.BuildStatsLine(pReport.core_count, pReport.strong_claim_count,
                pReport.weak_claim_count, pReport.pending_count, pCityName);
        }

        private static string BuildActionTooltip(string pReason, string pTargetKingdom, string pTargetCity,
            string pDesc)
        {
            string summary = WarDecisionTargetTextRules.BuildSummary(pReason, pTargetKingdom, pTargetCity);
            return string.IsNullOrEmpty(summary) ? pDesc : summary + "\n\n" + pDesc;
        }

        private void AddText(string pText, bool pHeader, bool pDim, string pTipTitle = "", string pTipDesc = "")
        {
            AddItemToList(new WarDecisionTargetRow
            {
                text = pText ?? "",
                is_header = pHeader,
                dim = pDim,
                enabled = !pDim,
                tooltip_title = pTipTitle ?? "",
                tooltip_desc = pTipDesc ?? ""
            });
        }

        private static string BuildNoCbTooltip(Kingdom pSource)
        {
            if (pSource?.data == null) return AW_L10n.Text("aw_war_no_cb_desc", "\u65E0\u7406\u7531\u5BA3\u6218");
            int year = Date.getCurrentYear();
            pSource.data.get("aw_no_cb_penalty_until_year", out int until, -99999);
            if (year < until)
                return AW_L10n.Text("aw_war_no_cb_cooldown", "\u5F3A\u5BA3\u51B7\u5374\u81F3") + until +
                       AW_L10n.Text("aw_year_suffix", "\u5E74");
            return AW_L10n.Text("aw_war_no_cb_penalty",
                "\u6D88\u8017\u653F\u6CBB\u70B9\u6570\u5E76\u589E\u52A0\u5408\u6CD5\u6027\u3001\u5916\u4EA4\u548C\u53DB\u4E71\u98CE\u9669\u60E9\u7F5A\u3002");
        }

        private static string RichKingdomName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "?";
            return HistoryText.Colored(pKingdom.name ?? "?", HistoryColors.FromKingdom(pKingdom)).Rich;
        }

        private static string RichCityName(City pCity, Kingdom pFallbackKingdom)
        {
            if (pCity?.data == null) return "";
            return HistoryText.Colored(pCity.data.name ?? "?", HistoryColors.FromCity(pCity, pFallbackKingdom)).Rich;
        }

        protected override AbstractListWindowItem<WarDecisionTargetRow> CreateItemPrefab()
        {
            var obj = new GameObject("WarDecisionTargetListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<WarDecisionTargetListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
