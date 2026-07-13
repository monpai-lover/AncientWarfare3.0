using System;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolInstitutionRowView : MonoBehaviour
    {
        private Text _label;

        public static SchoolInstitutionRowView Create(Transform pParent)
        {
            var obj = new GameObject("SchoolInstitutionRow", typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(0f, 26f);
            obj.GetComponent<Image>().color = new Color(.11f, .095f, .07f, .84f);
            var view = obj.AddComponent<SchoolInstitutionRowView>();
            view.Build();
            return view;
        }

        public void Bind(SchoolInstitutionReadModel pInstitution, string pCityName)
        {
            if (pInstitution == null)
            {
                gameObject.SetActive(false);
                return;
            }
            string type = AW_L10n.Text(pInstitution.InstitutionType,
                AW_L10n.Text("aw_school_institution_unknown", "Institution"));
            string city = string.IsNullOrWhiteSpace(pCityName)
                ? AW_L10n.Text("aw_school_unknown_city", "Unknown City") + " " +
                  pInstitution.CityId
                : pCityName;
            _label.text = type + "  " +
                          AW_L10n.Text("aw_school_institution_level", "Level") + " " +
                          pInstitution.Level + "  " + city + "  " +
                          AW_L10n.Text("aw_school_institution_condition", "Condition") +
                          " " + Mathf.RoundToInt((float)pInstitution.Condition) + "%";
            gameObject.SetActive(true);
        }

        public float LayoutHeight(float pWidth)
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null || _label == null) return 26f;
            float width = Mathf.Max(1f, pWidth);
            root.sizeDelta = new Vector2(width, 26f);
            float height = Mathf.Max(26f, Mathf.Ceil(_label.preferredHeight) + 6f);
            root.sizeDelta = new Vector2(width, height);
            return height;
        }

        private void Build()
        {
            var obj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 0f);
            rect.offsetMax = new Vector2(-8f, 0f);
            _label = obj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 8;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.color = new Color(.86f, .82f, .72f, 1f);
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.resizeTextForBestFit = false;
            _label.raycastTarget = false;
        }
    }
}
