using System;
using System.Text;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class DiplomacyKingdomListItem : MonoBehaviour
    {
        public const float Height = 48f;
        private long _kingdomId = -1L;
        private Action<long> _select;
        private Image _background;
        private Image _stripe;
        private Image _flagBackground;
        private Image _flagIcon;
        private Text _name;
        private Text _detail;
        private TipButton _tip;

        public static DiplomacyKingdomListItem Create(Transform pParent)
        {
            var obj = new GameObject("DiplomacyKingdom", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            obj.transform.SetParent(pParent, false);
            var item = obj.AddComponent<DiplomacyKingdomListItem>();
            item.Build();
            return item;
        }

        public void Bind(Kingdom pKingdom, string pName, string pDetail,
            int pOpinion, Color pColor, bool pSelected,
            bool pHasTributaryDetails, Action<long> pSelect)
        {
            _kingdomId = pKingdom?.id ?? -1L;
            _select = pSelect;
            _name.text = pName ?? "";
            _detail.text = pDetail ?? "";
            _detail.color = OpinionColor(pOpinion);
            _stripe.color = pColor;
            _name.color = Color.Lerp(pColor, Color.white, .35f);
            _background.color = pSelected
                ? new Color(.24f, .22f, .18f, .98f)
                : new Color(.11f, .105f, .09f, .94f);
            SetRowHeight(pHasTributaryDetails ? 62f : Height);
            if (pKingdom?.data != null)
            {
                string bannerId = "";
                try { bannerId = pKingdom.getActorAsset()?.banner_id ?? ""; }
                catch { }
                KingdomFlagBuilder.Build(bannerId,
                    pKingdom.data.banner_icon_id,
                    pKingdom.data.banner_background_id,
                    HistoryColors.FromKingdom(pKingdom),
                    pKingdom.data.color_id, _flagBackground, _flagIcon);
                BindTooltip(pKingdom, pDetail);
            }
            gameObject.SetActive(true);
        }

        private static Color OpinionColor(int pOpinion)
        {
            Color neutral = new Color(.94f, .78f, .28f, 1f);
            if (pOpinion == 0) return neutral;
            float amount = Mathf.Clamp01(Mathf.Abs(pOpinion) / 100f);
            return pOpinion > 0
                ? Color.Lerp(neutral, new Color(.24f, .88f, .34f, 1f),
                    amount)
                : Color.Lerp(neutral, new Color(.92f, .20f, .18f, 1f),
                    amount);
        }

        public void Unbind()
        {
            _kingdomId = -1L;
            _select = null;
            gameObject.SetActive(false);
        }

        private void SetRowHeight(float pHeight)
        {
            RectTransform rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(190f, pHeight);
            LayoutElement layout = GetComponent<LayoutElement>();
            layout.minHeight = pHeight;
            layout.preferredHeight = pHeight;
            Layout(_stripe.rectTransform, 0f, 0f, 4f, pHeight);
            Layout(_flagBackground.rectTransform, 8f,
                (pHeight - 26f) * .5f, 26f, 26f);
            Stretch(_name.rectTransform, 40f, 5f, 6f, 18f);
            Stretch(_detail.rectTransform, 40f, 26f, 6f,
                Mathf.Max(17f, pHeight - 31f));
        }

        private void Build()
        {
            RectTransform rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(190f, Height);
            LayoutElement layout = GetComponent<LayoutElement>();
            layout.minHeight = Height;
            layout.preferredHeight = Height;
            layout.flexibleWidth = 1f;
            _background = GetComponent<Image>();
            _tip = gameObject.AddComponent<TipButton>();
            _tip.type = AW_RawTooltip.TYPE;
            GetComponent<Button>().onClick.AddListener(
                () => _select?.Invoke(_kingdomId));

            var stripe = new GameObject("CountryColor", typeof(RectTransform),
                typeof(Image));
            stripe.transform.SetParent(transform, false);
            _stripe = stripe.GetComponent<Image>();
            _stripe.raycastTarget = false;
            Layout(_stripe.rectTransform, 0f, 0f, 4f, Height);

            var flag = new GameObject("Flag", typeof(RectTransform),
                typeof(Image));
            flag.transform.SetParent(transform, false);
            _flagBackground = flag.GetComponent<Image>();
            _flagBackground.preserveAspect = true;
            _flagBackground.raycastTarget = false;
            Layout(_flagBackground.rectTransform, 8f, 11f, 26f, 26f);
            var flagIcon = new GameObject("FlagIcon", typeof(RectTransform),
                typeof(Image));
            flagIcon.transform.SetParent(flag.transform, false);
            _flagIcon = flagIcon.GetComponent<Image>();
            _flagIcon.preserveAspect = true;
            _flagIcon.raycastTarget = false;
            _flagIcon.rectTransform.anchorMin = Vector2.zero;
            _flagIcon.rectTransform.anchorMax = Vector2.one;
            _flagIcon.rectTransform.offsetMin = Vector2.zero;
            _flagIcon.rectTransform.offsetMax = Vector2.zero;

            _name = CreateText("Name", 10, TextAnchor.UpperLeft);
            Stretch(_name.rectTransform, 40f, 5f, 6f, 18f);
            _detail = CreateText("Detail", 8, TextAnchor.UpperLeft);
            _detail.color = new Color(.76f, .74f, .69f, 1f);
            Stretch(_detail.rectTransform, 40f, 26f, 6f, 17f);
        }

        private void BindTooltip(Kingdom pKingdom, string pRelation)
        {
            string ruler = pKingdom.king?.getName() ??
                           AW_L10n.Text("aw_diplomacy_unknown_ruler",
                               "No ruler");
            string capital = pKingdom.capital?.name ??
                             AW_L10n.Text("aw_diplomacy_unknown_capital",
                                 "No capital");
            var detail = new StringBuilder();
            detail.AppendLine(pRelation ?? "");
            detail.AppendLine(AW_L10n.Text("aw_diplomacy_ruler", "Ruler") +
                              ": " + ruler);
            detail.AppendLine(AW_L10n.Text("aw_diplomacy_capital", "Capital") +
                              ": " + capital);
            detail.Append(AW_L10n.Text("aw_diplomacy_power", "Military power") +
                          ": " + Math.Max(0, pKingdom.power));
            _tip.enabled = true;
            _tip.hoverAction = () => Tooltip.show(gameObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = SuccessionDisputeService.GetDisplayName(
                        pKingdom),
                    tip_description = detail.ToString()
                });
        }

        private Text CreateText(string pName, int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(transform, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static void Stretch(RectTransform pRect, float pLeft,
            float pTop, float pRight, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(1f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pLeft, -pTop);
            pRect.sizeDelta = new Vector2(-pLeft - pRight, pHeight);
        }
    }
}
