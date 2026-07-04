using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal class MandateDynastyWindow : AbstractWindow<MandateDynastyWindow>
    {
        private const float WINDOW_W = 430f;
        private const float WINDOW_H = 390f;
        private const float PAD = 14f;
        private const float ROW_GAP = 4f;
        private const float STATUS_H = 104f;
        private const float DECISION_H = 36f;

        private static Sprite _whiteSprite;
        private readonly HashSet<int> _expandedPeriods = new HashSet<int>();
        private readonly HashSet<int> _expandedReigns = new HashSet<int>();
        private readonly List<GameObject> _created = new List<GameObject>();

        public static void Open()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.MANDATE_DYNASTY);
            InstallHistoryCallbacks();
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.MANDATE_DYNASTY,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
            ConfigureWindow();
        }

        public override void OnNormalEnable()
        {
            InstallHistoryCallbacks();
            Refresh();
        }

        private static void InstallHistoryCallbacks()
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

        private void ConfigureWindow()
        {
            var bgRect = BackgroundTransform.GetComponent<RectTransform>();
            if (bgRect != null) bgRect.sizeDelta = new Vector2(WINDOW_W, WINDOW_H);

            Transform close = BackgroundTransform.parent != null ? BackgroundTransform.parent.Find("CloseBackground") : null;
            if (close != null) close.localPosition = new Vector3(WINDOW_W / 2f - 20f, WINDOW_H / 2f - 12f);

            Transform titleBg = BackgroundTransform.Find("TitleBackground");
            var titleRect = titleBg != null ? titleBg.GetComponent<RectTransform>() : null;
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(WINDOW_W * 0.58f, 30f);
                titleBg.localPosition = new Vector3(0, WINDOW_H / 2f - 16f);
            }

            var sw = GetComponent<ScrollWindow>();
            if (sw?.titleText != null)
            {
                sw.titleText.transform.localPosition = new Vector3(0, WINDOW_H / 2f - 16f);
                sw.titleText.text = AW_L10n.Text("aw_mandate_dynasty_title", "\u5929\u547D\u738B\u671D");
                var titleTextRect = sw.titleText.GetComponent<RectTransform>();
                if (titleTextRect != null) titleTextRect.sizeDelta = new Vector2(WINDOW_W * 0.52f, 28f);
            }

            Transform scroll = BackgroundTransform.Find("Scroll View");
            var scrollRect = scroll != null ? scroll.GetComponent<RectTransform>() : null;
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(WINDOW_W - 32f, WINDOW_H - 62f);
                scroll.localPosition = new Vector3(0, -20f, 0);
            }

            Transform viewport = BackgroundTransform.Find("Scroll View/Viewport");
            var viewRect = viewport != null ? viewport.GetComponent<RectTransform>() : null;
            if (viewRect != null) viewRect.sizeDelta = new Vector2(WINDOW_W - 32f, WINDOW_H - 62f);
        }

        private void Refresh()
        {
            ClearCreated();
            ConfigureWindow();
            InstallHistoryCallbacks();

            float rowWidth = WINDOW_W - 56f;
            float y = 8f;
            MandateReport report = MandateService.ReadReport();
            List<MandatePeriodView> periods = MandateHistoryQuery.GetPeriods();
            MandatePeriodView currentPeriod = FindPeriod(periods, report.period_id);

            y = BuildStatusPanel(report, currentPeriod, y, rowWidth);
            y += 8f;

            if (report.active)
            {
                y = BuildDecisionSlot(MandateService.GetCurrentMandateKingdom(), y, rowWidth);
                y += 10f;
            }
            else
            {
                CreateText("NoMandateDesc", AW_L10n.Text("aw_mandate_none_desc",
                        "\u6700\u5F3A\u72EC\u7ACB\u738B\u56FD\u3001\u5386\u53F2\u4EBA\u7269\u738B\u56FD\u6216\u63A7\u5236\u65E7\u5929\u547D\u6CD5\u7406\u6838\u5FC3\u7684\u56FD\u5BB6\u53EF\u4EE5\u53D7\u547D\u79F0\u5E1D\u3002"),
                    TopLeft(PAD, y), new Vector2(rowWidth, 42f), TextAnchor.UpperCenter, 10, Color.white);
                y += 48f;
            }

            CreateText("HistoryTitle", AW_L10n.Text("aw_mandate_history_title", "\u5929\u547D\u53F2"),
                TopLeft(PAD, y), new Vector2(rowWidth, 20f), TextAnchor.MiddleLeft, 11,
                new Color(0.85f, 0.9f, 1f, 1f));
            y += 24f;

            if (periods.Count == 0)
            {
                CreateText("EmptyHistory", AW_L10n.Text("aw_mandate_history_empty", "\u5C1A\u65E0\u5929\u547D\u53F2\u8BB0\u5F55"),
                    TopLeft(PAD, y), new Vector2(rowWidth, 24f), TextAnchor.MiddleCenter, 10, Color.white);
                y += 28f;
            }
            else
            {
                foreach (MandatePeriodView period in periods)
                    y = BuildPeriod(period, y, rowWidth);
            }

            SetContentHeight(y + 14f);
        }

        private float BuildStatusPanel(MandateReport pReport, MandatePeriodView pPeriod, float pY, float pWidth)
        {
            GameObject panel = CreatePanel("MandateStatus", TopLeft(PAD, pY), new Vector2(pWidth, STATUS_H), 0.94f);
            string dynasty = pReport.active
                ? Fallback(pReport.dynasty_name, AW_L10n.Text("aw_mandate_dynasty_title", "\u5929\u547D\u738B\u671D"))
                : AW_L10n.Text("aw_mandate_none", "\u5F53\u524D\u6CA1\u6709\u5929\u547D\u738B\u671D");
            string color = pPeriod?.kingdom_color ?? HistoryColors.FromKingdom(MandateService.GetCurrentMandateKingdom());

            CreateFlag(panel.transform, pPeriod, new Vector2(8f, -8f), new Vector2(30f, 30f));
            CreateText("StatusTitle", RichName(dynasty, color), TopLeft(PAD + 42f, pY + 8f),
                new Vector2(pWidth - 52f, 24f), TextAnchor.MiddleLeft, 13,
                new Color(1f, 0.86f, 0.44f, 1f));

            float left = PAD + 10f;
            float right = PAD + pWidth * 0.52f;
            float rowY = pY + 38f;
            if (!pReport.active)
            {
                CreateMiniRow("NoMandate", AW_L10n.Text("aw_mandate_crisis", "\u5929\u547D\u72B6\u6001"),
                    AW_L10n.Text("aw_mandate_none", "\u5F53\u524D\u6CA1\u6709\u5929\u547D\u738B\u671D"), left, rowY, pWidth - 20f);
                return pY + STATUS_H;
            }

            CreateMiniRow("Kingdom", AW_L10n.Text("aw_mandate_kingdom", "\u5929\u547D\u56FD"), pReport.kingdom_name, left, rowY, 150f);
            CreateMiniRow("Emperor", AW_L10n.Text("aw_mandate_emperor", "\u5929\u547D\u7687\u5E1D"), pReport.emperor_name, right, rowY, 150f);
            rowY += 20f;
            CreateMiniRow("Value", AW_L10n.Text("aw_mandate_value", "\u5929\u547D\u503C"), pReport.mandate_value.ToString(), left, rowY, 150f);
            CreateMiniRow("Authority", AW_L10n.Text("aw_mandate_authority", "\u7687\u6743"), pReport.imperial_authority.ToString(), right, rowY, 150f);
            rowY += 20f;
            CreateMiniRow("Core", AW_L10n.Text("aw_mandate_core_control", "\u6CD5\u7406\u63A7\u5236"),
                pReport.controlled_core_count + "/" + pReport.core_count + " " + Mathf.RoundToInt(pReport.core_control * 100f) + "%", left, rowY, 150f);
            CreateMiniRow("Crisis", AW_L10n.Text("aw_mandate_crisis", "\u5929\u547D\u72B6\u6001"), CrisisText(pReport.crisis_level), right, rowY, 150f);

            SetTip(panel, dynasty, BuildStatusTooltip(pReport));
            return pY + STATUS_H;
        }

        private float BuildDecisionSlot(Kingdom pKingdom, float pY, float pWidth)
        {
            MandateDecisionDef def = MandateDecisionService.GetCurrentDef(pKingdom);
            float fraction = MandateDecisionService.GetProgressFraction(pKingdom);
            string title = AW_L10n.Text("aw_mandate_decision_slot", "\u5929\u671D\u51B3\u8BAE");
            string name = def == null
                ? AW_L10n.Text("aw_mandate_decision_idle", "\u65E0\u5F53\u524D\u51B3\u8BAE")
                : AW_L10n.Text(def.NameKey, def.FallbackName);

            GameObject slot = CreatePanel("MandateDecisionSlot", TopLeft(PAD, pY), new Vector2(pWidth, DECISION_H), 0.96f);
            var btn = slot.AddComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                MandateDecisionService.CycleCurrent(pKingdom);
                Refresh();
            });

            CreateIconObject("DecisionIcon", slot.transform, DecisionIcon(def), new Vector2(7f, -7f), new Vector2(20f, 20f));
            CreateText("DecisionText", title + ": " + name + "  " + Mathf.FloorToInt(fraction * 100f) + "%",
                TopLeft(PAD + 34f, pY + 4f), new Vector2(pWidth - 46f, 18f), TextAnchor.MiddleLeft, 10, Color.white);
            CreateProgressBar(slot.transform, fraction, new Vector2(34f, -26f), new Vector2(pWidth - 46f, 6f),
                new Color(1f, 0.72f, 0.26f, 0.9f));
            SetTip(slot, title, BuildDecisionTooltip(pKingdom, def));
            return pY + DECISION_H;
        }

        private float BuildPeriod(MandatePeriodView pPeriod, float pY, float pWidth)
        {
            bool expanded = _expandedPeriods.Contains(pPeriod.index);
            pY = CreateHistoryRow(new HistoryRow
            {
                is_header = true,
                dynasty_index = pPeriod.index,
                expanded = expanded,
                text = BuildPeriodTitle(pPeriod),
                tooltip_title = Fallback(pPeriod.dynasty_name, AW_L10n.Text("aw_mandate_period", "\u5929\u547D\u671D\u4EE3")),
                tooltip_desc = BuildPeriodTooltip(pPeriod)
            }, pY, pWidth);

            if (!expanded) return pY;

            for (int i = 0; i < pPeriod.reigns.Count; i++)
            {
                MandateReignView reign = pPeriod.reigns[i];
                int key = ReignKey(pPeriod.index, i);
                bool reignExpanded = _expandedReigns.Contains(key);
                pY = CreateHistoryRow(new HistoryRow
                {
                    is_header = true,
                    reign_index = key,
                    dynasty_index = -1,
                    expanded = reignExpanded,
                    text = BuildReignTitle(reign),
                    tooltip_title = AW_L10n.Text("aw_mandate_reign_period", "\u5929\u547D\u541B\u4E3B\u65F6\u671F"),
                    tooltip_desc = BuildReignTooltip(reign)
                }, pY, pWidth);

                if (!reignExpanded) continue;

                if (reign.has_king && reign.king_actor_id >= 0)
                {
                    pY = CreateHistoryRow(new HistoryRow
                    {
                        is_action = true,
                        action_actor_id = reign.king_actor_id,
                        text = AW_L10n.Text("aw_view_emperor_biography", "\u67E5\u770B\u5929\u5B50\u4F20\u8BB0\uFF1A") +
                               RichName(DisplayKingName(reign), DisplayKingColor(reign)),
                        tooltip_title = AW_L10n.Text("aw_view_emperor_biography", "\u67E5\u770B\u5929\u5B50\u4F20\u8BB0"),
                        tooltip_desc = AW_L10n.Text("aw_view_emperor_biography_desc", "\u6253\u5F00\u8BE5\u5929\u547D\u541B\u4E3B\u7684\u4EBA\u7269\u4F20\u8BB0")
                    }, pY, pWidth);
                }

                foreach (MandateHistoryEvent e in reign.events)
                    pY = CreateHistoryRow(BuildEventRow(e), pY, pWidth);
            }

            return pY;
        }

        private float CreateHistoryRow(HistoryRow pRow, float pY, float pWidth)
        {
            pRow.width = pWidth;
            var obj = new GameObject("MandateHistoryRow", typeof(RectTransform));
            obj.transform.SetParent(ContentTransform, false);
            _created.Add(obj);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = TopLeft(PAD, pY);

            var item = obj.AddComponent<HistoryListItem>();
            item.Setup(pRow);
            rect.anchoredPosition = TopLeft(PAD, pY);
            return pY + rect.sizeDelta.y + ROW_GAP;
        }

        private HistoryRow BuildEventRow(MandateHistoryEvent pEvent)
        {
            string targetType = "";
            long targetId = -1;
            if (pEvent.actor_id >= 0) { targetType = "actor"; targetId = pEvent.actor_id; }
            else if (pEvent.city_id >= 0) { targetType = "city"; targetId = pEvent.city_id; }
            else if (pEvent.kingdom_id >= 0) { targetType = "kingdom"; targetId = pEvent.kingdom_id; }

            return new HistoryRow
            {
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
            string origin = OriginText(pPeriod.origin_type);
            return RichName(name, pPeriod.kingdom_color) + "  " + origin + "  " +
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
            if (!pReport.active) return AW_L10n.Text("aw_mandate_none", "\u5F53\u524D\u6CA1\u6709\u5929\u547D\u738B\u671D");
            return AW_L10n.Text("aw_mandate_kingdom", "\u5929\u547D\u56FD") + ": " + pReport.kingdom_name +
                   "\n" + AW_L10n.Text("aw_mandate_emperor", "\u5929\u547D\u7687\u5E1D") + ": " + pReport.emperor_name +
                   "\n" + AW_L10n.Text("aw_mandate_value", "\u5929\u547D\u503C") + ": " + pReport.mandate_value +
                   "\n" + AW_L10n.Text("aw_mandate_authority", "\u7687\u6743") + ": " + pReport.imperial_authority +
                   "\n" + AW_L10n.Text("aw_mandate_prestige", "\u738B\u671D\u5A01\u671B") + ": " + pReport.dynasty_prestige +
                   "\n" + AW_L10n.Text("aw_mandate_core_control", "\u6CD5\u7406\u63A7\u5236") + ": " +
                   pReport.controlled_core_count + "/" + pReport.core_count +
                   "\n" + AW_L10n.Text("aw_mandate_vassals", "\u5929\u547D\u9644\u5EB8") + ": " + pReport.vassal_count +
                   "\n" + AW_L10n.Text("aw_mandate_origin", "\u6765\u6E90") + ": " + OriginText(pReport.origin_type) +
                   "\n" + AW_L10n.Text("aw_mandate_claimant", "\u5BA3\u79F0") + ": " + ClaimantText(pReport.claimant_kind);
        }

        private static string BuildDecisionTooltip(Kingdom pKingdom, MandateDecisionDef pDef)
        {
            if (pDef == null)
                return AW_L10n.Text("aw_mandate_decision_idle_desc", "\u70B9\u51FB\u5207\u6362\u4E3A\u53EF\u6267\u884C\u7684\u5929\u671D\u51B3\u8BAE\u3002");

            float progress = MandateDecisionService.GetProgress(pKingdom);
            float remaining = Mathf.Max(0f, pDef.Cost - progress);
            return AW_L10n.Text(pDef.DescKey, pDef.FallbackDesc) +
                   "\n" + AW_L10n.Text("aw_policy_progress", "\u8FDB\u5EA6") + ": " +
                   Mathf.FloorToInt(progress) + "/" + Mathf.CeilToInt(pDef.Cost) +
                   "\n" + AW_L10n.Text("aw_policy_remaining", "\u5269\u4F59") + ": " + Mathf.CeilToInt(remaining) +
                   "\n" + AW_L10n.Text("aw_policy_yearly_gain", "\u5E74\u589E\u957F") + ": " +
                   MandateDecisionService.EstimateYearlyGain(pKingdom).ToString("0.0") +
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
            string desc = AW_L10n.Text("aw_history_type", "\u7C7B\u578B\uFF1A") + pEvent.event_type +
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

        private void CreateMiniRow(string pName, string pLabel, string pValue, float pX, float pY, float pWidth)
        {
            CreateText(pName + "Label", pLabel + ":", TopLeft(pX, pY), new Vector2(72f, 18f),
                TextAnchor.MiddleLeft, 9, new Color(0.8f, 0.8f, 0.8f, 1f));
            CreateText(pName + "Value", pValue ?? "", TopLeft(pX + 74f, pY), new Vector2(pWidth - 74f, 18f),
                TextAnchor.MiddleLeft, 9, Color.white);
        }

        private GameObject CreatePanel(string pName, Vector2 pPos, Vector2 pSize, float pAlpha)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(TipButton));
            obj.transform.SetParent(ContentTransform, false);
            _created.Add(obj);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPos;
            AW_UIStyle.ApplyPanel(obj.GetComponent<Image>(), pAlpha);
            return obj;
        }

        private void CreateText(string pName, string pText, Vector2 pPos, Vector2 pSize,
            TextAnchor pAnchor, int pFontSize, Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(ContentTransform, false);
            _created.Add(obj);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = pPos;
            rect.sizeDelta = pSize;

            var text = obj.GetComponent<Text>();
            text.text = pText ?? "";
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAnchor;
            text.color = pColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
        }

        private void CreateFlag(Transform pParent, MandatePeriodView pPeriod, Vector2 pTopLeft, Vector2 pSize)
        {
            if (pParent == null || pPeriod == null) return;
            var flagObj = new GameObject("Flag", typeof(RectTransform), typeof(Image));
            flagObj.transform.SetParent(pParent, false);
            var rect = flagObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pTopLeft;
            var bg = flagObj.GetComponent<Image>();
            bg.preserveAspect = true;

            var iconObj = new GameObject("FlagIcon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(flagObj.transform, false);
            var iconRect = iconObj.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var icon = iconObj.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            KingdomFlagBuilder.Build(pPeriod.banner_id, pPeriod.banner_icon_id, pPeriod.banner_background_id,
                pPeriod.kingdom_color, pPeriod.kingdom_color_id, bg, icon);
        }

        private static void CreateIconObject(string pName, Transform pParent, Sprite pSprite, Vector2 pTopLeft, Vector2 pSize)
        {
            if (pParent == null || pSprite == null) return;
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pTopLeft;
            rect.sizeDelta = pSize;
            var img = obj.GetComponent<Image>();
            img.sprite = pSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private static void CreateProgressBar(Transform pParent, float pFraction, Vector2 pTopLeft, Vector2 pSize, Color pColor)
        {
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(pParent, false);
            var rect = track.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pTopLeft;
            rect.sizeDelta = pSize;
            var img = track.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = new Color(0f, 0f, 0f, 0.42f);
            img.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(pFraction), 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = WhiteSprite();
            fillImg.color = pColor;
            fillImg.raycastTarget = false;
        }

        private static void SetTip(GameObject pOwner, string pTitle, string pDesc)
        {
            var tip = pOwner.GetComponent<TipButton>();
            if (tip == null) return;
            tip.enabled = true;
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () =>
                Tooltip.show(pOwner, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = pTitle ?? "", tip_description = pDesc ?? "" });
        }

        private static Sprite DecisionIcon(MandateDecisionDef pDef)
        {
            Sprite sprite = pDef == null ? null : SpriteTextureLoader.getSprite(pDef.IconPath);
            return sprite ?? SpriteTextureLoader.getSprite("ui/Icons/traits/iconTianming")
                   ?? SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                   ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
        }

        private void SetContentHeight(float pHeight)
        {
            var contentRect = ContentTransform != null ? ContentTransform.GetComponent<RectTransform>() : null;
            if (contentRect == null) return;
            contentRect.sizeDelta = new Vector2(WINDOW_W - 40f, Mathf.Max(WINDOW_H - 70f, pHeight));
        }

        private void ClearCreated()
        {
            foreach (GameObject obj in _created)
                if (obj != null) Destroy(obj);
            _created.Clear();
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

        private static Vector2 TopLeft(float pX, float pY)
        {
            return new Vector2(pX, -pY);
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _whiteSprite;
        }
    }
}
