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
        private static readonly Vector2 MinimumSize = new Vector2(480f, 330f);
        private static readonly Vector2 MaximumSize = new Vector2(760f, 620f);

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
                    "aw_historical_figure_card_recycle_title",
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
            RenderCards(cards);
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
                "aw_historical_figure_cards_recycle_reset", "重置"));
            SetButtonText(_back, Text(
                "aw_historical_figure_cards_recycle_back", "返回仓库"));
            RenderResult();
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
                    (i % 3) * 94f + 4f, -((i / 3) * 100f + 4f), 88f, 92f);
            }
            _listContent.sizeDelta = new Vector2(286f,
                Mathf.Max(1f, Mathf.Ceil(pCards.Count / 3f) * 100f + 4f));
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
            Position(name.rectTransform, 3f, -72f, 74f, 13f);
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
            Position(portrait.rectTransform, 8f, -7f, 64f, 58f);
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
                Text label = ChildText(button.transform, "SlotLabel", 7);
                label.text = card == null ? (i + 1).ToString() : card.DisplayName;
                Position(label.rectTransform, 2f, -29f, 56f, 14f);
                Image portrait = ChildImage(button.transform, "SlotPortrait");
                portrait.sprite = card == null ? null :
                    SpriteTextureLoader.getSprite(card.PortraitPath) ??
                    SpriteTextureLoader.getSprite("ui/icons/iconKings");
                portrait.preserveAspect = true;
                Position(portrait.rectTransform, 5f, -4f, 50f, 40f);
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
            HistoricalFigureDrawWindow.OpenCardDetails(_lastOutput,
                _lastOutputCrateId);
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
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null) close.localPosition = new Vector3(
                _windowSize.x * .5f - 20f, _windowSize.y * .5f - 12f);
            float width = _windowSize.x - 42f;
            float height = _windowSize.y - 48f;
            Position(_root, 0f, 0f, width, height);
            Position(_status.rectTransform, 14f, 4f, width - 28f, 22f);
            Position(_summary.rectTransform, 14f, -23f, width - 28f, 18f);
            Position(_preview.rectTransform, 14f, -43f, width - 28f, 18f);
            Position(_result.rectTransform, 14f, -64f, width - 76f, 54f);
            Position(_resultPortrait.rectTransform, width - 56f, -64f, 42f, 48f);
            float listWidth = width * .56f;
            Position(_listViewport, 14f, -126f, listWidth, height - 164f);
            Position(_listScrollbar.GetComponent<RectTransform>(),
                listWidth + 16f, -126f, 8f, height - 164f);
            Position(_slotPanel, width * .60f, -126f, width * .40f - 14f,
                height - 164f);
            for (int i = 0; i < _slotButtons.Count; i++)
                Position(_slotButtons[i].GetComponent<RectTransform>(),
                    (i % 2) * 95f + 8f, -((i / 2) * 62f + 8f), 88f, 56f);
            float buttonTop = -height + 24f;
            Position(_submit.GetComponent<RectTransform>(), 14f, buttonTop, 110f, 26f);
            Position(_reset.GetComponent<RectTransform>(), 132f, buttonTop, 80f, 26f);
            Position(_back.GetComponent<RectTransform>(), width - 92f, buttonTop, 78f, 26f);
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
