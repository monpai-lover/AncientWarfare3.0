using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolInfluenceBreakdownView : MonoBehaviour
    {
        private Text _title;
        private Text _body;

        public static SchoolInfluenceBreakdownView Create(Transform pParent)
        {
            var obj = new GameObject("SchoolInfluenceBreakdown", typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 92f);
            obj.GetComponent<Image>().color = new Color(.06f, .055f, .045f, .86f);
            var view = obj.AddComponent<SchoolInfluenceBreakdownView>();
            view.Build();
            return view;
        }

        public void Bind(City pCity, string pSchoolId = null)
        {
            if (pCity?.data == null)
            {
                gameObject.SetActive(false);
                return;
            }
            if (string.IsNullOrEmpty(pSchoolId))
            {
                Dictionary<string, HistoricalSchoolLedgerSnapshot> ledgers =
                    HistoricalSchoolStore.LoadLedgersForCity(pCity.data.id);
                KeyValuePair<string, HistoricalSchoolLedgerSnapshot>[] rows = ledgers
                    .Where(p => p.Value != null)
                    .OrderByDescending(p => p.Value.Tradition + p.Value.Membership +
                                             p.Value.Institutions + p.Value.ActivePresence +
                                             p.Value.Momentum)
                    .Take(3)
                    .ToArray();
                _title.text = AW_L10n.Text("aw_school_influence_components",
                    "Influence Components");
                _body.text = rows.Length == 0
                    ? AW_L10n.Text("aw_school_no_durable_ledger", "No school ledger")
                    : string.Join("\n", rows.Select(p => ComponentLine(p.Key, p.Value))
                        .ToArray());
            }
            else
            {
                HistoricalSchoolLedgerSnapshot ledger =
                    HistoricalSchoolStore.LoadLedger(pCity.data.id, pSchoolId);
                CourtSchoolDefinition definition = CourtSchoolRegistry.Find(pSchoolId);
                _title.text = AW_L10n.Text("aw_school_influence_components",
                    "Influence Components") + ": " +
                              AW_L10n.Text(definition?.NameKey ?? "aw_court_school_none",
                                  pSchoolId);
                _body.text = AW_L10n.Text("aw_school_tradition", "Tradition") + " " +
                             Percent(ledger.Tradition) + "   " +
                             AW_L10n.Text("aw_school_membership", "Membership") + " " +
                             Percent(ledger.Membership) + "\n" +
                             AW_L10n.Text("aw_school_institutions", "Institutions") + " " +
                             ledger.Institutions.ToString("0.0") + "   " +
                             AW_L10n.Text("aw_school_active_presence", "Active Presence") +
                             " " + Percent(ledger.ActivePresence) + "\n" +
                             AW_L10n.Text("aw_school_momentum", "Momentum") + " " +
                             Percent(ledger.Momentum) +
                             "   " + AW_L10n.Text("aw_school_last_active", "Active year") +
                             " " + ledger.LastActiveYear;
            }
            gameObject.SetActive(true);
        }

        private static string ComponentLine(string pSchoolId,
            HistoricalSchoolLedgerSnapshot pLedger)
        {
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(pSchoolId);
            return AW_L10n.Text(definition?.NameKey ?? "aw_court_school_none", pSchoolId) +
                   "  " + AW_L10n.Text("aw_school_tradition", "Tradition") + " " +
                   Percent(pLedger.Tradition) + "  " +
                   AW_L10n.Text("aw_school_membership", "Membership") + " " +
                   Percent(pLedger.Membership) + "  " +
                   AW_L10n.Text("aw_school_institutions", "Institutions") + " " +
                   pLedger.Institutions.ToString("0.0") + "  " +
                   AW_L10n.Text("aw_school_active_presence", "Presence") + " " +
                   Percent(pLedger.ActivePresence) + "  " +
                   AW_L10n.Text("aw_school_momentum", "Momentum") + " " +
                   Percent(pLedger.Momentum);
        }

        private void Build()
        {
            _title = Text("Title", new Vector2(10f, -7f), new Vector2(-20f, 18f), 9,
                TextAnchor.UpperLeft);
            _title.color = new Color(.95f, .84f, .55f, 1f);
            _body = Text("Body", new Vector2(10f, -28f), new Vector2(-20f, 56f), 8,
                TextAnchor.UpperLeft);
            _body.color = new Color(.84f, .82f, .76f, 1f);
        }

        private Text Text(string pName, Vector2 pPosition, Vector2 pSize, int pFontSize,
            TextAnchor pAlignment)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAlignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pFontSize;
            text.raycastTarget = false;
            return text;
        }

        private static string Percent(float pValue) =>
            Mathf.RoundToInt(Mathf.Clamp01(pValue) * 100f) + "%";
    }
}
