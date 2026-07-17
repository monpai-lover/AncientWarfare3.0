using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class CentralPowerVassalRowView : MonoBehaviour
    {
        private Text _text;

        public static CentralPowerVassalRowView Create(Transform pParent)
        {
            var obj = new GameObject("CentralPowerVassalRow", typeof(RectTransform),
                typeof(CentralPowerVassalRowView));
            obj.transform.SetParent(pParent, false);
            var view = obj.GetComponent<CentralPowerVassalRowView>();
            view._text = obj.AddComponent<Text>();
            view._text.font = LocalizedTextManager.current_font;
            view._text.fontSize = 9;
            view._text.alignment = TextAnchor.MiddleLeft;
            view._text.horizontalOverflow = HorizontalWrapMode.Wrap;
            view._text.color = Color.white;
            return view;
        }

        public void Bind(CentralPowerVassalInfo pInfo)
        {
            if (_text == null || pInfo == null) return;
            _text.text = pInfo.kingdom_name + "  |  " +
                         AW_L10n.Text("aw_central_tribute", "Tribute") + " " +
                         pInfo.effective_tribute_rate + "%  |  " +
                         AW_L10n.Text("aw_central_autonomy", "Autonomy") + " " +
                         pInfo.effective_autonomy + "%  |  " +
                         AW_L10n.Text("aw_central_obligation", "Obligation") + " " +
                         pInfo.effective_military_obligation + "%";
            if (ColorUtility.TryParseHtmlString(pInfo.kingdom_color, out Color color))
                _text.color = color;
        }

        public void Layout(float pY, float pWidth)
        {
            RectTransform rect = GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -pY);
            rect.sizeDelta = new Vector2(Mathf.Max(80f, pWidth - 16f), 22f);
        }
    }
}
