using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.windows;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class ShiLineageCompositionElement : MonoBehaviour
    {
        private Text _title;
        private readonly Button[] _rows = new Button[3];
        private readonly Button[] _focusButtons = new Button[3];
        private readonly Text[] _labels = new Text[3];
        private Text _dominant;

        public static ShiLineageCompositionElement Create(Transform pParent)
        {
            var obj = new GameObject("element_shi_composition", typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -8f);
            rect.sizeDelta = new Vector2(360f, 150f);
            AW_UIStyle.ApplyPanel(obj.GetComponent<Image>(), .96f);
            var result = obj.AddComponent<ShiLineageCompositionElement>();
            result.Build();
            return result;
        }

        public void Bind(City pCity, CityShiInfluenceSnapshot pSnapshot)
        {
            if (pCity?.data == null || pSnapshot == null)
            {
                gameObject.SetActive(false);
                return;
            }
            CityShiInfluenceBranch dominant = pSnapshot.FindBranch(
                pSnapshot.DominantShiId);
            _dominant.text = AW_L10n.Text("aw_shi_map_dominant", "Dominant") +
                ": " + (dominant?.DisplayName ?? AW_L10n.Text(
                    "aw_shi_map_none", "No Shi"));
            _title.text = AW_L10n.Text("aw_shi_map_composition", "Shi Composition") +
                " - " + (pCity.data.name ?? "");
            for (int i = 0; i < _rows.Length; i++)
            {
                CityShiInfluenceBranch branch = i < pSnapshot.Branches.Count
                    ? pSnapshot.Branches[i] : null;
                bool valid = branch != null && branch.ShiId >= 0L &&
                    branch.IsValid;
                _rows[i].gameObject.SetActive(true);
                _focusButtons[i].gameObject.SetActive(valid);
                // Invalid entries keep Button.interactable disabled so stale rows cannot be opened.
                _rows[i].interactable = valid;
                _focusButtons[i].interactable = valid;
                _rows[i].onClick.RemoveAllListeners();
                _focusButtons[i].onClick.RemoveAllListeners();
                if (!valid)
                {
                    _labels[i].text = branch == null
                        ? AW_L10n.Text("aw_shi_map_none", "No Shi")
                        : AW_L10n.Text("aw_shi_map_unknown", "Unknown Shi");
                    continue;
                }
                long shiId = branch.ShiId;
                _labels[i].text = branch.DisplayName + "  " +
                    pSnapshot.SharePercent(shiId) + "%";
                _rows[i].onClick.AddListener(() => OpenTree(shiId));
                _focusButtons[i].onClick.AddListener(() =>
                    ShiLineageMapModeService.SetFocus(shiId));
            }
            gameObject.SetActive(true);
        }

        private static void OpenTree(long shiId)
        {
            if (shiId < 0L) return;
            try
            {
                FamilyTreeWindow.OpenBigTree(shiId);
            }
            catch { }
        }

        private void Build()
        {
            _title = AddText(transform, "Title", new Vector2(10f, -8f),
                new Vector2(340f, 20f), 10, TextAnchor.UpperLeft);
            _dominant = AddText(transform, "Dominant", new Vector2(10f, -30f),
                new Vector2(340f, 18f), 8, TextAnchor.UpperLeft);
            for (int i = 0; i < _rows.Length; i++)
            {
                var row = new GameObject("ShiRow" + i, typeof(RectTransform),
                    typeof(Image), typeof(Button));
                row.transform.SetParent(transform, false);
                RectTransform rect = row.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(8f, -52f - i * 30f);
                rect.sizeDelta = new Vector2(344f, 26f);
                AW_UIStyle.ApplyButton(row.GetComponent<Image>(), .96f);
                _rows[i] = row.GetComponent<Button>();
                _focusButtons[i] = CreateFocusButton(row.transform);
                _labels[i] = AddText(row.transform, "Label", Vector2.zero,
                    Vector2.zero, 9, TextAnchor.MiddleLeft, true);
                _labels[i].rectTransform.offsetMin = new Vector2(8f, 0f);
                _labels[i].rectTransform.offsetMax = new Vector2(-52f, 0f);
            }
        }

        private static Button CreateFocusButton(Transform pParent)
        {
            var obj = new GameObject("Focus", typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, .5f);
            rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(1f, .5f);
            rect.anchoredPosition = new Vector2(-4f, 0f);
            rect.sizeDelta = new Vector2(42f, 20f);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            Text text = AddText(obj.transform, "Label", Vector2.zero,
                Vector2.zero, 7, TextAnchor.MiddleCenter, true);
            text.text = AW_L10n.Text("aw_shi_map_focus", "Focus");
            return obj.GetComponent<Button>();
        }

        private static Text AddText(Transform pParent, string pName,
            Vector2 pPosition, Vector2 pSize, int pSizeFont, TextAnchor pAnchor,
            bool pStretch = false)
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
            text.fontSize = pSizeFont;
            text.alignment = pAnchor;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSizeFont;
            text.raycastTarget = false;
            return text;
        }
    }
}
