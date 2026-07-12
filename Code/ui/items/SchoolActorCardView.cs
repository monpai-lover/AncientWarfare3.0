using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolActorCardView : MonoBehaviour
    {
        public const float Height = 74f;
        private const float PortraitSize = 48f;

        private Image _background;
        private Button _button;
        private TipButton _tip;
        private GameObject _avatarHolder;
        private UiUnitAvatarElement _avatar;
        private Text _name;
        private Text _standing;
        private Text _detail;

        public static SchoolActorCardView Create(Transform pParent)
        {
            var obj = new GameObject("SchoolActorCard", typeof(RectTransform), typeof(Image),
                typeof(Outline), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(136f, Height);
            var view = obj.AddComponent<SchoolActorCardView>();
            view.Build();
            return view;
        }

        public void Bind(Actor pActor, string pStanding, string pDetail)
        {
            bool live = pActor?.data != null && pActor.isAlive() && !pActor.isRekt();
            gameObject.SetActive(live);
            if (!live) return;

            gameObject.name = "SchoolActor_" + pActor.data.id;
            Kingdom displayKingdom = HistoricalAffiliationService.ServiceKingdom(pActor) ??
                                     pActor.kingdom;
            Color kingdomColor = KingdomColor(displayKingdom);
            _background.color = Color.Lerp(kingdomColor, Color.black, .64f);
            Outline outline = GetComponent<Outline>();
            outline.effectColor = Color.Lerp(kingdomColor, Color.black, .78f);
            outline.effectDistance = new Vector2(1f, -1f);

            _avatarHolder.SetActive(_avatar != null);
            if (_avatar != null)
            {
                _avatar.enabled = true;
                if (_avatar.avatarLoader != null) _avatar.avatarLoader.enabled = true;
                _avatar.show(pActor);
            }

            _name.text = SafeName(pActor);
            _name.color = kingdomColor;
            _standing.text = pStanding ?? "";
            _detail.text = pDetail ?? "";

            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => ActionLibrary.openUnitWindow(pActor));
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            _tip.hoverAction = () => Tooltip.show(gameObject, AW_RawTooltip.TYPE, new TooltipData
            {
                tip_name = SafeName(pActor),
                tip_description = (pStanding ?? "") + "\n" + (pDetail ?? "")
            });
        }

        private void Build()
        {
            _background = GetComponent<Image>();
            AW_UIStyle.ApplyButton(_background, .96f);
            _button = GetComponent<Button>();
            _tip = GetComponent<TipButton>();

            _avatarHolder = new GameObject("PortraitSlot", typeof(RectTransform));
            _avatarHolder.transform.SetParent(transform, false);
            RectTransform portraitRect = _avatarHolder.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(6f, -6f);
            portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);

            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab != null)
            {
                _avatar = Instantiate(prefab, _avatarHolder.transform);
                RectTransform avatarRect = _avatar.GetComponent<RectTransform>();
                if (avatarRect != null)
                {
                    avatarRect.anchorMin = new Vector2(.5f, .5f);
                    avatarRect.anchorMax = new Vector2(.5f, .5f);
                    avatarRect.pivot = new Vector2(.5f, .5f);
                    avatarRect.anchoredPosition = Vector2.zero;
                    avatarRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
                    avatarRect.localScale = Vector3.one;
                }
            }

            _name = Text("Name", new Vector2(59f, -7f), new Vector2(70f, 17f), 9,
                TextAnchor.UpperLeft);
            _standing = Text("Standing", new Vector2(59f, -26f), new Vector2(70f, 16f), 8,
                TextAnchor.UpperLeft);
            _standing.color = new Color(.96f, .80f, .42f, 1f);
            _detail = Text("Detail", new Vector2(59f, -44f), new Vector2(70f, 25f), 7,
                TextAnchor.UpperLeft);
            _detail.color = new Color(.84f, .82f, .76f, 1f);
        }

        private Text Text(string pName, Vector2 pPosition, Vector2 pSize, int pFontSize,
            TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(transform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pPosition;
            rect.sizeDelta = pSize;
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAnchor;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pFontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            try
            {
                ColorAsset color = pKingdom?.getColor();
                if (color != null) return color.getColorMainSecond();
            }
            catch { }
            return new Color(.90f, .84f, .68f, 1f);
        }

        private static string SafeName(Actor pActor)
        {
            try { return pActor?.getName() ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }
    }
}
