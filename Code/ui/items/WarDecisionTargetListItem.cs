using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class WarDecisionTargetRow
    {
        public string text = "";
        public string stats = "";
        public string tooltip_title = "";
        public string tooltip_desc = "";
        public string button_text = "";
        public string icon_path = "";
        public bool is_header;
        public bool dim;
        public bool enabled = true;
        public int sort_order;
        public string sort_name = "";
        public System.Action action;
    }

    internal sealed class WarDecisionTargetListItem : AbstractListWindowItem<WarDecisionTargetRow>
    {
        private const float ROW_W = 220f;
        private const float ROW_H = 34f;

        private Text _label;
        private Text _stats;
        private Image _icon;
        private LayoutElement _layout;
        private TipButton _tip;
        private GameObject _buttonObj;
        private Text _buttonText;

        public override void Setup(WarDecisionTargetRow pObject)
        {
            EnsureUi();

            _label.text = pObject?.text ?? "";
            _stats.text = pObject?.stats ?? "";
            SetTip(gameObject, pObject?.tooltip_title, pObject?.tooltip_desc);

            Image bg = gameObject.GetComponent<Image>();
            if (pObject != null && pObject.is_header)
            {
                AW_UIStyle.ApplyPanel(bg, 0.95f);
                _label.fontStyle = FontStyle.Bold;
                _label.alignment = TextAnchor.MiddleCenter;
                _label.color = new Color(1f, 0.75f, 0.35f, 1f);
                _icon.gameObject.SetActive(false);
                _stats.gameObject.SetActive(false);
                _buttonObj.SetActive(false);
                ApplyHeight(24f);
                return;
            }

            bool enabled = pObject?.enabled ?? false;
            AW_UIStyle.ApplyListRow(bg, pObject != null && pObject.dim ? 0.78f : 0.9f);
            _label.fontStyle = FontStyle.Normal;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.color = enabled ? Color.white : new Color(0.68f, 0.68f, 0.68f, 1f);
            _stats.color = enabled ? new Color(0.88f, 0.88f, 0.88f, 1f) : new Color(0.58f, 0.58f, 0.58f, 1f);
            _stats.gameObject.SetActive(true);
            SetupIcon(pObject, enabled);

            SetupButton(pObject);
            ApplyHeight(ROW_H);
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            RectTransform rect = gameObject.GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ROW_W, ROW_H);

            _layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            _layout.minHeight = ROW_H;
            _layout.preferredHeight = ROW_H;

            Image bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(bg, 0.9f);

            _tip = gameObject.GetComponent<TipButton>() ?? gameObject.AddComponent<TipButton>();

            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(transform, false);
            RectTransform irect = iconObj.GetComponent<RectTransform>();
            irect.anchorMin = new Vector2(0f, 0.5f);
            irect.anchorMax = new Vector2(0f, 0.5f);
            irect.pivot = new Vector2(0f, 0.5f);
            irect.sizeDelta = new Vector2(18f, 18f);
            irect.anchoredPosition = new Vector2(6f, 0f);
            _icon = iconObj.GetComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;

            GameObject labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(transform, false);
            RectTransform lrect = labelObj.GetComponent<RectTransform>();
            lrect.anchorMin = new Vector2(0f, 0.5f);
            lrect.anchorMax = new Vector2(0f, 0.5f);
            lrect.pivot = new Vector2(0f, 0.5f);
            lrect.sizeDelta = new Vector2(132f, 18f);
            lrect.anchoredPosition = new Vector2(28f, 7f);
            _label = labelObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 10;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.supportRichText = true;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;

            GameObject statsObj = new GameObject("Stats", typeof(RectTransform), typeof(Text));
            statsObj.transform.SetParent(transform, false);
            RectTransform srect = statsObj.GetComponent<RectTransform>();
            srect.anchorMin = new Vector2(0f, 0.5f);
            srect.anchorMax = new Vector2(0f, 0.5f);
            srect.pivot = new Vector2(0f, 0.5f);
            srect.sizeDelta = new Vector2(132f, 14f);
            srect.anchoredPosition = new Vector2(28f, -9f);
            _stats = statsObj.GetComponent<Text>();
            _stats.font = LocalizedTextManager.current_font;
            _stats.fontSize = 8;
            _stats.color = new Color(0.88f, 0.88f, 0.88f, 1f);
            _stats.alignment = TextAnchor.MiddleLeft;
            _stats.supportRichText = true;
            _stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            _stats.verticalOverflow = VerticalWrapMode.Overflow;
            _stats.raycastTarget = false;

            _buttonObj = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            _buttonObj.transform.SetParent(transform, false);
            RectTransform brect = _buttonObj.GetComponent<RectTransform>();
            brect.anchorMin = new Vector2(1f, 0.5f);
            brect.anchorMax = new Vector2(1f, 0.5f);
            brect.pivot = new Vector2(1f, 0.5f);
            brect.sizeDelta = new Vector2(48f, 22f);
            brect.anchoredPosition = new Vector2(-6f, 0f);
            AW_UIStyle.ApplyButton(_buttonObj.GetComponent<Image>(), 0.95f);

            GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(_buttonObj.transform, false);
            RectTransform trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.offsetMin = Vector2.zero;
            trect.offsetMax = Vector2.zero;
            _buttonText = textObj.GetComponent<Text>();
            _buttonText.font = LocalizedTextManager.current_font;
            _buttonText.fontSize = 8;
            _buttonText.alignment = TextAnchor.MiddleCenter;
            _buttonText.color = Color.white;
            _buttonText.raycastTarget = false;
            _buttonText.resizeTextForBestFit = true;
            _buttonText.resizeTextMinSize = 6;
            _buttonText.resizeTextMaxSize = 8;
        }

        private void SetupIcon(WarDecisionTargetRow pObject, bool pEnabled)
        {
            if (_icon == null) return;
            Sprite sprite = SpriteTextureLoader.getSprite(pObject?.icon_path ?? "")
                            ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy")
                            ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
            _icon.gameObject.SetActive(sprite != null);
            if (sprite == null) return;
            _icon.sprite = sprite;
            _icon.color = pEnabled ? Color.white : new Color(0.62f, 0.62f, 0.62f, 0.9f);
        }

        private void SetupButton(WarDecisionTargetRow pObject)
        {
            bool hasAction = pObject?.action != null && !string.IsNullOrEmpty(pObject.button_text);
            _buttonObj.SetActive(hasAction);
            if (!hasAction) return;

            bool enabled = pObject.enabled;
            AW_UIStyle.ApplyButton(_buttonObj.GetComponent<Image>(), enabled ? 0.95f : 0.48f);
            _buttonText.text = pObject.button_text;
            _buttonText.color = enabled ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);

            Button button = _buttonObj.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.interactable = enabled;
            if (enabled) button.onClick.AddListener(() => pObject.action?.Invoke());
            SetTip(_buttonObj, pObject.tooltip_title, pObject.tooltip_desc);
        }

        private void ApplyHeight(float pHeight)
        {
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(ROW_W, pHeight);
            if (_layout == null) return;
            _layout.minHeight = pHeight;
            _layout.preferredHeight = pHeight;
        }

        private static void SetTip(GameObject pOwner, string pTitle, string pDesc)
        {
            TipButton tip = pOwner.GetComponent<TipButton>() ?? pOwner.AddComponent<TipButton>();
            string title = pTitle ?? "";
            string desc = pDesc ?? "";
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(desc))
            {
                tip.enabled = false;
                tip.hoverAction = null;
                return;
            }

            tip.enabled = true;
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () =>
                Tooltip.show(pOwner, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }
    }
}
