using System;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolMasterCardView : MonoBehaviour
    {
        public const float Height = 82f;
        private const float PortraitSize = 50f;
        private Image _background;
        private Image _archivePortrait;
        private UiUnitAvatarElement _avatar;
        private Text _name;
        private Text _status;
        private Text _detail;
        private Button _button;

        public static SchoolMasterCardView Create(Transform pParent)
        {
            var obj = new GameObject("SchoolMasterCard", typeof(RectTransform), typeof(Image),
                typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(176f, Height);
            var view = obj.AddComponent<SchoolMasterCardView>();
            view.Build();
            return view;
        }

        public void Bind(HistoricalSchoolMasterDefinition pDefinition,
            HistoricalSchoolMasterStoreRecord pRecord, Actor pActor)
        {
            if (pDefinition == null)
            {
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);
            Color schoolColor = Parse(CourtSchoolRegistry.Find(pDefinition.SchoolId)?.ColorHex,
                new Color(.35f, .32f, .25f, 1f));
            _background.color = Color.Lerp(schoolColor, Color.black, .72f);
            _name.text = pDefinition.CanonicalName;
            _name.color = schoolColor;

            bool live = pActor?.data != null && pActor.isAlive() && !pActor.isRekt();
            bool dead = pRecord?.Dead == true || (pRecord?.Spawned == true && !live);
            _status.text = dead
                ? AW_L10n.Text("aw_school_master_dead", "Dead")
                : pRecord?.Spawned == true
                    ? AW_L10n.Text("aw_school_master_living", "Living")
                    : AW_L10n.Text("aw_school_master_queued", "Queued");
            _status.color = dead ? new Color(.65f, .65f, .65f, 1f) :
                new Color(.92f, .82f, .55f, 1f);
            _detail.text = "#" + pDefinition.Order + "  " +
                           (dead ? AW_L10n.Text("aw_school_recent_history", "historical record") :
                            pRecord?.Spawned == true ? "active residence" :
                            "awaiting Xia descent");

            _avatar?.gameObject.SetActive(live);
            _archivePortrait.gameObject.SetActive(!live);
            if (live && _avatar != null)
            {
                _avatar.enabled = true;
                if (_avatar.avatarLoader != null) _avatar.avatarLoader.enabled = true;
                _avatar.show(pActor);
            }
            if (!live)
            {
                Sprite icon = pRecord?.ActorId >= 0
                    ? FamilyTreeNodeView.BuildArchivedPortrait(pRecord.ActorId)
                    : null;
                icon ??= SpriteTextureLoader.getSprite(
                    CourtSchoolRegistry.Find(pDefinition.SchoolId)?.IconPath ?? "") ??
                    SpriteTextureLoader.getSprite("ui/Icons/iconKnowledge");
                _archivePortrait.sprite = icon;
                _archivePortrait.color = dead ? new Color(.55f, .55f, .55f, 1f) : schoolColor;
            }

            _button.onClick.RemoveAllListeners();
            if (live) _button.onClick.AddListener(() => ActionLibrary.openUnitWindow(pActor));
        }

        private void Build()
        {
            _background = GetComponent<Image>();
            _button = GetComponent<Button>();
            var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portrait.transform.SetParent(transform, false);
            RectTransform portraitRect = portrait.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(6f, -6f);
            portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _archivePortrait = portrait.GetComponent<Image>();
            _archivePortrait.preserveAspect = true;
            _archivePortrait.raycastTarget = false;

            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab != null)
            {
                _avatar = Instantiate(prefab, portrait.transform);
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
                _avatar.gameObject.SetActive(false);
            }

            _name = Text("Name", new Vector2(62f, -7f), new Vector2(108f, 17f), 10);
            _status = Text("Status", new Vector2(62f, -27f), new Vector2(108f, 15f), 8);
            _detail = Text("Detail", new Vector2(62f, -45f), new Vector2(108f, 26f), 7);
            _detail.color = new Color(.82f, .80f, .74f, 1f);
        }

        private Text Text(string pName, Vector2 pPosition, Vector2 pSize, int pFontSize)
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
            text.alignment = TextAnchor.UpperLeft;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pFontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Color Parse(string pHex, Color pFallback)
        {
            return ColorUtility.TryParseHtmlString(pHex, out Color color) ? color : pFallback;
        }
    }
}
