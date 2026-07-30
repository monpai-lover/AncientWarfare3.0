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
        private GameObject _portraitHolder;
        private Image _archivePortrait;
        private UiUnitAvatarElement _avatar;
        private Text _name;
        private Text _status;
        private Text _detail;
        private Button _button;
        private TipButton _tip;
        private long _boundActorId = -1L;
        private long _boundArchiveActorId = -1L;
        private string _boundArchiveMasterId = "";
        private bool _boundArchiveDead;
        private bool _hasArchiveBinding;
        private bool _portraitAttemptedForBind;

        public bool HasPortrait => _avatar != null;

        public static SchoolMasterCardView Create(Transform pParent)
        {
            var obj = new GameObject("SchoolMasterCard", typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(TipButton));
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
                ClearInteractions();
                ReleaseActorBinding();
                gameObject.SetActive(false);
                return;
            }
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
                           (dead ? AW_L10n.Text("aw_school_master_historical_record",
                                "Historical record") :
                            pRecord?.Spawned == true
                                ? AW_L10n.Text("aw_school_master_active_residence",
                                    "Residence active")
                                : AW_L10n.Text("aw_school_master_awaiting_xia_descent",
                                    "Awaiting descent"));

            long actorId = live ? pActor.data.id : -1L;
            long archiveActorId = live ? -1L : pRecord?.ActorId ?? -1L;
            string archiveMasterId = live ? "" : pRecord?.MasterId ?? pDefinition.Id;
            bool reusePortrait = live && _boundActorId == actorId;
            bool reuseArchivePortrait = !live && _hasArchiveBinding &&
                                        _boundArchiveActorId == archiveActorId &&
                                        _boundArchiveDead == dead &&
                                        string.Equals(_boundArchiveMasterId, archiveMasterId,
                                            StringComparison.Ordinal) &&
                                        _archivePortrait.sprite != null;
            ClearInteractions();
            if (!reusePortrait && !reuseArchivePortrait)
            {
                ReleaseActorBinding();
                if (live)
                    _boundActorId = actorId;
                else
                {
                    _boundArchiveActorId = archiveActorId;
                    _boundArchiveMasterId = archiveMasterId;
                    _boundArchiveDead = dead;
                    _hasArchiveBinding = true;
                }
            }
            gameObject.SetActive(true);
            _portraitHolder.SetActive(true);
            _archivePortrait.enabled = !live;
            if (live)
            {
                EnsurePortrait(pActor);
            }
            else
            {
                if (!reuseArchivePortrait)
                {
                    Sprite icon = archiveActorId >= 0
                        ? FamilyTreeNodeView.BuildArchivedPortrait(archiveActorId)
                        : null;
                    icon ??= SpriteTextureLoader.getSprite(
                        CourtSchoolRegistry.Find(pDefinition.SchoolId)?.IconPath ?? "") ??
                        SpriteTextureLoader.getSprite("ui/Icons/iconKnowledge");
                    _archivePortrait.sprite = icon;
                }
                _archivePortrait.color = dead ? new Color(.55f, .55f, .55f, 1f) : schoolColor;
            }

            if (live)
            {
                _button.onClick.AddListener(() => OpenActor(actorId));
            }
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            _tip.clickAction = null;
            string title = _name.text;
            string description = _status.text + "\n" + _detail.text;
            _tip.hoverAction = () => Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                new TooltipData { tip_name = title, tip_description = description });
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
            _boundArchiveActorId = -1L;
            _boundArchiveMasterId = "";
            _boundArchiveDead = false;
            _hasArchiveBinding = false;
            _archivePortrait.sprite = null;
            _portraitAttemptedForBind = false;
            ReleasePortrait();
        }

        private void Build()
        {
            _background = GetComponent<Image>();
            _button = GetComponent<Button>();
            _tip = GetComponent<TipButton>();
            _tip.showOnClick = false;
            _portraitHolder = new GameObject("PortraitSlot", typeof(RectTransform), typeof(Image));
            _portraitHolder.transform.SetParent(transform, false);
            RectTransform portraitRect = _portraitHolder.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(6f, -6f);
            portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _archivePortrait = _portraitHolder.GetComponent<Image>();
            _archivePortrait.preserveAspect = true;
            _archivePortrait.raycastTarget = false;
            _portraitHolder.SetActive(false);

            _name = Text("Name", new Vector2(62f, -7f), new Vector2(108f, 17f), 10);
            _status = Text("Status", new Vector2(62f, -27f), new Vector2(108f, 15f), 8);
            _detail = Text("Detail", new Vector2(62f, -45f), new Vector2(108f, 26f), 7);
            _detail.color = new Color(.82f, .80f, .74f, 1f);
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

            _avatar = Instantiate(prefab, _portraitHolder.transform);
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
            _portraitHolder.SetActive(true);
            _archivePortrait.enabled = false;
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
            _portraitHolder?.SetActive(false);
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
