using AncientWarfare3.ui.items;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class FeudatoryPortraitPanel : MonoBehaviour
    {
        private const float PortraitSize = 72f;
        private Image _archivePortrait;
        private UiUnitAvatarElement _avatar;
        private Text _name;
        private Text _shi;
        private Button _button;
        private long _actorId = -1L;

        public static FeudatoryPortraitPanel Create(Transform pParent)
        {
            var obj = new GameObject("FeudatoryPortraitPanel",
                typeof(RectTransform), typeof(Button));
            obj.transform.SetParent(pParent, false);
            var panel = obj.AddComponent<FeudatoryPortraitPanel>();
            panel.Build();
            return panel;
        }

        public void Bind(long pActorId, string pName, string pShiLabel)
        {
            _actorId = pActorId;
            _name.text = string.IsNullOrEmpty(pName) ? "#" + pActorId : pName;
            _shi.text = string.IsNullOrEmpty(pShiLabel)
                ? AW_L10n.Text("aw_feudatory_shi_unknown", "Shi unknown")
                : pShiLabel;
            Actor actor = FindActor(pActorId);
            bool live = actor?.data != null && actor.isAlive() && !actor.isRekt();
            _archivePortrait.enabled = !live;
            if (live)
            {
                EnsureAvatar();
                if (_avatar != null)
                {
                    _avatar.gameObject.SetActive(true);
                    _avatar.enabled = true;
                    if (_avatar.avatarLoader != null)
                        _avatar.avatarLoader.enabled = true;
                    _avatar.show(actor);
                }
            }
            else
            {
                if (_avatar != null) _avatar.gameObject.SetActive(false);
                _archivePortrait.sprite = pActorId >= 0
                    ? FamilyTreeNodeView.BuildArchivedPortrait(pActorId)
                    : null;
                _archivePortrait.sprite ??=
                    SpriteTextureLoader.getSprite("ui/Icons/iconKings");
                _archivePortrait.color = new Color(.62f, .62f, .62f, 1f);
            }
            gameObject.SetActive(true);
        }

        public void Unbind()
        {
            _actorId = -1L;
            if (_avatar != null) _avatar.gameObject.SetActive(false);
            _archivePortrait.enabled = false;
            gameObject.SetActive(false);
        }

        private void Build()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OpenActor);
            var portrait = new GameObject("Portrait", typeof(RectTransform),
                typeof(Image));
            portrait.transform.SetParent(transform, false);
            RectTransform portraitRect = portrait.GetComponent<RectTransform>();
            Layout(portraitRect, 0f, 0f, PortraitSize, PortraitSize);
            _archivePortrait = portrait.GetComponent<Image>();
            _archivePortrait.preserveAspect = true;
            _archivePortrait.raycastTarget = false;
            _name = CreateText("Name", 12, TextAnchor.UpperLeft);
            LayoutStretchWidth(_name.rectTransform, 82f, 8f, 0f, 24f);
            _shi = CreateText("Shi", 9, TextAnchor.UpperLeft);
            _shi.color = new Color(.78f, .72f, .60f, 1f);
            LayoutStretchWidth(_shi.rectTransform, 82f, 37f, 0f, 28f);
        }

        private void EnsureAvatar()
        {
            if (_avatar != null) return;
            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab == null) return;
            _avatar = Instantiate(prefab, _archivePortrait.transform);
            RectTransform rect = _avatar.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            rect.localScale = Vector3.one;
        }

        private void OpenActor()
        {
            Actor actor = FindActor(_actorId);
            if (actor?.data != null && actor.isAlive() && !actor.isRekt())
                SchoolActorNavigation.Open(actor);
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try
            {
                ActorManager units = World.world?.units;
                return units?.get(pActorId);
            }
            catch { return null; }
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

        private static void LayoutStretchWidth(RectTransform pRect, float pX,
            float pY, float pRight, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(1f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(-pX - pRight, pHeight);
        }
    }
}
