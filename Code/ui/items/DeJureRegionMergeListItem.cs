using System;
using AncientWarfare3.core.court;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class DeJureRegionMergeListItem : MonoBehaviour
    {
        internal static GameObject Create(Transform pParent,
            DeJureRegionMergeCandidate pCandidate, bool pPrimary,
            Action pAction)
        {
            GameObject row = new GameObject("DeJureRegionMergeRow",
                typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(pParent, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(360f, 42f);
            AW_UIStyle.ApplyListRow(row.GetComponent<Image>(), 0.9f);
            Button button = row.GetComponent<Button>();
            button.interactable = pCandidate != null;
            if (pAction != null) button.onClick.AddListener(() => pAction());

            Text text = new GameObject("Text", typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(row.transform, false);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);
            text.font = LocalizedTextManager.current_font;
            text.fontSize = 9;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.supportRichText = true;
            text.text = pCandidate == null ? "" :
                (pPrimary ? pCandidate.PrimaryName : pCandidate.SecondaryName) +
                "\n" + (pPrimary ? pCandidate.PrimaryCityId :
                    pCandidate.SecondaryCityId).ToString();
            row.AddComponent<DeJureRegionMergeListItem>();
            return row;
        }
    }
}
