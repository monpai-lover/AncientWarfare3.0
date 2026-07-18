using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    internal class MandateDynastyWindow : AbstractListWindow<MandateDynastyWindow, HistoryRow>
    {
        private const float ROW_WIDTH = 220f;
        private readonly HashSet<int> _expandedPeriods = new HashSet<int>();
        private readonly HashSet<int> _expandedReigns = new HashSet<int>();

        public static void Open()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.MANDATE_DYNASTY);
            InstallCallbacks();
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.MANDATE_DYNASTY,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
        }

        public override void OnNormalEnable()
        {
            InstallCallbacks();
            Refresh();
        }

        private static void InstallCallbacks()
        {
            HistoryListItem.OnDynastyToggle = idx =>
            {
                if (Instance == null) return;
                if (Instance._expandedPeriods.Contains(idx)) Instance._expandedPeriods.Remove(idx);
                else Instance._expandedPeriods.Add(idx);
                Instance.Refresh();
            };
            HistoryListItem.OnHeaderToggle = idx =>
            {
                if (Instance == null) return;
                if (Instance._expandedReigns.Contains(idx)) Instance._expandedReigns.Remove(idx);
                else Instance._expandedReigns.Add(idx);
                Instance.Refresh();
            };
            HistoryListItem.OnFilterToggle = key =>
            {
                Kingdom kingdom = MandateService.GetCurrentMandateKingdom();
                if (kingdom?.data == null) return;
                if (key == "central_power")
                    CentralPowerWindow.Open(kingdom.id);
                else if (key == "feudatories")
                    FeudatoryWindow.Open(kingdom.id);
                else
                    MandateDecisionWindow.Open(kingdom.id);
            };
            HistoryListItem.OnActorBiography = actorId =>
            {
                if (actorId >= 0) HistoryListWindow.OpenPerson(actorId);
            };
            HistoryListItem.OnActorFamilyTree = actorId =>
            {
                if (actorId < 0) return;
                long shiId = LineageQuery.GetActorShiId(actorId);
                FamilyTreeWindow.OpenFamilyTree(actorId, shiId);
            };
        }

        public void Refresh()
        {
            ClearList();
            MandateReport report = MandateService.ReadReport();
            List<MandatePeriodView> periods = MandateHistoryQuery.GetPeriods();
            MandatePeriodView currentPeriod = FindPeriod(periods, report.period_id);

            AddStatusRows(report, currentPeriod);
            if (report.active)
            {
                Kingdom mandate = MandateService.GetCurrentMandateKingdom();
                AddCentralPowerRow(mandate);
                AddFeudatoryRow(mandate);
                AddDecisionRow(mandate);
            }
            else AddPlain(AW_L10n.Text("aw_mandate_none_desc",
                "\u6700\u5F3A\u72EC\u7ACB\u738B\u56FD\u3001\u5386\u53F2\u4EBA\u7269\u738B\u56FD\u6216\u63A7\u5236\u65E7\u5929\u547D\u6CD5\u7406\u6838\u5FC3\u7684\u56FD\u5BB6\u53EF\u4EE5\u53D7\u547D\u79F0\u5E1D\u3002"));

            AddPlain(AW_L10n.Text("aw_mandate_history_title", "\u5929\u547D\u53F2"), pHeader: true);
            if (periods.Count == 0)
            {
                AddPlain(AW_L10n.Text("aw_mandate_history_empty", "\u5C1A\u65E0\u5929\u547D\u53F2\u8BB0\u5F55"), pDim: true);
                return;
            }

            foreach (MandatePeriodView period in periods)
                AddPeriod(period);
        }

        private void AddStatusRows(MandateReport pReport, MandatePeriodView pPeriod)
        {
            string dynasty = pReport.active
                ? Fallback(pReport.dynasty_name, AW_L10n.Text("aw_mandate_dynasty_title", "\u5929\u547D\u738B\u671D"))
                : AW_L10n.Text("aw_mandate_none", "\u5F53\u524D\u6CA1\u6709\u5929\u547D\u738B\u671D");
            string color = pPeriod?.kingdom_color ?? HistoryColors.FromKingdom(MandateService.GetCurrentMandateKingdom());
            AddItemToList(new HistoryRow
            {
                width = ROW_WIDTH,
                is_header = true,
                dynasty_index = -1,
                reign_index = -1,
                text = RichName(dynasty, color),
                tooltip_title = dynasty,
                tooltip_desc = BuildStatusTooltip(pReport)
            });

            AddPlain(BuildPhaseSummary("  "));
            if (!pReport.active) return;
            AddPlain(BuildSacrificeSummary("  "));
            AddPlain(
                AW_L10n.Text("aw_mandate_kingdom", "\u5929\u547D\u56FD") + ": " + RichName(pReport.kingdom_name, color) +
                "  " + AW_L10n.Text("aw_mandate_emperor", "\u5929\u547D\u7687\u5E1D") + ": " + pReport.emperor_name);
            AddPlain(
                AW_L10n.Text("aw_mandate_value", "\u5929\u547D\u503C") + ": " + pReport.mandate_value +
                "  " + AW_L10n.Text("aw_mandate_authority", "\u7687\u6743") + ": " + pReport.imperial_authority +
                "  " + AW_L10n.Text("aw_mandate_crisis", "\u5929\u547D\u72B6\u6001") + ": " + CrisisText(pReport.crisis_level));
            AddPlain(
                AW_L10n.Text("aw_mandate_core_control", "\u6CD5\u7406\u63A7\u5236") + ": " +
                pReport.controlled_core_count + "/" + pReport.core_count + " " +
                Mathf.RoundToInt(pReport.core_control * 100f) + "%  " +
                AW_L10n.Text("aw_mandate_vassals", "\u5929\u547D\u9644\u5EB8") + ": " + pReport.vassal_count);
        }

        private void AddDecisionRow(Kingdom pKingdom)
        {
            MandateDecisionDef def = MandateDecisionService.GetCurrentDef(pKingdom);
            float fraction = MandateDecisionService.GetProgressFraction(pKingdom);
            string title = AW_L10n.Text("aw_mandate_decision_slot", "\u5929\u671D\u51B3\u8BAE");
            string name = def == null
                ? AW_L10n.Text("aw_mandate_decision_idle", "\u65E0\u5F53\u524D\u51B3\u8BAE")
                : AW_L10n.Text(def.NameKey, def.FallbackName);
            AddItemToList(new HistoryRow
            {
                width = ROW_WIDTH,
                is_filter = true,
                filter_key = "mandate_decisions",
                text = title + ": " + name + "  " + Mathf.FloorToInt(fraction * 100f) + "%",
                tooltip_title = title,
                tooltip_desc = BuildDecisionTooltip(pKingdom, def)
            });
        }

        private void AddCentralPowerRow(Kingdom pKingdom)
        {
            CentralizationSnapshot snapshot = CentralizationService.ReadSnapshot(pKingdom);
            string title = AW_L10n.Text("aw_central_power_entry", "Central Power");
            AddItemToList(new HistoryRow
            {
                width = ROW_WIDTH,
                is_filter = true,
                filter_key = "central_power",
                text = title + " · " + snapshot.effective_level + "/3",
                tooltip_title = title,
                tooltip_desc = AW_L10n.Text("aw_central_mandate_only",
                    "Only the Mandate realm can use central authority") +
                               "\n" + AW_L10n.Text("aw_central_nominal", "Nominal") +
                               ": " + snapshot.nominal_level +
                               "\n" + AW_L10n.Text("aw_central_phase_cap", "Phase cap") +
                               ": " + snapshot.phase_cap
            });
        }

        private void AddFeudatoryRow(Kingdom pKingdom)
        {
            int count = pKingdom?.data == null
                ? 0
                : FeudatoryService.GetByKingdom(pKingdom.id).Count;
            string title = AW_L10n.Text("aw_feudatory_entry", "Feudatories");
            AddItemToList(new HistoryRow
            {
                width = ROW_WIDTH,
                is_filter = true,
                filter_key = "feudatories",
                text = title + ": " + count,
                tooltip_title = title,
                tooltip_desc = AW_L10n.Text("aw_feudatory_entry_desc",
                    "Inspect princes, seats, cities, autonomy and garrisons")
            });
        }

        private void AddPeriod(MandatePeriodView pPeriod)
        {
            bool expanded = _expandedPeriods.Contains(pPeriod.index);
            AddItemToList(new HistoryRow
            {
                width = ROW_WIDTH,
                is_header = true,
                dynasty_index = pPeriod.index,
                reign_index = -1,
                expanded = expanded,
                text = BuildPeriodTitle(pPeriod),
                tooltip_title = Fallback(pPeriod.dynasty_name, AW_L10n.Text("aw_mandate_period", "\u5929\u547D\u671D\u4EE3")),
                tooltip_desc = BuildPeriodTooltip(pPeriod)
            });
            if (!expanded) return;

            for (int i = 0; i < pPeriod.reigns.Count; i++)
            {
                MandateReignView reign = pPeriod.reigns[i];
                int key = ReignKey(pPeriod.index, i);
                bool reignExpanded = _expandedReigns.Contains(key);
                AddItemToList(new HistoryRow
                {
                    width = ROW_WIDTH,
                    is_header = true,
                    dynasty_index = -1,
                    reign_index = key,
                    expanded = reignExpanded,
                    text = BuildReignTitle(reign),
                    tooltip_title = AW_L10n.Text("aw_mandate_reign_period", "\u5929\u547D\u541B\u4E3B\u65F6\u671F"),
                    tooltip_desc = BuildReignTooltip(reign)
                });
                if (!reignExpanded) continue;

                if (reign.has_king && reign.king_actor_id >= 0)
                {
                    AddItemToList(new HistoryRow
                    {
                        width = ROW_WIDTH,
                        is_action = true,
                        action_actor_id = reign.king_actor_id,
                        text = AW_L10n.Text("aw_view_emperor_biography", "\u67E5\u770B\u5929\u5B50\u4F20\u8BB0\uFF1A") +
                               RichName(DisplayKingName(reign), DisplayKingColor(reign)),
                        tooltip_title = AW_L10n.Text("aw_view_emperor_biography", "\u67E5\u770B\u5929\u5B50\u4F20\u8BB0"),
                        tooltip_desc = AW_L10n.Text("aw_view_emperor_biography_desc", "\u6253\u5F00\u8BE5\u5929\u547D\u541B\u4E3B\u7684\u4EBA\u7269\u4F20\u8BB0")
                    });
                }

                foreach (MandateHistoryEvent e in reign.events)
                    AddItemToList(BuildEventRow(e));
            }
        }

        private void AddPlain(string pText, bool pHeader = false, bool pDim = false)
        {
            AddItemToList(new HistoryRow
            {
                width = ROW_WIDTH,
                is_header = pHeader,
                dynasty_index = -1,
                reign_index = -1,
                text = pText ?? "",
                dim = pDim
            });
        }

        private static HistoryRow BuildEventRow(MandateHistoryEvent pEvent)
        {
            string targetType = "";
            long targetId = -1;
            if (pEvent.actor_id >= 0) { targetType = "actor"; targetId = pEvent.actor_id; }
            else if (pEvent.city_id >= 0) { targetType = "city"; targetId = pEvent.city_id; }
            else if (pEvent.kingdom_id >= 0) { targetType = "kingdom"; targetId = pEvent.kingdom_id; }

            return new HistoryRow
            {
                width = ROW_WIDTH,
                text = FormatEvent(pEvent),
                dim = true,
                target_type = targetType,
                target_id = targetId,
                tooltip_title = AW_L10n.Text("aw_mandate_event", "\u5929\u547D\u4E8B\u4EF6"),
                tooltip_desc = BuildEventTooltip(pEvent)
            };
        }

        private static string FormatEvent(MandateHistoryEvent pEvent)
        {
            string prefix = HistoryWriter.NormalizeYearPrefix(pEvent.year_prefix, pEvent.world_time);
            string year = string.IsNullOrEmpty(prefix) ? "" : HistoryColors.EscapeRich(prefix) + "  ";
            return year + HistoryColors.EscapeRich(pEvent.content);
        }

        private static string BuildPeriodTitle(MandatePeriodView pPeriod)
        {
            string name = Fallback(pPeriod.dynasty_name, pPeriod.kingdom_name);
            return RichName(name, pPeriod.kingdom_color) + "  " + OriginText(pPeriod.origin_type) + "  " +
                   YearSpan(pPeriod.start_time, pPeriod.end_time);
        }

        private static string BuildReignTitle(MandateReignView pReign)
        {
            string span = YearSpan(pReign.start_time, pReign.end_time);
            if (!pReign.has_king)
                return AW_L10n.Text("aw_mandate_no_emperor_period", "\u65E0\u660E\u786E\u5929\u5B50\u65F6\u671F") + "  " + span;
            string prefix = HistoryWriter.NormalizeYearPrefix(pReign.year_prefix_snapshot, pReign.start_time);
            string era = string.IsNullOrEmpty(prefix) ? "" : RichName(prefix, pReign.king_color) + "  ";
            return era + RichName(DisplayKingName(pReign), DisplayKingColor(pReign)) + "  " + span;
        }

        private static string BuildStatusTooltip(MandateReport pReport)
        {
            string phaseSummary = BuildPhaseSummary("\n");
            if (!pReport.active)
                return AW_L10n.Text("aw_mandate_none", "\u5F53\u524D\u6CA1\u6709\u5929\u547D\u738B\u671D") +
                       "\n" + phaseSummary;
            return AW_L10n.Text("aw_mandate_kingdom", "\u5929\u547D\u56FD") + ": " + pReport.kingdom_name +
                   "\n" + AW_L10n.Text("aw_mandate_emperor", "\u5929\u547D\u7687\u5E1D") + ": " + pReport.emperor_name +
                   "\n" + AW_L10n.Text("aw_mandate_value", "\u5929\u547D\u503C") + ": " + pReport.mandate_value +
                   "\n" + AW_L10n.Text("aw_mandate_authority", "\u7687\u6743") + ": " + pReport.imperial_authority +
                   "\n" + AW_L10n.Text("aw_mandate_prestige", "\u738B\u671D\u5A01\u671B") + ": " + pReport.dynasty_prestige +
                   "\n" + AW_L10n.Text("aw_mandate_core_control", "\u6CD5\u7406\u63A7\u5236") + ": " +
                   pReport.controlled_core_count + "/" + pReport.core_count +
                   "\n" + AW_L10n.Text("aw_mandate_vassals", "\u5929\u547D\u9644\u5EB8") + ": " + pReport.vassal_count +
                   "\n" + AW_L10n.Text("aw_mandate_origin", "\u6765\u6E90") + ": " + OriginText(pReport.origin_type) +
                   "\n" + AW_L10n.Text("aw_mandate_claimant", "\u5BA3\u79F0") + ": " + ClaimantText(pReport.claimant_kind) +
                   "\n" + phaseSummary +
                   "\n" + BuildSacrificeSummary("\n");
        }

        private static string BuildPhaseSummary(string pSeparator)
        {
            MandatePhase phase = MandatePhaseService.CurrentPhase;
            return AW_L10n.Text("aw_mandate_phase", "\u6CBB\u4E71\u9636\u6BB5") + ": " + PhaseText(phase) +
                   pSeparator + AW_L10n.Text("aw_mandate_phase_since", "\u9636\u6BB5\u8D77\u59CB") + ": " +
                   MandatePhaseService.PhaseSinceYear +
                   pSeparator + AW_L10n.Text("aw_mandate_catalyst", "\u4E71\u4E16\u50AC\u5316") + ": " +
                   MandatePhaseService.CatalystScore;
        }

        private static string BuildSacrificeSummary(string pSeparator)
        {
            Kingdom kingdom = MandateService.GetCurrentMandateKingdom();
            if (kingdom?.data == null) return "";
            MandateRitesSnapshot rites = MandateRitesService.ReadSnapshot(kingdom);
            kingdom.data.get(LineageKeys.MANDATE_SACRIFICE_BUFF_UNTIL,
                out int buffUntil, int.MinValue);
            kingdom.data.get(LineageKeys.MANDATE_SACRIFICE_BUFF_DELTA,
                out int storedDelta, 0);
            int currentYear = Date.getCurrentYear();
            int annualDelta = MandateSacrificeRules.ActiveAnnualDelta(
                currentYear, buffUntil, storedDelta);
            string summary = AW_L10n.Text("aw_ritual_total", "礼制完备度") + ": " +
                             rites.total_points + "/" + rites.ordinary_required +
                             pSeparator + AW_L10n.Text("aw_ritual_policy_source",
                                 "天命礼制政策") + ": " + rites.policy_points +
                             pSeparator + AW_L10n.Text("aw_ritual_capital_temple_source",
                                 "首都太庙") + ": " + rites.temple_points +
                             pSeparator + AW_L10n.Text("aw_ritual_sacrifice_source",
                                 "大祭永久点") + ": " + rites.permanent_points + "/10" +
                             pSeparator +
                             AW_L10n.Text("aw_mandate_sacrifice_annual_effect",
                                 "\u5927\u7940\u5E74\u6548") + ": " + Signed(annualDelta);
            if (buffUntil >= currentYear)
                summary += "  " + AW_L10n.Text("aw_mandate_sacrifice_buff_until",
                    "\u81F3\u5E74") + ": " + buffUntil;
            return summary;
        }

        private static string BuildDecisionTooltip(Kingdom pKingdom, MandateDecisionDef pDef)
        {
            if (pDef == null)
                return AW_L10n.Text("aw_mandate_decision_idle_desc", "\u70B9\u51FB\u5207\u6362\u4E3A\u53EF\u6267\u884C\u7684\u5929\u671D\u51B3\u8BAE\u3002");
            float progress = MandateDecisionService.GetProgress(pKingdom);
            float remaining = Mathf.Max(0f, pDef.Cost - progress);
            string yearlyLabel = pDef.SacrificeLevel.HasValue
                ? AW_L10n.Text("aw_mandate_sacrifice_yearly_spend", "\u672C\u5E74\u6295\u5165")
                : AW_L10n.Text("aw_policy_yearly_gain", "\u5E74\u589E\u957F");
            string qualification = "";
            if (pDef.SacrificeLevel.HasValue)
            {
                string value = MandateSacrificeService.IsQualified(pKingdom)
                    ? AW_L10n.Text("aw_mandate_sacrifice_qualified", "\u5408\u683C")
                    : AW_L10n.Text("aw_mandate_sacrifice_unqualified", "\u4E0D\u5408\u683C");
                qualification = "\n" +
                    AW_L10n.Text("aw_mandate_sacrifice_qualification", "\u793C\u5B98\u8D44\u683C") +
                    ": " + value;
            }
            return AW_L10n.Text(pDef.DescKey, pDef.FallbackDesc) +
                   "\n" + AW_L10n.Text("aw_policy_progress", "\u8FDB\u5EA6") + ": " +
                   Mathf.FloorToInt(progress) + "/" + Mathf.CeilToInt(pDef.Cost) +
                   "\n" + AW_L10n.Text("aw_policy_remaining", "\u5269\u4F59") + ": " + Mathf.CeilToInt(remaining) +
                   "\n" + yearlyLabel + ": " +
                   MandateDecisionService.EstimateYearlyGain(pKingdom, pDef).ToString("0.0") +
                   qualification +
                   "\n" + AW_L10n.Text("aw_mandate_decision_click_cycle", "\u70B9\u51FB\u5207\u6362\u5929\u671D\u51B3\u8BAE");
        }

        private static string BuildPeriodTooltip(MandatePeriodView pPeriod)
        {
            return AW_L10n.Text("aw_mandate_kingdom", "\u5929\u547D\u56FD") + ": " + Fallback(pPeriod.kingdom_name, "?") +
                   "\n" + AW_L10n.Text("aw_dynasty_founder", "\u5EFA\u7ACB\u8005\uFF1A") + Fallback(pPeriod.founder_name, "?") +
                   "\n" + AW_L10n.Text("aw_dynasty_duration", "\u5B58\u7EED\u65F6\u95F4\uFF1A") + YearSpan(pPeriod.start_time, pPeriod.end_time) +
                   "\n" + AW_L10n.Text("aw_mandate_origin", "\u6765\u6E90") + ": " + OriginText(pPeriod.origin_type) +
                   "\n" + AW_L10n.Text("aw_mandate_claimant", "\u5BA3\u79F0") + ": " + ClaimantText(pPeriod.claimant_kind) +
                   "\n" + AW_L10n.Text("aw_mandate_core_count", "\u6CD5\u7406\u6838\u5FC3") + ": " + pPeriod.legal_core_count +
                   "\n" + AW_L10n.Text("aw_mandate_value", "\u5929\u547D\u503C") + ": " +
                   pPeriod.start_mandate + " -> " + (pPeriod.end_time < 0 ? AW_L10n.Text("aw_until_now", "\u81F3\u4ECA") : pPeriod.end_mandate.ToString()) +
                   "\n" + AW_L10n.Text("aw_dynasty_end_reason", "\u7ED3\u675F\u539F\u56E0\uFF1A") + EndReasonLabel(pPeriod.end_reason, pPeriod.end_time);
        }

        private static string BuildReignTooltip(MandateReignView pReign)
        {
            if (!pReign.has_king)
                return AW_L10n.Text("aw_mandate_no_emperor_period", "\u65E0\u660E\u786E\u5929\u5B50\u65F6\u671F");
            return AW_L10n.Text("aw_mandate_emperor", "\u5929\u547D\u7687\u5E1D") + ": " + DisplayKingName(pReign) +
                   "\n" + AW_L10n.Text("aw_dynasty_duration", "\u5B58\u7EED\u65F6\u95F4\uFF1A") + YearSpan(pReign.start_time, pReign.end_time);
        }

        private static string BuildEventTooltip(MandateHistoryEvent pEvent)
        {
            string desc = AW_L10n.Text("aw_history_type", "\u7C7B\u578B\uFF1A") +
                          WarDisplayLabelRules.EventLabel(pEvent.event_type) +
                          "\n" + AW_L10n.Text("aw_history_time", "\u65F6\u95F4\uFF1A") +
                          HistoryWriter.NormalizeYearPrefix(pEvent.year_prefix, pEvent.world_time) +
                          "\n" + AW_L10n.Text("aw_mandate_value", "\u5929\u547D\u503C") + ": " + pEvent.mandate_value +
                          " (" + Signed(pEvent.value_delta) + ")" +
                          "\n" + AW_L10n.Text("aw_mandate_authority", "\u7687\u6743") + ": " + pEvent.imperial_authority;
            if (!string.IsNullOrEmpty(pEvent.kingdom_name))
                desc += "\n" + AW_L10n.Text("aw_mandate_kingdom", "\u5929\u547D\u56FD") + ": " + pEvent.kingdom_name;
            if (!string.IsNullOrEmpty(pEvent.actor_name))
                desc += "\n" + AW_L10n.Text("aw_actor", "\u4EBA\u7269") + ": " + pEvent.actor_name;
            if (!string.IsNullOrEmpty(pEvent.city_name))
                desc += "\n" + AW_L10n.Text("aw_city", "\u57CE\u5E02") + ": " + pEvent.city_name;
            return desc + "\n\n" + pEvent.content;
        }

        protected override AbstractListWindowItem<HistoryRow> CreateItemPrefab()
        {
            var obj = new GameObject("MandateHistoryListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<HistoryListItem>();
            obj.SetActive(false);
            return item;
        }

        private static MandatePeriodView FindPeriod(List<MandatePeriodView> pPeriods, long pPeriodId)
        {
            if (pPeriods == null) return null;
            foreach (MandatePeriodView period in pPeriods)
                if (period.period_id == pPeriodId) return period;
            return null;
        }

        private static int ReignKey(int pPeriodIndex, int pReignIndex)
        {
            return pPeriodIndex * 1000 + pReignIndex;
        }

        private static string DisplayKingName(MandateReignView pReign)
        {
            return string.IsNullOrEmpty(pReign.posthumous_title)
                ? Fallback(pReign.king_name, "?")
                : pReign.posthumous_title;
        }

        private static string DisplayKingColor(MandateReignView pReign)
        {
            return string.IsNullOrEmpty(pReign.posthumous_title)
                ? pReign.king_color
                : (string.IsNullOrEmpty(pReign.posthumous_color) ? pReign.king_color : pReign.posthumous_color);
        }

        private static string CrisisText(string pLevel)
        {
            switch (pLevel)
            {
                case "golden": return AW_L10n.Text("aw_mandate_crisis_golden", "\u76DB\u4E16");
                case "stable": return AW_L10n.Text("aw_mandate_crisis_stable", "\u5E73\u7A33");
                case "shaken": return AW_L10n.Text("aw_mandate_crisis_shaken", "\u52A8\u6447");
                case "lost": return AW_L10n.Text("aw_mandate_crisis_lost", "\u5931\u5FB7");
                case "collapse": return AW_L10n.Text("aw_mandate_crisis_collapse", "\u5D29\u89E3");
                default: return string.IsNullOrEmpty(pLevel) ? AW_L10n.Text("aw_mandate_crisis_unknown", "\u672A\u77E5") : pLevel;
            }
        }

        private static string PhaseText(MandatePhase pPhase)
        {
            switch (pPhase)
            {
                case MandatePhase.Decline:
                    return AW_L10n.Text("aw_mandate_phase_decline", "\u8870\u4E16");
                case MandatePhase.Chaos:
                    return AW_L10n.Text("aw_mandate_phase_chaos", "\u4E71\u4E16");
                case MandatePhase.Renewal:
                    return AW_L10n.Text("aw_mandate_phase_renewal", "\u65B0\u671D");
                default:
                    return AW_L10n.Text("aw_mandate_phase_golden", "\u6CBB\u4E16");
            }
        }

        private static string OriginText(string pOrigin)
        {
            switch (pOrigin)
            {
                case "rebel": return AW_L10n.Text("aw_mandate_origin_rebel", "\u4E49\u519B\u53D7\u547D");
                case "pseudo_foreign": return AW_L10n.Text("aw_mandate_origin_pseudo_foreign", "\u5916\u65CF\u4F2A\u671D");
                default: return AW_L10n.Text("aw_mandate_origin_native", "\u6B63\u7EDF\u53D7\u547D");
            }
        }

        private static string ClaimantText(string pClaimant)
        {
            switch (pClaimant)
            {
                case "rebel": return AW_L10n.Text("aw_mandate_claimant_rebel", "\u519C\u6C11\u4E49\u519B");
                case "foreign_pseudo": return AW_L10n.Text("aw_mandate_claimant_foreign_pseudo", "\u5165\u5173\u4F2A\u671D");
                default: return AW_L10n.Text("aw_mandate_claimant_orthodox", "\u6B63\u7EDF\u738B\u671D");
            }
        }

        private static string EndReasonLabel(string pReason, double pEndTime)
        {
            if (pEndTime < 0) return AW_L10n.Text("aw_until_now", "\u81F3\u4ECA");
            switch (pReason)
            {
                case "replaced": return AW_L10n.Text("aw_mandate_end_replaced", "\u5929\u547D\u66F4\u6613");
                case "kingdom_fell": return AW_L10n.Text("aw_dynasty_end_kingdom_fell", "\u56FD\u5BB6\u706D\u4EA1");
                case "low_mandate": return AW_L10n.Text("aw_mandate_end_low", "\u5929\u547D\u5D29\u89E3");
                case "war_lost": return AW_L10n.Text("aw_mandate_end_war_lost", "\u5929\u547D\u6218\u4E89\u5931\u5229");
                default: return string.IsNullOrEmpty(pReason)
                    ? AW_L10n.Text("aw_dynasty_end_unknown", "\u672A\u8BB0\u5F55")
                    : pReason;
            }
        }

        private static string YearSpan(double pStart, double pEnd)
        {
            string start = HistoryWriter.FormatDate(pStart);
            if (pEnd < 0) return start + "-" + AW_L10n.Text("aw_until_now", "\u81F3\u4ECA");
            string end = HistoryWriter.FormatDate(pEnd);
            return start == end ? start : start + "-" + end;
        }

        private static string RichName(string pText, string pColor)
        {
            return HistoryText.Colored(pText ?? "", pColor).Rich;
        }

        private static string Fallback(string pText, string pFallback)
        {
            return string.IsNullOrEmpty(pText) ? pFallback : pText;
        }

        private static string Signed(int pValue)
        {
            return pValue > 0 ? "+" + pValue : pValue.ToString();
        }
    }
}
