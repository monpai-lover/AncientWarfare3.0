using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class RulerHouseholdWindow :
        AbstractWindow<RulerHouseholdWindow>
    {
        private const float DefaultWidth = 560f;
        private const float DefaultHeight = 360f;
        private const float MinWidth = 420f;
        private const float MinHeight = 280f;
        private const float MaxWidth = 900f;
        private const float MaxHeight = 650f;
        private const float HeaderHeight = 66f;
        private static long _kingdomId = -1L;

        private readonly List<HouseholdRowView> _consortRows = new();
        private Vector2 _windowSize = new(DefaultWidth, DefaultHeight);
        private RectTransform _root;
        private RectTransform _header;
        private Text _rulerName;
        private Text _rulerDetail;
        private Button _courtBack;
        private Text _courtBackText;
        private Button _kingdomBack;
        private Text _kingdomBackText;
        private RectTransform _listRoot;
        private RectTransform _listContent;
        private ScrollRect _listScroll;
        private Text _principalLabel;
        private Text _consortLabel;
        private HouseholdRowView _principalRow;
        private WideWindowChrome _chrome;

        private sealed class HouseholdRowView
        {
            public GameObject Root;
            public Image Background;
            public Button Button;
            public GameObject PortraitRoot;
            public UiUnitAvatarElement Portrait;
            public Text Name;
            public Text Detail;
        }

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.HAREM);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.HAREM,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize =>
                {
                    _windowSize = pSize;
                    ApplyLayout();
                },
                new Vector2(DefaultWidth, DefaultHeight),
                new Vector2(MinWidth, MinHeight),
                new Vector2(MaxWidth, MaxHeight));
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            _root = new GameObject("RulerHouseholdRoot",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(ContentTransform, false);

            _header = new GameObject("HouseholdHeader",
                typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            _header.SetParent(_root, false);
            _header.GetComponent<Image>().color =
                new Color(.16f, .13f, .085f, .98f);
            _rulerName = CreateText(_header, "RulerName", 12,
                TextAnchor.UpperLeft, FontStyle.Bold);
            _rulerDetail = CreateText(_header, "RulerDetail", 9,
                TextAnchor.UpperLeft, FontStyle.Normal);
            _courtBack = CreateButton(_header, "BackToCourt", BackToCourt,
                out _courtBackText);
            _kingdomBack = CreateButton(_header, "BackToKingdom",
                BackToKingdom, out _kingdomBackText);

            CreateScrollArea(_root, out _listRoot, out _listContent,
                out _listScroll);
            _principalLabel = CreateSectionLabel(_listContent,
                "PrincipalWifeSection");
            _principalRow = CreateRow(_listContent, "PrincipalWifeRow");
            _consortLabel = CreateSectionLabel(_listContent,
                "ConsortSection");
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            SetWindowTitle();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            RulerHouseholdSnapshot snapshot =
                RulerHouseholdReadModelService.Build(kingdom);
            if (!snapshot.Available)
            {
                BindUnavailable(snapshot.Reason);
                return;
            }

            _rulerName.text = snapshot.RulerName;
            _rulerDetail.text =
                (snapshot.RulerTitle ?? "") + "  |  " +
                (snapshot.RealmName ?? "") + "  |  " +
                AW_L10n.Text("aw_household_consort_capacity",
                    "Consorts") + " " + snapshot.Consorts.Count + " / " +
                snapshot.ConsortCapacity;
            string principalSectionKey = snapshot.RulerIsFemale
                ? "aw_household_section_royal_husband"
                : "aw_household_section_principal_wife";
            string principalEmptyKey = snapshot.RulerIsFemale
                ? "aw_household_empty_royal_husband"
                : "aw_household_empty_principal_wife";
            _principalLabel.text = AW_L10n.Text(principalSectionKey,
                snapshot.RulerIsFemale ? "Prince Consort" : "Principal Wife");
            _consortLabel.text = AW_L10n.Text(
                "aw_household_section_consorts", "Consorts");
            BindRow(_principalRow, snapshot.PrincipalWife,
                AW_L10n.Text(principalEmptyKey, snapshot.RulerIsFemale
                    ? "No prince consort"
                    : "No principal wife"));
            for (int i = 0; i < snapshot.Consorts.Count; i++)
            {
                while (_consortRows.Count <= i)
                    _consortRows.Add(CreateRow(_listContent,
                        "ConsortRow" + _consortRows.Count));
                HouseholdRowView row = _consortRows[i];
                row.Root.transform.SetAsLastSibling();
                BindRow(row, snapshot.Consorts[i], "");
            }
            if (snapshot.Consorts.Count == 0)
            {
                if (_consortRows.Count == 0)
                    _consortRows.Add(CreateRow(_listContent, "ConsortEmpty"));
                HouseholdRowView empty = _consortRows[0];
                empty.Root.transform.SetAsLastSibling();
                BindRow(empty, null, AW_L10n.Text(
                    "aw_household_empty_consorts", "No consorts"));
            }
            for (int i = Math.Max(1, snapshot.Consorts.Count);
                 i < _consortRows.Count; i++)
                _consortRows[i].Root.SetActive(false);
            Canvas.ForceUpdateCanvases();
        }

        private void BindUnavailable(string pReason)
        {
            _rulerName.text = AW_L10n.Text("aw_household_unavailable",
                "Household unavailable");
            _rulerDetail.text = DiplomacyConversationWindow.ProposalFailure(
                pReason);
            _principalLabel.text = "";
            _consortLabel.text = "";
            _principalRow.Root.SetActive(false);
            for (int i = 0; i < _consortRows.Count; i++)
                _consortRows[i].Root.SetActive(false);
        }

        private static void BindRow(HouseholdRowView pView,
            RulerHouseholdDisplayRow pRow, string pEmptyText)
        {
            pView.Root.SetActive(true);
            pView.Button.onClick.RemoveAllListeners();
            if (pRow == null)
            {
                pView.Button.interactable = false;
                pView.PortraitRoot.SetActive(false);
                pView.Name.text = pEmptyText ?? "";
                pView.Detail.text = "";
                pView.Background.color = new Color(.11f, .105f, .09f,
                    .82f);
                return;
            }

            pView.Name.text = pRow.ActorName + "  " + AW_L10n.Text(
                pRow.TitleKey, pRow.Kind ==
                RulerHouseholdKind.PrincipalWife
                    ? "Principal Wife"
                    : "Consort");
            string age = pRow.Age >= 0
                ? pRow.Age.ToString()
                : AW_L10n.Text("aw_household_unknown", "Unknown");
            string year = pRow.EntryYear >= 0
                ? pRow.EntryYear.ToString()
                : AW_L10n.Text("aw_household_unknown", "Unknown");
            pView.Detail.text =
                AW_L10n.Text("aw_household_age", "Age") + " " + age +
                "  |  " +
                AW_L10n.Text("aw_household_origin", "Origin") + " " +
                pRow.OriginRealmName + "  |  " +
                AW_L10n.Text("aw_household_lineage", "Lineage") + " " +
                pRow.LineageLabel + "\n" +
                AW_L10n.Text("aw_household_entry_year", "Entered") + " " +
                year + "  |  " +
                AW_L10n.Text("aw_household_living_children",
                    "Living children") + " " + pRow.LivingChildren +
                "  |  " + AW_L10n.Text(pRow.Alive
                    ? "aw_household_status_active"
                    : "aw_household_status_unavailable",
                    pRow.Alive ? "In household" : "Unavailable");
            pView.Background.color = pRow.Kind ==
                RulerHouseholdKind.PrincipalWife
                ? new Color(.22f, .17f, .095f, .94f)
                : new Color(.135f, .125f, .105f, .92f);
            TryBindPortrait(pView, pRow.ActorId, pRow.Alive);
            BindNavigation(pView, pRow.ActorId, pRow.Alive);
        }

        private static void BindNavigation(HouseholdRowView pView,
            long pActorId, bool pMarkedAlive)
        {
            pView.Button.interactable = pMarkedAlive && pActorId >= 0L;
            if (!pView.Button.interactable) return;
            long actorId = pActorId;
            pView.Button.onClick.AddListener(() =>
            {
                Actor actor = null;
                bool actorAlive = false;
                bool actorRekt = true;
                try
                {
                    actor = World.world?.units?.get(actorId);
                    actorAlive = actor?.isAlive() == true;
                    actorRekt = actor?.isRekt() != false;
                }
                catch { }
                if (RulerHouseholdNavigationRules.CanOpen(
                        rowPresent: true, markedAlive: pMarkedAlive,
                        actorResolved: actor?.data != null,
                        actorAlive: actorAlive, actorRekt: actorRekt))
                    ActionLibrary.openUnitWindow(actor);
            });
        }

        private static void TryBindPortrait(HouseholdRowView pView,
            long pActorId, bool pAlive)
        {
            Actor actor = null;
            if (pAlive && pActorId >= 0L)
                try { actor = World.world?.units?.get(pActorId); }
                catch { actor = null; }
            if (actor?.data == null || !actor.isAlive() || actor.isRekt())
            {
                pView.PortraitRoot.SetActive(false);
                return;
            }
            pView.PortraitRoot.SetActive(true);
            if (pView.Portrait == null)
            {
                UiUnitAvatarElement prefab =
                    FamilyTreeNodeView.GetAvatarPrefab();
                if (prefab == null)
                {
                    pView.PortraitRoot.SetActive(false);
                    return;
                }
                pView.Portrait = UnityEngine.Object.Instantiate(prefab,
                    pView.PortraitRoot.transform);
                RectTransform rect =
                    pView.Portrait.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }
            pView.Portrait.show(actor);
        }

        private void SetWindowTitle()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.text = AW_L10n.Text(
                    "aw_ruler_household", "Ruler Household");
            _courtBackText.text = AW_L10n.Text("aw_court_back_to_court",
                "Back to Court");
            _kingdomBackText.text = AW_L10n.Text("aw_back_to_kingdom",
                "Back to Kingdom");
        }

        private void BackToCourt()
        {
            CourtWindow.Open(_kingdomId);
        }

        private void BackToKingdom()
        {
            AW_LineageWindowIds.ShowKingdom(_kingdomId);
        }

        private void ApplyLayout()
        {
            RectTransform background =
                BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
            float width = Math.Max(1f, _windowSize.x - 42f);
            float height = Math.Max(1f, _windowSize.y - 58f);
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            RectTransform title = BackgroundTransform?.Find(
                "TitleBackground")?.GetComponent<RectTransform>();
            if (title != null)
            {
                title.sizeDelta = new Vector2(_windowSize.x * .52f, 30f);
                title.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeRect != null)
            {
                nativeRect.sizeDelta = new Vector2(width, height);
                nativeRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect native = nativeScroll?.GetComponent<ScrollRect>();
            if (native != null)
            {
                native.horizontal = false;
                native.vertical = false;
            }
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null) viewport.sizeDelta =
                new Vector2(width, height);
            RectTransform content = ContentTransform as RectTransform;
            if (content != null) content.sizeDelta =
                new Vector2(width, height);
            Layout(_root, 0f, 0f, width, height);
            Layout(_header, 0f, 0f, width, HeaderHeight);
            Layout(_rulerName.rectTransform, 10f, 8f,
                Math.Max(60f, width - 238f), 22f);
            Layout(_rulerDetail.rectTransform, 10f, 35f,
                Math.Max(60f, width - 20f), 24f);
            Layout(_courtBack.GetComponent<RectTransform>(),
                Math.Max(10f, width - 222f), 8f, 102f, 24f);
            Layout(_kingdomBack.GetComponent<RectTransform>(),
                Math.Max(116f, width - 114f), 8f, 104f, 24f);
            Layout(_listRoot, 0f, HeaderHeight + 6f, width,
                Math.Max(1f, height - HeaderHeight - 6f));
            _chrome?.RepositionResizeHandle();
        }

        private static HouseholdRowView CreateRow(Transform pParent,
            string pName)
        {
            var row = new HouseholdRowView();
            row.Root = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            row.Root.transform.SetParent(pParent, false);
            row.Background = row.Root.GetComponent<Image>();
            row.Button = row.Root.GetComponent<Button>();
            row.Button.targetGraphic = row.Background;
            LayoutElement element = row.Root.GetComponent<LayoutElement>();
            element.minHeight = 58f;
            element.preferredHeight = 58f;
            row.PortraitRoot = new GameObject("Portrait",
                typeof(RectTransform));
            row.PortraitRoot.transform.SetParent(row.Root.transform, false);
            row.Name = CreateText(row.Root.transform, "Name", 10,
                TextAnchor.UpperLeft, FontStyle.Bold);
            row.Detail = CreateText(row.Root.transform, "Detail", 8,
                TextAnchor.UpperLeft, FontStyle.Normal);
            Layout(row.PortraitRoot.GetComponent<RectTransform>(), 7f, 7f,
                44f, 44f);
            RectTransform name = row.Name.rectTransform;
            name.anchorMin = new Vector2(0f, 1f);
            name.anchorMax = new Vector2(1f, 1f);
            name.pivot = new Vector2(0f, 1f);
            name.anchoredPosition = new Vector2(58f, -6f);
            name.sizeDelta = new Vector2(-66f, 20f);
            RectTransform detail = row.Detail.rectTransform;
            detail.anchorMin = new Vector2(0f, 1f);
            detail.anchorMax = new Vector2(1f, 1f);
            detail.pivot = new Vector2(0f, 1f);
            detail.anchoredPosition = new Vector2(58f, -25f);
            detail.sizeDelta = new Vector2(-66f, 30f);
            return row;
        }

        private static Text CreateSectionLabel(Transform pParent,
            string pName)
        {
            Text text = CreateText(pParent, pName, 9,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            LayoutElement element = text.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 24f;
            element.preferredHeight = 24f;
            text.color = new Color(1f, .82f, .42f, 1f);
            return text;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor, FontStyle pStyle)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.fontStyle = pStyle;
            text.alignment = pAnchor;
            text.color = new Color(.96f, .94f, .86f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(Transform pParent, string pName,
            UnityEngine.Events.UnityAction pAction, out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            pText = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleCenter, FontStyle.Normal);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(3f, 1f);
            pText.rectTransform.offsetMax = new Vector2(-3f, -1f);
            return button;
        }

        private static void CreateScrollArea(Transform pParent,
            out RectTransform pRoot, out RectTransform pContent,
            out ScrollRect pScroll)
        {
            pRoot = new GameObject("HouseholdList", typeof(RectTransform),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            pRoot.SetParent(pParent, false);
            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(Mask));
            viewport.transform.SetParent(pRoot, false);
            RectTransform viewportRect =
                viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-7f, 0f);
            viewport.GetComponent<Image>().color =
                new Color(.07f, .065f, .052f, .88f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            pContent = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter))
                .GetComponent<RectTransform>();
            pContent.SetParent(viewport.transform, false);
            pContent.anchorMin = new Vector2(0f, 1f);
            pContent.anchorMax = new Vector2(1f, 1f);
            pContent.pivot = new Vector2(.5f, 1f);
            pContent.anchoredPosition = Vector2.zero;
            pContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup group =
                pContent.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(4, 4, 3, 6);
            group.spacing = 3f;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            pContent.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            pScroll = pRoot.GetComponent<ScrollRect>();
            pScroll.viewport = viewportRect;
            pScroll.content = pContent;
            pScroll.horizontal = false;
            pScroll.vertical = true;
            pScroll.movementType = ScrollRect.MovementType.Clamped;
            pScroll.scrollSensitivity = 20f;
            DiplomacyConversationWindowScrollbar.Attach(pRoot, pScroll);
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }
    }
}
