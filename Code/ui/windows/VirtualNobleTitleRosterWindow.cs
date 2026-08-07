using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class VirtualNobleTitleRosterWindow : AbstractWindow<VirtualNobleTitleRosterWindow>
    {
        private const float RowHeight = 50f;
        private const float RowGap = 4f;
        private static readonly Vector2 DefaultSize = new Vector2(580f, 420f);
        private static readonly Vector2 MinimumSize = new Vector2(560f, 300f);
        private static readonly Vector2 MaximumSize = new Vector2(880f, 680f);
        private static long _kingdomId = -1L;
        private Vector2 _windowSize = DefaultSize;
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private RectTransform _viewport;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Scrollbar _scrollbar;
        private Text _header;
        private readonly List<GameObject> _rows = new List<GameObject>();

        private sealed class RosterEntry
        {
            internal RosterEntry(Actor pActor, string pDisplayTitle,
                bool pIsVirtual, long pTitleId)
            {
                Actor = pActor;
                DisplayTitle = pDisplayTitle ?? "";
                IsVirtual = pIsVirtual;
                TitleId = pTitleId;
            }

            internal Actor Actor { get; }
            internal string DisplayTitle { get; }
            internal bool IsVirtual { get; }
            internal long TitleId { get; }
        }

        internal static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.VIRTUAL_TITLES);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.VIRTUAL_TITLES,
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
                }, DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable() => Refresh();

        private void Refresh()
        {
            EnsureUi();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (_header != null)
                _header.text = kingdom?.data != null
                    ? kingdom.name + " - " + AW_L10n.Text("aw_virtual_titles", "Title Holders")
                    : AW_L10n.Text("aw_virtual_titles", "Title Holders");
            ClearRows();
            if (kingdom?.data == null || kingdom.isRekt()) return;

            var rows = new List<RosterEntry>();
            try
            {
                foreach (Actor actor in kingdom.units)
                {
                    if (!IsLiveActor(actor)) continue;
                    string formalTitle = NobleRankService.GetDisplayTitle(actor);
                    if (!string.IsNullOrWhiteSpace(formalTitle))
                        rows.Add(new RosterEntry(actor, formalTitle, false, -1L));
                }
            }
            catch { }

            foreach (VirtualNobleTitleSnapshot title in
                     VirtualNobleTitleService.GetActiveForKingdom(kingdom.id))
            {
                Actor actor = World.world?.units?.get(title.ActorId);
                if (!IsLiveActor(actor)) continue;
                rows.Add(new RosterEntry(actor, title.Text, true, title.TitleId));
            }

            string kingdomColor = kingdom.getColor()?.color_text ?? "";
            foreach (RosterEntry row in rows
                .OrderBy(p => p.IsVirtual ? 1 : 0)
                .ThenBy(p => p.DisplayTitle, StringComparer.Ordinal)
                .ThenBy(p => p.Actor?.data?.id ?? -1L))
                AddRow(row, kingdom.id, kingdomColor);
            LayoutRows();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private static bool IsLiveActor(Actor pActor)
        {
            return pActor?.data != null && !pActor.isRekt() && pActor.isAlive();
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            ContentSizeFitter fitter =
                ContentTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            var rootObject = new GameObject("VirtualNobleTitleRosterRoot", typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _header = CreateText(_root, "Header", 12, TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.42f, 1f));
            BuildScroller();
        }

        private void BuildScroller()
        {
            var viewportObject = new GameObject("RosterViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect));
            viewportObject.transform.SetParent(_root, false);
            _viewport = viewportObject.GetComponent<RectTransform>();
            Image panel = viewportObject.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(panel, 0.82f);
            panel.raycastTarget = true;

            var contentObject = new GameObject("Rows", typeof(RectTransform));
            contentObject.transform.SetParent(_viewport, false);
            _content = contentObject.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);

            _scroll = viewportObject.GetComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 18f;
            _scrollbar = CreateScrollbar(_root, _scroll);
        }

        private void AddRow(RosterEntry pEntry, long pKingdomId,
            string pKingdomColor)
        {
            var row = new GameObject("TitleHolder", typeof(RectTransform), typeof(Image));
            row.transform.SetParent(_content, false);
            AW_UIStyle.ApplyListRow(row.GetComponent<Image>(), 0.95f);
            UiUnitAvatarElement avatar = BuildPortrait(row.transform);
            if (avatar != null)
            {
                avatar.enabled = true;
                if (avatar.avatarLoader != null)
                    avatar.avatarLoader.enabled = true;
                avatar.show(pEntry.Actor);
            }
            Text titleText = CreateText(row.transform, "CeremonialTitle", 9,
                TextAnchor.MiddleLeft, ResolveKingdomTextColor(pKingdomColor));
            titleText.fontStyle = FontStyle.Bold;
            titleText.text = pEntry.DisplayTitle;
            SetRect(titleText, 68f, 2f, 110f, 20f);
            titleText.transform.SetAsFirstSibling();

            Text identity = CreateText(row.transform, "Identity", 8,
                TextAnchor.MiddleLeft, Color.white);
            identity.text = pEntry.Actor?.getName() ??
                AW_L10n.Text("aw_unknown_actor", "Unknown actor");
            SetRect(identity, 68f, 25f, 110f, 20f);
            Button actorButton = identity.gameObject.AddComponent<Button>();
            actorButton.targetGraphic = identity;
            actorButton.onClick.AddListener(() =>
            {
                if (pEntry.Actor?.data != null && !pEntry.Actor.isRekt())
                    ActionLibrary.openUnitWindow(pEntry.Actor);
            });

            if (!pEntry.IsVirtual)
            {
                _rows.Add(row);
                return;
            }

            Text inputLabel = CreateText(row.transform, "TitleLabel", 8,
                TextAnchor.MiddleLeft, ResolveKingdomTextColor(pKingdomColor));
            inputLabel.text = AW_L10n.Text("aw_ruler_appellation", "礼制称呼");
            SetRect(inputLabel, 188f, 2f, 116f, 18f);
            InputField input = BuildInput(row.transform, pEntry.DisplayTitle);
            SetRect(input, 188f, 24f, 116f, 21f);
            Button edit = BuildActionButton(row.transform, "Edit",
                AW_L10n.Text("aw_virtual_title_edit", "Edit"), () =>
            {
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.EditVirtualNobleTitle(pKingdomId,
                        pEntry.TitleId, input.text));
                Refresh();
            });
            SetRect(edit, 310f, 24f, 48f, 21f);
            Button delete = BuildActionButton(row.transform, "Delete",
                AW_L10n.Text("aw_virtual_title_delete", "Delete"), () =>
            {
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.DeleteVirtualNobleTitle(pKingdomId,
                        pEntry.TitleId));
                Refresh();
            });
            SetRect(delete, 364f, 24f, 52f, 21f);
            _rows.Add(row);
        }

        private static UiUnitAvatarElement BuildPortrait(Transform pParent)
        {
            var holder = new GameObject("PortraitSlot", typeof(RectTransform));
            holder.transform.SetParent(pParent, false);
            SetRect(holder.GetComponent<RectTransform>(), 8f, 5f, 40f, 40f);

            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab == null) return null;

            UiUnitAvatarElement avatar = UnityEngine.Object.Instantiate(
                prefab, holder.transform);
            RectTransform rect = avatar.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(40f, 40f);
                rect.localScale = Vector3.one;
            }
            return avatar;
        }

        private static InputField BuildInput(Transform parent, string value)
        {
            var obj = new GameObject("TitleInput", typeof(RectTransform),
                typeof(Image), typeof(InputField));
            obj.transform.SetParent(parent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.82f);
            Text text = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleLeft, Color.white);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4f, 0f);
            text.rectTransform.offsetMax = new Vector2(-4f, 0f);
            InputField input = obj.GetComponent<InputField>();
            input.textComponent = text;
            input.targetGraphic = obj.GetComponent<Image>();
            input.text = value ?? "";
            input.characterLimit = VirtualNobleTitleRules.MaximumTitleLength;
            input.lineType = InputField.LineType.SingleLine;
            input.readOnly = false;
            input.interactable = true;
            text.raycastTarget = false;
            return input;
        }

        private static Button BuildActionButton(Transform parent, string name,
            string label, Action action)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(Button));
            obj.transform.SetParent(parent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.95f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => action?.Invoke());
            Text text = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleCenter, Color.white);
            text.text = label;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private void ClearRows()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            _rows.Clear();
        }

        private void LayoutRows()
        {
            if (_content == null) return;
            float y = 0f;
            for (int i = 0; i < _rows.Count; i++)
            {
                RectTransform row = _rows[i]?.GetComponent<RectTransform>();
                if (row == null) continue;
                row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
                row.pivot = new Vector2(0f, 1f);
                row.anchoredPosition = new Vector2(0f, -y);
                row.sizeDelta = new Vector2(
                    Mathf.Max(1f, _content.sizeDelta.x), RowHeight);
                y += RowHeight + RowGap;
            }
            float viewportHeight = _viewport?.rect.height ?? 30f;
            _content.sizeDelta = new Vector2(
                Mathf.Max(1f, _content.sizeDelta.x),
                Mathf.Max(viewportHeight, y));
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform bg = BackgroundTransform?.GetComponent<RectTransform>();
            if (bg != null) bg.sizeDelta = _windowSize;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.56f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text("aw_virtual_titles", "Title Holders");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                window.titleText.raycastTarget = false;
            }
            DisableNativeScroll(width, height);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            SetRect(_header, 10f, 8f, width - 20f, 28f);
            float listHeight = Mathf.Max(30f, height - 52f);
            SetRect(_viewport, 10f, 42f, width - 32f, listHeight);
            SetRect(_scrollbar.GetComponent<RectTransform>(), width - 17f,
                42f, 8f, listHeight);
            _content.sizeDelta = new Vector2(
                Mathf.Max(1f, width - 46f), _content.sizeDelta.y);
            LayoutRows();
            _chrome?.RepositionResizeHandle();
        }

        private void DisableNativeScroll(float pWidth, float pHeight)
        {
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeRect = nativeScroll?.GetComponent<RectTransform>();
            if (nativeRect != null)
            {
                nativeRect.sizeDelta = new Vector2(pWidth, pHeight);
                nativeRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect native = nativeScroll?.GetComponent<ScrollRect>();
            if (native != null)
            {
                native.horizontal = false;
                native.vertical = false;
            }
            Transform nativeScrollbar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (nativeScrollbar != null)
                foreach (Graphic graphic in
                         nativeScrollbar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null) viewport.sizeDelta = new Vector2(pWidth, pHeight);
            RectTransform content = ContentTransform as RectTransform;
            if (content != null) content.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static Scrollbar CreateScrollbar(Transform pParent,
            ScrollRect pScroll)
        {
            var barObject = new GameObject("RosterScrollbar",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(pParent, false);
            barObject.GetComponent<Image>().color =
                new Color(0.08f, 0.075f, 0.065f, 0.98f);
            var slidingObject = new GameObject("Sliding Area",
                typeof(RectTransform));
            slidingObject.transform.SetParent(barObject.transform, false);
            RectTransform sliding = slidingObject.GetComponent<RectTransform>();
            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = new Vector2(1f, 1f);
            sliding.offsetMax = new Vector2(-1f, -1f);
            var handleObject = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handleObject.transform.SetParent(sliding, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.76f, 0.61f, 0.28f, 1f);
            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static Text CreateText(Transform parent, string name, int size,
            TextAnchor anchor, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Color ResolveKingdomTextColor(string pColorText)
        {
            if (ColorUtility.TryParseHtmlString(pColorText ?? "",
                    out Color color))
            {
                color.a = 1f;
                return color;
            }
            return new Color(1f, 0.84f, 0.42f, 1f);
        }

        private static void SetRect(Component pComponent, float x, float y,
            float width, float height)
        {
            RectTransform rect = pComponent?.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
