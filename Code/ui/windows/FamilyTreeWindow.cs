using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     家族树(小树)/ 氏族大树(大树)。纵向居中多叉树。
    ///     - 大树:以氏支始祖为根整株,懒加载折叠(+/−);点节点 → 打开该人的**小家庭树**。
    ///     - 小树:以某人为中心(父母-本人-子女);点节点 → 打开该人 **inspect 单位窗**;
    ///       节点带上/下溯箭头(有父显▲跳到父、有子显▼跳到长子,重开以其为中心的小树)。
    ///     - 标题按模式区分:大树="氏族大树",小树="家族树"。
    /// </summary>
    internal class FamilyTreeWindow : AbstractWindow<FamilyTreeWindow>
    {
        private const int NODE_W = 70;
        private const int NODE_H = 64; // 与 FamilyTreeNodeView.NODE_H 一致(名字下移后增高)
        private const int H_GAP = 12;
        private const int V_GAP = 34;
        private const int PAD = 12;
        private const float VIEWPORT_W = 232f; // NML 滚动视口宽(用于居中)

        private enum Mode { Family, BigTree }
        private static Mode _mode;
        private static long _centerActorId = -1;
        private static long _rootActorId = -1;
        private static long _backShiId = -1;

        private readonly HashSet<long> _expanded = new HashSet<long>();
        private readonly HashSet<long> _foldDecided = new HashSet<long>(); // 已定过默认折叠状态的节点(防手动 toggle 后被自动规则覆盖)
        private readonly List<FamilyTreeNodeView> _spawned = new List<FamilyTreeNodeView>();
        private readonly List<GameObject> _lines = new List<GameObject>();
        private Transform _canvas;
        private RectTransform _canvasRect;
        private Button _backButton;
        private Text _backText;
        private Text _titleText;
        private float _maxDepthY;

        public static void OpenBigTree(long pShiId)
        {
            long founder = LineageQuery.GetShiBranchFounderId(pShiId);
            if (founder < 0) return;
            _mode = Mode.BigTree;
            _rootActorId = founder;
            _backShiId = pShiId;
            EnsureCreated();
            ShowOrRefresh(false);
        }

        public static void OpenFamilyTree(long pCenterActorId, long pShiIdForBackButton)
        {
            _mode = Mode.Family;
            _centerActorId = pCenterActorId;
            _backShiId = pShiIdForBackButton;
            EnsureCreated();
            ShowOrRefresh(false);
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

        private System.Collections.IEnumerator RebuildNextFrame()
        {
            yield return null; // 等一帧让 Content/Viewport layout 结算
            Rebuild();
        }

        private static void EnsureCreated()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.FAMILY_TREE);
        }

        protected override void Init()
        {
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

            // "回氏族大树"按钮(窗口底部居中,小树模式可见)
            var btnObj = new GameObject("BackToBigTree", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(BackgroundTransform, false);
            var brect = btnObj.GetComponent<RectTransform>();
            brect.anchorMin = new Vector2(0.5f, 0f);
            brect.anchorMax = new Vector2(0.5f, 0f);
            brect.pivot = new Vector2(0.5f, 0f);
            brect.sizeDelta = new Vector2(120, 18);
            brect.anchoredPosition = new Vector2(0, 12);
            var bg = btnObj.GetComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/button");
            bg.type = Image.Type.Sliced;
            _backButton = btnObj.GetComponent<Button>();
            _backButton.onClick.AddListener(OnBack);
            var txtObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtObj.transform.SetParent(btnObj.transform, false);
            var trect = txtObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero; trect.anchorMax = Vector2.one; trect.sizeDelta = Vector2.zero;
            _backText = txtObj.GetComponent<Text>();
            _backText.font = LocalizedTextManager.current_font;
            _backText.fontSize = 10;
            _backText.alignment = TextAnchor.MiddleCenter;
            _backText.color = Color.white;
            _backText.text = "← 回氏族大树";
        }

        public override void OnNormalEnable()
        {
            Rebuild();
            // 首帧 Content/Viewport layout 可能未结算导致居中错位 → 隔一帧再重排一次。
            // 此处 GameObject 已 active(OnEnable 才触发),StartCoroutine 安全(不会 inactive 报错)。
            if (isActiveAndEnabled) StartCoroutine(RebuildNextFrame());
        }

        private void OnBack()
        {
            if (_backShiId >= 0) OpenBigTree(_backShiId);
        }

        private void Rebuild()
        {
            ClearSpawned();
            _canvasRect.anchoredPosition = Vector2.zero; // 重建树时复位拖动平移到起点
            _backButton.gameObject.SetActive(_mode == Mode.Family && _backShiId >= 0);
            _titleText.text = _mode == Mode.BigTree ? "氏族大树" : "家族树";

            TreeLayoutNode root = (_mode == Mode.Family) ? BuildFamilyRoot() : BuildBigTreeRoot();
            if (root == null) return;

            MeasureWidth(root);
            float totalW = root.subtreeWidth;
            // 居中:树宽不足视口宽时整体右移居中。
            float startX = PAD + Mathf.Max(0f, (VIEWPORT_W - totalW) / 2f);

            // 小树:本人若有父母,先在顶部画父母行,本人树整体下移一层腾出空间。
            bool hasParents = _mode == Mode.Family && root.parents.Count > 0;
            float bodyTopY = PAD + (hasParents ? NODE_H + V_GAP : 0f);

            LayoutAndRender(root, startX, bodyTopY, totalW);

            if (hasParents) RenderParentsRow(root);

            float canvasW = Mathf.Max(VIEWPORT_W, totalW + PAD * 2);
            _canvasRect.sizeDelta = new Vector2(canvasW, _maxDepthY + NODE_H + PAD);
        }

        /// <summary>小树:在本人节点正上方画父母行(1~2 个),并连线到本人。点击父母 → 以其为中心重开小树(上溯)。</summary>
        private void RenderParentsRow(TreeLayoutNode pRoot)
        {
            int n = pRoot.parents.Count;
            float rowWidth = n * NODE_W + (n - 1) * H_GAP;
            float startX = pRoot.centerX - rowWidth / 2f;
            float parentY = PAD; // 顶部行

            for (int i = 0; i < n; i++)
            {
                FamilyTreeNode pData = pRoot.parents[i];
                float cx = startX + NODE_W / 2f + i * (NODE_W + H_GAP);

                var view = FamilyTreeNodeView.Create(_canvas);
                long pid = pData.id;
                // 父母节点:点击 → 以父母为中心重开小树(继续上溯);本身再带 ▲(若其还有父母)。
                System.Action onUp = LineageQuery.GetParentIds(pid).Count > 0
                    ? (System.Action)(() => OpenFamilyTree(pid, _backShiId)) : null;
                view.Bind(pData, (_) => OpenFamilyTree(pid, _backShiId),
                    null, false, false, onUp, null);
                var rect = view.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(cx, -parentY);
                _spawned.Add(view);

                // 连线:父母底 → 本人顶
                DrawConnector(cx, parentY + NODE_H, pRoot.centerX, pRoot.topY);
            }
        }

        private class TreeLayoutNode
        {
            public FamilyTreeNode data;
            public bool expanded;
            public bool hasChildren;
            public List<TreeLayoutNode> children = new List<TreeLayoutNode>();
            public float subtreeWidth;
            public float centerX;
            public float topY;
            // 小树根专用:本人的父母节点(画在本人正上方一层,可点击上溯)。
            public List<FamilyTreeNode> parents = new List<FamilyTreeNode>();
        }

        // 小树:本人为根(父母画在上方、子女为子树)。
        private TreeLayoutNode BuildFamilyRoot()
        {
            var center = LineageQuery.GetFamilyTree(_centerActorId);
            if (center == null) return null;
            var root = new TreeLayoutNode { data = center, expanded = true };

            // 父母:GetFamilyTree 已填 center.parents(死人查档案,活人实时),直接保留画在本人上方。
            //   用户报"往上查不到父母" → 不再只靠根节点 ▲ 跳转,直接在小树里把父母画出来。
            foreach (var p in center.parents)
                if (p != null) root.parents.Add(p);

            var childIds = LineageQuery.GetChildIds(center.id);
            root.hasChildren = childIds.Count > 0;
            foreach (var cid in childIds)
            {
                var cn = BuildTreeNodeData(cid);
                if (cn != null)
                    root.children.Add(new TreeLayoutNode
                    {
                        data = cn, expanded = false,
                        hasChildren = LineageQuery.GetChildIds(cid).Count > 0
                    });
            }
            return root;
        }

        private TreeLayoutNode BuildBigTreeRoot()
        {
            var rootData = BuildTreeNodeData(_rootActorId);
            if (rootData == null) return null;
            _expanded.Add(_rootActorId);            // 根始终展开(其余按自动折叠规则首次决定)
            return BuildLayoutNode(rootData, 0);
        }

        /// <summary>
        ///     懒加载构建:**折叠节点不查 SQL、不建子节点**(node.expanded=false 时直接返回,不递归)。
        ///     展开节点才查直接子代 + 递归建子节点;每个子节点首次出现时按"自动折叠规则"(ProbeBranch,只看一层)
        ///     决定其初始展开/折叠 —— 全死 OR 无 king/leader/heir 的分支默认折叠(用户定调,省性能)。
        /// </summary>
        private TreeLayoutNode BuildLayoutNode(FamilyTreeNode pData, int pDepth)
        {
            var node = new TreeLayoutNode { data = pData };

            // 称王分封:若该节点开了新氏支(founded_branch_shi_id>=0)且**不是当前这棵树的根**,
            //   则其子嗣只记在新支 → 原树里把他当叶子(不展开子代,改由"建支:X氏"徽标点击跳新支)。
            //   注:他自己作为新支根打开时(_rootActorId==他)正常展开子代。
            if (pData.founded_branch_shi_id >= 0 && pData.id != _rootActorId)
            {
                node.hasChildren = false;
                node.expanded = false;
                return node;
            }

            // 轻量探测(只看一层子代):决定 hasChildren(显 +/−)与默认折叠。不递归全树。
            var probe = LineageQuery.ProbeBranch(pData.id);
            node.hasChildren = probe.has_children;

            // 首次见到该节点 → 按规则定默认折叠状态(之后用户手动 toggle 进 _expanded/_collapsedDecided 不再被覆盖)。
            if (node.hasChildren && !_foldDecided.Contains(pData.id))
            {
                _foldDecided.Add(pData.id);
                bool autoExpand = probe.any_alive && probe.any_important; // 有活人 且 有重要人物 → 默认展开
                if (autoExpand) _expanded.Add(pData.id);
                else _expanded.Remove(pData.id);
            }

            node.expanded = _expanded.Contains(pData.id);

            // 仅展开时才查子代 + 建节点(折叠 = 零查询零节点,真懒加载)。
            if (node.expanded)
                foreach (var cid in LineageQuery.GetChildIds(pData.id))
                {
                    var cd = BuildTreeNodeData(cid);
                    if (cd == null) continue;
                    // 平民/奴隶不进氏族大树(用户定调:族谱仍记录,但大树不绘制 —— 只能在自己家庭树上溯找到老祖)。
                    if (IsCommonerStatus(cd.status)) continue;
                    node.children.Add(BuildLayoutNode(cd, pDepth + 1));
                }
            return node;
        }

        /// <summary>是否平民/奴隶身份(氏族大树跳过,不绘制)。</summary>
        private static bool IsCommonerStatus(string pStatus)
        {
            return pStatus == LineageStatus.COMMON || pStatus == LineageStatus.SLAVE;
        }

        private void MeasureWidth(TreeLayoutNode pNode)
        {
            if (pNode.children.Count == 0 || !pNode.expanded) { pNode.subtreeWidth = NODE_W; return; }
            float sum = 0;
            for (int i = 0; i < pNode.children.Count; i++)
            {
                MeasureWidth(pNode.children[i]);
                sum += pNode.children[i].subtreeWidth;
                if (i > 0) sum += H_GAP;
            }
            pNode.subtreeWidth = Mathf.Max(NODE_W, sum);
        }

        private void LayoutAndRender(TreeLayoutNode pNode, float pXStart, float pY, float pWidth)
        {
            pNode.centerX = pXStart + pWidth / 2f;
            pNode.topY = pY;
            if (pY + NODE_H > _maxDepthY) _maxDepthY = pY + NODE_H;

            SpawnNode(pNode);

            if (pNode.children.Count == 0 || !pNode.expanded) return;
            float childY = pY + NODE_H + V_GAP;
            float cursor = pXStart + (pWidth - SumChildWidths(pNode)) / 2f;
            foreach (var child in pNode.children)
            {
                LayoutAndRender(child, cursor, childY, child.subtreeWidth);
                DrawConnector(pNode.centerX, pY + NODE_H, child.centerX, childY);
                cursor += child.subtreeWidth + H_GAP;
            }
        }

        private float SumChildWidths(TreeLayoutNode pNode)
        {
            float sum = 0;
            for (int i = 0; i < pNode.children.Count; i++)
            {
                sum += pNode.children[i].subtreeWidth;
                if (i > 0) sum += H_GAP;
            }
            return sum;
        }

        private void SpawnNode(TreeLayoutNode pNode)
        {
            var view = FamilyTreeNodeView.Create(_canvas);

            bool isRoot = (_mode == Mode.Family) && pNode.data.id == _centerActorId;
            System.Action onUp = null, onDown = null;
            if (_mode == Mode.Family && isRoot)
            {
                // 小树根节点:父母已在上方独立行画出并可点击上溯,这里不再重复 ▲(避免冗余);保留 ▼ 下溯。
                if (pNode.hasChildren)
                {
                    var kids = LineageQuery.GetChildIds(pNode.data.id);
                    if (kids.Count > 0) { long down = kids[0]; onDown = () => OpenFamilyTree(down, _backShiId); }
                }
            }

            view.Bind(pNode.data, OnNodeClick,
                (_mode == Mode.BigTree && pNode.hasChildren) ? (System.Action)(() => ToggleExpand(pNode.data.id)) : null,
                pNode.hasChildren, pNode.expanded, onUp, onDown);

            var rect = view.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(pNode.centerX, -pNode.topY);
            _spawned.Add(view);
        }

        private void DrawConnector(float pParentCx, float pParentBottomY, float pChildCx, float pChildTopY)
        {
            float midY = (pParentBottomY + pChildTopY) / 2f;
            DrawLine(pParentCx, pParentBottomY, pParentCx, midY);
            DrawLine(pParentCx, midY, pChildCx, midY);
            DrawLine(pChildCx, midY, pChildCx, pChildTopY);
        }

        private void DrawLine(float x1, float y1, float x2, float y2)
        {
            var obj = new GameObject("Line", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(_canvas, false);
            obj.transform.SetAsFirstSibling();
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
            _foldDecided.Add(pId); // 用户手动操作 → 标记已决定,自动折叠规则不再覆盖
            if (_expanded.Contains(pId)) _expanded.Remove(pId);
            else _expanded.Add(pId);
            Rebuild();
        }

        // 点节点头像:大树→开该人小家庭树;小树→打开该人 inspect 单位窗(活人)。
        private void OnNodeClick(long pActorId)
        {
            if (_mode == Mode.BigTree)
            {
                OpenFamilyTree(pActorId, _backShiId);
                return;
            }
            // 小树:活人 inspect
            var actor = World.world?.units?.get(pActorId);
            if (actor != null && !actor.isRekt())
                ActionLibrary.openUnitWindow(actor);
        }

        private FamilyTreeNode BuildTreeNodeData(long pId)
        {
            var n = LineageQuery.GetFamilyTree(pId);
            if (n == null) return null;
            n.parents.Clear();
            n.children.Clear();
            return n;
        }

        private void ClearSpawned()
        {
            foreach (var v in _spawned) if (v != null) Destroy(v.gameObject);
            _spawned.Clear();
            foreach (var l in _lines) if (l != null) Destroy(l);
            _lines.Clear();
            _maxDepthY = 0;
        }
    }
}
