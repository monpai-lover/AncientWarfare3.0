using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     编年史查看窗(三来源共用):
    ///     - OpenPerson / OpenCity:事件平铺(年份在前 + 内容在后,时间升序)。
    ///     - OpenKingdom:**按朝代分段折叠**——每个朝代时期一个可点击段头(有王=年号+实际纪年+王名+起止年;
    ///       无王=无王时期+时间区间如"13-18年"),点段头展开/收缩其事件。
    /// </summary>
    internal class HistoryListWindow : AbstractListWindow<HistoryListWindow, HistoryRow>
    {
        private enum Source { Person, Kingdom, City }
        private static Source _source;
        private static long _contextId = -1;

        // 国家史:展开的朝代段序号集合(段头 toggle 用)。默认全部展开。
        private readonly HashSet<int> _expandedReigns = new HashSet<int>();
        private bool _seeded; // 是否已对当前国 seed 默认全展开
        private List<ReignPeriod> _reigns;

        public static void OpenPerson(long pActorId)
        {
            _source = Source.Person; _contextId = pActorId; OpenInternal();
        }

        public static void OpenKingdom(long pKingdomId)
        {
            _source = Source.Kingdom;
            // 换国 → 清折叠状态,下次 RefreshKingdom 会默认全展开。
            if (Instance != null && _contextId != pKingdomId) { Instance._expandedReigns.Clear(); Instance._seeded = false; }
            _contextId = pKingdomId;
            OpenInternal();
        }

        public static void OpenCity(long pCityId)
        {
            _source = Source.City; _contextId = pCityId; OpenInternal();
        }

        private static void OpenInternal()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.HISTORY);
            // 段头 toggle 回调:展开/收缩对应朝代段后重建。
            HistoryListItem.OnHeaderToggle = i =>
            {
                if (Instance == null) return;
                if (Instance._expandedReigns.Contains(i)) Instance._expandedReigns.Remove(i);
                else Instance._expandedReigns.Add(i);
                Instance.Refresh();
            };
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.HISTORY,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init() { }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            ClearList();
            if (_contextId < 0) return;

            if (_source == Source.Kingdom) { RefreshKingdom(); return; }

            // 人物 / 城市:平铺事件行。
            List<HistoryEntry> list = _source == Source.City
                ? HistoryQuery.ReadCity(_contextId)
                : HistoryQuery.ReadPerson(_contextId);
            foreach (var e in list)
                AddItemToList(new HistoryRow { is_header = false, text = FormatEvent(e), dim = false });
        }

        /// <summary>国家史:朝代分段折叠渲染。</summary>
        private void RefreshKingdom()
        {
            _reigns = HistoryQuery.GetKingdomReigns(_contextId);

            // 首次进入该国 → 默认全部段展开(seed 一次);之后用户 toggle 自行控制。
            if (!_seeded)
            {
                _seeded = true;
                for (int j = 0; j < _reigns.Count; j++) _expandedReigns.Add(j);
            }

            for (int i = 0; i < _reigns.Count; i++)
            {
                var p = _reigns[i];
                bool expanded = _expandedReigns.Contains(i);

                AddItemToList(new HistoryRow
                {
                    is_header = true,
                    reign_index = i,
                    expanded = expanded,
                    text = BuildReignTitle(p)
                });

                if (!expanded) continue;
                foreach (var e in p.events)
                    AddItemToList(new HistoryRow { is_header = false, text = FormatEvent(e), dim = true });
            }
        }

        /// <summary>段头标题:有王=年号(纪年)·王名·起止年;无王=无王时期·起止年区间。</summary>
        private static string BuildReignTitle(ReignPeriod pReign)
        {
            string span = YearSpan(pReign.start_time, pReign.end_time);
            if (pReign.has_king)
            {
                // year_prefix_snapshot 已含"X年 年号"快照(写入当时,亡国也准)。
                string era = string.IsNullOrEmpty(pReign.year_prefix_snapshot) ? "" : pReign.year_prefix_snapshot + " · ";
                return era + pReign.king_name + " · " + span;
            }
            return "无王时期 · " + span;
        }

        /// <summary>时间区间"13-18年";end<0 用"至今"。</summary>
        private static string YearSpan(double pStart, double pEnd)
        {
            int y0 = Date.getYear(pStart);
            if (pEnd < 0) return y0 + "年-至今";
            int y1 = Date.getYear(pEnd);
            return y0 == y1 ? (y0 + "年") : (y0 + "-" + y1 + "年");
        }

        /// <summary>事件行:年份前缀 + 内容。</summary>
        private static string FormatEvent(HistoryEntry pEntry)
        {
            string year = string.IsNullOrEmpty(pEntry.year_prefix) ? "" : pEntry.year_prefix + "  ";
            return year + pEntry.content;
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
