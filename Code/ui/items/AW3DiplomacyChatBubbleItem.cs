using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class AW3DiplomacyChatBubbleItem : MonoBehaviour
    {
        private RectTransform _bubble;
        private Image _background;
        private Text _text;
        private LayoutElement _layout;
        private RectTransform _flagRoot;
        private Image _flagBackground;
        private Image _flagIcon;
        private TipButton _flagTip;
        private string _boundFlagSignature = string.Empty;

        public static AW3DiplomacyChatBubbleItem Create(Transform pParent)
        {
            var row = new GameObject("AW3MultiplayerChatBubbleRow",
                typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(pParent, false);
            var item = row.AddComponent<AW3DiplomacyChatBubbleItem>();
            item.Build();
            return item;
        }

        public void Bind(AW3DiplomacyChatEntry pEntry,
            long pBaseCountryId, float pRowWidth)
        {
            if (pEntry == null)
            {
                Unbind();
                return;
            }

            bool local = pEntry.SenderCountryId == pBaseCountryId;
            float maxWidth = Mathf.Max(140f, pRowWidth * .72f);
            string rulerName = RulerName(pEntry.SenderRulerActorId);
            _text.text = pEntry.SenderPlayerId +
                         (string.IsNullOrEmpty(rulerName)
                             ? string.Empty
                             : " | " + rulerName) +
                         "  #" + pEntry.HostSequence + "\n" + pEntry.Text;
            _text.rectTransform.sizeDelta = new Vector2(maxWidth - 18f, 400f);
            float textHeight = Mathf.Clamp(_text.preferredHeight, 34f, 220f);
            float bubbleHeight = textHeight + 12f;
            const float flagSlot = 32f;
            float x = local
                ? Mathf.Max(0f, pRowWidth - maxWidth - flagSlot - 3f)
                : flagSlot + 3f;
            Layout(_bubble, x, 2f, maxWidth, bubbleHeight);
            Layout(_text.rectTransform, 9f, 6f, maxWidth - 18f,
                textHeight);
            BindFlag(pEntry.SenderCountryId, local, pRowWidth,
                pEntry.SenderPlayerId, rulerName);
            _layout.minHeight = bubbleHeight + 4f;
            _layout.preferredHeight = bubbleHeight + 4f;
            _background.color = local
                ? new Color(.12f, .31f, .27f, .98f)
                : new Color(.25f, .20f, .14f, .98f);
            gameObject.SetActive(true);
        }

        public void Unbind()
        {
            gameObject.SetActive(false);
        }

        private void Build()
        {
            _layout = GetComponent<LayoutElement>();
            _layout.flexibleWidth = 1f;
            var bubble = new GameObject("MultiplayerBubble",
                typeof(RectTransform), typeof(Image));
            bubble.transform.SetParent(transform, false);
            _bubble = bubble.GetComponent<RectTransform>();
            _background = bubble.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(_background, .98f);
            _background.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(bubble.transform, false);
            _text = textObject.GetComponent<Text>();
            _text.font = LocalizedTextManager.current_font;
            _text.fontSize = 9;
            _text.alignment = TextAnchor.UpperLeft;
            _text.color = Color.white;
            _text.supportRichText = false;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            _text.raycastTarget = false;

            var flag = new GameObject("SpeakerFlag", typeof(RectTransform),
                typeof(Image));
            flag.transform.SetParent(transform, false);
            _flagRoot = flag.GetComponent<RectTransform>();
            _flagBackground = flag.GetComponent<Image>();
            _flagBackground.preserveAspect = true;
            _flagBackground.raycastTarget = true;
            _flagTip = flag.AddComponent<TipButton>();
            _flagTip.type = AW_RawTooltip.TYPE;
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
            _flagRoot.gameObject.SetActive(false);
        }

        private void BindFlag(long pCountryId, bool pLocal,
            float pRowWidth, string pPlayerId, string pRulerName)
        {
            Kingdom speaker = FindKingdom(pCountryId);
            if (speaker?.data == null)
            {
                _flagRoot.gameObject.SetActive(false);
                return;
            }

            string bannerId = string.Empty;
            try { bannerId = speaker.getActorAsset()?.banner_id ?? string.Empty; }
            catch { }
            string color = HistoryColors.FromKingdom(speaker);
            string signature = speaker.id + ":" + bannerId + ":" +
                               speaker.data.banner_icon_id + ":" +
                               speaker.data.banner_background_id + ":" +
                               speaker.data.color_id + ":" + color;
            if (_boundFlagSignature != signature)
            {
                KingdomFlagBuilder.Build(bannerId,
                    speaker.data.banner_icon_id,
                    speaker.data.banner_background_id, color,
                    speaker.data.color_id, _flagBackground, _flagIcon);
                _boundFlagSignature = signature;
            }

            float x = pLocal ? Mathf.Max(0f, pRowWidth - 27f) : 3f;
            Layout(_flagRoot, x, 6f, 24f, 24f);
            _flagTip.enabled = true;
            _flagTip.hoverAction = () => Tooltip.show(_flagTip.gameObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = pPlayerId,
                    tip_description = string.IsNullOrEmpty(pRulerName)
                        ? speaker.name ?? string.Empty
                        : pRulerName
                });
            _flagRoot.gameObject.SetActive(true);
        }

        private static string RulerName(long pActorId)
        {
            try { return World.world?.units?.get(pActorId)?.getName() ?? ""; }
            catch { return string.Empty; }
        }

        private static Kingdom FindKingdom(long pCountryId)
        {
            try { return World.world?.kingdoms?.get(pCountryId); }
            catch { return null; }
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }
    }
}
