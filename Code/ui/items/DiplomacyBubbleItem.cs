using AncientWarfare3.core.lineage;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class DiplomacyBubbleItem : MonoBehaviour
    {
        private RectTransform _bubble;
        private Image _background;
        private Text _text;
        private LayoutElement _layout;
        private Button _acceptButton;
        private Button _rejectButton;
        private Text _acceptText;
        private Text _rejectText;
        private RectTransform _flagRoot;
        private Image _flagBackground;
        private Image _flagIcon;
        private TipButton _flagTip;
        private string _boundFlagSignature = "";

        public static DiplomacyBubbleItem Create(Transform pParent)
        {
            var row = new GameObject("DiplomacyBubbleRow",
                typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(pParent, false);
            var item = row.AddComponent<DiplomacyBubbleItem>();
            item.Build();
            return item;
        }

        public void Bind(DiplomacyConversationEvent pEvent,
            long pBaseKingdomId, long pOtherKingdomId, float pRowWidth,
            Action<long, bool> pRespond)
        {
            if (pEvent == null)
            {
                Unbind();
                return;
            }
            DiplomacyBubbleSide side =
                DiplomacyConversationRules.ResolveBubbleSide(
                    pBaseKingdomId, pOtherKingdomId,
                    pEvent.SpeakerKingdomId);
            float maxWidth = Mathf.Max(140f, pRowWidth *
                (side == DiplomacyBubbleSide.Center ? .82f : .70f));
            string message = DiplomacyConversationService.BuildText(pEvent);
            _text.text = "<size=7><color=#A9A396>" +
                         DiplomacyConversationService.Timestamp(pEvent) +
                         "</color></size>\n" +
                         message;
            _text.rectTransform.sizeDelta = new Vector2(maxWidth - 18f, 400f);
            float textHeight = Mathf.Clamp(_text.preferredHeight, 30f, 220f);
            bool canRespond = pEvent.Proposal != null &&
                              pEvent.Proposal.Status ==
                              DiplomacyProposalStatus.Pending &&
                              pEvent.Proposal.ResponderKingdomId ==
                              pBaseKingdomId;
            float bubbleHeight = textHeight + 12f +
                                 (canRespond ? 28f : 0f);
            const float flagSlot = 32f;
            float x = side == DiplomacyBubbleSide.Right
                ? Mathf.Max(0f, pRowWidth - maxWidth - flagSlot - 3f)
                : side == DiplomacyBubbleSide.Center
                    ? Mathf.Max(0f, (pRowWidth - maxWidth) * .5f)
                    : flagSlot + 3f;
            Layout(_bubble, x, 2f, maxWidth, bubbleHeight);
            BindFlag(pEvent, side, pRowWidth);
            Layout(_text.rectTransform, 9f, 6f, maxWidth - 18f,
                textHeight);
            _acceptButton.gameObject.SetActive(canRespond);
            _rejectButton.gameObject.SetActive(canRespond);
            if (canRespond)
            {
                long proposalId = pEvent.Proposal.ProposalId;
                _acceptButton.onClick.RemoveAllListeners();
                _acceptButton.onClick.AddListener(
                    () => pRespond?.Invoke(proposalId, true));
                _rejectButton.onClick.RemoveAllListeners();
                _rejectButton.onClick.AddListener(
                    () => pRespond?.Invoke(proposalId, false));
                float buttonWidth = Mathf.Max(48f,
                    (maxWidth - 23f) * .5f);
                Layout(_rejectButton.GetComponent<RectTransform>(), 9f,
                    textHeight + 8f, buttonWidth, 22f);
                Layout(_acceptButton.GetComponent<RectTransform>(),
                    14f + buttonWidth, textHeight + 8f, buttonWidth, 22f);
            }
            _layout.minHeight = bubbleHeight + 4f;
            _layout.preferredHeight = bubbleHeight + 4f;
            _background.color = pEvent.IsProposalResponse &&
                                pEvent.Proposal?.Status ==
                                DiplomacyProposalStatus.Accepted
                ? new Color(.20f, .34f, .22f, .98f)
                : pEvent.IsProposalResponse &&
                  (pEvent.Proposal?.Status == DiplomacyProposalStatus.Rejected
                   || pEvent.Proposal?.Status == DiplomacyProposalStatus.Expired)
                    ? new Color(.28f, .17f, .15f, .96f)
                    : side == DiplomacyBubbleSide.Right
                ? new Color(.22f, .30f, .22f, .96f)
                : side == DiplomacyBubbleSide.Left
                    ? new Color(.18f, .17f, .145f, .96f)
                    : new Color(.13f, .13f, .12f, .90f);
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
            var bubble = new GameObject("Bubble", typeof(RectTransform),
                typeof(Image));
            bubble.transform.SetParent(transform, false);
            _bubble = bubble.GetComponent<RectTransform>();
            _background = bubble.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(_background, .96f);
            _background.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(bubble.transform, false);
            _text = textObject.GetComponent<Text>();
            _text.font = LocalizedTextManager.current_font;
            _text.fontSize = 9;
            _text.alignment = TextAnchor.UpperLeft;
            _text.color = Color.white;
            _text.supportRichText = true;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            _text.raycastTarget = false;

            _rejectButton = CreateButton(_bubble, "Reject", out _rejectText);
            _acceptButton = CreateButton(_bubble, "Accept", out _acceptText);
            _rejectText.text = AW_L10n.Text("aw_diplomacy_reject", "Reject");
            _acceptText.text = AW_L10n.Text("aw_diplomacy_accept", "Accept");
            _rejectButton.gameObject.SetActive(false);
            _acceptButton.gameObject.SetActive(false);

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

        private void BindFlag(DiplomacyConversationEvent pEvent,
            DiplomacyBubbleSide pSide, float pRowWidth)
        {
            if (pSide == DiplomacyBubbleSide.Center ||
                pEvent.SpeakerKingdomId < 0)
            {
                _flagRoot.gameObject.SetActive(false);
                return;
            }
            Kingdom speaker = FindKingdom(pEvent.SpeakerKingdomId);
            if (speaker?.data == null)
            {
                _flagRoot.gameObject.SetActive(false);
                return;
            }

            string bannerId = "";
            try { bannerId = speaker.getActorAsset()?.banner_id ?? ""; }
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

            float x = pSide == DiplomacyBubbleSide.Right
                ? Mathf.Max(0f, pRowWidth - 27f)
                : 3f;
            Layout(_flagRoot, x, 6f, 24f, 24f);
            _flagTip.enabled = true;
            _flagTip.hoverAction = () => Tooltip.show(_flagTip.gameObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = speaker.name ?? pEvent.SpeakerName ?? "",
                    tip_description = pEvent.SpeakerTitle ?? ""
                });
            _flagRoot.gameObject.SetActive(true);
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Button CreateButton(Transform pParent, string pName,
            out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .95f);
            var textObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(obj.transform, false);
            pText = textObject.GetComponent<Text>();
            pText.font = LocalizedTextManager.current_font;
            pText.fontSize = 8;
            pText.alignment = TextAnchor.MiddleCenter;
            pText.color = Color.white;
            pText.raycastTarget = false;
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(2f, 1f);
            pText.rectTransform.offsetMax = new Vector2(-2f, -1f);
            return obj.GetComponent<Button>();
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
