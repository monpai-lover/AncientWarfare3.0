using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui.windows;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class SchoolRosterNodeView : MonoBehaviour
    {
        public const float Width = 132f;
        public const float Height = 108f;
        private const float PortraitSize = 50f;

        private Image _background;
        private Button _button;
        private TipButton _tip;
        private GameObject _portraitHolder;
        private UiUnitAvatarElement _avatar;
        private Image _schoolIcon;
        private Button _schoolButton;
        private Text _name;
        private Text _standing;
        private Text _detail;
        private long _boundActorId = -1L;
        private bool _portraitVisible;
        private bool _portraitAttemptedForBind;

        public bool HasPortrait => _avatar != null;
        public bool CanAttemptPortrait => _boundActorId >= 0 && _avatar == null &&
                                          !_portraitAttemptedForBind;

        public static SchoolRosterNodeView Create(Transform pParent)
        {
            var obj = new GameObject("SchoolRosterNode", typeof(RectTransform), typeof(Image),
                typeof(Outline), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(Width, Height);
            SchoolRosterNodeView view = obj.AddComponent<SchoolRosterNodeView>();
            view.BuildUi();
            return view;
        }

        public bool Bind(SchoolRosterReadNode pNode, Sprite pSchoolIcon)
        {
            Unbind();
            Actor actor = pNode?.Actor;
            bool live = actor?.data != null && actor.isAlive() && !actor.isRekt();
            if (!live) return false;

            _boundActorId = actor.data.id;
            gameObject.name = "SchoolRosterActor_" + actor.data.id;
            Color kingdomColor = KingdomColor(pNode.DisplayKingdom);
            _background.color = Color.Lerp(kingdomColor, Color.black, .62f);
            Outline outline = GetComponent<Outline>();
            outline.effectColor = Color.Lerp(kingdomColor, Color.black, .76f);
            outline.effectDistance = new Vector2(2f, -2f);

            _portraitHolder.SetActive(false);

            _schoolIcon.sprite = pSchoolIcon;
            _schoolIcon.enabled = pSchoolIcon != null;
            _schoolButton.interactable = pSchoolIcon != null;
            _schoolButton.onClick.RemoveAllListeners();
            if (!string.IsNullOrEmpty(pNode.Layout.SchoolId))
            {
                string schoolId = pNode.Layout.SchoolId;
                _schoolButton.onClick.AddListener(() => SchoolWindow.OpenSchool(schoolId));
            }

            _name.text = pNode.ActorName;
            _name.color = kingdomColor;
            _standing.text = StandingLabel(pNode);
            _detail.text = AW_L10n.Text("aw_school_roster_generation", "Generation") + " " +
                           pNode.Layout.Generation + "  " +
                           AW_L10n.Text("aw_school_roster_reputation", "Reputation") + " " +
                           Mathf.RoundToInt(pNode.Layout.Reputation);

            _button.onClick.RemoveAllListeners();
            long actorId = _boundActorId;
            _button.onClick.AddListener(() => OpenActor(actorId));
            BindTooltip(pNode);
            return true;
        }

        public bool SetPortraitVisible(bool pVisible)
        {
            if (_boundActorId < 0) return false;
            if (!pVisible)
            {
                ReleasePortrait();
                return true;
            }
            Actor actor = FindActor(_boundActorId);
            if (actor?.data == null || !actor.isAlive() || actor.isRekt())
            {
                Unbind();
                return false;
            }
            if (_portraitVisible && _avatar != null && _avatar.enabled) return true;
            EnsurePortrait(actor);
            return true;
        }

        public void Unbind()
        {
            _button?.onClick.RemoveAllListeners();
            _schoolButton?.onClick.RemoveAllListeners();
            if (_schoolButton != null) _schoolButton.interactable = false;
            if (_tip != null)
            {
                _tip.hoverAction = null;
                _tip.clickAction = null;
                _tip.enabled = false;
            }
            _boundActorId = -1L;
            _portraitVisible = false;
            _portraitAttemptedForBind = false;
            ReleasePortrait();
            gameObject.SetActive(false);
        }

        private void BuildUi()
        {
            _background = GetComponent<Image>();
            AW_UIStyle.ApplyButton(_background, .96f);
            _button = GetComponent<Button>();
            _tip = GetComponent<TipButton>();
            _tip.showOnClick = false;

            _portraitHolder = new GameObject("PortraitSlot", typeof(RectTransform));
            _portraitHolder.transform.SetParent(transform, false);
            RectTransform portraitRect = _portraitHolder.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(9f, -6f);
            portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _portraitHolder.SetActive(false);

            var iconObject = new GameObject("SchoolIcon", typeof(RectTransform), typeof(Image),
                typeof(Button));
            iconObject.transform.SetParent(transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(1f, 1f);
            iconRect.anchorMax = new Vector2(1f, 1f);
            iconRect.pivot = new Vector2(1f, 1f);
            iconRect.anchoredPosition = new Vector2(-9f, -6f);
            iconRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _schoolIcon = iconObject.GetComponent<Image>();
            _schoolIcon.preserveAspect = true;
            _schoolButton = iconObject.GetComponent<Button>();

            _name = Text("Name", new Vector2(5f, -60f), new Vector2(Width - 10f, 17f), 10,
                TextAnchor.MiddleCenter);
            _standing = Text("Standing", new Vector2(5f, -78f),
                new Vector2(Width - 10f, 14f), 8, TextAnchor.MiddleCenter);
            _standing.color = new Color(.98f, .82f, .42f, 1f);
            _detail = Text("Detail", new Vector2(4f, -92f),
                new Vector2(Width - 8f, 14f), 7, TextAnchor.UpperCenter);
            _detail.color = new Color(.84f, .82f, .74f, 1f);
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
            if (prefab == null) return false;

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
            _avatar.gameObject.SetActive(true);
            _avatar.enabled = true;
            if (_avatar.avatarLoader != null) _avatar.avatarLoader.enabled = true;
            _avatar.show(pActor);
            _portraitVisible = true;
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
            _portraitVisible = false;
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

        private void BindTooltip(SchoolRosterReadNode pNode)
        {
            var lines = new List<string>
            {
                AW_L10n.Text("aw_school_roster_standing", "Standing") + ": " +
                StandingLabel(pNode),
                AW_L10n.Text("aw_school_roster_reputation", "Reputation") + ": " +
                Mathf.RoundToInt(pNode.Layout.Reputation),
                AW_L10n.Text("aw_school_roster_generation", "Generation") + ": " +
                pNode.Layout.Generation
            };
            if (!string.IsNullOrEmpty(pNode.TeacherName))
                lines.Add(AW_L10n.Text("aw_school_roster_teacher", "Teacher") + ": " +
                          pNode.TeacherName);
            if (pNode.ResidenceCity?.data != null)
                lines.Add(AW_L10n.Text("aw_school_roster_residence", "Residence") + ": " +
                          pNode.ResidenceCity.data.name);
            if (pNode.DisplayKingdom?.data != null)
                lines.Add(pNode.DisplayKingdom.name);

            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            string title = pNode.ActorName;
            string description = string.Join("\n", lines.ToArray());
            _tip.hoverAction = () => Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                new TooltipData { tip_name = title, tip_description = description });
        }

        private static string StandingName(SchoolRosterStanding pStanding)
        {
            switch (pStanding)
            {
                case SchoolRosterStanding.HistoricalMaster:
                    return AW_L10n.Text("aw_school_roster_standing_master", "Historical Master");
                case SchoolRosterStanding.QualifiedTeacher:
                    return AW_L10n.Text("aw_school_roster_standing_teacher", "Teacher");
                case SchoolRosterStanding.DirectDisciple:
                    return AW_L10n.Text("aw_school_roster_standing_direct", "Direct Disciple");
                case SchoolRosterStanding.LaterDisciple:
                    return AW_L10n.Text("aw_school_roster_standing_later", "Later Disciple");
                default:
                    return AW_L10n.Text("aw_school_roster_standing_member", "Member");
            }
        }

        private static string StandingLabel(SchoolRosterReadNode pNode)
        {
            string standing = StandingName(pNode.Layout.Standing);
            return pNode.Layout.StableOrder == 0
                ? AW_L10n.Text("aw_school_roster_standing_leader", "Leader") +
                  " / " + standing
                : standing;
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            try
            {
                ColorAsset color = pKingdom?.getColor();
                if (color != null) return color.getColorText();
            }
            catch { }
            return new Color(.90f, .84f, .68f, 1f);
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
            ActionLibrary.openUnitWindow(resolved);
        }
    }
}
