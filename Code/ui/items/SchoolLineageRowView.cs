using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolLineageRowView : MonoBehaviour
    {
        private Text _label;

        public static SchoolLineageRowView Create(Transform pParent)
        {
            var obj = new GameObject("SchoolLineageRow", typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(0f, 26f);
            obj.GetComponent<Image>().color = new Color(.075f, .07f, .06f, .82f);
            var view = obj.AddComponent<SchoolLineageRowView>();
            view.Build();
            return view;
        }

        public void Bind(string pStudent, string pTeacher, int pGeneration,
            float pReputation, bool pIsAlive)
        {
            if (string.IsNullOrWhiteSpace(pStudent))
            {
                gameObject.SetActive(false);
                return;
            }
            string life = pIsAlive
                ? ""
                : "[" + AW_L10n.Text("aw_school_lineage_dead", "Deceased") + "] ";
            string teacher = string.IsNullOrWhiteSpace(pTeacher)
                ? AW_L10n.Text("aw_school_lineage_founder", "Founder")
                : pTeacher;
            _label.text = life + pStudent + "  ←  " + teacher + "  " +
                          AW_L10n.Text("aw_school_roster_generation", "Generation") +
                          " " + pGeneration + "  " +
                          AW_L10n.Text("aw_school_roster_reputation", "Reputation") +
                          " " + Mathf.RoundToInt(pReputation);
            _label.color = pIsAlive ? new Color(.84f, .86f, .80f, 1f) :
                new Color(.55f, .55f, .55f, 1f);
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
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.resizeTextForBestFit = false;
            _label.raycastTarget = false;
        }
    }
}
