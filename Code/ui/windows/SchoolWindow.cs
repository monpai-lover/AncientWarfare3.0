using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class SchoolWindow : AbstractWindow<SchoolWindow>
    {
        private const float Width = 560f;
        private const float Height = 340f;
        private const float LeftWidth = 180f;
        private const float Gap = 6f;
        private const float ContentMargin = 14f;
        private const float RefreshCheckInterval = 0.25f;

        private enum SortMode
        {
            Historical,
            Influence,
            Cities
        }

        private sealed class SchoolMetrics
        {
            public int Members;
            public int Cities;
            public float Influence;
        }

        private static string _requestedSchool = CourtSchoolId.Ru;
        private static long _requestedCity = -1L;
        private readonly List<SchoolListItem> _listItems = new List<SchoolListItem>();
        private readonly List<SchoolInfluenceBar> _bars = new List<SchoolInfluenceBar>();
        private readonly List<SchoolActorCardView> _actorCards = new List<SchoolActorCardView>();
        private readonly List<SchoolMasterCardView> _masterCards =
            new List<SchoolMasterCardView>();
        private readonly List<SchoolInstitutionRowView> _institutionRows =
            new List<SchoolInstitutionRowView>();
        private readonly List<SchoolLineageRowView> _lineageRows =
            new List<SchoolLineageRowView>();
        private RectTransform _listContent;
        private RectTransform _detailPanel;
        private RectTransform _detailContent;
        private ScrollRect _detailScroll;
        private Text _detailTitle;
        private Text _detailBody;
        private Button _rosterButton;
        private Text _rosterButtonText;
        private SchoolInfluenceBreakdownView _breakdown;
        private Text _sortText;
        private SortMode _sortMode;
        private string _selectedSchool = CourtSchoolId.Ru;
        private long _selectedCity = -1L;
        private int _displayedSnapshotGeneration = -1;
        private HistoricalSchoolRosterRevisionStamp _displayedSchoolRevisionStamp;
        private float _nextRefreshCheckTime;

        public static void OpenSchool(string pSchoolId = CourtSchoolId.Ru)
        {
            _requestedSchool = CourtSchoolRegistry.Find(pSchoolId) == null ? CourtSchoolId.Ru : pSchoolId;
            _requestedCity = -1L;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.SCHOOL);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.SCHOOL,
                () => Instance?.ApplyRequestAndRefresh());
        }

        public static void OpenCity(long pCityId)
        {
            _requestedCity = pCityId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.SCHOOL);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.SCHOOL,
                () => Instance?.ApplyRequestAndRefresh());
        }

        public static void ResetWorldCache()
        {
            _requestedSchool = CourtSchoolId.Ru;
            _requestedCity = -1L;
            Instance?.ResetWorldState();
        }

        protected override void Init()
        {
            ConfigureWindow();
            BuildUi();
        }

        public override void OnNormalEnable()
        {
            SchoolMapModeService.BeginWindowMode();
            _nextRefreshCheckTime = 0f;
            ApplyRequestAndRefresh();
        }

        public override void OnNormalDisable()
        {
            SchoolMapModeService.EndWindowMode();
            HideDetailRows(pReleaseActorReferences: true);
        }

        private void Update()
        {
            if (!isActiveAndEnabled || World.world == null ||
                Time.unscaledTime < _nextRefreshCheckTime) return;
            _nextRefreshCheckTime = Time.unscaledTime + RefreshCheckInterval;
            if (CitySchoolSnapshotService.HasPendingDemand) return;
            bool schoolRevisionCurrent = _selectedCity >= 0 ||
                _displayedSchoolRevisionStamp != null &&
                _displayedSchoolRevisionStamp.IsCurrent(_selectedSchool,
                    HistoricalSchoolRevisionService.Source);
            if (_displayedSnapshotGeneration == CitySchoolSnapshotService.Generation &&
                schoolRevisionCurrent) return;
            Refresh();
        }

        private void ConfigureWindow()
        {
            RectTransform background = BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = new Vector2(Width, Height);
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null) close.localPosition = new Vector3(Width * .5f - 20f, Height * .5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(300f, 30f);
                titleRect.localPosition = new Vector3(0f, Height * .5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text("aw_school_window_title", "Hundred Schools");
                window.titleText.transform.localPosition = new Vector3(0f, Height * .5f - 16f, 0f);
            }
            Transform scroll = BackgroundTransform?.Find("Scroll View");
            RectTransform scrollRect = scroll?.GetComponent<RectTransform>();
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(Width - 36f, Height - 58f);
                scrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect originalScroll = scroll?.GetComponent<ScrollRect>();
            if (originalScroll != null) originalScroll.enabled = false;
            RectTransform content = ContentTransform?.GetComponent<RectTransform>();
            if (content != null) content.sizeDelta = new Vector2(Width - 36f, Height - 58f);
            foreach (LayoutGroup layout in ContentTransform?.GetComponents<LayoutGroup>() ?? Array.Empty<LayoutGroup>())
                layout.enabled = false;
        }

        private void BuildUi()
        {
            if (ContentTransform == null) return;
            float contentWidth = Width - 36f;
            float contentHeight = Height - 58f;
            RectTransform left = Panel("SchoolListPanel", ContentTransform, new Vector2(0f, 1f),
                new Vector2(ContentMargin, -ContentMargin), new Vector2(LeftWidth, contentHeight - ContentMargin * 2f));
            RectTransform right = Panel("SchoolDetailPanel", ContentTransform, new Vector2(0f, 1f),
                new Vector2(ContentMargin + LeftWidth + Gap, -ContentMargin),
                new Vector2(contentWidth - LeftWidth - Gap - ContentMargin * 2f,
                    contentHeight - ContentMargin * 2f));
            _detailPanel = right;

            if (right.GetComponent<RectMask2D>() == null) right.gameObject.AddComponent<RectMask2D>();
            _detailScroll = right.GetComponent<ScrollRect>() ?? right.gameObject.AddComponent<ScrollRect>();
            var detailContentObject = new GameObject("SchoolDetailContent", typeof(RectTransform));
            detailContentObject.transform.SetParent(right, false);
            _detailContent = detailContentObject.GetComponent<RectTransform>();
            _detailContent.anchorMin = new Vector2(0f, 1f);
            _detailContent.anchorMax = new Vector2(1f, 1f);
            _detailContent.pivot = new Vector2(.5f, 1f);
            _detailContent.anchoredPosition = Vector2.zero;
            _detailContent.sizeDelta = new Vector2(0f, right.sizeDelta.y);
            _detailScroll.viewport = right;
            _detailScroll.content = _detailContent;
            _detailScroll.horizontal = false;
            _detailScroll.vertical = true;
            _detailScroll.movementType = ScrollRect.MovementType.Clamped;

            Button sortButton = ButtonWithText("Sort", left, new Vector2(0f, 1f),
                new Vector2(5f, -5f), new Vector2(LeftWidth - 10f, 28f), out _sortText);
            sortButton.onClick.AddListener(CycleSort);

            var viewportObject = new GameObject("ListViewport", typeof(RectTransform), typeof(Image),
                typeof(RectMask2D), typeof(ScrollRect));
            viewportObject.transform.SetParent(left, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = new Vector2(0f, 1f);
            viewport.anchorMax = new Vector2(0f, 1f);
            viewport.pivot = new Vector2(0f, 1f);
            viewport.anchoredPosition = new Vector2(5f, -38f);
            viewport.sizeDelta = new Vector2(LeftWidth - 10f, contentHeight - ContentMargin * 2f - 43f);
            viewportObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, .12f);

            var listObject = new GameObject("SchoolListContent", typeof(RectTransform));
            listObject.transform.SetParent(viewport, false);
            _listContent = listObject.GetComponent<RectTransform>();
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = new Vector2(0f,
                CourtSchoolRegistry.All.Count * (SchoolListItem.Height + 2f));
            ScrollRect listScroll = viewportObject.GetComponent<ScrollRect>();
            listScroll.viewport = viewport;
            listScroll.content = _listContent;
            listScroll.horizontal = false;
            listScroll.vertical = true;
            listScroll.movementType = ScrollRect.MovementType.Clamped;

            _detailTitle = Text("Title", _detailContent, new Vector2(0f, 1f), new Vector2(12f, -8f),
                new Vector2(right.sizeDelta.x - 150f, 26f), 14, TextAnchor.UpperLeft);
            _rosterButton = ButtonWithText("OpenCurrentSchoolRoster", _detailContent,
                new Vector2(0f, 1f), new Vector2(right.sizeDelta.x - 132f, -6f),
                new Vector2(120f, 24f), out _rosterButtonText);
            _rosterButton.onClick.AddListener(() => SchoolRosterWindow.Open(_selectedSchool));
            _rosterButton.gameObject.SetActive(false);
            _detailBody = Text("Body", _detailContent, new Vector2(0f, 1f), new Vector2(12f, -40f),
                new Vector2(right.sizeDelta.x - 24f, 1f), 10, TextAnchor.UpperLeft);
            _detailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detailBody.verticalOverflow = VerticalWrapMode.Overflow;

            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                _listItems.Add(SchoolListItem.Create(_listContent));
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
            {
                SchoolInfluenceBar bar = SchoolInfluenceBar.Create(_detailContent);
                bar.gameObject.SetActive(false);
                _bars.Add(bar);
            }
            for (int i = 0; i < 5; i++)
            {
                SchoolActorCardView card = SchoolActorCardView.Create(_detailContent);
                card.gameObject.SetActive(false);
                _actorCards.Add(card);
            }
            for (int i = 0; i < 6; i++)
            {
                SchoolMasterCardView card = SchoolMasterCardView.Create(_detailContent);
                card.gameObject.SetActive(false);
                _masterCards.Add(card);
            }
            for (int i = 0; i < 4; i++)
            {
                SchoolInstitutionRowView row = SchoolInstitutionRowView.Create(_detailContent);
                row.gameObject.SetActive(false);
                _institutionRows.Add(row);
                SchoolLineageRowView lineage = SchoolLineageRowView.Create(_detailContent);
                lineage.gameObject.SetActive(false);
                _lineageRows.Add(lineage);
            }
            _breakdown = SchoolInfluenceBreakdownView.Create(_detailContent);
            _breakdown.gameObject.SetActive(false);
        }

        private void ApplyRequestAndRefresh()
        {
            if (_requestedCity >= 0)
            {
                _selectedCity = _requestedCity;
                _requestedCity = -1L;
            }
            else
            {
                _selectedCity = -1L;
                _selectedSchool = CourtSchoolRegistry.Find(_requestedSchool) == null
                    ? CourtSchoolId.Ru
                    : _requestedSchool;
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_listContent == null) return;
            Dictionary<string, SchoolMetrics> metrics = BuildMetrics();
            List<CourtSchoolDefinition> ordered = CourtSchoolRegistry.All.ToList();
            if (_sortMode == SortMode.Influence)
                ordered = ordered.OrderByDescending(p => metrics[p.Id].Influence)
                    .ThenBy(RegistryOrder).ToList();
            else if (_sortMode == SortMode.Cities)
                ordered = ordered.OrderByDescending(p => metrics[p.Id].Cities)
                    .ThenBy(RegistryOrder).ToList();

            UpdateSortLabel();
            for (int i = 0; i < _listItems.Count; i++)
            {
                SchoolListItem item = _listItems[i];
                CourtSchoolDefinition definition = ordered[i];
                RectTransform rect = item.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(0f, -i * (SchoolListItem.Height + 2f));
                SchoolMetrics value = metrics[definition.Id];
                item.Bind(definition, value.Members, value.Cities, value.Influence,
                    _selectedCity < 0 && definition.Id == _selectedSchool, SelectSchool);
                item.gameObject.SetActive(true);
            }

            HideDetailRows(pReleaseActorReferences: false);
            HistoricalSchoolRosterRevisionStamp schoolRevisionStamp = null;
            try
            {
                if (_selectedCity >= 0) ShowCityDetail(World.world?.cities?.get(_selectedCity));
                else ShowSchoolDetail(CourtSchoolRegistry.Find(_selectedSchool), metrics,
                    out schoolRevisionStamp);
            }
            finally
            {
                ReleaseUnusedActorCards();
            }
            _displayedSnapshotGeneration = CitySchoolSnapshotService.Generation;
            _displayedSchoolRevisionStamp = schoolRevisionStamp;
        }

        private Dictionary<string, SchoolMetrics> BuildMetrics()
        {
            var result = CourtSchoolRegistry.All.ToDictionary(p => p.Id, p => new SchoolMetrics(),
                StringComparer.Ordinal);
            foreach (CourtSchoolDefinition definition in CourtSchoolRegistry.All)
                result[definition.Id].Members = SchoolMembershipService.Count(definition.Id);
            try
            {
                if (World.world?.cities != null)
                    foreach (City city in World.world.cities)
                    {
                        CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
                        if (snapshot == null) continue;
                        if (result.TryGetValue(snapshot.DominantSchool, out SchoolMetrics dominant))
                            dominant.Cities++;
                        foreach (KeyValuePair<string, float> score in snapshot.Scores)
                            if (result.TryGetValue(score.Key, out SchoolMetrics value))
                                value.Influence += score.Value;
                    }
            }
            catch { }
            return result;
        }

        private void SelectSchool(string pSchoolId)
        {
            _selectedSchool = pSchoolId;
            _selectedCity = -1L;
            SchoolMapModeService.SetFocus(pSchoolId);
            Refresh();
        }

        private void ShowSchoolDetail(CourtSchoolDefinition pDefinition,
            Dictionary<string, SchoolMetrics> pMetrics,
            out HistoricalSchoolRosterRevisionStamp pRevisionStamp)
        {
            pRevisionStamp = null;
            if (pDefinition == null) return;
            _rosterButton.gameObject.SetActive(true);
            _rosterButtonText.text = AW_L10n.Text("aw_school_roster_open_current",
                "Teacher Lineage");
            _detailTitle.color = Parse(pDefinition.ColorHex, Color.white);
            _detailTitle.text = AW_L10n.Text(pDefinition.NameKey, pDefinition.Id);
            SchoolMetrics metrics = pMetrics[pDefinition.Id];
            _detailBody.text = AW_L10n.Text(pDefinition.DescriptionKey, pDefinition.Id) + "\n\n" +
                               DirectionText(pDefinition.Direction) + "\n" +
                               AW_L10n.Text("aw_school_compatible_offices", "Compatible offices") + ": " +
                               string.Join(" / ", pDefinition.CompatibleOffices.Select(OfficeName).ToArray()) + "\n" +
                               AW_L10n.Text("aw_school_total_influence", "Total influence") + ": " +
                               Mathf.RoundToInt(metrics.Influence) + "    " +
                               AW_L10n.Text("aw_school_dominant_cities", "Dominant cities") + ": " +
                               metrics.Cities + "\n" +
                               AW_L10n.Text("aw_school_top_cities", "Leading cities") + ": " +
                               TopCities(pDefinition.Id) + "\n" +
                               AW_L10n.Text("aw_school_top_kingdoms", "Leading kingdoms") + ": " +
                               TopKingdoms(pDefinition.Id) + "\n" +
                               AW_L10n.Text("aw_school_master_gallery", "Historical Masters") +
                               " / " + AW_L10n.Text("aw_school_institution_list",
                                   "School Institutions") + " / " +
                               AW_L10n.Text("aw_school_lineage_list", "Teacher Lineage") + "\n" +
                               RecentHistory(pDefinition.Id);
            float top = LayoutDetailBody();
            int representatives = ShowRepresentatives(pDefinition.Id, top,
                out pRevisionStamp);
            top += ActorCardRows(representatives) *
                (SchoolActorCardView.Height + 6f) + 12f;
            int masters = ShowMasterGallery(pDefinition.Id, top);
            top += MasterCardRows(masters) * (SchoolMasterCardView.Height + 6f) + 12f;
            top = ShowInstitutions(pDefinition.Id, top);
            top = ShowLineage(pDefinition.Id, top);
            SetDetailContentHeight(top + 18f);
            ResetDetailScroll();
        }

        private static string TopCities(string pSchoolId)
        {
            var values = new List<KeyValuePair<string, float>>();
            try
            {
                if (World.world?.cities != null)
                    foreach (City city in World.world.cities)
                    {
                        CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
                        if (snapshot == null || !snapshot.Scores.TryGetValue(pSchoolId, out float score) ||
                            score <= 0f) continue;
                        values.Add(new KeyValuePair<string, float>(city.data.name ?? "", score));
                    }
            }
            catch { }
            string text = string.Join(" / ", values.OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal).Take(5).Select(p => p.Key).ToArray());
            return string.IsNullOrEmpty(text) ? AW_L10n.Text("aw_school_none", "None") : text;
        }

        private static string TopKingdoms(string pSchoolId)
        {
            var values = new Dictionary<long, KeyValuePair<string, float>>();
            try
            {
                if (World.world?.cities != null)
                    foreach (City city in World.world.cities)
                    {
                        CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
                        Kingdom kingdom = city?.kingdom;
                        if (snapshot == null || kingdom?.data == null ||
                            !snapshot.Scores.TryGetValue(pSchoolId, out float score) || score <= 0f) continue;
                        values.TryGetValue(kingdom.id, out KeyValuePair<string, float> previous);
                        values[kingdom.id] = new KeyValuePair<string, float>(kingdom.name ?? "",
                            previous.Value + score);
                    }
            }
            catch { }
            string text = string.Join(" / ", values.Values.OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal).Take(3).Select(p => p.Key).ToArray());
            return string.IsNullOrEmpty(text) ? AW_L10n.Text("aw_school_none", "None") : text;
        }

        private void ShowCityDetail(City pCity)
        {
            _rosterButton.gameObject.SetActive(false);
            if (pCity?.data == null)
            {
                _detailTitle.text = AW_L10n.Text("aw_school_city_missing", "City missing");
                _detailBody.text = "";
                SetDetailContentHeight(LayoutDetailBody() + 18f);
                ResetDetailScroll();
                return;
            }
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(pCity);
            CourtSchoolDefinition dominant = CourtSchoolRegistry.Find(snapshot?.DominantSchool);
            _detailTitle.color = dominant == null ? Color.gray : Parse(dominant.ColorHex, Color.white);
            _detailTitle.text = pCity.data.name + " - " + (pCity.kingdom?.name ?? "");
            _detailBody.text = snapshot == null || snapshot.TotalScore <= 0f
                ? AW_L10n.Text("aw_school_map_no_influence", "No school influence")
                : AW_L10n.Text("aw_school_map_dominant", "Dominant") + ": " +
                  AW_L10n.Text(dominant?.NameKey ?? "aw_court_school_none", "No school") + "\n" +
                  AW_L10n.Text("aw_school_contributors", "Contributors") + ": " +
                  string.Join(" / ", snapshot.Contributors.Take(5)
                      .Select(p => p.ActorName + " " + Mathf.RoundToInt(p.Score)).ToArray()) +
                  LedgerSummary(snapshot) + "\n" + SchoolLandmarkService.Describe(pCity);
            float top = LayoutDetailBody();
            if (snapshot != null && snapshot.TotalScore > 0f)
            {
                _breakdown.Bind(pCity);
                RectTransform breakdownRect = _breakdown.GetComponent<RectTransform>();
                float breakdownHeight = _breakdown.LayoutHeight(DetailWidth());
                breakdownRect.anchoredPosition = new Vector2(12f, -top);
                top += breakdownHeight + 8f;
            }
            if (snapshot == null || snapshot.TotalScore <= 0f)
            {
                SetDetailContentHeight(top + 18f);
                ResetDetailScroll();
                return;
            }
            int index = 0;
            foreach (KeyValuePair<string, float> item in snapshot.Scores
                         .OrderByDescending(p => p.Value).ThenBy(p => RegistryOrder(p.Key)))
            {
                if (index >= _bars.Count) break;
                SchoolInfluenceBar bar = _bars[index];
                RectTransform rect = bar.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(12f,
                    -top - index * (SchoolInfluenceBar.Height + 4f));
                rect.sizeDelta = new Vector2(DetailWidth(), SchoolInfluenceBar.Height);
                bar.Bind(CourtSchoolRegistry.Find(item.Key), item.Value,
                    item.Value / snapshot.TotalScore, SelectSchool);
                bar.gameObject.SetActive(true);
                index++;
            }
            float contributorTop = top + index * (SchoolInfluenceBar.Height + 4f) + 10f;
            int contributors = ShowContributors(snapshot.Contributors, contributorTop);
            SetDetailContentHeight(contributorTop + ActorCardRows(contributors) *
                (SchoolActorCardView.Height + 6f) + 18f);
            ResetDetailScroll();
        }

        private static string LedgerSummary(CitySchoolSnapshot pSnapshot)
        {
            if (pSnapshot?.LedgerScores == null || pSnapshot.LedgerScores.Count == 0)
                return "";
            string label = AW_L10n.Text("aw_school_ledger_influence", "Ledger influence");
            string values = string.Join(" / ", pSnapshot.LedgerScores
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .Take(5)
                .Select(p => AW_L10n.Text(CourtSchoolRegistry.Find(p.Key)?.NameKey ??
                    "aw_court_school_none", p.Key) + " " + Mathf.RoundToInt(p.Value))
                .ToArray());
            return "\n" + label + ": " + values;
        }

        private int ShowContributors(IEnumerable<CitySchoolInfluenceContribution> pContributions, float pTop)
        {
            int index = 0;
            foreach (CitySchoolInfluenceContribution contribution in pContributions ??
                     Array.Empty<CitySchoolInfluenceContribution>())
            {
                if (index >= _actorCards.Count) break;
                Actor actor = World.world?.units?.get(contribution.ActorId);
                if (actor?.data == null || !actor.isAlive() || actor.isRekt()) continue;
                SchoolActorCardView card = _actorCards[index];
                LayoutActorCard(card, index, pTop);
                card.Bind(actor, StandingText(index), ContributionRole(contribution.Role) + "  " +
                    Mathf.RoundToInt(contribution.Score));
                index++;
            }
            return index;
        }

        private int ShowRepresentatives(string pSchoolId, float pTop,
            out HistoricalSchoolRosterRevisionStamp pRevisionStamp)
        {
            SchoolRosterReadModel model = SchoolRosterReadModelService.Build(pSchoolId,
                1f, 1f, SchoolRosterRules.DefaultColumnsPerRow);
            pRevisionStamp = model.RevisionStamp;
            SchoolRosterReadNode[] representatives = model.Nodes
                .OrderBy(p => p.Layout.StableOrder)
                .Take(_actorCards.Count)
                .ToArray();
            for (int i = 0; i < representatives.Length; i++)
            {
                SchoolRosterReadNode representative = representatives[i];
                Actor actor = representative.Actor;
                SchoolActorCardView card = _actorCards[i];
                LayoutActorCard(card, i, pTop);
                card.Bind(actor, StandingText(i),
                    RosterStandingText(representative.Layout.Standing) + "  " +
                    AW_L10n.Text("aw_school_ability", "Ability") + "  " +
                    Mathf.RoundToInt(Ability(actor)));
            }
            return representatives.Length;
        }

        private int ShowMasterGallery(string pSchoolId, float pTop)
        {
            Dictionary<string, HistoricalSchoolMasterStoreRecord> byId =
                HistoricalSchoolStore.LoadMasterStates()
                    .Where(p => p != null && !string.IsNullOrEmpty(p.MasterId))
                    .GroupBy(p => p.MasterId, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => p.OrderByDescending(v => v.SpawnYear).First(),
                        StringComparer.Ordinal);
            HistoricalSchoolMasterDefinition[] definitions = HistoricalSchoolMasterRegistry.All
                .Where(p => p.SchoolId == pSchoolId)
                .OrderBy(p => p.Order)
                .ToArray();
            int index = 0;
            foreach (HistoricalSchoolMasterDefinition definition in definitions)
            {
                if (index >= _masterCards.Count) break;
                byId.TryGetValue(definition.Id, out HistoricalSchoolMasterStoreRecord record);
                Actor actor = record?.ActorId >= 0 ? World.world?.units?.get(record.ActorId) : null;
                SchoolMasterCardView card = _masterCards[index];
                LayoutMasterCard(card, index, pTop);
                card.Bind(definition, record, actor);
                index++;
            }
            return index;
        }

        private float ShowInstitutions(string pSchoolId, float pTop)
        {
            List<SchoolInstitutionReadModel> institutions =
                HistoricalSchoolStore.LoadInstitutions(pSchoolId, pLimit: _institutionRows.Count);
            float cursor = pTop;
            int rendered = 0;
            for (int i = 0; i < _institutionRows.Count; i++)
            {
                SchoolInstitutionRowView row = _institutionRows[i];
                if (i >= institutions.Count)
                {
                    row.gameObject.SetActive(false);
                    continue;
                }
                SchoolInstitutionReadModel institution = institutions[i];
                RectTransform rect = row.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(12f, -cursor);
                row.Bind(institution, FindCityName(institution.CityId));
                cursor += row.LayoutHeight(DetailWidth()) + 4f;
                rendered++;
            }
            return rendered == 0 ? pTop : cursor + 4f;
        }

        private float ShowLineage(string pSchoolId, float pTop)
        {
            var members = SchoolMembershipService.Members(pSchoolId)
                .Select(p => SchoolMembershipService.GetActive(p))
                .Where(p => p != null)
                .OrderByDescending(p => p.Reputation)
                .ThenBy(p => p.ActorId)
                .Take(_lineageRows.Count)
                .ToArray();
            float cursor = pTop;
            int rendered = 0;
            for (int i = 0; i < _lineageRows.Count; i++)
            {
                SchoolLineageRowView row = _lineageRows[i];
                if (i >= members.Length)
                {
                    row.gameObject.SetActive(false);
                    continue;
                }
                SchoolMembershipRecord membership = members[i];
                Actor student = World.world?.units?.get(membership.ActorId);
                Actor teacher = membership.TeacherActorId >= 0
                    ? World.world?.units?.get(membership.TeacherActorId)
                    : null;
                string studentName = student?.data != null
                    ? SafeActorName(student)
                    : HistoricalSchoolMasterRegistry.Find(membership.SourceId)?.CanonicalName ??
                      ("actor " + membership.ActorId);
                string teacherName = teacher?.data != null ? SafeActorName(teacher) : "";
                RectTransform rect = row.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(12f, -cursor);
                row.Bind(studentName, teacherName, membership.Generation,
                    membership.Reputation, student?.data != null && student.isAlive() &&
                    !student.isRekt());
                cursor += row.LayoutHeight(DetailWidth()) + 4f;
                rendered++;
            }
            return rendered == 0 ? pTop : cursor;
        }

        private static string RecentHistory(string pSchoolId)
        {
            List<SchoolEventReadModel> events = HistoricalSchoolStore.LoadRecentSchoolEvents(
                pSchoolId, pLimit: 3);
            List<SchoolWorkReadModel> works = HistoricalSchoolStore.LoadWorks(pSchoolId, 2);
            List<SchoolDebateReadModel> debates = HistoricalSchoolStore.LoadDebates(pSchoolId,
                pLimit: 2);
            var lines = new List<string>();
            string debateLabel = AW_L10n.Text("aw_school_history_debate", "Debate");
            string workLabel = AW_L10n.Text("aw_school_history_work", "Work");
            foreach (SchoolEventReadModel item in events)
                lines.Add((item.EventYear >= 0 ? item.EventYear + " " : "") +
                          EventLabel(item.EventType));
            foreach (SchoolDebateReadModel item in debates)
                lines.Add((item.DebateYear >= 0 ? item.DebateYear + " " : "") +
                          debateLabel + " " + (item.TopicId ?? ""));
            foreach (SchoolWorkReadModel item in works)
                lines.Add((item.WrittenYear >= 0 ? item.WrittenYear + " " : "") +
                          workLabel + " " + (item.DisplayName ?? item.WorkKey));
            string heading = AW_L10n.Text("aw_school_recent_history",
                "Recent School History");
            return heading + ": " + (lines.Count == 0
                ? AW_L10n.Text("aw_school_none", "None")
                : string.Join(" / ", lines.Take(5).ToArray()));
        }

        private static string EventLabel(string pEventType)
        {
            string type = pEventType ?? "event";
            string key = "aw_school_event_" + type;
            switch (type)
            {
                case "lecture": return AW_L10n.Text(key, "Lecture");
                case "persuasion": return AW_L10n.Text(key, "Persuasion");
                case "disciple_joined": return AW_L10n.Text(key, "Disciple joined");
                case "school_conversion": return AW_L10n.Text(key, "School conversion");
                case "school_rediscovery": return AW_L10n.Text(key, "School rediscovered");
                case "journey_arrival": return AW_L10n.Text(key, "Journey arrival");
                case "guest_service_started": return AW_L10n.Text(key, "Guest service");
                case "guest_service_renewed": return AW_L10n.Text(key, "Guest service renewed");
                case "institution_founded": return AW_L10n.Text(key, "Institution founded");
                case "lineage_successor": return AW_L10n.Text(key, "Lineage succession");
                default: return type;
            }
        }

        private void LayoutMasterCard(SchoolMasterCardView pCard, int pIndex, float pTop)
        {
            if (pCard == null || _detailPanel == null) return;
            const float gap = 6f;
            float width = (_detailPanel.sizeDelta.x - 24f - gap) * .5f;
            int row = pIndex / 2;
            int column = pIndex % 2;
            RectTransform rect = pCard.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(12f + column * (width + gap),
                -pTop - row * (SchoolMasterCardView.Height + gap));
            rect.sizeDelta = new Vector2(width, SchoolMasterCardView.Height);
        }

        private static int MasterCardRows(int pCount) => (Math.Max(0, pCount) + 1) / 2;

        private static string FindCityName(long pCityId)
        {
            if (pCityId < 0) return "";
            try { return World.world?.cities?.get(pCityId)?.data?.name ?? ""; }
            catch { return ""; }
        }

        private static string SafeActorName(Actor pActor)
        {
            try { return pActor?.getName() ?? pActor?.data?.name ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }

        private void HideDetailRows(bool pReleaseActorReferences)
        {
            foreach (SchoolInfluenceBar bar in _bars) bar.gameObject.SetActive(false);
            if (pReleaseActorReferences)
                UnbindActorCards();
            else
            {
                foreach (SchoolActorCardView card in _actorCards)
                    card.gameObject.SetActive(false);
                foreach (SchoolMasterCardView card in _masterCards)
                    card.gameObject.SetActive(false);
            }
            foreach (SchoolInstitutionRowView row in _institutionRows) row.gameObject.SetActive(false);
            foreach (SchoolLineageRowView row in _lineageRows) row.gameObject.SetActive(false);
            if (_breakdown != null) _breakdown.gameObject.SetActive(false);
        }

        private void ReleaseUnusedActorCards()
        {
            foreach (SchoolActorCardView card in _actorCards)
                if (!card.gameObject.activeSelf) card.Unbind();
            foreach (SchoolMasterCardView card in _masterCards)
                if (!card.gameObject.activeSelf) card.Unbind();
        }

        private void UnbindActorCards()
        {
            foreach (SchoolActorCardView card in _actorCards) card.Unbind();
            foreach (SchoolMasterCardView card in _masterCards) card.Unbind();
        }

        private void ResetWorldState()
        {
            SchoolMapModeService.EndWindowMode();
            HideDetailRows(pReleaseActorReferences: true);
            _selectedSchool = CourtSchoolId.Ru;
            _selectedCity = -1L;
            _displayedSnapshotGeneration = -1;
            _displayedSchoolRevisionStamp = null;
            _nextRefreshCheckTime = 0f;
            if (_detailTitle != null) _detailTitle.text = "";
            if (_detailBody != null) _detailBody.text = "";
        }

        private void LayoutActorCard(SchoolActorCardView pCard, int pIndex, float pTop)
        {
            if (pCard == null || _detailPanel == null) return;
            const float gap = 6f;
            float width = (_detailPanel.sizeDelta.x - 24f - gap) * .5f;
            int row = pIndex / 2;
            int column = pIndex % 2;
            RectTransform rect = pCard.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(12f + column * (width + gap),
                -pTop - row * (SchoolActorCardView.Height + gap));
            rect.sizeDelta = new Vector2(width, SchoolActorCardView.Height);
        }

        private static int ActorCardRows(int pCount) => (Math.Max(0, pCount) + 1) / 2;

        private static string StandingText(int pIndex)
        {
            switch (SchoolActorStandingRules.Resolve(pIndex))
            {
                case SchoolActorStandingRules.Leader:
                    return AW_L10n.Text("aw_school_standing_leader", "School Leader");
                case SchoolActorStandingRules.Core:
                    return AW_L10n.Text("aw_school_standing_core", "Core Figure");
                default:
                    return AW_L10n.Text("aw_school_standing_representative", "Representative");
            }
        }

        private static string RosterStandingText(SchoolRosterStanding pStanding)
        {
            switch (pStanding)
            {
                case SchoolRosterStanding.HistoricalMaster:
                    return AW_L10n.Text("aw_school_roster_standing_master",
                        "Historical Master");
                case SchoolRosterStanding.Leader:
                    return AW_L10n.Text("aw_school_roster_standing_leader", "Leader");
                case SchoolRosterStanding.QualifiedTeacher:
                    return AW_L10n.Text("aw_school_roster_standing_teacher", "Teacher");
                case SchoolRosterStanding.DirectDisciple:
                    return AW_L10n.Text("aw_school_roster_standing_direct",
                        "Direct Disciple");
                case SchoolRosterStanding.LaterDisciple:
                    return AW_L10n.Text("aw_school_roster_standing_later",
                        "Later Disciple");
                default:
                    return AW_L10n.Text("aw_school_roster_standing_member", "Member");
            }
        }

        private float DetailWidth()
        {
            return Mathf.Max(1f, (_detailPanel?.sizeDelta.x ?? 1f) - 24f);
        }

        private float LayoutDetailBody()
        {
            RectTransform rect = _detailBody?.GetComponent<RectTransform>();
            if (rect == null) return 40f;
            float width = DetailWidth();
            rect.anchoredPosition = new Vector2(12f, -40f);
            rect.sizeDelta = new Vector2(width, 1f);
            float height = Mathf.Max(18f, Mathf.Ceil(_detailBody.preferredHeight) + 4f);
            rect.sizeDelta = new Vector2(width, height);
            return 40f + height + 12f;
        }

        private void SetDetailContentHeight(float pHeight)
        {
            if (_detailContent == null || _detailPanel == null) return;
            _detailContent.sizeDelta = new Vector2(0f, Mathf.Max(_detailPanel.sizeDelta.y, pHeight));
        }

        private void ResetDetailScroll()
        {
            if (_detailScroll != null) _detailScroll.verticalNormalizedPosition = 1f;
        }

        private void CycleSort()
        {
            _sortMode = (SortMode)(((int)_sortMode + 1) % 3);
            Refresh();
        }

        private void UpdateSortLabel()
        {
            string key = _sortMode == SortMode.Historical ? "aw_school_sort_historical" :
                _sortMode == SortMode.Influence ? "aw_school_sort_influence" : "aw_school_sort_cities";
            _sortText.text = AW_L10n.Text(key, _sortMode.ToString());
        }

        private static string DirectionText(CourtSchoolDirection pDirection)
        {
            return AW_L10n.Text("aw_school_directions", "Directions") + ": " +
                   AW_L10n.Text("aw_school_direction_livelihood", "Livelihood") + " " +
                   Percent(pDirection.Livelihood) + "  " +
                   AW_L10n.Text("aw_school_direction_war", "War") + " " + Percent(pDirection.War) +
                   "  " + AW_L10n.Text("aw_school_direction_aggression", "Aggression") + " " +
                   Percent(pDirection.Aggression) + "  " +
                   AW_L10n.Text("aw_school_direction_peace", "Peace") + " " +
                   Percent(pDirection.Peace) + "\n" +
                   AW_L10n.Text("aw_school_direction_order", "Order") + " " + Percent(pDirection.Order) +
                   "  " + AW_L10n.Text("aw_school_direction_commerce", "Commerce") + " " +
                   Percent(pDirection.Commerce) + "  " +
                   AW_L10n.Text("aw_school_direction_technology", "Technology") + " " +
                   Percent(pDirection.Technology);
        }

        private static string Percent(float pValue) => Mathf.RoundToInt(pValue * 100f) + "%";

        private static float Ability(Actor pActor)
        {
            try
            {
                return pActor.stats["stewardship"] + pActor.stats["diplomacy"] +
                       pActor.stats["warfare"] + pActor.stats["intelligence"];
            }
            catch { return 0f; }
        }

        private static string OfficeName(string pOffice)
        {
            if (pOffice == "general") return AW_L10n.Text("aw_court_general", "General");
            return AW_L10n.Text("aw_court_office_" + pOffice, pOffice);
        }

        private static string ContributionRole(string pRole)
        {
            switch (pRole ?? "")
            {
                case CitySchoolRole.King: return AW_L10n.Text("aw_label_king", "King");
                case CitySchoolRole.Heir: return AW_L10n.Text("aw_label_heir", "Heir");
                case CitySchoolRole.Leader: return OfficeName(CourtOfficeId.Governor);
                case CitySchoolRole.CentralOfficer:
                    return AW_L10n.Text("aw_court_central_officers", "Central Official");
                case CitySchoolRole.General: return AW_L10n.Text("aw_court_general", "General");
                case CitySchoolRole.Scholar: return AW_L10n.Text("aw_school_role_scholar", "Resident Scholar");
                default: return AW_L10n.Text("aw_court_roles", "Official");
            }
        }

        private static int RegistryOrder(CourtSchoolDefinition pDefinition) =>
            RegistryOrder(pDefinition?.Id);

        private static int RegistryOrder(string pSchoolId)
        {
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                if (CourtSchoolRegistry.All[i].Id == pSchoolId) return i;
            return int.MaxValue;
        }

        private static RectTransform Panel(string pName, Transform pParent, Vector2 pAnchor,
            Vector2 pPosition, Vector2 pSize)
        {
            Transform existing = pParent.Find(pName);
            GameObject obj = existing != null
                ? existing.gameObject
                : new GameObject(pName, typeof(RectTransform), typeof(Image));
            if (existing == null) obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = pAnchor;
            rect.anchorMax = pAnchor;
            rect.pivot = pAnchor;
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            obj.GetComponent<Image>().color = new Color(.075f, .065f, .05f, .96f);
            return rect;
        }

        private static Text Text(string pName, Transform pParent, Vector2 pAnchor,
            Vector2 pPosition, Vector2 pSize, int pFontSize, TextAnchor pAlignment)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = pAnchor;
            rect.anchorMax = pAnchor;
            rect.pivot = pAnchor;
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAlignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button ButtonWithText(string pName, Transform pParent, Vector2 pAnchor,
            Vector2 pPosition, Vector2 pSize, out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = pAnchor;
            rect.anchorMax = pAnchor;
            rect.pivot = pAnchor;
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .95f);
            pText = Text("Text", obj.transform, Vector2.zero, Vector2.zero, pSize, 9,
                TextAnchor.MiddleCenter);
            RectTransform textRect = pText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(.5f, .5f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return obj.GetComponent<Button>();
        }

        private static Color Parse(string pHex, Color pFallback)
        {
            return ColorUtility.TryParseHtmlString(pHex, out Color color) ? color : pFallback;
        }
    }
}
