using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class ConferredPosthumousTitleWindow :
        AbstractWindow<ConferredPosthumousTitleWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(520f, 340f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(720f, 520f);

        private static long _kingdomId = -1L;
        private static long _actorId = -1L;

        private Vector2 _windowSize = DefaultSize;
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private Image _portrait;
        private Text _identity;
        private Text _relationship;
        private Text _proposed;
        private RectTransform _viewport;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Scrollbar _scrollbar;
        private Text _meaning;
        private Text _career;
        private Text _deeds;
        private Text _scores;
        private Text _feedback;
        private Button _confirm;
        private Text _confirmText;
        private Button _cancel;
        private ConferredPosthumousPreview _preview;
        private bool _pending;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId, long pActorId)
        {
            _kingdomId = pKingdomId;
            _actorId = pActorId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.CONFERRED_POSTHUMOUS);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.CONFERRED_POSTHUMOUS,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyWindowLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize =>
                {
                    _windowSize = pSize;
                    ApplyWindowLayout();
                }, DefaultSize, MinimumSize, MaximumSize);
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (!_commandRefreshRequested) return;
            _commandRefreshRequested = false;
            _pending = false;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            if (_pending) _commandRefreshRequested = true;
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyWindowLayout();
            _preview = ConferredPosthumousTitleService.Prepare(
                _kingdomId, _actorId);
            Sprite portrait = FamilyTreeNodeView.BuildArchivedPortrait(
                _actorId);
            _portrait.sprite = portrait;
            _portrait.enabled = portrait != null;
            _portrait.preserveAspect = true;

            _identity.text = string.IsNullOrEmpty(_preview.ActorName)
                ? AW_L10n.Text("aw_conferred_unknown_actor", "Unknown person")
                : _preview.ActorName;
            _relationship.text = AW_L10n.Text(
                                     "aw_conferred_relationship", "Relationship") +
                                 ": " + _preview.RelationshipLabel;
            _proposed.text = _preview.Result ==
                             ConferredPosthumousResult.Success
                ? AW_L10n.Text("aw_conferred_proposed", "Proposed title") +
                  ": " + _preview.DisplayTitle
                : AW_L10n.Text("aw_conferred_unavailable",
                    "Conferment unavailable");
            _meaning.text = AW_L10n.Text(
                                "aw_conferred_title_meaning", "Title meaning") +
                            "\n" + _preview.TitleMeaning;
            _career.text = BuildCareerText(_preview);
            _deeds.text = AW_L10n.Text(
                              "aw_conferred_major_deeds", "Major deeds") +
                          "\n" + _preview.MajorDeeds;
            _scores.text = BuildScoreText(_preview);
            _feedback.text = ResultText(_preview);
            _scroll.verticalNormalizedPosition = 1f;
            RenderButtons();
        }

        private void Confirm()
        {
            if (_pending || _preview?.CanCommit != true) return;
            _pending = true;
            RenderButtons();
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.ConferPosthumousTitle(
                        _kingdomId, _actorId, _preview.PreviewToken));
            if (result.Status == AW3CommandStatus.Accepted)
            {
                HistoryListWindow.RefreshPersonAfterConferment(
                    _actorId, _kingdomId);
                GetComponent<ScrollWindow>()?.clickHide();
                return;
            }
            if (result.Status == AW3CommandStatus.Pending) return;
            _pending = false;
            _preview = ConferredPosthumousTitleService.Prepare(
                _kingdomId, _actorId);
            ConferredPosthumousResult failure = Enum.IsDefined(
                    typeof(ConferredPosthumousResult), result.DetailCode)
                ? (ConferredPosthumousResult)result.DetailCode
                : ConferredPosthumousResult.PersistenceFailed;
            _feedback.text = ResultText(failure,
                _preview?.CooldownRemaining ?? 0);
            RenderButtons();
        }

        private void Cancel()
        {
            if (_pending) return;
            GetComponent<ScrollWindow>()?.clickHide();
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

            var rootObject = new GameObject(
                "ConferredPosthumousRoot", typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();

            var portraitObject = new GameObject("ArchivedPortrait",
                typeof(RectTransform), typeof(Image));
            portraitObject.transform.SetParent(_root, false);
            _portrait = portraitObject.GetComponent<Image>();
            _portrait.color = Color.white;

            _identity = CreateText(_root, "Identity", 13,
                TextAnchor.MiddleLeft, new Color(1f, 0.84f, 0.42f, 1f));
            _identity.fontStyle = FontStyle.Bold;
            _relationship = CreateText(_root, "Relationship", 9,
                TextAnchor.MiddleLeft, new Color(0.88f, 0.86f, 0.78f, 1f));
            _proposed = CreateText(_root, "ProposedTitle", 11,
                TextAnchor.MiddleLeft, new Color(0.95f, 0.78f, 0.32f, 1f));
            _proposed.fontStyle = FontStyle.Bold;

            BuildScroller();
            _meaning = CreateText(_content, "Meaning", 9,
                TextAnchor.UpperLeft, Color.white);
            _career = CreateText(_content, "Career", 9,
                TextAnchor.UpperLeft, Color.white);
            _deeds = CreateText(_content, "Deeds", 9,
                TextAnchor.UpperLeft, Color.white);
            _scores = CreateText(_content, "Scores", 9,
                TextAnchor.UpperLeft, new Color(0.92f, 0.86f, 0.68f, 1f));
            _feedback = CreateText(_root, "Feedback", 8,
                TextAnchor.MiddleLeft, new Color(1f, 0.58f, 0.48f, 1f));

            _confirm = CreateButton(_root, "Confirm",
                AW_L10n.Text("aw_conferred_confirm", "Issue decree"),
                Confirm, out _confirmText);
            _cancel = CreateButton(_root, "Cancel",
                AW_L10n.Text("aw_title_cancel", "Cancel"),
                Cancel, out _);
        }

        private void BuildScroller()
        {
            var viewportObject = new GameObject("DetailsViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect));
            viewportObject.transform.SetParent(_root, false);
            _viewport = viewportObject.GetComponent<RectTransform>();
            Image panel = viewportObject.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(panel, 0.82f);
            panel.raycastTarget = true;

            var contentObject = new GameObject("DetailsContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(_viewport, false);
            _content = contentObject.GetComponent<RectTransform>();
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
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

        private void ApplyWindowLayout()
        {
            if (_root == null) return;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background =
                BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find(
                "TitleBackground");
            RectTransform titleRect =
                titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(
                    _windowSize.x * 0.58f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_conferred_window_title", "Confer Posthumous Title");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            DisableNativeScroll(width, height);

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);

            float portraitSize = height < 240f ? 58f : 72f;
            SetRect(_portrait.rectTransform, 8f, 6f,
                portraitSize, portraitSize);
            float textX = 16f + portraitSize;
            SetRect(_identity.rectTransform, textX, 6f,
                width - textX - 8f, 23f);
            SetRect(_relationship.rectTransform, textX, 30f,
                width - textX - 8f, 20f);
            SetRect(_proposed.rectTransform, textX, 51f,
                width - textX - 8f, 24f);

            float detailsTop = 8f + portraitSize + 5f;
            float footer = 58f;
            float detailsHeight = Mathf.Max(92f,
                height - detailsTop - footer);
            SetRect(_viewport, 8f, detailsTop,
                width - 28f, detailsHeight);
            SetRect(_scrollbar.GetComponent<RectTransform>(),
                width - 17f, detailsTop, 8f, detailsHeight);
            float innerWidth = Mathf.Max(1f, width - 42f);
            float contentHeight = 286f;
            _content.sizeDelta = new Vector2(innerWidth,
                Mathf.Max(detailsHeight, contentHeight));
            SetRect(_meaning.rectTransform, 8f, 6f,
                innerWidth - 16f, 52f);
            SetRect(_career.rectTransform, 8f, 62f,
                innerWidth - 16f, 58f);
            SetRect(_deeds.rectTransform, 8f, 124f,
                innerWidth - 16f, 68f);
            SetRect(_scores.rectTransform, 8f, 196f,
                innerWidth - 16f, 82f);

            float feedbackTop = detailsTop + detailsHeight + 4f;
            SetRect(_feedback.rectTransform, 8f, feedbackTop,
                Mathf.Max(1f, width - 190f), 24f);
            SetRect(_confirm.GetComponent<RectTransform>(),
                width - 174f, height - 29f, 78f, 24f);
            SetRect(_cancel.GetComponent<RectTransform>(),
                width - 88f, height - 29f, 78f, 24f);
            _chrome?.RepositionResizeHandle();
        }

        private void DisableNativeScroll(float pWidth, float pHeight)
        {
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeRect != null)
            {
                nativeRect.sizeDelta = new Vector2(pWidth, pHeight);
                nativeRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeComponent =
                nativeScroll?.GetComponent<ScrollRect>();
            if (nativeComponent != null)
            {
                nativeComponent.horizontal = false;
                nativeComponent.vertical = false;
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
            RectTransform nativeViewport =
                ContentTransform?.parent as RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(pWidth, pHeight);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private void RenderButtons()
        {
            bool canConfirm = !_pending && _preview?.CanCommit == true;
            _confirm.interactable = canConfirm;
            _cancel.interactable = !_pending;
            _confirmText.text = _pending
                ? AW_L10n.Text("aw_title_committing", "Committing")
                : AW_L10n.Text("aw_conferred_confirm", "Issue decree");
            AW_UIStyle.ApplyButton(_confirm.GetComponent<Image>(),
                canConfirm ? 0.98f : 0.45f);
        }

        private static string BuildCareerText(
            ConferredPosthumousPreview pPreview)
        {
            string office = string.IsNullOrEmpty(pPreview.HighestOfficeLabel)
                ? AW_L10n.Text("aw_conferred_none", "None")
                : pPreview.HighestOfficeLabel;
            string noble = string.IsNullOrEmpty(pPreview.NobleTitleLabel)
                ? AW_L10n.Text("aw_conferred_none", "None")
                : pPreview.NobleTitleLabel;
            return AW_L10n.Text("aw_conferred_highest_office",
                       "Highest office") + ": " + office + "\n" +
                   AW_L10n.Text("aw_conferred_noble_title",
                       "Noble title") + ": " + noble;
        }

        private static string BuildScoreText(
            ConferredPosthumousPreview pPreview)
        {
            return AW_L10n.Text("aw_hist_posthumous_civil_label", "Civil") +
                   pPreview.CivilScore + "    " +
                   AW_L10n.Text("aw_hist_posthumous_territory_label",
                       "Territory") + pPreview.TerritoryScore + "\n" +
                   AW_L10n.Text("aw_hist_posthumous_war_label", "War") +
                   pPreview.WarScore + "    " +
                   AW_L10n.Text("aw_hist_posthumous_order_label", "Order") +
                   pPreview.OrderScore + "\n" +
                   AW_L10n.Text("aw_hist_posthumous_ending_label", "Ending") +
                   pPreview.EndingScore + "    " +
                   AW_L10n.Text("aw_hist_posthumous_total_label", "Total") +
                   pPreview.TotalScore;
        }

        private static string ResultText(
            ConferredPosthumousPreview pPreview)
        {
            return pPreview == null
                ? ResultText(ConferredPosthumousResult.PersistenceFailed, 0)
                : ResultText(pPreview.Result, pPreview.CooldownRemaining);
        }

        private static string ResultText(ConferredPosthumousResult pResult,
            int pCooldown)
        {
            if (pResult == ConferredPosthumousResult.Success) return "";
            if (pResult == ConferredPosthumousResult.Cooldown)
                return string.Format(AW_L10n.Text(
                        "aw_conferred_result_cooldown",
                        "The realm must wait {0} more years"), pCooldown);
            string key = pResult switch
            {
                ConferredPosthumousResult.InvalidKingdom =>
                    "aw_conferred_result_invalid_kingdom",
                ConferredPosthumousResult.MissingContext =>
                    "aw_conferred_result_missing_context",
                ConferredPosthumousResult.MissingArchive =>
                    "aw_conferred_result_missing_archive",
                ConferredPosthumousResult.TargetLiving =>
                    "aw_conferred_result_target_living",
                ConferredPosthumousResult.NoHistoricalRelationship =>
                    "aw_conferred_result_no_relationship",
                ConferredPosthumousResult.AlreadyTitled =>
                    "aw_conferred_result_already_titled",
                ConferredPosthumousResult.NoTitleAvailable =>
                    "aw_conferred_result_no_title",
                ConferredPosthumousResult.StalePreview =>
                    "aw_conferred_result_stale",
                _ => "aw_conferred_result_persistence_failed"
            };
            return AW_L10n.Text(key, "Conferment is unavailable");
        }

        private static Scrollbar CreateScrollbar(Transform pParent,
            ScrollRect pScroll)
        {
            var barObject = new GameObject("ConferredScrollbar",
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

        private static Button CreateButton(Transform pParent, string pName,
            string pLabel, Action pAction, out Text pText)
        {
            var buttonObject = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(buttonObject.GetComponent<Image>(), 0.98f);
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => pAction?.Invoke());
            pText = CreateText(buttonObject.transform, "Text", 9,
                TextAnchor.MiddleCenter, Color.white);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(3f, 1f);
            pText.rectTransform.offsetMax = new Vector2(-3f, -1f);
            pText.text = pLabel ?? "";
            return button;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor, Color pColor)
        {
            var textObject = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(pParent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = pColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
            return text;
        }

        private static void SetRect(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(
                Mathf.Max(1f, pWidth), Mathf.Max(1f, pHeight));
        }
    }
}
