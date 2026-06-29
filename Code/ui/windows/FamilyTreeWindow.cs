using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     家族树 / 氏族大树(双模式)。
    ///     - 家族树:以某 actor 居中,显示父母+本人+子女三层。点节点→以该节点重新居中。
    ///     - 氏族大树:以氏支始祖为根,懒加载折叠下钻(默认展开 DEFAULT_DEPTH 层)。点节点→进该节点家族树。
    /// </summary>
    internal class FamilyTreeWindow : AbstractWindow<FamilyTreeWindow>
    {
        private const int DEFAULT_DEPTH = 2;   // 大树默认展开层数

        private enum Mode { Family, BigTree }
        private static Mode _mode;
        private static long _centerActorId = -1;
        private static long _rootActorId = -1;      // 大树根(始祖)
        private static long _backShiId = -1;        // 家族树模式下"回大树"用的氏支

        private readonly HashSet<long> _expanded = new HashSet<long>();
        private readonly List<FamilyTreeNodeView> _spawned = new List<FamilyTreeNodeView>();
        private Transform _treeRoot;
        private Button _backButton;
        private Text _backText;

        public static void OpenBigTree(long pShiId)
        {
            long founder = LineageQuery.GetShiBranchFounderId(pShiId);
            if (founder < 0) return;
            _mode = Mode.BigTree;
            _rootActorId = founder;
            _backShiId = pShiId;
            EnsureCreated();
            ScrollWindow.showWindow(AW_LineageWindowIds.FAMILY_TREE);
            if (Instance != null) Instance.Rebuild();
        }

        public static void OpenFamilyTree(long pCenterActorId, long pShiIdForBackButton)
        {
            _mode = Mode.Family;
            _centerActorId = pCenterActorId;
            _backShiId = pShiIdForBackButton;
            EnsureCreated();
            ScrollWindow.showWindow(AW_LineageWindowIds.FAMILY_TREE);
            if (Instance != null) Instance.Rebuild();
        }

        private static void EnsureCreated()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.FAMILY_TREE);
        }

        protected override void Init()
        {
            // "回氏族大树"按钮(家族树模式可见)
            var btnObj = new GameObject("BackToBigTree", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(BackgroundTransform, false);
            var brect = btnObj.GetComponent<RectTransform>();
            brect.anchorMin = new Vector2(0, 1);
            brect.anchorMax = new Vector2(0, 1);
            brect.sizeDelta = new Vector2(120, 18);
            brect.anchoredPosition = new Vector2(70, -14);
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

            _treeRoot = ContentTransform;
        }

        public override void OnNormalEnable()
        {
            Rebuild();
        }

        private void OnBack()
        {
            if (_backShiId >= 0) OpenBigTree(_backShiId);
        }

        private void Rebuild()
        {
            ClearSpawned();
            _backButton.gameObject.SetActive(_mode == Mode.Family && _backShiId >= 0);

            if (_mode == Mode.Family)
                BuildFamilyView();
            else
                BuildBigTreeView();
        }

        // ── 家族树:父母 / 本人 / 子女 三层 ──
        private void BuildFamilyView()
        {
            var center = LineageQuery.GetFamilyTree(_centerActorId);
            if (center == null) return;

            foreach (var p in center.parents)
                Spawn(p, "父母", 1);

            Spawn(center, "本人", 0);

            foreach (var c in center.children)
                Spawn(c, "子女", 2);
        }

        // ── 氏族大树:始祖为根,懒加载折叠 ──
        private void BuildBigTreeView()
        {
            var root = BuildTreeNode(_rootActorId);
            if (root == null) return;
            // 默认展开 DEFAULT_DEPTH 层
            PreExpand(_rootActorId, 0);
            RenderSubtree(root, 0, "始祖");
        }

        private void PreExpand(long pId, int pDepth)
        {
            if (pDepth >= DEFAULT_DEPTH) return;
            _expanded.Add(pId);
            foreach (var cid in LineageQuery.GetChildIds(pId))
                PreExpand(cid, pDepth + 1);
        }

        private void RenderSubtree(FamilyTreeNode pNode, int pIndent, string pRelation)
        {
            var children = LineageQuery.GetChildIds(pNode.id);
            bool hasChildren = children.Count > 0;
            bool expanded = _expanded.Contains(pNode.id);

            var view = FamilyTreeNodeView.Create(_treeRoot);
            view.Bind(pNode, pRelation, OnNodeClick,
                hasChildren ? (System.Action)(() => ToggleExpand(pNode.id)) : null,
                hasChildren, expanded);
            ApplyIndent(view, pIndent);
            _spawned.Add(view);

            if (!expanded) return;
            foreach (var cid in children)
            {
                var cn = BuildTreeNode(cid);
                if (cn != null) RenderSubtree(cn, pIndent + 1, "");
            }
        }

        private void ToggleExpand(long pId)
        {
            if (_expanded.Contains(pId)) _expanded.Remove(pId);
            else _expanded.Add(pId);
            Rebuild();
        }

        // 大树点节点→进该节点家族树(保留 back 到本氏支大树);家族树内点节点→重新居中
        private void OnNodeClick(long pActorId)
        {
            OpenFamilyTree(pActorId, _backShiId);
        }

        /// <summary>单节点 DTO(无父母/子女展开,只本体)。复用 GetFamilyTree 的本体部分。</summary>
        private FamilyTreeNode BuildTreeNode(long pId)
        {
            var n = LineageQuery.GetFamilyTree(pId);
            if (n == null) return null;
            // GetFamilyTree 会带 parents/children,大树渲染只用本体字段,清空避免混淆
            n.parents.Clear();
            n.children.Clear();
            return n;
        }

        private void ApplyIndent(FamilyTreeNodeView pView, int pIndent)
        {
            var le = pView.GetComponent<LayoutElement>();
            if (le != null) le.minWidth = 240 + pIndent * 14;
            var hl = pView.GetComponent<HorizontalLayoutGroup>();
            if (hl != null) hl.padding = new RectOffset(pIndent * 14, 0, 0, 0);
        }

        private void Spawn(FamilyTreeNode pNode, string pRelation, int pIndent)
        {
            var view = FamilyTreeNodeView.Create(_treeRoot);
            view.Bind(pNode, pRelation, OnNodeClick, null, false, false);
            ApplyIndent(view, pIndent);
            _spawned.Add(view);
        }

        private void ClearSpawned()
        {
            foreach (var v in _spawned)
                if (v != null) Destroy(v.gameObject);
            _spawned.Clear();
        }
    }
}
