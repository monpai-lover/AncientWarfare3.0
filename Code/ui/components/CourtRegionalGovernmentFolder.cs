using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.ui.items;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class CourtRegionalGovernmentFolder : MonoBehaviour
    {
        internal const float Width = 260f;
        internal const float CollapsedHeight = 32f;
        internal const float MiniScale = 0.56f;
        internal const float MiniWidth = CourtActorNodeView.Width * MiniScale;
        internal const float MiniHeight = CourtActorNodeView.Height * MiniScale + 18f;
        internal const float Gap = 8f;
        internal const int Columns = 3;

        private Text _label;
        private Text _arrow;
        private Button _toggle;
        private RectTransform _content;
        private readonly List<CourtActorNodeView> _officialPool =
            new List<CourtActorNodeView>();
        private readonly List<Text> _cityLabels = new List<Text>();
        private Action<bool> _expandedChanged;
        private bool _expanded;

        internal bool IsExpanded => _expanded;
        internal RectTransform LinkTarget => GetComponent<RectTransform>();

        internal static CourtRegionalGovernmentFolder Create(Transform pParent)
        {
            GameObject obj = new GameObject("CourtRegionalGovernmentFolder",
                typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(Width, CollapsedHeight);
            CourtRegionalGovernmentFolder folder =
                obj.AddComponent<CourtRegionalGovernmentFolder>();
            folder.BuildUi();
            folder.SetExpanded(false);
            return folder;
        }

        internal void Bind(IReadOnlyList<LocalCourtReadModel> pCities,
            Kingdom pKingdom, bool pExpanded, Action<bool> pOnExpandedChanged,
            Action<CourtActorNodeView> pQueuePortrait)
        {
            List<LocalCourtReadModel> cities = (pCities ??
                Array.Empty<LocalCourtReadModel>()).Where(city => city != null)
                .ToList();
            _expandedChanged = pOnExpandedChanged;
            string regionName = cities.Count == 0 ? "" :
                (string.IsNullOrWhiteSpace(cities[0].RegionName)
                    ? cities[0].CityName : cities[0].RegionName);
            string regionTitle = cities.Count == 0 ? "" : cities[0].RegionTitle;
            string countLabel = string.Format(AW_L10n.Text(
                    "aw_court_regional_folder_count", "{0} commanderies"),
                Math.Max(1, cities.Count));
            _label.text = string.Format(AW_L10n.Text(
                    "aw_court_regional_folder", "{0}  Governors  {1}"),
                RegionalGovernmentRules.AdministrativeLabel(regionName,
                    regionTitle), countLabel);
            _arrow.text = pExpanded ? "-" : "+";
            SetExpanded(pExpanded);
            for (int index = 0; index < cities.Count; index++)
            {
                CourtActorNodeView official = GetOfficial(index);
                LocalCourtReadModel city = cities[index];
                official.Bind(city.LeaderNode ?? VacancyNode(city), pKingdom);
                RectTransform officialRect = official.GetComponent<RectTransform>();
                int row = index / Columns;
                int column = index % Columns;
                officialRect.anchoredPosition = new Vector2(
                    8f + MiniWidth * 0.5f + column * (MiniWidth + Gap),
                    -CollapsedHeight - row * MiniHeight - 8f);
                officialRect.localScale = Vector3.one * MiniScale;
                official.gameObject.SetActive(_expanded);
                if (_expanded && official.NeedsPortrait)
                    pQueuePortrait?.Invoke(official);
                Text cityLabel = GetCityLabel(index);
                cityLabel.text = RegionalGovernmentRules.AdministrativeLabel(
                    RegionalGovernmentRules.CityName(city.CityName),
                    city.LocalLevelTitle);
                RectTransform cityRect = cityLabel.rectTransform;
                cityRect.anchorMin = cityRect.anchorMax = new Vector2(0f, 1f);
                cityRect.pivot = new Vector2(0f, 1f);
                cityRect.anchoredPosition = new Vector2(
                    8f + column * (MiniWidth + Gap),
                    -CollapsedHeight - row * MiniHeight - 8f -
                    CourtActorNodeView.Height * MiniScale - 2f);
                cityRect.sizeDelta = new Vector2(MiniWidth, 14f);
                cityLabel.gameObject.SetActive(_expanded);
            }
            HideUnused(cities.Count);
            GetComponent<RectTransform>().sizeDelta = new Vector2(
                Width, HeightForCount(cities.Count, pExpanded));
        }

        internal static float HeightForCount(int pCount, bool pExpanded)
        {
            if (!pExpanded) return CollapsedHeight;
            int rows = Mathf.CeilToInt(Mathf.Max(1, pCount) / (float)Columns);
            return CollapsedHeight + rows * MiniHeight + Mathf.Max(0, rows - 1) * 4f + 8f;
        }

        internal void ToggleExpanded()
        {
            SetExpanded(!_expanded);
            _expandedChanged?.Invoke(_expanded);
        }

        internal void SetExpanded(bool pExpanded)
        {
            _expanded = pExpanded;
            if (_arrow != null) _arrow.text = pExpanded ? "-" : "+";
            foreach (CourtActorNodeView official in _officialPool)
                if (official != null) official.gameObject.SetActive(pExpanded);
            foreach (Text label in _cityLabels)
                if (label != null) label.gameObject.SetActive(pExpanded);
        }

        private void BuildUi()
        {
            AW_UIStyle.ApplyPanel(GetComponent<Image>(), 0.88f);
            GetComponent<Image>().raycastTarget = false;
            GameObject header = new GameObject("ToggleExpanded", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            header.transform.SetParent(transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, CollapsedHeight);
            AW_UIStyle.ApplyButton(header.GetComponent<Image>(), 0.96f);
            _toggle = header.GetComponent<Button>();
            _toggle.onClick.AddListener(ToggleExpanded);
            TipButton tip = header.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.showOnClick = false;
            tip.hoverAction = () => Tooltip.show(header, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = _label?.text ?? AW_L10n.Text(
                        "aw_court_regional_folder", "Regional officials"),
                    tip_description = AW_L10n.Text(
                        _expanded ? "aw_court_regional_folder_desc_expanded" :
                            "aw_court_regional_folder_desc_collapsed",
                        _expanded ? "Collapse subordinate officials" :
                            "Expand subordinate officials")
                });
            _label = CreateText(header.transform, "Label", 9, TextAnchor.MiddleLeft);
            _label.rectTransform.anchorMin = Vector2.zero;
            _label.rectTransform.anchorMax = Vector2.one;
            _label.rectTransform.offsetMin = new Vector2(10f, 2f);
            _label.rectTransform.offsetMax = new Vector2(-30f, -2f);
            _arrow = CreateText(header.transform, "Arrow", 12, TextAnchor.MiddleCenter);
            _arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
            _arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
            _arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
            _arrow.rectTransform.anchoredPosition = new Vector2(-7f, 0f);
            _arrow.rectTransform.sizeDelta = new Vector2(20f, 0f);
            _content = new GameObject("ExpandedOfficials", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _content.SetParent(transform, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = Vector2.zero;
        }

        private CourtActorNodeView GetOfficial(int pIndex)
        {
            while (_officialPool.Count <= pIndex)
            {
                CourtActorNodeView view = CourtActorNodeView.Create(_content);
                _officialPool.Add(view);
            }
            return _officialPool[pIndex];
        }

        private Text GetCityLabel(int pIndex)
        {
            while (_cityLabels.Count <= pIndex)
                _cityLabels.Add(CreateText(_content, "CityLabel_" + _cityLabels.Count,
                    7, TextAnchor.MiddleCenter));
            return _cityLabels[pIndex];
        }

        private void HideUnused(int pUsed)
        {
            for (int index = pUsed; index < _officialPool.Count; index++)
                if (_officialPool[index] != null) _officialPool[index].gameObject.SetActive(false);
            for (int index = pUsed; index < _cityLabels.Count; index++)
                if (_cityLabels[index] != null) _cityLabels[index].gameObject.SetActive(false);
        }

        private static CourtPyramidNodeModel VacancyNode(LocalCourtReadModel pCity)
        {
            return new CourtPyramidNodeModel(-1L, "city_leader:" + pCity.CityId,
                CourtPyramidRoleId.Governor, CourtPyramidRules.GovernorRank,
                -1, true)
            {
                OfficeLayer = CourtOfficeLayer.City,
                CityId = pCity.CityId,
                CityName = pCity.CityName,
                DisplayTitle = pCity.LocalLevelTitle
            };
        }

        private static Text CreateText(Transform pParent, string pName, int pSize,
            TextAnchor pAnchor)
        {
            Text text = new GameObject(pName, typeof(RectTransform), typeof(Text))
                .GetComponent<Text>();
            text.transform.SetParent(pParent, false);
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }
    }
}
