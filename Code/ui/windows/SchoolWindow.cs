using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class SchoolWindow : AbstractWindow<SchoolWindow>
    {
        private const float Width = 700f;
        private const float Height = 420f;
        private const float LeftWidth = 224f;
        private const float Gap = 8f;
        private const float ContentMargin = 18f;

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
        private readonly List<Button> _actorButtons = new List<Button>();
        private RectTransform _listContent;
        private RectTransform _detailPanel;
        private Text _detailTitle;
        private Text _detailBody;
        private Text _sortText;
        private SortMode _sortMode;
        private string _selectedSchool = CourtSchoolId.Ru;
        private long _selectedCity = -1L;

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

        protected override void Init()
        {
            ConfigureWindow();
            BuildUi();
        }

        public override void OnNormalEnable()
        {
            SchoolMapModeService.BeginWindowMode();
            ApplyRequestAndRefresh();
        }

        private void OnDisable()
        {
            SchoolMapModeService.EndWindowMode();
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

            _detailTitle = Text("Title", right, new Vector2(0f, 1f), new Vector2(12f, -8f),
                new Vector2(right.sizeDelta.x - 24f, 26f), 14, TextAnchor.UpperLeft);
            _detailBody = Text("Body", right, new Vector2(0f, 1f), new Vector2(12f, -40f),
                new Vector2(right.sizeDelta.x - 24f, 152f), 10, TextAnchor.UpperLeft);
            _detailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detailBody.verticalOverflow = VerticalWrapMode.Truncate;

            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                _listItems.Add(SchoolListItem.Create(_listContent));
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
            {
                SchoolInfluenceBar bar = SchoolInfluenceBar.Create(right);
                bar.gameObject.SetActive(false);
                _bars.Add(bar);
            }
            for (int i = 0; i < 5; i++)
            {
                Button button = ButtonWithText("Actor" + i, right, new Vector2(0f, 1f),
                    Vector2.zero, new Vector2(right.sizeDelta.x - 24f, 24f), out _);
                button.gameObject.SetActive(false);
                _actorButtons.Add(button);
            }
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
            CitySchoolSnapshotService.ProcessDirty(8);
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

            if (_selectedCity >= 0) ShowCityDetail(World.world?.cities?.get(_selectedCity));
            else ShowSchoolDetail(CourtSchoolRegistry.Find(_selectedSchool), metrics);
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
            Dictionary<string, SchoolMetrics> pMetrics)
        {
            HideDetailRows();
            if (pDefinition == null) return;
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
                               TopKingdoms(pDefinition.Id);
            ShowRepresentatives(pDefinition.Id, 205f);
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
            HideDetailRows();
            if (pCity?.data == null)
            {
                _detailTitle.text = AW_L10n.Text("aw_school_city_missing", "City missing");
                _detailBody.text = "";
                return;
            }
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(pCity, pEnsureFresh: true);
            CourtSchoolDefinition dominant = CourtSchoolRegistry.Find(snapshot?.DominantSchool);
            _detailTitle.color = dominant == null ? Color.gray : Parse(dominant.ColorHex, Color.white);
            _detailTitle.text = pCity.data.name + " - " + (pCity.kingdom?.name ?? "");
            _detailBody.text = snapshot == null || snapshot.TotalScore <= 0f
                ? AW_L10n.Text("aw_school_map_no_influence", "No school influence")
                : AW_L10n.Text("aw_school_map_dominant", "Dominant") + ": " +
                  AW_L10n.Text(dominant?.NameKey ?? "aw_court_school_none", "No school") + "\n" +
                  AW_L10n.Text("aw_school_contributors", "Contributors") + ": " +
                  string.Join(" / ", snapshot.Contributors.Take(5)
                      .Select(p => p.ActorName + " " + Mathf.RoundToInt(p.Score)).ToArray());
            if (snapshot == null || snapshot.TotalScore <= 0f) return;
            int index = 0;
            foreach (KeyValuePair<string, float> item in snapshot.Scores
                         .OrderByDescending(p => p.Value).ThenBy(p => RegistryOrder(p.Key)))
            {
                if (index >= _bars.Count) break;
                SchoolInfluenceBar bar = _bars[index];
                RectTransform rect = bar.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(12f, -198f - index * (SchoolInfluenceBar.Height + 4f));
                rect.sizeDelta = new Vector2(_detailPanel.sizeDelta.x - 24f, SchoolInfluenceBar.Height);
                bar.Bind(CourtSchoolRegistry.Find(item.Key), item.Value / snapshot.TotalScore);
                bar.gameObject.SetActive(true);
                index++;
            }
        }

        private void ShowRepresentatives(string pSchoolId, float pTop)
        {
            Actor[] actors = SchoolMembershipService.Members(pSchoolId)
                .Select(id => World.world?.units?.get(id))
                .Where(p => p?.data != null && p.isAlive() && !p.isRekt())
                .OrderByDescending(Ability)
                .ThenBy(p => p.data.id)
                .Take(_actorButtons.Count)
                .ToArray();
            for (int i = 0; i < actors.Length; i++)
            {
                Actor actor = actors[i];
                Button button = _actorButtons[i];
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(12f, -pTop - i * 28f);
                Text label = button.GetComponentInChildren<Text>();
                if (label != null) label.text = actor.getName();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ActionLibrary.openUnitWindow(actor));
                button.gameObject.SetActive(true);
            }
        }

        private void HideDetailRows()
        {
            foreach (SchoolInfluenceBar bar in _bars) bar.gameObject.SetActive(false);
            foreach (Button button in _actorButtons) button.gameObject.SetActive(false);
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
