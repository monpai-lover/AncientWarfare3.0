using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.uiquery;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     家族树(小树)/ 氏族大树(大树)。纵向居中多叉树。
    ///     - 大树:以氏支始祖为根整株,懒加载折叠(+/−);点节点 → 打开该人的**小家庭树**。
    ///     - 小树:以某人为中心(父母-本人-子女);点根节点 → 打开本人 inspect,点非根节点 → 以该人为新根重开小树;
    ///       节点带上/下溯箭头(有父显▲跳到父、有子显▼跳到长子,重开以其为中心的小树)。
    ///     - 标题按模式区分:大树="氏族大树",小树="家族树"。
    /// </summary>
    internal class FamilyTreeWindow : AbstractWindow<FamilyTreeWindow>
    {
        private const int NODE_W = 70;
        private const int NODE_H = 78; // 与 FamilyTreeNodeView.NODE_H 一致(名字+社会地位+徽标)
        private const int H_GAP = 6;
        private const int V_GAP = 34;
        private const int PAD = 12;
        private const float WINDOW_W = 480f;
        private const float WINDOW_H = 310f;
        private const float VIEWPORT_W = 430f; // 树画布保持完整宽度,工具入口在窗口右侧内缩
        private const float VIEWPORT_H = 230f;
        private const float VIEWPORT_X = 0f;
        private const float SIDE_RIGHT_INSET = 80f;
        private static readonly float SIDE_RIGHT = FamilyTreeToolbarLayoutRules.RightAlignedX(SIDE_RIGHT_INSET);
        private const float RENAME_TOP = -164f;
        private static readonly Vector2 SIDE_BUTTON_SIZE = new Vector2(78, 20);

        private enum Mode { Family, BigTree }
        private static Mode _mode;
        private static long _centerActorId = -1;
        private static long _rootActorId = -1;
        private static long _backShiId = -1;
        private static long _locateActorId = -1;
        private static LineageTreeReadSpec _readSpec;
        private static bool _showHalfSiblingRelations = false;

        private HashSet<long> _expanded = new HashSet<long>();
        private HashSet<long> _foldDecided = new HashSet<long>(); // 已定过默认折叠状态的节点(防手动 toggle 后被自动规则覆盖)
        private List<FamilyTreeNodeView> _spawned = new List<FamilyTreeNodeView>();
        private List<GameObject> _lines = new List<GameObject>();
        private List<FamilyTreeNodeView> _nodePool = new List<FamilyTreeNodeView>();
        private List<GameObject> _linePool = new List<GameObject>();
        private readonly Dictionary<long, FamilyTreeNodeView>
            _bigTreeNodeViews =
                new Dictionary<long, FamilyTreeNodeView>();
        private Transform _canvas;
        private RectTransform _canvasRect;
        private RectTransform _viewportRect;
        private Button _entryBackButton;
        private Button _backButton;
        private Text _backText;
        private Button _expandButton;
        private Button _collapseButton;
        private Button _resetViewButton;
        private Button _halfSiblingButton;
        private Text _halfSiblingText;
        private Button _renameClanButton;
        private Button _renameSurnameButton;
        private GameObject _renameClanPanel;
        private InputField _renameClanInput;
        private Text _renameClanHintText;
        private Text _titleText;
        private float _maxDepthY;
        private long _lastTreeRootId = -1;
        private const int MAX_AUTO_EXPAND_VISITS = 160;
        private const int MaterializationStepsPerFrame = 64;
        private bool _locateFound;
        private Vector2 _locateTarget;
        private bool _resetAnchorReady;
        private Vector2 _resetRootAnchor;
        private bool _commandPending;
        private bool _commandRefreshRequested;
        private bool _renameSurnameMode;
        private readonly AWUiBoundedRetryState _bulkReadRetry =
            new AWUiBoundedRetryState(3, 2, 8);
        private AWUiRetryTicket _bulkReadTicket;
        private bool _bulkReadTicketActive;
        private long _bulkReadRequestId = -1L;
        private string _bulkReadRequestKey = string.Empty;
        private LineageBulkSnapshot _bulkSnapshot;
        private long _bulkSnapshotRootId = -1L;
        private long _bulkSnapshotProjectionRevision = -1L;
        private string _bulkSnapshotSpecKey = string.Empty;
        private long _bulkRequestWorldGeneration = -1L;
        private long _bulkRequestProjectionRevision = -1L;
        private int _bulkReadWaitStartedFrame;
        private int _bulkReadRequestStartedFrame;
        private bool _forceSynchronousFallback;
        private Dictionary<long, IReadOnlyList<long>> _childIdsCache =
            new Dictionary<long, IReadOnlyList<long>>();
        private readonly AWUiIncrementalWorkState _materializationState =
            new AWUiIncrementalWorkState(MaterializationStepsPerFrame);
        private readonly AWUiMaterializationIntentState _intentState =
            new AWUiMaterializationIntentState();
        private FamilyTreeMaterializationRequest _pendingMaterialization;
        private FamilyTreeMaterializationRequest _activeMaterialization;
        private IEnumerator _materializationSteps;
        private bool _cleanupPending;
        private bool _cleanupLinesOnly;
        private bool _initialCenterRequested;
        private bool _snapshotReadyForMaterialization;

        public static void OpenBigTree(long pShiId)
        {
            OpenDetachedSpec(LineageTreeReadSpec.ForBigTree(pShiId),
                Mode.BigTree, -1L, pShiId, -1L);
        }

        public static void OpenBigTreeLocate(long pActorId, long pShiId)
        {
            if (pShiId < 0L) return;
            OpenDetachedSpec(LineageTreeReadSpec.ForLocate(pActorId, pShiId),
                Mode.BigTree, -1L, pShiId, pActorId);
        }

        public static void OpenFamilyTree(long pCenterActorId, long pShiIdForBackButton)
        {
            OpenDetachedSpec(LineageTreeReadSpec.ForFamily(pCenterActorId),
                Mode.Family, pCenterActorId, pShiIdForBackButton, -1L);
        }

        public static void ResetWorldState()
        {
            _readSpec = null;
            _mode = Mode.Family;
            _centerActorId = -1L;
            _rootActorId = -1L;
            _backShiId = -1L;
            _locateActorId = -1L;
            Instance?.ResetWorldInstanceState();
        }

        private static void OpenDetachedSpec(LineageTreeReadSpec pSpec,
            Mode pMode, long pCenterActorId, long pBackShiId,
            long pLocateActorId)
        {
            if (pSpec == null) return;
            EnsureCreated();
            Instance?.CancelDetachedRead();
            _readSpec = pSpec;
            _mode = pMode;
            _centerActorId = pCenterActorId;
            _rootActorId = -1L;
            _backShiId = pBackShiId;
            _locateActorId = pLocateActorId;
            Instance?.ResetDetachedTreeState();
            Instance?.RequestInitialCenter();
            Instance?.BeginDetachedRead();
            ShowOrRefresh(false);
        }

        private void ResetDetachedTreeState()
        {
            _bulkSnapshot = null;
            _bulkSnapshotRootId = -1L;
            _bulkSnapshotProjectionRevision = -1L;
            _bulkSnapshotSpecKey = string.Empty;
            _bulkRequestWorldGeneration = -1L;
            _bulkRequestProjectionRevision = -1L;
            _childIdsCache = new Dictionary<long, IReadOnlyList<long>>();
            ResetFoldState();
            _locateFound = false;
            _locateTarget = Vector2.zero;
            _resetAnchorReady = false;
            _initialCenterRequested = false;
            _snapshotReadyForMaterialization = false;
            _intentState.CancelAll();
            CancelMaterialization(clearPendingRequest: true);
            BeginBoundedCleanup();
        }

        private void ResetWorldInstanceState()
        {
            CancelDetachedRead();
            ResetDetachedTreeState();
            _commandPending = false;
            _commandRefreshRequested = false;
            if (_renameClanInput != null)
                _renameClanInput.interactable = true;
        }

        private void RequestInitialCenter()
        {
            _initialCenterRequested = true;
        }

        private void BeginDetachedRead()
        {
            if (_readSpec == null) return;
            _bulkReadTicket = _bulkReadRetry.Begin(
                AWAsyncRuntime.WorldGeneration,
                FamilyTreeProjectionRevision.Current, _readSpec.Key,
                Time.frameCount);
            _bulkReadTicketActive = true;
            _bulkReadWaitStartedFrame = Time.frameCount;
            _bulkReadRequestStartedFrame = 0;
            _forceSynchronousFallback = false;
        }

        private void CancelDetachedRead()
        {
            if (_bulkReadRequestId >= 0L)
                AWHistoricalReadService.ReleaseRequest(_bulkReadRequestId,
                    _bulkReadRequestKey);
            ClearBulkReadRequestIdentity();
            _bulkReadRetry.Cancel();
            _bulkReadTicketActive = false;
            _bulkRequestWorldGeneration = -1L;
            _bulkRequestProjectionRevision = -1L;
            _bulkReadWaitStartedFrame = 0;
            _bulkReadRequestStartedFrame = 0;
            _forceSynchronousFallback = false;
        }

        private void ClearBulkReadRequestIdentity(long pRequestId = -1L)
        {
            if (pRequestId >= 0L && pRequestId != _bulkReadRequestId)
                return;
            _bulkReadRequestId = -1L;
            _bulkReadRequestKey = string.Empty;
        }

        /// <summary>
        ///     统一打开/刷新:已是当前窗 → 直接 Rebuild;否则 SafeShow 激活窗口
        ///     (SafeShow 内 finishAnimations 已根治首次打不开;激活同步触发 OnNormalEnable→Rebuild)。
        /// </summary>
        private static void ShowOrRefresh(bool pJustCreated)
        {
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.FAMILY_TREE,
                () => { if (Instance != null) Instance.Rebuild(); });
        }

        private static void EnsureCreated()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.FAMILY_TREE);
        }

        protected override void Init()
        {
            ConfigureWideTreeWindow();
            EnsureEntryBackButton();

            var canvasObj = new GameObject("TreeCanvas", typeof(RectTransform));
            canvasObj.transform.SetParent(ContentTransform, false);
            _canvasRect = canvasObj.GetComponent<RectTransform>();
            _canvasRect.anchorMin = new Vector2(0, 1);
            _canvasRect.anchorMax = new Vector2(0, 1);
            _canvasRect.pivot = new Vector2(0, 1);
            _canvasRect.anchoredPosition = Vector2.zero;
            _canvas = canvasObj.transform;

            // 拖动接收面:**满铺整个视口(Viewport=ContentTransform.parent)的常驻透明 Image**,
            // 挂在最底层(SetAsFirstSibling),这样视口内**任意位置**(含空白、顶部)点下都能拖,
            // 节点 Button 在其之上仍可点击(Button 不实现 IDragHandler,拖动事件冒泡到本面 → 平移树画布)。
            Transform viewport = ContentTransform != null ? ContentTransform.parent : null;
            _viewportRect = viewport as RectTransform;
            if (FamilyTreeInteractionRules.ShouldAttachToViewport(
                    viewport != null))
            {
                var viewportPan =
                    viewport.GetComponent<TreeDragPanHandler>() ??
                    viewport.gameObject.AddComponent<TreeDragPanHandler>();
                viewportPan.Setup(_canvasRect, _viewportRect);
            }
            Transform dragParent = viewport != null ? viewport : ContentTransform;
            var dragObj = new GameObject("TreeDragSurface", typeof(RectTransform), typeof(Image),
                typeof(AncientWarfare3.ui.items.TreeDragPanHandler));
            dragObj.transform.SetParent(dragParent, false);
            dragObj.transform.SetAsFirstSibling(); // 置底,不挡节点点击
            var drect = dragObj.GetComponent<RectTransform>();
            drect.anchorMin = Vector2.zero; drect.anchorMax = Vector2.one; // 满铺父(视口)
            drect.offsetMin = Vector2.zero; drect.offsetMax = Vector2.zero;
            var dragBg = dragObj.GetComponent<Image>();
            dragBg.color = new Color(0, 0, 0, 0);
            dragBg.raycastTarget = true;
            var pan = dragObj.GetComponent<AncientWarfare3.ui.items.TreeDragPanHandler>();
            pan.Setup(_canvasRect, null); // viewport=null → 无边界自由平移

            // 复用窗口自带标题控件(ScrollWindow.titleText),按模式改文字,避免再加 Text 与原版标题重叠。
            var sw = GetComponent<ScrollWindow>();
            if (sw != null) _titleText = sw.titleText;

            _expandButton = MakeToolbarButton("ExpandLiveBranches", AW_L10n.Text("aw_tree_expand", "展开"), new Vector2(SIDE_RIGHT, -104), ExpandAllLiveBranches, SIDE_BUTTON_SIZE);
            _collapseButton = MakeToolbarButton("CollapseBranches", AW_L10n.Text("aw_tree_collapse", "收缩"), new Vector2(SIDE_RIGHT, -128), CollapseAllBranches, SIDE_BUTTON_SIZE);
            _resetViewButton = MakeToolbarButton("ResetTreeView", AW_L10n.Text("aw_tree_reset_view", "复位"), new Vector2(SIDE_RIGHT, -152), ResetViewToAnchor, SIDE_BUTTON_SIZE);
            _halfSiblingButton = MakeToolbarButton("HalfSiblingRelations", "", new Vector2(SIDE_RIGHT, -54), ToggleHalfSiblingRelations, SIDE_BUTTON_SIZE);
            _renameClanButton = MakeToolbarButton("RenameVisibleClan", AW_L10n.Text("aw_rename_visible_clan", "\u6539\u6C0F"), new Vector2(SIDE_RIGHT, RENAME_TOP), ToggleRenameClanPanel, SIDE_BUTTON_SIZE);
            _renameSurnameButton = MakeToolbarButton("RenamePatrilinealSurname",
                AW_L10n.Text("aw_rename_visible_surname", "\u6539\u59D3"),
                new Vector2(SIDE_RIGHT, RENAME_TOP), ToggleRenameSurnamePanel,
                SIDE_BUTTON_SIZE);
            _halfSiblingText = _halfSiblingButton != null ? _halfSiblingButton.GetComponentInChildren<Text>() : null;
            UpdateHalfSiblingButtonText();
            BuildRenameClanPanel();

            // "回氏族大树"按钮(窗口底部居中,小树模式可见)
            var btnObj = new GameObject("BackToBigTree", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(BackgroundTransform, false);
            var brect = btnObj.GetComponent<RectTransform>();
            brect.anchorMin = new Vector2(1f, 1f);
            brect.anchorMax = new Vector2(1f, 1f);
            brect.pivot = new Vector2(1f, 1f);
            brect.sizeDelta = SIDE_BUTTON_SIZE;
            brect.anchoredPosition = new Vector2(SIDE_RIGHT, -80);
            var bg = btnObj.GetComponent<Image>();
            AW_UIStyle.ApplyButton(bg, 0.95f);
            _backButton = btnObj.GetComponent<Button>();
            _backButton.onClick.AddListener(OnBack);
            var txtObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtObj.transform.SetParent(btnObj.transform, false);
            var trect = txtObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero; trect.anchorMax = Vector2.one; trect.sizeDelta = Vector2.zero;
            _backText = txtObj.GetComponent<Text>();
            _backText.font = LocalizedTextManager.current_font;
            _backText.fontSize = 9;
            _backText.alignment = TextAnchor.MiddleCenter;
            _backText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _backText.color = Color.white;
            _backText.text = AW_L10n.Text("aw_back_big_tree", "← 回氏族大树");
        }

        private void ConfigureWideTreeWindow()
        {
            var bgRect = BackgroundTransform.GetComponent<RectTransform>();
            if (bgRect != null) bgRect.sizeDelta = new Vector2(WINDOW_W, WINDOW_H);

            Transform close = BackgroundTransform.parent != null ? BackgroundTransform.parent.Find("CloseBackground") : null;
            if (close != null) close.localPosition = new Vector3(WINDOW_W / 2f - 20f, WINDOW_H / 2f - 12f);

            Transform titleBg = BackgroundTransform.Find("TitleBackground");
            var titleRect = titleBg != null ? titleBg.GetComponent<RectTransform>() : null;
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(WINDOW_W * 0.5f, 30f);
                titleBg.localPosition = new Vector3(0, WINDOW_H / 2f - 16f);
            }

            var sw = GetComponent<ScrollWindow>();
            if (sw?.titleText != null)
            {
                sw.titleText.transform.localPosition = new Vector3(0, WINDOW_H / 2f - 16f);
                var tr = sw.titleText.GetComponent<RectTransform>();
                if (tr != null) tr.sizeDelta = new Vector2(WINDOW_W * 0.46f, 28f);
            }

            Transform scroll = BackgroundTransform.Find("Scroll View");
            var scrollRect = scroll != null ? scroll.GetComponent<RectTransform>() : null;
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(VIEWPORT_W, VIEWPORT_H);
                scroll.localPosition = new Vector3(VIEWPORT_X, -18f, 0);
            }

            Transform viewport = BackgroundTransform.Find("Scroll View/Viewport");
            var viewRect = viewport != null ? viewport.GetComponent<RectTransform>() : null;
            if (viewRect != null)
            {
                float horizontalSpan = viewRect.anchorMax.x -
                                       viewRect.anchorMin.x;
                float verticalSpan = viewRect.anchorMax.y -
                                     viewRect.anchorMin.y;
                viewRect.sizeDelta = new Vector2(
                    FamilyTreeViewportLayoutRules.SizeDeltaForDesiredExtent(
                        VIEWPORT_W, VIEWPORT_W, horizontalSpan),
                    FamilyTreeViewportLayoutRules.SizeDeltaForDesiredExtent(
                        VIEWPORT_H, VIEWPORT_H, verticalSpan));
            }
        }

        private void EnsureEntryBackButton()
        {
            if (_entryBackButton != null || BackgroundTransform?.parent == null)
                return;

            Transform parent = BackgroundTransform.parent;
            var buttonObject = new GameObject("FamilyTreeBackToEntry",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            buttonObject.transform.SetParent(parent, false);

            RectTransform buttonRect =
                buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(24f, 24f);

            Image background = buttonObject.GetComponent<Image>();
            AW_UIStyle.ApplyButton(background, 0.96f);
            background.raycastTarget = true;

            var iconObject = new GameObject("Icon", typeof(RectTransform),
                typeof(Image));
            iconObject.transform.SetParent(buttonObject.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(14f, 14f);
            iconRect.localScale = new Vector3(-1f, 1f, 1f);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(
                "ui/icons/iconArrowMetaRight");
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            _entryBackButton = buttonObject.GetComponent<Button>();
            _entryBackButton.onClick.AddListener(WindowHistory.clickBack);

            TipButton tip = buttonObject.GetComponent<TipButton>();
            tip.enabled = true;
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(buttonObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = AW_L10n.Text(
                        "aw_family_tree_back_to_entry", "Back to Entry"),
                    tip_description = ""
                });

            Transform close = parent.Find("CloseBackground");
            Vector3 closePosition = close != null
                ? close.localPosition
                : new Vector3(WINDOW_W / 2f - 20f,
                    WINDOW_H / 2f - 12f, 0f);
            _entryBackButton.transform.localPosition =
                closePosition + new Vector3(-30f, 0f, 0f);
            _entryBackButton.transform.SetAsLastSibling();
            if (close != null) close.SetAsLastSibling();
        }

        private Button MakeToolbarButton(string pName, string pText, Vector2 pTopRightOffset,
            System.Action pAction, Vector2? pSize = null)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(BackgroundTransform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = pSize ?? new Vector2(46, 18);
            rect.anchoredPosition = pTopRightOffset;
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.95f);

            var btn = obj.GetComponent<Button>();
            btn.onClick.AddListener(() => pAction?.Invoke());

            var txtObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtObj.transform.SetParent(obj.transform, false);
            var trect = txtObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.offsetMin = Vector2.zero;
            trect.offsetMax = Vector2.zero;
            var txt = txtObj.GetComponent<Text>();
            txt.font = LocalizedTextManager.current_font;
            txt.fontSize = 10;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = pText;
            return btn;
        }

        private void BuildRenameClanPanel()
        {
            var obj = new GameObject("RenameVisibleClanPanel", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(BackgroundTransform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(86, 76);
            rect.anchoredPosition = new Vector2(SIDE_RIGHT, RENAME_TOP - 24f);
            AW_UIStyle.ApplyPanel(obj.GetComponent<Image>(), 0.96f);
            _renameClanPanel = obj;

            var inputObj = new GameObject("ClanInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObj.transform.SetParent(obj.transform, false);
            var inputRect = inputObj.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(0f, 1f);
            inputRect.pivot = new Vector2(0f, 1f);
            inputRect.sizeDelta = new Vector2(70, 18);
            inputRect.anchoredPosition = new Vector2(8, -8);
            AW_UIStyle.ApplyButton(inputObj.GetComponent<Image>(), 0.82f);

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(inputObj.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4, 1);
            textRect.offsetMax = new Vector2(-4, -1);
            var text = textObj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = 10;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var placeholderObj = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            placeholderObj.transform.SetParent(inputObj.transform, false);
            var placeholderRect = placeholderObj.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(4, 1);
            placeholderRect.offsetMax = new Vector2(-4, -1);
            var placeholder = placeholderObj.GetComponent<Text>();
            placeholder.font = LocalizedTextManager.current_font;
            placeholder.fontSize = 10;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.text = AW_L10n.Text("aw_rename_visible_clan_placeholder", "\u65B0\u6C0F");

            _renameClanInput = inputObj.GetComponent<InputField>();
            _renameClanInput.textComponent = text;
            _renameClanInput.placeholder = placeholder;

            MakeRenamePanelButton(obj.transform, "Ok", AW_L10n.Text("aw_confirm", "\u786E\u5B9A"),
                new Vector2(8, -31), new Vector2(34, 17), ConfirmRenameVisibleClan);
            MakeRenamePanelButton(obj.transform, "Cancel", "X",
                new Vector2(48, -31), new Vector2(30, 17), () => _renameClanPanel.SetActive(false));

            var hintObj = new GameObject("Hint", typeof(RectTransform), typeof(Text));
            hintObj.transform.SetParent(obj.transform, false);
            var hintRect = hintObj.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(0f, 1f);
            hintRect.pivot = new Vector2(0f, 1f);
            hintRect.sizeDelta = new Vector2(70, 28);
            hintRect.anchoredPosition = new Vector2(8, -52);
            _renameClanHintText = hintObj.GetComponent<Text>();
            _renameClanHintText.font = LocalizedTextManager.current_font;
            _renameClanHintText.fontSize = 9;
            _renameClanHintText.alignment = TextAnchor.MiddleLeft;
            _renameClanHintText.color = new Color(0.95f, 0.86f, 0.55f, 1f);
            _renameClanHintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _renameClanHintText.text = AW_L10n.Text("aw_rename_visible_clan_hint", "\u6539\u5F53\u524D\u6C0F\u652F\u6811\u5168\u90E8\u6210\u5458");
            _renameClanPanel.SetActive(false);
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private Button MakeRenamePanelButton(Transform pParent, string pName, string pText,
            Vector2 pPosition, Vector2 pSize, System.Action pAction)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPosition;
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.95f);

            var btn = obj.GetComponent<Button>();
            btn.onClick.AddListener(() => pAction?.Invoke());

            var txtObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtObj.transform.SetParent(obj.transform, false);
            var trect = txtObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.offsetMin = Vector2.zero;
            trect.offsetMax = Vector2.zero;
            var txt = txtObj.GetComponent<Text>();
            txt.font = LocalizedTextManager.current_font;
            txt.fontSize = 10;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = pText;
            return btn;
        }

        public override void OnNormalEnable()
        {
            InvalidateBulkSnapshot();
            Rebuild();
        }

        public override void OnNormalDisable()
        {
            CancelDetachedRead();
            _bulkSnapshot = null;
            _intentState.CancelAll();
            CancelMaterialization(clearPendingRequest: true);
            TransferOwnedCleanup();
        }

        private void OnDestroy()
        {
            CancelDetachedRead();
            _intentState.CancelAll();
            CancelMaterialization(clearPendingRequest: true);
            TransferOwnedCleanup();
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
        }

        private void Update()
        {
            if (_commandRefreshRequested)
            {
                _commandRefreshRequested = false;
                _commandPending = false;
                if (_renameClanInput != null)
                    _renameClanInput.interactable = true;
                ResetQueryCache();
                if (isActiveAndEnabled) Rebuild();
            }
            if (_bulkSnapshot != null && !_bulkReadTicketActive &&
                _bulkSnapshotProjectionRevision !=
                    FamilyTreeProjectionRevision.Current)
            {
                PreservePanForNextRebuild();
                Rebuild();
            }
            if (_snapshotReadyForMaterialization && isActiveAndEnabled &&
                _bulkSnapshot != null && !_bulkReadTicketActive)
            {
                _snapshotReadyForMaterialization = false;
                QueueMaterialization(_bulkSnapshot,
                    _bulkSnapshotProjectionRevision);
            }
            TryScheduleBulkSnapshot();
            AdvanceMaterialization();
        }

        private void OnCommandStateChanged()
        {
            if (_commandPending) _commandRefreshRequested = true;
        }

        private void OnBack()
        {
            if (_mode == Mode.BigTree)
            {
                long parentShi = -1L;
                if (_bulkSnapshot != null &&
                    _bulkSnapshot.TryGetNode(_rootActorId,
                        out LineageTreeNodeSnapshot rootNode))
                    parentShi = rootNode.ParentShiId;
                if (parentShi >= 0) OpenBigTree(parentShi);
                return;
            }

            long currentShi = -1L;
            if (_bulkSnapshot != null &&
                _bulkSnapshot.TryGetNode(_centerActorId,
                    out LineageTreeNodeSnapshot centerNode))
                currentShi = centerNode.ShiId;
            if (currentShi < 0) currentShi = _backShiId;
            if (currentShi >= 0) OpenBigTreeLocate(_centerActorId, currentShi);
        }

        private void ToggleRenameClanPanel()
        {
            if (_renameClanPanel == null || _mode != Mode.BigTree) return;
            _renameSurnameMode = false;
            bool show = !_renameClanPanel.activeSelf;
            _renameClanPanel.SetActive(show);
            if (!show) return;

            if (_renameClanInput != null)
            {
                string currentName = string.Empty;
                if (_bulkSnapshot != null &&
                    _bulkSnapshot.TryGetNode(_rootActorId,
                        out LineageTreeNodeSnapshot rootNode))
                    currentName = rootNode.ClanName;
                _renameClanInput.text = currentName ?? string.Empty;
                if (_renameClanInput.placeholder is Text placeholder)
                    placeholder.text = AW_L10n.Text(
                        "aw_rename_visible_clan_placeholder", "\u65B0\u6C0F");
            }
            if (_renameClanHintText != null)
                _renameClanHintText.text = AW_L10n.Text("aw_rename_visible_clan_hint", "\u6539\u5F53\u524D\u6C0F\u652F\u6811\u5168\u90E8\u6210\u5458");
            try { _renameClanInput?.ActivateInputField(); } catch { }
        }

        private void ToggleRenameSurnamePanel()
        {
            if (_renameClanPanel == null || _mode != Mode.Family) return;
            _renameSurnameMode = true;
            bool show = !_renameClanPanel.activeSelf;
            _renameClanPanel.SetActive(show);
            if (!show) return;

            string currentName = string.Empty;
            if (_bulkSnapshot != null &&
                _bulkSnapshot.TryGetNode(_centerActorId,
                    out LineageTreeNodeSnapshot centerNode))
                currentName = centerNode.FamilyName;
            if (_renameClanInput != null)
            {
                _renameClanInput.text = currentName ?? string.Empty;
                if (_renameClanInput.placeholder is Text placeholder)
                    placeholder.text = AW_L10n.Text(
                        "aw_rename_visible_surname_placeholder", "\u65B0\u59D3");
            }
            if (_renameClanHintText != null)
                _renameClanHintText.text = AW_L10n.Text(
                    "aw_rename_visible_surname_hint",
                    "\u6539\u5F53\u524D\u4EBA\u7269\u53CA\u7236\u7CFB\u540E\u4EE3\u7684\u59D3");
            try { _renameClanInput?.ActivateInputField(); } catch { }
        }

        private void ConfirmRenameVisibleClan()
        {
            if (_commandPending) return;
            if (_renameSurnameMode && _mode != Mode.Family) return;
            if (!_renameSurnameMode && _mode != Mode.BigTree) return;
            string raw = _renameClanInput != null ? _renameClanInput.text : "";
            bool valid = _renameSurnameMode
                ? VisibleSurnameRenameRules.TryNormalizeFamilyName(raw, out _)
                : VisibleClanRenameRules.TryNormalizeClanName(raw, out _);
            if (!valid)
            {
                if (_renameClanHintText != null)
                    _renameClanHintText.text = _renameSurnameMode
                        ? AW_L10n.Text("aw_rename_visible_surname_invalid",
                            "\u8BF7\u8F93\u5165\u6709\u6548\u59D3\u540D")
                        : AW_L10n.Text("aw_rename_visible_clan_invalid",
                            "\u8BF7\u8F93\u5165\u6709\u6548\u6C0F\u540D");
                return;
            }

            long targetShiId = VisibleClanRenameRules.ResolveTargetShiId(
                _readSpec?.ShiId ?? -1L, _backShiId);
            long countryId = _renameSurnameMode
                ? ResolveRenameCountryIdForActor(_centerActorId)
                : ResolveRenameCountryId(targetShiId);
            if (countryId <= 0)
            {
                if (_renameClanHintText != null)
                    _renameClanHintText.text = AW_L10n.Text("aw_rename_visible_clan_none", "\u6CA1\u6709\u53EF\u66F4\u65B0\u8282\u70B9");
                return;
            }

            AW3CommandRequest request = _renameSurnameMode
                ? AW3CommandRequest.RenameSurname(countryId,
                    _centerActorId, raw)
                : AW3CommandRequest.RenameClan(countryId,
                    targetShiId, raw);
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(request);
            if (result.Status == AW3CommandStatus.Pending)
            {
                _commandPending = true;
                if (_renameClanInput != null)
                    _renameClanInput.interactable = false;
                if (_renameClanHintText != null)
                    _renameClanHintText.text = AW_L10n.Text(
                        "aw3_command_pending", "Waiting for host");
                return;
            }
            if (!result.Accepted)
            {
                if (_renameClanHintText != null)
                    _renameClanHintText.text = AW_L10n.Text(
                        "aw_rename_visible_clan_none",
                        "No nodes can be updated");
                return;
            }

            if (_renameClanPanel != null) _renameClanPanel.SetActive(false);
            ResetQueryCache();
            PreservePanForNextRebuild();
            Rebuild();
        }

        private long ResolveRenameCountryId(long pShiId)
        {
            if (pShiId <= 0 || World.world?.kingdoms == null) return -1L;
            if (World.world?.units != null)
                foreach (Actor actor in World.world.units)
                {
                    if (actor?.data == null || actor.isRekt() ||
                        actor.kingdom?.data == null || actor.kingdom.isRekt())
                        continue;
                    actor.data.get(LineageKeys.SHI_ID, out long actorShiId, -1L);
                    if (actorShiId == pShiId) return actor.kingdom.id;
                }
            if (_bulkSnapshot != null &&
                _bulkSnapshot.TryGetNode(_rootActorId,
                    out LineageTreeNodeSnapshot rootNode) &&
                rootNode.KingdomId > 0) return rootNode.KingdomId;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                Actor ruler = kingdom?.king;
                if (ruler?.data == null || ruler.isRekt()) continue;
                ruler.data.get(LineageKeys.SHI_ID, out long rulerShiId,
                    -1L);
                if (rulerShiId == pShiId) return kingdom.id;
            }
            return -1L;
        }

        private long ResolveRenameCountryIdForActor(long pActorId)
        {
            try
            {
                Actor actor = World.world?.units?.get(pActorId);
                if (actor?.kingdom?.data != null && !actor.kingdom.isRekt())
                    return actor.kingdom.id;
            }
            catch { }
            if (_bulkSnapshot != null &&
                _bulkSnapshot.TryGetNode(pActorId,
                    out LineageTreeNodeSnapshot node)) return node.KingdomId;
            return -1L;
        }

        private void ToggleHalfSiblingRelations()
        {
            _showHalfSiblingRelations = !_showHalfSiblingRelations;
            UpdateHalfSiblingButtonText();
            ResetQueryCache();
            Rebuild();
        }

        private void UpdateHalfSiblingButtonText()
        {
            if (_halfSiblingText == null) return;
            _halfSiblingText.text = _showHalfSiblingRelations
                ? AW_L10n.Text("aw_tree_half_sibling_on", "\u534A\u80DE\u5F00")
                : AW_L10n.Text("aw_tree_half_sibling_off", "\u534A\u80DE\u5173");
        }

        private void ResetBigTreeDefault(long pFounderId)
        {
            InvalidateBulkSnapshot();
            ResetFoldState();
            _foldDecided.Add(pFounderId);
            _lastTreeRootId = pFounderId;
            _locateFound = false;
            _locateTarget = Vector2.zero;
            ResetQueryCache();
        }

        private void Rebuild()
        {
            CancelMaterialization(clearPendingRequest: true);
            if (_readSpec == null) return;
            long projectionRevision = FamilyTreeProjectionRevision.Current;
            if (_bulkSnapshot != null &&
                _bulkSnapshotProjectionRevision == projectionRevision &&
                string.Equals(_bulkSnapshotSpecKey, _readSpec.Key,
                    System.StringComparison.Ordinal))
            {
                QueueMaterialization(_bulkSnapshot, projectionRevision);
                return;
            }
            BeginDetachedRead();
            TryScheduleBulkSnapshot();
        }

        private bool TryScheduleBulkSnapshot()
        {
            if (!_bulkReadTicketActive || _readSpec == null) return false;
            long projectionRevision = FamilyTreeProjectionRevision.Current;
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            LineageTreeReadSpec spec = _readSpec;
            AWUiRetryTicket ticket = _bulkReadTicket;
            if (!_bulkReadRetry.AcceptStableWorld(ticket, worldGeneration,
                    spec.Key))
            {
                return false;
            }
            if (_bulkReadRetry.Exhausted)
            {
                ShowDetachedReadUnavailable();
                return false;
            }
            bool asynchronousReadRequired = AWAsyncRuntime.UiEnabled ||
                                            AWAsyncRuntime.ShadowEnabled;
            bool historicalReadReady = AWHistoricalReadService.Ready;
            int waitingFrames = System.Math.Max(0,
                Time.frameCount - _bulkReadWaitStartedFrame);
            int requestElapsedFrames = System.Math.Max(0,
                Time.frameCount - _bulkReadRequestStartedFrame);
            if (FamilyTreeMaterializationRules.ShouldRecoverTimedOutRequest(
                    _bulkReadRetry.InFlight, requestElapsedFrames,
                    FamilyTreeMaterializationRules.RequestTimeoutFrames))
            {
                if (_bulkReadRequestId >= 0L)
                    AWHistoricalReadService.ReleaseRequest(
                        _bulkReadRequestId, _bulkReadRequestKey);
                ClearBulkReadRequestIdentity();
                _bulkReadRetry.RecordFault(ticket, Time.frameCount);
                _forceSynchronousFallback = true;
            }
            bool synchronousFallback = _forceSynchronousFallback ||
                FamilyTreeMaterializationRules
                .ShouldUseSynchronousFallback(asynchronousReadRequired,
                    historicalReadReady, waitingFrames,
                    FamilyTreeMaterializationRules
                        .ReaderStartupFallbackFrames);
            if (!FamilyTreeMaterializationRules
                    .ShouldConsumeDetachedReadAttempt(
                        asynchronousReadRequired,
                        historicalReadReady) && !synchronousFallback)
            {
                ShowDetachedReadLoading();
                return false;
            }
            if (!_bulkReadRetry.TryStart(ticket, Time.frameCount))
                return _bulkReadRetry.InFlight;

            _bulkRequestWorldGeneration = worldGeneration;
            _bulkRequestProjectionRevision = projectionRevision;
            ShowDetachedReadLoading();
            var execution = new LineageTreeReadExecution(spec);
            if (synchronousFallback)
            {
                try
                {
                    object result =
                        AWHistoricalMainThreadReadService.Read(execution,
                            System.Threading.CancellationToken.None);
                    ApplyBulkSnapshot(ticket, spec, -1L, result);
                    return !_bulkReadTicketActive;
                }
                catch (System.Exception error)
                {
                    HandleBulkReadFault(ticket, spec, -1L, error);
                    return false;
                }
            }
            long scheduledRequestId = -1L;
            var request = new AWHistoricalReadRequest(
                "lineage-tree:" + ticket.Generation + ":" + spec.Key,
                new AWAsyncStamp(worldGeneration, 0L,
                    projectionRevision), execution.Execute,
                result => ApplyBulkSnapshot(ticket, spec,
                    scheduledRequestId, result),
                error => HandleBulkReadFault(ticket, spec,
                    scheduledRequestId, error),
                pDatabaseEpoch: LineageArchiveManager.RuntimeDatabaseEpoch);
            if (AWHistoricalReadService.TrySchedule(request,
                    out scheduledRequestId))
            {
                _bulkReadRequestId = scheduledRequestId;
                _bulkReadRequestKey = request.Key;
                _bulkReadRequestStartedFrame = Time.frameCount;
                return true;
            }
            CancelBulkQuery();
            return false;
        }

        private void CancelBulkQuery()
        {
            if (!_bulkReadTicketActive) return;
            _bulkReadRetry.RecordFault(_bulkReadTicket, Time.frameCount);
            if (_bulkReadRetry.Exhausted) ShowDetachedReadUnavailable();
        }

        private void ApplyBulkSnapshot(AWUiRetryTicket pTicket,
            LineageTreeReadSpec pSpec, long pRequestId, object pResult)
        {
            long projectionRevision = FamilyTreeProjectionRevision.Current;
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            long requestProjectionRevision =
                _bulkRequestProjectionRevision;
            if (_readSpec == null || !_bulkReadTicketActive ||
                pTicket.Generation != _bulkReadTicket.Generation ||
                !string.Equals(_readSpec.Key, pSpec.Key,
                    System.StringComparison.Ordinal) ||
                worldGeneration != _bulkRequestWorldGeneration) return;
            if (!FamilyTreeMaterializationRules
                    .AcceptCompletedSnapshot(
                        sameGeneration: pTicket.Generation ==
                            _bulkReadTicket.Generation,
                        sameWorldGeneration: worldGeneration ==
                            _bulkRequestWorldGeneration,
                        sameSpec: string.Equals(_readSpec.Key, pSpec.Key,
                            System.StringComparison.Ordinal),
                        sameProjectionRevision: projectionRevision ==
                            requestProjectionRevision))
            {
                RestartDetachedReadAfterStaleCompletion(pTicket, pSpec,
                    pRequestId, worldGeneration);
                return;
            }

            LineageBulkSnapshot snapshot = pResult as LineageBulkSnapshot;
            if (snapshot == null || snapshot.RootActorId < 0L)
            {
                HandleBulkReadFault(pTicket, pSpec,
                    pRequestId,
                    new System.InvalidOperationException(
                        "Detached lineage query returned no root actor."));
                return;
            }

            _bulkReadRetry.RecordSuccess(pTicket);
            _forceSynchronousFallback = false;
            ClearBulkReadRequestIdentity(pRequestId);
            _bulkReadTicketActive = false;
            _bulkSnapshot = snapshot;
            _bulkSnapshotRootId = snapshot.RootActorId;
            _bulkSnapshotProjectionRevision = requestProjectionRevision;
            _bulkSnapshotSpecKey = pSpec.Key;
            _rootActorId = snapshot.RootActorId;
            if (pSpec.ShiId >= 0L) _backShiId = pSpec.ShiId;
            _locateActorId = pSpec.Mode == LineageTreeReadMode.Locate
                ? snapshot.LocateActorId
                : -1L;
            ApplyLocateExpansion(snapshot, pSpec.Mode);
            var pKey = new AWUiQueryKey("family_tree", snapshot.RootActorId,
                pSpec.Key, requestProjectionRevision, pTicket.Generation);
            if (isActiveAndEnabled)
            {
                _snapshotReadyForMaterialization = false;
                QueueMaterialization(pKey, snapshot);
            }
            else if (FamilyTreeMaterializationRules.ShouldQueueAfterInactiveCompletion(
                         snapshotAccepted: true, windowActive: false))
            {
                _snapshotReadyForMaterialization = true;
            }
        }

        private void RestartDetachedReadAfterStaleCompletion(
            AWUiRetryTicket pTicket, LineageTreeReadSpec pSpec,
            long pRequestId, long pWorldGeneration)
        {
            if (_readSpec == null || !_bulkReadTicketActive ||
                pTicket.Generation != _bulkReadTicket.Generation ||
                pWorldGeneration != AWAsyncRuntime.WorldGeneration ||
                !string.Equals(_readSpec.Key, pSpec.Key,
                    System.StringComparison.Ordinal)) return;

            _bulkReadRetry.RecordFault(pTicket, Time.frameCount);
            ClearBulkReadRequestIdentity(pRequestId);
            _bulkReadTicketActive = false;
            _bulkRequestWorldGeneration = -1L;
            _bulkRequestProjectionRevision = -1L;
            BeginDetachedRead();
        }

        private void HandleBulkReadFault(AWUiRetryTicket pTicket,
            LineageTreeReadSpec pSpec, long pRequestId,
            System.Exception pError)
        {
            long projectionRevision = FamilyTreeProjectionRevision.Current;
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            if (_readSpec == null || !_bulkReadTicketActive ||
                pTicket.Generation != _bulkReadTicket.Generation ||
                !string.Equals(_readSpec.Key, pSpec.Key,
                    System.StringComparison.Ordinal) ||
                worldGeneration != _bulkRequestWorldGeneration) return;
            if (!_bulkReadRetry.AcceptStableWorld(pTicket, worldGeneration,
                    _readSpec.Key))
            {
                RestartDetachedReadAfterStaleCompletion(pTicket, pSpec,
                    pRequestId, worldGeneration);
                return;
            }

            ClearBulkReadRequestIdentity(pRequestId);
            _bulkReadRetry.RecordFault(pTicket, Time.frameCount);
            ModClass.LogWarning("Family tree bulk read failed: " +
                                (pError?.Message ?? "unknown error"));
            if (_bulkReadRetry.Exhausted) ShowDetachedReadUnavailable();
        }

        private void InvalidateBulkSnapshot()
        {
            CancelDetachedRead();
            _bulkSnapshot = null;
            _bulkSnapshotRootId = -1L;
            _bulkSnapshotProjectionRevision = -1L;
            _bulkSnapshotSpecKey = string.Empty;
            _bulkRequestWorldGeneration = -1L;
            _bulkRequestProjectionRevision = -1L;
            _childIdsCache = new Dictionary<long, IReadOnlyList<long>>();
            _intentState.CancelAll();
            CancelMaterialization(clearPendingRequest: true);
            BeginBoundedCleanup();
        }

        private void ApplyLocateExpansion(LineageBulkSnapshot pSnapshot,
            LineageTreeReadMode pMode)
        {
            ResetFoldState();
            _lastTreeRootId = pSnapshot.RootActorId;
            if (pMode != LineageTreeReadMode.Locate ||
                pSnapshot.LocatePath.Count == 0)
            {
                _foldDecided.Add(pSnapshot.RootActorId);
                return;
            }

            for (int index = 0; index < pSnapshot.LocatePath.Count; index++)
            {
                long actorId = pSnapshot.LocatePath[index];
                _foldDecided.Add(actorId);
                if (index < pSnapshot.LocatePath.Count - 1)
                    _expanded.Add(actorId);
            }
        }

        private void ShowDetachedReadLoading()
        {
            if (_titleText != null)
                _titleText.text = AW_L10n.Text(
                    "aw_family_tree_loading", "Loading family tree");
        }

        private void ShowDetachedReadUnavailable()
        {
            if (_titleText != null)
                _titleText.text = AW_L10n.Text(
                    "aw_family_tree_unavailable", "Family tree unavailable");
        }

        private void QueueMaterialization(AWUiQueryKey pKey,
            LineageBulkSnapshot pSnapshot)
        {
            QueueMaterialization(pSnapshot, pKey.Revision);
        }

        private void QueueMaterialization(LineageBulkSnapshot pSnapshot,
            long pContentRevision,
            LineageBulkSnapshot pShadowSnapshot = null)
        {
            if (pSnapshot == null) return;
            CancelMaterialization(clearPendingRequest: true);
            AWUiIncrementalTicket ticket = _materializationState.Begin(
                AWAsyncRuntime.WorldGeneration, pContentRevision);
            AWUiMaterializationIntentLease intentLease =
                _intentState.Capture();
            _pendingMaterialization = new FamilyTreeMaterializationRequest(
                ticket, pSnapshot, _mode, _centerActorId, _rootActorId,
                _backShiId, _locateActorId, _showHalfSiblingRelations,
                intentLease, pShadowSnapshot,
                pReuseRenderedViews: false,
                pCenterOnTarget: _initialCenterRequested);
        }

        private void QueueBigTreeRelayout()
        {
            if (_mode != Mode.BigTree || _bulkSnapshot == null) return;
            long projectionRevision = FamilyTreeProjectionRevision.Current;
            if (_bulkSnapshotProjectionRevision != projectionRevision)
            {
                Rebuild();
                return;
            }

            CancelMaterialization(clearPendingRequest: true);
            BeginBoundedLineCleanup();
            AWUiIncrementalTicket ticket = _materializationState.Begin(
                AWAsyncRuntime.WorldGeneration, projectionRevision);
            AWUiMaterializationIntentLease intentLease =
                _intentState.Capture();
            _pendingMaterialization = new FamilyTreeMaterializationRequest(
                ticket, _bulkSnapshot, _mode, _centerActorId,
                _rootActorId, _backShiId, _locateActorId,
                _showHalfSiblingRelations, intentLease, null,
                pReuseRenderedViews: true,
                pCenterOnTarget: false);
        }

        private void AdvanceMaterialization()
        {
            FamilyTreeMaterializationRequest scheduledRequest =
                _activeMaterialization ?? _pendingMaterialization;
            if (scheduledRequest != null &&
                !IsMaterializationCurrent(scheduledRequest))
                CancelStaleMaterialization(scheduledRequest);

            int budget = _materializationState.TakeFrameStepBudget(
                int.MaxValue);
            while (budget > 0)
            {
                if (_cleanupPending)
                {
                    if (AdvanceCleanupStep()) budget--;
                    continue;
                }

                FamilyTreeMaterializationRequest request =
                    _activeMaterialization ?? _pendingMaterialization;
                if (request == null) return;
                if (!IsMaterializationCurrent(request))
                {
                    CancelStaleMaterialization(request);
                    continue;
                }

                if (_materializationSteps == null)
                {
                    _activeMaterialization = request;
                    _pendingMaterialization = null;
                    _locateFound = false;
                    _locateTarget = Vector2.zero;
                    _maxDepthY = 0f;
                    if (_canvas != null)
                        _canvas.gameObject.SetActive(true);
                    _materializationSteps =
                        MaterializeIncrementally(request).GetEnumerator();
                }

                bool advanced;
                using (LineageBulkSnapshotContext.Push(request.Snapshot))
                    advanced = _materializationSteps.MoveNext();
                if (advanced)
                {
                    budget--;
                    continue;
                }

                (_materializationSteps as System.IDisposable)?.Dispose();
                _materializationSteps = null;
                _activeMaterialization = null;
                _materializationState.Cancel();
            }
        }

        private void CancelStaleMaterialization(
            FamilyTreeMaterializationRequest pRequest)
        {
            bool sameWorld = pRequest.Ticket.WorldGeneration ==
                             AWAsyncRuntime.WorldGeneration;
            if (!sameWorld) _intentState.CancelAll();
            CancelMaterialization(clearPendingRequest: true);
            BeginBoundedCleanup();
        }

        private bool IsMaterializationCurrent(
            FamilyTreeMaterializationRequest pRequest)
        {
            long currentRoot = _mode == Mode.Family
                ? _centerActorId
                : _rootActorId;
            return isActiveAndEnabled && pRequest.Mode == _mode &&
                   pRequest.RootActorId == currentRoot &&
                   _materializationState.AcceptAcceptedSnapshot(
                       pRequest.Ticket, AWAsyncRuntime.WorldGeneration);
        }

        private void CancelMaterialization(bool clearPendingRequest)
        {
            _materializationState.Cancel();
            (_materializationSteps as System.IDisposable)?.Dispose();
            _materializationSteps = null;
            _activeMaterialization = null;
            if (clearPendingRequest) _pendingMaterialization = null;
        }

        private void BeginBoundedCleanup()
        {
            _cleanupLinesOnly = false;
            _bigTreeNodeViews.Clear();
            _cleanupPending = _spawned.Count > 0 || _lines.Count > 0;
            if (_cleanupPending && _canvas != null)
                _canvas.gameObject.SetActive(false);
            if (!_cleanupPending) _maxDepthY = 0f;
        }

        private void BeginBoundedLineCleanup()
        {
            _cleanupLinesOnly = true;
            _cleanupPending = _lines.Count > 0;
        }

        private bool AdvanceCleanupStep()
        {
            if (!_cleanupLinesOnly && _spawned.Count > 0)
            {
                int index = _spawned.Count - 1;
                FamilyTreeNodeView view = _spawned[index];
                _spawned.RemoveAt(index);
                if (view != null)
                {
                    view.gameObject.SetActive(false);
                    _nodePool.Add(view);
                }
                FinishCleanupIfEmpty();
                return true;
            }

            if (_lines.Count > 0)
            {
                int index = _lines.Count - 1;
                GameObject line = _lines[index];
                _lines.RemoveAt(index);
                if (line != null)
                {
                    line.SetActive(false);
                    _linePool.Add(line);
                }
                FinishCleanupIfEmpty();
                return true;
            }

            _cleanupPending = false;
            _cleanupLinesOnly = false;
            if (_spawned.Count == 0) _maxDepthY = 0f;
            return false;
        }

        private void FinishCleanupIfEmpty()
        {
            if ((!_cleanupLinesOnly && _spawned.Count > 0) ||
                _lines.Count > 0) return;
            _cleanupPending = false;
            _cleanupLinesOnly = false;
            if (_spawned.Count == 0) _maxDepthY = 0f;
        }

        /// <summary>小树:在本人节点正上方画父母行(1~2 个),并连线到本人。点击父母 → 以其为中心重开小树(上溯)。</summary>
        private void ApplyInitialCenterPan(
            FamilyTreeMaterializationRequest pRequest)
        {
            if (pRequest == null || !pRequest.CenterOnTarget ||
                _canvasRect == null || pRequest.Root == null) return;

            Vector2 target = pRequest.Mode == Mode.BigTree &&
                             pRequest.LocateActorId >= 0 && _locateFound
                ? _locateTarget
                : new Vector2(pRequest.Root.centerX, pRequest.Root.topY);
            float x = FamilyTreeViewportLayoutRules.CenterPanX(target.x,
                LiveViewportWidth(), VIEWPORT_W);
            float y = FamilyTreeViewportLayoutRules.CenterPanY(target.y,
                NODE_H, LiveViewportHeight(), VIEWPORT_H);
            _canvasRect.anchoredPosition = new Vector2(x, y);
            // 记录当前树根的布局锚点 —— 「复位」按钮靠它把被拖出画布的
            // 族谱拉回视口中心。初开/定位时都刷新,保证复位目标始终是当前树。
            _resetAnchorReady = true;
            _resetRootAnchor = new Vector2(target.x, target.y);
        }

        /// <summary>
        ///     把「复位」锚点刷成当前树根。「复位」= 把被拖出画布的族谱重新拉回
        ///     视口中心。锚点用布局后的树根坐标(centerX/topY),而不是靠
        ///     _canvasRect 当前位移反推 —— 后者在只拖动不重排时也是对的,但
        ///     重排(展开/收起/跳转)后必须跟随新的根。
        /// </summary>
        private void RefreshResetAnchor(
            FamilyTreeMaterializationRequest pRequest)
        {
            if (pRequest?.Root == null) return;
            Vector2 target = pRequest.Mode == Mode.BigTree &&
                             pRequest.LocateActorId >= 0 && _locateFound
                ? _locateTarget
                : new Vector2(pRequest.Root.centerX, pRequest.Root.topY);
            _resetAnchorReady = true;
            _resetRootAnchor = new Vector2(target.x, target.y);
        }

        /// <summary>把被拖出画布的族谱拉回视口中心,并复位缩放。</summary>
        private void ResetViewToAnchor()
        {
            if (_canvasRect == null) return;
            if (!_resetAnchorReady)
            {
                // 锚点还没就绪(极早期),退化为零位(左上),至少保证画布可见。
                _canvasRect.anchoredPosition = Vector2.zero;
                _canvasRect.localScale = Vector3.one;
                return;
            }
            float x = FamilyTreeViewportLayoutRules.CenterPanX(
                _resetRootAnchor.x, LiveViewportWidth(), VIEWPORT_W);
            float y = FamilyTreeViewportLayoutRules.CenterPanY(
                _resetRootAnchor.y, NODE_H, LiveViewportHeight(), VIEWPORT_H);
            _canvasRect.anchoredPosition = new Vector2(x, y);
            _canvasRect.localScale = Vector3.one;
        }

        private float LiveViewportWidth()
        {
            return _viewportRect != null ? _viewportRect.rect.width : 0f;
        }

        private float LiveViewportHeight()
        {
            return _viewportRect != null ? _viewportRect.rect.height : 0f;
        }

        private void CommitInitialCenter(
            FamilyTreeMaterializationRequest pRequest)
        {
            if (pRequest?.CenterOnTarget == true)
                _initialCenterRequested = false;
        }

        private void SpawnFamilySideNode(FamilyTreeNode pData, float pCenterX,
            float pTopY, long pBackShiId)
        {
            var view = AcquireNode();
            long id = pData.id;
            view.Bind(pData, (_) => OpenFamilyTree(id, pBackShiId),
                null, false, false, null, null);
            var rect = view.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(pCenterX, -pTopY);
            _spawned.Add(view);
        }

        private static float GetSiblingRowWidth(TreeLayoutNode pRoot)
        {
            if (pRoot == null || pRoot.siblings.Count == 0) return 0f;
            return Mathf.Max(
                GetSiblingSideExtent(pRoot.olderSiblingCount),
                GetSiblingSideExtent(pRoot.youngerSiblingCount)) * 2f;
        }

        private static float GetSiblingSideExtent(int pCount)
        {
            float extent = NODE_W / 2f;
            if (pCount <= 0) return extent;
            return extent + H_GAP + pCount * NODE_W + (pCount - 1) * H_GAP;
        }

        private static bool IsProvenHalfSibling(bool pSharedFather, bool pSharedMother,
            bool pCenterHasFather, bool pCenterHasMother,
            bool pSiblingHasFather, bool pSiblingHasMother)
        {
            return (pSharedFather && !pSharedMother && pCenterHasMother && pSiblingHasMother)
                || (pSharedMother && !pSharedFather && pCenterHasFather && pSiblingHasFather);
        }

        private static string BuildBasicSiblingRelationLabel(FamilyTreeNode pSibling, FamilyTreeNode pCenter)
        {
            bool older = IsOlderThanCenter(pSibling, pCenter);
            if (pSibling?.sex == 0)
                return older
                    ? AW_L10n.Text("aw_relation_older_brother", "\u5144")
                    : AW_L10n.Text("aw_relation_younger_brother", "\u5F1F");
            return older
                ? AW_L10n.Text("aw_relation_older_sister", "\u59D0")
                : AW_L10n.Text("aw_relation_younger_sister", "\u59B9");
        }

        private static string BuildSiblingRelationLabel(FamilyTreeNode pSibling, FamilyTreeNode pCenter,
            bool pSharedFather, bool pSharedMother,
            bool pCenterHasFather, bool pCenterHasMother,
            bool pSiblingHasFather, bool pSiblingHasMother)
        {
            bool older = IsOlderThanCenter(pSibling, pCenter);
            bool male = pSibling?.sex == 0;
            if (pSharedFather && !pSharedMother && pCenterHasMother && pSiblingHasMother)
            {
                if (male)
                    return older
                        ? AW_L10n.Text("aw_relation_same_father_older_brother", "\u540C\u7236\u5F02\u6BCD\u5144")
                        : AW_L10n.Text("aw_relation_same_father_younger_brother", "\u540C\u7236\u5F02\u6BCD\u5F1F");
                return older
                    ? AW_L10n.Text("aw_relation_same_father_older_sister", "\u540C\u7236\u5F02\u6BCD\u59D0")
                    : AW_L10n.Text("aw_relation_same_father_younger_sister", "\u540C\u7236\u5F02\u6BCD\u59B9");
            }
            if (pSharedMother && !pSharedFather && pCenterHasFather && pSiblingHasFather)
            {
                if (male)
                    return older
                        ? AW_L10n.Text("aw_relation_same_mother_older_brother", "\u540C\u6BCD\u5F02\u7236\u5144")
                        : AW_L10n.Text("aw_relation_same_mother_younger_brother", "\u540C\u6BCD\u5F02\u7236\u5F1F");
                return older
                    ? AW_L10n.Text("aw_relation_same_mother_older_sister", "\u540C\u6BCD\u5F02\u7236\u59D0")
                    : AW_L10n.Text("aw_relation_same_mother_younger_sister", "\u540C\u6BCD\u5F02\u7236\u59B9");
            }

            if (male)
                return older
                    ? AW_L10n.Text("aw_relation_older_brother", "\u5144")
                    : AW_L10n.Text("aw_relation_younger_brother", "\u5F1F");
            return older
                ? AW_L10n.Text("aw_relation_older_sister", "\u59D0")
                : AW_L10n.Text("aw_relation_younger_sister", "\u59B9");
        }

        private IEnumerable ApplyTreeGenerationIncrementally(
            FamilyTreeNode pNode)
        {
            if (pNode == null || pNode.tree_generation > 0) yield break;
            LineageBulkSnapshot snapshot =
                LineageBulkSnapshotContext.Current;
            if (snapshot == null ||
                !snapshot.TryGetNode(pNode.id,
                    out LineageTreeNodeSnapshot nodeSnapshot)) yield break;
            long founderId = nodeSnapshot.ShiFounderActorId;
            yield return null;
            if (founderId < 0) yield break;

            var visited = new HashSet<long> { pNode.id };
            var frames = new Stack<GenerationFrame>();
            frames.Push(new GenerationFrame
            {
                ActorId = pNode.id,
                Depth = 0
            });
            yield return null;
            while (frames.Count > 0)
            {
                GenerationFrame frame = frames.Peek();
                if (frame.ActorId == founderId)
                {
                    pNode.tree_generation = frame.Depth + 1;
                    yield return null;
                    yield break;
                }
                if (frame.Depth > 96)
                {
                    frames.Pop();
                    yield return null;
                    continue;
                }
                if (!frame.Loaded)
                {
                    frame.ParentIds = GetParentIdsForMaterialization(
                        frame.ActorId, pUseReverseLiveLookup: false);
                    frame.Loaded = true;
                    yield return null;
                    continue;
                }
                if (frame.ParentIndex < frame.ParentIds.Count)
                {
                    long parentId =
                        frame.ParentIds[frame.ParentIndex++];
                    if (parentId >= 0 && visited.Add(parentId))
                        frames.Push(new GenerationFrame
                        {
                            ActorId = parentId,
                            Depth = frame.Depth + 1
                        });
                    yield return null;
                    continue;
                }
                frames.Pop();
                yield return null;
            }
        }

        private static string BuildSiblingRelationLabel(FamilyTreeNode pSibling, FamilyTreeNode pCenter)
        {
            bool older = IsOlderThanCenter(pSibling, pCenter);
            if (pSibling.sex == 0)
                return older
                    ? AW_L10n.Text("aw_relation_older_brother", "兄")
                    : AW_L10n.Text("aw_relation_younger_brother", "弟");
            return older
                ? AW_L10n.Text("aw_relation_older_sister", "姐")
                : AW_L10n.Text("aw_relation_younger_sister", "妹");
        }

        private static bool IsOlderThanCenter(FamilyTreeNode pSibling, FamilyTreeNode pCenter)
        {
            if (pSibling == null || pCenter == null) return false;
            if (pSibling.birth_time > 0 && pCenter.birth_time > 0 &&
                !Mathf.Approximately((float)pSibling.birth_time, (float)pCenter.birth_time))
                return pSibling.birth_time < pCenter.birth_time;
            return pSibling.id < pCenter.id;
        }

        private static int CompareByBirth(FamilyTreeNode pLeft, FamilyTreeNode pRight)
        {
            double lb = pLeft?.birth_time ?? 0;
            double rb = pRight?.birth_time ?? 0;
            int cmp = lb.CompareTo(rb);
            if (cmp != 0) return cmp;
            long lid = pLeft?.id ?? -1;
            long rid = pRight?.id ?? -1;
            return lid.CompareTo(rid);
        }

        private sealed class FamilyTreeMaterializationRequest
        {
            public FamilyTreeMaterializationRequest(
                AWUiIncrementalTicket pTicket,
                LineageBulkSnapshot pSnapshot, Mode pMode,
                long pCenterActorId, long pBigTreeRootId, long pBackShiId,
                long pLocateActorId, bool pShowHalfSiblings,
                AWUiMaterializationIntentLease pIntentLease,
                LineageBulkSnapshot pShadowSnapshot,
                bool pReuseRenderedViews, bool pCenterOnTarget)
            {
                Ticket = pTicket;
                Snapshot = pSnapshot;
                Mode = pMode;
                CenterActorId = pCenterActorId;
                BigTreeRootId = pBigTreeRootId;
                RootActorId = pMode == Mode.Family
                    ? pCenterActorId
                    : pBigTreeRootId;
                BackShiId = pBackShiId;
                LocateActorId = pLocateActorId;
                ShowHalfSiblings = pShowHalfSiblings;
                IntentLease = pIntentLease;
                ShadowSnapshot = pShadowSnapshot;
                ReuseRenderedViews = pReuseRenderedViews;
                CenterOnTarget = pCenterOnTarget;
            }

            public AWUiIncrementalTicket Ticket { get; }
            public LineageBulkSnapshot Snapshot { get; }
            public Mode Mode { get; }
            public long CenterActorId { get; }
            public long BigTreeRootId { get; }
            public long RootActorId { get; }
            public long BackShiId { get; }
            public long LocateActorId { get; }
            public bool ShowHalfSiblings { get; }
            public AWUiMaterializationIntentLease IntentLease { get; }
            public bool PreservePan => IntentLease.PreservePan;
            public Vector2 SavedPan => new Vector2(IntentLease.PanX,
                IntentLease.PanY);
            public bool ExpandLiveBranches => IntentLease.ExpandLive;
            public bool ExpandLiveCompleted;
            public LineageBulkSnapshot ShadowSnapshot { get; }
            public bool ReuseRenderedViews { get; }
            public bool CenterOnTarget { get; }
            public HashSet<long> RenderedActorIds { get; } =
                new HashSet<long>();
            public IReadOnlyList<long> SynchronousParentIds =
                System.Array.Empty<long>();
            public IReadOnlyList<long> SynchronousChildIds =
                System.Array.Empty<long>();
            public TreeLayoutNode Root;
            public float TotalWidth;
            public readonly Dictionary<long, bool> ParentHasParents =
                new Dictionary<long, bool>();
            public readonly AWUiActorVisitBudget VisitBudget =
                new AWUiActorVisitBudget(MAX_AUTO_EXPAND_VISITS);
            public readonly HashSet<long> VisitedNodeIds =
                new HashSet<long>();
        }

        private sealed class BigTreeBuildFrame
        {
            public TreeLayoutNode Node;
            public int Depth;
            public IReadOnlyList<long> ChildIds;
            public int ChildIndex;
            public long CandidateId;
            public bool CandidateIsAgnatic;
            public FamilyTreeNode CandidateData;
            public int Stage;
        }

        private sealed class ExpandLiveFrame
        {
            public long ActorId;
            public int Depth;
            public FamilyTreeNode ActorData;
            public IReadOnlyList<long> ChildIds =
                System.Array.Empty<long>();
            public int ChildIndex;
            public long CandidateId;
            public bool CandidateIsAgnatic;
            public FamilyTreeNode CandidateData;
            public bool HasVisibleChildren;
            public bool DescendantAlive;
            public ExpandLiveFrame Parent;
            public int Stage;
        }

        private sealed class GenerationFrame
        {
            public long ActorId;
            public int Depth;
            public IReadOnlyList<long> ParentIds =
                System.Array.Empty<long>();
            public int ParentIndex;
            public bool Loaded;
        }

        private sealed class MeasureFrame
        {
            public TreeLayoutNode Node;
            public MeasureFrame Parent;
            public int ChildIndex;
            public int MeasuredChildren;
            public float ChildWidthSum;
        }

        private sealed class RenderFrame
        {
            public TreeLayoutNode Node;
            public float XStart;
            public float Y;
            public float Width;
            public float Cursor;
            public int ChildIndex;
            public bool Spawned;
            public bool ConnectorPending;
            public TreeLayoutNode ConnectorChild;
            public int ConnectorSegment;
        }

        private sealed class FamilyTreeNodeBirthComparer :
            IComparer<FamilyTreeNode>
        {
            public int Compare(FamilyTreeNode pLeft, FamilyTreeNode pRight)
            {
                int value = CompareByBirth(pLeft, pRight);
                if (value != 0) return value;
                if (ReferenceEquals(pLeft, pRight)) return 0;
                long leftId = pLeft?.id ?? -1L;
                long rightId = pRight?.id ?? -1L;
                return leftId.CompareTo(rightId);
            }
        }

        private class TreeLayoutNode
        {
            public FamilyTreeNode data;
            public bool expanded;
            public bool hasChildren;
            public List<TreeLayoutNode> children = new List<TreeLayoutNode>();
            public float subtreeWidth;
            public float childrenWidth;
            public float centerX;
            public float topY;
            public List<FamilyTreeNode> siblings = new List<FamilyTreeNode>();
            public int olderSiblingCount;
            public int youngerSiblingCount;
            // 小树根专用:本人的父母节点(画在本人正上方一层,可点击上溯)。
            public List<FamilyTreeNode> parents = new List<FamilyTreeNode>();
        }

        private IEnumerable MaterializeIncrementally(
            FamilyTreeMaterializationRequest pRequest)
        {
            ResetQueryCache();
            yield return null;

            if (pRequest.Mode == Mode.BigTree &&
                pRequest.ExpandLiveBranches)
            {
                IEnumerator expandSteps =
                    ExpandLiveBranchesIncrementally(pRequest)
                        .GetEnumerator();
                while (expandSteps.MoveNext()) yield return null;
                (expandSteps as System.IDisposable)?.Dispose();
                pRequest.ExpandLiveCompleted = true;
            }

            IEnumerable build = pRequest.Mode == Mode.Family
                ? BuildFamilyIncrementally(pRequest)
                : BuildBigTreeIncrementally(pRequest);
            IEnumerator buildSteps = build.GetEnumerator();
            while (buildSteps.MoveNext()) yield return null;
            (buildSteps as System.IDisposable)?.Dispose();
            if (pRequest.ShadowSnapshot != null)
            {
                IEnumerator shadowSteps =
                    CompareShadowAdjacencyIncrementally(pRequest)
                        .GetEnumerator();
                while (shadowSteps.MoveNext()) yield return null;
                (shadowSteps as System.IDisposable)?.Dispose();
            }
            if (pRequest.Root == null) yield break;
            PrepareMaterializationSurface(pRequest);
            yield return null;

            IEnumerator measureSteps = MeasureIncrementally(
                pRequest.Root).GetEnumerator();
            while (measureSteps.MoveNext()) yield return null;
            (measureSteps as System.IDisposable)?.Dispose();

            float siblingRowWidth = pRequest.Mode == Mode.Family
                ? GetSiblingRowWidth(pRequest.Root)
                : 0f;
            pRequest.TotalWidth = Mathf.Max(
                pRequest.Root.subtreeWidth, siblingRowWidth);
            float startX = FamilyTreeViewportLayoutRules.CenteredTreeStartX(
                pRequest.TotalWidth, PAD, LiveViewportWidth(), VIEWPORT_W);
            bool hasParents = pRequest.Mode == Mode.Family &&
                              pRequest.Root.parents.Count > 0;
            float bodyTopY = PAD +
                             (hasParents ? NODE_H + V_GAP : 0f);
            float rootStartX = startX +
                               (pRequest.TotalWidth -
                                pRequest.Root.subtreeWidth) / 2f;
            yield return null;

            PrepareFullRenderSwap(pRequest);
            while (_cleanupPending) yield return null;
            if (_canvas != null) _canvas.gameObject.SetActive(true);

            IEnumerator renderSteps = RenderTreeIncrementally(pRequest,
                pRequest.Root, rootStartX, bodyTopY,
                pRequest.Root.subtreeWidth).GetEnumerator();
            while (renderSteps.MoveNext()) yield return null;
            (renderSteps as System.IDisposable)?.Dispose();

            if (hasParents)
            {
                IEnumerator parentSteps = RenderParentsIncrementally(
                    pRequest, pRequest.Root).GetEnumerator();
                while (parentSteps.MoveNext()) yield return null;
                (parentSteps as System.IDisposable)?.Dispose();
            }
            if (pRequest.Mode == Mode.Family &&
                pRequest.Root.siblings.Count > 0)
            {
                IEnumerator siblingSteps = RenderSiblingsIncrementally(
                    pRequest, pRequest.Root).GetEnumerator();
                while (siblingSteps.MoveNext()) yield return null;
                (siblingSteps as System.IDisposable)?.Dispose();
            }

            if (pRequest.ReuseRenderedViews)
            {
                IEnumerator recycleSteps =
                    RecycleUnusedBigTreeViewsIncrementally(pRequest)
                        .GetEnumerator();
                while (recycleSteps.MoveNext()) yield return null;
                (recycleSteps as System.IDisposable)?.Dispose();
            }

            _canvasRect.sizeDelta = new Vector2(
                FamilyTreeViewportLayoutRules.CanvasWidth(
                    pRequest.TotalWidth, PAD, LiveViewportWidth(), VIEWPORT_W),
                _maxDepthY + NODE_H + PAD);
            if (pRequest.Mode == Mode.BigTree &&
                pRequest.LocateActorId >= 0 && !_locateFound)
            {
                _locateActorId = pRequest.Root.data.id;
                _locateTarget = new Vector2(pRequest.Root.centerX,
                    pRequest.Root.topY);
                _locateFound = true;
            }
            if (pRequest.CenterOnTarget)
                ApplyInitialCenterPan(pRequest);
            else if (pRequest.PreservePan)
                _canvasRect.anchoredPosition = pRequest.SavedPan;
            RefreshResetAnchor(pRequest);
            CommitInitialCenter(pRequest);
            _intentState.Commit(pRequest.IntentLease);
            yield return null;
        }

        private void PrepareFullRenderSwap(
            FamilyTreeMaterializationRequest pRequest)
        {
            if (pRequest?.ReuseRenderedViews == true) return;
            BeginBoundedCleanup();
        }

        private IEnumerable CompareShadowAdjacencyIncrementally(
            FamilyTreeMaterializationRequest pRequest)
        {
            int synchronousLength =
                pRequest.SynchronousParentIds.Count + 1 +
                pRequest.SynchronousChildIds.Count;
            var synchronous = new long[synchronousLength];
            int synchronousIndex = 0;
            yield return null;
            for (int index = 0;
                 index < pRequest.SynchronousParentIds.Count; index++)
            {
                synchronous[synchronousIndex++] =
                    pRequest.SynchronousParentIds[index];
                yield return null;
            }
            synchronous[synchronousIndex++] = long.MinValue;
            yield return null;
            for (int index = 0;
                 index < pRequest.SynchronousChildIds.Count; index++)
            {
                synchronous[synchronousIndex++] =
                    pRequest.SynchronousChildIds[index];
                yield return null;
            }

            IReadOnlyList<long> asyncParents =
                pRequest.ShadowSnapshot.ParentIds(pRequest.RootActorId);
            yield return null;
            IReadOnlyList<long> asyncChildren =
                pRequest.ShadowSnapshot.ChildIds(pRequest.RootActorId);
            yield return null;
            var asynchronous = new long[
                asyncParents.Count + 1 + asyncChildren.Count];
            int asynchronousIndex = 0;
            yield return null;
            for (int index = 0; index < asyncParents.Count; index++)
            {
                asynchronous[asynchronousIndex++] = asyncParents[index];
                yield return null;
            }
            asynchronous[asynchronousIndex++] = long.MinValue;
            yield return null;
            for (int index = 0; index < asyncChildren.Count; index++)
            {
                asynchronous[asynchronousIndex++] = asyncChildren[index];
                yield return null;
            }

            var comparison = new AWUiIncrementalIdComparison(
                synchronous, asynchronous);
            while (comparison.MoveNext()) yield return null;
            if (!comparison.IsMatch)
                ModClass.LogWarning("Family tree shadow adjacency mismatch at " +
                                    comparison.MismatchIndex);
        }

        private IEnumerable MeasureIncrementally(TreeLayoutNode pRoot)
        {
            if (pRoot == null) yield break;
            var frames = new Stack<MeasureFrame>();
            frames.Push(new MeasureFrame { Node = pRoot });
            yield return null;

            while (frames.Count > 0)
            {
                MeasureFrame frame = frames.Peek();
                bool terminal = frame.Node.children.Count == 0 ||
                                !frame.Node.expanded;
                if (terminal)
                {
                    frame.Node.childrenWidth = 0f;
                    frame.Node.subtreeWidth = NODE_W;
                    frames.Pop();
                    AccumulateMeasuredChild(frame);
                    yield return null;
                    continue;
                }

                if (frame.ChildIndex < frame.Node.children.Count)
                {
                    TreeLayoutNode child =
                        frame.Node.children[frame.ChildIndex++];
                    frames.Push(new MeasureFrame
                    {
                        Node = child,
                        Parent = frame
                    });
                    yield return null;
                    continue;
                }

                frame.Node.childrenWidth = frame.ChildWidthSum +
                    Mathf.Max(0, frame.MeasuredChildren - 1) * H_GAP;
                frame.Node.subtreeWidth = Mathf.Max(NODE_W,
                    frame.Node.childrenWidth);
                frames.Pop();
                AccumulateMeasuredChild(frame);
                yield return null;
            }
        }

        private static void AccumulateMeasuredChild(MeasureFrame pFrame)
        {
            if (pFrame.Parent == null) return;
            pFrame.Parent.MeasuredChildren++;
            pFrame.Parent.ChildWidthSum += pFrame.Node.subtreeWidth;
        }

        private IEnumerable RenderTreeIncrementally(
            FamilyTreeMaterializationRequest pRequest,
            TreeLayoutNode pRoot, float pXStart, float pY, float pWidth)
        {
            if (pRoot == null) yield break;
            var frames = new Stack<RenderFrame>();
            frames.Push(new RenderFrame
            {
                Node = pRoot,
                XStart = pXStart,
                Y = pY,
                Width = pWidth
            });
            yield return null;

            while (frames.Count > 0)
            {
                RenderFrame frame = frames.Peek();
                if (!frame.Spawned)
                {
                    frame.Node.centerX = frame.XStart + frame.Width / 2f;
                    frame.Node.topY = frame.Y;
                    _maxDepthY = Mathf.Max(_maxDepthY, frame.Y + NODE_H);
                    SpawnNode(frame.Node, pRequest);
                    frame.Cursor = frame.XStart +
                        (frame.Width - frame.Node.childrenWidth) / 2f;
                    frame.Spawned = true;
                    yield return null;
                    continue;
                }

                if (frame.ConnectorPending)
                {
                    DrawConnectorSegment(frame.Node.centerX,
                        frame.Y + NODE_H,
                        frame.ConnectorChild.centerX,
                        frame.ConnectorChild.topY,
                        frame.ConnectorSegment++);
                    if (frame.ConnectorSegment >= 3)
                    {
                        frame.ConnectorPending = false;
                        frame.ConnectorChild = null;
                        frame.ConnectorSegment = 0;
                    }
                    yield return null;
                    continue;
                }

                if (!frame.Node.expanded ||
                    frame.ChildIndex >= frame.Node.children.Count)
                {
                    frames.Pop();
                    yield return null;
                    continue;
                }

                TreeLayoutNode child =
                    frame.Node.children[frame.ChildIndex++];
                float childX = frame.Cursor;
                frame.Cursor += child.subtreeWidth + H_GAP;
                frame.ConnectorPending = true;
                frame.ConnectorChild = child;
                frames.Push(new RenderFrame
                {
                    Node = child,
                    XStart = childX,
                    Y = frame.Y + NODE_H + V_GAP,
                    Width = child.subtreeWidth
                });
                yield return null;
            }
        }

        private IEnumerable RenderParentsIncrementally(
            FamilyTreeMaterializationRequest pRequest,
            TreeLayoutNode pRoot)
        {
            int count = pRoot.parents.Count;
            float rowWidth = count * NODE_W +
                             Mathf.Max(0, count - 1) * H_GAP;
            float startX = pRoot.centerX - rowWidth / 2f;
            for (int index = 0; index < count; index++)
            {
                FamilyTreeNode data = pRoot.parents[index];
                float centerX = startX + NODE_W / 2f +
                                index * (NODE_W + H_GAP);
                FamilyTreeNodeView view = AcquireNode();
                long actorId = data.id;
                bool hasParents = pRequest.ParentHasParents.TryGetValue(
                    actorId, out bool knownHasParents) && knownHasParents;
                System.Action onUp = hasParents
                    ? (System.Action)(() => OpenFamilyTree(actorId,
                        pRequest.BackShiId))
                    : null;
                view.Bind(data,
                    _ => OpenFamilyTree(actorId, pRequest.BackShiId),
                    null, false, false, onUp, null);
                RectTransform rect = view.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(centerX, -PAD);
                _spawned.Add(view);
                yield return null;

                IEnumerator connector = DrawConnectorIncrementally(
                    centerX, PAD + NODE_H, pRoot.centerX,
                    pRoot.topY).GetEnumerator();
                while (connector.MoveNext()) yield return null;
                (connector as System.IDisposable)?.Dispose();
            }
        }

        private IEnumerable RenderSiblingsIncrementally(
            FamilyTreeMaterializationRequest pRequest,
            TreeLayoutNode pRoot)
        {
            var left = new List<FamilyTreeNode>();
            var right = new List<FamilyTreeNode>();
            for (int index = 0; index < pRoot.siblings.Count; index++)
            {
                FamilyTreeNode sibling = pRoot.siblings[index];
                if (IsOlderThanCenter(sibling, pRoot.data))
                    left.Add(sibling);
                else
                    right.Add(sibling);
                yield return null;
            }

            float y = pRoot.topY;
            float leftStart = pRoot.centerX - NODE_W / 2f - H_GAP -
                              left.Count * NODE_W -
                              Mathf.Max(0, left.Count - 1) * H_GAP;
            for (int index = 0; index < left.Count; index++)
            {
                SpawnFamilySideNode(left[index], leftStart + NODE_W / 2f +
                    index * (NODE_W + H_GAP), y, pRequest.BackShiId);
                yield return null;
            }

            float rightStart = pRoot.centerX + NODE_W / 2f + H_GAP;
            for (int index = 0; index < right.Count; index++)
            {
                SpawnFamilySideNode(right[index],
                    rightStart + NODE_W / 2f +
                    index * (NODE_W + H_GAP), y, pRequest.BackShiId);
                yield return null;
            }
        }

        private IEnumerable DrawConnectorIncrementally(float pParentCx,
            float pParentBottomY, float pChildCx, float pChildTopY)
        {
            for (int segment = 0; segment < 3; segment++)
            {
                DrawConnectorSegment(pParentCx, pParentBottomY, pChildCx,
                    pChildTopY, segment);
                yield return null;
            }
        }

        private void DrawConnectorSegment(float pParentCx,
            float pParentBottomY, float pChildCx, float pChildTopY,
            int pSegment)
        {
            float midY = (pParentBottomY + pChildTopY) / 2f;
            if (pSegment == 0)
                DrawLine(pParentCx, pParentBottomY, pParentCx, midY);
            else if (pSegment == 1)
                DrawLine(pParentCx, midY, pChildCx, midY);
            else
                DrawLine(pChildCx, midY, pChildCx, pChildTopY);
        }

        private void PrepareMaterializationSurface(
            FamilyTreeMaterializationRequest pRequest)
        {
            if (!pRequest.PreservePan)
                _canvasRect.anchoredPosition = Vector2.zero;
            bool showTreeTools = pRequest.Mode == Mode.BigTree;
            long parentShiId = -1L;
            if (showTreeTools && pRequest.Snapshot != null &&
                pRequest.Snapshot.TryGetNode(pRequest.BigTreeRootId,
                    out LineageTreeNodeSnapshot rootNode))
                parentShiId = rootNode.ParentShiId;
            if (_backButton != null)
                _backButton.gameObject.SetActive(
                    (pRequest.Mode == Mode.Family &&
                     pRequest.BackShiId >= 0) || parentShiId >= 0);
            if (_backText != null)
                _backText.text = showTreeTools
                    ? AW_L10n.Text("aw_return_home_shi", "Return home")
                    : AW_L10n.Text("aw_locate_clan_tree", "Locate clan tree");
            if (_expandButton != null)
                _expandButton.gameObject.SetActive(showTreeTools);
            if (_collapseButton != null)
                _collapseButton.gameObject.SetActive(showTreeTools);
            if (_halfSiblingButton != null)
                _halfSiblingButton.gameObject.SetActive(!showTreeTools);
            if (_renameClanButton != null)
                _renameClanButton.gameObject.SetActive(showTreeTools);
            if (_renameSurnameButton != null)
                _renameSurnameButton.gameObject.SetActive(!showTreeTools);
            if (_renameClanPanel != null &&
                (_renameSurnameMode == showTreeTools))
                _renameClanPanel.SetActive(false);
            UpdateHalfSiblingButtonText();
            if (_titleText != null)
                _titleText.text = showTreeTools
                    ? AW_L10n.Text("aw_clan_big_tree", "Clan tree")
                    : AW_L10n.Text("aw_family_tree_short", "Family tree");
        }

        // 小树:本人为根(父母画在上方、子女为子树)。
        private IEnumerable BuildFamilyIncrementally(
            FamilyTreeMaterializationRequest pRequest)
        {
            FamilyTreeNode center = BuildTreeNodeData(
                pRequest.CenterActorId);
            yield return null;
            if (center == null) yield break;

            var root = new TreeLayoutNode
            {
                data = center,
                expanded = true
            };
            pRequest.Root = root;
            IEnumerator centerGeneration =
                ApplyTreeGenerationIncrementally(center).GetEnumerator();
            while (centerGeneration.MoveNext()) yield return null;
            (centerGeneration as System.IDisposable)?.Dispose();
            center.relation_label = AW_L10n.Text(
                "aw_relation_self", "Self");
            yield return null;

            IReadOnlyList<long> parentIds =
                GetParentIdsForMaterialization(center.id,
                    pUseReverseLiveLookup: true);
            if (pRequest.ShadowSnapshot != null)
                pRequest.SynchronousParentIds = parentIds;
            yield return null;
            var centerFatherIds = new HashSet<long>();
            var centerMotherIds = new HashSet<long>();
            var centerResolvedParentIds = new HashSet<long>();
            for (int parentIndex = 0;
                 parentIndex < parentIds.Count; parentIndex++)
            {
                long parentId = parentIds[parentIndex];
                FamilyTreeNode parent = BuildTreeNodeData(parentId);
                if (parent != null)
                {
                    parent.relation_label = parent.sex == 0
                        ? AW_L10n.Text("aw_relation_father", "Father")
                        : AW_L10n.Text("aw_relation_mother", "Mother");
                    IEnumerator parentGeneration =
                        ApplyTreeGenerationIncrementally(parent)
                            .GetEnumerator();
                    while (parentGeneration.MoveNext()) yield return null;
                    (parentGeneration as System.IDisposable)?.Dispose();
                    root.parents.Add(parent);
                    centerResolvedParentIds.Add(parent.id);
                    if (parent.sex == 0) centerFatherIds.Add(parent.id);
                    else centerMotherIds.Add(parent.id);
                }
                yield return null;

                IReadOnlyList<long> grandparentIds =
                    GetParentIdsForMaterialization(parentId,
                        pUseReverseLiveLookup: true);
                pRequest.ParentHasParents[parentId] =
                    grandparentIds.Count > 0;
                yield return null;
            }

            var seenSiblings = new HashSet<long>();
            var orderedSiblings = new SortedSet<FamilyTreeNode>(
                new FamilyTreeNodeBirthComparer());
            for (int parentIndex = 0;
                 parentIndex < parentIds.Count; parentIndex++)
            {
                IReadOnlyList<long> siblingIds = GetChildIdsCached(
                    parentIds[parentIndex]);
                yield return null;
                for (int siblingIndex = 0;
                     siblingIndex < siblingIds.Count; siblingIndex++)
                {
                    long siblingId = siblingIds[siblingIndex];
                    bool candidate = siblingId != center.id &&
                                     seenSiblings.Add(siblingId);
                    yield return null;
                    if (!candidate) continue;

                    FamilyTreeNode sibling = BuildTreeNodeData(siblingId);
                    yield return null;
                    if (sibling == null) continue;

                    IReadOnlyList<long> siblingParentIds =
                        GetParentIdsForMaterialization(siblingId,
                            pUseReverseLiveLookup: true);
                    bool sharesFather = false;
                    bool sharesMother = false;
                    bool siblingHasFather = false;
                    bool siblingHasMother = false;
                    var siblingResolvedParentIds = new HashSet<long>();
                    yield return null;
                    for (int relationIndex = 0;
                         relationIndex < siblingParentIds.Count;
                         relationIndex++)
                    {
                        long relationId = siblingParentIds[relationIndex];
                        FamilyTreeNode relation = BuildTreeNodeData(
                            relationId);
                        if (relation != null)
                            siblingResolvedParentIds.Add(relationId);
                        if (relation != null && relation.sex == 0)
                        {
                            siblingHasFather = true;
                            sharesFather |= centerFatherIds.Contains(
                                relationId);
                        }
                        else if (relation != null)
                        {
                            siblingHasMother = true;
                            sharesMother |= centerMotherIds.Contains(
                                relationId);
                        }
                        yield return null;
                    }

                    if (!FamilyTreeRelationRules.ShouldIncludeSibling(
                            parentIds, siblingParentIds,
                            centerResolvedParentIds,
                            siblingResolvedParentIds,
                            pRequest.ShowHalfSiblings))
                    {
                        yield return null;
                        continue;
                    }

                    sibling.relation_label = pRequest.ShowHalfSiblings
                        ? BuildSiblingRelationLabel(sibling, center,
                            sharesFather, sharesMother,
                            centerFatherIds.Count > 0,
                            centerMotherIds.Count > 0,
                            siblingHasFather, siblingHasMother)
                        : BuildBasicSiblingRelationLabel(sibling, center);
                    IEnumerator siblingGeneration =
                        ApplyTreeGenerationIncrementally(sibling)
                            .GetEnumerator();
                    while (siblingGeneration.MoveNext()) yield return null;
                    (siblingGeneration as System.IDisposable)?.Dispose();
                    orderedSiblings.Add(sibling);
                    yield return null;
                }
            }

            IEnumerator<FamilyTreeNode> ordered =
                orderedSiblings.GetEnumerator();
            while (ordered.MoveNext())
            {
                FamilyTreeNode sibling = ordered.Current;
                root.siblings.Add(sibling);
                if (IsOlderThanCenter(sibling, center))
                    root.olderSiblingCount++;
                else
                    root.youngerSiblingCount++;
                yield return null;
            }
            ordered.Dispose();

            IReadOnlyList<long> childIds = GetChildIdsCached(center.id);
            if (pRequest.ShadowSnapshot != null)
                pRequest.SynchronousChildIds = childIds;
            root.hasChildren = childIds.Count > 0;
            yield return null;
            for (int childIndex = 0;
                 childIndex < childIds.Count; childIndex++)
            {
                long childId = childIds[childIndex];
                yield return null;
                FamilyTreeNode child = BuildTreeNodeData(childId);
                yield return null;
                if (child == null) continue;
                child.relation_label = child.sex == 0
                    ? AW_L10n.Text("aw_relation_son", "Son")
                    : AW_L10n.Text("aw_relation_daughter", "Daughter");
                IEnumerator childGeneration =
                    ApplyTreeGenerationIncrementally(child)
                        .GetEnumerator();
                while (childGeneration.MoveNext()) yield return null;
                (childGeneration as System.IDisposable)?.Dispose();
                IReadOnlyList<long> grandchildIds =
                    GetChildIdsCached(childId);
                bool hasChildren = grandchildIds.Count > 0;
                yield return null;
                root.children.Add(new TreeLayoutNode
                {
                    data = child,
                    expanded = false,
                    hasChildren = hasChildren
                });
                yield return null;
            }
        }

        private IEnumerable ExpandLiveBranchesIncrementally(
            FamilyTreeMaterializationRequest pRequest)
        {
            var frames = new Stack<ExpandLiveFrame>();
            var includedIds = new HashSet<long>
            {
                pRequest.BigTreeRootId
            };
            frames.Push(new ExpandLiveFrame
            {
                ActorId = pRequest.BigTreeRootId,
                Depth = 0
            });
            int visited = 1;
            yield return null;

            while (frames.Count > 0)
            {
                ExpandLiveFrame frame = frames.Peek();
                if (frame.Stage == 0)
                {
                    _foldDecided.Add(frame.ActorId);
                    frame.ActorData ??= BuildTreeNodeData(frame.ActorId);
                    frame.Stage = frame.ActorData == null ||
                                  frame.Depth > 64
                        ? 5
                        : 1;
                    yield return null;
                    continue;
                }

                if (frame.Stage == 1)
                {
                    frame.ChildIds = GetChildIdsCached(frame.ActorId);
                    frame.Stage = 2;
                    yield return null;
                    continue;
                }

                if (frame.Stage == 2)
                {
                    if (frame.ChildIndex >= frame.ChildIds.Count)
                    {
                        frame.Stage = 5;
                        continue;
                    }
                    frame.CandidateId =
                        frame.ChildIds[frame.ChildIndex++];
                    LineageBulkSnapshot snapshot =
                        LineageBulkSnapshotContext.Current;
                    frame.CandidateIsAgnatic =
                        ShouldIncludeBigTreeCandidate(snapshot,
                            frame.ActorId, frame.CandidateId) &&
                        includedIds.Add(frame.CandidateId);
                    frame.Stage = 3;
                    yield return null;
                    continue;
                }

                if (frame.Stage == 3)
                {
                    if (!frame.CandidateIsAgnatic)
                    {
                        frame.Stage = 2;
                        continue;
                    }
                    frame.CandidateData = BuildTreeNodeData(
                        frame.CandidateId);
                    frame.Stage = 4;
                    yield return null;
                    continue;
                }

                if (frame.Stage == 4)
                {
                    FamilyTreeNode child = frame.CandidateData;
                    frame.CandidateData = null;
                    frame.Stage = 2;
                    if (child != null)
                    {
                        frame.HasVisibleChildren = true;
                        if (visited < MAX_AUTO_EXPAND_VISITS &&
                            frame.Depth < 64)
                        {
                            visited++;
                            frames.Push(new ExpandLiveFrame
                            {
                                ActorId = child.id,
                                ActorData = child,
                                Depth = frame.Depth + 1,
                                Parent = frame
                            });
                        }
                        else if (child.is_alive)
                            frame.DescendantAlive = true;
                    }
                    yield return null;
                    continue;
                }

                if (frame.HasVisibleChildren &&
                    (frame.ActorId == pRequest.BigTreeRootId ||
                     frame.DescendantAlive))
                    _expanded.Add(frame.ActorId);
                else
                    _expanded.Remove(frame.ActorId);
                frames.Pop();
                if (frame.Parent != null)
                    frame.Parent.DescendantAlive |=
                        frame.ActorData?.is_alive == true ||
                        frame.DescendantAlive;
                yield return null;
            }
        }

        private IEnumerable BuildBigTreeIncrementally(
            FamilyTreeMaterializationRequest pRequest)
        {
            FamilyTreeNode rootData = BuildTreeNodeData(
                pRequest.BigTreeRootId);
            yield return null;
            if (rootData == null) yield break;
            if (pRequest.ShadowSnapshot != null)
            {
                pRequest.SynchronousParentIds =
                    GetParentIdsForMaterialization(
                        pRequest.BigTreeRootId,
                        pUseReverseLiveLookup: true);
                yield return null;
            }
            if (_lastTreeRootId != pRequest.BigTreeRootId)
            {
                ResetQueryCache();
                ResetFoldState();
                _lastTreeRootId = pRequest.BigTreeRootId;
            }
            if (!_foldDecided.Contains(pRequest.BigTreeRootId))
            {
                _foldDecided.Add(pRequest.BigTreeRootId);
                _expanded.Remove(pRequest.BigTreeRootId);
            }

            var root = new TreeLayoutNode { data = rootData };
            pRequest.Root = root;
            var frames = new Stack<BigTreeBuildFrame>();
            var includedIds = new HashSet<long>
            {
                pRequest.BigTreeRootId
            };
            frames.Push(CreateBigTreeBuildFrame(root, 0));
            yield return null;

            while (frames.Count > 0)
            {
                BigTreeBuildFrame frame = frames.Peek();
                if (frame.Stage == 0)
                {
                    frame.Node.data.tree_generation = frame.Depth + 1;
                    frame.Node.expanded = _expanded.Contains(
                        frame.Node.data.id);
                    frame.ChildIds = GetChildIdsCached(
                        frame.Node.data.id);
                    if (pRequest.ShadowSnapshot != null &&
                        frame.Node.data.id == pRequest.BigTreeRootId)
                        pRequest.SynchronousChildIds = frame.ChildIds;
                    frame.Stage = 1;
                    yield return null;
                    continue;
                }
                if (frame.Stage == 1)
                {
                    if (frame.ChildIndex >= frame.ChildIds.Count)
                    {
                        frame.Stage = 4;
                        continue;
                    }
                    frame.CandidateId =
                        frame.ChildIds[frame.ChildIndex++];
                    LineageBulkSnapshot snapshot =
                        LineageBulkSnapshotContext.Current;
                    frame.CandidateIsAgnatic =
                        ShouldIncludeBigTreeCandidate(snapshot,
                            frame.Node.data.id, frame.CandidateId) &&
                        includedIds.Add(frame.CandidateId);
                    frame.Stage = 2;
                    yield return null;
                    continue;
                }
                if (frame.Stage == 2)
                {
                    if (!frame.CandidateIsAgnatic)
                    {
                        frame.Stage = 1;
                        continue;
                    }
                    frame.CandidateData = BuildTreeNodeData(
                        frame.CandidateId);
                    frame.Stage = 3;
                    yield return null;
                    continue;
                }
                if (frame.Stage == 3)
                {
                    FamilyTreeNode childData = frame.CandidateData;
                    frame.CandidateData = null;
                    frame.Stage = 1;
                    if (childData != null)
                    {
                        frame.Node.hasChildren = true;
                        if (!_foldDecided.Contains(frame.Node.data.id))
                        {
                            _foldDecided.Add(frame.Node.data.id);
                            _expanded.Remove(frame.Node.data.id);
                            frame.Node.expanded = false;
                        }
                        if (frame.Node.expanded)
                        {
                            var childNode = new TreeLayoutNode
                            {
                                data = childData
                            };
                            frame.Node.children.Add(childNode);
                            frames.Push(CreateBigTreeBuildFrame(childNode,
                                frame.Depth + 1));
                        }
                        else
                            frame.Stage = 4;
                    }
                    yield return null;
                    continue;
                }

                frames.Pop();
                yield return null;
            }
        }

        private static BigTreeBuildFrame CreateBigTreeBuildFrame(
            TreeLayoutNode pNode, int pDepth)
        {
            return new BigTreeBuildFrame
            {
                Node = pNode,
                Depth = pDepth,
                ChildIds = new List<long>()
            };
        }

        private static bool ShouldIncludeBigTreeCandidate(
            LineageBulkSnapshot pSnapshot, long pParentId, long pChildId)
        {
            if (pSnapshot == null ||
                !pSnapshot.TryGetNode(pParentId,
                    out LineageTreeNodeSnapshot parent) ||
                !pSnapshot.TryGetNode(pChildId,
                    out LineageTreeNodeSnapshot child)) return false;
            if (IsUnresolvedLegacy(child.ArchiveResolution) &&
                pSnapshot.ParentIds(pChildId).Contains(pParentId)) return true;
            return FamilyTreeRelationRules.ShouldIncludeBigTreeEdge(
                pParentId, pSnapshot.FatherId(pChildId),
                pSnapshot.MotherId(pChildId), parent.Sex,
                parent.HasHeldTitle, child.Sex, child.Status,
                child.HasHeldTitle, pSnapshot.BigTreeProfile);
        }

        private static bool IsUnresolvedLegacy(string pResolution)
        {
            return string.Equals(pResolution,
                LineageFamilyArchiveMigration.UnresolvedLegacy,
                System.StringComparison.Ordinal);
        }

        private void SpawnNode(TreeLayoutNode pNode,
            FamilyTreeMaterializationRequest pRequest = null)
        {
            Mode mode = pRequest?.Mode ?? _mode;
            long rootActorId = pRequest?.BigTreeRootId ?? _rootActorId;
            long centerActorId = pRequest?.CenterActorId ?? _centerActorId;
            long backShiId = pRequest?.BackShiId ?? _backShiId;
            long locateActorId = pRequest?.LocateActorId ?? _locateActorId;
            FamilyTreeNodeView view = null;
            bool reuseView = mode == Mode.BigTree &&
                             pRequest?.ReuseRenderedViews == true &&
                             _bigTreeNodeViews.TryGetValue(pNode.data.id,
                                 out view) && view != null;
            if (!reuseView) view = AcquireNode();
            if (mode == Mode.BigTree && locateActorId >= 0 &&
                pNode.data.id == locateActorId)
            {
                pNode.data.relation_label = AW_L10n.Text("aw_tree_locate_target", "\u76EE\u6807");
                _locateFound = true;
                _locateTarget = new Vector2(pNode.centerX, pNode.topY);
            }

            bool isRoot = mode == Mode.Family &&
                          pNode.data.id == centerActorId;
            System.Action onUp = null, onDown = null;
            if (mode == Mode.Family && isRoot)
            {
                // 小树根节点:父母已在上方独立行画出并可点击上溯,这里不再重复 ▲(避免冗余);保留 ▼ 下溯。
                if (pNode.hasChildren)
                {
                    var kids = GetChildIdsCached(pNode.data.id);
                    if (kids.Count > 0)
                    {
                        long down = kids[0];
                        onDown = () => OpenFamilyTree(down, backShiId);
                    }
                }
            }

            System.Action toggle = mode == Mode.BigTree && pNode.hasChildren
                ? (System.Action)(() => ToggleExpand(pNode.data.id))
                : null;
            if (reuseView)
                view.UpdateExpansionState(pNode.hasChildren,
                    pNode.expanded, toggle);
            else
                view.Bind(pNode.data,
                    isRoot ? OnNodeClick : OnFamilyNodeClick,
                    toggle, pNode.hasChildren, pNode.expanded,
                    onUp, onDown);

            var rect = view.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(pNode.centerX, -pNode.topY);
            if (!reuseView) _spawned.Add(view);
            if (mode == Mode.BigTree)
                _bigTreeNodeViews[pNode.data.id] = view;
            if (pRequest?.ReuseRenderedViews == true)
                pRequest.RenderedActorIds.Add(pNode.data.id);
        }

        private IEnumerable RecycleUnusedBigTreeViewsIncrementally(
            FamilyTreeMaterializationRequest pRequest)
        {
            var staleIds = new List<long>();
            foreach (KeyValuePair<long, FamilyTreeNodeView> pair in
                     _bigTreeNodeViews)
            {
                if (!pRequest.RenderedActorIds.Contains(pair.Key))
                    staleIds.Add(pair.Key);
                yield return null;
            }

            for (int index = 0; index < staleIds.Count; index++)
            {
                long actorId = staleIds[index];
                if (_bigTreeNodeViews.TryGetValue(actorId,
                        out FamilyTreeNodeView view))
                {
                    _bigTreeNodeViews.Remove(actorId);
                    _spawned.Remove(view);
                    if (view != null)
                    {
                        view.gameObject.SetActive(false);
                        _nodePool.Add(view);
                    }
                }
                yield return null;
            }
        }

        private void DrawLine(float x1, float y1, float x2, float y2)
        {
            var obj = AcquireLine();
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            obj.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
            float cx = (x1 + x2) / 2f, cy = -(y1 + y2) / 2f;
            float len = Mathf.Max(1f, Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2)));
            bool horizontal = Mathf.Abs(x2 - x1) > Mathf.Abs(y2 - y1);
            rect.sizeDelta = horizontal ? new Vector2(len, 1.5f) : new Vector2(1.5f, len);
            rect.anchoredPosition = new Vector2(cx, cy);
            _lines.Add(obj);
        }

        private void ToggleExpand(long pId)
        {
            _locateActorId = -1;
            _intentState.CancelForManualFold();
            _foldDecided.Add(pId); // 用户手动操作 → 标记已决定,自动折叠规则不再覆盖
            if (_expanded.Contains(pId)) _expanded.Remove(pId);
            else _expanded.Add(pId);
            PreservePanForNextRebuild();
            QueueBigTreeRelayout();
        }

        private void ExpandAllLiveBranches()
        {
            _locateActorId = -1;
            if (_mode != Mode.BigTree || _rootActorId < 0) return;
            ResetQueryCache();
            ResetFoldState();
            PreservePanForNextRebuild();
            _intentState.RequestExpandLive();
            Rebuild();
        }

        private void CollapseAllBranches()
        {
            _locateActorId = -1;
            if (_mode != Mode.BigTree || _rootActorId < 0) return;
            _intentState.CancelForManualFold();
            ResetFoldState();
            _foldDecided.Add(_rootActorId);
            ResetQueryCache();
            PreservePanForNextRebuild();
            Rebuild();
        }

        // 点节点头像:大树→开该人小家庭树;小树根→打开 inspect。
        private void OnNodeClick(long pActorId)
        {
            if (_mode == Mode.BigTree)
            {
                OpenFamilyTree(pActorId, _backShiId);
                return;
            }
            // 小树:活人 inspect
            var actor = World.world?.units?.get(pActorId);
            SchoolActorNavigation.Open(actor);
        }

        // 小树非根节点:父母/子女/同辈节点点击都切换为目标作为新根,便于连续溯源。
        private void OnFamilyNodeClick(long pActorId)
        {
            OpenFamilyTree(pActorId, _backShiId);
        }

        private FamilyTreeNode BuildTreeNodeData(long pId)
        {
            FamilyTreeMaterializationRequest request =
                _activeMaterialization;
            LineageBulkSnapshot snapshot = request?.Snapshot ??
                LineageBulkSnapshotContext.Current ?? _bulkSnapshot;
            if (snapshot == null ||
                !snapshot.TryGetNode(pId,
                    out LineageTreeNodeSnapshot node)) return null;

            if (request != null && !request.VisitedNodeIds.Contains(pId))
            {
                if (!request.VisitBudget.TryVisit(pId)) return null;
                request.VisitedNodeIds.Add(pId);
            }

            ShiBranchDisplayProjection projection =
                ShiBranchRules.ResolveDisplayProjection(
                    node.ShiId, node.ParentShiId, node.ShiDisplay,
                    node.ParentShiDisplay, node.RootShiDisplay);
            string branchDisplay = node.FoundedBranchShiId >= 0L &&
                                   !string.IsNullOrWhiteSpace(
                                       node.BranchDisplay)
                ? node.BranchDisplay
                : projection.BranchDisplay;

            var result = new FamilyTreeNode
            {
                id = node.Id,
                display_name = string.Equals(node.ArchiveResolution,
                    LineageFamilyArchiveMigration.UnresolvedLegacy,
                    System.StringComparison.Ordinal)
                    ? AW_L10n.Text(
                        "aw_family_tree_unresolved_descendant",
                        "资料缺失的后代")
                    : node.DisplayName,
                asset_id = node.AssetId,
                archive_resolution = node.ArchiveResolution,
                sex = node.Sex,
                is_alive = node.IsAlive,
                status = node.Status,
                clan_name = node.ClanName,
                birth_time = node.BirthTime,
                death_time = node.DeathTime,
                kingdom_id = node.KingdomId,
                kingdom_name = node.KingdomName,
                kingdom_color = node.KingdomColor,
                original_clan_id = node.OriginalClanId,
                clan_color_text = node.ClanColorText,
                clan_color_id = node.ClanColorId,
                clan_banner_icon_id = node.ClanBannerIconId,
                clan_banner_background_id = node.ClanBannerBackgroundId,
                city_name = node.CityName,
                social_title = node.SocialTitle,
                social_title_color = node.SocialTitleColor,
                has_held_title = node.HasHeldTitle,
                ruling_shi = node.RulingShi,
                career_summary = node.CareerSummary,
                shi_id = node.ShiId,
                noble_distance = node.NobleDistance,
                head = node.Head,
                skin = node.Skin,
                skin_set = node.SkinSet,
                subspecies_id = node.SubspeciesId,
                age_overgrowth = node.AgeOvergrowth,
                phenotype_index = node.PhenotypeIndex,
                phenotype_shade = node.PhenotypeShade,
                founded_branch_shi_id = node.FoundedBranchShiId,
                death_cause = node.DeathCause,
                branch_home_display = "",
                branch_display = branchDisplay,
                parent_shi_display = projection.ParentDisplay,
                root_shi_display = projection.RootDisplay,
                origin_city_name = node.OriginCityName,
                state_name = node.StateName,
                ritual_appellation = node.RitualAppellation,
                retrospective_relation = node.RetrospectiveRelation
            };

            FamilyTreeSnapshotOverlayService.ReconcileReadModel(result,
                node);
            return result;
        }

        private void ResetQueryCache()
        {
            _childIdsCache = new Dictionary<long, IReadOnlyList<long>>();
        }

        private void ResetFoldState()
        {
            _expanded = new HashSet<long>();
            _foldDecided = new HashSet<long>();
        }

        private IReadOnlyList<long> GetChildIdsCached(long pActorId)
        {
            if (!_childIdsCache.TryGetValue(pActorId, out var ids))
            {
                LineageBulkSnapshot bulk =
                    LineageBulkSnapshotContext.Current;
                ids = bulk != null && bulk.ContainsNode(pActorId)
                    ? bulk.ChildIds(pActorId)
                    : System.Array.Empty<long>();
                _childIdsCache[pActorId] = ids;
            }
            return ids;
        }

        private static IReadOnlyList<long> GetParentIdsForMaterialization(
            long pActorId, bool pUseReverseLiveLookup)
        {
            LineageBulkSnapshot bulk = LineageBulkSnapshotContext.Current;
            if (bulk != null && bulk.ContainsNode(pActorId))
                return bulk.ParentIds(pActorId);
            return System.Array.Empty<long>();
        }

        private FamilyTreeNodeView AcquireNode()
        {
            if (_nodePool.Count > 0)
            {
                int index = _nodePool.Count - 1;
                var view = _nodePool[index];
                _nodePool.RemoveAt(index);
                if (view != null)
                {
                    view.transform.SetParent(_canvas, false);
                    view.gameObject.SetActive(true);
                    return view;
                }
            }
            return FamilyTreeNodeView.Create(_canvas);
        }

        private GameObject AcquireLine()
        {
            GameObject obj = null;
            if (_linePool.Count > 0)
            {
                int index = _linePool.Count - 1;
                obj = _linePool[index];
                _linePool.RemoveAt(index);
            }

            if (obj == null)
                obj = new GameObject("Line", typeof(RectTransform), typeof(Image));

            obj.transform.SetParent(_canvas, false);
            obj.transform.SetAsFirstSibling();
            obj.SetActive(true);
            return obj;
        }

        private void TransferOwnedCleanup()
        {
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            FamilyTreeDeferredCleanupHost.EnqueueOwned(
                _spawned, _lines, worldGeneration);
            FamilyTreeDeferredCleanupHost.EnqueueOwned(
                _nodePool, _linePool, worldGeneration);
            _spawned = new List<FamilyTreeNodeView>();
            _lines = new List<GameObject>();
            _nodePool = new List<FamilyTreeNodeView>();
            _linePool = new List<GameObject>();
            _bigTreeNodeViews.Clear();
            _cleanupPending = false;
            _cleanupLinesOnly = false;
            _maxDepthY = 0f;
        }

        private void PreservePanForNextRebuild()
        {
            Vector2 pan = _canvasRect != null
                ? _canvasRect.anchoredPosition
                : Vector2.zero;
            _intentState.RequestPan(pan.x, pan.y);
        }
    }

    internal static class FamilyTreeDeferredCleanupHost
    {
        private const int MaximumPerFrame = 8;
        private static readonly AWUiBoundedCleanupQueue<FamilyTreeNodeView>
            Nodes = new AWUiBoundedCleanupQueue<FamilyTreeNodeView>();
        private static readonly AWUiBoundedCleanupQueue<GameObject> Lines =
            new AWUiBoundedCleanupQueue<GameObject>();
        private static int _lastDrainFrame = -1;
        private static int _drainedThisFrame;

        public static void EnqueueOwned(List<FamilyTreeNodeView> nodes,
            List<GameObject> lines, long worldGeneration)
        {
            Nodes.EnqueueOwned(nodes, worldGeneration);
            Lines.EnqueueOwned(lines, worldGeneration);
        }

        public static void Drain(int maximumSteps)
        {
            int frame = Time.frameCount;
            if (_lastDrainFrame != frame)
            {
                _lastDrainFrame = frame;
                _drainedThisFrame = 0;
            }
            int remaining = System.Math.Min(
                System.Math.Max(0, maximumSteps),
                System.Math.Max(0, MaximumPerFrame - _drainedThisFrame));
            if (remaining == 0) return;
            int drained = Nodes.Drain(remaining, DestroyNode);
            remaining -= drained;
            if (remaining > 0)
                drained += Lines.Drain(remaining, DestroyLine);
            _drainedThisFrame += drained;
        }

        public static void InvalidateWorld(long currentWorldGeneration)
        {
            Nodes.InvalidateWorld(currentWorldGeneration);
            Lines.InvalidateWorld(currentWorldGeneration);
        }

        private static void DestroyNode(FamilyTreeNodeView node)
        {
            if (node != null) UnityEngine.Object.Destroy(node.gameObject);
        }

        private static void DestroyLine(GameObject line)
        {
            if (line != null) UnityEngine.Object.Destroy(line);
        }
    }
}
