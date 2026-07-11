using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
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
        private Text _name;
        private Text _roles;
        private Button _button;
        private TipButton _tip;

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
            _schoolIcon.enabled = schoolSprite != null;
            _schoolIcon.preserveAspect = true;

            _name.text = pNode.IsVacancy
                ? OfficeName(pNode.OfficeId) + " - " + AW_L10n.Text("aw_court_no_officer", "Vacant")
                : pNode.ActorName;
            _roles.text = RoleLine(pNode, pKingdom);

            _button.onClick.RemoveAllListeners();
            _button.interactable = live;
            if (live) _button.onClick.AddListener(() => ActionLibrary.openUnitWindow(actor));
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

            var schoolSlot = new GameObject("SchoolIconSlot", typeof(RectTransform), typeof(Image));
            schoolSlot.transform.SetParent(transform, false);
            RectTransform schoolRect = schoolSlot.GetComponent<RectTransform>();
            schoolRect.anchorMin = new Vector2(1f, 1f);
            schoolRect.anchorMax = new Vector2(1f, 1f);
            schoolRect.pivot = new Vector2(1f, 1f);
            schoolRect.anchoredPosition = new Vector2(-10f, -6f);
            schoolRect.sizeDelta = new Vector2(SlotSize, SlotSize);
            _schoolIcon = schoolSlot.GetComponent<Image>();
            _schoolIcon.preserveAspect = true;
            _schoolIcon.raycastTarget = false;

            _name = CreateText("Name", new Vector2(6f, -62f), new Vector2(Width - 12f, 16f),
                10, TextAnchor.MiddleCenter);
            _name.resizeTextForBestFit = true;
            _name.resizeTextMinSize = 7;
            _name.resizeTextMaxSize = 10;
            _roles = CreateText("Roles", new Vector2(4f, -79f), new Vector2(Width - 8f, 22f),
                8, TextAnchor.UpperCenter);
            _roles.color = new Color(0.95f, 0.86f, 0.58f, 1f);
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
            if (pNode.AppointmentYear >= 0)
                lines.Add(AW_L10n.Text("aw_court_appointed_year", "Appointed") + ": " + pNode.AppointmentYear);
            if (!string.IsNullOrEmpty(pNode.CityName))
                lines.Add(AW_L10n.Text("aw_court_city", "City") + ": " + pNode.CityName);
            if (pNode.Merit > 0)
                lines.Add(AW_L10n.Text("aw_general_merit", "Merit") + ": " + pNode.Merit);
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
            return AW_L10n.Text("aw_court_school_" + SchoolLocaleSuffix(pSchoolId), pSchoolId ?? "");
        }

        private static string SchoolLocaleSuffix(string pSchoolId)
        {
            switch (pSchoolId ?? "")
            {
                case CourtSchoolId.Legalist: return "fa";
                case CourtSchoolId.Mohist: return "mo";
                case CourtSchoolId.Military: return "bing";
                case CourtSchoolId.Diplomat: return "zongheng";
                case CourtSchoolId.Agrarian: return "nong";
                case CourtSchoolId.YinYang: return "yinyang";
                case CourtSchoolId.Logician: return "ming";
                default: return pSchoolId ?? "";
            }
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
