using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     编年史查看窗(三来源共用):
    ///     - OpenPerson:事件平铺(年份在前 + 内容在后,时间升序)。
    ///     - OpenKingdom:**按朝代分段折叠**——每个朝代时期一个可点击段头(有王=年号+实际纪年+王名+起止年;
    ///       无王=无王时期+时间区间如"13-18年"),点段头展开/收缩其事件。
    ///     - OpenCity:**按归属期分段折叠**——城市隶属同一王国的连续时期为一段(国名·起止年),
    ///       每次易主开新段,点段头展开/收缩其事件。
    /// </summary>
    internal class HistoryListWindow : AbstractListWindow<HistoryListWindow, HistoryRow>
    {
        private enum Source { Person, Kingdom, City }
        private static Source _source;
        private static long _contextId = -1;

        // 两层折叠：朝代展开集 + 王段展开集
        private readonly HashSet<int> _expandedDynasties = new HashSet<int>();
        private readonly HashSet<int> _expandedReigns    = new HashSet<int>();
        private bool _seeded;
        private List<DynastyView>  _dynasties;
        private List<ReignPeriod>  _reigns;     // 城市史用

        // 人物传记分类筛选（"" = 全部）
        private static string _personFilter = "";

        public static void OpenPerson(long pActorId)
        {
            Source previous = _source;
            _source = Source.Person;
            if (Instance != null && (previous != Source.Person || _contextId != pActorId)) _personFilter = "";
            _contextId = pActorId;
            OpenInternal();
        }

        public static void OpenKingdom(long pKingdomId)
        {
            Source previous = _source;
            _source = Source.Kingdom;
            if (Instance != null && (previous != Source.Kingdom || _contextId != pKingdomId))
            {
                Instance._expandedDynasties.Clear();
                Instance._expandedReigns.Clear();
                Instance._seeded = false;
            }
            _contextId = pKingdomId;
            OpenInternal();
        }

        public static void OpenCity(long pCityId)
        {
            Source previous = _source;
            _source = Source.City;
            if (Instance != null && (previous != Source.City || _contextId != pCityId))
            {
                Instance._expandedDynasties.Clear();
                Instance._expandedReigns.Clear();
                Instance._seeded = false;
            }
            _contextId = pCityId;
            OpenInternal();
        }

        private static void OpenInternal()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.HISTORY);
            // 王段 toggle 回调
            HistoryListItem.OnHeaderToggle = i =>
            {
                if (Instance == null) return;
                if (Instance._expandedReigns.Contains(i)) Instance._expandedReigns.Remove(i);
                else Instance._expandedReigns.Add(i);
                Instance.Refresh();
            };
            // 朝代 toggle 回调（新增）
            HistoryListItem.OnDynastyToggle = i =>
            {
                if (Instance == null) return;
                if (Instance._expandedDynasties.Contains(i)) Instance._expandedDynasties.Remove(i);
                else Instance._expandedDynasties.Add(i);
                Instance.Refresh();
            };
            // 分类筛选 toggle 回调：整行点击→循环切下一个分类
            HistoryListItem.OnFilterToggle = _ =>
            {
                // 循环：全部→life→honor→clan→war→bond→全部
                int cur = 0;
                for (int k = 0; k < CATEGORIES.Length; k++)
                    if (CATEGORIES[k].cat == _personFilter) { cur = k; break; }
                _personFilter = CATEGORIES[(cur + 1) % CATEGORIES.Length].cat;
                Instance?.Refresh();
            };
            HistoryListItem.OnActorBiography = actorId =>
            {
                if (actorId >= 0) OpenPerson(actorId);
            };
            HistoryListItem.OnActorFamilyTree = actorId =>
            {
                if (actorId < 0) return;
                long shiId = LineageQuery.GetActorShiId(actorId);
                FamilyTreeWindow.OpenFamilyTree(actorId, shiId);
            };
            bool wasCurrent = ScrollWindow.isCurrentWindow(AW_LineageWindowIds.HISTORY);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.HISTORY,
                () => { if (Instance != null) Instance.Refresh(); });
            if (!wasCurrent && Instance != null) Instance.Refresh();
        }

        protected override void Init()
        {
            // 使用原版列表窗尺寸。
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            ClearList();
            if (_contextId < 0) return;

            if (_source == Source.Kingdom) { RefreshKingdom(); return; }
            if (_source == Source.City)    { RefreshPeriods(HistoryQuery.GetCityPeriods(_contextId)); return; }

            // 人物：先渲染分类筛选条，再渲染（过滤后的）事件行
            RefreshPerson();
        }

        // ─── 国家史：两层折叠（朝代 → 王段） ───
        private void RefreshKingdom()
        {
            _dynasties = HistoryQuery.GetKingdomDynasties(_contextId);
            if (!_seeded)
            {
                _seeded = true;
            }
            for (int di = 0; di < _dynasties.Count; di++)
            {
                var dyn = _dynasties[di];
                bool dynExp = _expandedDynasties.Contains(di);
                // 朝代段头（dynasty_index≥0）
                AddItemToList(new HistoryRow
                {
                    is_header = true, dynasty_index = di,
                    expanded = dynExp,
                    text = BuildDynastyTitle(dyn),
                    tooltip_title = BuildDynastyDisplayName(dyn),
                    tooltip_desc = BuildDynastyTooltip(dyn)
                });
                if (!dynExp) continue;
                for (int ri = 0; ri < dyn.reigns.Count; ri++)
                {
                    var reign = dyn.reigns[ri];
                    int rKey = di * 1000 + ri;
                    bool rExp = _expandedReigns.Contains(rKey);
                    // 王段段头（reign_index=rKey, dynasty_index=-1）
                    AddItemToList(new HistoryRow
                    {
                        is_header = true, reign_index = rKey, dynasty_index = -1,
                        expanded = rExp,
                        text = BuildReignTitle(reign)
                    });
                    if (!rExp) continue;
                    if (reign.has_king && reign.king_actor_id >= 0)
                    {
                        AddItemToList(new HistoryRow
                        {
                            is_action = true,
                            action_actor_id = reign.king_actor_id,
                            text = BuildBiographyButtonText(reign),
                            dim = true
                        });
                    }
                    foreach (var e in reign.events)
                        AddItemToList(BuildEventRow(e, true));
                }
            }
        }

        // ─── 城市史：单层归属期折叠 ───
        private void RefreshPeriods(List<ReignPeriod> pPeriods)
        {
            _reigns = pPeriods;
            if (!_seeded)
            {
                _seeded = true;
            }
            for (int i = 0; i < _reigns.Count; i++)
            {
                var p = _reigns[i];
                bool expanded = _expandedReigns.Contains(i);
                AddItemToList(new HistoryRow
                {
                    is_header = true, reign_index = i, dynasty_index = -1,
                    expanded = expanded, text = BuildReignTitle(p)
                });
                if (!expanded) continue;
                foreach (var e in p.events)
                    AddItemToList(BuildEventRow(e, true));
            }
        }

        // ─── 人物传记：分类筛选条 + 事件平铺 ───
        private static readonly (string cat, string label)[] CATEGORIES =
        {
            ("",      AW_L10n.Text("aw_history_filter_all", "\u5168\u90E8")),
            (ChronicleCategory.LIFE,  AW_L10n.Text("aw_history_filter_life", "\u4EBA\u751F")),
            (ChronicleCategory.HONOR, AW_L10n.Text("aw_history_filter_honor", "\u8363\u8000")),
            (ChronicleCategory.CLAN,  AW_L10n.Text("aw_history_filter_clan", "\u6C0F\u65CF")),
            (ChronicleCategory.WAR,   AW_L10n.Text("aw_history_filter_war", "\u6218\u4E8B")),
            (ChronicleCategory.BOND,  AW_L10n.Text("aw_history_filter_bond", "\u7F81\u7ECA")),
        };

        private void RefreshPerson()
        {
            AddItemToList(new HistoryRow
            {
                is_action = true,
                action_kind = "family_tree",
                action_actor_id = _contextId,
                text = AW_L10n.Text("aw_action_family_tree", "\u5B9A\u4F4D\u65CF\u8C31"),
                tooltip_title = AW_L10n.Text("aw_action_family_tree", "\u5B9A\u4F4D\u65CF\u8C31"),
                tooltip_desc = AW_L10n.Text("aw_action_family_tree_desc", "\u6253\u5F00\u8BE5\u4EBA\u7684\u5BB6\u5EAD\u6811")
            });

            // 渲染分类筛选条（一行6个 toggle，用 is_filter=true 标记）
            AddItemToList(new HistoryRow
            {
                is_header = false, is_filter = true,
                text = BuildFilterBarText(),
                dim = false
            });
            // 渲染（过滤后的）事件行
            foreach (var e in HistoryQuery.ReadPerson(_contextId))
            {
                if (_personFilter != "" && e.category != _personFilter) continue;
                AddItemToList(BuildEventRow(e, false));
            }
        }

        private static string BuildFilterBarText()
        {
            // 拼成单行文本，HistoryListItem 负责渲染为可点击区域
            var sb = new System.Text.StringBuilder();
            foreach (var (cat, label) in CATEGORIES)
                sb.Append(_personFilter == cat ? "[" + label + "]" : label).Append(" ");
            return sb.ToString().TrimEnd();
        }

        private static string BuildDynastyTitle(DynastyView pDyn)
        {
            string span = YearSpan(pDyn.start_time, pDyn.end_time);
            string name = BuildDynastyDisplayName(pDyn);
            string color = string.IsNullOrEmpty(pDyn.dynasty_color) ? pDyn.kingdom_color : pDyn.dynasty_color;
            return RichName("【" + name + "】", color) + " " + span;
        }

        private static string BuildDynastyDisplayName(DynastyView pDyn)
        {
            if (pDyn != null && !string.IsNullOrEmpty(pDyn.clan_name))
            {
                string ruleName = pDyn.clan_name +
                    AW_L10n.Text("aw_shi_suffix", "\u6C0F") +
                    AW_L10n.Text("aw_dynasty_rule_suffix", "\u7EDF\u6CBB");
                return string.IsNullOrEmpty(pDyn.origin_city_name)
                    ? ruleName
                    : pDyn.origin_city_name + " " + ruleName;
            }

            if (pDyn != null && !string.IsNullOrEmpty(pDyn.dynasty_name))
            {
                string oldName = pDyn.dynasty_name;
                return oldName.EndsWith("\u671D") && oldName.Length > 1
                    ? oldName.Substring(0, oldName.Length - 1) + "\u6C0F\u7EDF\u6CBB"
                    : oldName;
            }

            return AW_L10n.Text("aw_history_early_period", "\u65E9\u671F");
        }

        private static string BuildDynastyTooltip(DynastyView pDyn)
        {
            if (pDyn == null) return "";
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(pDyn.original_kingdom_name))
                sb.Append(AW_L10n.Text("aw_dynasty_original_kingdom", "\u5EFA\u7ACB\u65F6\u56FD\u540D\uFF1A"))
                    .Append(pDyn.original_kingdom_name)
                    .Append("\n");
            if (!string.IsNullOrEmpty(pDyn.founder_king_name))
                sb.Append(AW_L10n.Text("aw_dynasty_founder", "\u5EFA\u7ACB\u8005\uFF1A"))
                    .Append(pDyn.founder_king_name)
                    .Append("\n");
            sb.Append(AW_L10n.Text("aw_dynasty_duration", "\u5B58\u7EED\u65F6\u95F4\uFF1A"))
                .Append(YearSpan(pDyn.start_time, pDyn.end_time))
                .Append("\n");
            sb.Append(AW_L10n.Text("aw_dynasty_end_reason", "\u7ED3\u675F\u539F\u56E0\uFF1A"))
                .Append(EndReasonLabel(pDyn.end_reason, pDyn.end_time))
                .Append("\n");
            return sb.ToString().TrimEnd();
        }

        private static string EndReasonLabel(string pReason, double pEndTime)
        {
            if (pEndTime < 0) return AW_L10n.Text("aw_until_now", "\u81F3\u4ECA");
            switch (pReason)
            {
                case "dynasty_replaced": return AW_L10n.Text("aw_dynasty_end_replaced", "\u6539\u671D\u6362\u4EE3");
                case "kingdom_fell": return AW_L10n.Text("aw_dynasty_end_kingdom_fell", "\u56FD\u5BB6\u706D\u4EA1");
                case "unknown_successor": return AW_L10n.Text("aw_dynasty_end_unknown_successor", "\u738B\u5BA4\u65AD\u7EDD");
                default: return AW_L10n.Text("aw_dynasty_end_unknown", "\u672A\u8BB0\u5F55");
            }
        }

        private static string BuildReignTitle(ReignPeriod pReign)
        {
            string span = YearSpan(pReign.start_time, pReign.end_time);
            if (pReign.is_city_period)
            {
                string owner = string.IsNullOrEmpty(pReign.owner_name)
                    ? AW_L10n.Text("aw_history_no_owner", "\u65E0\u6240\u5C5E")
                    : pReign.owner_name;
                string ownerPart = RichName(owner, string.IsNullOrEmpty(pReign.owner_color) ? pReign.period_color : pReign.owner_color);
                if (!pReign.has_king)
                    return ownerPart + " · " + span;

                string prefix = HistoryWriter.NormalizeYearPrefix(pReign.year_prefix_snapshot, pReign.start_time);
                string era = string.IsNullOrEmpty(prefix) ? "" : RichName(prefix, pReign.period_color) + " · ";
                return era + RichName(DisplayKingName(pReign), DisplayKingColor(pReign)) + " · " + ownerPart + " · " + span;
            }

            if (pReign.has_king)
            {
                string prefix = HistoryWriter.NormalizeYearPrefix(pReign.year_prefix_snapshot, pReign.start_time);
                string era = string.IsNullOrEmpty(prefix) ? "" : RichName(prefix, pReign.period_color) + " · ";
                return era + RichName(DisplayKingName(pReign), DisplayKingColor(pReign)) + " · " + span;
            }
            return AW_L10n.Text("aw_history_no_king_period", "\u65E0\u738B\u65F6\u671F") + " · " + span;
        }

        private static string BuildBiographyButtonText(ReignPeriod pReign)
        {
            return AW_L10n.Text("aw_view_king_biography", "\u67E5\u770B\u541B\u4E3B\u4F20\u8BB0\uFF1A") + RichName(DisplayKingName(pReign), DisplayKingColor(pReign));
        }

        private static string DisplayKingName(ReignPeriod pReign)
        {
            return string.IsNullOrEmpty(pReign.posthumous_title)
                ? pReign.king_name
                : pReign.posthumous_title;
        }

        private static string DisplayKingColor(ReignPeriod pReign)
        {
            return string.IsNullOrEmpty(pReign.posthumous_title)
                ? pReign.king_color
                : (string.IsNullOrEmpty(pReign.posthumous_color) ? pReign.king_color : pReign.posthumous_color);
        }

        private static string RichName(string pText, string pColor)
        {
            return HistoryText.Colored(pText ?? "", pColor).Rich;
        }

        /// <summary>时间区间"6年3月21日-7年1月1日";end<0 用"至今"。</summary>
        private static string YearSpan(double pStart, double pEnd)
        {
            string start = HistoryWriter.FormatDate(pStart);
            if (pEnd < 0) return start + "-" + AW_L10n.Text("aw_until_now", "\u81F3\u4ECA");
            string end = HistoryWriter.FormatDate(pEnd);
            return start == end ? start : start + "-" + end;
        }

        /// <summary>事件行:年份前缀 + 内容。</summary>
        private static string FormatEvent(HistoryEntry pEntry)
        {
            string prefix = !string.IsNullOrEmpty(pEntry.year_prefix_rich)
                ? pEntry.year_prefix_rich
                : HistoryColors.EscapeRich(HistoryWriter.NormalizeYearPrefix(pEntry.year_prefix, pEntry.world_time));
            string year = string.IsNullOrEmpty(prefix) ? "" : prefix + "  ";
            string content = !string.IsNullOrEmpty(pEntry.content_rich)
                ? pEntry.content_rich
                : HistoryColors.EscapeRich(pEntry.content);
            return year + content;
        }

        private static HistoryRow BuildEventRow(HistoryEntry pEntry, bool pDim)
        {
            return new HistoryRow
            {
                is_header = false,
                text = FormatEvent(pEntry),
                dim = pDim,
                target_type = !string.IsNullOrEmpty(pEntry.target_type) ? pEntry.target_type : CurrentTargetType(),
                target_id = pEntry.target_id >= 0 ? pEntry.target_id : _contextId,
                tooltip_title = AW_L10n.Text("aw_history_event", "\u5386\u53F2\u4E8B\u4EF6"),
                tooltip_desc = BuildEventTooltip(pEntry)
            };
        }

        private static string CurrentTargetType()
        {
            if (_source == Source.Person) return "actor";
            if (_source == Source.Kingdom) return "kingdom";
            if (_source == Source.City) return "city";
            return "";
        }

        private static string BuildEventTooltip(HistoryEntry pEntry)
        {
            string type = string.IsNullOrEmpty(pEntry.event_type) ? "" : AW_L10n.Text("aw_history_type", "\u7C7B\u578B\uFF1A") + pEntry.event_type + "\n";
            string time = AW_L10n.Text("aw_history_time", "\u65F6\u95F4\uFF1A") + HistoryWriter.NormalizeYearPrefix(pEntry.year_prefix, pEntry.world_time) + "\n";
            string snapshot = _source == Source.Person ? BuildPersonSnapshotTooltip(pEntry) : "";
            string content = !string.IsNullOrEmpty(pEntry.content_rich)
                ? pEntry.content_rich
                : HistoryColors.EscapeRich(pEntry.content);
            if (pEntry.event_type == KingdomEvent.POSTHUMOUS && pEntry.target_type == "actor" && pEntry.target_id >= 0)
            {
                string extra = PosthumousTitleService.BuildTooltip(pEntry.target_id);
                if (!string.IsNullOrEmpty(extra))
                    return type + time + snapshot + content + "\n\n" + extra;
            }
            return type + time + snapshot + content;
        }

        private static string BuildPersonSnapshotTooltip(HistoryEntry pEntry)
        {
            if (pEntry == null) return "";
            var sb = new System.Text.StringBuilder();
            if (pEntry.age_at_event >= 0)
                sb.Append(AW_L10n.Text("aw_history_age_at_event", "\u65F6\u5E74\uFF1A"))
                    .Append(pEntry.age_at_event)
                    .Append(AW_L10n.Text("aw_age_suffix", "\u5C81"))
                    .Append("\n");
            string role = !string.IsNullOrEmpty(pEntry.role_label) ? pEntry.role_label : RoleLabel(pEntry.role_snapshot);
            if (!string.IsNullOrEmpty(role))
                sb.Append(AW_L10n.Text("aw_history_role_at_event", "\u5F53\u65F6\u8EAB\u4EFD\uFF1A"))
                    .Append(role)
                    .Append("\n");
            return sb.ToString();
        }

        private static string RoleLabel(string pRole)
        {
            switch (pRole)
            {
                case "king": return AW_L10n.Text("aw_role_king", "\u541B\u4E3B");
                case "city_leader": return AW_L10n.Text("aw_role_city_leader", "\u57CE\u4E3B");
                case "clan_chief": return AW_L10n.Text("aw_role_clan_chief", "\u6C0F\u65CF\u5BB6\u4E3B");
                case "royal_guard_captain": return AW_L10n.Text("aw_role_royal_guard_captain", "\u7981\u536B\u519B\u7EDF\u9886");
                case "royal_guard": return AW_L10n.Text("aw_role_royal_guard", "\u7981\u536B\u519B");
                case "slave": return AW_L10n.Text("aw_role_slave", "\u5974\u96B6");
                case "warrior": return AW_L10n.Text("aw_role_warrior", "\u58EB\u5175");
                case "noble": return AW_L10n.Text("aw_role_noble", "\u8D35\u65CF");
                case "common_lineage": return AW_L10n.Text("aw_role_common_lineage", "\u6709\u6C0F\u5E73\u6C11");
                case "common": return AW_L10n.Text("aw_role_common", "\u5E73\u6C11");
                default: return "";
            }
        }

        protected override AbstractListWindowItem<HistoryRow> CreateItemPrefab()
        {
            var obj = new GameObject("HistoryListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<HistoryListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
