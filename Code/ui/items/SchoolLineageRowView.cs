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
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
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
            _label.text = (pIsAlive ? "" : "[dead] ") + pStudent + "  <-  " +
                          (string.IsNullOrWhiteSpace(pTeacher) ? "founder" : pTeacher) +
                          "  G" + pGeneration + "  Rep " + Mathf.RoundToInt(pReputation);
            _label.color = pIsAlive ? new Color(.84f, .86f, .80f, 1f) :
                new Color(.55f, .55f, .55f, 1f);
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
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Truncate;
            _label.resizeTextForBestFit = true;
            _label.resizeTextMinSize = 6;
            _label.resizeTextMaxSize = 8;
            _label.raycastTarget = false;
        }
    }
}
