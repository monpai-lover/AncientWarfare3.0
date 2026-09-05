using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class HistoricalFigureDrawWindow :
        AbstractWindow<HistoricalFigureDrawWindow>
    {
        private const string MysteryPortraitPath =
            "ui/historical_cards/rare_special";
        private static readonly Vector2 DefaultSize = new Vector2(500f, 420f);
        private static readonly Vector2 MinimumSize = new Vector2(480f, 330f);
        private static readonly Vector2 MaximumSize = new Vector2(760f, 620f);
        private const float NormalRollDuration = 6f;
        private const float QuickRollDuration = 1.5f;
        private const float RollStartDelay = .05f;
        private const float SliderEntranceDuration = .15f;
        private const float RevealEntranceDuration = .18f;
        private const float DesktopLensRadius = 62f;
        private const float MobileLensRadius = 62f;
        private const float ReferenceMagnifiedScale = 1.15f;

        private enum DrawState { Idle, Rolling, Reveal, Details, Placement,
            PlacementConfirm, Deploying }

        private enum DetailsReturnTarget
        {
            CrateSelection,
            CrateContents,
            Inventory,
            Recycle
        }

        private static HistoricalFigureCardCollectionStore Store =>
            HistoricalFigureCardRuntimeService.Collection;
        private static HistoricalFigureCardDefinition _selectedCard;
        private static HistoricalFigureCardRevealResult _lastReveal;
        private static WorldTile _selectedTile;
        private static City _selectedCity;
        private static string _deploymentId = "";
        private static DrawState _state = DrawState.Idle;
        private static bool _pendingConfirmWindow;
        /// <summary>
        ///     BeginPlacement 主动关窗期间置真,让 <c>OnDisable</c> 的
        ///     「关窗即取消」不要把自己那次隐藏当成玩家取消。
        /// </summary>
        private static bool _suppressCloseCancel;
        /// <summary>
        ///     进入选点状态的时刻(unscaled)。选点在此后一小段静默期内不受理,
        ///     见 <see cref="IsPickingTile"/>。
        /// </summary>
        private static float _pickingArmedTime;
        /// <summary>
        ///     选点静默期。点「部署到城市」那一次点击必须在这段时间内彻底走完,
        ///     否则它会被当成玩家选的格子。
        ///
        ///     <para>
        ///     不再去猜输入事件的时序 —— 前三版判据(帧号、等松开、等新按下)
        ///     都建立在「按钮 onClick 与原版地图点击分别落在哪个事件、相隔几帧」
        ///     的推断上,而这个推断每次都不成立。一段固定的静默期与事件顺序
        ///     无关:0.2 秒足够任何一次点击走完全部处理,而玩家把鼠标从按钮
        ///     移到地图上再按下,本来就远不止这个时间。
        ///     </para>
        /// </summary>
        private const float PickingArmDelay = .2f;

        private static string _selectedCrateId = "";
        private static bool _inventoryMode;
        private const int InventoryPageSize = 20;
        private int _inventoryPage;
        private static int _detailsReturnInventoryPage;
        private static DetailsReturnTarget _detailsReturnTarget =
            DetailsReturnTarget.CrateSelection;
        private static HistoricalFigureCardRole _selectedRole =
            HistoricalFigureCardRole.Monarch;
        private static float _rollStartedAt;
        private static float _revealStartedAt;
        private static int _rollingIndex;
        private static int _lastAudioIndex = -1;
        private static bool _quickOpening;
        private static bool _autoOpening;
        private static HistoricalFigureCardInventorySort _inventorySort =
            HistoricalFigureCardInventorySort.Fame;
        private static Sprite _whiteSprite;
        private static Sprite _circleSprite;
        private static Sprite _leftEdgeFadeSprite;
        private static Sprite _rightEdgeFadeSprite;
        private static readonly Dictionary<string, Sprite> _cardGradientSprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private Image _stageBackdrop;
        private Image _stageShade;
        private Text _body;
        private Text _status;
        private RectTransform _openingSliderStage;
        private CanvasGroup _openingSliderCanvas;
        private RectTransform _trackViewport;
        private RectTransform _track;
        private RectTransform _lensViewport;
        private RectTransform _lensTrack;
        private Image _trackLeftFade;
        private Image _trackRightFade;
        private Image _centerLine;
        private Image _revealPanel;
        private CanvasGroup _revealCanvas;
        private Text _revealName;
        private Text _revealRarity;
        private Text _revealMeta;
        private Text _revealBiography;
        private RectTransform _revealBiographyViewport;
        private RectTransform _revealBiographyContent;
        private ScrollRect _revealBiographyScroll;
        private Scrollbar _revealBiographyScrollbar;
        private Image _revealBar;
        private Image _revealPortrait;
        private RectTransform _collectionViewport;
        private RectTransform _collectionRoot;
        private ScrollRect _collectionScroll;
        private Scrollbar _collectionScrollbar;
        private readonly List<Button> _crateButtons = new List<Button>();
        private readonly List<Button> _roleButtons = new List<Button>();
        private readonly List<Button> _crateCardButtons = new List<Button>();
        private readonly List<Button> _inventoryButtons = new List<Button>();
        private Button _mysteryGoldButton;
        private readonly List<HistoricalFigureCardListItem> _trackItems =
            new List<HistoricalFigureCardListItem>();
        private readonly List<HistoricalFigureCardListItem> _lensTrackItems =
            new List<HistoricalFigureCardListItem>();
        private readonly List<Button> _sortButtons = new List<Button>();
        private readonly List<Text> _rarityStats = new List<Text>();
        private Button _draw;
        private Button _skip;
        private Button _deploy;
        private Button _confirm;
        private Button _cancel;
        private Button _inventory;
        private Button _back;
        private Button _closeReveal;
        private Button _openAgain;
        private Button _quickOpen;
        private Button _autoOpen;
        private Button _sound;
        private Button _recycleModeButton;
        private Button _inventoryPreviousPage;
        private Button _inventoryNextPage;
        private Text _inventoryPageLabel;
        private WideWindowChrome _chrome;
        private bool _built;
        private float _cardLayoutWidth = -1f;
        private float _crateLayoutWidth = -1f;
        // Virtualised card grid state: only the rows intersecting the viewport
        // get a live button, so a large inventory no longer instantiates
        // hundreds of GameObjects on every switch into the tab.
        private IReadOnlyList<HistoricalFigureCardDefinition> _virtualCards =
            Array.Empty<HistoricalFigureCardDefinition>();
        private List<Button> _virtualButtons;
        private bool _virtualOwnedOnly;
        private int _virtualStartIndex;
        private int _virtualColumns = 1;
        private float _virtualButtonWidth;
        private float _virtualTileHeight;
        private int _virtualFirstBound = -1;
        private int _virtualBoundCount;
        // Tiles are six GameObjects each; building a viewport's worth in one
        // frame is a visible hitch. Amortise across frames instead.
        private const int MaxTileBuildsPerFrame = 2;
        private bool _virtualPoolIncomplete;
        // 「返回箱子」的位置是底部按钮排的锚点:「部署到城市」按它算相对位置。
        private const float BackButtonX = 230f;
        private const float BackButtonWidth = 72f;
        // Cached inventory projection. Rebuilding it walks the whole catalogue
        // (filter + sort + several sums), which made every recycle click feel
        // sluggish, so it is only recomputed when the collection or the sort
        // order actually changes -- selection state alone does not affect it.
        private IReadOnlyList<HistoricalFigureCardDefinition> _inventoryCache;
        private int _inventoryCacheTotalOwned;
        private int _inventoryCacheRevision = -1;
        private IReadOnlyDictionary<string, int> _inventoryOwnedCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private HistoricalFigureCardInventorySort _inventoryCacheSort =
            (HistoricalFigureCardInventorySort)(-1);

        public static void Open()
        {
            HistoricalFigureCardRuntimeService.Initialize();
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.HISTORICAL_FIGURE_CARDS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.HISTORICAL_FIGURE_CARDS,
                () => Instance?.Refresh());
        }

        /// <summary>
        ///     是否处于「部署流程中」——用于按钮显隐等 UI 判断。
        ///     包含选点(Placement)和确认(PlacementConfirm)两个阶段。
        /// </summary>
        internal static bool IsPlacementActive =>
            _state == DrawState.Placement || _state == DrawState.PlacementConfirm;

        /// <summary>
        ///     是否应该接管地图点击。**只在真正选点期间为真。**
        ///
        ///     <para>
        ///     确认窗打开后状态是 <c>PlacementConfirm</c>,这时玩家的点击目标是
        ///     窗口上的按钮,不是去选新格子。若此时仍然接管地图点击,「取消」/
        ///     「关闭」那一次点击的 mouse-up 会先关窗(<c>isWindowActive()</c>
        ///     随即变 false),接着被 <c>checkEmptyClick</c> 前缀捞走 →
        ///     <c>SelectMapTile</c> 选中光标下的格子 → <c>_pendingConfirmWindow</c>
        ///     置真 → 下一帧窗口又被开回来,表现就是「关掉马上又弹出来」。
        ///     </para>
        /// </summary>
        internal static bool IsPickingTile =>
            _state == DrawState.Placement &&
            UnityEngine.Time.unscaledTime - _pickingArmedTime >=
                PickingArmDelay;

        internal static void ResetTransientState()
        {
            _selectedCard = null;
            _lastReveal = null;
            _selectedCrateId = "";
            _selectedRole = HistoricalFigureCardRole.Monarch;
            _inventoryMode = false;
            _detailsReturnInventoryPage = 0;
            _detailsReturnTarget = DetailsReturnTarget.CrateSelection;
            _selectedTile = null;
            _selectedCity = null;
            _deploymentId = "";
            _pendingConfirmWindow = false;
            _pickingArmedTime = 0f;
            _state = DrawState.Idle;
            _rollStartedAt = 0f;
            _revealStartedAt = 0f;
            _rollingIndex = 0;
            _lastAudioIndex = -1;
            _autoOpening = false;
            Instance?.UpdateTrack(null, -1);
            if (Instance != null && Instance.isActiveAndEnabled)
                Instance.Refresh();
        }

        /// <summary>
        ///     关窗即取消部署。
        ///
        ///     <para>
        ///     确认窗的 X、Escape、以及点窗外关闭都只走原版的关窗流程,不经过
        ///     <see cref="CancelPlacement"/> —— 于是 <c>_state</c> 留在
        ///     <c>PlacementConfirm</c>、<c>_pendingConfirmWindow</c> 可能还没被
        ///     消费,下一帧 <c>TickPendingConfirmWindow</c> 又把窗口开回来。
        ///     玩家看到的就是「关了又弹出来」。
        ///     </para>
        ///
        ///     <para>
        ///     必须重写 <c>OnNormalDisable</c> 而不是自己写 <c>OnDisable</c>:
        ///     基类 <c>AbstractWindow</c> 已有一个 <c>private void OnDisable</c>
        ///     (里面维护 <c>IsOpened</c> 并派发 <c>OnNormalDisable</c>),
        ///     Unity 只调最派生类的那一个 —— 自己写 <c>OnDisable</c> 会把基类
        ///     的整套关窗清理吃掉,窗口状态从此错乱,表现为「点部署不弹窗」。
        ///     </para>
        ///
        ///     <para>
        ///     部署成功那条路自己会把状态收干净(<c>_state</c> 已是
        ///     <c>Details</c>),所以这里只处理仍停在部署流程里的情况。
        ///     </para>
        /// </summary>
        public override void OnNormalDisable()
        {
            // BeginPlacement 自己会 clickHide 一次(它要把地图让出来给玩家
            // 选点),那次隐藏不是玩家在关窗,不能当成取消。
            if (_suppressCloseCancel || !IsPlacementActive) return;
            _selectedTile = null;
            _selectedCity = null;
            _pendingConfirmWindow = false;
            _state = DrawState.Details;
        }

        internal static void SelectMapTile(WorldTile pTile)
        {
            if (!IsPickingTile || pTile?.data == null) return;            City city = pTile.zone_city;
            bool validCity = city?.data != null && !city.isRekt() &&
                city.isAlive() && city.kingdom?.data != null &&
                !city.kingdom.isRekt() && city.kingdom.isCiv() &&
                !city.kingdom.isNeutral();
            bool validUnownedTile = city == null && pTile.zone != null &&
                pTile.Type?.ground == true && pTile.Type.liquid == false &&
                pTile.Type.lava == false && pTile.Type.block == false &&
                !pTile.hasBuilding();
            if (!validCity && !validUnownedTile)
            {
                ModClass.LogWarning("[AW3 cards deploy] tile rejected " +
                    pTile.x + "," + pTile.y +
                    " city=" + (city?.name ?? "none") +
                    " zone=" + (pTile.zone != null) +
                    " ground=" + (pTile.Type?.ground == true) +
                    " liquid=" + (pTile.Type?.liquid == true) +
                    " lava=" + (pTile.Type?.lava == true) +
                    " block=" + (pTile.Type?.block == true) +
                    " building=" + pTile.hasBuilding());
                // 拒绝必须让玩家看得见。原来这里是静默 return —— 玩家点在
                // 不合法的格子上时屏幕毫无反应,和「功能坏了」完全无法区分,
                // 实测因此被反复当成 bug 上报。
                try
                {
                    WorldTip.showNow(TileRejectionText(pTile, city),
                        pTranslate: false, "top", 3f);
                }
                catch { }
                return;
            }
            _selectedTile = pTile;
            _selectedCity = validCity ? city : null;
            _state = DrawState.PlacementConfirm;
            // 别在同一帧重开窗口。BeginPlacement 里的 clickHide 会
            // hide() → setActive(false)(此时 OnDisable 已经跑过),之后才把
            // _should_clear 置 true —— 这面旗于是悬在那里。若此刻立刻 Open(),
            // 窗口重新启用,那面旗会在下一次 disable 时才被消费,顺带
            // WindowHistory.clear()。表现就是「第一次部署必定弹不出确认窗,
            // 放完一个普通 actor 之后反而能弹了」(旗已被消费掉)。
            // 推迟一帧再开,让 OnDisable 先把状态收干净。
            _pendingConfirmWindow = true;
        }

        /// <summary>
        ///     被拒格子的原因文案。按玩家最可能踩到的顺序判断,
        ///     只说第一条不满足的 —— 一次给一个可执行的指令,
        ///     不罗列全部判据。
        /// </summary>
        private static string TileRejectionText(WorldTile pTile, City pCity)
        {
            bool ministerOnly = _selectedCard != null &&
                (HistoricalFigureCardRoleRules.IsMinister(_selectedCard) ||
                 HistoricalFigureCardRoleRules.IsMilitaryGeneral(
                     _selectedCard));
            if (pCity != null)
                return Text("aw_historical_figure_cards_reject_city",
                    "这座城市不属于任何文明国家");
            if (ministerOnly)
                return Text("aw_historical_figure_cards_reject_minister",
                    "大臣只能部署进已有的文明城市");
            if (pTile.Type?.liquid == true)
                return Text("aw_historical_figure_cards_reject_water",
                    "不能部署到水面上");
            if (pTile.Type?.lava == true || pTile.Type?.block == true ||
                pTile.Type?.ground != true)
                return Text("aw_historical_figure_cards_reject_terrain",
                    "这里的地形无法部署");
            if (pTile.hasBuilding())
                return Text("aw_historical_figure_cards_reject_building",
                    "这块地上有建筑，请另选一处空地");
            return Text("aw_historical_figure_cards_reject_generic",
                "这里不能部署，请另选一处");
        }

        /// <summary>
        ///     选到格子后延后一帧再开确认窗。由补丁层的每帧钩子驱动 ——
        ///     窗口这时是隐藏的,自身的 Update 不会跑。
        /// </summary>
        internal static void TickPendingConfirmWindow()
        {
            if (!_pendingConfirmWindow) return;
            _pendingConfirmWindow = false;
            if (_state != DrawState.PlacementConfirm) return;
            Open();
        }

        private void SelectCollectionCard(string pCardId)
        {
            HistoricalFigureCardDefinition card =
                HistoricalFigureCardCatalog.Get(pCardId);
            if (_state != DrawState.Idle || card == null ||
                Store.GetOwnedCount(card.CardId) <= 0) return;
            _selectedCard = card;
            _lastReveal = null;
            _detailsReturnInventoryPage = _inventoryPage;
            _detailsReturnTarget = DetailsReturnTarget.Inventory;
            _state = DrawState.Details;
            Refresh();
        }

        private void SelectCrate(string pCrateId)
        {
            HistoricalFigureCardCrate crate =
                HistoricalFigureCardCrates.Get(pCrateId);
            if (_state != DrawState.Idle || crate == null) return;
            if (IsMinisterOnlyCrate(crate.Id))
                _selectedRole = HistoricalFigureCardRole.Minister;
            _selectedCard = null;
            _lastReveal = null;
            _selectedCrateId = crate.Id;
            _detailsReturnTarget = DetailsReturnTarget.CrateContents;
            _inventoryMode = false;
            Refresh();
        }

        private void SelectRole(HistoricalFigureCardRole pRole)
        {
            if (_state != DrawState.Idle) return;
            _selectedRole = pRole;
            _selectedCrateId = "";
            _selectedCard = null;
            Refresh();
        }

        private void OpenInventory()
        {
            if (_state == DrawState.Rolling || IsPlacementActive) return;
            _selectedCard = null;
            _lastReveal = null;
            _selectedCrateId = "";
            _detailsReturnTarget = DetailsReturnTarget.Inventory;
            _inventoryMode = true;
            _inventoryPage = 0;
            _state = DrawState.Idle;
            Refresh();
        }

        internal static void OpenInventoryView()
        {
            Open();
            Instance?.OpenInventory();
        }

        internal static void OpenCardDetails(
            HistoricalFigureCardDefinition pCard, string pCrateId,
            bool pReturnToRecycle = false)
        {
            if (pCard == null) return;
            _selectedCard = pCard;
            _lastReveal = null;
            _selectedCrateId = string.IsNullOrEmpty(pCrateId)
                ? pCard.CollectionId : pCrateId;
            _selectedRole = pCard.Role;
            _inventoryMode = false;
            _detailsReturnTarget = pReturnToRecycle
                ? DetailsReturnTarget.Recycle
                : (_selectedCrateId.Length > 0
                    ? DetailsReturnTarget.CrateContents
                    : DetailsReturnTarget.CrateSelection);
            _state = DrawState.Details;
            Open();
        }

        private void OpenDedicatedRecycle()
        {
            if (!_inventoryMode || _state != DrawState.Idle) return;
            GetComponent<ScrollWindow>()?.clickHide();
            HistoricalFigureRecycleWindow.Open();
        }

        private void BackToPreviousPage()
        {
            if (_state == DrawState.Rolling || IsPlacementActive) return;
            if (_state == DrawState.Idle)
            {
                _selectedCard = null;
                _lastReveal = null;
                _selectedCrateId = "";
                _inventoryMode = false;
                _detailsReturnTarget = DetailsReturnTarget.CrateSelection;
                Refresh();
                return;
            }
            if (_detailsReturnTarget == DetailsReturnTarget.Recycle)
            {
                _detailsReturnTarget = DetailsReturnTarget.CrateSelection;
                _selectedCard = null;
                _lastReveal = null;
                _selectedCrateId = "";
                _inventoryMode = false;
                GetComponent<ScrollWindow>()?.clickHide();
                HistoricalFigureRecycleWindow.Open();
                return;
            }
            _selectedCard = null;
            _lastReveal = null;
            if (_detailsReturnTarget == DetailsReturnTarget.Inventory)
            {
                _selectedCrateId = "";
                _inventoryMode = true;
                _inventoryPage = _detailsReturnInventoryPage;
            }
            else
            {
                if (_detailsReturnTarget == DetailsReturnTarget.CrateSelection)
                    _selectedCrateId = "";
                _inventoryMode = false;
            }
            _state = DrawState.Idle;
            Refresh();
        }

        protected override void Init()
        {
            BuildUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size => { _windowSize = size; ApplyLayout(); },
                DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable() => Refresh();

        private void Update()
        {
            if (!isActiveAndEnabled) return;
            // Keep growing the tile pool a few per frame until the viewport is
            // covered, so opening the warehouse costs a handful of cheap
            // frames instead of one long stall.
            if (_virtualPoolIncomplete) BindVisibleCards(false);
            if (_state == DrawState.Reveal)
            {
                UpdateRevealEntrance();
                if (_autoOpening && Time.unscaledTime - _revealStartedAt >= .8f)
                    OpenAgain();
                return;
            }
            if (_state != DrawState.Rolling) return;
            UpdateSliderEntrance();
            float duration = _quickOpening ? QuickRollDuration : NormalRollDuration;
            float progress = Mathf.Clamp01((Time.unscaledTime - _rollStartedAt) /
                duration);
            float eased = CaseOpeningEase(progress);
            _rollingIndex = Mathf.Min(HistoricalFigureCardDrawService.WinnerIndex,
                Mathf.FloorToInt(eased * HistoricalFigureCardDrawService.WinnerIndex));
            if (_rollingIndex != _lastAudioIndex)
            {
                _lastAudioIndex = _rollingIndex;
                HistoricalFigureCardAudioService.PlayScroll();
            }
            HistoricalFigureCardDefinition rollingCard = _lastReveal?.RollingCards == null ||
                _lastReveal.RollingCards.Count <= _rollingIndex ? _selectedCard :
                _lastReveal.RollingCards[_rollingIndex];
            _status.text = Format("aw_historical_figure_cards_rolling",
                "\u6eda\u52a8\u4e2d {0}/{1}\uff1a{2}", _rollingIndex + 1,
                HistoricalFigureCardDrawService.RollingCardCount,
                rollingCard?.DisplayName ?? "-");
            _body.text = Format("aw_historical_figure_cards_rolling_body",
                "\u5f00\u7bb1\u8f68\u9053\u6b63\u5728\u6eda\u52a8...\n\u5f53\u524d\uff1a{0}\n\u4e2d\u5956\u4f4d\u7f6e\uff1a{1}",
                rollingCard?.DisplayName ?? "-",
                HistoricalFigureCardDrawService.WinnerIndex);
            UpdateTrack(_lastReveal.RollingCards,
                eased * HistoricalFigureCardDrawService.WinnerIndex,
                _rollingIndex);
            if (progress >= 1f) FinishRoll();
        }

        private void BuildUi()
        {
            if (_built || ContentTransform == null) return;
            _built = true;
            foreach (LayoutGroup layout in ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            ContentSizeFitter fitter = ContentTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            GameObject root = new GameObject("HistoricalFigureDrawRoot",
                typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();

            _stageBackdrop = new GameObject("OpeningStageBackdrop",
                typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _stageBackdrop.transform.SetParent(_root, false);
            _stageBackdrop.sprite = SpriteTextureLoader.getSprite(
                "ui/historical_cards/de_ancient");
            _stageBackdrop.color = new Color(1f, 1f, 1f, .34f);
            _stageBackdrop.raycastTarget = false;
            _stageShade = new GameObject("OpeningStageShade",
                typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _stageShade.transform.SetParent(_root, false);
            _stageShade.sprite = WhiteSprite();
            _stageShade.color = new Color(.015f, .02f, .025f, .62f);
            _stageShade.raycastTarget = false;

            _status = MakeText("Status", _root, 12,
                TextAnchor.UpperLeft);
            _body = MakeText("Body", _root, 9,
                TextAnchor.UpperLeft);
            _body.verticalOverflow = VerticalWrapMode.Overflow;

            GameObject openingSliderStage = new GameObject(
                "OpeningSliderStage", typeof(RectTransform),
                typeof(CanvasGroup));
            openingSliderStage.transform.SetParent(_root, false);
            _openingSliderStage = openingSliderStage.GetComponent<RectTransform>();
            _openingSliderCanvas = openingSliderStage.GetComponent<CanvasGroup>();

            GameObject viewport = new GameObject("CardTrackViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(_openingSliderStage, false);
            _trackViewport = viewport.GetComponent<RectTransform>();
            Image trackBackdrop = viewport.GetComponent<Image>();
            trackBackdrop.sprite = WhiteSprite();
            trackBackdrop.color = new Color(.035f, .04f, .05f, .96f);
            trackBackdrop.raycastTarget = false;
            GameObject track = new GameObject("CardTrack",
                typeof(RectTransform));
            track.transform.SetParent(_trackViewport, false);
            _track = track.GetComponent<RectTransform>();
            _track.anchorMin = new Vector2(0f, .5f);
            _track.anchorMax = new Vector2(0f, .5f);
            _track.pivot = new Vector2(0f, .5f);
            _track.sizeDelta = new Vector2(
                HistoricalFigureCardDrawService.RollingCardCount *
                    HistoricalFigureCardListItem.DesktopWidth,
                HistoricalFigureCardListItem.DesktopHeight);
            _track.anchoredPosition = Vector2.zero;

            _trackLeftFade = new GameObject("OpeningTrackLeftFade",
                typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _trackLeftFade.transform.SetParent(_openingSliderStage, false);
            _trackLeftFade.sprite = EdgeFadeSprite(true);
            _trackLeftFade.color = new Color(.01f, .015f, .02f, .88f);
            _trackLeftFade.raycastTarget = false;
            _trackRightFade = new GameObject("OpeningTrackRightFade",
                typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _trackRightFade.transform.SetParent(_openingSliderStage, false);
            _trackRightFade.sprite = EdgeFadeSprite(false);
            _trackRightFade.color = new Color(.01f, .015f, .02f, .88f);
            _trackRightFade.raycastTarget = false;

            GameObject lensViewport = new GameObject("CardLensViewport",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            lensViewport.transform.SetParent(_openingSliderStage, false);
            _lensViewport = lensViewport.GetComponent<RectTransform>();
            Image lensImage = lensViewport.GetComponent<Image>();
            lensImage.sprite = CircleSprite();
            lensImage.color = new Color(.05f, .06f, .08f, .98f);
            lensImage.raycastTarget = false;
            lensViewport.GetComponent<Mask>().showMaskGraphic = true;
            GameObject lensTrack = new GameObject("CardLensTrack",
                typeof(RectTransform));
            lensTrack.transform.SetParent(_lensViewport, false);
            _lensTrack = lensTrack.GetComponent<RectTransform>();
            _lensTrack.anchorMin = new Vector2(0f, .5f);
            _lensTrack.anchorMax = new Vector2(0f, .5f);
            _lensTrack.pivot = new Vector2(0f, .5f);
            _lensTrack.sizeDelta = new Vector2(
                HistoricalFigureCardDrawService.RollingCardCount *
                    HistoricalFigureCardListItem.DesktopWidth,
                HistoricalFigureCardListItem.DesktopHeight);

            _centerLine = new GameObject("WinnerMarker", typeof(RectTransform),
                typeof(Image)).GetComponent<Image>();
            _centerLine.transform.SetParent(_openingSliderStage, false);
            _centerLine.sprite = WhiteSprite();
            _centerLine.color = new Color(1f, .82f, .2f, .94f);
            _centerLine.raycastTarget = false;

            _revealPanel = new GameObject("RevealPanel", typeof(RectTransform),
                typeof(Image), typeof(CanvasGroup)).GetComponent<Image>();
            _revealPanel.transform.SetParent(_root, false);
            _revealCanvas = _revealPanel.GetComponent<CanvasGroup>();
            _revealPanel.sprite = WhiteSprite();
            _revealPanel.color = new Color(.05f, .06f, .08f, .98f);
            _revealPanel.raycastTarget = false;
            _revealPortrait = new GameObject("Portrait", typeof(RectTransform),
                typeof(Image)).GetComponent<Image>();
            _revealPortrait.transform.SetParent(_revealPanel.transform, false);
            _revealPortrait.sprite = SpriteTextureLoader.getSprite(
                "ui/icons/iconKings") ?? SpriteTextureLoader.getSprite(
                "ui/icons/iconKnowledge");
            _revealPortrait.preserveAspect = true;
            _revealPortrait.raycastTarget = false;
            _revealName = MakeText("Name", _revealPanel.transform, 13,
                TextAnchor.MiddleCenter);
            _revealRarity = MakeText("Rarity", _revealPanel.transform, 9,
                TextAnchor.MiddleCenter);
            _revealMeta = MakeText("Meta", _revealPanel.transform, 7,
                TextAnchor.UpperLeft);
            GameObject biographyViewport = new GameObject(
                "BiographyViewport", typeof(RectTransform), typeof(Image),
                typeof(RectMask2D));
            biographyViewport.transform.SetParent(_revealPanel.transform, false);
            _revealBiographyViewport =
                biographyViewport.GetComponent<RectTransform>();
            Image biographyViewportImage = biographyViewport.GetComponent<Image>();
            biographyViewportImage.sprite = WhiteSprite();
            biographyViewportImage.color = new Color(0f, 0f, 0f, .01f);
            biographyViewportImage.raycastTarget = true;
            GameObject biographyContent = new GameObject("BiographyContent",
                typeof(RectTransform));
            biographyContent.transform.SetParent(_revealBiographyViewport, false);
            _revealBiographyContent =
                biographyContent.GetComponent<RectTransform>();
            _revealBiographyContent.anchorMin = new Vector2(0f, 1f);
            _revealBiographyContent.anchorMax = new Vector2(0f, 1f);
            _revealBiographyContent.pivot = new Vector2(0f, 1f);
            _revealBiography = MakeText("Biography", _revealBiographyContent,
                7, TextAnchor.UpperLeft);
            _revealBiography.verticalOverflow = VerticalWrapMode.Overflow;
            _revealBiographyScroll = biographyViewport.AddComponent<ScrollRect>();
            _revealBiographyScroll.viewport = _revealBiographyViewport;
            _revealBiographyScroll.content = _revealBiographyContent;
            _revealBiographyScroll.horizontal = false;
            _revealBiographyScroll.vertical = true;
            _revealBiographyScroll.movementType =
                ScrollRect.MovementType.Clamped;
            _revealBiographyScroll.scrollSensitivity = 18f;
            _revealBiographyScrollbar = CreateVerticalScrollbar(
                "BiographyScrollbar", _revealPanel.transform);
            _revealBiographyScroll.verticalScrollbar =
                _revealBiographyScrollbar;
            _revealBiographyScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            _revealBar = new GameObject("RarityBar", typeof(RectTransform),
                typeof(Image)).GetComponent<Image>();
            _revealBar.transform.SetParent(_revealPanel.transform, false);
            _revealBar.sprite = WhiteSprite();
            _revealBar.raycastTarget = false;
            GameObject collectionViewport = new GameObject("CollectionViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            collectionViewport.transform.SetParent(_root, false);
            Image collectionImage = collectionViewport.GetComponent<Image>();
            collectionImage.color = new Color(.06f, .06f, .07f, .94f);
            collectionImage.raycastTarget = true;
            _collectionViewport = collectionViewport.GetComponent<RectTransform>();

            GameObject collection = new GameObject("Collection",
                typeof(RectTransform));
            collection.transform.SetParent(collectionViewport.transform, false);
            _collectionRoot = collection.GetComponent<RectTransform>();
            _collectionRoot.anchorMin = new Vector2(0f, 1f);
            _collectionRoot.anchorMax = new Vector2(0f, 1f);
            _collectionRoot.pivot = new Vector2(0f, 1f);
            _collectionRoot.anchoredPosition = Vector2.zero;
            _collectionScroll = collectionViewport.AddComponent<ScrollRect>();
            _collectionScroll.viewport = _collectionViewport;
            _collectionScroll.content = _collectionRoot;
            _collectionScroll.horizontal = false;
            _collectionScroll.vertical = true;
            _collectionScroll.movementType = ScrollRect.MovementType.Clamped;
            _collectionScroll.scrollSensitivity = 22f;
            _collectionScrollbar = CreateVerticalScrollbar(
                "CollectionScrollbar", _root);
            _collectionScroll.verticalScrollbar = _collectionScrollbar;
            _collectionScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            // Virtualised grid: rebind the pooled tiles as the viewport moves.
            _collectionScroll.onValueChanged.AddListener(
                _ => BindVisibleCards(false));

            _draw = MakeButton("Draw", _root,
                AW_L10n.Text("aw_historical_figure_cards_draw", "\u62bd\u53d6"), Draw);
            _skip = MakeButton("Skip", _root,
                Text("aw_historical_figure_cards_skip", "\u8df3\u8fc7"), Skip);
            _deploy = MakeButton("Deploy", _root,
                AW_L10n.Text("aw_historical_figure_cards_deploy", "\u90e8\u7f72\u5230\u57ce\u5e02"), BeginPlacement);
            _confirm = MakeButton("Confirm", _root,
                Text("aw_historical_figure_cards_confirm", "\u786e\u8ba4\u90e8\u7f72"), ConfirmPlacement);
            _cancel = MakeButton("Cancel", _root,
                Text("aw_title_cancel", "\u53d6\u6d88"), CancelPlacement);
            _inventory = MakeButton("Inventory", _root,
                Text("aw_historical_figure_cards_inventory", "\u4ed3\u5e93"),
                OpenInventory);
            _back = MakeButton("Back", _root,
                Text("aw_historical_figure_cards_back", "\u8fd4\u56de\u7bb1\u5b50"),
                BackToPreviousPage);
            _closeReveal = MakeButton("CloseReveal", _root,
                Text("aw_historical_figure_cards_close", "\u5173\u95ed"), CloseReveal);
            _openAgain = MakeButton("OpenAgain", _root,
                Text("aw_historical_figure_cards_open_again", "\u518d\u5f00\u4e00\u6b21"), OpenAgain);
            _quickOpen = MakeButton("QuickOpen", _root, "", ToggleQuickOpening);
            _autoOpen = MakeButton("AutoOpen", _root, "", ToggleAutoOpening);
            _sound = MakeButton("Sound", _root, "", ToggleSound);
            _recycleModeButton = MakeButton("RecycleMode", _root, "",
                OpenDedicatedRecycle);
            Button monarch = MakeButton("MonarchCategory", _root,
                Text("aw_historical_figure_cards_role_monarch", "君主箱"),
                () => SelectRole(HistoricalFigureCardRole.Monarch));
            Button minister = MakeButton("MinisterCategory", _root,
                Text("aw_historical_figure_cards_role_minister", "大臣箱"),
                () => SelectRole(HistoricalFigureCardRole.Minister));
            _roleButtons.Add(monarch);
            _roleButtons.Add(minister);
            CreateInventoryControls();
        }

        private void Refresh()
        {
            bool needsInitialLayout = !_built;
            BuildUi();
            if (needsInitialLayout) ApplyLayout();
            Store.Load();
            if (_state != DrawState.Rolling)
                UpdateTrack(_lastReveal?.RollingCards,
                    _lastReveal?.WinnerIndex ?? -1);
            _draw.interactable = HistoricalFigureCardRuntimeService.
                IsCatalogueAvailable && _state == DrawState.Idle &&
                _selectedCrateId.Length > 0;
            _skip.interactable = _state == DrawState.Rolling;
            _deploy.interactable = (_state == DrawState.Details ||
                _state == DrawState.Reveal) && _selectedCard != null &&
                Store.GetOwnedCount(_selectedCard.CardId) > 0;
            _confirm.interactable = _state == DrawState.PlacementConfirm &&
                _selectedTile != null;
            _cancel.interactable = _state == DrawState.Placement ||
                _state == DrawState.PlacementConfirm;
            bool showCollection = _state == DrawState.Idle && _selectedCard == null;
            _collectionViewport?.gameObject.SetActive(showCollection);
            bool showCollectionScrollbar = showCollection &&
                (_inventoryMode || _selectedCrateId.Length > 0);
            _collectionScrollbar?.gameObject.SetActive(showCollectionScrollbar);
            bool showTrack = _state == DrawState.Rolling;
            bool showOpeningStage = showTrack || _state == DrawState.Reveal;
            // 开箱滚动轨道只在 Rolling 时用。以前它从不隐藏,ApplyLayout 里的
            // trackMode 就恒为 true —— 于是 collectionTop 永远取 -178,网格上方
            // 白白空出一大条,compactHeader 那套压缩逻辑一次也没生效。
            _trackViewport?.gameObject.SetActive(showTrack);
            _lensViewport?.gameObject.SetActive(showTrack);
            _stageBackdrop?.gameObject.SetActive(showOpeningStage);
            _stageShade?.gameObject.SetActive(showOpeningStage);
            _openingSliderStage?.gameObject.SetActive(showTrack);
            bool showRevealPanel = _state == DrawState.Reveal || _state == DrawState.Details;
            _revealPanel?.gameObject.SetActive(showRevealPanel);
            SetInventoryControlsVisible(_inventoryMode &&
                _state == DrawState.Idle);
            if (showRevealPanel)
            {
                UpdateRevealPanel();
                if (_state == DrawState.Reveal)
                {
                    UpdateRevealActionLabels();
                    UpdateRevealEntrance();
                }
                else
                {
                    _revealCanvas.alpha = 1f;
                    _revealPanel.rectTransform.localScale = Vector3.one;
                }
            }
            ApplyLayout();
            if (_state == DrawState.Rolling) return;

            if (_state == DrawState.Placement || _state == DrawState.PlacementConfirm)
            {
                bool ministerOnly =
                    HistoricalFigureCardRoleRules.IsMinister(_selectedCard) ||
                    HistoricalFigureCardRoleRules.IsMilitaryGeneral(
                        _selectedCard);
                _status.text = _state == DrawState.Placement
                    ? (ministerOnly
                        ? Text("aw_historical_figure_cards_select_city_minister",
                            "\u8bf7\u9009\u62e9\u4e00\u5ea7\u6587\u660e\u57ce\u5e02")
                        : Text("aw_historical_figure_cards_select_city",
                            "\u8bf7\u9009\u62e9\u6587\u660e\u57ce\u5e02\u6216\u65e0\u4e3b\u9646\u5730"))
                    : Format("aw_historical_figure_cards_target",
                        "\u76ee\u6807\uff1a{0}", _selectedCity?.name ??
                        (_selectedTile == null ? "-" : _selectedTile.x + ", " +
                         _selectedTile.y));
                _body.text = CardText(_selectedCard) + "\n\n" +
                    (ministerOnly
                        ? Text(
                            "aw_historical_figure_cards_confirm_city_hint_minister",
                            "\u786e\u8ba4\u540e\u5c06\u5728\u6b64\u90e8\u7f72\uff1b\u5927\u81e3\u53ea\u80fd\u8fdb\u5165\u5df2\u6709\u56fd\u5bb6\uff0c\u65e0\u4e3b\u5730\u65e0\u6cd5\u90e8\u7f72\u3002")
                        : Text("aw_historical_figure_cards_confirm_city_hint",
                            "\u786e\u8ba4\u540e\u5c06\u5728\u6b64\u90e8\u7f72\uff1b\u65e0\u4e3b\u5730\u4f1a\u5efa\u7acb\u5176\u5386\u53f2\u56fd\u5bb6\u3002"));
                return;
            }
            if (_selectedCard != null && (_state == DrawState.Details || _state == DrawState.Reveal))
            {
                _status.text = Format("aw_historical_figure_cards_details_title",
                    "{0} [{1}]", _selectedCard.DisplayName,
                    RarityName(_selectedCard.Rarity));
                _body.text = CardText(_selectedCard) +
                    "\n\n" + Format("aw_historical_figure_cards_owned",
                    "\u5df2\u62e5\u6709\uff1a{0}", Store.GetOwnedCount(_selectedCard.CardId));
                SetButtonText(_back, Text(
                    "aw_historical_figure_cards_back_previous",
                    "\u8fd4\u56de\u4e0a\u4e00\u9875"));
                return;
            }

            _status.text = _inventoryMode
                ? Text("aw_historical_figure_cards_inventory_title", "\u5386\u53f2\u4eba\u7269\u4ed3\u5e93")
                : (_selectedCrateId.Length == 0
                    ? Text("aw_historical_figure_cards_crates_title", "\u5386\u53f2\u4eba\u7269\u7bb1\u5b50")
                    : Text("aw_historical_figure_cards_crate_title", "\u5386\u53f2\u65f6\u671f\u7bb1\u5b50"));
            SetButtonText(_back, Text("aw_historical_figure_cards_back",
                "\u8fd4\u56de\u7bb1\u5b50"));
            if (_inventoryMode)
            {
                _body.text = Text("aw_historical_figure_cards_inventory_hint",
                    "\u5df2\u62e5\u6709\u7684\u5386\u53f2\u4eba\u7269\u3002");
                UpdateInventory();
            }
            else if (_selectedCrateId.Length == 0)
            {
                _body.text = Text("aw_historical_figure_cards_crates_hint",
                    "\u9009\u62e9\u4e00\u4e2a\u5386\u53f2\u65f6\u671f\u7bb1\u5b50\u3002");
                UpdateCrates();
            }
            else
            {
                HistoricalFigureCardCrate crate =
                    HistoricalFigureCardCrates.Get(_selectedCrateId);
                IReadOnlyList<HistoricalFigureCardDefinition> cards =
                    HistoricalFigureCardCatalog.GetCards(_selectedCrateId,
                        _selectedRole);
                // \u7bb1\u5b50\u62ac\u5934\u538b\u6210\u4e00\u884c:\u540d\u53f7 \u00b7 \u63cf\u8ff0,\u91d1\u8272\u6c60\u7684\u8bf4\u660e\u5e76\u8fdb\u540c\u4e00\u884c\u3002
                _body.text = Format("aw_historical_figure_cards_crate_body",
                    "{0} \u00b7 {1} \u00b7 {2}", CrateName(crate),
                    CrateDescription(crate),
                    Format("aw_historical_figure_cards_shared_gold",
                        "\u542b\u795e\u79d8\u91d1\u8272\u4eba\u7269\uff08\u6240\u6709\u7bb1\u5b50\u5171\u4eab\uff09"));
                UpdateCrateContents(cards);
            }
        }

        private void Draw()
        {
            if (_state != DrawState.Idle || _selectedCrateId.Length == 0) return;
            _lastReveal = HistoricalFigureCardDrawService.DrawAndCommit(
                HistoricalFigureCardCatalog.GetCards(_selectedCrateId,
                    _selectedRole),
                _selectedCrateId, Store);
            if (!_lastReveal.Succeeded)
            {
                _status.text = Format("aw_historical_figure_cards_draw_failed",
                    "\u62bd\u53d6\u5931\u8d25\uff1a{0}", _lastReveal.Error);
                return;
            }
            _selectedCard = _lastReveal.Winner;
            _state = DrawState.Rolling;
            _rollStartedAt = Time.unscaledTime + RollStartDelay;
            _rollingIndex = 0;
            _lastAudioIndex = -1;
            _draw.interactable = false;
            _skip.interactable = true;
            _deploy.interactable = false;
            _confirm.interactable = false;
            _cancel.interactable = false;
            Refresh();
            UpdateTrack(_lastReveal.RollingCards, 0f, 0);
            HistoricalFigureCardAudioService.PlayUnlock();
        }

        private void Skip()
        {
            if (_state != DrawState.Rolling || _lastReveal == null) return;
            FinishRoll();
            HistoricalFigureCardAudioService.PlayImmediateUnlock();
        }

        private void FinishRoll()
        {
            if (_state != DrawState.Rolling) return;
            _state = DrawState.Reveal;
            _revealStartedAt = Time.unscaledTime;
            UpdateTrack(_lastReveal?.RollingCards,
                HistoricalFigureCardDrawService.WinnerIndex,
                HistoricalFigureCardDrawService.WinnerIndex);
            _status.text = Format("aw_historical_figure_cards_reveal",
                "\u83b7\u5f97\uff1a{0}  \u4f4d\u7f6e {1}", _selectedCard.DisplayName,
                HistoricalFigureCardDrawService.WinnerIndex);
            Refresh();
            HistoricalFigureCardAudioService.PlayReveal(_selectedCard.Rarity);
        }

        private void UpdateSliderEntrance()
        {
            if (_openingSliderStage == null || _openingSliderCanvas == null)
                return;
            float shownAt = _rollStartedAt - RollStartDelay;
            float progress = Mathf.Clamp01((Time.unscaledTime - shownAt) /
                SliderEntranceDuration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            _openingSliderCanvas.alpha = progress;
            float scale = Mathf.Lerp(.5f, 1f, eased);
            _openingSliderStage.localScale = new Vector3(scale, scale, 1f);
        }

        private void UpdateRevealEntrance()
        {
            if (_revealPanel == null || _revealCanvas == null) return;
            float progress = Mathf.Clamp01((Time.unscaledTime -
                _revealStartedAt) / RevealEntranceDuration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            _revealCanvas.alpha = progress;
            float scale = Mathf.Lerp(.4f, 1f, eased);
            _revealPanel.rectTransform.localScale =
                new Vector3(scale, scale, 1f);
        }

        private void CloseReveal()
        {
            if (_state != DrawState.Reveal) return;
            _autoOpening = false;
            _selectedCard = null;
            _lastReveal = null;
            _state = DrawState.Idle;
            Refresh();
        }

        private void OpenAgain()
        {
            if (_state != DrawState.Reveal || _selectedCrateId.Length == 0)
                return;
            _selectedCard = null;
            _lastReveal = null;
            _state = DrawState.Idle;
            Draw();
        }

        private void ToggleQuickOpening()
        {
            _quickOpening = !_quickOpening;
            if (!_quickOpening) _autoOpening = false;
            UpdateRevealActionLabels();
        }

        private void ToggleAutoOpening()
        {
            if (!_quickOpening) return;
            _autoOpening = !_autoOpening;
            UpdateRevealActionLabels();
        }

        private void ToggleSound()
        {
            HistoricalFigureCardAudioService.SetEnabled(
                !HistoricalFigureCardAudioService.Enabled);
            UpdateRevealActionLabels();
        }

        private void BeginPlacement()
        {
            if ((_state != DrawState.Details && _state != DrawState.Reveal) ||
                _selectedCard == null ||
                Store.GetOwnedCount(_selectedCard.CardId) <= 0) return;
            _autoOpening = false;
            _selectedTile = null;
            _selectedCity = null;
            _deploymentId = Guid.NewGuid().ToString("N");
            _state = DrawState.Placement;
            // 点「部署到城市」这次点击要先彻底走完,静默期内不受理选点。
            _pickingArmedTime = UnityEngine.Time.unscaledTime;
            // 关窗后必须把这两样都清掉,否则点图会有肉眼可见的延迟:
            //   1. clickHide 把 controls_lock_timer 设成 0.3s,归零前
            //      updateControls 的整个点击分支被跳过;
            //   2. hide tween(0.02s 延迟 + 0.1s 时长)跑完之前
            //      ScrollWindow.isAnimationActive() 为真,于是
            //      MapBox.isGameplayControlsLocked() 也为真 —— updateControls
            //      在它为真时直接 return,点击同样被吞。
            // 选点已有 IsPickingTile 状态机把关,这两道防误触都不需要。
            //
            // 压制标志必须罩住 finishAnimations:原版 setActive(false) 只是
            // **起了一个 tween**,真正的 gameObject.SetActive(false) 在 tween
            // 的 activeToFalse 回调里,也就是 finishAnimations 这一句才触发 ——
            // OnNormalDisable 因此跑在 clickHide 返回之后。只罩住 clickHide
            // 的话,标志早已复位,自己主动的这次关窗会被当成玩家取消,
            // _state 被打回 Details,选点直接失效(表现为点部署没有弹窗)。
            _suppressCloseCancel = true;
            try
            {
                GetComponent<ScrollWindow>()?.clickHide();
                ScrollWindow.finishAnimations();
            }
            finally { _suppressCloseCancel = false; }
            if (World.world?.player_control != null)
                World.world.player_control.controls_lock_timer = 0f;
            // 提示必须在关窗之后:clickHide 走原版关窗流程,期间 WorldTip 会被
            // 收起,先弹的提示会被一起吃掉。
            HistoricalFigureCardPlacementPowerService.ShowPlacementHint(
                HistoricalFigureCardRoleRules.IsMinister(_selectedCard) ||
                HistoricalFigureCardRoleRules.IsMilitaryGeneral(_selectedCard));
        }

        private void ConfirmPlacement()
        {
            if (_state != DrawState.PlacementConfirm || _selectedCard == null ||
                _selectedTile == null)
            {
                ModClass.LogWarning("[AW3 cards deploy] confirm ignored state=" +
                    _state + " card=" + (_selectedCard?.CardId ?? "null") +
                    " tile=" + (_selectedTile == null ? "null"
                        : _selectedTile.x + "," + _selectedTile.y));
                return;
            }
            _state = DrawState.Deploying;
            HistoricalFigureCardDeploymentResult result =
                HistoricalFigureCardDeploymentService.TryDeploy(
                    new HistoricalFigureCardDeploymentRequest(_selectedCard.CardId,
                        _lastReveal?.DrawId ?? "", _deploymentId,
                        _selectedTile, _selectedCity));
            ModClass.LogInfo("[AW3 cards deploy] card=" + _selectedCard.CardId +
                " tile=" + _selectedTile.x + "," + _selectedTile.y +
                " city=" + (_selectedCity?.name ?? "none") +
                " ok=" + result.Succeeded + " err=" + (result.Error ?? "") +
                " kingdom=" + (result.KingdomName ?? ""));
            if (result.Succeeded)
            {
                _status.text = Format("aw_historical_figure_cards_deployed",
                    "\u5df2\u90e8\u7f72 {0}\uff0c\u56fd\u53f7\u4e3a{1}", _selectedCard.DisplayName,
                    result.KingdomName);
                _selectedTile = null;
                _selectedCity = null;
                _pendingConfirmWindow = false;
                _state = DrawState.Details;
                // \u90e8\u7f72\u6210\u529f\u540e\u73a9\u5bb6\u60f3\u770b\u7684\u662f\u521a\u843d\u5730\u7684\u4eba\u7269,\u4e0d\u662f\u518d\u5f39\u56de\u5361\u7247\u8be6\u60c5\u3002
                // \u5173\u7a97\u628a\u89c6\u91ce\u4ea4\u8fd8\u7ed9\u5730\u56fe,\u5e76\u7528\u4e00\u6761\u5de5\u5177\u680f\u63d0\u793a\u56de\u62a5\u7ed3\u679c\u3002
                Refresh();
                try
                {
                    WorldTip.showNow(_status.text, pTranslate: false, "top", 4f);
                }
                catch { }
                GetComponent<ScrollWindow>()?.clickHide();
                return;
            }
            _status.text = Format("aw_historical_figure_cards_deploy_failed",
                "\u90e8\u7f72\u5931\u8d25\uff1a{0}", result.Error);
            _state = DrawState.Placement;
            // \u540c BeginPlacement:\u70b9\u300c\u786e\u8ba4\u90e8\u7f72\u300d\u8fd9\u6b21\u70b9\u51fb\u8981\u5148\u5f7b\u5e95\u8d70\u5b8c,
            // \u5426\u5219\u786e\u8ba4\u6309\u94ae\u7684\u4f4d\u7f6e\u4f1a\u88ab\u5f53\u6210\u65b0\u9009\u7684\u683c\u5b50\u3002
            _pickingArmedTime = UnityEngine.Time.unscaledTime;
            Refresh();
        }

        private void CancelPlacement()
        {
            if (!IsPlacementActive) return;
            _selectedTile = null;
            _selectedCity = null;
            // 未消费的开窗请求必须一并清掉,否则下一帧 TickPendingConfirmWindow
            // 还会把确认窗开回来。
            _pendingConfirmWindow = false;
            _state = DrawState.Details;
            Refresh();
        }

        private static string CardText(HistoricalFigureCardDefinition pCard)
        {
            if (pCard == null)
                return Text("aw_historical_figure_cards_no_card", "\u672a\u9009\u62e9\u5361\u7247\u3002");
            return Format("aw_historical_figure_cards_details",
                "\u59d3\u540d\uff1a{0}\n\u59d3\uff1a{1}  \u6c0f\uff1a{2}\n\u671d\u4ee3\uff1a{3}  \u65f6\u671f\uff1a{4}\n\u751f\u5352\uff1a{5} - {6}  \u540d\u6c14\uff1a{7}\n\u5386\u53f2\u56fd\u53f7\uff1a{8}\n\u7236\uff1a{9}\n\u6bcd\uff1a{10}\n\n\u80cc\u666f\uff1a{11}\n\n\u8be6\u7ec6\u4ecb\u7ecd\uff1a{12}",
                pCard.DisplayName, pCard.FamilyName, pCard.ClanName,
                pCard.DynastyName, pCard.HistoricalEra,
                HistoricalYearText(pCard.BirthYear),
                HistoricalYearText(pCard.DeathYear), pCard.FameScore,
                pCard.HistoricalKingdomName,
                pCard.ParentDisplayName(true), pCard.ParentDisplayName(false),
                pCard.BackgroundSummary, pCard.DetailedBiography);
        }

        private static string HistoricalYearText(int pYear)
        {
            if (pYear == HistoricalFigureCardCatalog.UnknownYear)
                return Text("aw_historical_figure_cards_year_unknown",
                    "\u53f2\u6599\u4e0d\u8be6");
            return pYear < 0
                ? Format("aw_historical_figure_cards_year_bce",
                    "\u516c\u5143\u524d{0}\u5e74", -pYear)
                : Format("aw_historical_figure_cards_year_ce",
                    "\u516c\u5143{0}\u5e74", pYear);
        }

        private void UpdateTrack(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            int pSelectedIndex)
        {
            UpdateTrack(pCards, pSelectedIndex, pSelectedIndex);
        }

        private void UpdateTrack(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            float pTrackIndex, int pSelectedIndex)
        {
            if (_track == null) return;
            int count = pCards?.Count ?? 0;
            float viewportWidth = _trackViewport?.rect.width ?? 684f;
            float cardWidth = HistoricalFigureCardListItem.WidthForViewport(
                viewportWidth);
            float cardHeight = HistoricalFigureCardListItem.HeightForViewport(
                viewportWidth);
            _track.sizeDelta = new Vector2(
                HistoricalFigureCardDrawService.RollingCardCount * cardWidth,
                cardHeight);
            _lensTrack.sizeDelta = new Vector2(
                HistoricalFigureCardDrawService.RollingCardCount * cardWidth,
                cardHeight);
            while (_trackItems.Count < count)
                _trackItems.Add(HistoricalFigureCardListItem.Create(_track,
                    cardWidth, cardHeight));
            while (_lensTrackItems.Count < count)
            {
                HistoricalFigureCardListItem item =
                    HistoricalFigureCardListItem.Create(_lensTrack, cardWidth,
                        cardHeight);
                item.SetScale(ReferenceMagnifiedScale);
                _lensTrackItems.Add(item);
            }
            string mysteryName = Text(
                "aw_historical_figure_cards_mystery_gold",
                "\u795e\u79d8\u4eba\u7269");
            for (int i = 0; i < _trackItems.Count; i++)
            {
                bool visible = i < count;
                _trackItems[i].SetVisible(visible);
                _lensTrackItems[i].SetVisible(visible);
                if (!visible) continue;
                _trackItems[i].SetSize(cardWidth, cardHeight);
                _lensTrackItems[i].SetSize(cardWidth, cardHeight);
                _trackItems[i].SetScale(1f);
                _lensTrackItems[i].SetScale(ReferenceMagnifiedScale);
                _trackItems[i].SetCard(pCards[i], i == pSelectedIndex,
                    mysteryName);
                _trackItems[i].SetPosition(i * cardWidth);
                _lensTrackItems[i].SetCard(pCards[i], i == pSelectedIndex,
                    mysteryName);
                _lensTrackItems[i].SetPosition(i * cardWidth);
            }
            float progress = pTrackIndex < 0f ? 0f : Mathf.Clamp01(
                pTrackIndex / HistoricalFigureCardDrawService.WinnerIndex);
            float targetOffset = viewportWidth * .5f - cardWidth * .5f -
                HistoricalFigureCardDrawService.WinnerIndex * cardWidth;
            float backgroundOffset = Mathf.Lerp(-15f, targetOffset, progress);
            _track.anchoredPosition = new Vector2(backgroundOffset, 0f);
            float lensRadius = LensRadiusForViewport(viewportWidth);
            float lensOffset = lensRadius - cardWidth * .5f -
                pTrackIndex * cardWidth;
            _lensTrack.anchoredPosition = new Vector2(lensOffset, 0f);
        }

        private void UpdateCrates()
        {
            if (_collectionRoot == null) return;
            ShowOnlyPool(_crateButtons);
            HideMysteryGoldCard();
            for (int i = 0; i < _roleButtons.Count; i++)
            {
                _roleButtons[i].gameObject.SetActive(true);
                Image roleImage = _roleButtons[i].GetComponent<Image>();
                if (roleImage != null)
                    roleImage.color = i == (int)_selectedRole
                        ? new Color(.44f, .35f, .16f, 1f)
                        : new Color(.16f, .16f, .18f, .96f);
            }
            IReadOnlyList<HistoricalFigureCardCrate> crates =
                HistoricalFigureCardCrates.All;
            // Grid metrics are shared by every tile: compute once per refresh.
            float gridWidth = _collectionViewport?.rect.width ?? 438f;
            int gridColumns = Mathf.Max(3, Mathf.FloorToInt(
                (gridWidth + 6f) / 138f));
            float gridButtonWidth = (gridWidth - (gridColumns - 1) * 6f) /
                gridColumns;
            float gridTileHeight = CrateTileHeight(gridButtonWidth);
            bool layoutDirty = !Mathf.Approximately(gridButtonWidth,
                _crateLayoutWidth);
            _crateLayoutWidth = gridButtonWidth;
            while (_crateButtons.Count < crates.Count)
            {
                int index = _crateButtons.Count;
                Button button = MakeButton("Crate" + index,
                    _collectionRoot, "", () =>
                    {
                        if (index < crates.Count) SelectCrate(crates[index].Id);
                    });
                _crateButtons.Add(button);
            }
            for (int i = 0; i < _crateButtons.Count; i++)
            {
                bool visible = i < crates.Count;
                Button button = _crateButtons[i];
                if (button.gameObject.activeSelf != visible)
                    button.gameObject.SetActive(visible);
                if (!visible) continue;
                HistoricalFigureCardCrate crate = crates[i];
                Image image = button.GetComponent<Image>();
                if (image != null) image.color = CrateColor(i);
                bool created = EnsureCrateVisual(button);
                Image icon = ChildImage(button.transform, "CrateIcon");
                if (icon != null)
                    icon.sprite = SpriteTextureLoader.getSprite(crate.ImagePath) ??
                        SpriteTextureLoader.getSprite("ui/icons/iconKings") ??
                        SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
                Text title = ChildText(button.transform, "CrateTitle");
                Text count = ChildText(button.transform, "CrateCount");
                Text gold = ChildText(button.transform, "GoldPool");
                if (title != null) title.text = CrateName(crate);
                if (count != null)
                    count.text = Format("aw_historical_figure_cards_crate_count",
                        "{0} \u4eba\u7269", crate.CardCountFor(
                            IsMinisterOnlyCrate(crate.Id)
                                ? HistoricalFigureCardRole.Minister
                                : _selectedRole));
                if (gold != null)
                    gold.text = Text("aw_historical_figure_cards_shared_gold_badge",
                        "\u5171\u4eab\u91d1\u6c60");
                if (layoutDirty || created)
                    LayoutCrateVisual(button, gridButtonWidth);
                Position(button.GetComponent<RectTransform>(),
                    (i % gridColumns) * (gridButtonWidth + 6f),
                    -((i / gridColumns) * (gridTileHeight + 8f) + 2f),
                    gridButtonWidth, gridTileHeight);
                button.interactable = _state == DrawState.Idle;
            }
            _collectionRoot.sizeDelta = new Vector2(gridWidth,
                Mathf.Max(1f, Mathf.Ceil(crates.Count /
                    (float)gridColumns) * (gridTileHeight + 8f) + 4f));
            ResetCollectionScrollToTop();
        }

        private void UpdateInventory()
        {
            // Pool mutual exclusion happens in UpdateCardList/ShowOnlyPool.
            HideMysteryGoldCard();
            SetRoleButtonsVisible(false);
            int revision = Store.Revision;
            if (_inventoryCache == null ||
                _inventoryCacheRevision != revision ||
                _inventoryCacheSort != _inventorySort)
            {
                int snapshotRevision = Store.CopyInventorySnapshot(
                    out IReadOnlyDictionary<string, int> ownedCounts,
                    out IReadOnlyList<HistoricalFigureCardDrawRecord> draws);
                var latestRanks = new Dictionary<string, int>(StringComparer.Ordinal);
                int nextRank = 0;
                for (int i = draws.Count - 1; i >= 0; i--)
                {
                    string cardId = draws[i]?.CardId;
                    if (string.IsNullOrEmpty(cardId) || latestRanks.ContainsKey(cardId))
                        continue;
                    latestRanks[cardId] = nextRank++;
                }
                _inventoryCache = HistoricalFigureCardInventoryRules.Sort(
                    HistoricalFigureCardCatalog.All.Where(p => p != null &&
                        ownedCounts.TryGetValue(p.CardId, out int count) &&
                        count > 0), _inventorySort,
                    latestRanks);
                _inventoryCacheTotalOwned = _inventoryCache.Sum(p =>
                    ownedCounts.TryGetValue(p.CardId, out int count)
                        ? count : 0);
                _inventoryOwnedCounts = ownedCounts;
                _inventoryCacheRevision = snapshotRevision;
                _inventoryCacheSort = _inventorySort;
                UpdateInventoryStats(_inventoryCache, _inventoryCacheTotalOwned,
                    _inventoryOwnedCounts);
            }
            IReadOnlyList<HistoricalFigureCardDefinition> cards = _inventoryCache;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(
                cards.Count / (float)InventoryPageSize));
            _inventoryPage = Mathf.Clamp(_inventoryPage, 0, totalPages - 1);
            IReadOnlyList<HistoricalFigureCardDefinition> pageCards = cards
                .Skip(_inventoryPage * InventoryPageSize)
                .Take(InventoryPageSize)
                .ToArray();
            UpdateInventoryPagination(totalPages);
            int totalOwned = _inventoryCacheTotalOwned;
            if (cards.Count == 0)
                _body.text = Text("aw_historical_figure_cards_inventory_empty",
                    "\u4ed3\u5e93\u4e3a\u7a7a\u3002");
            else
                _body.text = Format("aw_historical_figure_cards_inventory_summary",
                    "{0} \u5f20\u4eba\u7269\u5361 \u00b7 \u6309{1}\u6392\u5e8f", totalOwned,
                    InventorySortName(_inventorySort));
            UpdateCardList(pageCards, true, _inventoryButtons);
        }

        private void UpdateCrateContents(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards)
        {
            // Pool mutual exclusion happens in UpdateCardList/ShowOnlyPool.
            SetRoleButtonsVisible(false);
            HistoricalFigureCardDefinition[] visibleCards =
                (pCards ?? Array.Empty<HistoricalFigureCardDefinition>())
                .Where(p => p?.Rarity != null &&
                            !p.Rarity.Equals(HistoricalFigureCardRarity.Gold))
                .ToArray();
            UpdateCardList(visibleCards, false, _crateCardButtons, 1);
            UpdateMysteryGoldCard(visibleCards.Length + 1);
        }

        // The crate / crate-content / inventory pools all live under
        // _collectionRoot. Relying on scattered HideButtons calls let stale
        // tiles from the previous view stay visible (crate art showing through
        // inventory cards), so every render routes through this single
        // mutual-exclusion point instead.
        private void ShowOnlyPool(List<Button> pActivePool)
        {
            if (!ReferenceEquals(_crateButtons, pActivePool))
                HideButtons(_crateButtons);
            if (!ReferenceEquals(_crateCardButtons, pActivePool))
                HideButtons(_crateCardButtons);
            if (!ReferenceEquals(_inventoryButtons, pActivePool))
                HideButtons(_inventoryButtons);
            if (ReferenceEquals(_virtualButtons, pActivePool)) return;
            // The virtual window pointed at another pool. Detach it before the
            // caller repopulates: ScrollRect.onValueChanged is always live, and
            // moving the content (ResetCollectionScrollToTop) would otherwise
            // call straight back into BindVisibleCards and re-enable the tiles
            // that were just hidden -- crate art showing through the crate
            // contents. UpdateCardList re-attaches; the crate grid leaves it
            // detached so scrolling the crate list binds nothing.
            _virtualButtons = null;
            _virtualCards = Array.Empty<HistoricalFigureCardDefinition>();
            _virtualFirstBound = -1;
            _virtualBoundCount = 0;
        }

        private void UpdateCardList(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            bool pOwnedOnly, List<Button> pButtons, int pStartIndex = 0)
        {
            IReadOnlyList<HistoricalFigureCardDefinition> cards =
                pCards ?? Array.Empty<HistoricalFigureCardDefinition>();
            if (_collectionRoot == null) return;
            ShowOnlyPool(pButtons);
            float gridWidth = _collectionViewport?.rect.width ?? 438f;
            int gridColumns = Mathf.Max(4, Mathf.FloorToInt(
                (gridWidth + 6f) / 108f));
            float gridButtonWidth = (gridWidth - (gridColumns - 1) * 6f) /
                gridColumns;
            float gridTileHeight = CardTileHeight(gridButtonWidth);
            bool layoutDirty = !Mathf.Approximately(gridButtonWidth,
                _cardLayoutWidth);
            _cardLayoutWidth = gridButtonWidth;

            _virtualCards = cards;
            _virtualButtons = pButtons;
            _virtualOwnedOnly = pOwnedOnly;
            _virtualStartIndex = pStartIndex;
            _virtualColumns = gridColumns;
            _virtualButtonWidth = gridButtonWidth;
            _virtualTileHeight = gridTileHeight;

            _collectionRoot.sizeDelta = new Vector2(gridWidth,
                Mathf.Max(1f, Mathf.Ceil((cards.Count + pStartIndex) /
                    (float)gridColumns) * (gridTileHeight + 8f) + 4f));
            // New list contents: jump back to the top and force a full rebind
            // (the previous binding window is meaningless for the new data).
            ResetCollectionScrollToTop();
            _virtualFirstBound = -1;
            _virtualBoundCount = 0;
            // Switching crates reuses the same pool. A tile that will not be
            // rebound this pass (the new crate is shorter, or the incremental
            // fill has not reached it yet) would keep showing the previous
            // crate's card underneath the new one, so blank the pool first and
            // let BindVisibleCards re-enable exactly what it binds.
            HideButtons(pButtons);
            BindVisibleCards(layoutDirty);
        }

        // Instantiates only the number of tile buttons that fit the viewport
        // plus a small buffer, then binds the rows the scroller is currently
        // showing to them. Scrolling re-binds instead of recreating.
        private void BindVisibleCards(bool pLayoutDirty)
        {
            if (_collectionScroll == null || _virtualButtons == null) return;
            int total = _virtualCards.Count + _virtualStartIndex;
            int topRow = Mathf.Max(0, Mathf.FloorToInt(
                Mathf.Abs(_collectionRoot.anchoredPosition.y) /
                (_virtualTileHeight + 8f)));
            float viewportHeight = _collectionViewport?.rect.height ??
                (_virtualTileHeight * 4f);
            int visibleRows = Mathf.Max(1, Mathf.CeilToInt(
                viewportHeight / (_virtualTileHeight + 8f)) + 2);
            int firstVisible = topRow * _virtualColumns - _virtualStartIndex;
            int lastVisible = (topRow + visibleRows) * _virtualColumns -
                _virtualStartIndex;
            firstVisible = Mathf.Max(0, firstVisible);
            lastVisible = Mathf.Min(total, lastVisible);

            int needed = Mathf.Max(0, lastVisible - firstVisible);
            int buffer = _virtualColumns; // keep a spare row for fast scrolling
            int desired = Mathf.Min(total > 0 ? Mathf.Max(needed + buffer,
                _virtualColumns * 4) : 0, total);
            // Creating a tile means instantiating six GameObjects
            // (button + portrait + name + meta + badge + rarity bar), so
            // building a whole viewport at once is what made the first switch
            // into the warehouse hitch. Cap how many are born per call and let
            // the next frame's scroll/refresh finish the job -- the tiles that
            // do not exist yet simply stay unbound for one frame.
            int budget = MaxTileBuildsPerFrame;
            while (_virtualButtons.Count < desired && budget-- > 0)
            {
                int index = _virtualButtons.Count;
                _virtualButtons.Add(MakeButton("Card" + index,
                    _collectionRoot, "", null));
            }
            // Still short of the viewport: ask Update to continue next frame.
            _virtualPoolIncomplete = _virtualButtons.Count < desired;
            desired = Mathf.Min(desired, _virtualButtons.Count);
            // Never shrink the pool by removing entries: the caller owns this
            // list (HideButtons walks it when switching between the crate and
            // inventory views) and a removed button would be orphaned on
            // screen forever. Surplus tiles stay in the list, just disabled.

            // Rebind only when the visible window actually moved, otherwise a
            // drag would re-run the whole bind loop on every scroll event.
            // While the pool is still filling the bound count changes every
            // frame, so that check lets those passes through on its own.
            if (!pLayoutDirty && firstVisible == _virtualFirstBound &&
                desired == _virtualBoundCount) return;

            for (int i = 0; i < _virtualButtons.Count; i++)
            {
                int index = firstVisible + i;
                bool bound = i < desired && index >= 0 && index < total;
                Button button = _virtualButtons[i];
                // A slot with no card behind it (the reserved mystery-gold
                // index) must be switched off, not merely skipped: leaving it
                // active keeps the previous crate's content AND its old
                // position, which is what stacked a stale card on top of a
                // neighbouring tile when switching crates.
                bool hasCard = bound &&
                    IsBoundCardIndex(index);
                if (button.gameObject.activeSelf != hasCard)
                    button.gameObject.SetActive(hasCard);
                if (!hasCard) continue;
                BindCardTile(button, index, pLayoutDirty);
            }
            _virtualFirstBound = firstVisible;
            _virtualBoundCount = desired;
        }

        /// <summary>
        ///     该网格序号背后是否真有一张卡。
        ///     箱子内容视图把 0 号位留给「神秘金色人物」(_virtualStartIndex=1),
        ///     那一格由 UpdateMysteryGoldCard 单独画,不属于池子。
        /// </summary>
        private bool IsBoundCardIndex(int pIndex)
        {
            int cardIndex = pIndex - _virtualStartIndex;
            return cardIndex >= 0 && cardIndex < _virtualCards.Count;
        }

        private void BindCardTile(Button pButton, int pIndex,
            bool pLayoutDirty)
        {
            int cardIndex = pIndex - _virtualStartIndex;
            HistoricalFigureCardDefinition card = cardIndex < 0 ||
                cardIndex >= _virtualCards.Count ? null : _virtualCards[cardIndex];
            if (card == null) return;
            if (_virtualOwnedOnly)
                BindInventoryCardSelection(pButton, card.CardId);
            bool created = EnsureCardVisual(pButton);
            Image image = pButton.GetComponent<Image>();
            if (image != null)
            {
                Color rarityColor = ParseColor(card.Rarity?.ColorHex,
                    new Color(.25f, .22f, .18f, .95f));
                image.sprite = CardGradientSprite(rarityColor);
                image.color = Color.white;
                Image rarityBar = ChildImage(pButton.transform, "RarityBar");
                if (rarityBar != null) rarityBar.color = rarityColor;
            }
            Text name = ChildText(pButton.transform, "CardName");
            Text meta = ChildText(pButton.transform, "CardMeta");
            Text owned = ChildText(pButton.transform, "OwnedBadge");
            Image portrait = ChildImage(pButton.transform, "CardPortrait");
            if (name != null) name.text = card.DisplayName ?? "-";
            if (meta != null)
                meta.text = (card.HistoricalKingdomName ?? "") + "  " +
                    RarityName(card.Rarity);
            if (owned != null)
            {
                owned.gameObject.SetActive(_virtualOwnedOnly);
                owned.text = "x" + (_inventoryOwnedCounts.TryGetValue(
                    card.CardId, out int count) ? count : 0);
            }
            if (portrait != null)
            {
                Sprite sprite = string.IsNullOrEmpty(card.PortraitPath)
                    ? null : SpriteTextureLoader.getSprite(card.PortraitPath);
                portrait.sprite = sprite ??
                    SpriteTextureLoader.getSprite("ui/icons/iconKings") ??
                    SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
            }
            if (pLayoutDirty || created)
                LayoutCardVisual(pButton, _virtualButtonWidth);
            Position(pButton.GetComponent<RectTransform>(),
                (pIndex % _virtualColumns) * (_virtualButtonWidth + 6f),
                -((pIndex / _virtualColumns) * (_virtualTileHeight + 8f) + 2f),
                _virtualButtonWidth, _virtualTileHeight);
            pButton.interactable = _virtualOwnedOnly && _state == DrawState.Idle;
        }

        private void ResetCollectionScrollToTop()
        {
            if (_collectionScroll == null || _collectionRoot == null) return;
            // No Canvas.ForceUpdateCanvases() here: this runs on every refresh
            // and a forced global canvas rebuild dominated the frame cost once
            // the inventory held a few dozen cards. Anchoring the content back
            // to the top is enough; the ScrollRect settles on the next layout.
            _collectionScroll.StopMovement();
            _collectionRoot.anchoredPosition = Vector2.zero;
        }

        private void BindInventoryCardSelection(Button pButton, string pCardId)
        {
            if (pButton == null) return;
            pButton.onClick.RemoveAllListeners();
            pButton.onClick.AddListener(() =>
            {
                HistoricalFigureCardAudioService.PlayButtonPress();
                SelectCollectionCard(pCardId);
            });
        }

        private void UpdateMysteryGoldCard(int pTotalCount)
        {
            if (_collectionRoot == null) return;
            if (_mysteryGoldButton == null)
            {
                _mysteryGoldButton = MakeButton("MysteryGold",
                    _collectionRoot, "", null);
                EnsureCardVisual(_mysteryGoldButton);
                Text mark = MakeText("MysteryMark",
                    _mysteryGoldButton.transform, 24, TextAnchor.MiddleCenter);
                mark.text = "?";
                mark.color = new Color(1f, .84f, .16f, 1f);
            }
            _mysteryGoldButton.gameObject.SetActive(true);
            _mysteryGoldButton.interactable = false;
            Image background = _mysteryGoldButton.GetComponent<Image>();
            Color gold = ParseColor(HistoricalFigureCardRarity.Gold.ColorHex,
                new Color(1f, .84f, 0f, 1f));
            if (background != null)
            {
                background.sprite = CardGradientSprite(gold);
                background.color = Color.white;
            }
            Image portrait = ChildImage(_mysteryGoldButton.transform,
                "CardPortrait");
            if (portrait != null)
            {
                portrait.gameObject.SetActive(true);
                portrait.sprite = SpriteTextureLoader.getSprite(
                    MysteryPortraitPath);
                portrait.preserveAspect = true;
            }
            Image rarity = ChildImage(_mysteryGoldButton.transform, "RarityBar");
            if (rarity != null) rarity.color = gold;
            Text name = ChildText(_mysteryGoldButton.transform, "CardName");
            if (name != null)
                name.text = Text("aw_historical_figure_cards_mystery_gold",
                    "\u795e\u79d8\u4eba\u7269");
            Text meta = ChildText(_mysteryGoldButton.transform, "CardMeta");
            if (meta != null)
                meta.text = Text("aw_historical_figure_cards_mystery_gold_meta",
                    "\u91d1\u8272 \u00b7 \u5171\u4eab\u5927\u5956\u6c60");
            Text owned = ChildText(_mysteryGoldButton.transform, "OwnedBadge");
            if (owned != null) owned.gameObject.SetActive(false);

            float viewportWidth = _collectionViewport?.rect.width ?? 438f;
            int columns = Mathf.Max(4, Mathf.FloorToInt(
                (viewportWidth + 6f) / 108f));
            float buttonWidth = (viewportWidth - (columns - 1) * 6f) / columns;
            LayoutCardVisual(_mysteryGoldButton, buttonWidth);
            Text mysteryMark = ChildText(_mysteryGoldButton.transform,
                "MysteryMark");
            if (mysteryMark != null)
            {
                mysteryMark.gameObject.SetActive(false);
                Position(mysteryMark.rectTransform, 6f, -4f,
                    buttonWidth - 12f, CardTileHeight(buttonWidth) - 18f);
            }
            float tileHeight = CardTileHeight(buttonWidth);
            Position(_mysteryGoldButton.GetComponent<RectTransform>(), 0f, -2f,
                buttonWidth, tileHeight);
            _collectionRoot.sizeDelta = new Vector2(viewportWidth,
                Mathf.Max(1f, Mathf.Ceil(pTotalCount / (float)columns) *
                    (tileHeight + 8f) + 4f));
        }

        private void HideMysteryGoldCard()
        {
            _mysteryGoldButton?.gameObject.SetActive(false);
        }

        private void SetRoleButtonsVisible(bool pVisible)
        {
            foreach (Button button in _roleButtons)
                button.gameObject.SetActive(pVisible);
        }

        private void CreateInventoryControls()
        {
            HistoricalFigureCardInventorySort[] sorts =
            {
                HistoricalFigureCardInventorySort.Latest,
                HistoricalFigureCardInventorySort.Rarity,
                HistoricalFigureCardInventorySort.Name,
                HistoricalFigureCardInventorySort.Fame
            };
            foreach (HistoricalFigureCardInventorySort sort in sorts)
            {
                HistoricalFigureCardInventorySort selectedSort = sort;
                Button button = MakeButton("Sort" + sort, _root,
                    InventorySortName(sort), () =>
                    {
                        if (!_inventoryMode || _state != DrawState.Idle) return;
                        _inventorySort = selectedSort;
                        _inventoryPage = 0;
                        Refresh();
                    });
                _sortButtons.Add(button);
            }
            foreach (HistoricalFigureCardRarity rarity in
                     HistoricalFigureCardRarity.All)
            {
                Text stat = MakeText("InventoryStat" + rarity.Id, _root, 7,
                    TextAnchor.MiddleCenter);
                stat.color = ParseColor(rarity.ColorHex, Color.white);
                _rarityStats.Add(stat);
            }
            _inventoryPreviousPage = MakeButton("InventoryPreviousPage", _root,
                Text("aw_historical_figure_cards_previous_page", "上一页"),
                () => ChangeInventoryPage(-1));
            _inventoryPageLabel = MakeText("InventoryPageLabel", _root, 7,
                TextAnchor.MiddleCenter);
            _inventoryNextPage = MakeButton("InventoryNextPage", _root,
                Text("aw_historical_figure_cards_next_page", "下一页"),
                () => ChangeInventoryPage(1));
            SetInventoryControlsVisible(false);
        }

        private void ChangeInventoryPage(int pDelta)
        {
            if (!_inventoryMode || _state != DrawState.Idle ||
                _inventoryCache == null) return;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(
                _inventoryCache.Count / (float)InventoryPageSize));
            _inventoryPage = Mathf.Clamp(_inventoryPage + pDelta, 0,
                totalPages - 1);
            Refresh();
        }

        private void UpdateInventoryPagination(int pTotalPages)
        {
            _inventoryPageLabel.text = Format(
                "aw_historical_figure_cards_page", "{0}/{1}",
                _inventoryPage + 1, pTotalPages);
            _inventoryPreviousPage.interactable = _inventoryPage > 0;
            _inventoryNextPage.interactable = _inventoryPage < pTotalPages - 1;
        }

        private static Scrollbar CreateVerticalScrollbar(string pName,
            Transform pParent)
        {
            GameObject trackObject = new GameObject(pName,
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            trackObject.transform.SetParent(pParent, false);
            Image trackImage = trackObject.GetComponent<Image>();
            trackImage.sprite = WhiteSprite();
            trackImage.color = new Color(.07f, .065f, .055f, .96f);

            GameObject slidingObject = new GameObject("Sliding Area",
                typeof(RectTransform));
            slidingObject.transform.SetParent(trackObject.transform, false);
            RectTransform sliding = slidingObject.GetComponent<RectTransform>();
            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = new Vector2(1f, 1f);
            sliding.offsetMax = new Vector2(-1f, -1f);

            GameObject handleObject = new GameObject("Handle",
                typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(sliding, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.sprite = WhiteSprite();
            handleImage.color = new Color(.76f, .61f, .28f, 1f);

            Scrollbar scrollbar = trackObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private void SetInventoryControlsVisible(bool pVisible)
        {
            HistoricalFigureCardInventorySort[] sorts =
            {
                HistoricalFigureCardInventorySort.Latest,
                HistoricalFigureCardInventorySort.Rarity,
                HistoricalFigureCardInventorySort.Name,
                HistoricalFigureCardInventorySort.Fame
            };
            for (int i = 0; i < _sortButtons.Count; i++)
            {
                Button button = _sortButtons[i];
                button.gameObject.SetActive(pVisible);
                Text label = button.GetComponentInChildren<Text>();
                if (label != null && i < sorts.Length)
                    label.text = InventorySortName(sorts[i]);
                Image image = button.GetComponent<Image>();
                if (image != null && i < sorts.Length)
                    image.color = sorts[i] == _inventorySort
                        ? new Color(.44f, .35f, .16f, 1f)
                        : new Color(.16f, .16f, .18f, .96f);
            }
            foreach (Text stat in _rarityStats)
                stat.gameObject.SetActive(pVisible);
            if (_recycleModeButton != null)
            {
                _recycleModeButton.gameObject.SetActive(pVisible);
                SetButtonText(_recycleModeButton,
                    Text("aw_historical_figure_cards_recycle_mode",
                        "\u6c70\u6362"));
            }
            if (_inventoryPreviousPage != null)
            {
                _inventoryPreviousPage.gameObject.SetActive(pVisible);
                _inventoryPageLabel.gameObject.SetActive(pVisible);
                _inventoryNextPage.gameObject.SetActive(pVisible);
                SetButtonText(_inventoryPreviousPage, Text(
                    "aw_historical_figure_cards_previous_page", "上一页"));
                SetButtonText(_inventoryNextPage, Text(
                    "aw_historical_figure_cards_next_page", "下一页"));
            }
        }

        private void UpdateInventoryStats(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            int pTotalOwned,
            IReadOnlyDictionary<string, int> pOwnedCounts)
        {
            for (int i = 0; i < _rarityStats.Count &&
                            i < HistoricalFigureCardRarity.All.Count; i++)
            {
                HistoricalFigureCardRarity rarity =
                    HistoricalFigureCardRarity.All[i];
                int count = (pCards ?? Array.Empty<HistoricalFigureCardDefinition>())
                    .Where(p => p?.Rarity != null && p.Rarity.Equals(rarity))
                    .Sum(p => pOwnedCounts != null &&
                        pOwnedCounts.TryGetValue(p.CardId, out int owned)
                            ? owned : 0);
                float percent = pTotalOwned <= 0 ? 0f :
                    count * 100f / pTotalOwned;
                _rarityStats[i].text = Format(
                    "aw_historical_figure_cards_inventory_stat",
                    "{0} {1} · {2:0.0}%", rarity.ShortName, count, percent);
            }
        }

        private void UpdateRevealPanel()
        {
            if (_revealPanel == null || _selectedCard == null) return;
            Color rarityColor = ParseColor(_selectedCard.Rarity?.ColorHex,
                Color.white);
            Color panelColor = Color.Lerp(new Color(.04f, .045f, .055f, 1f),
                rarityColor, .18f);
            panelColor.a = .99f;
            _revealPanel.color = panelColor;
            _revealName.text = _selectedCard.DisplayName ?? "-";
            _revealName.color = Color.white;
            _revealRarity.text = Format(
                "aw_historical_figure_cards_reveal_result",
                "{0} · {1:0.00}%", _selectedCard.Rarity?.DisplayName ?? "-",
                (_selectedCard.Rarity?.Probability ?? 0f) * 100f);
            _revealRarity.text = Format(
                "aw_historical_figure_cards_reveal_result",
                "{0} - {1:0.00}%", RarityName(_selectedCard.Rarity),
                (_selectedCard.Rarity?.Probability ?? 0f) * 100f);
            _revealRarity.color = rarityColor;
            _revealBar.color = rarityColor;
            _revealMeta.text = Format(
                "aw_historical_figure_cards_reveal_meta_extended",
                "\u56fd\u53f7\uff1a{0}\n\u671d\u4ee3\uff1a{1}\n\u540d\u6c14\uff1a{2}\n\u7c7b\u578b\uff1a{3}\n\u6765\u6e90\uff1a{4}",
                _selectedCard.HistoricalKingdomName,
                _selectedCard.DynastyName, _selectedCard.FameScore,
                CardRoleName(_selectedCard),
                string.IsNullOrEmpty(_selectedCard.CollectionId)
                    ? "-" : CollectionDisplayName(_selectedCard.CollectionId));
            _revealBiography.text = Format(
                "aw_historical_figure_cards_reveal_identity",
                "\u751f\u5352\uff1a{0} - {1}\n\u7236\uff1a{2}\n\u6bcd\uff1a{3}\n\u80cc\u666f\uff1a{4}\n\n\u8be6\u7ec6\u4ecb\u7ecd\uff1a{5}",
                HistoricalYearText(_selectedCard.BirthYear),
                HistoricalYearText(_selectedCard.DeathYear),
                _selectedCard.ParentDisplayName(true),
                _selectedCard.ParentDisplayName(false),
                _selectedCard.BackgroundSummary ?? "",
                _selectedCard.DetailedBiography ?? _selectedCard.Biography ?? "");
            LayoutRevealBiography(true);
            Sprite portrait = string.IsNullOrEmpty(_selectedCard.PortraitPath)
                ? null
                : SpriteTextureLoader.getSprite(_selectedCard.PortraitPath);
            _revealPortrait.sprite = portrait ??
                SpriteTextureLoader.getSprite("ui/icons/iconKings") ??
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
        }

        private void LayoutRevealBiography(bool pResetScroll)
        {
            if (_revealBiographyViewport == null ||
                _revealBiographyContent == null ||
                _revealBiography == null) return;
            float viewportWidth = Mathf.Max(1f,
                _revealBiographyViewport.sizeDelta.x);
            float viewportHeight = Mathf.Max(1f,
                _revealBiographyViewport.sizeDelta.y);
            Position(_revealBiography.rectTransform, 0f, 0f,
                viewportWidth - 2f, viewportHeight);
            float contentHeight = Mathf.Max(viewportHeight,
                _revealBiography.preferredHeight + 4f);
            _revealBiographyContent.sizeDelta = new Vector2(
                viewportWidth - 2f, contentHeight);
            Position(_revealBiography.rectTransform, 0f, 0f,
                viewportWidth - 2f, contentHeight);
            if (!pResetScroll || _revealBiographyScroll == null) return;
            _revealBiographyScroll.StopMovement();
            _revealBiographyContent.anchoredPosition = Vector2.zero;
            _revealBiographyScroll.verticalNormalizedPosition = 1f;
        }

        private void UpdateRevealActionLabels()
        {
            SetButtonText(_quickOpen, _quickOpening
                ? Text("aw_historical_figure_cards_quick_on", "\u5feb\u901f\uff1a\u5f00")
                : Text("aw_historical_figure_cards_quick_off", "\u5feb\u901f\uff1a\u5173"));
            SetButtonText(_autoOpen, _autoOpening
                ? Text("aw_historical_figure_cards_auto_stop", "\u505c\u6b62\u8fde\u5f00")
                : Text("aw_historical_figure_cards_auto_open", "\u81ea\u52a8\u8fde\u5f00"));
            SetButtonText(_sound, HistoricalFigureCardAudioService.Enabled
                ? Text("aw_historical_figure_cards_sound_on", "\u58f0\u97f3\uff1a\u5f00")
                : Text("aw_historical_figure_cards_sound_off", "\u58f0\u97f3\uff1a\u5173"));
            if (_autoOpen != null) _autoOpen.interactable = _quickOpening;
        }

        private static string InventorySortName(
            HistoricalFigureCardInventorySort pSort)
        {
            switch (pSort)
            {
                case HistoricalFigureCardInventorySort.Rarity:
                    return Text("aw_historical_figure_cards_sort_rarity", "\u7a00\u6709\u5ea6");
                case HistoricalFigureCardInventorySort.Name:
                    return Text("aw_historical_figure_cards_sort_name", "\u59d3\u540d");
                case HistoricalFigureCardInventorySort.Fame:
                    return Text("aw_historical_figure_cards_sort_fame", "\u540d\u6c14");
                default:
                    return Text("aw_historical_figure_cards_sort_latest", "\u6700\u65b0");
            }
        }

        private static bool EnsureCrateVisual(Button pButton)
        {
            if (pButton == null || pButton.transform.Find("CrateTitle") != null)
                return false;
            Text title = pButton.GetComponentInChildren<Text>();
            if (title != null)
            {
                title.gameObject.name = "CrateTitle";
                title.fontSize = 8;
                title.alignment = TextAnchor.MiddleCenter;
            }
            Image icon = new GameObject("CrateIcon", typeof(RectTransform),
                typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(pButton.transform, false);
            icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconKings") ??
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Text count = MakeText("CrateCount", pButton.transform, 7,
                TextAnchor.MiddleCenter);
            count.color = new Color(.78f, .79f, .82f, 1f);
            Text gold = MakeText("GoldPool", pButton.transform, 6,
                TextAnchor.MiddleCenter);
            gold.color = new Color(1f, .84f, .28f, 1f);
            return true;
        }

        private static void LayoutCrateVisual(Button pButton, float pWidth)
        {
            Image icon = ChildImage(pButton.transform, "CrateIcon");
            Text title = ChildText(pButton.transform, "CrateTitle");
            Text count = ChildText(pButton.transform, "CrateCount");
            Text gold = ChildText(pButton.transform, "GoldPool");
            if (icon != null)
                Position(icon.rectTransform, (pWidth - 62f) * .5f, -5f,
                    62f, 48f);
            if (title != null)
                Position(title.rectTransform, 4f, -54f, pWidth - 8f, 16f);
            if (count != null)
                Position(count.rectTransform, 4f, -70f, pWidth - 8f, 12f);
            if (gold != null)
                Position(gold.rectTransform, 4f, -81f, pWidth - 8f, 10f);
        }

        private static bool EnsureCardVisual(Button pButton)
        {
            if (pButton == null || pButton.transform.Find("CardName") != null)
                return false;
            Text name = pButton.GetComponentInChildren<Text>();
            if (name != null)
            {
                name.gameObject.name = "CardName";
                name.fontSize = 8;
                name.alignment = TextAnchor.MiddleCenter;
            }
            Image portrait = new GameObject("CardPortrait", typeof(RectTransform),
                typeof(Image)).GetComponent<Image>();
            portrait.transform.SetParent(pButton.transform, false);
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            Text meta = MakeText("CardMeta", pButton.transform, 6,
                TextAnchor.MiddleCenter);
            meta.color = new Color(.82f, .83f, .86f, 1f);
            Text owned = MakeText("OwnedBadge", pButton.transform, 7,
                TextAnchor.MiddleCenter);
            Image rarity = new GameObject("RarityBar", typeof(RectTransform),
                typeof(Image)).GetComponent<Image>();
            rarity.transform.SetParent(pButton.transform, false);
            rarity.sprite = WhiteSprite();
            rarity.raycastTarget = false;
            return true;
        }

        private static void LayoutCardVisual(Button pButton, float pWidth)
        {
            Image portrait = ChildImage(pButton.transform, "CardPortrait");
            Image rarity = ChildImage(pButton.transform, "RarityBar");
            Text name = ChildText(pButton.transform, "CardName");
            Text meta = ChildText(pButton.transform, "CardMeta");
            Text owned = ChildText(pButton.transform, "OwnedBadge");
            float imageHeight = Mathf.Max(24f, pWidth - 12f) * .75f;
            if (portrait != null)
                Position(portrait.rectTransform, 6f, -4f, pWidth - 12f,
                    imageHeight);
            if (name != null)
                Position(name.rectTransform, 4f, -(imageHeight + 8f),
                    pWidth - 8f, 15f);
            if (meta != null)
                Position(meta.rectTransform, 4f, -(imageHeight + 23f),
                    pWidth - 8f, 13f);
            if (owned != null)
                Position(owned.rectTransform, pWidth - 30f, -4f, 26f, 13f);
            if (rarity != null)
                Position(rarity.rectTransform, 0f, -CardTileHeight(pWidth) + 6f,
                    pWidth, 6f);
        }

        private static Image ChildImage(Transform pParent, string pName)
        {
            return pParent?.Find(pName)?.GetComponent<Image>();
        }

        private static Text ChildText(Transform pParent, string pName)
        {
            return pParent?.Find(pName)?.GetComponent<Text>();
        }

        private static void SetButtonText(Button pButton, string pText)
        {
            Text label = pButton?.GetComponentInChildren<Text>();
            if (label != null) label.text = pText ?? "";
        }

        private static string RarityName(HistoricalFigureCardRarity pRarity)
        {
            if (pRarity == null) return "-";
            return Text("aw_historical_figure_cards_rarity_" + pRarity.Id,
                pRarity.DisplayName ?? pRarity.Id);
        }

        private static string CardRoleName(
            HistoricalFigureCardDefinition pCard)
        {
            if (pCard?.Role == HistoricalFigureCardRole.Minister)
                return pCard.IsMilitaryGeneral
                    ? Text("aw_historical_figure_cards_type_general", "武将")
                    : Text("aw_historical_figure_cards_type_civil", "文臣");
            return Text("aw_historical_figure_cards_type_monarch", "君主");
        }

        private static float LensRadiusForViewport(float pViewportWidth)
        {
            return pViewportWidth < 640f ? MobileLensRadius : DesktopLensRadius;
        }

        private static float CrateTileHeight(float pWidth)
        {
            float imageWidth = Mathf.Max(24f, pWidth - 8f);
            return imageWidth * .75f + 43f;
        }

        private static float CardTileHeight(float pWidth)
        {
            float imageWidth = Mathf.Max(24f, pWidth - 12f);
            return imageWidth * .75f + 42f;
        }

        private static Color CrateColor(int pIndex)
        {
            Color[] colors =
            {
                new Color(.24f, .18f, .12f, .98f),
                new Color(.14f, .22f, .20f, .98f),
                new Color(.16f, .18f, .27f, .98f),
                new Color(.27f, .18f, .15f, .98f),
                new Color(.18f, .22f, .13f, .98f),
                new Color(.23f, .16f, .24f, .98f)
            };
            return colors[Mathf.Abs(pIndex) % colors.Length];
        }

        private static Color ParseColor(string pHex, Color pFallback)
        {
            return !string.IsNullOrEmpty(pHex) &&
                   ColorUtility.TryParseHtmlString(pHex, out Color color)
                ? color
                : pFallback;
        }

        private static float CaseOpeningEase(float pProgress)
        {
            float targetX = Mathf.Clamp01(pProgress);
            float low = 0f;
            float high = 1f;
            float t = targetX;
            for (int i = 0; i < 12; i++)
            {
                float x = CubicBezier(t, .1f, .4f);
                if (x < targetX) low = t;
                else high = t;
                t = (low + high) * .5f;
            }
            return CubicBezier(t, .4f, 1f);
        }

        private static float CubicBezier(float pT, float pFirst,
            float pSecond)
        {
            float inverse = 1f - pT;
            return 3f * inverse * inverse * pT * pFirst +
                   3f * inverse * pT * pT * pSecond + pT * pT * pT;
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f), new Vector2(.5f, .5f), 1f);
            return _whiteSprite;
        }

        private static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "HistoricalFigureCardLensMask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * .5f;
            float radius = center - .5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y),
                    new Vector2(center, center));
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(
                    radius - distance + 1f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f), size);
            _circleSprite.name = "HistoricalFigureCardLensMask";
            _circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return _circleSprite;
        }

        private static Sprite EdgeFadeSprite(bool pFromLeft)
        {
            Sprite cached = pFromLeft ? _leftEdgeFadeSprite :
                _rightEdgeFadeSprite;
            if (cached != null) return cached;
            const int width = 32;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = pFromLeft ? "HistoricalCardLeftEdgeFade" :
                    "HistoricalCardRightEdgeFade",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[width];
            for (int x = 0; x < width; x++)
            {
                float progress = x / (float)(width - 1);
                float alpha = pFromLeft ? 1f - progress : progress;
                pixels[x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(alpha * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, 1f),
                new Vector2(.5f, .5f), 1f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            if (pFromLeft) _leftEdgeFadeSprite = sprite;
            else _rightEdgeFadeSprite = sprite;
            return sprite;
        }

        private static Sprite CardGradientSprite(Color pRarityColor)
        {
            string key = ColorUtility.ToHtmlStringRGBA(pRarityColor);
            if (_cardGradientSprites.TryGetValue(key, out Sprite cached))
                return cached;
            const int height = 16;
            Texture2D texture = new Texture2D(1, height, TextureFormat.RGBA32,
                false)
            {
                name = "HistoricalCardGradient_" + key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color32[] pixels = new Color32[height];
            Color top = new Color(.36f, .36f, .39f, 1f);
            for (int y = 0; y < height; y++)
            {
                float progress = y / (float)(height - 1);
                pixels[y] = Color.Lerp(top, pRarityColor, progress);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f,
                height), new Vector2(.5f, .5f), 1f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _cardGradientSprites[key] = sprite;
            return sprite;
        }

        private static void HideButtons(IEnumerable<Button> pButtons)
        {
            foreach (Button button in pButtons ?? Enumerable.Empty<Button>())
                button?.gameObject.SetActive(false);
        }

        private static Text MakeText(string pName, Transform pParent, int pSize,
            TextAnchor pAnchor)
        {
            Text text = new GameObject(pName, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(pParent, false);
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static string Text(string pKey, string pFallback)
        {
            return AW_L10n.Text(pKey, pFallback);
        }

        private static string CrateName(HistoricalFigureCardCrate pCrate)
        {
            return pCrate == null ? "-" : Text(pCrate.NameKey, pCrate.DisplayName);
        }

        private static string CrateDescription(HistoricalFigureCardCrate pCrate)
        {
            return pCrate == null ? "-" :
                Text(pCrate.DescriptionKey, pCrate.Description);
        }

        private static bool IsMinisterOnlyCrate(string pCrateId)
        {
            return string.Equals(pCrateId,
                HistoricalFigureCardSupporterSeeds.CollectionId,
                StringComparison.Ordinal);
        }

        private static string CollectionDisplayName(string pCollectionId)
        {
            HistoricalFigureCardCrate crate =
                HistoricalFigureCardCrates.Get(pCollectionId);
            return crate == null ? pCollectionId : CrateName(crate);
        }

        private static string Format(string pKey, string pFallback,
            params object[] pArgs)
        {
            return string.Format(Text(pKey, pFallback), pArgs);
        }

        private static Button MakeButton(string pName, Transform pParent,
            string pText, Action pAction)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = new Color(.16f, .17f, .19f, .94f);
            Button button = obj.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
            colors.pressedColor = new Color(.74f, .74f, .74f, 1f);
            colors.disabledColor = new Color(.48f, .48f, .48f, .7f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                HistoricalFigureCardAudioService.PlayButtonPress();
                pAction?.Invoke();
            });
            EventTrigger trigger = obj.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerEnter
            };
            entry.callback.AddListener(_ =>
                HistoricalFigureCardAudioService.PlayItemHover());
            trigger.triggers.Add(entry);
            Text text = MakeText("Text", obj.transform, 8, TextAnchor.MiddleCenter);
            text.text = pText;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(2f, 1f);
            text.rectTransform.offsetMax = new Vector2(-2f, -1f);
            return button;
        }

        private static void Position(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static void PositionCentered(RectTransform pRect, float pX,
            float pY, float pWidth, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(.5f, .5f);
            pRect.anchoredPosition = new Vector2(pX + pWidth * .5f,
                pY - pHeight * .5f);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            // 42/58 是标题栏 + 内边距的留白。高度这里只留 40:原来的 58 把
            // 底部又白扣了 18px,而底部按钮行本来就画在这个盒子里
            // (-height + 28),于是滚动视口的父物体被凭空压矮一截,
            // 卡片网格看起来只占窗口的一小半。
            float height = Mathf.Max(1f, _windowSize.y - 40f);
            bool collectionMode = _collectionViewport != null &&
                _collectionViewport.gameObject.activeSelf;
            bool trackMode = _trackViewport != null &&
                _trackViewport.gameObject.activeSelf;
            bool revealMode = _state == DrawState.Reveal || _state == DrawState.Details;
            bool inventoryControls = _inventoryMode &&
                _state == DrawState.Idle;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;

            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * .56f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text("aw_historical_figure_cards_title",
                    "\u5386\u53f2\u4eba\u7269\u62bd\u5361");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
                RectTransform titleTextRect = window.titleText.GetComponent<RectTransform>();
                if (titleTextRect != null)
                    titleTextRect.sizeDelta = new Vector2(_windowSize.x * .46f, 28f);
                window.titleText.raycastTarget = false;
            }

            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect = nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(width, height);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeScrollComponent = nativeScroll?.GetComponent<ScrollRect>();
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
            RectTransform nativeViewport = ContentTransform?.parent as RectTransform;
            if (nativeViewport != null) nativeViewport.sizeDelta = new Vector2(width, height);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
            {
                nativeContent.anchorMin = new Vector2(0f, 1f);
                nativeContent.anchorMax = new Vector2(0f, 1f);
                nativeContent.pivot = new Vector2(0f, 1f);
                nativeContent.anchoredPosition = Vector2.zero;
                nativeContent.sizeDelta = new Vector2(width, height);
            }

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            Position(_stageBackdrop.rectTransform, 0f, 0f, width, height);
            Position(_stageShade.rectTransform, 0f, 0f, width, height);
            PositionCentered(_openingSliderStage, 0f, 0f, width, height);
            // In the grid modes the top chrome is compressed so the card /
            // crate grid gets the bulk of the window instead of sitting in the
            // lower third under a band of empty space.
            bool compactHeader = collectionMode && !trackMode;
            // 抬头整体上移 10px,正文压成一行(见 Refresh 里的箱子文案)。
            Position(_status.rectTransform, 14f, 4f, width - 28f,
                compactHeader ? 20f : 24f);
            float bodyTop = compactHeader ? -17f : -26f;
            float bodyHeight = collectionMode ? 16f :
                (trackMode ? 40f : Mathf.Max(40f, height - 70f));
            Position(_body.rectTransform, 14f, bodyTop, width - 28f, bodyHeight);
            float trackCardWidth = HistoricalFigureCardListItem.WidthForViewport(
                width - 28f);
            float trackCardHeight = HistoricalFigureCardListItem.HeightForViewport(
                width - 28f);
            float trackTop = Mathf.Max(0f, (height - trackCardHeight) * .5f);
            Position(_trackViewport, 14f, -trackTop, width - 28f,
                trackCardHeight);
            Position(_trackLeftFade.rectTransform, 14f, -trackTop, 64f,
                trackCardHeight);
            Position(_trackRightFade.rectTransform, width - 78f, -trackTop,
                64f, trackCardHeight);
            _body.gameObject.SetActive(!revealMode && !trackMode);
            float lensRadius = LensRadiusForViewport(width - 28f);
            float lensDiameter = lensRadius * 2f;
            float lensTop = (height - lensDiameter) * .5f;
            Position(_lensViewport, (width - lensDiameter) * .5f, -lensTop,
                lensDiameter, lensDiameter);
            float markerHeight = trackCardHeight * ReferenceMagnifiedScale;
            float markerTop = (height - markerHeight) * .5f;
            Position(_centerLine.rectTransform, width * .5f - 1f, -markerTop,
                2f, markerHeight);
            float revealWidth = Mathf.Min(width - 56f, 430f);
            PositionCentered(_revealPanel.rectTransform,
                (width - revealWidth) * .5f,
                -38f, revealWidth, 230f);
            Position(_revealPortrait.rectTransform, 18f, -20f, 112f, 112f);
            Position(_revealName.rectTransform, 146f, -20f,
                revealWidth - 164f, 30f);
            Position(_revealRarity.rectTransform, 146f, -52f,
                revealWidth - 164f, 22f);
            Position(_revealMeta.rectTransform, 146f, -80f,
                revealWidth - 164f, 40f);
            Position(_revealBiographyViewport, 146f, -122f,
                revealWidth - 166f, 88f);
            Position(_revealBiographyScrollbar?.GetComponent<RectTransform>(),
                revealWidth - 15f, -122f, 8f, 88f);
            LayoutRevealBiography(false);
            Position(_revealBar.rectTransform, 0f, -215f, revealWidth, 7f);
            // Content area starts right under the (compressed) top chrome so
            // the grid owns the bulk of the window. With compactHeader the
            // chrome ends at -81 (sort row) for the inventory and at -43
            // (hint line) for the crate list.
            float collectionTop = trackMode ? -178f
                : compactHeader
                    ? (inventoryControls ? -84f : -48f)
                    : (inventoryControls ? -118f : -100f);
            // Grow the scroll viewport down to just above the bottom button
            // row instead of stopping at a flat inset. The buttons live at
            // -height + 28 with a 28px box, so everything above -height + 60
            // is free space that used to sit empty under the grid.
            float collectionBottom = height - 60f;
            float collectionHeight = Mathf.Max(54f,
                collectionBottom - Mathf.Abs(collectionTop));
            bool showCollectionScrollbar = _collectionScrollbar != null &&
                _collectionScrollbar.gameObject.activeSelf;
            float collectionWidth = width - 42f;
            Position(_collectionViewport, 14f, collectionTop,
                showCollectionScrollbar ? collectionWidth : width - 28f,
                collectionHeight);
            Position(_collectionScrollbar?.GetComponent<RectTransform>(),
                width - 24f, collectionTop, 8f, collectionHeight);
            float statWidth = Mathf.Max(48f, (width - 44f) / 5f);
            float statTop = compactHeader ? -45f : -77f;
            float sortTop = compactHeader ? -61f : -95f;
            for (int i = 0; i < _rarityStats.Count; i++)
                Position(_rarityStats[i].rectTransform,
                    14f + i * (statWidth + 4f), statTop, statWidth, 15f);
            for (int i = 0; i < _sortButtons.Count; i++)
                Position(_sortButtons[i].GetComponent<RectTransform>(),
                    14f + i * 59f, sortTop, 55f, 20f);
            for (int i = 0; i < _roleButtons.Count; i++)
                Position(_roleButtons[i].GetComponent<RectTransform>(),
                    width * .56f + i * 92f, 4f, 86f, 24f);
            Position(_recycleModeButton?.GetComponent<RectTransform>(),
                250f, sortTop, 60f, 20f);
            float inventoryPageX = Mathf.Max(316f, width - 122f);
            Position(_inventoryPreviousPage?.GetComponent<RectTransform>(),
                inventoryPageX, sortTop, 38f, 20f);
            Position(_inventoryPageLabel?.rectTransform, inventoryPageX + 40f,
                sortTop, 34f, 20f);
            Position(_inventoryNextPage?.GetComponent<RectTransform>(),
                inventoryPageX + 76f, sortTop, 38f, 20f);
            float buttonTop = -height + 28f;
            Position(_draw.GetComponent<RectTransform>(), 14f, buttonTop,
                100f, 28f);
            Position(_skip.GetComponent<RectTransform>(), 14f, buttonTop,
                82f, 28f);
            Position(_deploy.GetComponent<RectTransform>(), 122f, buttonTop,
                110f, 28f);
            Position(_confirm.GetComponent<RectTransform>(), 234f, buttonTop,
                110f, 28f);
            Position(_cancel.GetComponent<RectTransform>(), 350f, buttonTop,
                70f, 28f);
            Position(_inventory.GetComponent<RectTransform>(), 122f, buttonTop,
                96f, 28f);
            Position(_back.GetComponent<RectTransform>(), BackButtonX,
                buttonTop, BackButtonWidth, 28f);
            float resultButtonTop = -height + 28f;
            float resultX = Mathf.Max(8f, (width - 426f) * .5f);
            Position(_closeReveal.GetComponent<RectTransform>(), resultX,
                resultButtonTop, 48f, 26f);
            Position(_sound.GetComponent<RectTransform>(), resultX + 52f,
                resultButtonTop, 62f, 26f);
            Position(_quickOpen.GetComponent<RectTransform>(), resultX + 118f,
                resultButtonTop, 72f, 26f);
            Position(_autoOpen.GetComponent<RectTransform>(), resultX + 194f,
                resultButtonTop, 72f, 26f);
            Position(_deploy.GetComponent<RectTransform>(), resultX + 270f,
                resultButtonTop, 72f, 26f);
            Position(_openAgain.GetComponent<RectTransform>(), resultX + 346f,
                resultButtonTop, 80f, 26f);
            if (!revealMode)
                // 「部署到城市」跟在「返回箱子」右侧 30px,别再各写各的绝对
                // 坐标 —— 返回箱子一动这个就叠上去了。
                Position(_deploy.GetComponent<RectTransform>(),
                    BackButtonX + BackButtonWidth + 30f, buttonTop,
                    110f, 28f);
            _draw.gameObject.SetActive(_state == DrawState.Idle &&
                _selectedCrateId.Length > 0);
            _skip.gameObject.SetActive(_state == DrawState.Rolling);
            _deploy.gameObject.SetActive(_state == DrawState.Details ||
                _state == DrawState.Reveal);
            _confirm.gameObject.SetActive(_state == DrawState.PlacementConfirm);
            _cancel.gameObject.SetActive(IsPlacementActive);
            _inventory.gameObject.SetActive(_state != DrawState.Rolling &&
                !IsPlacementActive && _selectedCard == null);
            _back.gameObject.SetActive(_state != DrawState.Rolling &&
                _state != DrawState.Reveal && !IsPlacementActive && (_inventoryMode ||
                    _selectedCrateId.Length > 0 || _selectedCard != null));
            _closeReveal.gameObject.SetActive(_state == DrawState.Reveal);
            _openAgain.gameObject.SetActive(_state == DrawState.Reveal);
            _quickOpen.gameObject.SetActive(_state == DrawState.Reveal);
            _autoOpen.gameObject.SetActive(_state == DrawState.Reveal &&
                _quickOpening);
            _sound.gameObject.SetActive(_state == DrawState.Reveal);
            _chrome?.RepositionResizeHandle();
        }
    }
}
