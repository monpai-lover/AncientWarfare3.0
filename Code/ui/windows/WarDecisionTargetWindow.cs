using System;
using System.Collections.Generic;
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

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.WAR_TARGETS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.WAR_TARGETS,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
        }

        public override void OnNormalEnable()
        {
            Refresh();
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

            List<WarDecisionTargetRow> rows = BuildRows(kingdom);
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

        private static List<WarDecisionTargetRow> BuildRows(Kingdom pKingdom)
        {
            var rows = new List<WarDecisionTargetRow>();
            City coreProjectCity = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
            if (coreProjectCity?.data != null)
            {
                string cityName = coreProjectCity.data.name ?? "?";
                string cityNameRich = RichCityName(coreProjectCity, pKingdom);
                string label = AW_L10n.Text("aw_war_fabricate_core", "\u5236\u9020\u6838\u5FC3");
                rows.Add(new WarDecisionTargetRow
                {
                    sort_order = WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.PROJECT_CORE),
                    sort_name = cityName,
                    text = label,
                    stats = AW_L10n.Text("aw_city_short", "\u57CE") + ": " + cityNameRich,
                    button_text = AW_L10n.Text("aw_war_target_action_project", "\u7B79\u5907"),
                    icon_path = WarIconPathRules.ResolveTargetIconPath(WarTerritoryService.PROJECT_CORE),
                    tooltip_title = label,
                    tooltip_desc = AW_L10n.Text("aw_war_fabricate_core_desc",
                        "\u5728\u672C\u56FD\u63A7\u5236\u7684\u975E\u6838\u5FC3\u57CE\u5E02\u5236\u9020\u6838\u5FC3\u3002") +
                                   "\n" + AW_L10n.Text("aw_war_target_selected_city", "\u9009\u4E2D\u57CE\u5E02\uFF1A") + cityName,
                    action = () =>
                    {
                        KingdomPolicyService.StartFabricationDecision(pKingdom, pKingdom, coreProjectCity,
                            WarTerritoryService.PROJECT_CORE);
                        Open(pKingdom.id);
                    }
                });
            }

            foreach (WarTerritoryService.TargetReport report in WarTerritoryService.BuildTargetReports(pKingdom))
                AddReportRows(rows, pKingdom, report);
            return rows;
        }

        private static void AddReportRows(List<WarDecisionTargetRow> pRows, Kingdom pSource,
            WarTerritoryService.TargetReport pReport)
        {
            Kingdom target = pReport?.target;
            if (target?.data == null) return;
            string targetName = target.name ?? "?";
            string targetNameRich = RichKingdomName(target);

            AddProjectRows(pRows, pSource, pReport, target, targetName, targetNameRich);
            if (pReport.can_take_mandate)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_TAKE_MANDATE),
                    AW_L10n.Text("aw_war_take_mandate", "\u593A\u53D6\u5929\u547D"),
                    WarTerritoryService.GOAL_TAKE_MANDATE,
                    AW_L10n.Text("aw_war_take_mandate_desc", "\u5BF9\u5F53\u524D\u5929\u547D\u56FD\u53D1\u52A8\u5929\u547D\u6218\u4E89\uFF0C\u80DC\u5229\u540E\u8F6C\u79FB\u5929\u547D\u3002"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_TAKE_MANDATE,
                        target.capital,
                        MandateService.WAR_TIANMING, "tianming", "\u593A\u53D6\u5929\u547D"));

            if (pReport.can_mandate_conquest)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_MANDATE_CONQUEST),
                    AW_L10n.Text("aw_war_mandate_conquest", "\u5929\u547D\u5F81\u670D"),
                    WarTerritoryService.GOAL_MANDATE_CONQUEST,
                    AW_L10n.Text("aw_war_mandate_conquest_desc", "\u5929\u547D\u56FD\u5BF9\u5F31\u5C0F\u5916\u56FD\u53D1\u52A8\u5F81\u670D\u6218\u4E89\uFF0C\u4E0D\u9700\u5236\u9020\u5BA3\u79F0\u4E14\u4E0D\u53D7\u5F3A\u5BA3\u60E9\u7F5A\u3002"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_MANDATE_CONQUEST,
                        target.capital,
                        WarDecisionService.WAR_NORMAL, "mandate_conquest", "\u5929\u547D\u5F81\u670D"));

            if (pReport.can_reclaim)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_TAKE_CORE_CITY),
                    AW_L10n.Text("aw_war_reclaim", "\u6536\u590D\u6838\u5FC3"),
                    WarTerritoryService.GOAL_TAKE_CORE_CITY,
                    AW_L10n.Text("aw_war_reclaim_desc", "\u6536\u590D\u8BE5\u56FD\u5360\u636E\u7684\u6838\u5FC3\u57CE\u5E02"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_TAKE_CORE_CITY,
                        WarTerritoryService.FindBestCoreTargetCityForDecision(pSource, target),
                        "reclaim", "core_reclaim", "\u6536\u590D\u6838\u5FC3"));

            if (pReport.can_press_claim)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_PRESS_CLAIM_CITY),
                    AW_L10n.Text("aw_war_press_claim", "\u6309\u5BA3\u79F0\u5BA3\u6218"),
                    WarTerritoryService.GOAL_PRESS_CLAIM_CITY,
                    AW_L10n.Text("aw_war_press_claim_desc", "\u6309\u5F3A/\u5F31\u5BA3\u79F0\u53D1\u52A8\u6218\u4E89"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_PRESS_CLAIM_CITY,
                        WarTerritoryService.FindBestClaimTargetCityForDecision(pSource, target),
                        WarDecisionService.WAR_NORMAL, "claim_war", "\u6309\u5BA3\u79F0\u5BA3\u6218"));

            if (pReport.can_restore)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_RESTORE_KINGDOM),
                    AW_L10n.Text("aw_war_restoration", "\u590D\u56FD\u6218\u4E89"),
                    WarTerritoryService.GOAL_RESTORE_KINGDOM,
                    AW_L10n.Text("aw_war_restoration_desc", "\u4EE5\u4EA1\u56FD\u738B\u5BA4\u8840\u7EDF\u53D1\u8D77\u590D\u56FD\u6218\u4E89"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_RESTORE_KINGDOM,
                        WarTerritoryService.FindBestRestorationTargetCityForDecision(pSource, target),
                        WarDecisionService.WAR_RESTORATION, "restoration", "\u590D\u56FD"));

            if (pReport.can_force_vassal)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_FORCE_VASSAL),
                    AW_L10n.Text("aw_war_force_vassal", "\u5F3A\u5236\u81E3\u670D"),
                    WarTerritoryService.GOAL_FORCE_VASSAL,
                    AW_L10n.Text("aw_war_force_vassal_desc", "\u8FEB\u4F7F\u76EE\u6807\u6210\u4E3A\u9644\u5EB8"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_FORCE_VASSAL, null,
                        "vassal_war", "force_vassal", "\u5F3A\u5236\u81E3\u670D"));

            if (pReport.can_force_tributary)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_FORCE_TRIBUTARY),
                    AW_L10n.Text("aw_war_force_tributary", "\u53E9\u5173\u7EB3\u8D21"),
                    WarTerritoryService.GOAL_FORCE_TRIBUTARY,
                    AW_L10n.Text("aw_war_force_tributary_desc",
                        "\u8FEB\u4F7F\u76F8\u90BB\u5F31\u56FD\u5C81\u65F6\u7EB3\u8D21\uFF0C\u4F46\u4E0D\u7EB3\u5165\u9644\u5EB8\u4F53\u7CFB"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_FORCE_TRIBUTARY, null,
                        WarDecisionService.WAR_TRIBUTARY, "tributary_war", "\u53E9\u5173\u7EB3\u8D21"));

            if (pReport.can_independence)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_INDEPENDENCE),
                    AW_L10n.Text("aw_war_independence", "\u72EC\u7ACB\u6218\u4E89"),
                    WarTerritoryService.GOAL_INDEPENDENCE,
                    AW_L10n.Text("aw_war_independence_desc", "\u5BF9\u5F53\u524D\u5B97\u4E3B\u53D1\u52A8\u72EC\u7ACB\u6218\u4E89"),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_INDEPENDENCE, null,
                        "independence_war", "independence_war", "\u8131\u79BB\u5B97\u4E3B"));

            if (pReport.can_no_cb)
                AddWarRow(pRows, pSource, target, pReport,
                    WarDecisionTargetOrderRules.SortOrder(WarTerritoryService.GOAL_NO_CB),
                    AW_L10n.Text("aw_war_no_cb", "\u5F3A\u5BA3"),
                    WarTerritoryService.GOAL_NO_CB,
                    BuildNoCbTooltip(pSource),
                    AW_L10n.Text("aw_war_target_action_war", "\u5BA3\u6218"),
                    () => KingdomPolicyService.StartWarDecision(pSource, target,
                        WarTerritoryService.GOAL_NO_CB, null,
                        WarDecisionService.WAR_NORMAL, "no_cb", "\u65E0\u7406\u7531\u5BA3\u6218"));
        }

        private static void AddProjectRows(List<WarDecisionTargetRow> pRows, Kingdom pSource,
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

        private static void AddProjectRow(List<WarDecisionTargetRow> pRows, Kingdom pSource, Kingdom pTarget,
            City pCity, int pOrder, string pLabel, string pProjectType, string pTargetName, string pTargetNameRich,
            string pCityName, string pCityNameRich)
        {
            pRows.Add(new WarDecisionTargetRow
            {
                sort_order = pOrder,
                sort_name = pTargetName,
                text = WarDecisionTargetTextRules.BuildRowLabel(pTargetNameRich, pLabel),
                stats = AW_L10n.Text("aw_war_target_city", "\u76EE\u6807\u57CE\u5E02\uFF1A") + pCityNameRich,
                button_text = AW_L10n.Text("aw_war_target_action_project", "\u7B79\u5907"),
                icon_path = WarIconPathRules.ResolveTargetIconPath(pProjectType),
                tooltip_title = pLabel,
                tooltip_desc = AW_L10n.Text("aw_war_target_selected_kingdom", "\u76EE\u6807\u56FD\uFF1A") + pTargetName +
                               "\n" + AW_L10n.Text("aw_war_target_selected_city", "\u9009\u4E2D\u57CE\u5E02\uFF1A") + pCityName,
                action = () =>
                {
                    KingdomPolicyService.StartFabricationDecision(pSource, pTarget, pCity, pProjectType);
                    Open(pSource.id);
                }
            });
        }

        private static void AddWarRow(List<WarDecisionTargetRow> pRows, Kingdom pSource, Kingdom pTarget,
            WarTerritoryService.TargetReport pReport, int pOrder, string pLabel, string pGoalType, string pDesc,
            string pButtonText, Func<bool> pFallback)
        {
            WarTerritoryService.WarTargetOption option =
                WarTerritoryService.FindBestTargetOption(pSource, pTarget, pGoalType);
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
                tooltip_desc = BuildActionTooltip(pLabel, targetName, cityName, pDesc),
                action = () =>
                {
                    if (option != null) KingdomPolicyService.StartWarDecision(pSource, option);
                    else pFallback?.Invoke();
                    Open(pSource.id);
                }
            });
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
