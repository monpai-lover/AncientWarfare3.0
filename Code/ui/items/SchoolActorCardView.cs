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
        private long _boundActorId = -1L;
        private bool _portraitAttemptedForBind;

        public bool HasPortrait => _avatar != null;

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
            if (!live)
            {
                ClearInteractions();
                ReleaseActorBinding();
                gameObject.SetActive(false);
                return;
            }

            long actorId = pActor.data.id;
            bool reusePortrait = _boundActorId == actorId;
            ClearInteractions();
            if (!reusePortrait)
            {
                ReleaseActorBinding();
                _boundActorId = actorId;
            }
            gameObject.SetActive(true);
            gameObject.name = "SchoolActor_" + actorId;
            Kingdom displayKingdom = HistoricalAffiliationService.ServiceKingdom(pActor) ??
                                     pActor.kingdom;
            Color kingdomColor = KingdomColor(displayKingdom);
            _background.color = Color.Lerp(kingdomColor, Color.black, .64f);
            Outline outline = GetComponent<Outline>();
            outline.effectColor = Color.Lerp(kingdomColor, Color.black, .78f);
            outline.effectDistance = new Vector2(1f, -1f);

            EnsurePortrait(pActor);

            string actorName = SafeName(pActor);
            string standing = pStanding ?? "";
            string detail = pDetail ?? "";
            _name.text = actorName;
            _name.color = kingdomColor;
            _standing.text = standing;
            _detail.text = detail;

            _button.onClick.AddListener(() => OpenActor(actorId));
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            _tip.clickAction = null;
            string tooltipDescription = standing + "\n" + detail;
            _tip.hoverAction = () => Tooltip.show(gameObject, AW_RawTooltip.TYPE, new TooltipData
            {
                tip_name = actorName,
                tip_description = tooltipDescription
            });
        }

        public void Unbind()
        {
            ClearInteractions();
            ReleaseActorBinding();
            gameObject.SetActive(false);
        }

        public bool TryEnsurePortrait()
        {
            Actor actor = FindActor(_boundActorId);
            return actor?.data != null && actor.isAlive() && !actor.isRekt() &&
                   EnsurePortrait(actor);
        }

        private void ClearInteractions()
        {
            _button?.onClick.RemoveAllListeners();
            if (_tip == null) return;
            _tip.hoverAction = null;
            _tip.clickAction = null;
            _tip.enabled = false;
        }

        private void ReleaseActorBinding()
        {
            _boundActorId = -1L;
            _portraitAttemptedForBind = false;
            ReleasePortrait();
        }

        private void Build()
        {
            _background = GetComponent<Image>();
            AW_UIStyle.ApplyButton(_background, .96f);
            _button = GetComponent<Button>();
            _tip = GetComponent<TipButton>();
            _tip.showOnClick = false;

            _avatarHolder = new GameObject("PortraitSlot", typeof(RectTransform));
            _avatarHolder.transform.SetParent(transform, false);
            RectTransform portraitRect = _avatarHolder.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(6f, -6f);
            portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _avatarHolder.SetActive(false);

            _name = Text("Name", new Vector2(59f, -7f), new Vector2(70f, 17f), 9,
                TextAnchor.UpperLeft);
            _standing = Text("Standing", new Vector2(59f, -26f), new Vector2(70f, 16f), 8,
                TextAnchor.UpperLeft);
            _standing.color = new Color(.96f, .80f, .42f, 1f);
            _detail = Text("Detail", new Vector2(59f, -44f), new Vector2(70f, 25f), 7,
                TextAnchor.UpperLeft);
            _detail.color = new Color(.84f, .82f, .76f, 1f);
        }

        private bool EnsurePortrait(Actor pActor)
        {
            if (_avatar != null)
            {
                ShowPortrait(pActor);
                return true;
            }
            if (_portraitAttemptedForBind) return false;
            _portraitAttemptedForBind = true;
            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab == null)
            {
                _portraitAttemptedForBind = false;
                return false;
            }

            _avatar = Instantiate(prefab, _avatarHolder.transform);
            DisablePortraitInteraction(_avatar);
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
            _portraitAttemptedForBind = false;
            ShowPortrait(pActor);
            return true;
        }

        private void ShowPortrait(Actor pActor)
        {
            _avatarHolder.SetActive(true);
            _avatar.gameObject.SetActive(true);
            _avatar.enabled = true;
            if (_avatar.avatarLoader != null) _avatar.avatarLoader.enabled = true;
            _avatar.show(pActor);
        }

        private void ReleasePortrait()
        {
            if (_avatar != null)
            {
                _avatar.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_avatar.gameObject);
                _avatar = null;
            }
            _avatarHolder?.SetActive(false);
        }

        private static void DisablePortraitInteraction(UiUnitAvatarElement pAvatar)
        {
            if (pAvatar == null) return;
            CanvasGroup inputBlocker = pAvatar.GetComponent<CanvasGroup>() ??
                                       pAvatar.gameObject.AddComponent<CanvasGroup>();
            inputBlocker.blocksRaycasts = false;
            inputBlocker.interactable = false;
            foreach (Button button in pAvatar.GetComponentsInChildren<Button>(true))
            {
                button.onClick.RemoveAllListeners();
                button.interactable = false;
            }
            foreach (Graphic graphic in pAvatar.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
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

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static void OpenActor(long pActorId)
        {
            Actor resolved = FindActor(pActorId);
            if (resolved?.data == null || !resolved.isAlive() || resolved.isRekt()) return;
            SchoolActorNavigation.Open(resolved);
        }
    }
}
