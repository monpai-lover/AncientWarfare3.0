using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal enum PolicyPanelMode
    {
        Research,
        ClassState,
        Decision
    }

    internal class KingdomPolicyWindow : AbstractWindow<KingdomPolicyWindow>
    {
        private const float DEFAULT_WINDOW_W = 560f;
        private const float DEFAULT_WINDOW_H = 360f;
        private const float NODE_W = 112f;
        private const float NODE_H = 44f;
        private const float TREE_COL_W = 158f;
        private const float TREE_ROW_H = 72f;
        private const float TREE_MIN_W = 1120f;
        private const float TREE_SECTION_TOP = 36f;
        private const float TREE_SECTION_BOTTOM = 36f;
        private const float LINK_THICKNESS = 2.5f;
        private const float CONTENT_PAD_X = 14f;
        private const float CONTENT_PAD_BOTTOM = 22f;
        private const float SUMMARY_H = 34f;
        private const float PREPARATION_H = 24f;
        private const float SECTION_TITLE_H = 18f;
        private const float PROGRESS_H = 30f;
        private const float DECISION_SIDEBAR_W = 74f;
        private const float DECISION_SIDEBAR_GAP = 6f;
        private const float SCROLL_MARGIN_X = 42f;
        private const float SCROLL_MARGIN_Y = 58f;
        private const float CANVAS_TOP_GAP = 10f;

        private static long _kingdomId = -1;
        private static PolicyPanelMode _mode = PolicyPanelMode.Research;
        private static Sprite _lineSprite;
        private static Sprite _whiteSprite;
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly Dictionary<string, Vector2> _nodeCenters = new Dictionary<string, Vector2>();
        private Vector2 _windowSize = new Vector2(DEFAULT_WINDOW_W, DEFAULT_WINDOW_H);
        private float _contentWidth = DEFAULT_WINDOW_W - SCROLL_MARGIN_X;
        private float _viewportHeight = DEFAULT_WINDOW_H - SCROLL_MARGIN_Y;
        private Transform _canvas;
        private RectTransform _canvasRect;
        private GameObject _dragSurface;

        public static void Open(long pKingdomId)
        {
            OpenResearch(pKingdomId);
        }

        public static void OpenResearch(long pKingdomId)
        {
            _mode = PolicyPanelMode.Research;
            OpenInternal(pKingdomId);
        }

        public static void OpenClassState(long pKingdomId)
        {
            _mode = PolicyPanelMode.ClassState;
            OpenInternal(pKingdomId);
        }

        public static void OpenDecision(long pKingdomId)
        {
            _mode = PolicyPanelMode.Decision;
            OpenInternal(pKingdomId);
        }

        private static void OpenInternal(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.POLICY_TREE);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.POLICY_TREE,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
            ConfigureWindow();
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void ConfigureWindow()
        {
            ApplyWindowLayout();
            InstallDragAndResizeHandles();
            EnsureCanvas();
            EnsureCanvasPanZoom();
        }

        private void ApplyWindowLayout()
        {
            _contentWidth = Mathf.Max(1f, _windowSize.x - SCROLL_MARGIN_X);
            _viewportHeight = Mathf.Max(1f, _windowSize.y - SCROLL_MARGIN_Y);

            var bgRect = BackgroundTransform.GetComponent<RectTransform>();
            if (bgRect != null) bgRect.sizeDelta = _windowSize;

            Transform close = BackgroundTransform.parent != null ? BackgroundTransform.parent.Find("CloseBackground") : null;
            if (close != null) close.localPosition = new Vector3(_windowSize.x / 2f - 20f, _windowSize.y / 2f - 12f);

            Transform titleBg = BackgroundTransform.Find("TitleBackground");
            var titleRect = titleBg != null ? titleBg.GetComponent<RectTransform>() : null;
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.52f, 30f);
                titleBg.localPosition = new Vector3(0, _windowSize.y / 2f - 16f);
            }

            var sw = GetComponent<ScrollWindow>();
            if (sw?.titleText != null)
            {
                sw.titleText.transform.localPosition = new Vector3(0, _windowSize.y / 2f - 16f);
                sw.titleText.text = AW_L10n.Text("aw_policy_tree_title", "\u56FD\u7B56\u79D1\u6280");
                sw.titleText.raycastTarget = false;
                var titleTextRect = sw.titleText.GetComponent<RectTransform>();
                if (titleTextRect != null) titleTextRect.sizeDelta = new Vector2(_windowSize.x * 0.48f, 28f);
            }

            Transform scroll = BackgroundTransform.Find("Scroll View");
            var scrollRect = scroll != null ? scroll.GetComponent<RectTransform>() : null;
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(_contentWidth, _viewportHeight);
                scroll.localPosition = new Vector3(0, -20f, 0);
            }

            var scrollComponent = scroll != null ? scroll.GetComponent<ScrollRect>() : null;
            if (scrollComponent != null)
            {
                scrollComponent.vertical = false;
                scrollComponent.horizontal = false;
            }

            Transform viewport = BackgroundTransform.Find("Scroll View/Viewport");
            var viewRect = viewport != null ? viewport.GetComponent<RectTransform>() : null;
            if (viewRect != null) viewRect.sizeDelta = new Vector2(_contentWidth, _viewportHeight);
            EnsureViewportMask(viewport);
            // 可拖动的科技/国策/决策节点挂在 ContentTransform 下的 PolicyCanvas 上，
            // 直接给 ContentTransform 的父视口挂遮罩，才能把自由平移的节点裁剪在窗体框内。
            EnsureViewportMask(ContentTransform != null ? ContentTransform.parent : null);

            Transform scrollbar = BackgroundTransform.Find("Scroll View/Scrollbar Vertical");
            HideNativeScrollbarVisual(scrollbar);
            var scrollbarRect = scrollbar != null ? scrollbar.GetComponent<RectTransform>() : null;
            if (scrollbarRect != null)
            {
                scrollbarRect.anchorMin = new Vector2(1f, 0f);
                scrollbarRect.anchorMax = new Vector2(1f, 1f);
                scrollbarRect.pivot = new Vector2(1f, 0.5f);
                scrollbarRect.sizeDelta = new Vector2(1f, 0f);
                scrollbarRect.anchoredPosition = new Vector2(9999f, 0f);
            }

            var contentRect = ContentTransform != null ? ContentTransform.GetComponent<RectTransform>() : null;
            if (contentRect != null)
                contentRect.sizeDelta = new Vector2(_contentWidth, Mathf.Max(_viewportHeight + 1f, contentRect.sizeDelta.y));

            if (_canvasRect != null)
                _canvasRect.sizeDelta = new Vector2(_contentWidth, Mathf.Max(1f, _viewportHeight));
        }

        private static void HideNativeScrollbarVisual(Transform pScrollbar)
        {
            if (pScrollbar == null) return;

            pScrollbar.gameObject.SetActive(true);
            var scrollbar = pScrollbar.GetComponent<Scrollbar>();
            if (scrollbar != null) scrollbar.interactable = false;

            foreach (var graphic in pScrollbar.GetComponentsInChildren<Graphic>(true))
            {
                graphic.enabled = false;
                graphic.raycastTarget = false;
            }
        }

        private static void EnsureViewportMask(Transform pViewport)
        {
            if (pViewport == null) return;
            if (pViewport.GetComponent<RectMask2D>() == null)
                pViewport.gameObject.AddComponent<RectMask2D>();
        }

        private void EnsureCanvas()
        {
            if (_canvasRect != null || ContentTransform == null) return;

            Transform existing = ContentTransform.Find("PolicyCanvas");
            GameObject canvasObj = existing != null
                ? existing.gameObject
                : new GameObject("PolicyCanvas", typeof(RectTransform));
            if (existing == null) canvasObj.transform.SetParent(ContentTransform, false);

            _canvas = canvasObj.transform;
            _canvasRect = canvasObj.GetComponent<RectTransform>() ?? canvasObj.AddComponent<RectTransform>();
            _canvasRect.anchorMin = new Vector2(0f, 1f);
            _canvasRect.anchorMax = new Vector2(0f, 1f);
            _canvasRect.pivot = new Vector2(0f, 1f);
            _canvasRect.anchoredPosition = Vector2.zero;
            _canvasRect.localScale = Vector3.one;
            _canvas.SetAsFirstSibling();

            var pan = canvasObj.GetComponent<TreeDragPanHandler>() ?? canvasObj.AddComponent<TreeDragPanHandler>();
            pan.Setup(_canvasRect, null);
        }

        private void EnsureCanvasPanZoom()
        {
            if (_canvasRect == null || ContentTransform == null) return;
            Transform viewport = ContentTransform.parent;
            if (viewport == null) return;

            if (_dragSurface != null && _dragSurface.transform.parent == viewport)
                return;

            Transform existing = viewport.Find("PolicyDragSurface");
            _dragSurface = existing != null
                ? existing.gameObject
                : new GameObject("PolicyDragSurface", typeof(RectTransform), typeof(Image), typeof(TreeDragPanHandler));
            if (existing == null) _dragSurface.transform.SetParent(viewport, false);
            _dragSurface.transform.SetAsFirstSibling();

            var rect = _dragSurface.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var img = _dragSurface.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;

            var pan = _dragSurface.GetComponent<TreeDragPanHandler>();
            pan.Setup(_canvasRect, null);
        }

        private void InstallDragAndResizeHandles()
        {
            Transform titleBg = BackgroundTransform.Find("TitleBackground");
            RectTransform rootRect = BackgroundTransform.parent != null
                ? BackgroundTransform.parent.GetComponent<RectTransform>()
                : GetComponent<RectTransform>();
            if (titleBg != null && rootRect != null)
            {
                var img = titleBg.GetComponent<Image>();
                if (img != null) img.raycastTarget = true;
                var drag = titleBg.GetComponent<PolicyWindowDragHandler>() ??
                           titleBg.gameObject.AddComponent<PolicyWindowDragHandler>();
                drag.Setup(rootRect);
            }

        }

        private void Refresh()
        {
            ApplyWindowLayout();
            EnsureCanvas();
            EnsureCanvasPanZoom();
            ClearCreated();
            _nodeCenters.Clear();
            ResetCanvas();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom == null || kingdom.isRekt())
            {
                CreateText("Missing", AW_L10n.Text("aw_policy_no_kingdom", "\u738B\u56FD\u4E0D\u5B58\u5728"),
                    TopLeft(0, 30), new Vector2(_contentWidth, 24), TextAnchor.MiddleCenter, 12, Color.white);
                SetContentHeight(80f);
                return;
            }

            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(kingdom))
            {
                float disabledY = BuildPolicyDisabledPanel(kingdom, 16f);
                SetContentHeight(disabledY);
                return;
            }

            KingdomPolicyService.EnsureInitialized(kingdom);
            float y = BuildSummary(kingdom, 0f);
            y = BuildWarPreparationStatus(kingdom, y);
            if (_mode == PolicyPanelMode.ClassState)
            {
                y = BuildClassStateChooser(kingdom, y + 8f);
                SetPolicyChromeToFront();
                SetContentHeight(y);
                return;
            }

            if (_mode == PolicyPanelMode.Decision)
            {
                y = BuildDecisionChooser(kingdom, y + 6f);
                SetPolicyChromeToFront();
                SetContentHeight(y);
                return;
            }

            y = BuildProgressOverview(kingdom, y + 6f);
            float treeY = y + 8f;
            y = BuildSection(kingdom, AW_L10n.Text("aw_policy_tech_section", "\u79D1\u6280\u7814\u53D1"),
                KingdomPolicyDefs.Techs, treeY);
            CreateSectionSeparator(y + 2f);
            y = BuildSection(kingdom, AW_L10n.Text("aw_policy_social_section", "\u793E\u4F1A\u56FD\u7B56"),
                KingdomPolicyDefs.SocialPolicies, y + 18f);
            BuildPolicyLinks(kingdom);
            SetPolicyChromeToFront();
            SetContentHeight(y);
        }

        private void ResetCanvas()
        {
            if (_canvasRect == null) return;
            _canvasRect.anchoredPosition = Vector2.zero;
            _canvasRect.localScale = Vector3.one;
        }

        private float BuildPolicyDisabledPanel(Kingdom pKingdom, float pY)
        {
            float width = Mathf.Max(1f, _contentWidth - CONTENT_PAD_X * 2f);
            if (!KingdomPolicyService.CanUsePolicySystem(pKingdom))
            {
                CreateText("PolicyUnsupportedTitle", AW_L10n.Text("aw_policy_unsupported", "\u5F53\u524D\u7269\u79CD\u4E0D\u652F\u6301AW3\u56FD\u7B56"),
                    TopLeft(CONTENT_PAD_X, pY), new Vector2(width, 26f), TextAnchor.MiddleCenter, 12,
                    new Color(1f, 0.82f, 0.55f, 1f));
                CreateText("PolicyUnsupportedDesc", AW_L10n.Text("aw_policy_unsupported_desc", "\u76EE\u524DAW3\u56FD\u7B56\u53EA\u5BF9Xia\u9ED8\u8BA4\u542F\u7528\uFF0CHuman\u9700\u624B\u52A8\u5F00\u542F\u3002"),
                    TopLeft(CONTENT_PAD_X, pY + 32f), new Vector2(width, 42f), TextAnchor.UpperCenter, 10,
                    Color.white);
                return pY + 84f;
            }

            CreateText("PolicyDisabledTitle", AW_L10n.Text("aw_policy_disabled", "\u56FD\u7B56\u672A\u542F\u7528"),
                TopLeft(CONTENT_PAD_X, pY), new Vector2(width, 24f), TextAnchor.MiddleCenter, 12,
                new Color(1f, 0.82f, 0.55f, 1f));
            CreateText("PolicyDisabledDesc", AW_L10n.Text("aw_policy_disabled_desc", "\u8BE5\u56FD\u5C1A\u672A\u53C2\u4E0EAW3\u56FD\u7B56\uFF0C\u53EF\u4EE5\u624B\u52A8\u542F\u7528\u4EE5\u4FDD\u7559Human\u73A9\u5BB6\u9009\u62E9\u3002"),
                TopLeft(CONTENT_PAD_X, pY + 28f), new Vector2(width, 38f), TextAnchor.UpperCenter, 10,
                Color.white);

            float gap = 10f;
            float buttonW = Mathf.Min(180f, (width - gap) * 0.5f);
            float x = CONTENT_PAD_X + (width - buttonW * 2f - gap) * 0.5f;
            float buttonY = pY + 76f;
            var manual = CreateButtonBox("EnablePolicyManual", AW_L10n.Text("aw_policy_enable_manual", "\u542F\u7528\u56FD\u7B56"),
                TopLeft(x, buttonY), new Vector2(buttonW, SUMMARY_H), Color.white, () =>
                {
                    KingdomPolicyService.SetPolicyEnabled(pKingdom, true);
                    KingdomPolicyService.SetPolicyAIEnabled(pKingdom, false);
                    Refresh();
                });
            SetTip(manual, AW_L10n.Text("aw_policy_enable_manual", "\u542F\u7528\u56FD\u7B56"),
                AW_L10n.Text("aw_policy_enable_manual_desc", "\u542F\u7528\u56FD\u7B56\u4F46\u4E0D\u8BA9AI\u81EA\u52A8\u9009\u62E9\u7814\u53D1\u3002"));

            var auto = CreateButtonBox("EnablePolicyAI", AW_L10n.Text("aw_policy_enable_ai", "\u542F\u7528\u81EA\u52A8"),
                TopLeft(x + buttonW + gap, buttonY), new Vector2(buttonW, SUMMARY_H),
                new Color(0.78f, 1f, 0.72f, 1f), () =>
                {
                    KingdomPolicyService.SetPolicyEnabled(pKingdom, true);
                    KingdomPolicyService.SetPolicyAIEnabled(pKingdom, true);
                    Refresh();
                });
            SetTip(auto, AW_L10n.Text("aw_policy_enable_ai", "\u542F\u7528\u81EA\u52A8"),
                AW_L10n.Text("aw_policy_enable_ai_desc", "\u542F\u7528\u56FD\u7B56\u5E76\u5141\u8BB8AI\u53EA\u5728\u7A7A\u69FD\u65F6\u81EA\u52A8\u9009\u62E9\u3002"));
            return buttonY + SUMMARY_H + 16f;
        }

        private float BuildSummary(Kingdom pKingdom, float pY)
        {
            string classId = KingdomPolicyService.GetClassId(pKingdom);
            string classText = AW_L10n.Text(KingdomPolicyService.GetClassLocaleKey(classId),
                ClassFallbackName(classId));
            string points = AW_L10n.Text("aw_policy_points_short", "\u653F") + ":" +
                            Mathf.FloorToInt(KingdomPolicyService.GetPoliticalPoints(pKingdom)) + "  " +
                            AW_L10n.Text("aw_tech_points_short", "\u6280") + ":" +
                            Mathf.FloorToInt(KingdomPolicyService.GetTechPoints(pKingdom));

            float width = Mathf.Max(1f, _contentWidth - CONTENT_PAD_X * 2f);
            float gap = 8f;
            float classW = 112f;
            float decisionW = 96f;
            float aiW = 64f;
            float currentW = Mathf.Max(150f, width - classW - decisionW - aiW - gap * 3f);
            float x = CONTENT_PAD_X;

            var classBox = CreateButtonBox("ClassState", classText, TopLeft(x, pY), new Vector2(classW, SUMMARY_H),
                new Color(0.8f, 0.72f, 0.52f, 1f), () =>
                {
                    _mode = PolicyPanelMode.ClassState;
                    Refresh();
                });
            SetTip(classBox, AW_L10n.Text("aw_policy_class_state", "\u653F\u6CBB\u72B6\u6001"),
                ClassDesc(classId) + "\n" + points);

            string current = BuildCurrentSummary(pKingdom);
            x += classW + gap;
            var currentBox = CreateButtonBox("CurrentResearch", current, TopLeft(x, pY), new Vector2(currentW, SUMMARY_H),
                Color.white, () =>
                {
                    _mode = PolicyPanelMode.Research;
                    Refresh();
                });
            SetTip(currentBox, AW_L10n.Text("aw_policy_current_research", "\u5F53\u524D\u7814\u53D1"),
                current + "\n" + points);

            string decision = BuildDecisionSummary(pKingdom);
            x += currentW + gap;
            var decisionBox = CreateButtonBox("CurrentDecision", decision, TopLeft(x, pY), new Vector2(decisionW, SUMMARY_H),
                Color.white, () =>
                {
                    _mode = PolicyPanelMode.Decision;
                    Refresh();
                });
            SetTip(decisionBox, AW_L10n.Text("aw_policy_decisions", "\u5E38\u6001\u51B3\u7B56"),
                decision + "\n" + points);
            x += decisionW + gap;
            bool aiEnabled = KingdomPolicyService.IsPolicyAIEnabled(pKingdom);
            string aiText = aiEnabled
                ? AW_L10n.Text("aw_policy_ai_on", "AI\u5F00")
                : AW_L10n.Text("aw_policy_ai_off", "AI\u5173");
            var aiBox = CreateButtonBox("PolicyAI", aiText, TopLeft(x, pY), new Vector2(aiW, SUMMARY_H),
                aiEnabled ? new Color(0.78f, 1f, 0.72f, 1f) : Color.white, () =>
                {
                    KingdomPolicyService.SetPolicyAIEnabled(pKingdom, !KingdomPolicyService.IsPolicyAIEnabled(pKingdom));
                    Refresh();
                });
            SetTip(aiBox, AW_L10n.Text("aw_policy_ai_toggle", "\u81EA\u52A8\u7814\u53D1"),
                aiEnabled
                    ? AW_L10n.Text("aw_policy_ai_on_desc", "AI\u4F1A\u5728\u7A7A\u69FD\u65F6\u81EA\u52A8\u9009\u62E9\u79D1\u6280\u3001\u56FD\u7B56\u548C\u51B3\u7B56\u3002")
                    : AW_L10n.Text("aw_policy_ai_off_desc", "AI\u4E0D\u4F1A\u586B\u5145\u7A7A\u69FD\uFF0C\u53EA\u4FDD\u7559\u73A9\u5BB6\u624B\u52A8\u9009\u62E9\u3002"));
            return pY + SUMMARY_H;
        }

        private float BuildWarPreparationStatus(Kingdom pKingdom, float pY)
        {
            if (!WarNoticeService.TryGetPreparationSummary(
                    pKingdom, out WarPreparationSummary summary)) return pY;

            float y = pY + 6f;
            float width = Mathf.Max(1f, _contentWidth - CONTENT_PAD_X * 2f);
            var obj = new GameObject("WarPreparationStatus", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(ContentTransform, false);
            _created.Add(obj);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, PREPARATION_H);
            rect.anchoredPosition = TopLeft(CONTENT_PAD_X, y);

            var image = obj.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(image, 0.96f);
            image.color = summary.DeploymentReady
                ? new Color(0.18f, 0.34f, 0.23f, 0.96f)
                : new Color(0.38f, 0.27f, 0.13f, 0.96f);
            var button = obj.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var tip = obj.GetComponent<TipButton>();
            tip.showOnClick = false;

            Text text = CreateTextObject("Text", obj.transform,
                BuildWarPreparationLine(summary), new Vector2(7f, 1f),
                new Vector2(-7f, -1f), TextAnchor.MiddleLeft, 9, Color.white);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
            SetTip(obj, AW_L10n.Text("aw_war_preparation", "\u6218\u5907"),
                BuildWarPreparationTooltip(summary));
            return y + PREPARATION_H;
        }

        private static string BuildWarPreparationLine(WarPreparationSummary pSummary)
        {
            string deployment = AW_L10n.Text(
                pSummary.DeploymentReady
                    ? "aw_war_deployment_ready"
                    : "aw_war_deployment_preparing",
                pSummary.DeploymentReady ? "\u8FB9\u9632\u5C31\u7EEA" : "\u8FB9\u9632\u96C6\u7ED3\u4E2D");
            return AW_L10n.Text("aw_war_preparation", "\u6218\u5907") + "  " +
                   AW_L10n.Text("aw_war_notice_target", "\u6218\u4E66\u76EE\u6807") + ": " +
                   WarPreparationTargetName(pSummary.TargetKingdomId) + "  " +
                   AW_L10n.Text("aw_war_notice_window", "\u5F00\u6218\u671F\u9650") + ": " +
                   pSummary.EarliestWarYear + "-" + pSummary.ForcedWarYear + "  " +
                   AW_L10n.Text("aw_war_levy_count", "\u5F81\u53EC\u5175") + ": " +
                   pSummary.LevyCount + "  " + deployment;
        }

        private static string BuildWarPreparationTooltip(WarPreparationSummary pSummary)
        {
            string deployment = AW_L10n.Text(
                pSummary.DeploymentReady
                    ? "aw_war_deployment_ready"
                    : "aw_war_deployment_preparing",
                pSummary.DeploymentReady ? "\u8FB9\u9632\u5C31\u7EEA" : "\u8FB9\u9632\u96C6\u7ED3\u4E2D");
            return AW_L10n.Text("aw_war_notice_target", "\u6218\u4E66\u76EE\u6807") + ": " +
                   WarPreparationTargetName(pSummary.TargetKingdomId) + "\n" +
                   AW_L10n.Text("aw_war_notice_year", "\u4E0B\u4E66\u5E74\u4EFD") + ": " +
                   pSummary.NoticeYear + "\n" +
                   AW_L10n.Text("aw_war_notice_window", "\u5F00\u6218\u671F\u9650") + ": " +
                   pSummary.EarliestWarYear + "-" + pSummary.ForcedWarYear + "\n" +
                   AW_L10n.Text("aw_war_levy_count", "\u5F81\u53EC\u5175") + ": " +
                   pSummary.LevyCount + "\n" + deployment;
        }

        private static string WarPreparationTargetName(long pKingdomId)
        {
            try
            {
                Kingdom target = pKingdomId >= 0 ? World.world?.kingdoms?.get(pKingdomId) : null;
                return target?.data == null || string.IsNullOrEmpty(target.name) ? "?" : target.name;
            }
            catch { return "?"; }
        }

        private float BuildProgressOverview(Kingdom pKingdom, float pY)
        {
            KingdomPolicyDef current = GetDisplayedCurrent(pKingdom);
            PolicyNodeKind kind = current?.Kind ?? PolicyNodeKind.Tech;
            float width = Mathf.Max(260f, _contentWidth - CONTENT_PAD_X * 2f);
            CreateProgressSlot("CurrentExecutionSlot", pKingdom, current, kind,
                TopLeft(CONTENT_PAD_X, pY), new Vector2(width, PROGRESS_H));
            return pY + PROGRESS_H;
        }

        private void SetPolicyChromeToFront()
        {
            if (ContentTransform == null) return;
            foreach (string name in new[]
                     {
                         "ClassState",
                         "CurrentResearch",
                         "CurrentDecision",
                         "PolicyAI",
                         "WarPreparationStatus",
                         "CurrentExecutionSlot",
                         "DecisionResearchProgress"
                     })
            {
                Transform child = ContentTransform.Find(name);
                if (child != null) child.SetAsLastSibling();
            }
        }

        private void CreateProgressSlot(string pName, Kingdom pKingdom, KingdomPolicyDef pDef, PolicyNodeKind pKind,
            Vector2 pPos, Vector2 pSize)
        {
            var slot = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            slot.transform.SetParent(ContentTransform, false);
            _created.Add(slot);

            var rect = slot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPos;

            AW_UIStyle.ApplyPanel(slot.GetComponent<Image>(), 0.94f);
            var button = slot.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                _mode = pKind == PolicyNodeKind.Decision ? PolicyPanelMode.Decision : PolicyPanelMode.Research;
                Refresh();
            });

            CreateIconObject("Icon", slot.transform, ProgressIcon(pDef, pKind),
                new Vector2(6f, -6f), new Vector2(18f, 18f));

            Text label = CreateTextObject("Text", slot.transform, ProgressLineText(pKingdom, pDef, pKind),
                new Vector2(30f, 8f), new Vector2(-8f, -8f), TextAnchor.UpperLeft, 9, Color.white);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 6;
            label.resizeTextMaxSize = 9;

            float fraction = Mathf.Clamp01(ProgressFraction(pKingdom, pDef));
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(slot.transform, false);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0f);
            trackRect.anchorMax = new Vector2(1f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.offsetMin = new Vector2(30f, 4f);
            trackRect.offsetMax = new Vector2(-8f, 10f);
            var trackImg = track.GetComponent<Image>();
            trackImg.sprite = WhiteSprite();
            trackImg.color = new Color(0f, 0f, 0f, 0.42f);
            trackImg.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(fraction, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = WhiteSprite();
            fillImg.color = ProgressBarColor(pKind);
            fillImg.raycastTarget = false;

            SetTip(slot, ProgressTitle(pDef, pKind), BuildAllProgressTooltip(pKingdom, pDef, pKind));
        }

        private static float ProgressFraction(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            return pDef == null ? 0f : KingdomPolicyService.GetProgressFraction(pKingdom, pDef);
        }

        private static Sprite ProgressIcon(KingdomPolicyDef pDef, PolicyNodeKind pKind)
        {
            Sprite sprite = pDef == null ? null : SpriteTextureLoader.getSprite(pDef.IconPath);
            if (sprite != null) return sprite;

            if (pKind == PolicyNodeKind.Tech)
                return SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
            if (pKind == PolicyNodeKind.Decision)
                return SpriteTextureLoader.getSprite("ui/icons/iconPlotsList")
                       ?? SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                       ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
            return SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                   ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge");
        }

        private static Color ProgressBarColor(PolicyNodeKind pKind)
        {
            if (pKind == PolicyNodeKind.Tech) return new Color(0.28f, 0.58f, 1f, 0.9f);
            if (pKind == PolicyNodeKind.Decision) return new Color(0.58f, 0.86f, 0.36f, 0.9f);
            return new Color(1f, 0.72f, 0.26f, 0.9f);
        }

        private static string ProgressLineText(Kingdom pKingdom, KingdomPolicyDef pDef, PolicyNodeKind pKind)
        {
            string prefix = pKind == PolicyNodeKind.Tech
                ? AW_L10n.Text("aw_tech_points_short", "\u6280")
                : pKind == PolicyNodeKind.Decision
                    ? AW_L10n.Text("aw_decision_points_short", "\u4EE4")
                    : AW_L10n.Text("aw_policy_points_short", "\u653F");
            if (pDef == null)
                return prefix + ": " + NoCurrentText(pKind);

            float progress = KingdomPolicyService.GetProgress(pKingdom, pKind);
            string line = prefix + ": " + PolicyName(pDef) + " " + Mathf.FloorToInt(progress) + "/" +
                   Mathf.CeilToInt(pDef.Cost) + " " +
                   Mathf.FloorToInt(KingdomPolicyService.GetProgressFraction(pKingdom, pDef) * 100f) + "%";
            if (pKind == PolicyNodeKind.Decision)
            {
                string target = KingdomPolicyService.BuildDecisionTargetLine(pKingdom);
                if (!string.IsNullOrEmpty(target)) line += "  " + target;
            }

            return line;
        }

        private static string ProgressTitle(KingdomPolicyDef pDef, PolicyNodeKind pKind)
        {
            string title = pKind == PolicyNodeKind.Tech
                ? AW_L10n.Text("aw_policy_tech_section", "\u79D1\u6280\u7814\u53D1")
                : pKind == PolicyNodeKind.Decision
                    ? AW_L10n.Text("aw_policy_decisions", "\u5E38\u6001\u51B3\u7B56")
                    : AW_L10n.Text("aw_policy_social_section", "\u793E\u4F1A\u56FD\u7B56");
            return pDef == null ? title : title + ": " + PolicyName(pDef);
        }

        private static KingdomPolicyDef GetDisplayedCurrent(Kingdom pKingdom)
        {
            KingdomPolicyDef decision = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision));
            KingdomPolicyDef tech = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Tech));
            KingdomPolicyDef social = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Social));

            if (_mode == PolicyPanelMode.Decision) return decision;
            if (tech != null) return tech;
            if (social != null) return social;
            return decision;
        }

        private static string BuildProgressTooltip(Kingdom pKingdom, KingdomPolicyDef pDef, PolicyNodeKind pKind)
        {
            if (pDef == null)
                return NoCurrentText(pKind);

            float progress = KingdomPolicyService.GetProgress(pKingdom, pKind);
            float remaining = Mathf.Max(0f, pDef.Cost - progress);
            float banked = pKind == PolicyNodeKind.Tech
                ? KingdomPolicyService.GetTechPoints(pKingdom)
                : KingdomPolicyService.GetPoliticalPoints(pKingdom);
            float yearlyGain = pKind == PolicyNodeKind.Tech
                ? KingdomPolicyService.GetTechPointGain(pKingdom)
                : KingdomPolicyService.GetPoliticalPointGain(pKingdom);
            int years = EstimateYearsRemaining(remaining, banked, yearlyGain);

            var lines = new List<string>
            {
                PolicyDesc(pDef),
                AW_L10n.Text("aw_policy_progress", "\u8FDB\u5EA6") + ": " + Mathf.FloorToInt(progress) + "/" + Mathf.CeilToInt(pDef.Cost),
                AW_L10n.Text("aw_policy_remaining", "\u5269\u4F59") + ": " + Mathf.CeilToInt(remaining),
                AW_L10n.Text("aw_policy_status", "\u72B6\u6001") + ": " + StatusText(KingdomPolicyService.GetStatus(pKingdom, pDef)),
                AW_L10n.Text("aw_policy_yearly_gain", "\u5E74\u589E\u957F") + ": " + yearlyGain.ToString("0.0"),
                AW_L10n.Text("aw_policy_estimated_years", "\u9884\u8BA1\u5E74\u6570") + ": " + years
            };
            if (pKind == PolicyNodeKind.Decision)
            {
                string target = KingdomPolicyService.BuildDecisionTargetLine(pKingdom);
                if (!string.IsNullOrEmpty(target)) lines.Insert(1, target);
            }
            return string.Join("\n", lines.ToArray());
        }

        private static string BuildCoreFabricationSidebarTooltip(Kingdom pKingdom)
        {
            var lines = new List<string>
            {
                AW_L10n.Text("aw_core_fabrication_queue_desc",
                    "\u5236\u9020\u6838\u5FC3\u4F7F\u7528\u72EC\u7ACB\u961F\u5217\uFF0C\u4E0D\u5360\u7528\u5E38\u6001\u51B3\u7B56\u69FD\u3002")
            };

            if (KingdomPolicyService.IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId))
            {
                lines.Add(AW_L10n.Text("aw_policy_core_fabrication_locked", "\u5236\u9020\u6838\u5FC3\u5DF2\u9501\u5B9A"));
                return string.Join("\n", lines.ToArray());
            }

            long currentCityId = KingdomPolicyService.GetCoreFabricationCityId(pKingdom);
            if (currentCityId >= 0)
            {
                string cityName = KingdomPolicyService.GetCoreFabricationCityName(pKingdom);
                lines.Add(AW_L10n.Text("aw_core_fabrication_current", "\u5F53\u524D") + ": " +
                          (string.IsNullOrEmpty(cityName) ? "?" : cityName) + " " +
                          Mathf.FloorToInt(KingdomPolicyService.GetCoreFabricationProgress(pKingdom)) + "/" +
                          Mathf.CeilToInt(KingdomPolicyService.GetCoreFabricationCost()));
            }
            else
            {
                lines.Add(AW_L10n.Text("aw_core_fabrication_current", "\u5F53\u524D") + ": " +
                          AW_L10n.Text("aw_policy_idle", "\u5F85\u5B9A"));
            }

            lines.Add(AW_L10n.Text("aw_core_fabrication_project_count", "\u961F\u5217\u9879\u76EE") + ": " +
                      KingdomPolicyService.CountCoreFabricationProjects(pKingdom));

            City next = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
            if (next?.data != null)
                lines.Add(AW_L10n.Text("aw_core_fabrication_click_next", "\u70B9\u51FB\u52A0\u5165") + ": " +
                          next.data.name);
            else
                lines.Add(AW_L10n.Text("aw_core_fabrication_no_target", "\u6CA1\u6709\u53EF\u5236\u9020\u6838\u5FC3\u7684\u57CE\u5E02"));

            return string.Join("\n", lines.ToArray());
        }

        private static string BuildAllProgressTooltip(Kingdom pKingdom, KingdomPolicyDef pDef, PolicyNodeKind pKind)
        {
            var lines = new List<string>();
            if (pDef != null)
                lines.Add(BuildProgressTooltip(pKingdom, pDef, pKind));
            else
                lines.Add(NoCurrentText(pKind));

            lines.Add("");
            lines.Add(ProgressLineText(pKingdom,
                KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Tech)), PolicyNodeKind.Tech));
            lines.Add(ProgressLineText(pKingdom,
                KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Social)), PolicyNodeKind.Social));
            lines.Add(ProgressLineText(pKingdom,
                KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision)), PolicyNodeKind.Decision));
            return string.Join("\n", lines.ToArray());
        }

        private static int EstimateYearsRemaining(float pRemaining, float pBanked, float pYearlyGain)
        {
            if (pRemaining <= 0f) return 0;
            float remaining = pRemaining;
            float stock = Mathf.Max(0f, pBanked);
            float gain = Mathf.Max(0f, pYearlyGain);
            for (int year = 1; year <= 99; year++)
            {
                stock += gain;
                float spend = Mathf.Min(stock, Mathf.Min(KingdomPolicyService.MAX_YEARLY_SPEND, remaining));
                if (spend <= 0.001f) return 99;
                stock -= spend;
                remaining -= spend;
                if (remaining <= 0.001f) return year;
            }
            return 99;
        }

        private static string NoCurrentText(PolicyNodeKind pKind)
        {
            return pKind == PolicyNodeKind.Decision
                ? AW_L10n.Text("aw_policy_no_current_decision", "\u5F53\u524D\u6CA1\u6709\u51B3\u7B56")
                : AW_L10n.Text("aw_policy_no_current_research", "\u5F53\u524D\u6CA1\u6709\u7814\u53D1");
        }

        private float BuildClassStateChooser(Kingdom pKingdom, float pY)
        {
            CreateText("Section_ClassState", AW_L10n.Text("aw_policy_class_state", "\u653F\u6CBB\u72B6\u6001"),
                TopLeft(CONTENT_PAD_X, pY), new Vector2(160, SECTION_TITLE_H), TextAnchor.MiddleLeft, 11,
                new Color(1f, 0.92f, 0.6f, 1f));

            string current = KingdomPolicyService.GetClassId(pKingdom);
            float cardW = 136f;
            float cardH = 36f;
            float gapX = 12f;
            float gapY = 12f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((_contentWidth - CONTENT_PAD_X * 2f + gapX) / (cardW + gapX)));
            columns = Mathf.Min(4, columns);
            for (int i = 0; i < KingdomPolicyDefs.ClassStates.Length; i++)
            {
                string classId = KingdomPolicyDefs.ClassStates[i];
                bool active = classId == current;
                Vector2 pos = TopLeft(CONTENT_PAD_X + 4f + (i % columns) * (cardW + gapX),
                    pY + 28f + (i / columns) * (cardH + gapY));
                var box = CreateButtonBox("Class_" + classId, ClassName(classId), pos, new Vector2(cardW, cardH),
                    active ? pKingdom.getColor().getColorText() : Color.white,
                    () =>
                    {
                        KingdomPolicyService.ForceSetClassState(pKingdom, classId);
                        Refresh();
                    });

                Image img = box.GetComponent<Image>();
                if (img != null)
                    img.color = active ? new Color(0.65f, 0.52f, 0.22f, 0.95f) : Color.white;
                SetTip(box, ClassName(classId),
                    ClassDesc(classId) + "\n" +
                    AW_L10n.Text("aw_policy_class_manual_desc", "\u624B\u52A8\u5207\u6362\u5C5E\u4E8E\u4E0A\u5E1D\u7C7B\u64CD\u4F5C\uFF1BAI\u53EA\u4F1A\u5728\u5B8C\u6210\u5BF9\u5E94\u79D1\u6280\u548C\u56FD\u7B56\u540E\u81EA\u52A8\u6539\u53D8\u653F\u4F53\u3002"));
            }

            int rows = Mathf.CeilToInt(KingdomPolicyDefs.ClassStates.Length / (float)columns);
            return pY + 28f + rows * (cardH + gapY) + CONTENT_PAD_BOTTOM;
        }

        private float BuildDecisionChooser(Kingdom pKingdom, float pY)
        {
            CreateText("Section_DecisionProgress", AW_L10n.Text("aw_policy_decisions", "\u5E38\u6001\u51B3\u7B56"),
                TopLeft(CONTENT_PAD_X, pY), new Vector2(160, SECTION_TITLE_H), TextAnchor.MiddleLeft, 11,
                new Color(1f, 0.92f, 0.6f, 1f));

            KingdomPolicyDef decision = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision));
            float barX = CONTENT_PAD_X;
            float fullW = Mathf.Max(260f, _contentWidth - CONTENT_PAD_X * 2f);
            bool showCoreSidebar = CoreFabricationSlotRules.ShouldShowDecisionSidebarButton(
                pIsDecisionPanel: _mode == PolicyPanelMode.Decision,
                pPolicyEnabled: KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom));
            float barW = showCoreSidebar
                ? Mathf.Max(180f, fullW - DECISION_SIDEBAR_W - DECISION_SIDEBAR_GAP)
                : fullW;
            CreateProgressSlot("DecisionResearchProgress", pKingdom, decision, PolicyNodeKind.Decision,
                TopLeft(barX, pY + 20f), new Vector2(barW, PROGRESS_H));
            if (showCoreSidebar)
                BuildCoreFabricationSidebarButton(pKingdom,
                    TopLeft(barX + barW + DECISION_SIDEBAR_GAP, pY + 20f),
                    new Vector2(DECISION_SIDEBAR_W, PROGRESS_H));

            return BuildSection(pKingdom, AW_L10n.Text("aw_policy_decision_section", "\u53EF\u6267\u884C\u51B3\u7B56"),
                KingdomPolicyDefs.Decisions, pY + 58f);
        }

        private void BuildCoreFabricationSidebarButton(Kingdom pKingdom, Vector2 pPos, Vector2 pSize)
        {
            bool locked = KingdomPolicyService.IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId);
            int count = KingdomPolicyService.CountCoreFabricationProjects(pKingdom);
            int progress = Mathf.FloorToInt(KingdomPolicyService.GetCoreFabricationProgressFraction(pKingdom) * 100f);
            string label = locked
                ? AW_L10n.Text("aw_policy_node_locked", "\u5DF2\u9501\u5B9A")
                : CoreFabricationSlotRules.BuildSidebarLabel(
                KingdomPolicyService.GetCoreFabricationCityName(pKingdom), count, progress,
                AW_L10n.Text("aw_core_fabrication_queue", "\u6838\u5FC3\u961F\u5217"),
                AW_L10n.Text("aw_war_cb_core", "\u6838\u5FC3"));
            var box = CreateButtonBox("CoreFabricationQueue", label, pPos, pSize,
                new Color(1f, 0.93f, 0.68f, 1f), () =>
                {
                    if (KingdomPolicyService.IsNodeLocked(pKingdom, DecisionQueueRules.FabricateCoreDecisionId))
                        return;
                    City city = WarTerritoryService.FindFirstCoreProjectTargetCity(pKingdom);
                    if (city?.data != null)
                        KingdomPolicyService.StartFabricationDecision(pKingdom, pKingdom, city,
                            WarTerritoryService.PROJECT_CORE);
                    Refresh();
                });
            Image img = box.GetComponent<Image>();
            if (img != null && locked) img.color = NodeLockedBackground();
            else if (img != null && count > 0) img.color = new Color(0.58f, 0.47f, 0.22f, 0.96f);
            var button = box.GetComponent<Button>();
            if (button != null) button.interactable = !locked;
            SetTip(box, AW_L10n.Text("aw_core_fabrication_queue", "\u6838\u5FC3\u961F\u5217"),
                BuildCoreFabricationSidebarTooltip(pKingdom));
        }

        private float BuildSection(Kingdom pKingdom, string pTitle, IEnumerable<KingdomPolicyDef> pDefs, float pY)
        {
            var defs = pDefs.ToList();
            Transform parent = _canvas ?? ContentTransform;
            CreateText("Section_" + pTitle, pTitle, TopLeft(CONTENT_PAD_X, pY), new Vector2(180, SECTION_TITLE_H),
                TextAnchor.MiddleLeft, 11, new Color(1f, 0.92f, 0.6f, 1f), parent);

            PolicyTreeLayout layout = BuildTreeLayout(defs);
            float startX = CONTENT_PAD_X + 4f;
            float startY = pY + TREE_SECTION_TOP;

            foreach (KingdomPolicyDef def in defs)
            {
                if (!layout.Positions.TryGetValue(def.Id, out Vector2 localPos))
                    continue;

                Vector2 pos = TopLeft(startX + localPos.x, startY + localPos.y);
                BuildNode(pKingdom, def, pos, parent);
            }

            ExpandCanvasSize(Mathf.Max(TREE_MIN_W, CONTENT_PAD_X * 2f + NODE_W + layout.MaxDepth * TREE_COL_W + 80f),
                startY + layout.MaxRow * TREE_ROW_H + NODE_H + TREE_SECTION_BOTTOM);

            return startY + layout.MaxRow * TREE_ROW_H + NODE_H + TREE_SECTION_BOTTOM;
        }

        private PolicyTreeLayout BuildTreeLayout(List<KingdomPolicyDef> pDefs)
        {
            if (pDefs.Count == 0) return new PolicyTreeLayout();

            bool useManualLayout = pDefs.Any(p => p.Kind == PolicyNodeKind.Decision);
            if (useManualLayout)
                return BuildManualTreeLayout(pDefs);

            var byId = pDefs
                .Where(p => !string.IsNullOrEmpty(p.Id))
                .GroupBy(p => p.Id)
                .ToDictionary(p => p.Key, p => p.First());
            var depthCache = new Dictionary<string, int>();
            var branchCache = new Dictionary<string, string>();
            foreach (KingdomPolicyDef def in pDefs)
                CalculateTreeDepth(def, byId, depthCache, new HashSet<string>());

            var ordered = pDefs
                .Select(def => new
                {
                    Def = def,
                    Depth = depthCache.TryGetValue(def.Id, out int depth) ? depth : 0,
                    Branch = GetBranchKey(def, byId, branchCache, new HashSet<string>())
                })
                .OrderBy(p => p.Depth)
                .ThenBy(p => p.Branch)
                .ThenBy(p => p.Def.Row)
                .ThenBy(p => p.Def.Column)
                .ThenBy(p => PolicyName(p.Def))
                .ToList();

            var layout = new PolicyTreeLayout();
            foreach (var column in ordered.GroupBy(p => p.Depth).OrderBy(p => p.Key))
            {
                int row = 0;
                foreach (var item in column)
                {
                    layout.Positions[item.Def.Id] = new Vector2(column.Key * TREE_COL_W, row * TREE_ROW_H);
                    layout.MaxDepth = Mathf.Max(layout.MaxDepth, column.Key);
                    layout.MaxRow = Mathf.Max(layout.MaxRow, row);
                    row++;
                }
            }

            return layout;
        }

        private static PolicyTreeLayout BuildManualTreeLayout(List<KingdomPolicyDef> pDefs)
        {
            var layout = new PolicyTreeLayout();
            foreach (KingdomPolicyDef def in pDefs)
            {
                layout.Positions[def.Id] = new Vector2(def.Column * TREE_COL_W, def.Row * TREE_ROW_H);
                layout.MaxDepth = Mathf.Max(layout.MaxDepth, def.Column);
                layout.MaxRow = Mathf.Max(layout.MaxRow, def.Row);
            }

            return layout;
        }

        private static int CalculateTreeDepth(KingdomPolicyDef pDef, Dictionary<string, KingdomPolicyDef> pById,
            Dictionary<string, int> pCache, HashSet<string> pVisiting)
        {
            if (pDef == null || string.IsNullOrEmpty(pDef.Id)) return 0;
            if (pCache.TryGetValue(pDef.Id, out int cached)) return cached;
            if (!pVisiting.Add(pDef.Id)) return 0;

            int depth = 0;
            foreach (string req in SameKindRequirements(pDef))
            {
                if (string.IsNullOrEmpty(req) || !pById.TryGetValue(req, out KingdomPolicyDef reqDef)) continue;
                depth = Mathf.Max(depth, CalculateTreeDepth(reqDef, pById, pCache, pVisiting) + 1);
            }

            pVisiting.Remove(pDef.Id);
            pCache[pDef.Id] = depth;
            return depth;
        }

        private static string GetBranchKey(KingdomPolicyDef pDef, Dictionary<string, KingdomPolicyDef> pById,
            Dictionary<string, string> pCache, HashSet<string> pVisiting)
        {
            if (pDef == null || string.IsNullOrEmpty(pDef.Id)) return "";
            if (pCache.TryGetValue(pDef.Id, out string cached)) return cached;
            if (!pVisiting.Add(pDef.Id)) return pDef.Id;

            string root = pDef.Id;
            KingdomPolicyDef firstReq = SameKindRequirements(pDef)
                .Where(pById.ContainsKey)
                .Select(p => pById[p])
                .OrderBy(p => p.Row)
                .ThenBy(p => p.Column)
                .ThenBy(p => PolicyName(p))
                .FirstOrDefault();
            if (firstReq != null)
                root = GetBranchKey(firstReq, pById, pCache, pVisiting);

            pVisiting.Remove(pDef.Id);
            pCache[pDef.Id] = root;
            return root;
        }

        private static IEnumerable<string> SameKindRequirements(KingdomPolicyDef pDef)
        {
            if (pDef == null) return System.Array.Empty<string>();
            if (pDef.Kind == PolicyNodeKind.Tech) return pDef.RequiredTechs ?? System.Array.Empty<string>();
            if (pDef.Kind == PolicyNodeKind.Social) return pDef.RequiredPolicies ?? System.Array.Empty<string>();
            return System.Array.Empty<string>();
        }

        private void CreateSectionSeparator(float pY)
        {
            Transform parent = _canvas ?? ContentTransform;
            var obj = new GameObject("PolicyTreeSeparator", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            _created.Add(obj);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = TopLeft(CONTENT_PAD_X, pY);
            rect.sizeDelta = new Vector2(Mathf.Max(TREE_MIN_W - CONTENT_PAD_X * 2f, _contentWidth - CONTENT_PAD_X * 2f), 2f);

            var img = obj.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = new Color(1f, 0.92f, 0.6f, 0.35f);
            img.raycastTarget = false;
        }

        private void ExpandCanvasSize(float pWidth, float pHeight)
        {
            if (_canvasRect == null) return;
            _canvasRect.sizeDelta = new Vector2(
                Mathf.Max(_canvasRect.sizeDelta.x, _contentWidth, pWidth),
                Mathf.Max(_canvasRect.sizeDelta.y, _viewportHeight, pHeight));
        }

        private void BuildPolicyLinks(Kingdom pKingdom)
        {
            foreach (KingdomPolicyDef target in KingdomPolicyDefs.ResearchPolicies)
            {
                if (target.Kind == PolicyNodeKind.Tech)
                {
                    foreach (string requiredTech in target.RequiredTechs ?? System.Array.Empty<string>())
                        DrawOrthogonalRequirementLine(pKingdom, KingdomPolicyDefs.Get(requiredTech), target);
                    continue;
                }

                if (target.Kind == PolicyNodeKind.Social)
                {
                    foreach (string requiredPolicy in target.RequiredPolicies ?? System.Array.Empty<string>())
                        DrawOrthogonalRequirementLine(pKingdom, KingdomPolicyDefs.Get(requiredPolicy), target);
                }
            }
        }

        private void DrawOrthogonalRequirementLine(Kingdom pKingdom, KingdomPolicyDef pSource, KingdomPolicyDef pTarget)
        {
            if (pSource == null || pTarget == null) return;
            if (!_nodeCenters.TryGetValue(pSource.Id, out Vector2 sourceCenter)) return;
            if (!_nodeCenters.TryGetValue(pTarget.Id, out Vector2 targetCenter)) return;

            Vector2 delta = targetCenter - sourceCenter;
            if (delta.sqrMagnitude < 0.001f) return;

            float sign = targetCenter.x >= sourceCenter.x ? 1f : -1f;
            Vector2 start = HorizontalRectEdge(sourceCenter, sign);
            Vector2 end = HorizontalRectEdge(targetCenter, -sign);
            float midX = (start.x + end.x) * 0.5f;
            if (Mathf.Abs(end.x - start.x) < 56f)
                midX = start.x + sign * 56f;

            Color color = LinkColor(KingdomPolicyService.GetStatus(pKingdom, pTarget));
            string name = pSource.Id + "_to_" + pTarget.Id;
            if (Mathf.Abs(start.y - end.y) < 2f)
            {
                DrawLinkSegment(name + "_h", start, end, color);
                return;
            }

            Vector2 bendA = new Vector2(midX, start.y);
            Vector2 bendB = new Vector2(midX, end.y);
            DrawLinkSegment(name + "_h1", start, bendA, color);
            DrawLinkSegment(name + "_v", bendA, bendB, color);
            DrawLinkSegment(name + "_h2", bendB, end, color);
        }

        private void DrawLinkSegment(string pName, Vector2 pStart, Vector2 pEnd, Color pColor)
        {
            Vector2 lineDelta = pEnd - pStart;
            float length = lineDelta.magnitude;
            if (length < 2f) return;

            var obj = new GameObject("PolicyLink_" + pName, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(_canvas ?? ContentTransform, false);
            _created.Add(obj);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(length, LINK_THICKNESS);
            rect.anchoredPosition = pStart;
            rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(lineDelta.y, lineDelta.x) * Mathf.Rad2Deg);
            obj.transform.SetAsFirstSibling();

            var img = obj.GetComponent<Image>();
            img.sprite = LineSprite();
            img.type = Image.Type.Simple;
            img.color = pColor;
            img.raycastTarget = false;
        }

        private static Vector2 HorizontalRectEdge(Vector2 pCenter, float pSign)
        {
            return new Vector2(pCenter.x + pSign * (NODE_W * 0.5f - 2f), pCenter.y);
        }

        private static Color LinkColor(PolicyNodeStatus pStatus)
        {
            if (pStatus == PolicyNodeStatus.Completed) return new Color(0.35f, 0.9f, 0.42f, 0.78f);
            if (pStatus == PolicyNodeStatus.Current) return new Color(1f, 0.86f, 0.35f, 0.9f);
            if (pStatus == PolicyNodeStatus.Available) return new Color(1f, 0.78f, 0.38f, 0.74f);
            return new Color(0.55f, 0.55f, 0.55f, 0.42f);
        }

        private static Sprite LineSprite()
        {
            if (_lineSprite != null) return _lineSprite;
            _lineSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _lineSprite;
        }

        private void BuildNode(Kingdom pKingdom, KingdomPolicyDef pDef, Vector2 pPos, Transform pParent)
        {
            PolicyNodeStatus status = KingdomPolicyService.GetStatus(pKingdom, pDef);
            bool playerLocked = KingdomPolicyService.IsNodeLocked(pKingdom, pDef.Id);
            bool forceMode = _mode == PolicyPanelMode.Research || _mode == PolicyPanelMode.Decision;
            string name = PolicyName(pDef);
            if (status == PolicyNodeStatus.Current)
                name += "\n" + Mathf.FloorToInt(KingdomPolicyService.GetProgressFraction(pKingdom, pDef) * 100f) + "%";

            _nodeCenters[pDef.Id] = new Vector2(pPos.x + NODE_W * 0.5f, pPos.y - NODE_H * 0.5f);
            var box = CreateButtonBox("Node_" + pDef.Id, name, pPos, new Vector2(NODE_W, NODE_H),
                NodeTextColor(status), () =>
                {
                    if (pDef.Id == "aw_decision_year_name")
                    {
                        NameDecisionWindow.Open(pKingdom.id);
                        return;
                    }
                    bool changed = forceMode
                        ? KingdomPolicyService.ForceStartResearch(pKingdom, pDef.Id)
                        : KingdomPolicyService.StartResearch(pKingdom, pDef.Id);
                    if (changed)
                        Refresh();
                }, pParent);

            var button = box.GetComponent<Button>();
            if (button != null)
                button.interactable = !playerLocked && (forceMode
                    ? status != PolicyNodeStatus.Completed
                    : status == PolicyNodeStatus.Available);

            Image img = box.GetComponent<Image>();
            if (img != null) img.color = playerLocked ? NodeLockedBackground() : NodeBackground(status);
            AddNodeIcon(box.transform, pDef);
            AddCrossRequirementBadge(box.transform, pDef);
            AddNodeLockToggle(box.transform, pKingdom, pDef);
            SetTip(box, PolicyName(pDef), BuildNodeTooltip(pKingdom, pDef, status, forceMode));
        }

        private GameObject CreateButtonBox(string pName, string pText, Vector2 pPos, Vector2 pSize,
            Color pTextColor, System.Action pClick, Transform pParent = null)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent ?? ContentTransform, false);
            _created.Add(obj);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPos;

            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.95f);
            var btn = obj.GetComponent<Button>();
            btn.onClick.AddListener(() => pClick?.Invoke());

            var text = CreateTextObject("Text", obj.transform, pText, new Vector2(4, 0), new Vector2(-4, 0),
                TextAnchor.MiddleCenter, 9, pTextColor);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
            return obj;
        }

        private void CreateText(string pName, string pText, Vector2 pPos, Vector2 pSize,
            TextAnchor pAnchor, int pFontSize, Color pColor, Transform pParent = null)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent ?? ContentTransform, false);
            _created.Add(obj);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPos;
            var text = obj.GetComponent<Text>();
            SetupText(text, pText, pAnchor, pFontSize, pColor);
        }

        private Text CreateTextObject(string pName, Transform pParent, string pText, Vector2 pOffsetMin,
            Vector2 pOffsetMax, TextAnchor pAnchor, int pFontSize, Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = pOffsetMin;
            rect.offsetMax = pOffsetMax;
            var text = obj.GetComponent<Text>();
            SetupText(text, pText, pAnchor, pFontSize, pColor);
            text.raycastTarget = false;
            return text;
        }

        private void CreateIconObject(string pName, Transform pParent, Sprite pSprite, Vector2 pTopLeft, Vector2 pSize)
        {
            if (pSprite == null) return;
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pTopLeft;
            rect.sizeDelta = pSize;
            var img = obj.GetComponent<Image>();
            img.sprite = pSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private static void SetupText(Text pText, string pValue, TextAnchor pAnchor, int pFontSize, Color pColor)
        {
            pText.text = pValue;
            pText.font = LocalizedTextManager.current_font;
            pText.fontSize = pFontSize;
            pText.alignment = pAnchor;
            pText.color = pColor;
            pText.horizontalOverflow = HorizontalWrapMode.Wrap;
            pText.verticalOverflow = VerticalWrapMode.Overflow;
            pText.supportRichText = true;
        }

        private void AddNodeIcon(Transform pNode, KingdomPolicyDef pDef)
        {
            Sprite sprite = SpriteTextureLoader.getSprite(pDef.IconPath)
                            ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                            ?? SpriteTextureLoader.getSprite("ui/special/button");
            if (sprite == null) return;

            var obj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(pNode, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(13, 13);
            rect.anchoredPosition = new Vector2(3, -3);
            var img = obj.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private void AddCrossRequirementBadge(Transform pNode, KingdomPolicyDef pDef)
        {
            if (pDef.Kind != PolicyNodeKind.Social) return;
            if (pDef.RequiredTechs == null || pDef.RequiredTechs.Length == 0) return;

            Sprite sprite = SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                            ?? SpriteTextureLoader.getSprite("ui/special/button");
            CreateIconObject("TechRequirementBadge", pNode, sprite,
                new Vector2(NODE_W - 16f, -3f), new Vector2(13f, 13f));
        }

        private void AddNodeLockToggle(Transform pNode, Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pNode == null || pKingdom?.data == null || pDef == null) return;
            bool locked = KingdomPolicyService.IsNodeLocked(pKingdom, pDef.Id);
            var obj = new GameObject("PolicyNodeLock", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pNode, false);

            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(15f, 15f);
            rect.anchoredPosition = new Vector2(NODE_W - 18f, -3f);

            var img = obj.GetComponent<Image>();
            AW_UIStyle.ApplyButton(img, 0.95f);
            img.color = locked ? new Color(0.76f, 0.28f, 0.22f, 1f) : new Color(0.34f, 0.42f, 0.52f, 0.96f);

            Sprite sprite = SpriteTextureLoader.getSprite(locked ? "ui/icons/iconLock" : "ui/icons/iconUnlock")
                            ?? SpriteTextureLoader.getSprite("ui/icons/iconPlotsList");
            if (sprite != null)
                CreateIconObject("Icon", obj.transform, sprite, new Vector2(2f, -2f), new Vector2(11f, 11f));
            else
                CreateTextObject("Text", obj.transform, locked ? "X" : "L",
                    new Vector2(1f, 0f), new Vector2(-1f, 0f), TextAnchor.MiddleCenter, 8, Color.white);

            var button = obj.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                if (KingdomPolicyService.ToggleNodeLocked(pKingdom, pDef.Id))
                    Refresh();
            });

            string title = locked
                ? AW_L10n.Text("aw_policy_unlock_node", "\u89E3\u9664\u9501\u5B9A")
                : AW_L10n.Text("aw_policy_lock_node", "\u9501\u5B9A\u8BE5\u9879");
            string desc = locked
                ? AW_L10n.Text("aw_policy_unlock_node_desc", "\u6062\u590D\u8BE5\u9879\u7684\u6B63\u5E38\u9009\u62E9")
                : AW_L10n.Text("aw_policy_lock_node_desc", "\u73A9\u5BB6\u548CAI\u90FD\u4E0D\u80FD\u9009\u62E9\u8BE5\u9879");
            SetTip(obj, title, PolicyName(pDef) + "\n" + desc);
        }

        private static Vector2 TopLeft(float pX, float pY)
        {
            return new Vector2(pX, -pY);
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _whiteSprite;
        }

        private void SetContentHeight(float pHeight)
        {
            var contentRect = ContentTransform != null ? ContentTransform.GetComponent<RectTransform>() : null;
            if (contentRect == null) return;
            float height = Mathf.Max(_viewportHeight + 1f, pHeight + CONTENT_PAD_BOTTOM);
            contentRect.sizeDelta = new Vector2(_contentWidth, height);
        }

        private static string BuildCurrentSummary(Kingdom pKingdom)
        {
            var parts = new List<string>();
            KingdomPolicyDef tech = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Tech));
            KingdomPolicyDef social = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Social));
            if (tech != null) parts.Add(PolicyName(tech));
            if (social != null) parts.Add(PolicyName(social));
            return parts.Count == 0 ? AW_L10n.Text("aw_policy_idle", "\u5F85\u5B9A") : string.Join("/", parts.ToArray());
        }

        private static string BuildDecisionSummary(Kingdom pKingdom)
        {
            KingdomPolicyDef decision = KingdomPolicyDefs.Get(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision));
            if (decision == null)
                return AW_L10n.Text("aw_policy_no_current_decision", "\u65E0\u51B3\u7B56");

            string name = PolicyName(decision);
            string target = KingdomPolicyService.BuildDecisionTargetLine(pKingdom);
            return string.IsNullOrEmpty(target) ? name : name + " " + target;
        }

        private static string BuildNodeTooltip(Kingdom pKingdom, KingdomPolicyDef pDef, PolicyNodeStatus pStatus,
            bool pForceMode)
        {
            bool playerLocked = KingdomPolicyService.IsNodeLocked(pKingdom, pDef?.Id);
            var lines = new List<string>
            {
                PolicyDesc(pDef),
                AW_L10n.Text("aw_policy_cost", "\u9700\u6C42") + ": " + Mathf.CeilToInt(pDef.Cost),
                AW_L10n.Text("aw_policy_status", "\u72B6\u6001") + ": " +
                (playerLocked
                    ? AW_L10n.Text("aw_policy_node_locked_by_player", "\u73A9\u5BB6\u9501\u5B9A")
                    : StatusText(pStatus))
            };

            if (playerLocked)
                lines.Add(AW_L10n.Text("aw_policy_lock_node_desc",
                    "\u73A9\u5BB6\u548CAI\u90FD\u4E0D\u80FD\u9009\u62E9\u8BE5\u9879"));

            if (pForceMode && !playerLocked && pStatus != PolicyNodeStatus.Completed)
                lines.Add(AW_L10n.Text("aw_policy_force_switch_hint", "\u70B9\u51FB\u5F3A\u5236\u5207\u6362\u4E3A\u5F53\u524D\u7814\u53D1"));

            if (pStatus == PolicyNodeStatus.Current)
            {
                lines.Add(AW_L10n.Text("aw_policy_progress", "\u8FDB\u5EA6") + ": " +
                          Mathf.FloorToInt(KingdomPolicyService.GetProgress(pKingdom, pDef.Kind)) + "/" +
                          Mathf.CeilToInt(pDef.Cost));
                if (pDef.Kind == PolicyNodeKind.Decision)
                {
                    string target = KingdomPolicyService.BuildDecisionTargetLine(pKingdom);
                    if (!string.IsNullOrEmpty(target)) lines.Add(target);
                }
            }

            AddRequirementTooltipLines(lines, pDef);

            var missing = KingdomPolicyService.MissingRequirements(pKingdom, pDef).ToList();
            if (missing.Count > 0)
            {
                var names = missing.Select(id => PolicyName(KingdomPolicyDefs.Get(id) ?? new KingdomPolicyDef { FallbackName = id, NameKey = id }));
                lines.Add(AW_L10n.Text("aw_policy_missing", "\u672A\u6EE1\u8DB3") + ": " + string.Join(", ", names.ToArray()));
            }
            AddSpecialRequirementTooltipLines(lines, pKingdom, pDef);

            return string.Join("\n", lines.ToArray());
        }

        private static void AddSpecialRequirementTooltipLines(List<string> pLines, Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pDef?.Id == "aw_decision_claim_mandate")
            {
                MandateRitesSnapshot rites = MandateRitesService.ReadSnapshot(pKingdom);
                if (MandateService.CanDeclareMandate(pKingdom, out string reason)) return;
                string detail = reason == "ritual_completeness_missing"
                    ? RitualCompletenessRequirement(rites)
                    : MandateRequirementReason(reason);
                pLines.Add(AW_L10n.Text("aw_policy_missing", "\u672A\u6EE1\u8DB3") +
                           ": " + detail);
                return;
            }
            if (pDef?.Id != "aw_decision_title_upgrade") return;
            if (KingdomPolicyService.CanPromoteTitle(pKingdom, out string promotionReason))
                return;
            pLines.Add(AW_L10n.Text("aw_policy_missing", "\u672A\u6EE1\u8DB3") +
                       ": " + PromotionRequirementReason(promotionReason));
        }

        private static string RitualCompletenessRequirement(MandateRitesSnapshot pRites)
        {
            var parts = new List<string>
            {
                AW_L10n.Text("aw_ritual_completeness_missing", "礼制完备度不足") +
                " " + pRites.total_points + "/" + pRites.ordinary_required
            };
            if (pRites.policy_points == 0)
                parts.Add(AW_L10n.Text("aw_ritual_policy_source", "天命礼制政策"));
            if (pRites.temple_points == 0)
                parts.Add(AW_L10n.Text("aw_ritual_capital_temple_source", "首都太庙"));
            parts.Add(AW_L10n.Text("aw_ritual_sacrifice_source", "大祭永久点") +
                      ": " + pRites.permanent_points);
            return string.Join(" · ", parts.ToArray());
        }

        private static string PromotionRequirementReason(string pReason)
        {
            return pReason switch
            {
                "requires_ancestral_rites" => AW_L10n.Text(
                    "aw_requires_ancestral_rites", "需要完成宗庙礼制"),
                "requires_rites_music" => AW_L10n.Text(
                    "aw_requires_rites_music", "需要完成礼乐科技"),
                "requires_overlord_approval" => AW_L10n.Text(
                    "aw_title_upgrade_overlord", "需要宗主批准"),
                "maximum_title" => AW_L10n.Text(
                    "aw_title_upgrade_maximum", "已达最高爵位"),
                "territory_requirement" => AW_L10n.Text(
                    "aw_title_upgrade_territory", "领土规模不足"),
                _ => string.IsNullOrEmpty(pReason)
                    ? AW_L10n.Text("aw_mandate_req_unknown", "条件不足")
                    : pReason
            };
        }

        private static string MandateRequirementReason(string pReason)
        {
            switch (pReason)
            {
                case "invalid": return AW_L10n.Text("aw_mandate_req_invalid", "\u65E0\u6548\u56FD\u5BB6");
                case "no_king": return AW_L10n.Text("aw_mandate_req_no_king", "\u6CA1\u6709\u5728\u4F4D\u541B\u4E3B");
                case "already_exists": return AW_L10n.Text("aw_mandate_req_already_exists", "\u5F53\u524D\u5DF2\u6709\u5929\u547D\u738B\u671D");
                case "vassal": return AW_L10n.Text("aw_mandate_req_vassal", "\u9644\u5EB8\u56FD\u4E0D\u80FD\u53D7\u547D");
                case "unsupported": return AW_L10n.Text("aw_mandate_req_unsupported", "\u672A\u63A5\u5165\u5929\u547D\u4F53\u7CFB");
                case "too_small": return AW_L10n.Text("aw_mandate_req_too_small", "\u56FD\u5BB6\u8FC7\u5C0F");
                case "core_control": return AW_L10n.Text("aw_mandate_req_core_control", "\u6CD5\u7406\u63A7\u5236\u4E0D\u8DB3");
                case "not_strongest": return AW_L10n.Text("aw_mandate_req_not_strongest", "\u4E0D\u662F\u6700\u5F3A\u72EC\u7ACB\u56FD");
                case "ritual_completeness_missing": return AW_L10n.Text("aw_ritual_completeness_missing", "礼制完备度不足");
                default: return string.IsNullOrEmpty(pReason) ? AW_L10n.Text("aw_mandate_req_unknown", "\u6761\u4EF6\u4E0D\u8DB3") : pReason;
            }
        }

        private static void AddRequirementTooltipLines(List<string> pLines, KingdomPolicyDef pDef)
        {
            if (pDef == null) return;
            if (pDef.Kind == PolicyNodeKind.Tech)
            {
                AddRequirementTooltipLine(pLines, "aw_policy_tech_prereq", "\u79D1\u6280\u524D\u7F6E", pDef.RequiredTechs);
                return;
            }

            if (pDef.Kind == PolicyNodeKind.Social)
            {
                AddRequirementTooltipLine(pLines, "aw_policy_policy_prereq", "\u56FD\u7B56\u524D\u7F6E", pDef.RequiredPolicies);
                AddRequirementTooltipLine(pLines, "aw_policy_cross_tech_prereq", "\u79D1\u6280\u524D\u63D0", pDef.RequiredTechs);
            }
        }

        private static void AddRequirementTooltipLine(List<string> pLines, string pKey, string pFallback, string[] pIds)
        {
            if (pIds == null || pIds.Length == 0) return;
            var names = pIds
                .Select(id => PolicyName(KingdomPolicyDefs.Get(id) ?? new KingdomPolicyDef { FallbackName = id, NameKey = id }))
                .Where(p => !string.IsNullOrEmpty(p));
            pLines.Add(AW_L10n.Text(pKey, pFallback) + "\uFF1A" + string.Join(", ", names.ToArray()));
        }

        private sealed class PolicyTreeLayout
        {
            public readonly Dictionary<string, Vector2> Positions = new Dictionary<string, Vector2>();
            public int MaxDepth;
            public int MaxRow;
        }

        private static string PolicyName(KingdomPolicyDef pDef)
        {
            return pDef == null ? "" : AW_L10n.Text(pDef.NameKey, pDef.FallbackName);
        }

        private static string PolicyDesc(KingdomPolicyDef pDef)
        {
            return pDef == null ? "" : AW_L10n.Text(pDef.DescKey, pDef.FallbackDesc);
        }

        private static string StatusText(PolicyNodeStatus pStatus)
        {
            if (pStatus == PolicyNodeStatus.Completed) return AW_L10n.Text("aw_policy_completed", "\u5DF2\u5B8C\u6210");
            if (pStatus == PolicyNodeStatus.Current) return AW_L10n.Text("aw_policy_current", "\u8FDB\u884C\u4E2D");
            if (pStatus == PolicyNodeStatus.Available) return AW_L10n.Text("aw_policy_available", "\u53EF\u7814\u53D1");
            return AW_L10n.Text("aw_policy_locked", "\u672A\u89E3\u9501");
        }

        private static string ClassFallbackName(string pClassId)
        {
            return ClassName(pClassId);
        }

        private static string ClassName(string pClassId)
        {
            return AW_L10n.Text(KingdomPolicyService.GetClassLocaleKey(pClassId),
                pClassId switch
                {
                    KingdomPolicyDefs.ClassSlaveOwner => "\u5974\u96B6\u5236",
                    KingdomPolicyDefs.ClassHalfAristocrat => "\u534A\u8D35\u65CF\u5236",
                    KingdomPolicyDefs.ClassAristocrat => "\u5C01\u5EFA\u8D35\u65CF",
                    KingdomPolicyDefs.ClassReform => "\u6539\u9769\u5236",
                    KingdomPolicyDefs.ClassRepublic => "\u5171\u548C\u653F\u4F53",
                    KingdomPolicyDefs.ClassRebel => "\u519C\u6C11\u4E49\u519B",
                    _ => "\u90E8\u843D\u5236"
                });
        }

        private static string ClassDesc(string pClassId)
        {
            if (pClassId == KingdomPolicyDefs.ClassSlaveOwner)
                return AW_L10n.Text("aw_policy_class_slaveowner_desc", "\u56FD\u5BB6\u627F\u8BA4\u5974\u96B6\u52B3\u5F79\u4E0E\u5974\u96B6\u519B\u4F53\u7CFB\u3002");
            if (pClassId == KingdomPolicyDefs.ClassHalfAristocrat)
                return AW_L10n.Text("aw_policy_class_halfaristocrat_desc", "\u5974\u96B6\u5236\u5411\u8D35\u65CF\u5206\u5C42\u8FC7\u6E21\u3002");
            if (pClassId == KingdomPolicyDefs.ClassAristocrat)
                return AW_L10n.Text("aw_policy_class_aristocrat_desc", "\u8D35\u65CF\u548C\u6C0F\u652F\u6210\u4E3A\u5730\u65B9\u79E9\u5E8F\u6838\u5FC3\u3002");
            if (pClassId == KingdomPolicyDefs.ClassReform)
                return AW_L10n.Text("aw_policy_class_reform_desc", "\u6539\u9769\u65E7\u5236\uFF0C\u63A8\u52A8\u5E9F\u5974\u548C\u66F4\u96C6\u4E2D\u7684\u56FD\u5BB6\u79E9\u5E8F\u3002");
            if (pClassId == KingdomPolicyDefs.ClassRepublic)
                return AW_L10n.Text("aw_policy_class_republic_desc", "\u65E0\u53EF\u7ACB\u4E4B\u541B\u65F6\u7684\u5171\u548C\u653F\u4F53\uFF0C\u56FD\u5BB6\u4E0D\u518D\u4F7F\u7528\u541B\u4E3B\u7235\u4F4D\u540E\u7F00\u3002");
            if (pClassId == KingdomPolicyDefs.ClassRebel)
                return AW_L10n.Text("aw_policy_class_peasant_rebel_desc", "\u4E49\u519B\u519B\u653F\u5E9C\u4E0D\u8BBE\u7981\u536B\u519B\uFF0C\u5E76\u52A8\u5458\u6210\u5E74\u7537\u6027\u4FDD\u536B\u8D77\u4E49\u3002");
            return AW_L10n.Text("aw_policy_class_default_desc", "\u4EE5\u57FA\u7840\u8840\u7F18\u548C\u805A\u843D\u79E9\u5E8F\u7EF4\u6301\u7684\u65E9\u671F\u56FD\u5BB6\u3002");
        }

        private static Color NodeTextColor(PolicyNodeStatus pStatus)
        {
            if (pStatus == PolicyNodeStatus.Locked) return new Color(0.72f, 0.72f, 0.72f, 1f);
            if (pStatus == PolicyNodeStatus.Completed) return new Color(0.78f, 1f, 0.74f, 1f);
            if (pStatus == PolicyNodeStatus.Current) return new Color(1f, 0.9f, 0.55f, 1f);
            return Color.white;
        }

        private static Color NodeBackground(PolicyNodeStatus pStatus)
        {
            if (pStatus == PolicyNodeStatus.Locked) return new Color(0.45f, 0.45f, 0.45f, 0.62f);
            if (pStatus == PolicyNodeStatus.Completed) return new Color(0.35f, 0.55f, 0.35f, 0.95f);
            if (pStatus == PolicyNodeStatus.Current) return new Color(0.65f, 0.52f, 0.22f, 0.95f);
            return Color.white;
        }

        private static Color NodeLockedBackground()
        {
            return new Color(0.38f, 0.22f, 0.22f, 0.9f);
        }

        private static void SetTip(GameObject pOwner, string pTitle, string pDesc)
        {
            var tip = pOwner.GetComponent<TipButton>();
            if (tip == null) return;
            tip.enabled = true;
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () =>
                Tooltip.show(pOwner, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = pTitle ?? "", tip_description = pDesc ?? "" });
        }

        private void ClearCreated()
        {
            foreach (var obj in _created)
                if (obj != null) Destroy(obj);
            _created.Clear();
        }

        private sealed class PolicyWindowDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private RectTransform _target;
            private Vector2 _startPointer;
            private Vector2 _startAnchored;

            public void Setup(RectTransform pTarget)
            {
                _target = pTarget;
            }

            public void OnBeginDrag(PointerEventData pEventData)
            {
                if (_target == null) return;
                _startPointer = pEventData.position;
                _startAnchored = _target.anchoredPosition;
            }

            public void OnDrag(PointerEventData pEventData)
            {
                if (_target == null) return;
                Vector2 delta = pEventData.position - _startPointer;
                _target.anchoredPosition = _startAnchored + delta;
            }
        }

    }
}
