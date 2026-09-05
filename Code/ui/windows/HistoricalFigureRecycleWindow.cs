using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    /// Standalone card trade-up station. Selection is transient and the store
    /// remains the only owner of the input/output transaction.
    /// </summary>
    internal sealed class HistoricalFigureRecycleWindow :
        AbstractWindow<HistoricalFigureRecycleWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(550f, 410f);
        private static readonly Vector2 MinimumSize = new Vector2(480f, 350f);
        private static readonly Vector2 MaximumSize = new Vector2(760f, 620f);
        private const int SlotColumns = 3;
        private const float SlotGap = 5f;
        private const int CardColumns = 3;
        private const int PageSize = 20;
        private const float CardWidth = 88f;
        private const float CardHeight = 64f;
        private const float CardRowGap = 8f;

        private static HistoricalFigureCardCollectionStore Store =>
            HistoricalFigureCardRuntimeService.Collection;

        private readonly HistoricalFigureCardRecycleSelectionState _selection =
            new HistoricalFigureCardRecycleSelectionState();
        private readonly List<GameObject> _cardButtons = new List<GameObject>();
        private readonly List<Button> _slotButtons = new List<Button>();
        private Vector2 _windowSize = DefaultSize;
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private RectTransform _listViewport;
        private RectTransform _listContent;
        private ScrollRect _listScroll;
        private Scrollbar _listScrollbar;
        private RectTransform _slotPanel;
        private Text _status;
        private Text _summary;
        private Text _preview;
        private Text _result;
        private Image _resultPortrait;
        private HistoricalFigureCardDefinition _lastOutput;
        private string _lastOutputCrateId = "";
        private Button _submit;
        private Button _reset;
        private Button _back;
        private Button _previousPage;
        private Button _nextPage;
        private Text _pageLabel;
        private int _page;
        private bool _built;

        public static void Open()
        {
            HistoricalFigureCardRuntimeService.Initialize();
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.HISTORICAL_FIGURE_CARD_RECYCLE);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.HISTORICAL_FIGURE_CARD_RECYCLE,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            BuildUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize => { _windowSize = pSize; ApplyLayout(); },
                DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_historical_figure_cards_recycle_title",
                    "历史人物汰换");
                window.titleText.raycastTarget = false;
            }
            Refresh();
        }

        private void BuildUi()
        {
            if (_built || ContentTransform == null) return;
            _built = true;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            ContentSizeFitter fitter = ContentTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            GameObject root = new GameObject("HistoricalFigureRecycleRoot",
                typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();

            _status = MakeText("Status", _root, 12, TextAnchor.UpperLeft,
                Color.white);
            _summary = MakeText("Summary", _root, 8, TextAnchor.UpperLeft,
                new Color(.82f, .82f, .82f, 1f));
            _preview = MakeText("Preview", _root, 8, TextAnchor.UpperLeft,
                new Color(.95f, .82f, .42f, 1f));
            _result = MakeText("Result", _root, 8, TextAnchor.UpperLeft,
                new Color(.72f, .88f, .72f, 1f));
            _resultPortrait = ChildImage(_root, "ResultPortrait");
            _resultPortrait.preserveAspect = true;
            _resultPortrait.raycastTarget = false;

            GameObject viewport = new GameObject("RecycleCardViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect));
            viewport.transform.SetParent(_root, false);
            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(.055f, .06f, .07f, .96f);
            viewportImage.raycastTarget = true;
            _listViewport = viewport.GetComponent<RectTransform>();
            _listScroll = viewport.GetComponent<ScrollRect>();
            _listScroll.horizontal = false;
            _listScroll.vertical = true;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScroll.scrollSensitivity = 24f;

            GameObject content = new GameObject("RecycleCardContent",
                typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _listContent = content.GetComponent<RectTransform>();
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(0f, 1f);
            _listContent.pivot = new Vector2(0f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listScroll.viewport = _listViewport;
            _listScroll.content = _listContent;
            _listScrollbar = CreateScrollbar(_root);
            _listScroll.verticalScrollbar = _listScrollbar;
            _listScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            _previousPage = MakeButton("PreviousPage", _root,
                Text("aw_historical_figure_cards_previous_page", "上一页"),
                () => ChangePage(-1));
            _pageLabel = MakeText("PageLabel", _root, 7,
                TextAnchor.MiddleCenter, Color.white);
            _nextPage = MakeButton("NextPage", _root,
                Text("aw_historical_figure_cards_next_page", "下一页"),
                () => ChangePage(1));

            GameObject slots = new GameObject("RecycleSlots", typeof(RectTransform),
                typeof(Image));
            slots.transform.SetParent(_root, false);
            _slotPanel = slots.GetComponent<RectTransform>();
            slots.GetComponent<Image>().color = new Color(.09f, .08f, .07f, .96f);
            for (int i = 0; i < 10; i++)
            {
                int slotIndex = i;
                Button button = MakeButton("Slot" + i, _slotPanel, "",
                    () => RemoveSlot(slotIndex));
                _slotButtons.Add(button);
            }

            _submit = MakeButton("Submit", _root, "", Submit);
            _reset = MakeButton("Reset", _root, "", ResetSelection);
            _back = MakeButton("Back", _root, "", BackToInventory);
        }

        private void Refresh()
        {
            BuildUi();
            Store.Load();
            ApplyLayout();
            IReadOnlyList<HistoricalFigureCardDefinition> cards =
                HistoricalFigureCardRecycleSelectionRules.FilterVisible(
                    HistoricalFigureCardCatalog.All, Store.OwnedCounts,
                    _selection.LockedRarity);
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(
                cards.Count / (float)PageSize));
            _page = Mathf.Clamp(_page, 0, totalPages - 1);
            IReadOnlyList<HistoricalFigureCardDefinition> pageCards = cards
                .Skip(_page * PageSize)
                .Take(PageSize)
                .ToArray();
            UpdatePagination(totalPages);
            RenderCards(pageCards);
            RenderSlots();
            int required = HistoricalFigureCardRecycleSelectionRules.RequiredCount(
                _selection.LockedRarity);
            _status.text = Text("aw_historical_figure_cards_recycle_title",
                "历史人物汰换");
            _summary.text = Format("aw_historical_figure_cards_recycle_summary",
                "已选择 {0}/{1}", _selection.SlotCardIds.Count, required);
            HistoricalFigureCardRarity output =
                HistoricalFigureCardRecycleRules.NextRarity(
                    _selection.LockedRarity);
            _preview.text = output == null
                ? Text("aw_historical_figure_cards_recycle_next_empty", "选择同品质卡片")
                : Format("aw_historical_figure_cards_recycle_next",
                    "下一品质：{0}", RarityName(output));
            _submit.interactable = required > 0 &&
                _selection.SlotCardIds.Count == required;
            SetButtonText(_submit, Format(
                "aw_historical_figure_cards_recycle_submit", "汰换 {0}/{1}",
                _selection.SlotCardIds.Count, required));
            SetButtonText(_reset, Text(
                "aw_historical_figure_cards_recycle_cancel", "取消"));
            SetButtonText(_back, Text(
                "aw_historical_figure_cards_recycle_back", "返回仓库"));
            RenderResult();
        }

        private void ChangePage(int pDelta)
        {
            IReadOnlyList<HistoricalFigureCardDefinition> cards =
                HistoricalFigureCardRecycleSelectionRules.FilterVisible(
                    HistoricalFigureCardCatalog.All, Store.OwnedCounts,
                    _selection.LockedRarity);
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(
                cards.Count / (float)PageSize));
            _page = Mathf.Clamp(_page + pDelta, 0, totalPages - 1);
            Refresh();
        }

        private void UpdatePagination(int pTotalPages)
        {
            _pageLabel.text = Format("aw_historical_figure_cards_page", "{0}/{1}",
                _page + 1, pTotalPages);
            _previousPage.interactable = _page > 0;
            _nextPage.interactable = _page < pTotalPages - 1;
        }

        private void RenderResult()
        {
            if (_lastOutput == null)
            {
                _result.text = "";
                _resultPortrait.gameObject.SetActive(false);
                return;
            }
            _result.text = Format("aw_historical_figure_cards_recycle_success",
                "获得：{0}  来源：{1}", _lastOutput.DisplayName,
                SourceCrateName(_lastOutputCrateId)) + "\n" +
                Format("aw_historical_figure_cards_recycle_result_details",
                    "{0} [{1}]  {2}\n{3}", _lastOutput.DisplayName,
                    RarityName(_lastOutput.Rarity),
                    _lastOutput.HistoricalKingdomName,
                    _lastOutput.DetailedBiography);
            _resultPortrait.gameObject.SetActive(true);
            _resultPortrait.sprite = string.IsNullOrEmpty(_lastOutput.PortraitPath)
                ? null : SpriteTextureLoader.getSprite(_lastOutput.PortraitPath);
            _resultPortrait.sprite = _resultPortrait.sprite ??
                SpriteTextureLoader.getSprite("ui/icons/iconKings");
        }

        private void RenderCards(IReadOnlyList<HistoricalFigureCardDefinition> pCards)
        {
            foreach (GameObject card in _cardButtons)
                card.SetActive(false);
            for (int i = 0; i < pCards.Count; i++)
            {
                HistoricalFigureCardDefinition card = pCards[i];
                GameObject buttonObject;
                if (i < _cardButtons.Count)
                {
                    buttonObject = _cardButtons[i];
                    buttonObject.SetActive(true);
                }
                else
                {
                    buttonObject = CreateCardButton(i);
                    _cardButtons.Add(buttonObject);
                }
                BindCard(buttonObject, card);
                Position(buttonObject.GetComponent<RectTransform>(),
                    (i % CardColumns) * (CardWidth + 6f) + 4f,
                    -((i / CardColumns) * (CardHeight + CardRowGap) + 4f),
                    CardWidth, CardHeight);
            }
            _listContent.sizeDelta = new Vector2(
                Mathf.Max(1f, _listViewport.rect.width - 12f),
                Mathf.Max(1f, Mathf.Ceil(pCards.Count / (float)CardColumns) *
                    (CardHeight + CardRowGap) + 4f));
            _listScroll.StopMovement();
        }

        private GameObject CreateCardButton(int pIndex)
        {
            GameObject obj = new GameObject("Card" + pIndex,
                typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(_listContent, false);
            Image image = obj.GetComponent<Image>();
            image.raycastTarget = true;
            Button button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            return obj;
        }

        private void BindCard(GameObject pObject,
            HistoricalFigureCardDefinition pCard)
        {
            Button button = pObject.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (HistoricalFigureCardRecycleSelectionRules.TryAdd(
                    _selection, pCard, Store.OwnedCounts, out string error))
                {
                    HistoricalFigureCardAudioService.PlayButtonPress();
                    _result.text = "";
                }
                else
                    _result.text = LocalizedError(error);
                Refresh();
            });
            Image image = pObject.GetComponent<Image>();
            image.sprite = GradientSprite(ParseColor(pCard.Rarity?.ColorHex,
                new Color(.3f, .42f, 1f, 1f)));
            image.color = Color.white;
            int remaining = Math.Max(0, Store.GetOwnedCount(pCard.CardId) -
                GetSelectedCount(pCard.CardId));
            button.interactable = remaining > 0;
            Text name = ChildText(pObject.transform, "Name", 7);
            name.text = pCard.DisplayName ?? "-";
            Position(name.rectTransform, 3f, -48f, 74f, 13f);
            Text meta = ChildText(pObject.transform, "Meta", 6);
            meta.text = "x" + remaining + "  " +
                RarityName(pCard.Rarity);
            Position(meta.rectTransform, 3f, 3f, 74f, 12f);
            Image portrait = ChildImage(pObject.transform, "Portrait");
            portrait.sprite = string.IsNullOrEmpty(pCard.PortraitPath) ? null :
                SpriteTextureLoader.getSprite(pCard.PortraitPath);
            portrait.sprite = portrait.sprite ?? SpriteTextureLoader.getSprite(
                "ui/icons/iconKings");
            portrait.preserveAspect = true;
            Position(portrait.rectTransform, 8f, -7f, 64f, 34f);
        }

        private int GetSelectedCount(string pCardId)
        {
            return _selection.SlotCardIds.Count(id =>
                string.Equals(id, pCardId, StringComparison.Ordinal));
        }

        private void RenderSlots()
        {
            for (int i = 0; i < _slotButtons.Count; i++)
            {
                Button button = _slotButtons[i];
                bool occupied = i < _selection.SlotCardIds.Count;
                button.gameObject.SetActive(true);
                button.interactable = occupied;
                Image image = button.GetComponent<Image>();
                HistoricalFigureCardDefinition card = occupied
                    ? HistoricalFigureCardCatalog.Get(_selection.SlotCardIds[i])
                    : null;
                image.sprite = card == null ? WhiteSprite() : GradientSprite(
                    ParseColor(card.Rarity?.ColorHex, Color.white));
                image.color = card == null
                    ? new Color(.15f, .15f, .16f, .9f) : Color.white;
                float slotSize = Mathf.Max(32f,
                    button.GetComponent<RectTransform>().sizeDelta.x);
                Text label = ChildText(button.transform, "SlotLabel", 7);
                label.text = card == null ? (i + 1).ToString() : card.DisplayName;
                Position(label.rectTransform, 2f, -slotSize + 2f,
                    slotSize - 4f, 12f);
                Image portrait = ChildImage(button.transform, "SlotPortrait");
                portrait.sprite = card == null ? null :
                    SpriteTextureLoader.getSprite(card.PortraitPath) ??
                    SpriteTextureLoader.getSprite("ui/icons/iconKings");
                portrait.preserveAspect = true;
                Position(portrait.rectTransform, 4f, -3f,
                    slotSize - 8f, Mathf.Max(18f, slotSize - 17f));
            }
        }

        private void RemoveSlot(int pIndex)
        {
            if (pIndex < 0 || pIndex >= _selection.SlotCardIds.Count) return;
            HistoricalFigureCardRecycleSelectionRules.RemoveAt(_selection,
                pIndex);
            Refresh();
        }

        private void ResetSelection()
        {
            HistoricalFigureCardRecycleSelectionRules.Clear(_selection);
            _page = 0;
            _result.text = "";
            _lastOutput = null;
            _lastOutputCrateId = "";
            Refresh();
        }

        private void Submit()
        {
            int required = HistoricalFigureCardRecycleSelectionRules.RequiredCount(
                _selection.LockedRarity);
            if (required <= 0 || _selection.SlotCardIds.Count != required)
            {
                _result.text = LocalizedError("recycle_incomplete");
                return;
            }
            var inputs = _selection.SlotCardIds.Select(id =>
            {
                HistoricalFigureCardDefinition card =
                    HistoricalFigureCardCatalog.Get(id);
                return new HistoricalFigureCardRecycleInput(id, card?.Rarity,
                    "");
            }).ToList();
            if (!HistoricalFigureCardRecycleRules.TryCreatePlan(inputs,
                out HistoricalFigureCardRecyclePlan plan, out string error))
            {
                _result.text = LocalizedError(error);
                return;
            }
            IReadOnlyDictionary<string, int> sources =
                Store.GetRecycleSourceCounts(_selection.SlotCardIds);
            string crateId = HistoricalFigureCardRecycleRules.SelectWeightedCrate(
                sources, UnityEngine.Random.Range(0, int.MaxValue));
            if (string.IsNullOrEmpty(crateId))
            {
                _result.text = LocalizedError("recycle_source_missing");
                return;
            }
            IReadOnlyList<HistoricalFigureCardDefinition> pool =
                plan.OutputRarity.Equals(HistoricalFigureCardRarity.Gold)
                    ? HistoricalFigureCardCatalog.All
                    : HistoricalFigureCardCatalog.GetCards(crateId);
            HistoricalFigureCardDefinition[] eligible = pool.Where(card =>
                card?.Rarity != null && card.Rarity.Equals(plan.OutputRarity))
                .ToArray();
            if (eligible.Length == 0)
            {
                _result.text = LocalizedError("recycle_output_missing");
                return;
            }
            HistoricalFigureCardDefinition output = eligible[
                UnityEngine.Random.Range(0, eligible.Length)];
            if (!Store.TryRecycle(_selection.SlotCardIds, output.CardId,
                output.Rarity.Id, crateId, Guid.NewGuid().ToString("N")))
            {
                _result.text = LocalizedError("recycle_persistence_failed");
                return;
            }
            _lastOutput = output;
            _lastOutputCrateId = crateId;
            HistoricalFigureCardAudioService.PlayReveal(output.Rarity);
            HistoricalFigureCardRecycleSelectionRules.Clear(_selection);
            Refresh();
            RenderTradeUpResult();
        }

        private void RenderTradeUpResult()
        {
            if (_lastOutput == null) return;
            GetComponent<ScrollWindow>()?.clickHide();
            HistoricalFigureDrawWindow.OpenCardDetails(_lastOutput, _lastOutputCrateId, true);
        }

        private void BackToInventory()
        {
            HistoricalFigureCardRecycleSelectionRules.Clear(_selection);
            _lastOutput = null;
            _lastOutputCrateId = "";
            GetComponent<ScrollWindow>()?.clickHide();
            HistoricalFigureDrawWindow.OpenInventoryView();
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 40f);
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null) close.localPosition = new Vector3(
                _windowSize.x * .5f - 20f, _windowSize.y * .5f - 12f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_historical_figure_cards_recycle_title",
                    "历史人物汰换");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
                RectTransform titleTextRect =
                    window.titleText.GetComponent<RectTransform>();
                if (titleTextRect != null)
                    titleTextRect.sizeDelta = new Vector2(
                        _windowSize.x * .46f, 28f);
                window.titleText.raycastTarget = false;
            }

            // The native Scroll View otherwise clips custom children to its old
            // narrow template rectangle, which hides the right panel and footer.
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(width, height);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeScrollComponent =
                nativeScroll?.GetComponent<ScrollRect>();
            if (nativeScrollComponent != null)
            {
                nativeScrollComponent.horizontal = false;
                nativeScrollComponent.vertical = false;
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
            RectTransform nativeViewport = ContentTransform?.parent as
                RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(width, height);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
            {
                nativeContent.anchorMin = new Vector2(0f, 1f);
                nativeContent.anchorMax = new Vector2(0f, 1f);
                nativeContent.pivot = new Vector2(0f, 1f);
                nativeContent.anchoredPosition = Vector2.zero;
                nativeContent.sizeDelta = new Vector2(width, height);
            }

            Position(_root, 0f, 0f, width, height);
            Position(_status.rectTransform, 14f, 4f, width - 28f, 22f);
            Position(_summary.rectTransform, 14f, -23f, width - 28f, 18f);
            Position(_preview.rectTransform, 14f, -43f, width - 28f, 18f);
            Position(_result.rectTransform, 14f, -64f, width - 76f, 48f);
            Position(_resultPortrait.rectTransform, width - 56f, -64f, 42f, 48f);
            float listTop = -112f;
            float listHeight = Mathf.Max(140f, height - 150f);
            float listWidth = Mathf.Min(286f, width - 216f);
            listWidth = Mathf.Max(220f, listWidth);
            float scrollbarX = 14f + listWidth + 4f;
            float rightX = scrollbarX + 14f;
            float rightWidth = Mathf.Max(120f, width - rightX - 14f);
            Position(_listViewport, 14f, listTop, listWidth, listHeight);
            Position(_listScrollbar.GetComponent<RectTransform>(),
                scrollbarX, listTop, 10f, listHeight);
            Position(_slotPanel, rightX, listTop, rightWidth, listHeight);
            float slotSizeByWidth = (rightWidth - 14f -
                (SlotColumns - 1) * SlotGap) / SlotColumns;
            float slotSizeByHeight = (listHeight - 14f - 3f * SlotGap) / 4f;
            float slotSize = Mathf.Max(32f, Mathf.Min(50f,
                Mathf.Min(slotSizeByWidth, slotSizeByHeight)));
            for (int i = 0; i < _slotButtons.Count; i++)
            {
                int slotColumn = i == 9 ? 1 : i % SlotColumns;
                Position(_slotButtons[i].GetComponent<RectTransform>(),
                    7f + slotColumn * (slotSize + SlotGap),
                    -(7f + (i / SlotColumns) * (slotSize + SlotGap)),
                    slotSize, slotSize);
            }
            float slotButtonTop = -height + 28f;
            Position(_previousPage.GetComponent<RectTransform>(), 104f,
                slotButtonTop, 38f, 26f);
            Position(_pageLabel.rectTransform, 146f, slotButtonTop, 34f, 26f);
            Position(_nextPage.GetComponent<RectTransform>(), 184f,
                slotButtonTop, 38f, 26f);
            Position(_submit.GetComponent<RectTransform>(), rightX,
                slotButtonTop, Mathf.Min(82f, (rightWidth - 6f) * .5f), 26f);
            Position(_reset.GetComponent<RectTransform>(), rightX +
                Mathf.Min(82f, (rightWidth - 6f) * .5f) + 6f,
                slotButtonTop, Mathf.Min(82f, (rightWidth - 6f) * .5f), 26f);
            Position(_back.GetComponent<RectTransform>(), 14f, slotButtonTop,
                84f, 26f);
            _chrome?.RepositionResizeHandle();
        }

        private static Text MakeText(string pName, Transform pParent, int pSize,
            TextAnchor pAnchor, Color pColor)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = pColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Text ChildText(Transform pParent, string pName, int pSize)
        {
            Transform found = pParent.Find(pName);
            if (found != null) return found.GetComponent<Text>();
            return MakeText(pName, pParent, pSize, TextAnchor.MiddleCenter,
                Color.white);
        }

        private static Image ChildImage(Transform pParent, string pName)
        {
            Transform found = pParent.Find(pName);
            if (found != null) return found.GetComponent<Image>();
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static Button MakeButton(string pName, Transform pParent,
            string pText, Action pAction)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = new Color(.16f, .17f, .19f, .94f);
            Button button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => pAction?.Invoke());
            Text text = MakeText("Text", obj.transform, 7,
                TextAnchor.MiddleCenter, Color.white);
            text.text = pText;
            Position(text.rectTransform, 2f, 0f, 76f, 20f);
            return button;
        }

        private static Scrollbar CreateScrollbar(Transform pParent)
        {
            GameObject obj = new GameObject("RecycleScrollbar",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            obj.transform.SetParent(pParent, false);
            Image track = obj.GetComponent<Image>();
            track.sprite = WhiteSprite();
            track.color = new Color(.07f, .065f, .055f, .96f);
            GameObject sliding = new GameObject("Sliding Area", typeof(RectTransform));
            sliding.transform.SetParent(obj.transform, false);
            RectTransform slidingRect = sliding.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(1f, 1f);
            slidingRect.offsetMax = new Vector2(-1f, -1f);
            GameObject handle = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handle.transform.SetParent(sliding.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            Image handleImage = handle.GetComponent<Image>();
            handleImage.sprite = WhiteSprite();
            handleImage.color = new Color(.76f, .61f, .28f, 1f);
            Scrollbar scrollbar = obj.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static Sprite WhiteSprite()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(.5f, .5f), 1f);
        }

        private static Sprite GradientSprite(Color pColor)
        {
            Texture2D texture = new Texture2D(1, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { new Color(.35f, .35f, .37f), pColor });
            texture.Apply(false, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 2f),
                new Vector2(.5f, .5f), 1f);
        }

        private static void Position(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static string Text(string pKey, string pFallback)
        {
            return AW_L10n.Text(pKey, pFallback);
        }

        private static string Format(string pKey, string pFallback,
            params object[] pArgs)
        {
            return string.Format(Text(pKey, pFallback), pArgs);
        }

        private static string RarityName(HistoricalFigureCardRarity pRarity)
        {
            return pRarity?.DisplayName ?? "-";
        }

        private static string SourceCrateName(string pCrateId)
        {
            HistoricalFigureCardCrate crate =
                HistoricalFigureCardCrates.Get(pCrateId);
            return crate == null ? pCrateId :
                AW_L10n.Text(crate.NameKey, crate.DisplayName);
        }

        private static string LocalizedError(string pError)
        {
            switch (pError ?? "")
            {
                case "recycle_same_rarity":
                    return Text("aw_historical_figure_cards_recycle_same_rarity",
                        "必须选择同品质卡片");
                case "recycle_insufficient_owned":
                    return Text("aw_historical_figure_cards_recycle_insufficient",
                        "持有数量不足");
                case "recycle_gold_forbidden":
                    return Text("aw_historical_figure_cards_recycle_gold_locked",
                        "金色卡片不可汰换");
                default:
                    return Text("aw_historical_figure_cards_recycle_failed",
                        "汰换失败：" + pError);
            }
        }

        private static Color ParseColor(string pHex, Color pFallback)
        {
            return !string.IsNullOrEmpty(pHex) &&
                ColorUtility.TryParseHtmlString(pHex, out Color color)
                ? color : pFallback;
        }

        private static void SetButtonText(Button pButton, string pText)
        {
            Text text = pButton?.GetComponentInChildren<Text>();
            if (text != null) text.text = pText;
        }
    }
}
