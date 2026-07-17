using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.ui.windows;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class CourtActorNodeView : MonoBehaviour
    {
        public const float Width = 132f;
        public const float Height = 104f;
        private const float SlotSize = 52f;

        private Image _background;
        private UiUnitAvatarElement _avatar;
        private GameObject _avatarHolder;
        private Image _schoolIcon;
        private Button _schoolButton;
        private Text _name;
        private Text _roles;
        private Button _button;
        private TipButton _tip;
        private GameObject _manageOfficeObject;
        private Button _manageOfficeButton;
        private Text _manageOfficeText;
        private TipButton _manageOfficeTip;

        public static CourtActorNodeView Create(Transform pParent)
        {
            var obj = new GameObject("CourtActorNode", typeof(RectTransform), typeof(Image),
                typeof(Outline), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(Width, Height);
            var view = obj.AddComponent<CourtActorNodeView>();
            view.BuildUi();
            return view;
        }

        public void Bind(CourtPyramidNodeModel pNode, Kingdom pKingdom)
        {
            if (pNode == null || pKingdom?.data == null) return;
            gameObject.name = pNode.IsVacancy ? "CourtVacancy_" + pNode.OfficeId : "CourtActor_" + pNode.ActorId;
            Color kingdomColor = KingdomColor(pKingdom);
            _background.color = Color.Lerp(kingdomColor, Color.black, 0.58f);
            Outline outline = GetComponent<Outline>();
            outline.effectColor = Color.Lerp(kingdomColor, Color.black, 0.72f);
            outline.effectDistance = new Vector2(2f, -2f);
            _name.color = pNode.IsVacancy ? new Color(0.76f, 0.76f, 0.72f, 1f) : kingdomColor;

            Actor actor = pNode.IsVacancy ? null : World.world?.units?.get(pNode.ActorId);
            bool live = actor?.data != null && actor.isAlive() && !actor.isRekt();
            _avatarHolder.SetActive(live);
            if (live && _avatar != null)
            {
                _avatar.enabled = true;
                if (_avatar.avatarLoader != null) _avatar.avatarLoader.enabled = true;
                _avatar.show(actor);
            }

            Sprite schoolSprite = string.IsNullOrEmpty(pNode.SchoolIconPath)
                ? null
                : SpriteTextureLoader.getSprite(pNode.SchoolIconPath);
            _schoolIcon.sprite = schoolSprite;
            _schoolIcon.enabled = schoolSprite != null && !string.IsNullOrEmpty(pNode.SchoolId);
            _schoolIcon.preserveAspect = true;
            _schoolButton.onClick.RemoveAllListeners();
            _schoolButton.interactable = _schoolIcon.enabled;
            if (_schoolIcon.enabled)
            {
                string schoolId = pNode.SchoolId;
                _schoolButton.onClick.AddListener(() => SchoolWindow.OpenSchool(schoolId));
            }

            _name.text = pNode.IsVacancy
                ? OfficeName(pNode.OfficeId) + " - " + AW_L10n.Text("aw_court_no_officer", "Vacant")
                : pNode.ActorName;
            string roleLine = RoleLine(pNode, pKingdom);
            string officialGrade = OfficialGradeShort(pNode);
            _roles.text = string.IsNullOrEmpty(officialGrade)
                ? roleLine
                : roleLine + "\n" + officialGrade;

            _button.onClick.RemoveAllListeners();
            bool canAppoint = pNode.IsVacancy && !string.IsNullOrEmpty(pNode.OfficeId);
            _button.interactable = live || canAppoint;
            if (live)
                _button.onClick.AddListener(() => ActionLibrary.openUnitWindow(actor));
            else if (canAppoint)
                _button.onClick.AddListener(() =>
                    CourtAppointmentWindow.Open(pKingdom.id, pNode.OfficeId));

            long incumbentActorId = live && !pNode.IsVacancy
                ? actor.data.id
                : -1L;
            CourtManualOfficeAction officeAction =
                CourtManualAppointmentRules.ResolveOfficeAction(
                    CourtService.IsManualOfficeInCurrentTier(
                        pKingdom, pNode.OfficeId), incumbentActorId);
            bool canManageOffice = officeAction != CourtManualOfficeAction.None;
            _manageOfficeObject.SetActive(canManageOffice);
            _roles.rectTransform.sizeDelta = new Vector2(
                canManageOffice ? Width - 56f : Width - 8f, 22f);
            _manageOfficeButton.onClick.RemoveAllListeners();
            if (canManageOffice)
            {
                bool replacing = officeAction == CourtManualOfficeAction.Replace;
                _manageOfficeText.text = replacing
                    ? AW_L10n.Text("aw_court_replace_officer", "Replace")
                    : AW_L10n.Text("aw_court_select_officer", "Select");
                _manageOfficeButton.onClick.AddListener(() =>
                    CourtAppointmentWindow.Open(pKingdom.id, pNode.OfficeId, incumbentActorId));
                string tipDescription = replacing
                    ? AW_L10n.Text("aw_court_replace_officer_desc",
                        "Choose a new actor to replace the current officer.")
                    : AW_L10n.Text("aw_court_select_officer_desc",
                        "Choose an actor for this vacant office.");
                _manageOfficeTip.enabled = true;
                _manageOfficeTip.type = AW_RawTooltip.TYPE;
                _manageOfficeTip.hoverAction = () => Tooltip.show(
                    _manageOfficeObject, AW_RawTooltip.TYPE, new TooltipData
                    {
                        tip_name = _manageOfficeText.text,
                        tip_description = tipDescription
                    });
            }
            else
            {
                _manageOfficeTip.enabled = false;
                _manageOfficeTip.hoverAction = null;
            }
            SetTip(pNode, actor, pKingdom);
        }

        private void BuildUi()
        {
            _background = GetComponent<Image>();
            AW_UIStyle.ApplyButton(_background, 0.96f);
            _button = GetComponent<Button>();
            _tip = GetComponent<TipButton>();

            _avatarHolder = new GameObject("PortraitSlot", typeof(RectTransform));
            _avatarHolder.transform.SetParent(transform, false);
            RectTransform portraitRect = _avatarHolder.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 1f);
            portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(10f, -6f);
            portraitRect.sizeDelta = new Vector2(SlotSize, SlotSize);

            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab != null)
            {
                _avatar = Instantiate(prefab, _avatarHolder.transform);
                RectTransform avatarRect = _avatar.GetComponent<RectTransform>();
                if (avatarRect != null)
                {
                    avatarRect.anchorMin = new Vector2(0.5f, 0.5f);
                    avatarRect.anchorMax = new Vector2(0.5f, 0.5f);
                    avatarRect.pivot = new Vector2(0.5f, 0.5f);
                    avatarRect.anchoredPosition = Vector2.zero;
                    avatarRect.sizeDelta = new Vector2(SlotSize, SlotSize);
                    avatarRect.localScale = Vector3.one;
                }
            }

            var schoolSlot = new GameObject("SchoolIconSlot", typeof(RectTransform), typeof(Image),
                typeof(Button));
            schoolSlot.transform.SetParent(transform, false);
            RectTransform schoolRect = schoolSlot.GetComponent<RectTransform>();
            schoolRect.anchorMin = new Vector2(1f, 1f);
            schoolRect.anchorMax = new Vector2(1f, 1f);
            schoolRect.pivot = new Vector2(1f, 1f);
            schoolRect.anchoredPosition = new Vector2(-10f, -6f);
            schoolRect.sizeDelta = new Vector2(SlotSize, SlotSize);
            _schoolIcon = schoolSlot.GetComponent<Image>();
            _schoolIcon.preserveAspect = true;
            _schoolIcon.raycastTarget = true;
            _schoolButton = schoolSlot.GetComponent<Button>();

            _name = CreateText("Name", new Vector2(6f, -62f), new Vector2(Width - 12f, 16f),
                10, TextAnchor.MiddleCenter);
            _name.resizeTextForBestFit = true;
            _name.resizeTextMinSize = 7;
            _name.resizeTextMaxSize = 10;
            _roles = CreateText("Roles", new Vector2(4f, -79f), new Vector2(Width - 8f, 22f),
                8, TextAnchor.UpperCenter);
            _roles.color = new Color(0.95f, 0.86f, 0.58f, 1f);

            _manageOfficeObject = new GameObject("ManageOffice", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            _manageOfficeObject.transform.SetParent(transform, false);
            RectTransform manageRect =
                _manageOfficeObject.GetComponent<RectTransform>();
            manageRect.anchorMin = new Vector2(1f, 0f);
            manageRect.anchorMax = new Vector2(1f, 0f);
            manageRect.pivot = new Vector2(1f, 0f);
            manageRect.anchoredPosition = new Vector2(-5f, 5f);
            manageRect.sizeDelta = new Vector2(42f, 16f);
            AW_UIStyle.ApplyButton(_manageOfficeObject.GetComponent<Image>(), 0.96f);
            _manageOfficeButton = _manageOfficeObject.GetComponent<Button>();
            _manageOfficeTip = _manageOfficeObject.GetComponent<TipButton>();
            _manageOfficeTip.showOnClick = false;

            var manageTextObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            manageTextObject.transform.SetParent(_manageOfficeObject.transform, false);
            RectTransform manageTextRect =
                manageTextObject.GetComponent<RectTransform>();
            manageTextRect.anchorMin = Vector2.zero;
            manageTextRect.anchorMax = Vector2.one;
            manageTextRect.offsetMin = Vector2.zero;
            manageTextRect.offsetMax = Vector2.zero;
            _manageOfficeText = manageTextObject.GetComponent<Text>();
            _manageOfficeText.font = LocalizedTextManager.current_font;
            _manageOfficeText.fontSize = 7;
            _manageOfficeText.alignment = TextAnchor.MiddleCenter;
            _manageOfficeText.color = Color.white;
            _manageOfficeText.raycastTarget = false;
            _manageOfficeText.resizeTextForBestFit = true;
            _manageOfficeText.resizeTextMinSize = 6;
            _manageOfficeText.resizeTextMaxSize = 7;
            _manageOfficeObject.SetActive(false);
        }

        private Text CreateText(string pName, Vector2 pPosition, Vector2 pSize, int pFontSize,
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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void SetTip(CourtPyramidNodeModel pNode, Actor pActor, Kingdom pKingdom)
        {
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            string title = pNode.IsVacancy ? OfficeName(pNode.OfficeId) : pNode.ActorName;
            string desc = BuildTooltip(pNode, pActor, pKingdom);
            _tip.hoverAction = () => Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                new TooltipData { tip_name = title, tip_description = desc });
        }

        private static string BuildTooltip(CourtPyramidNodeModel pNode, Actor pActor, Kingdom pKingdom)
        {
            var lines = new List<string>();
            lines.Add(AW_L10n.Text("aw_court_roles", "Roles") + ": " + RoleLine(pNode, pKingdom));
            lines.Add(AW_L10n.Text("aw_court_school", "School") + ": " + SchoolName(pNode.SchoolId));
            if (pNode.IsVacancy && !string.IsNullOrEmpty(pNode.OfficeId))
                lines.Add(AW_L10n.Text("aw_court_vacancy_click",
                    "Click to appoint an eligible actor"));
            if (pNode.AppointmentYear >= 0)
                lines.Add(AW_L10n.Text("aw_court_appointed_year", "Appointed") + ": " + pNode.AppointmentYear);
            if (!string.IsNullOrEmpty(pNode.CityName))
                lines.Add(AW_L10n.Text("aw_court_city", "City") + ": " + pNode.CityName);
            if (pNode.Merit > 0)
                lines.Add(AW_L10n.Text("aw_general_merit", "Merit") + ": " + pNode.Merit);
            if (pNode.OfficialRank > 0)
            {
                lines.Add(AW_L10n.Text("aw_court_official_rank", "Official rank") + ": " +
                          pNode.OfficialRank + "/18");
                lines.Add(AW_L10n.Text("aw_court_official_track", "Career track") + ": " +
                          OfficialTrackName(pNode.OfficialTrack));
                lines.Add(AW_L10n.Text("aw_court_official_merit", "Career merit") + ": " +
                          pNode.OfficialMerit.ToString("0.00") + "/" + pNode.OfficialMeritCap);
                lines.Add(AW_L10n.Text("aw_court_official_kaoke", "Last evaluation") + ": " +
                          OfficialEvaluationName(pNode.OfficialLastEvaluation));
                if (pNode.OfficialTermEndYear >= 0)
                    lines.Add(AW_L10n.Text("aw_court_official_term_end", "Term review year") + ": " +
                              pNode.OfficialTermEndYear);
            }
            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_ID,
                out long ministerialPremierId, -1L);
            if (pNode.ActorId >= 0 && pNode.ActorId == ministerialPremierId)
            {
                pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_POWER,
                    out int ministerialPower, 0);
                int stage = MinisterialPowerRules.HighestReachedThreshold(
                    ministerialPower);
                lines.Add(AW_L10n.Text("aw_court_ministerial_power", "Ministerial power") +
                          ": " + ministerialPower + "/100");
                lines.Add(AW_L10n.Text("aw_court_ministerial_stage", "Authority stage") +
                          ": " + MinisterialStageName(stage));
            }
            if (pActor?.data != null)
            {
                lines.Add(AW_L10n.Text("aw_court_age", "Age") + ": " + SafeAge(pActor));
                lines.Add(AW_L10n.Text("aw_court_stat_stewardship", "Stewardship") + " " +
                          SafeStat(pActor, "stewardship").ToString("0") + "  " +
                          AW_L10n.Text("aw_court_stat_diplomacy", "Diplomacy") + " " +
                          SafeStat(pActor, "diplomacy").ToString("0"));
                lines.Add(AW_L10n.Text("aw_court_stat_warfare", "Warfare") + " " +
                          SafeStat(pActor, "warfare").ToString("0") + "  " +
                          AW_L10n.Text("aw_court_stat_intelligence", "Intelligence") + " " +
                          SafeStat(pActor, "intelligence").ToString("0"));
            }
            return string.Join("\n", lines.ToArray());
        }

        private static string OfficialGradeShort(CourtPyramidNodeModel pNode)
        {
            if (pNode == null || pNode.OfficialRank <= 0) return "";
            return string.Format(AW_L10n.Text("aw_court_official_rank_short", "{0} rank {1}"),
                OfficialTrackName(pNode.OfficialTrack), pNode.OfficialRank);
        }

        private static string OfficialTrackName(int pTrack)
        {
            return pTrack == OfficialCareerRankRules.MilitaryTrack
                ? AW_L10n.Text("aw_court_official_track_military", "Military")
                : AW_L10n.Text("aw_court_official_track_civil", "Civil");
        }

        private static string OfficialEvaluationName(int pGrade)
        {
            if (pGrade < 0 || pGrade > 4)
                return AW_L10n.Text("aw_court_official_kaoke_none", "Not evaluated");
            return AW_L10n.Text("aw_court_official_kaoke_" + pGrade, pGrade.ToString());
        }

        private static string MinisterialStageName(int pThreshold)
        {
            return AW_L10n.Text("aw_court_ministerial_stage_" + pThreshold,
                pThreshold.ToString());
        }

        private static string RoleLine(CourtPyramidNodeModel pNode, Kingdom pKingdom)
        {
            var labels = new List<string>();
            foreach (string role in pNode.Roles ?? new List<string>())
            {
                string label = RoleName(role, pNode.CityName, pKingdom);
                if (!string.IsNullOrEmpty(label) && !labels.Contains(label)) labels.Add(label);
            }
            if (labels.Count == 0 && !string.IsNullOrEmpty(pNode.OfficeId))
                labels.Add(OfficeName(pNode.OfficeId));
            return string.Join(" / ", labels.ToArray());
        }

        private static string RoleName(string pRole, string pCityName, Kingdom pKingdom)
        {
            switch (pRole ?? "")
            {
                case CourtPyramidRoleId.King:
                    return AW_L10n.Text(GovernmentTitleRules.RulerKey(
                        RepublicGovernmentService.IsRepublic(pKingdom)), "King");
                case CourtPyramidRoleId.Heir:
                    return AW_L10n.Text(GovernmentTitleRules.SuccessorKey(
                        RepublicGovernmentService.IsRepublic(pKingdom),
                        MandateService.GetCurrentMandateKingdom() == pKingdom), "Heir");
                case CourtPyramidRoleId.General: return AW_L10n.Text("aw_court_general", "General");
                case CourtPyramidRoleId.Governor:
                    return string.IsNullOrEmpty(pCityName)
                        ? OfficeName(CourtOfficeId.Governor)
                        : pCityName + " " + OfficeName(CourtOfficeId.Governor);
                default: return OfficeName(pRole);
            }
        }

        private static string OfficeName(string pOfficeId)
        {
            return AW_L10n.Text("aw_court_office_" + (pOfficeId ?? ""), pOfficeId ?? "");
        }

        private static string SchoolName(string pSchoolId)
        {
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(pSchoolId);
            return definition == null
                ? AW_L10n.Text("aw_court_school_none", "No school")
                : AW_L10n.Text(definition.NameKey, definition.Id);
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            string hex = HistoryColors.FromKingdom(pKingdom);
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color))
                return new Color(color.r, color.g, color.b, 1f);
            return Color.white;
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor.getAge(); }
            catch { return 0; }
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}
