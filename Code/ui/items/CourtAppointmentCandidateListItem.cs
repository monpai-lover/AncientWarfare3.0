using System;
using AncientWarfare3.core.court;
using AncientWarfare3.ui.windows;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal enum CourtAppointmentNavigationAction
    {
        None,
        Previous,
        Next
    }

    internal sealed class CourtAppointmentCandidateRow
    {
        public CourtAppointmentCandidateView candidate;
        public string role_text = "";
        public string school_text = "";
        public bool is_message;
        public bool is_header;
        public bool is_error;
        public string message_title = "";
        public string message_body = "";
        public CourtAppointmentNavigationAction navigation_action;
    }

    internal sealed class CourtAppointmentCandidateListItem :
        AbstractListWindowItem<CourtAppointmentCandidateRow>
    {
        private const float RowWidth = 220f;
        private const float CandidateHeight = 68f;
        private const float MessageHeight = 42f;
        private const float PortraitSize = 46f;

        private LayoutElement _layout;
        private Image _background;
        private Button _rowButton;
        private TipButton _tip;
        private GameObject _avatarHolder;
        private UiUnitAvatarElement _avatar;
        private Text _name;
        private Text _role;
        private Text _school;
        private Text _stats;
        private GameObject _appointObject;
        private Button _appointButton;
        private Text _appointText;

        public override void Setup(CourtAppointmentCandidateRow pObject)
        {
            EnsureUi();
            ClearInteractions();
            if (pObject != null &&
                pObject.navigation_action != CourtAppointmentNavigationAction.None)
            {
                SetupNavigation(pObject);
                return;
            }
            if (pObject == null || pObject.is_message)
            {
                SetupMessage(pObject);
                return;
            }
            SetupCandidate(pObject);
        }

        private void EnsureUi()
        {
            if (_name != null) return;
            RectTransform rect = gameObject.GetComponent<RectTransform>() ??
                                 gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(RowWidth, CandidateHeight);
            _layout = gameObject.GetComponent<LayoutElement>() ??
                      gameObject.AddComponent<LayoutElement>();
            _background = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            _rowButton = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            _tip = gameObject.GetComponent<TipButton>() ?? gameObject.AddComponent<TipButton>();
            _tip.showOnClick = false;
            AW_UIStyle.ApplyListRow(_background, 0.92f);

            _avatarHolder = new GameObject("PortraitSlot", typeof(RectTransform));
            _avatarHolder.transform.SetParent(transform, false);
            RectTransform portrait = _avatarHolder.GetComponent<RectTransform>();
            portrait.anchorMin = new Vector2(0f, 0.5f);
            portrait.anchorMax = new Vector2(0f, 0.5f);
            portrait.pivot = new Vector2(0f, 0.5f);
            portrait.anchoredPosition = new Vector2(6f, 0f);
            portrait.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            _avatarHolder.SetActive(false);

            _name = CreateText("Name", new Vector2(57f, -5f),
                new Vector2(108f, 16f), 9, TextAnchor.UpperLeft);
            _role = CreateText("Role", new Vector2(57f, -21f),
                new Vector2(108f, 14f), 8, TextAnchor.UpperLeft);
            _role.color = new Color(0.96f, 0.80f, 0.42f, 1f);
            _school = CreateText("School", new Vector2(57f, -35f),
                new Vector2(108f, 13f), 7, TextAnchor.UpperLeft);
            _school.color = new Color(0.82f, 0.86f, 0.96f, 1f);
            _stats = CreateText("Stats", new Vector2(57f, -48f),
                new Vector2(108f, 17f), 7, TextAnchor.UpperLeft);
            _stats.color = new Color(0.84f, 0.82f, 0.76f, 1f);

            _appointObject = new GameObject("Appoint", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            _appointObject.transform.SetParent(transform, false);
            RectTransform appointRect = _appointObject.GetComponent<RectTransform>();
            appointRect.anchorMin = new Vector2(1f, 0.5f);
            appointRect.anchorMax = new Vector2(1f, 0.5f);
            appointRect.pivot = new Vector2(1f, 0.5f);
            appointRect.anchoredPosition = new Vector2(-6f, 0f);
            appointRect.sizeDelta = new Vector2(46f, 24f);
            AW_UIStyle.ApplyButton(_appointObject.GetComponent<Image>(), 0.96f);
            _appointButton = _appointObject.GetComponent<Button>();

            var appointTextObject = new GameObject("Text", typeof(RectTransform),
                typeof(Text));
            appointTextObject.transform.SetParent(_appointObject.transform, false);
            RectTransform appointTextRect =
                appointTextObject.GetComponent<RectTransform>();
            appointTextRect.anchorMin = Vector2.zero;
            appointTextRect.anchorMax = Vector2.one;
            appointTextRect.offsetMin = Vector2.zero;
            appointTextRect.offsetMax = Vector2.zero;
            _appointText = appointTextObject.GetComponent<Text>();
            _appointText.font = LocalizedTextManager.current_font;
            _appointText.fontSize = 8;
            _appointText.alignment = TextAnchor.MiddleCenter;
            _appointText.color = Color.white;
            _appointText.raycastTarget = false;
            _appointText.resizeTextForBestFit = true;
            _appointText.resizeTextMinSize = 6;
            _appointText.resizeTextMaxSize = 8;
        }

        private void SetupCandidate(CourtAppointmentCandidateRow pRow)
        {
            CourtAppointmentCandidateView candidate = pRow.candidate;
            Actor actor = FindActor(candidate?.actor_id ?? -1L);
            bool live = actor?.data != null && actor.isAlive() && !actor.isRekt();
            ApplyHeight(CandidateHeight);
            AW_UIStyle.ApplyListRow(_background, live ? 0.92f : 0.62f);
            _avatarHolder.SetActive(live);
            if (live) ShowAvatar(actor);
            _name.gameObject.SetActive(true);
            _role.gameObject.SetActive(true);
            _school.gameObject.SetActive(true);
            _stats.gameObject.SetActive(true);
            _appointObject.SetActive(true);

            _name.rectTransform.anchoredPosition = new Vector2(57f, -5f);
            _name.rectTransform.sizeDelta = new Vector2(108f, 16f);
            _role.rectTransform.anchoredPosition = new Vector2(57f, -21f);
            _role.rectTransform.sizeDelta = new Vector2(108f, 14f);
            _role.alignment = TextAnchor.UpperLeft;
            _role.fontStyle = FontStyle.Normal;
            string age = string.Format(AW_L10n.Text("aw_court_candidate_age_short",
                "{0}y"), candidate?.age ?? 0);
            _name.text = (candidate?.actor_name ?? "") + "  " + age;
            _name.color = live ? ActorColor(actor) : new Color(0.62f, 0.62f, 0.62f, 1f);
            _role.text = pRow.role_text ?? "";
            _school.text = AW_L10n.Text("aw_court_school", "School") + ": " +
                           (pRow.school_text ?? "");
            _stats.text = string.Format(AW_L10n.Text("aw_court_candidate_stats_compact",
                    "Gov {0:0}  Dip {1:0}  War {2:0}  Int {3:0}"),
                candidate?.stewardship ?? 0f,
                candidate?.diplomacy ?? 0f,
                candidate?.warfare ?? 0f,
                candidate?.intelligence ?? 0f);

            long actorId = candidate?.actor_id ?? -1L;
            _rowButton.interactable = live;
            if (live) _rowButton.onClick.AddListener(() => OpenActor(actorId));
            _appointButton.interactable = live;
            _appointText.text = AW_L10n.Text("aw_court_appointment_action", "Appoint");
            if (live)
                _appointButton.onClick.AddListener(() =>
                    CourtAppointmentWindow.Appoint(actorId));

            string tooltip = (pRow.role_text ?? "") + "\n" +
                             AW_L10n.Text("aw_court_school", "School") + ": " +
                             (pRow.school_text ?? "") + "\n" + _stats.text + "\n" +
                             AW_L10n.Text("aw_court_candidate_score", "Appointment score") +
                             ": " + (candidate?.score ?? 0f).ToString("0.0");
            SetTip(_tip, gameObject, candidate?.actor_name ?? "", tooltip);
            TipButton appointTip = _appointObject.GetComponent<TipButton>();
            SetTip(appointTip, _appointObject,
                AW_L10n.Text("aw_court_appointment_action", "Appoint"),
                AW_L10n.Text("aw_court_appointment_action_desc",
                    "Appoint this actor to the vacant office."));
        }

        private void SetupMessage(CourtAppointmentCandidateRow pRow)
        {
            bool header = pRow?.is_header ?? false;
            bool error = pRow?.is_error ?? false;
            bool hasBody = !string.IsNullOrEmpty(pRow?.message_body);
            ApplyHeight(header && hasBody ? 56f : MessageHeight);
            if (header) AW_UIStyle.ApplyPanel(_background, 0.96f);
            else AW_UIStyle.ApplyListRow(_background, error ? 0.82f : 0.72f);
            _avatarHolder.SetActive(false);
            _appointObject.SetActive(false);
            _school.gameObject.SetActive(false);
            _stats.gameObject.SetActive(false);
            _name.gameObject.SetActive(true);
            _role.gameObject.SetActive(hasBody);
            _name.rectTransform.anchoredPosition = new Vector2(8f, -6f);
            _name.rectTransform.sizeDelta = new Vector2(204f, 16f);
            _name.text = pRow?.message_title ?? "";
            _name.alignment = header ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            _name.fontStyle = header ? FontStyle.Bold : FontStyle.Normal;
            _name.color = error
                ? new Color(1f, 0.55f, 0.45f, 1f)
                : header
                    ? new Color(1f, 0.75f, 0.35f, 1f)
                    : new Color(0.78f, 0.78f, 0.74f, 1f);
            _role.rectTransform.anchoredPosition = new Vector2(8f, -22f);
            _role.rectTransform.sizeDelta = new Vector2(204f, header ? 28f : 16f);
            _role.alignment = header ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            _role.fontStyle = FontStyle.Normal;
            _role.text = pRow?.message_body ?? "";
            _rowButton.interactable = false;
        }

        private void SetupNavigation(CourtAppointmentCandidateRow pRow)
        {
            ApplyHeight(MessageHeight);
            AW_UIStyle.ApplyListRow(_background, 0.84f);
            _avatarHolder.SetActive(false);
            _school.gameObject.SetActive(false);
            _stats.gameObject.SetActive(false);
            _role.gameObject.SetActive(false);
            _name.gameObject.SetActive(true);
            _appointObject.SetActive(true);
            _name.rectTransform.anchoredPosition = new Vector2(8f, -12f);
            _name.rectTransform.sizeDelta = new Vector2(150f, 18f);
            _name.alignment = TextAnchor.MiddleLeft;
            _name.fontStyle = FontStyle.Bold;
            _name.color = new Color(0.95f, 0.82f, 0.46f, 1f);
            _name.text = pRow.message_title ?? "";
            _appointText.text = pRow.message_title ?? "";
            _rowButton.interactable = false;
            _appointButton.interactable = true;
            int delta = pRow.navigation_action ==
                        CourtAppointmentNavigationAction.Previous ? -1 : 1;
            _appointButton.onClick.AddListener(() =>
                CourtAppointmentWindow.ChangePage(delta));
        }

        private void ClearInteractions()
        {
            _rowButton.onClick.RemoveAllListeners();
            _appointButton.onClick.RemoveAllListeners();
            _tip.hoverAction = null;
            _tip.clickAction = null;
            _tip.enabled = false;
            TipButton appointTip = _appointObject.GetComponent<TipButton>();
            appointTip.hoverAction = null;
            appointTip.clickAction = null;
            appointTip.enabled = false;
            _name.fontStyle = FontStyle.Normal;
            _name.alignment = TextAnchor.UpperLeft;
        }

        private void ShowAvatar(Actor actor)
        {
            if (_avatar == null)
            {
                UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
                if (prefab == null)
                {
                    _avatarHolder.SetActive(false);
                    return;
                }
                _avatar = Instantiate(prefab, _avatarHolder.transform);
                RectTransform rect = _avatar.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
                    rect.localScale = Vector3.one;
                }
            }
            _avatar.gameObject.SetActive(true);
            _avatar.enabled = true;
            if (_avatar.avatarLoader != null) _avatar.avatarLoader.enabled = true;
            _avatar.show(actor);
        }

        private Text CreateText(string pName, Vector2 pPosition, Vector2 pSize,
            int pFontSize, TextAnchor pAnchor)
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
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pFontSize;
            text.raycastTarget = false;
            return text;
        }

        private void ApplyHeight(float pHeight)
        {
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(RowWidth, pHeight);
            _layout.minHeight = pHeight;
            _layout.preferredHeight = pHeight;
        }

        private static Actor FindActor(long pActorId)
        {
            try { return pActorId < 0 ? null : World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static void OpenActor(long pActorId)
        {
            Actor actor = FindActor(pActorId);
            if (actor?.data == null || !actor.isAlive() || actor.isRekt()) return;
            ActionLibrary.openUnitWindow(actor);
        }

        private static Color ActorColor(Actor pActor)
        {
            try
            {
                ColorAsset color = pActor?.kingdom?.getColor();
                return color?.getColorText() ?? Color.white;
            }
            catch { return Color.white; }
        }

        private static void SetTip(TipButton pTip, GameObject pOwner,
            string pTitle, string pDescription)
        {
            if (pTip == null) return;
            pTip.enabled = true;
            pTip.type = AW_RawTooltip.TYPE;
            string title = pTitle ?? "";
            string description = pDescription ?? "";
            pTip.hoverAction = () => Tooltip.show(pOwner, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = title,
                    tip_description = description
                });
        }
    }
}
