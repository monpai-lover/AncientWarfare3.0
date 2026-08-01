using System.Collections.Generic;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.uiquery;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

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
        private static long _personConferKingdomId = -1;

        // 两层折叠：朝代展开集 + 王段展开集
        private readonly HashSet<int> _expandedDynasties = new HashSet<int>();
        private readonly HashSet<int> _expandedReigns    = new HashSet<int>();
        private bool _seeded;
        private List<DynastyView>  _dynasties;
        private List<ReignPeriod>  _reigns;     // 城市史用

        // 人物传记分类筛选（"" = 全部）
        private const string CareerFilter = ChronicleCategory.CAREER;
        private static string _personFilter = "";
        private readonly AWUiQueryState _readQueryState =
            new AWUiQueryState(AW_LineageWindowIds.HISTORY);
        private AWHistoricalWindowReadResult _readResult;
        private long _readResultRevision = -1L;
        private long _pendingContextId = -1L;
        private long _pendingRevision = -1L;
        private string _pendingFilter = "";
        private AWHistoricalWindowSource _pendingSource;
        private bool _readQueryPending;
        private readonly Queue<HistoryRow> _pendingRows =
            new Queue<HistoryRow>();
        private Button _exportButton;
        private Text _exportText;
        private TipButton _exportTip;
        private bool _exportPending;
        private long _exportRequestId = -1L;
        private string _exportRequestKey = "";
        private string _exportFeedbackKey = "";
        private string _exportFeedbackDetail = "";

        public static void OpenPerson(long pActorId, long pConferKingdomId = -1L)
        {
            Source previous = _source;
            long previousKingdomContext = _personConferKingdomId;
            _source = Source.Person;
            if (Instance != null && (previous != Source.Person ||
                                     _contextId != pActorId ||
                                     previousKingdomContext !=
                                     pConferKingdomId))
            {
                Instance.CancelExportRequest();
                _personFilter = "";
            }
            _contextId = pActorId;
            _personConferKingdomId = pConferKingdomId;
            Instance?.InvalidateReadResult();
            OpenInternal();
        }

        public static void OpenKingdom(long pKingdomId)
        {
            Source previous = _source;
            _source = Source.Kingdom;
            if (Instance != null && (previous != Source.Kingdom || _contextId != pKingdomId))
            {
                Instance.CancelExportRequest();
                Instance._expandedDynasties.Clear();
                Instance._expandedReigns.Clear();
                Instance._seeded = false;
            }
            _contextId = pKingdomId;
            Instance?.InvalidateReadResult();
            OpenInternal();
        }

        public static void OpenCity(long pCityId)
        {
            Source previous = _source;
            _source = Source.City;
            if (Instance != null && (previous != Source.City || _contextId != pCityId))
            {
                Instance.CancelExportRequest();
                Instance._expandedDynasties.Clear();
                Instance._expandedReigns.Clear();
                Instance._seeded = false;
            }
            _contextId = pCityId;
            Instance?.InvalidateReadResult();
            OpenInternal();
        }

        public static void ResetWorldCache()
        {
            if (WorldSwitchCacheRules.ShouldClearContextBoundWindow(_contextId)) _contextId = -1;
            _personConferKingdomId = -1L;
            _personFilter = "";
            if (Instance == null) return;
            Instance._expandedDynasties.Clear();
            Instance._expandedReigns.Clear();
            Instance._seeded = false;
            Instance._dynasties = null;
            Instance._reigns = null;
            Instance.CancelExportRequest();
            Instance.InvalidateReadResult();
            Instance.ResetRenderRows();
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
                // 循环：全部→life→honor→career→clan→war→bond→全部
                (string cat, string label)[] categories = BuildCategories();
                int cur = 0;
                for (int k = 0; k < categories.Length; k++)
                    if (categories[k].cat == _personFilter) { cur = k; break; }
                _personFilter = categories[(cur + 1) % categories.Length].cat;
                Instance?.Refresh();
            };
            HistoryListItem.OnActorBiography = (actorId, kingdomId) =>
            {
                if (actorId >= 0) OpenPerson(actorId, kingdomId);
            };
            HistoryListItem.OnConferredPosthumous = (actorId, kingdomId) =>
            {
                if (actorId >= 0 && kingdomId >= 0)
                    ConferredPosthumousTitleWindow.Open(
                        kingdomId, actorId);
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
            CreateExportButton();
            // 使用原版列表窗尺寸。
        }

        public override void OnNormalEnable()
        {
            InvalidateReadResult();
            Refresh();
            RefreshExportButton();
        }

        public override void OnNormalDisable()
        {
            _readQueryState.Close();
            _readQueryPending = false;
            _pendingRows.Clear();
            CancelExportRequest();
        }

        private void CreateExportButton()
        {
            var buttonObject = new GameObject("ExportChronicleText",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            buttonObject.transform.SetParent(BackgroundTransform, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(78f, 20f);
            rect.anchoredPosition = new Vector2(80f, -56f);
            AW_UIStyle.ApplyButton(buttonObject.GetComponent<Image>(), .95f);
            _exportButton = buttonObject.GetComponent<Button>();
            _exportButton.onClick.AddListener(ExportChronicleText);
            _exportTip = buttonObject.GetComponent<TipButton>();
            _exportTip.type = AW_RawTooltip.TYPE;
            _exportTip.hoverAction = ShowExportTooltip;

            var textObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _exportText = textObject.GetComponent<Text>();
            _exportText.font = LocalizedTextManager.current_font;
            _exportText.fontSize = 8;
            _exportText.alignment = TextAnchor.MiddleCenter;
            _exportText.color = Color.white;
            _exportText.raycastTarget = false;
            RefreshExportButton();
        }

        private void ExportChronicleText()
        {
            if (_exportPending || _contextId < 0L) return;
            if (!AW3SaveDirectoryRegistry.TryGet(out string saveDirectory))
            {
                SetExportFeedback("aw_chronicle_export_save_first", "");
                RefreshExportButton();
                return;
            }
            if (!AWHistoricalReadService.Ready)
            {
                SetExportFeedback("aw_chronicle_export_archive_unavailable", "");
                RefreshExportButton();
                return;
            }

            long contextId = _contextId;
            ChronicleTextExportRequest request =
                new ChronicleTextExportRequest(CurrentExportSource(),
                    contextId, ExportDisplayName(), saveDirectory);
            _exportRequestKey = "chronicle-export:" + request.Source + ":" +
                                contextId;
            var execution = new AWChronicleTextExportExecution(request,
                System.DateTime.Now);
            var readRequest = new AWHistoricalReadRequest(_exportRequestKey,
                new AWAsyncStamp(AWAsyncRuntime.WorldGeneration, 0L,
                    HistoricalContentRevision.Current), execution.Execute,
                ApplyExportResult, HandleExportFault,
                pDatabaseEpoch: LineageArchiveManager.RuntimeDatabaseEpoch);
            _exportPending = AWHistoricalReadService.TrySchedule(readRequest,
                out _exportRequestId);
            if (!_exportPending)
            {
                _exportRequestId = -1L;
                _exportRequestKey = "";
                SetExportFeedback("aw_chronicle_export_archive_unavailable", "");
            }
            else
                SetExportFeedback("aw_chronicle_export_pending", "");
            RefreshExportButton();
        }

        private void ApplyExportResult(object pValue)
        {
            if (!_exportPending) return;
            _exportPending = false;
            _exportRequestId = -1L;
            _exportRequestKey = "";
            var result = pValue as ChronicleTextExportResult;
            if (result?.Succeeded == true)
                SetExportFeedback("aw_chronicle_export_success",
                    System.IO.Path.GetFileName(result.Path));
            else
                SetExportFeedback("aw_chronicle_export_failed",
                    result?.Error ?? "");
            RefreshExportButton();
        }

        private void HandleExportFault(System.Exception pError)
        {
            if (!_exportPending) return;
            _exportPending = false;
            _exportRequestId = -1L;
            _exportRequestKey = "";
            SetExportFeedback("aw_chronicle_export_failed",
                pError?.GetBaseException()?.Message ?? "");
            RefreshExportButton();
        }

        private void CancelExportRequest()
        {
            if (_exportRequestId >= 0L &&
                !string.IsNullOrEmpty(_exportRequestKey))
                AWHistoricalReadService.ReleaseRequest(_exportRequestId,
                    _exportRequestKey);
            _exportPending = false;
            _exportRequestId = -1L;
            _exportRequestKey = "";
        }

        private ChronicleTextExportSource CurrentExportSource()
        {
            if (_source == Source.Kingdom)
                return ChronicleTextExportSource.Kingdom;
            if (_source == Source.City) return ChronicleTextExportSource.City;
            return ChronicleTextExportSource.Person;
        }

        private string ExportDisplayName()
        {
            if (_readResult != null && _readResult.ContextId == _contextId &&
                _readResult.Source == CurrentReadSource())
            {
                if (_source == Source.Person && _readResult.Entries.Count > 0)
                    return _readResult.Entries[0]?.subject_name ?? "person";
                if (_source == Source.Kingdom && _readResult.Dynasties.Count > 0)
                {
                    DynastyView dynasty = _readResult.Dynasties[0];
                    if (!string.IsNullOrWhiteSpace(dynasty?.original_kingdom_name))
                        return dynasty.original_kingdom_name;
                    if (!string.IsNullOrWhiteSpace(dynasty?.dynasty_name))
                        return dynasty.dynasty_name;
                }
                if (_source == Source.City && _readResult.Periods.Count > 0)
                {
                    ReignPeriod period = _readResult.Periods[0];
                    if (period?.events != null && period.events.Count > 0)
                        return period.events[0]?.subject_name ?? "city";
                }
            }
            return CurrentExportSource().ToString().ToLowerInvariant();
        }

        private void SetExportFeedback(string pKey, string pDetail)
        {
            _exportFeedbackKey = pKey ?? "";
            _exportFeedbackDetail = pDetail ?? "";
        }

        private void RefreshExportButton()
        {
            if (_exportButton == null || _exportText == null) return;
            bool hasSaveDirectory = AW3SaveDirectoryRegistry.TryGet(out _);
            _exportButton.interactable = _contextId >= 0L &&
                                         hasSaveDirectory && !_exportPending &&
                                         AWHistoricalReadService.Ready;
            _exportText.text = _exportPending
                ? AW_L10n.Text("aw_chronicle_export_pending", "Exporting")
                : AW_L10n.Text("aw_chronicle_export_txt", "Export TXT");
        }

        private void ShowExportTooltip()
        {
            if (_exportTip == null) return;
            string description;
            if (_exportPending)
                description = AW_L10n.Text("aw_chronicle_export_pending",
                    "Exporting complete chronicle.");
            else if (!AW3SaveDirectoryRegistry.TryGet(out _))
                description = AW_L10n.Text("aw_chronicle_export_save_first",
                    "Save the world before exporting its chronicle.");
            else if (!string.IsNullOrEmpty(_exportFeedbackKey))
                description = AW_L10n.Text(_exportFeedbackKey,
                    _exportFeedbackKey) + _exportFeedbackDetail;
            else
                description = AW_L10n.Text("aw_chronicle_export_desc",
                    "Export the complete chronicle with historical dates.");
            Tooltip.show(_exportTip.gameObject, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text("aw_chronicle_export_txt",
                        "Export TXT"),
                    tip_description = description
                });
        }

        public void Refresh()
        {
            ResetRenderRows();
            if (_contextId < 0) return;

            if (TryRefreshFromAsyncRead()) return;
            RefreshSynchronously();
        }

        private void RefreshSynchronously()
        {
            if (_contextId < 0) return;

            if (_source == Source.Kingdom) { RefreshKingdom(); return; }
            if (_source == Source.City)    { RefreshPeriods(HistoryQuery.GetCityPeriods(_contextId)); return; }

            // 人物：先渲染分类筛选条，再渲染（过滤后的）事件行
            RefreshPerson();
        }

        private bool TryRefreshFromAsyncRead()
        {
            AWHistoricalWindowSource source = CurrentReadSource();
            long revision = HistoricalContentRevision.Current;
            if (_readResult != null &&
                _readResult.Source == source &&
                _readResult.ContextId == _contextId &&
                _readResultRevision == revision)
            {
                RenderReadResult(_readResult);
                return true;
            }
            if ((!AWAsyncRuntime.UiEnabled &&
                 !AWAsyncRuntime.ShadowEnabled) ||
                !AWHistoricalReadService.Ready)
            {
                _readQueryPending = false;
                return false;
            }

            string filter = source == AWHistoricalWindowSource.Person
                ? _personFilter
                : string.Empty;
            if (_readQueryPending && _pendingSource == source &&
                _pendingContextId == _contextId &&
                _pendingRevision == revision &&
                string.Equals(_pendingFilter, filter,
                    System.StringComparison.Ordinal))
                return true;

            AWUiQueryKey key = _readQueryState.Begin(_contextId,
                source + ":" + filter, revision);
            _pendingSource = source;
            _pendingContextId = _contextId;
            _pendingRevision = revision;
            _pendingFilter = filter;
            _readQueryPending = true;
            var execution = new AWHistoricalWindowReadExecution(source,
                _contextId);
            var request = new AWHistoricalReadRequest(
                "history-window:" + source + ":" + _contextId,
                new AWAsyncStamp(AWAsyncRuntime.WorldGeneration, 0L,
                    revision), execution.Execute,
                result => ApplyReadResult(key, result),
                error => HandleReadFault(key, error),
                pDatabaseEpoch: LineageArchiveManager.RuntimeDatabaseEpoch);
            if (AWHistoricalReadService.TrySchedule(request)) return true;
            _readQueryPending = false;
            return false;
        }

        private void ApplyReadResult(AWUiQueryKey pKey, object pResult)
        {
            if (!_readQueryState.Accept(pKey)) return;
            _readQueryPending = false;
            if (_contextId != pKey.ContextId ||
                HistoricalContentRevision.Current != pKey.Revision)
            {
                _readResult = null;
                if (isActiveAndEnabled) Refresh();
                return;
            }
            var result = pResult as AWHistoricalWindowReadResult;
            if (result == null || result.ContextId != _contextId ||
                result.Source != CurrentReadSource())
            {
                HandleReadFault(pKey, new System.InvalidOperationException(
                    "historical read returned an invalid result"));
                return;
            }
            FinalizeReadResult(result);
            if (AWAsyncRuntime.ShadowEnabled)
            {
                AWHistoricalWindowReadResult synchronous =
                    BuildSynchronousReadResult(result.Source,
                        result.ContextId);
                AWAsyncShadowRuntime.CompareIds("ui_history",
                    "history:" + pKey.Filter + ":" + pKey.ContextId,
                    ReadResultIds(synchronous), ReadResultIds(result));
                _readQueryState.Close();
                _readResult = null;
                _readResultRevision = -1L;
                ResetRenderRows();
                RenderReadResult(synchronous);
                return;
            }
            _readResult = result;
            _readResultRevision = pKey.Revision;
            if (isActiveAndEnabled) Refresh();
        }

        private void HandleReadFault(AWUiQueryKey pKey,
            System.Exception pError)
        {
            if (!_readQueryState.Accept(pKey)) return;
            _readQueryState.Close();
            _readQueryPending = false;
            _readResult = null;
            ModClass.LogWarning("Historical window read failed: " +
                                (pError?.Message ?? "unknown error"));
            if (!isActiveAndEnabled || _contextId != pKey.ContextId) return;
            ResetRenderRows();
            RefreshSynchronously();
        }

        private static AWHistoricalWindowSource CurrentReadSource()
        {
            if (_source == Source.Kingdom)
                return AWHistoricalWindowSource.Kingdom;
            if (_source == Source.City)
                return AWHistoricalWindowSource.City;
            return AWHistoricalWindowSource.Person;
        }

        private static void FinalizeReadResult(
            AWHistoricalWindowReadResult pResult)
        {
            if (pResult.Source == AWHistoricalWindowSource.Kingdom)
                HistoryQuery.FinalizeKingdomDynasties(pResult.ContextId,
                    pResult.Dynasties);
            else if (pResult.Source == AWHistoricalWindowSource.City)
                HistoryQuery.FinalizeCityPeriods(pResult.Periods);
            else
                HistoryQuery.FinalizePersonEntries(pResult.Entries);
        }

        private static AWHistoricalWindowReadResult BuildSynchronousReadResult(
            AWHistoricalWindowSource pSource, long pContextId)
        {
            AWHistoricalWindowReadResult result;
            if (pSource == AWHistoricalWindowSource.Kingdom)
                result = AWHistoricalWindowReadResult.ForKingdom(pContextId,
                    HistoryQuery.GetKingdomDynasties(pContextId));
            else if (pSource == AWHistoricalWindowSource.City)
                result = AWHistoricalWindowReadResult.ForCity(pContextId,
                    HistoryQuery.GetCityPeriods(pContextId));
            else
                result = AWHistoricalWindowReadResult.ForPerson(pContextId,
                    HistoryQuery.ReadPerson(pContextId));
            FinalizeReadResult(result);
            return result;
        }

        private static long[] ReadResultIds(
            AWHistoricalWindowReadResult pResult)
        {
            var result = new List<long>();
            if (pResult == null) return result.ToArray();
            if (pResult.Source == AWHistoricalWindowSource.Person)
            {
                foreach (HistoryEntry entry in pResult.Entries)
                    if (entry != null) result.Add(entry.event_id);
                return result.ToArray();
            }
            if (pResult.Source == AWHistoricalWindowSource.City)
            {
                for (int periodIndex = 0;
                     periodIndex < pResult.Periods.Count; periodIndex++)
                {
                    result.Add(long.MinValue + periodIndex);
                    ReignPeriod period = pResult.Periods[periodIndex];
                    if (period == null) continue;
                    foreach (HistoryEntry entry in period.events)
                        if (entry != null) result.Add(entry.event_id);
                }
                return result.ToArray();
            }
            for (int dynastyIndex = 0;
                 dynastyIndex < pResult.Dynasties.Count; dynastyIndex++)
            {
                result.Add(long.MinValue + dynastyIndex);
                DynastyView dynasty = pResult.Dynasties[dynastyIndex];
                if (dynasty == null) continue;
                for (int reignIndex = 0;
                     reignIndex < dynasty.reigns.Count; reignIndex++)
                {
                    result.Add(long.MinValue / 2L + reignIndex);
                    ReignPeriod reign = dynasty.reigns[reignIndex];
                    if (reign == null) continue;
                    foreach (HistoryEntry entry in reign.events)
                        if (entry != null) result.Add(entry.event_id);
                }
            }
            return result.ToArray();
        }

        private void RenderReadResult(AWHistoricalWindowReadResult pResult)
        {
            if (pResult.Source == AWHistoricalWindowSource.Kingdom)
            {
                RenderKingdom(pResult.Dynasties);
                return;
            }
            if (pResult.Source == AWHistoricalWindowSource.City)
            {
                RefreshPeriods(pResult.Periods);
                return;
            }
            RefreshPerson(pResult.Entries);
        }

        private void InvalidateReadResult()
        {
            _readQueryState.Close();
            _readQueryPending = false;
            _readResult = null;
            _readResultRevision = -1L;
            _pendingContextId = -1L;
            _pendingRevision = -1L;
            _pendingFilter = string.Empty;
            _pendingRows.Clear();
        }

        private void ResetRenderRows()
        {
            _pendingRows.Clear();
            ClearList();
        }

        private void QueueHistoryRow(HistoryRow pRow)
        {
            if (pRow != null) _pendingRows.Enqueue(pRow);
        }

        private void Update()
        {
            int maximum = AWUiCandidateRules.TakeRenderBatch(
                _pendingRows.Count, 4);
            if (maximum <= 0) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            long budget = System.Math.Max(1L,
                System.Diagnostics.Stopwatch.Frequency / 1000L);
            for (int index = 0; index < maximum; index++)
            {
                if (_pendingRows.Count == 0) break;
                AddItemToList(_pendingRows.Dequeue());
                if (System.Diagnostics.Stopwatch.GetTimestamp() - started >=
                    budget) break;
            }
        }

        // ─── 国家史：两层折叠（朝代 → 王段） ───
        private void RefreshKingdom()
        {
            RenderKingdom(HistoryQuery.GetKingdomDynasties(_contextId));
        }

        private void RenderKingdom(List<DynastyView> pDynasties)
        {
            _dynasties = pDynasties ?? new List<DynastyView>();
            if (!_seeded)
            {
                _seeded = true;
            }
            for (int di = 0; di < _dynasties.Count; di++)
            {
                var dyn = _dynasties[di];
                bool dynExp = _expandedDynasties.Contains(di);
                // 朝代段头（dynasty_index≥0）
                QueueHistoryRow(new HistoryRow
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
                    QueueHistoryRow(new HistoryRow
                    {
                        is_header = true, reign_index = rKey, dynasty_index = -1,
                        expanded = rExp,
                        text = BuildReignTitle(reign)
                    });
                    if (!rExp) continue;
                    if (reign.has_king && reign.king_actor_id >= 0)
                    {
                        QueueHistoryRow(new HistoryRow
                        {
                            is_action = true,
                            action_actor_id = reign.king_actor_id,
                            action_kingdom_id = _contextId,
                            text = BuildBiographyButtonText(reign),
                            dim = true
                        });
                    }
                    foreach (var e in reign.events)
                        QueueHistoryRow(BuildEventRow(e, true));
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
                QueueHistoryRow(new HistoryRow
                {
                    is_header = true, reign_index = i, dynasty_index = -1,
                    expanded = expanded, text = BuildReignTitle(p)
                });
                if (!expanded) continue;
                foreach (var e in p.events)
                    QueueHistoryRow(BuildEventRow(e, true));
            }
        }

        // ─── 人物传记：分类筛选条 + 事件平铺 ───
        private static (string cat, string label)[] BuildCategories()
        {
            return new[]
            {
                ("", AW_L10n.Text("aw_history_filter_all", "\u5168\u90E8")),
                (ChronicleCategory.LIFE,
                    AW_L10n.Text("aw_history_filter_life", "\u4EBA\u751F")),
                (ChronicleCategory.HONOR,
                    AW_L10n.Text("aw_history_filter_honor", "\u8363\u8000")),
                (CareerFilter, AW_L10n.Text("aw_history_filter_career", "\u4ED5\u9014")),
                (ChronicleCategory.CLAN,
                    AW_L10n.Text("aw_history_filter_clan", "\u6C0F\u65CF")),
                (ChronicleCategory.WAR,
                    AW_L10n.Text("aw_history_filter_war", "\u6218\u4E8B")),
                (ChronicleCategory.BOND,
                    AW_L10n.Text("aw_history_filter_bond", "\u7F81\u7ECA"))
            };
        }

        private void RefreshPerson(List<HistoryEntry> pEntries = null)
        {
            QueueHistoryRow(new HistoryRow
            {
                is_action = true,
                action_kind = "family_tree",
                action_actor_id = _contextId,
                text = AW_L10n.Text("aw_action_family_tree", "\u5B9A\u4F4D\u65CF\u8C31"),
                tooltip_title = AW_L10n.Text("aw_action_family_tree", "\u5B9A\u4F4D\u65CF\u8C31"),
                tooltip_desc = AW_L10n.Text("aw_action_family_tree_desc", "\u6253\u5F00\u8BE5\u4EBA\u7684\u5BB6\u5EAD\u6811")
            });

            AddConferredPosthumousRow();

            // 渲染分类筛选条（一行6个 toggle，用 is_filter=true 标记）
            QueueHistoryRow(new HistoryRow
            {
                is_header = false, is_filter = true,
                text = BuildFilterBarText(),
                dim = false
            });

            if (_personFilter == CareerFilter)
            {
                RefreshCareer(pEntries);
                return;
            }

            // 渲染（过滤后的）事件行
            foreach (var e in pEntries ?? HistoryQuery.ReadPerson(_contextId))
            {
                if (_personFilter != "" && e.category != _personFilter) continue;
                QueueHistoryRow(BuildEventRow(e, false));
            }
        }

        public static void RefreshPersonAfterConferment(
            long pActorId, long pKingdomId)
        {
            if (Instance == null || _source != Source.Person ||
                _contextId != pActorId ||
                _personConferKingdomId != pKingdomId) return;
            Instance.InvalidateReadResult();
            Instance.Refresh();
        }

        private void AddConferredPosthumousRow()
        {
            if (_personConferKingdomId < 0 || _contextId < 0) return;
            ConferredPosthumousPreview preview =
                ConferredPosthumousTitleService.Prepare(
                    _personConferKingdomId, _contextId);
            if (preview.Result == ConferredPosthumousResult.AlreadyTitled)
            {
                QueueHistoryRow(new HistoryRow
                {
                    text = AW_L10n.Text("aw_conferred_existing", "Posthumous title") +
                           ": " + preview.ExistingTitle,
                    tooltip_title = AW_L10n.Text(
                        "aw_conferred_existing", "Posthumous title"),
                    tooltip_desc = preview.ExistingTitle,
                    dim = false
                });
                return;
            }
            if (preview.Result == ConferredPosthumousResult.TargetLiving ||
                preview.Result == ConferredPosthumousResult.MissingArchive ||
                preview.Result ==
                    ConferredPosthumousResult.NoHistoricalRelationship ||
                preview.Result == ConferredPosthumousResult.MissingContext ||
                preview.Result == ConferredPosthumousResult.InvalidKingdom ||
                !ConferredPosthumousTitleRules.IsEligibleRole(preview.Roles))
                return;

            bool enabled = preview.Result ==
                           ConferredPosthumousResult.Success;
            string reason = enabled
                ? AW_L10n.Text("aw_conferred_action_desc",
                    "Review the automatically evaluated posthumous title")
                : ConferredResultText(preview);
            QueueHistoryRow(new HistoryRow
            {
                is_action = true,
                action_kind = "conferred_posthumous",
                action_actor_id = _contextId,
                action_kingdom_id = _personConferKingdomId,
                action_enabled = enabled,
                text = enabled
                    ? AW_L10n.Text("aw_conferred_action", "Confer posthumous title")
                    : AW_L10n.Text("aw_conferred_unavailable", "Conferment unavailable"),
                tooltip_title = enabled
                    ? preview.DisplayTitle
                    : AW_L10n.Text("aw_conferred_unavailable",
                        "Conferment unavailable"),
                tooltip_desc = reason
            });
        }

        private static string ConferredResultText(
            ConferredPosthumousPreview pPreview)
        {
            if (pPreview.Result == ConferredPosthumousResult.Cooldown)
                return string.Format(AW_L10n.Text(
                        "aw_conferred_result_cooldown",
                        "The realm must wait {0} more years"),
                    pPreview.CooldownRemaining);
            string key = pPreview.Result switch
            {
                ConferredPosthumousResult.NoTitleAvailable =>
                    "aw_conferred_result_no_title",
                ConferredPosthumousResult.PersistenceFailed =>
                    "aw_conferred_result_persistence_failed",
                _ => "aw_conferred_result_unavailable"
            };
            return AW_L10n.Text(key, "Conferment is unavailable");
        }

        private void RefreshCareer(List<HistoryEntry> pEntries = null)
        {
            List<OfficialCareerReadModel> career =
                OfficialCareerService.LoadCareer(_contextId);
            var careerEvents = new List<HistoryEntry>();
            foreach (HistoryEntry entry in pEntries ??
                     HistoryQuery.ReadPerson(_contextId))
            {
                if (OfficialCareerBiographyRules.IsCareerEvent(entry.event_type))
                    careerEvents.Add(entry);
            }

            if (career.Count == 0 && careerEvents.Count == 0)
            {
                QueueHistoryRow(new HistoryRow
                {
                    text = AW_L10n.Text("aw_career_empty", "No recorded official career"),
                    dim = true,
                    tooltip_title = AW_L10n.Text("aw_history_filter_career", "Career"),
                    tooltip_desc = AW_L10n.Text("aw_career_empty", "No recorded official career")
                });
                return;
            }

            foreach (OfficialCareerReadModel record in career)
                QueueHistoryRow(BuildCareerRow(record));
            foreach (HistoryEntry entry in careerEvents)
                QueueHistoryRow(BuildEventRow(entry, false));
        }

        private static HistoryRow BuildCareerRow(OfficialCareerReadModel pCareer)
        {
            string office = CareerOfficeLabel(pCareer.OfficeId,
                pCareer.InstitutionAtAppointment);
            string kingdomName = string.IsNullOrEmpty(pCareer.KingdomName)
                ? AW_L10n.Text("aw_career_unknown_kingdom", "Unknown realm")
                : pCareer.KingdomName;
            string kingdom = RichName(kingdomName, pCareer.KingdomColor);
            var text = new System.Text.StringBuilder();
            text.Append(AW_L10n.Text("aw_career_office", "Office"))
                .Append(": ").Append(HistoryColors.EscapeRich(office)).Append("\n")
                .Append(AW_L10n.Text("aw_career_kingdom", "Realm"))
                .Append(": ").Append(kingdom);
            if (pCareer.HasCity)
            {
                string city = string.IsNullOrEmpty(pCareer.CityName)
                    ? AW_L10n.Text("aw_career_unknown_city", "Unknown city")
                    : pCareer.CityName;
                text.Append("  ").Append(AW_L10n.Text("aw_career_city", "City"))
                    .Append(": ").Append(HistoryColors.EscapeRich(city));
            }
            if (pCareer.RankAtAppointment > 0)
                text.Append("\n").Append(AW_L10n.Text(
                        "aw_court_official_rank", "Official rank"))
                    .Append(": ").Append(AW_L10n.Text(
                        OfficialCareerRankRules.RankNameKey(
                            pCareer.RankAtAppointment),
                        OfficialCareerRankRules.RankFallbackEnglish(
                            pCareer.RankAtAppointment)));
            if (pCareer.LocalGradeAtAppointment > 0)
                text.Append("  ").Append(AW_L10n.Text(
                        "aw_court_local_grade", "Local grade"))
                    .Append(": ").Append(AW_L10n.Text(
                        NineRankRules.GradeNameKey(
                            pCareer.LocalGradeAtAppointment),
                        NineRankRules.GradeFallbackEnglish(
                            pCareer.LocalGradeAtAppointment)));

            text.Append("\n").Append(AW_L10n.Text("aw_career_appointed", "Appointed"))
                .Append(": ").Append(CareerTimeLabel(pCareer.AppointedTime)).Append("  ");
            if (pCareer.IsCurrent)
            {
                text.Append(AW_L10n.Text("aw_career_current", "Currently serving"));
            }
            else
            {
                text.Append(AW_L10n.Text("aw_career_ended", "Ended"))
                    .Append(": ").Append(CareerTimeLabel(pCareer.EndedTime))
                    .Append("\n").Append(AW_L10n.Text("aw_career_end_reason", "Reason"))
                    .Append(": ").Append(HistoryColors.EscapeRich(
                        CareerEndReasonLabel(pCareer.EndReason)));
            }

            return new HistoryRow
            {
                text = text.ToString(),
                dim = false,
                tooltip_title = office,
                tooltip_desc = text.ToString()
            };
        }

        private static string CareerOfficeLabel(string pOfficeId,
            string pInstitution)
        {
            string unknown = AW_L10n.Text("aw_career_unknown_office", "Unknown office");
            if (string.IsNullOrWhiteSpace(pOfficeId)) return unknown;
            string fallback = unknown + " (" + HumanizeIdentifier(pOfficeId) + ")";
            return AW_L10n.Text(
                CourtInstitutionRules.OfficeLocalizationKey(
                    pInstitution, pOfficeId), fallback);
        }

        private static string CareerEndReasonLabel(string pReason)
        {
            string unknown = AW_L10n.Text("aw_career_unknown_reason", "Unknown reason");
            if (string.IsNullOrWhiteSpace(pReason)) return unknown;
            string fallback = unknown + " (" + HumanizeIdentifier(pReason) + ")";
            return AW_L10n.Text("aw_career_end_reason_" + pReason, fallback);
        }

        private static string CareerTimeLabel(double pTime)
        {
            return pTime < 0
                ? AW_L10n.Text("aw_career_unknown_time", "Unknown time")
                : HistoryWriter.FormatDate(pTime);
        }

        private static string HumanizeIdentifier(string pValue)
        {
            return string.IsNullOrWhiteSpace(pValue)
                ? ""
                : pValue.Trim().Replace('_', ' ').Replace('-', ' ');
        }

        private static string BuildFilterBarText()
        {
            // 拼成单行文本，HistoryListItem 负责渲染为可点击区域
            var sb = new System.Text.StringBuilder();
            foreach (var (cat, label) in BuildCategories())
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
                string chronology = BuildReignChronologySpan(pReign);
                if (!string.IsNullOrEmpty(chronology))
                    return chronology;
                int[] raw = Date.getRawDate(pReign.start_time);
                string fallback = GanzhiChronologyRules.GetYearName(raw[2]) +
                                  " " + DisplayKingName(pReign);
                return RichName(fallback, pReign.period_color);
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

        private static string BuildReignChronologySpan(ReignPeriod pReign)
        {
            if (pReign == null || pReign.start_time < 0d) return "";
            double endpoint = pReign.end_time >= 0d
                ? pReign.end_time
                : World.world != null
                    ? World.world.getCurWorldTime()
                    : pReign.start_time;
            if (pReign.era_periods != null &&
                pReign.era_periods.Count > 0)
            {
                var lines = new List<string>();
                EraChronologyPeriod firstEra = pReign.era_periods[0];
                if (firstEra != null &&
                    EraChronologyPeriodRules.ShouldAddPreEraSpan(
                        pReign.start_time, firstEra.start_time))
                {
                    double preEraEnd = System.Math.Min(endpoint,
                        firstEra.start_time);
                    int[] preStart = Date.getRawDate(pReign.start_time);
                    int[] preEnd = Date.getRawDate(preEraEnd);
                    int preEndYear = ReignHeaderChronologyRules
                        .CalculateReignYear(preStart[2], preStart[1],
                            preStart[0], preEnd[2], preEnd[1],
                            preEnd[0]);
                    string preEraLine = ReignHeaderChronologyRules
                        .FormatSpan(
                            GanzhiChronologyRules.GetYearName(preStart[2]),
                            BuildReignChronology(pReign, 1),
                            GanzhiChronologyRules.GetYearName(preEnd[2]),
                            BuildReignChronology(pReign, preEndYear));
                    if (!string.IsNullOrEmpty(preEraLine))
                        lines.Add(RichName(preEraLine,
                            pReign.period_color));
                }
                for (int index = 0;
                     index < pReign.era_periods.Count; index++)
                {
                    EraChronologyPeriod era =
                        pReign.era_periods[index];
                    if (era == null ||
                        !EraNameRules.IsValidCustom(era.era_stem))
                        continue;
                    double nextStart = index + 1 <
                                       pReign.era_periods.Count
                        ? pReign.era_periods[index + 1].start_time
                        : -1d;
                    double eraEnd = EraChronologyPeriodRules.ResolveEnd(
                        era.start_time, era.end_time, nextStart,
                        pReign.end_time, endpoint);
                    if (!EraChronologyPeriodRules.OverlapsReign(
                            era.start_time, eraEnd,
                            pReign.start_time, pReign.end_time))
                        continue;
                    double eraStart = System.Math.Max(
                        era.start_time, pReign.start_time);
                    eraEnd = System.Math.Min(eraEnd, endpoint);
                    int[] eraStartDate = Date.getRawDate(eraStart);
                    int[] eraOriginDate = Date.getRawDate(era.start_time);
                    int[] eraEndDate = Date.getRawDate(eraEnd);
                    int startEraYear = ReignHeaderChronologyRules
                        .CalculateReignYear(eraOriginDate[2],
                            eraOriginDate[1], eraOriginDate[0],
                            eraStartDate[2], eraStartDate[1],
                            eraStartDate[0]);
                    int endEraYear = ReignHeaderChronologyRules
                        .CalculateReignYear(eraOriginDate[2],
                            eraOriginDate[1], eraOriginDate[0],
                            eraEndDate[2], eraEndDate[1],
                            eraEndDate[0]);
                    string line = ReignHeaderChronologyRules.FormatSpan(
                        GanzhiChronologyRules.GetYearName(
                            eraStartDate[2]),
                        era.era_stem +
                        EraNameRules.FormatYear(startEraYear),
                        GanzhiChronologyRules.GetYearName(eraEndDate[2]),
                        era.era_stem +
                        EraNameRules.FormatYear(endEraYear));
                    if (!string.IsNullOrEmpty(line))
                        lines.Add(RichName(line,
                            string.IsNullOrEmpty(era.era_color)
                                ? pReign.period_color
                                : era.era_color));
                }
                if (lines.Count > 0) return string.Join("\n", lines);
            }

            int[] start = Date.getRawDate(pReign.start_time);
            int[] end = Date.getRawDate(endpoint);
            int endReignYear = ReignHeaderChronologyRules.CalculateReignYear(
                start[2], start[1], start[0], end[2], end[1], end[0]);
            string startChronology = BuildReignChronology(pReign, 1);
            string endChronology = BuildReignChronology(
                pReign, endReignYear);
            return RichName(ReignHeaderChronologyRules.FormatSpan(
                GanzhiChronologyRules.GetYearName(start[2]),
                startChronology,
                GanzhiChronologyRules.GetYearName(end[2]),
                endChronology), pReign.period_color);
        }

        private static string BuildReignChronology(ReignPeriod pReign,
            int pReignYear)
        {
            if (EraNameRules.IsValidCustom(pReign.formal_era_stem))
                return pReign.formal_era_stem +
                       EraNameRules.FormatYear(pReignYear);
            if (string.IsNullOrWhiteSpace(pReign.state_name_snapshot) ||
                string.IsNullOrWhiteSpace(pReign.given_name) ||
                pReign.title_rank < 0) return "";
            int rank = Mathf.Clamp(pReign.title_rank,
                (int)KingdomTitle.Baron, (int)KingdomTitle.Emperor);
            return RegnalChronologyRules.Format(
                pReign.state_name_snapshot,
                KingdomTitleService.GetTitleChar((KingdomTitle)rank),
                pReign.given_name, pReignYear,
                isHereditaryMonarchy: true, isRepublic: false);
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
            content = NormalizeLegacyCareerKeys(content);
            return year + WarDisplayLabelRules.NormalizeEmbeddedKeys(content);
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
            string type = string.IsNullOrEmpty(pEntry.event_type)
                ? ""
                : AW_L10n.Text("aw_history_type", "\u7C7B\u578B\uFF1A") +
                  WarDisplayLabelRules.EventLabel(pEntry.event_type) + "\n";
            string time = AW_L10n.Text("aw_history_time", "\u65F6\u95F4\uFF1A") + HistoryWriter.NormalizeYearPrefix(pEntry.year_prefix, pEntry.world_time) + "\n";
            string snapshot = _source == Source.Person ? BuildPersonSnapshotTooltip(pEntry) : "";
            string content = !string.IsNullOrEmpty(pEntry.content_rich)
                ? pEntry.content_rich
                : HistoryColors.EscapeRich(pEntry.content);
            content = NormalizeLegacyCareerKeys(content);
            content = WarDisplayLabelRules.NormalizeEmbeddedKeys(content);
            if (pEntry.event_type == KingdomEvent.POSTHUMOUS && pEntry.target_type == "actor" && pEntry.target_id >= 0)
            {
                string extra = PosthumousTitleService.BuildTooltip(pEntry.target_id);
                if (!string.IsNullOrEmpty(extra))
                    return type + time + snapshot + content + "\n\n" + extra;
            }
            return type + time + snapshot + content;
        }

        private static string NormalizeLegacyCareerKeys(string pText)
        {
            return OfficialCareerBiographyRules.NormalizeLegacyLocalizationKeys(
                pText,
                rank => AW_L10n.Text(
                    OfficialCareerRankRules.RankNameKey(rank),
                    OfficialCareerRankRules.RankFallbackEnglish(rank)),
                key => AW_L10n.Text(key,
                    HistoryLocalizationRules.Text(key, "en")));
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
                case "heir_shizi": return AW_L10n.Text("aw_heir_shizi", "\u4E16\u5B50");
                case "republic_head": return AW_L10n.Text("aw_republic_head", "\u5143\u9996");
                case "republic_elder": return AW_L10n.Text("aw_republic_elder", "\u5143\u8001");
                case "heir_taizi": return AW_L10n.Text("aw_heir_taizi", "\u592A\u5B50");
                case "city_leader": return AW_L10n.Text("aw_role_city_leader", "\u57CE\u4E3B");
                case "clan_chief": return AW_L10n.Text("aw_role_clan_chief", "\u6C0F\u65CF\u5BB6\u4E3B");
                case "royal_guard_captain": return AW_L10n.Text("aw_role_royal_guard_captain", "\u7981\u536B\u519B\u7EDF\u9886");
                case "royal_guard": return AW_L10n.Text("aw_role_royal_guard", "\u7981\u536B\u519B");
                case "fief_holder": return AW_L10n.Text("aw_role_fief_holder", "\u5C01\u5730\u5927\u5C06");
                case "general": return AW_L10n.Text("aw_role_general", "\u5927\u5C06");
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
