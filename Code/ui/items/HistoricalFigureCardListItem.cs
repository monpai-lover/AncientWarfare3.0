using AncientWarfare3.content.figures;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class HistoricalFigureCardListItem
    {
        public const float Width = 82f;
        public const float Height = 92f;

        private readonly GameObject _root;
        private readonly Image _background;
        private readonly Image _rarityBar;
        private readonly Image _portrait;
        private readonly Text _name;
        private readonly Text _kingdom;

        private HistoricalFigureCardListItem(GameObject pRoot,
            Image pBackground, Image pRarityBar, Image pPortrait,
            Text pName, Text pKingdom)
        {
            _root = pRoot;
            _background = pBackground;
            _rarityBar = pRarityBar;
            _portrait = pPortrait;
            _name = pName;
            _kingdom = pKingdom;
        }

        public static HistoricalFigureCardListItem Create(Transform pParent)
        {
            GameObject root = new GameObject("HistoricalFigureCard",
                typeof(RectTransform), typeof(Image));
            root.transform.SetParent(pParent, false);
            Image background = root.GetComponent<Image>();
            background.color = new Color(.08f, .09f, .11f, .98f);

            Image rarity = ChildImage("Rarity", root.transform,
                new Color(.3f, .42f, 1f, 1f));
            Position(rarity.rectTransform, 0f, 0f, Width, 4f,
                new Vector2(0f, 0f));

            Image portrait = ChildImage("Portrait", root.transform,
                new Color(.2f, .2f, .2f, 1f));
            Position(portrait.rectTransform, 6f, -8f, 28f, 28f,
                new Vector2(0f, 1f));
            portrait.sprite = SpriteTextureLoader.getSprite("ui/icons/iconKings")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");

            Text name = MakeText("Name", root.transform, 7,
                TextAnchor.UpperLeft);
            Position(name.rectTransform, 38f, -8f, 40f, 30f,
                new Vector2(0f, 1f));
            Text kingdom = MakeText("Kingdom", root.transform, 6,
                TextAnchor.LowerLeft);
            kingdom.color = new Color(.75f, .76f, .78f, 1f);
            Position(kingdom.rectTransform, 6f, 7f, 70f, 25f,
                new Vector2(0f, 0f));
            return new HistoricalFigureCardListItem(root, background, rarity,
                portrait, name, kingdom);
        }

        public void SetCard(HistoricalFigureCardDefinition pCard,
            bool pWinner = false)
        {
            if (pCard == null)
            {
                _root.SetActive(false);
                return;
            }
            _root.SetActive(true);
            Color rarityColor = ParseColor(pCard.Rarity?.ColorHex,
                new Color(.3f, .42f, 1f, 1f));
            _rarityBar.color = rarityColor;
            _background.color = pWinner
                ? new Color(rarityColor.r * .28f, rarityColor.g * .28f,
                    rarityColor.b * .28f, 1f)
                : new Color(.08f, .09f, .11f, .98f);
            _name.text = pCard.DisplayName ?? "-";
            _kingdom.text = pCard.HistoricalKingdomName ?? "";
            if (!string.IsNullOrEmpty(pCard.PortraitPath))
            {
                Sprite portrait = SpriteTextureLoader.getSprite(
                    pCard.PortraitPath);
                if (portrait != null) _portrait.sprite = portrait;
            }
        }

        public void SetPosition(float pX)
        {
            RectTransform rect = _root.transform as RectTransform;
            rect.anchoredPosition = new Vector2(pX, 0f);
        }

        public void SetVisible(bool pVisible) => _root.SetActive(pVisible);

        private static Image ChildImage(string pName, Transform pParent,
            Color pColor)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.color = pColor;
            return image;
        }

        private static Text MakeText(string pName, Transform pParent,
            int pSize, TextAnchor pAnchor)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
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

        private static void Position(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight, Vector2 pAnchor)
        {
            pRect.anchorMin = pAnchor;
            pRect.anchorMax = pAnchor;
            pRect.pivot = pAnchor;
            pRect.anchoredPosition = new Vector2(pX, pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static Color ParseColor(string pHex, Color pFallback)
        {
            return !string.IsNullOrEmpty(pHex) &&
                   ColorUtility.TryParseHtmlString(pHex, out Color color)
                ? color : pFallback;
        }
    }
}
