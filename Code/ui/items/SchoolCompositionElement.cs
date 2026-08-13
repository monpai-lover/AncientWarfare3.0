using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.windows;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolCompositionElement : MonoBehaviour
    {
        private const float Height = 72f;
        private const float HeaderWidth = 152f;
        private const float CellWidth = 96f;
        private const float DetailsWidth = 64f;
        private const float Gap = 4f;
        private const int MaxVisibleCells = 4;

        private sealed class Cell
        {
            public GameObject Root;
            public Image Background;
            public Image Icon;
            public Text Label;
            public Button Button;
        }

        private readonly List<Cell> _cells = new List<Cell>();
        private Image _dominantIcon;
        private Text _cityName;
        private Text _dominantName;
        private Button _detailsButton;
        private Text _detailsText;

        public static SchoolCompositionElement Create(Transform pParent)
        {
            var obj = new GameObject("element_school_composition", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(HeaderWidth + DetailsWidth + Gap, Height);
            AW_UIStyle.ApplyPanel(obj.GetComponent<Image>(), .96f);
            var element = obj.AddComponent<SchoolCompositionElement>();
            element.Build();
            return element;
        }

        public void Bind(City pCity, CitySchoolSnapshot pSnapshot)
        {
            if (pCity?.data == null || pSnapshot == null)
            {
                gameObject.SetActive(false);
                return;
            }

            CourtSchoolDefinition dominant = CourtSchoolRegistry.Find(pSnapshot.DominantSchool);
            _dominantIcon.sprite = dominant == null
                ? null
                : SpriteTextureLoader.getSprite(dominant.IconPath);
            _dominantIcon.enabled = _dominantIcon.sprite != null;
            _cityName.text = AW_L10n.Text("aw_school_composition", "School Composition") +
                             " - " + (pCity.data.name ?? "");
            _dominantName.text = AW_L10n.Text("aw_school_dominant_short", "Dominant") + ": " +
                                 SchoolName(dominant);

            KeyValuePair<string, float>[] scores = pSnapshot.Scores
                .Where(p => p.Value > 0f && CourtSchoolRegistry.Find(p.Key) != null)
                .OrderByDescending(p => p.Value)
                .ThenBy(p => RegistryOrder(p.Key))
                .ToArray();
            KeyValuePair<string, float>[] visibleScores = scores.Take(MaxVisibleCells).ToArray();
            EnsureCells(visibleScores.Length);
            for (int i = 0; i < _cells.Count; i++)
            {
                Cell cell = _cells[i];
                if (i >= visibleScores.Length)
                {
                    cell.Root.SetActive(false);
                    continue;
                }

                KeyValuePair<string, float> score = visibleScores[i];
                CourtSchoolDefinition definition = CourtSchoolRegistry.Find(score.Key);
                float share = pSnapshot.TotalScore <= 0f ? 0f : score.Value / pSnapshot.TotalScore;
                cell.Icon.sprite = SpriteTextureLoader.getSprite(definition.IconPath);
                cell.Icon.enabled = cell.Icon.sprite != null;
                cell.Label.text = SchoolInfluenceLabelRules.Build(SchoolName(definition), score.Value, share);
                cell.Background.color = Color.Lerp(Parse(definition.ColorHex), Color.black, .68f);
                cell.Button.onClick.RemoveAllListeners();
                string schoolId = definition.Id;
                cell.Button.onClick.AddListener(() => OpenSchoolWindow(schoolId));
                cell.Root.SetActive(true);
            }

            long cityId = pCity.data.id;
            _detailsButton.onClick.RemoveAllListeners();
            _detailsButton.onClick.AddListener(() => OpenSchoolWindow(cityId));
            int hidden = Math.Max(0, scores.Length - visibleScores.Length);
            _detailsText.text = AW_L10n.Text("aw_school_details", "Details") +
                                (hidden > 0 ? " +" + hidden : "");
            Layout(visibleScores.Length);
            gameObject.SetActive(true);
        }

        private static void OpenSchoolWindow(string pSchoolId)
        {
            // The selected-city tab owns the map-mode composition UI. It must
            // be closed before the school window can open a native UnitWindow.
            SchoolMapBottomBarController.Hide();
            SchoolWindow.OpenSchool(pSchoolId);
        }

        private static void OpenSchoolWindow(long pCityId)
        {
            SchoolMapBottomBarController.Hide();
            SchoolWindow.OpenCity(pCityId);
        }

        private void Build()
        {
            _dominantIcon = Image("DominantSchoolIcon", new Vector2(7f, -8f), new Vector2(38f, 38f));
            _cityName = Text("CityName", new Vector2(49f, -8f), new Vector2(98f, 20f), 9,
                TextAnchor.UpperLeft);
            _dominantName = Text("DominantSchool", new Vector2(49f, -31f), new Vector2(98f, 29f), 8,
                TextAnchor.UpperLeft);
            _dominantName.color = new Color(.92f, .82f, .55f, 1f);

            var details = new GameObject("SchoolDetails", typeof(RectTransform), typeof(Image), typeof(Button));
            details.transform.SetParent(transform, false);
            AW_UIStyle.ApplyButton(details.GetComponent<Image>(), .96f);
            _detailsButton = details.GetComponent<Button>();
            _detailsText = ChildText(details.transform, "Label", Vector2.zero, Vector2.zero, 8,
                TextAnchor.MiddleCenter, pStretch: true);
        }

        private void EnsureCells(int pCount)
        {
            int required = Math.Min(CourtSchoolRegistry.All.Count, Math.Max(0, pCount));
            while (_cells.Count < required)
                _cells.Add(CreateCell(_cells.Count));
        }

        private Cell CreateCell(int pIndex)
        {
            var obj = new GameObject("SchoolCell" + pIndex, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(transform, false);
            var cell = new Cell
            {
                Root = obj,
                Background = obj.GetComponent<Image>(),
                Button = obj.GetComponent<Button>()
            };
            AW_UIStyle.ApplyButton(cell.Background, .96f);

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(obj.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 1f);
            iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0f, 1f);
            iconRect.anchoredPosition = new Vector2(5f, -9f);
            iconRect.sizeDelta = new Vector2(34f, 34f);
            cell.Icon = iconObject.GetComponent<Image>();
            cell.Icon.preserveAspect = true;
            cell.Icon.raycastTarget = false;

            cell.Label = ChildText(obj.transform, "Label", new Vector2(43f, -7f),
                new Vector2(CellWidth - 47f, 48f), 8, TextAnchor.MiddleLeft);
            return cell;
        }

        private void Layout(int pVisibleCells)
        {
            int count = Math.Max(0, pVisibleCells);
            float x = HeaderWidth;
            for (int i = 0; i < count && i < _cells.Count; i++)
            {
                RectTransform rect = _cells[i].Root.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x, -5f);
                rect.sizeDelta = new Vector2(CellWidth, Height - 10f);
                x += CellWidth + Gap;
            }

            RectTransform detailsRect = _detailsButton.GetComponent<RectTransform>();
            detailsRect.anchorMin = new Vector2(0f, 1f);
            detailsRect.anchorMax = new Vector2(0f, 1f);
            detailsRect.pivot = new Vector2(0f, 1f);
            detailsRect.anchoredPosition = new Vector2(x, -5f);
            detailsRect.sizeDelta = new Vector2(DetailsWidth, Height - 10f);
            GetComponent<RectTransform>().sizeDelta = new Vector2(x + DetailsWidth, Height);
        }

        private Image Image(string pName, Vector2 pPosition, Vector2 pSize)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            Image image = obj.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private Text Text(string pName, Vector2 pPosition, Vector2 pSize, int pFontSize,
            TextAnchor pAnchor)
        {
            return ChildText(transform, pName, pPosition, pSize, pFontSize, pAnchor);
        }

        private static Text ChildText(Transform pParent, string pName, Vector2 pPosition,
            Vector2 pSize, int pFontSize, TextAnchor pAnchor, bool pStretch = false)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = pStretch ? Vector2.zero : new Vector2(0f, 1f);
            rect.anchorMax = pStretch ? Vector2.one : new Vector2(0f, 1f);
            rect.pivot = pStretch ? new Vector2(.5f, .5f) : new Vector2(0f, 1f);
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAnchor;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pFontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static string SchoolName(CourtSchoolDefinition pDefinition)
        {
            return pDefinition == null
                ? AW_L10n.Text("aw_court_school_none", "No school")
                : AW_L10n.Text(pDefinition.NameKey, pDefinition.Id);
        }

        private static int RegistryOrder(string pSchoolId)
        {
            for (int i = 0; i < CourtSchoolRegistry.All.Count; i++)
                if (CourtSchoolRegistry.All[i].Id == pSchoolId) return i;
            return int.MaxValue;
        }

        private static Color Parse(string pHex)
        {
            return ColorUtility.TryParseHtmlString(pHex, out Color color) ? color : Color.gray;
        }
    }
}
