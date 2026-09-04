using System;
using System.Collections.Generic;
using AncientWarfare3.content.figures;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class HistoricalFigureCardListItem
    {
        public const float DesktopWidth = 82f;
        public const float DesktopHeight = 92f;
        public const float MobileWidth = 82f;
        public const float MobileHeight = 92f;
        public const float Width = DesktopWidth;
        public const float Height = DesktopHeight;
        private const string MysteryPortraitPath =
            "ui/historical_cards/rare_special";

        private readonly GameObject _root;
        private readonly Image _background;
        private readonly Image _rarityBar;
        private readonly Image _portrait;
        private readonly Text _name;
        private readonly Text _kingdom;
        private float _width;
        private float _height;
        private static readonly Dictionary<string, Sprite> GradientSprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private HistoricalFigureCardListItem(GameObject pRoot,
            Image pBackground, Image pRarityBar, Image pPortrait,
            Text pName, Text pKingdom, float pWidth, float pHeight)
        {
            _root = pRoot;
            _background = pBackground;
            _rarityBar = pRarityBar;
            _portrait = pPortrait;
            _name = pName;
            _kingdom = pKingdom;
            _width = pWidth;
            _height = pHeight;
        }

        public static HistoricalFigureCardListItem Create(Transform pParent,
            float pWidth = Width, float pHeight = Height)
        {
            GameObject root = new GameObject("HistoricalFigureCard",
                typeof(RectTransform), typeof(Image));
            root.transform.SetParent(pParent, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, .5f);
            rootRect.pivot = new Vector2(.5f, .5f);
            rootRect.sizeDelta = new Vector2(pWidth, pHeight);
            Image background = root.GetComponent<Image>();
            background.color = new Color(.08f, .09f, .11f, .98f);

            Image rarity = ChildImage("Rarity", root.transform,
                new Color(.3f, .42f, 1f, 1f));
            Position(rarity.rectTransform, 0f, 0f, pWidth, 4f,
                new Vector2(0f, 0f));

            Image portrait = ChildImage("Portrait", root.transform,
                new Color(.2f, .2f, .2f, 1f));
            Position(portrait.rectTransform, 10f, -8f, pWidth - 20f,
                pHeight - 38f,
                new Vector2(0f, 1f));
            portrait.preserveAspect = true;
            portrait.sprite = SpriteTextureLoader.getSprite("ui/icons/iconKings")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
            Text name = MakeText("Name", root.transform, 7,
                TextAnchor.MiddleCenter);
            Position(name.rectTransform, 4f, -(pHeight - 28f),
                pWidth - 8f, 14f,
                new Vector2(0f, 1f));
            Text kingdom = MakeText("Kingdom", root.transform, 6,
                TextAnchor.MiddleCenter);
            kingdom.color = new Color(.75f, .76f, .78f, 1f);
            Position(kingdom.rectTransform, 4f, 4f, pWidth - 8f, 12f,
                new Vector2(0f, 0f));
            return new HistoricalFigureCardListItem(root, background, rarity,
                portrait, name, kingdom, pWidth, pHeight);
        }

        public static float WidthForViewport(float pViewportWidth)
        {
            return Width;
        }

        public static float HeightForViewport(float pViewportWidth)
        {
            return Height;
        }

        public void SetCard(HistoricalFigureCardDefinition pCard,
            bool pWinner = false, string pConcealedName = "")
        {
            if (pCard == null)
            {
                _root.SetActive(false);
                return;
            }
            _root.SetActive(true);
            Color rarityColor = ParseColor(pCard.Rarity?.ColorHex,
                new Color(.3f, .42f, 1f, 1f));
            bool concealIdentity = !string.IsNullOrEmpty(pConcealedName) &&
                pCard.Rarity != null &&
                pCard.Rarity.Equals(HistoricalFigureCardRarity.Gold);
            _rarityBar.color = rarityColor;
            _background.sprite = GradientSprite(rarityColor);
            _background.color = Color.white;
            _name.text = concealIdentity ? pConcealedName :
                (pCard.DisplayName ?? "-");
            _kingdom.text = concealIdentity ? "" :
                (pCard.HistoricalKingdomName ?? "");
            Sprite portrait = concealIdentity ||
                string.IsNullOrEmpty(pCard.PortraitPath)
                ? null : SpriteTextureLoader.getSprite(pCard.PortraitPath);
            _portrait.sprite = portrait ?? (concealIdentity
                ? SpriteTextureLoader.getSprite(MysteryPortraitPath)
                : null) ??
                SpriteTextureLoader.getSprite("ui/icons/iconKings") ??
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
        }

        public void SetSize(float pWidth, float pHeight)
        {
            _width = Mathf.Max(1f, pWidth);
            _height = Mathf.Max(1f, pHeight);
            RectTransform rect = _root.transform as RectTransform;
            rect.sizeDelta = new Vector2(_width, _height);
            Position(_rarityBar.rectTransform, 0f, 0f, _width, 4f,
                new Vector2(0f, 0f));
            Position(_portrait.rectTransform, 10f, -8f, _width - 20f,
                _height - 38f,
                new Vector2(0f, 1f));
            Position(_name.rectTransform, 4f, -(_height - 28f),
                _width - 8f, 14f,
                new Vector2(0f, 1f));
            Position(_kingdom.rectTransform, 4f, 4f, _width - 8f, 12f,
                new Vector2(0f, 0f));
        }

        public void SetPosition(float pX)
        {
            RectTransform rect = _root.transform as RectTransform;
            rect.anchoredPosition = new Vector2(pX + _width * .5f, 0f);
        }

        public void SetScale(float pScale)
        {
            float scale = Mathf.Max(.1f, pScale);
            _root.transform.localScale = new Vector3(scale, scale, 1f);
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

        private static Sprite GradientSprite(Color pRarityColor)
        {
            string key = ColorUtility.ToHtmlStringRGBA(pRarityColor);
            if (GradientSprites.TryGetValue(key, out Sprite cached))
                return cached;
            const int height = 16;
            Texture2D texture = new Texture2D(1, height,
                TextureFormat.RGBA32, false)
            {
                name = "HistoricalTrackGradient_" + key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color32[] pixels = new Color32[height];
            Color top = new Color(.36f, .36f, .39f, 1f);
            for (int y = 0; y < height; y++)
                pixels[y] = Color.Lerp(top, pRarityColor,
                    y / (float)(height - 1));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f,
                height), new Vector2(.5f, .5f), 1f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            GradientSprites[key] = sprite;
            return sprite;
        }
    }
}
