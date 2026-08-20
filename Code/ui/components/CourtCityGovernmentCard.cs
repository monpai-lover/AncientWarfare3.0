using System;
using AncientWarfare3.core.court;
using AncientWarfare3.ui.items;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class CourtCityGovernmentCard : MonoBehaviour
    {
        internal const float Width = 148f;
        internal const float Height = 58f;

        private Text _title;
        private Text _governorName;
        private Text _summary;
        private Button _openButton;
        private CourtActorNodeView _leader;

        internal CourtActorNodeView LeaderNode => null;

        internal static CourtCityGovernmentCard Create(Transform pParent)
        {
            var obj = new GameObject("CourtCityGovernmentCard",
                typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(Width, Height);
            CourtCityGovernmentCard card =
                obj.AddComponent<CourtCityGovernmentCard>();
            card.BuildUi();
            return card;
        }

        internal void Bind(LocalCourtReadModel pModel, Kingdom pKingdom,
            Action<long> pOpenCity)
        {
            if (pModel == null || pKingdom?.data == null) return;
            gameObject.name = "CourtCityGovernment_" + pModel.CityId;
            string cityLabel = RegionalGovernmentRules.AdministrativeLabel(
                RegionalGovernmentRules.CityName(pModel.CityName),
                pModel.LocalLevelTitle);
            _title.text = cityLabel + " | " + pModel.CityTypeName;
            _governorName.text = AW_L10n.Text("aw_local_office_zhoumu", "郡守") +
                ": " + (pModel.RegionalSuperiorNode?.ActorName ??
                    AW_L10n.Text("aw_court_vacancy", "Vacant"));
            _summary.text = string.Format(
                AW_L10n.Text("aw_local_court_card_summary",
                    "Officials {0}/{1}  Efficiency {2}"),
                pModel.ActiveSeats, pModel.TotalSeats,
                Mathf.FloorToInt(pModel.Efficiency)) + "\n" +
                AW_L10n.Text("aw_corruption_country", "Country corruption") +
                " " + (pModel.CountryCorruption?.Score ?? 0) + "  " +
                AW_L10n.Text("aw_corruption_local", "Local corruption") +
                " " + (pModel.CityCorruption?.Score ?? 0);
            _openButton.onClick.RemoveAllListeners();
            long cityId = pModel.CityId;
            _openButton.onClick.AddListener(() => pOpenCity?.Invoke(cityId));
            _leader.gameObject.SetActive(false);
            _summary.gameObject.SetActive(false);
        }

        private void BuildUi()
        {
            Image background = GetComponent<Image>();
            AW_UIStyle.ApplyPanel(background, 0.82f);
            background.raycastTarget = false;

            var header = new GameObject("OpenCityGovernment",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            header.transform.SetParent(transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 20f);
            AW_UIStyle.ApplyButton(header.GetComponent<Image>(), 0.94f);
            _openButton = header.GetComponent<Button>();
            _title = CreateText(header.transform, "Title", 9,
                TextAnchor.MiddleCenter);
            _title.rectTransform.anchorMin = Vector2.zero;
            _title.rectTransform.anchorMax = Vector2.one;
            _title.rectTransform.offsetMin = new Vector2(3f, 1f);
            _title.rectTransform.offsetMax = new Vector2(-3f, -1f);

            _leader = CourtActorNodeView.Create(transform);
            _leader.gameObject.SetActive(false);

            _governorName = CreateText(transform, "GovernorName", 8,
                TextAnchor.MiddleCenter);
            RectTransform governorRect = _governorName.rectTransform;
            governorRect.anchorMin = new Vector2(0f, 1f);
            governorRect.anchorMax = new Vector2(1f, 1f);
            governorRect.pivot = new Vector2(0.5f, 1f);
            governorRect.anchoredPosition = new Vector2(0f, -24f);
            governorRect.sizeDelta = new Vector2(-8f, 24f);

            _summary = CreateText(transform, "Summary", 7,
                TextAnchor.MiddleCenter);
            _summary.gameObject.SetActive(false);
            RectTransform summaryRect = _summary.rectTransform;
            summaryRect.anchorMin = new Vector2(0f, 0f);
            summaryRect.anchorMax = new Vector2(1f, 0f);
            summaryRect.pivot = new Vector2(0.5f, 0f);
            summaryRect.anchoredPosition = new Vector2(0f, 2f);
            summaryRect.sizeDelta = new Vector2(-6f, 12f);
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            Text text = new GameObject(pName, typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
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
