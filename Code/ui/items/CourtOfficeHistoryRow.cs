using AncientWarfare3.core.court;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class CourtOfficeHistoryRow : MonoBehaviour
    {
        internal const float Height = 48f;

        private Image _background;
        private Text _name;
        private Text _details;
        private Text _reason;

        internal static CourtOfficeHistoryRow Create(Transform pParent)
        {
            var obj = new GameObject("CourtOfficeHistoryRow",
                typeof(RectTransform), typeof(Image), typeof(Outline));
            obj.transform.SetParent(pParent, false);
            var row = obj.AddComponent<CourtOfficeHistoryRow>();
            row._background = obj.GetComponent<Image>();
            AW_UIStyle.ApplyButton(row._background, 0.97f);
            Outline outline = obj.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(1f, -1f);
            row._name = CreateText(obj.transform, "Name", 10,
                TextAnchor.MiddleLeft);
            row._details = CreateText(obj.transform, "Details", 8,
                TextAnchor.MiddleLeft);
            row._details.color = new Color(0.94f, 0.84f, 0.58f, 1f);
            row._reason = CreateText(obj.transform, "Reason", 8,
                TextAnchor.MiddleRight);
            row._reason.color = new Color(0.78f, 0.8f, 0.82f, 1f);
            return row;
        }

        internal void Bind(OfficialCareerHistoryRow pRow,
            string pOfficeName, string pCityName, string pYearRange,
            string pEndReason)
        {
            if (pRow == null)
            {
                Unbind();
                return;
            }
            _name.text = string.IsNullOrWhiteSpace(pRow.ActorName)
                ? AW_L10n.Text("aw_court_history_unknown_actor", "Unknown")
                : pRow.ActorName;
            _details.text = pOfficeName +
                            (string.IsNullOrWhiteSpace(pCityName)
                                ? ""
                                : "  |  " + pCityName) +
                            "  |  " + pYearRange;
            _reason.text = pRow.IsCurrent ? AW_L10n.Text(
                "aw_court_history_current", "Current") : pEndReason;
            gameObject.SetActive(true);
        }

        internal void Layout(float pY, float pWidth)
        {
            RectTransform rect = GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(4f, -pY);
            rect.sizeDelta = new Vector2(Mathf.Max(120f, pWidth - 8f),
                Height - 4f);
            LayoutText(_name.rectTransform, 8f, 3f,
                rect.sizeDelta.x * 0.48f, 18f);
            LayoutText(_details.rectTransform, 8f, 22f,
                rect.sizeDelta.x - 16f, 17f);
            LayoutText(_reason.rectTransform, rect.sizeDelta.x * 0.5f, 3f,
                rect.sizeDelta.x * 0.5f - 8f, 18f);
        }

        internal void Unbind()
        {
            gameObject.SetActive(false);
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static void LayoutText(RectTransform pRect, float pX,
            float pY, float pWidth, float pHeight)
        {
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth), pHeight);
        }
    }
}
