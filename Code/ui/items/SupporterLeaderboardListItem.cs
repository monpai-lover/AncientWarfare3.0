using AncientWarfare3.ui;
using NeoModLoader.api;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SupporterLeaderboardListItem :
        AbstractListWindowItem<SupporterLeaderboardEntry>
    {
        private const float RowWidth = 220f;
        private Text _label;

        public override void Setup(SupporterLeaderboardEntry pObject)
        {
            EnsureUi();
            if (pObject == null)
            {
                _label.text = "";
                return;
            }

            string rank = pObject.Rank.ToString();
            string name = string.IsNullOrEmpty(pObject.Name) ? "Justin" : pObject.Name;
            string description = pObject.Description ?? "";
            string detail;
            if (!string.IsNullOrEmpty(description))
            {
                detail = description;
            }
            else
            {
                string amount = string.IsNullOrEmpty(pObject.Amount) ? "-" : pObject.Amount;
                // Monetary entries receive the localized currency prefix.
                string amountPrefix = decimal.TryParse(
                        amount,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out _)
                    ? AW_L10n.Text("aw_supporter_amount_prefix", "¥")
                    : "";
                detail = amountPrefix + amount;
            }
            string date = pObject.Date ?? "";
            string dateSuffix = string.IsNullOrEmpty(date) || string.Equals(date, "-",
                System.StringComparison.Ordinal) ? "" : "   " + date;
            _label.text = rank + "   " + name + "   " +
                          detail + dateSuffix;
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            RectTransform rect = gameObject.GetComponent<RectTransform>() ??
                                  gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(RowWidth, 28f);
            LayoutElement layout = gameObject.GetComponent<LayoutElement>() ??
                                    gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 28f;
            layout.preferredHeight = 28f;

            Image background = gameObject.GetComponent<Image>() ??
                               gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(background, 0.95f);

            GameObject labelObject = new GameObject("Label",
                typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);
            _label = labelObject.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 10;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.raycastTarget = false;
        }
    }
}
