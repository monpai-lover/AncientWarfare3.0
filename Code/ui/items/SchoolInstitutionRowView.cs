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
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
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
            _label.text = (pInstitution.InstitutionType ?? "institution") +
                          "  Lv." + pInstitution.Level + "  " +
                          (pCityName ?? ("city " + pInstitution.CityId)) +
                          "  " + Mathf.RoundToInt((float)pInstitution.Condition) + "%";
            gameObject.SetActive(true);
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
            _label.verticalOverflow = VerticalWrapMode.Truncate;
            _label.resizeTextForBestFit = true;
            _label.resizeTextMinSize = 6;
            _label.resizeTextMaxSize = 8;
            _label.raycastTarget = false;
        }
    }
}
