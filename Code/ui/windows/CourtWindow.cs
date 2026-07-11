using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
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
    internal class CourtWindow : AbstractWindow<CourtWindow>
    {
        private const float DefaultWidth = 560f;
        private const float DefaultHeight = 360f;
        private const float MinWidth = 420f;
        private const float MinHeight = 280f;
        private const float MaxWidth = 900f;
        private const float MaxHeight = 650f;
        private const float ScrollMarginX = 42f;
        private const float ScrollMarginY = 58f;
        private const float SummaryHeight = 62f;
        private const float CanvasTopGap = 10f;
        private const float CanvasPadding = 24f;
        private const int PortraitsPerFrame = 8;

        private static long _kingdomId = -1L;
        private static Sprite _whiteSprite;
        private readonly List<CourtActorNodeView> _nodePool = new List<CourtActorNodeView>();
        private readonly List<GameObject> _links = new List<GameObject>();
        private Vector2 _windowSize = new Vector2(DefaultWidth, DefaultHeight);
        private RectTransform _canvasRect;
        private GameObject _dragSurface;
        private RectTransform _summaryRect;
        private Text _summaryPrimary;
        private Text _summarySecondary;
        private Image _summaryFlagBackground;
        private Image _summaryFlagIcon;
        private RectTransform _resizeHandle;
        private long _displayedKingdomId = -1L;
        private Coroutine _renderCoroutine;
        private int _renderVersion;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.COURT);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.COURT,
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
            EnsureUi();
            InstallWindowHandlers();
        }

        private void ApplyWindowLayout()
        {
            float contentWidth = Mathf.Max(1f, _windowSize.x - ScrollMarginX);
            float viewportHeight = Mathf.Max(1f, _windowSize.y - ScrollMarginY);
            RectTransform background = BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;

            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f, _windowSize.y * 0.5f - 12f);

            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.52f, 30f);
                titleRect.localPosition = new Vector3(0f, _windowSize.y * 0.5f - 16f, 0f);
            }

            ScrollWindow scrollWindow = GetComponent<ScrollWindow>();
            if (scrollWindow?.titleText != null)
            {
                scrollWindow.titleText.text = AW_L10n.Text("aw_court_title", "Court of the Hundred Schools");
                scrollWindow.titleText.transform.localPosition =
                    new Vector3(0f, _windowSize.y * 0.5f - 16f, 0f);
                scrollWindow.titleText.raycastTarget = false;
            }

            Transform scroll = BackgroundTransform?.Find("Scroll View");
            RectTransform scrollRect = scroll?.GetComponent<RectTransform>();
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(contentWidth, viewportHeight);
                scrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect scrollComponent = scroll?.GetComponent<ScrollRect>();
            if (scrollComponent != null)
            {
                scrollComponent.horizontal = false;
                scrollComponent.vertical = false;
            }
            Transform scrollbar = BackgroundTransform?.Find("Scroll View/Scrollbar Vertical");
            if (scrollbar != null)
            {
                foreach (Graphic graphic in scrollbar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
                RectTransform scrollbarRect = scrollbar.GetComponent<RectTransform>();
                if (scrollbarRect != null) scrollbarRect.anchoredPosition = new Vector2(9999f, 0f);
            }

            Transform viewport = ContentTransform?.parent;
            RectTransform viewportRect = viewport?.GetComponent<RectTransform>();
            if (viewportRect != null) viewportRect.sizeDelta = new Vector2(contentWidth, viewportHeight);
            if (viewport != null && viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform contentRect = ContentTransform?.GetComponent<RectTransform>();
            if (contentRect != null) contentRect.sizeDelta = new Vector2(contentWidth, viewportHeight);
            LayoutFixedUi(contentWidth, viewportHeight);
        }

        private void EnsureUi()
        {
            if (ContentTransform == null) return;
            Transform existingSummary = ContentTransform.Find("CourtSummary");
            GameObject summary = existingSummary != null
                ? existingSummary.gameObject
                : new GameObject("CourtSummary", typeof(RectTransform), typeof(Image));
            if (existingSummary == null) summary.transform.SetParent(ContentTransform, false);
            _summaryRect = summary.GetComponent<RectTransform>();
            summary.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.055f, 0.96f);
            EnsureSummaryFlag(summary.transform);
            _summaryPrimary = EnsureText(summary.transform, "Primary", 11, TextAnchor.UpperLeft);
            _summarySecondary = EnsureText(summary.transform, "Secondary", 9, TextAnchor.UpperLeft);

            Transform existingSurface = ContentTransform.Find("CourtDragSurface");
            _dragSurface = existingSurface != null
                ? existingSurface.gameObject
                : new GameObject("CourtDragSurface", typeof(RectTransform), typeof(Image), typeof(TreeDragPanHandler));
            if (existingSurface == null) _dragSurface.transform.SetParent(ContentTransform, false);
            Image surfaceImage = _dragSurface.GetComponent<Image>();
            surfaceImage.sprite = WhiteSprite();
            surfaceImage.color = new Color(0f, 0f, 0f, 0.001f);
            surfaceImage.raycastTarget = true;

            Transform existingCanvas = ContentTransform.Find("CourtCanvas");
            GameObject canvas = existingCanvas != null
                ? existingCanvas.gameObject
                : new GameObject("CourtCanvas", typeof(RectTransform), typeof(TreeDragPanHandler));
            if (existingCanvas == null) canvas.transform.SetParent(ContentTransform, false);
            _canvasRect = canvas.GetComponent<RectTransform>();
            _canvasRect.anchorMin = new Vector2(0.5f, 1f);
            _canvasRect.anchorMax = new Vector2(0.5f, 1f);
            _canvasRect.pivot = new Vector2(0.5f, 1f);
            _canvasRect.sizeDelta = Vector2.one;

            _dragSurface.transform.SetAsFirstSibling();
            _canvasRect.transform.SetSiblingIndex(1);
            _summaryRect.transform.SetAsLastSibling();
            TreeDragPanHandler canvasPan = canvas.GetComponent<TreeDragPanHandler>();
            canvasPan.Setup(_canvasRect, ContentTransform.parent as RectTransform);
            TreeDragPanHandler surfacePan = _dragSurface.GetComponent<TreeDragPanHandler>();
            surfacePan.Setup(_canvasRect, ContentTransform.parent as RectTransform);
            ApplyWindowLayout();
        }

        private void LayoutFixedUi(float pContentWidth, float pViewportHeight)
        {
            if (_summaryRect != null)
            {
                _summaryRect.anchorMin = new Vector2(0f, 1f);
                _summaryRect.anchorMax = new Vector2(0f, 1f);
                _summaryRect.pivot = new Vector2(0f, 1f);
                _summaryRect.anchoredPosition = Vector2.zero;
                _summaryRect.sizeDelta = new Vector2(pContentWidth, SummaryHeight);
                LayoutSummaryText(_summaryPrimary, 44f, 4f, pContentWidth - 52f, 25f);
                LayoutSummaryText(_summarySecondary, 44f, 29f, pContentWidth - 52f, 29f);
            }
            RectTransform surface = _dragSurface?.GetComponent<RectTransform>();
            if (surface != null)
            {
                surface.anchorMin = new Vector2(0f, 1f);
                surface.anchorMax = new Vector2(0f, 1f);
                surface.pivot = new Vector2(0f, 1f);
                surface.anchoredPosition = new Vector2(0f, -SummaryHeight);
                surface.sizeDelta = new Vector2(pContentWidth,
                    Mathf.Max(1f, pViewportHeight - SummaryHeight));
            }
            if (_resizeHandle != null)
                _resizeHandle.anchoredPosition = new Vector2(-2f, 2f);
        }

        private void InstallWindowHandlers()
        {
            RectTransform root = BackgroundTransform?.parent?.GetComponent<RectTransform>() ??
                                 GetComponent<RectTransform>();
            Transform title = BackgroundTransform?.Find("TitleBackground");
            if (title != null && root != null)
            {
                Image titleImage = title.GetComponent<Image>();
                if (titleImage != null) titleImage.raycastTarget = true;
                CourtWindowDragHandler drag = title.GetComponent<CourtWindowDragHandler>() ??
                                              title.gameObject.AddComponent<CourtWindowDragHandler>();
                drag.Setup(root);
            }

            Transform existing = BackgroundTransform?.Find("CourtResizeHandle");
            GameObject handle = existing != null
                ? existing.gameObject
                : new GameObject("CourtResizeHandle", typeof(RectTransform), typeof(Image),
                    typeof(CourtWindowResizeHandler));
            if (existing == null) handle.transform.SetParent(BackgroundTransform, false);
            _resizeHandle = handle.GetComponent<RectTransform>();
            _resizeHandle.anchorMin = new Vector2(1f, 0f);
            _resizeHandle.anchorMax = new Vector2(1f, 0f);
            _resizeHandle.pivot = new Vector2(1f, 0f);
            _resizeHandle.sizeDelta = new Vector2(18f, 18f);
            _resizeHandle.anchoredPosition = new Vector2(-2f, 2f);
            Image image = handle.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = new Color(0.84f, 0.68f, 0.34f, 0.72f);
            CourtWindowResizeHandler resize = handle.GetComponent<CourtWindowResizeHandler>();
            resize.Setup(() => _windowSize, size =>
            {
                _windowSize = new Vector2(
                    Mathf.Clamp(size.x, MinWidth, MaxWidth),
                    Mathf.Clamp(size.y, MinHeight, MaxHeight));
                ApplyWindowLayout();
            });
        }

        private void Refresh()
        {
            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                ApplyWindowLayout();
                EnsureUi();
                Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
                bool switched = _displayedKingdomId != _kingdomId;
                _displayedKingdomId = _kingdomId;
                if (switched) ResetCanvas();
                CancelPendingRender();
                HideNodesAndLinks();

                if (kingdom?.data == null || kingdom.isRekt())
                {
                    _summaryPrimary.text = AW_L10n.Text("aw_policy_no_kingdom", "Kingdom missing");
                    _summarySecondary.text = "";
                    if (_summaryFlagBackground != null) _summaryFlagBackground.enabled = false;
                    if (_summaryFlagIcon != null) _summaryFlagIcon.enabled = false;
                    return;
                }

                CourtSnapshot snapshot = CourtService.GetSnapshot(kingdom);
                UpdateSummary(kingdom, snapshot);
                List<CourtPyramidNodeModel> nodes = CourtReadModelService.Build(kingdom);
                CourtPyramidCanvasBounds bounds = CourtPyramidRules.CalculateCanvasBounds(nodes,
                    CourtActorNodeView.Width, CourtActorNodeView.Height, CanvasPadding);
                _canvasRect.sizeDelta = new Vector2(bounds.Width, bounds.Height);
                Vector2 nodeOffset = new Vector2(bounds.OffsetX, bounds.OffsetY);
                BuildLinks(nodes, KingdomColor(kingdom), nodeOffset);
                int renderVersion = _renderVersion;
                _renderCoroutine = StartCoroutine(RenderNodesBatched(
                    nodes, kingdom, nodeOffset, renderVersion));
                _summaryRect.transform.SetAsLastSibling();
            }
            finally
            {
                UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtUiBuildIndex, benchmark);
            }
        }

        private void UpdateSummary(Kingdom pKingdom, CourtSnapshot pSnapshot)
        {
            string bannerId = "";
            try { bannerId = pKingdom.getActorAsset()?.banner_id ?? ""; }
            catch { }
            KingdomFlagBuilder.Build(bannerId, pKingdom.data.banner_icon_id,
                pKingdom.data.banner_background_id, HistoryColors.FromKingdom(pKingdom),
                pKingdom.data.color_id, _summaryFlagBackground, _summaryFlagIcon);
            string government = RepublicGovernmentService.IsRepublic(pKingdom)
                ? AW_L10n.Text("aw_government_republic", "Republic")
                : AW_L10n.Text("aw_government_monarchy", "Monarchy");
            string tier = TierName(CourtService.ResolveTier(pKingdom));
            _summaryPrimary.color = KingdomColor(pKingdom);
            _summaryPrimary.text = pKingdom.name + "  |  " + government + "  |  " + tier + "  |  " +
                                   AW_L10n.Text("aw_court_efficiency", "Court Efficiency") + " " +
                                   Mathf.FloorToInt(pSnapshot.efficiency);
            string schools = SchoolName(pSnapshot.dominant_school);
            if (!string.IsNullOrEmpty(pSnapshot.secondary_school))
                schools += " / " + SchoolName(pSnapshot.secondary_school);
            _summarySecondary.text = AW_L10n.Text("aw_court_dominant_school", "Dominant Schools") + ": " +
                                     schools + "    " +
                                     AW_L10n.Text("aw_court_direction_livelihood", "Livelihood") + " " +
                                     Percent(pSnapshot.livelihood) + "  " +
                                     AW_L10n.Text("aw_court_direction_aggression", "Aggression") + " " +
                                     Percent(pSnapshot.aggression) + "  " +
                                     AW_L10n.Text("aw_court_direction_peace", "Peace") + " " +
                                     Percent(pSnapshot.peace);
        }

        private CourtActorNodeView GetNode(int pIndex)
        {
            while (_nodePool.Count <= pIndex)
                _nodePool.Add(CourtActorNodeView.Create(_canvasRect));
            return _nodePool[pIndex];
        }

        private IEnumerator RenderNodesBatched(List<CourtPyramidNodeModel> pNodes, Kingdom pKingdom,
            Vector2 pOffset, int pVersion)
        {
            int index = 0;
            while (index < pNodes.Count)
            {
                if (pVersion != _renderVersion || pKingdom?.data == null || pKingdom.id != _displayedKingdomId)
                    yield break;
                int end = CourtPyramidRules.NextBatchEnd(index, pNodes.Count, PortraitsPerFrame);
                long benchmark = UpdateAgeBenchmark.Begin();
                try
                {
                    for (; index < end; index++)
                    {
                        CourtActorNodeView view = GetNode(index);
                        RectTransform rect = view.GetComponent<RectTransform>();
                        rect.anchoredPosition = new Vector2(
                            pNodes[index].X + pOffset.x, pNodes[index].Y + pOffset.y);
                        view.Bind(pNodes[index], pKingdom);
                        view.gameObject.SetActive(true);
                    }
                }
                finally
                {
                    UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtUiBuildIndex, benchmark);
                }
                if (index < pNodes.Count) yield return null;
            }
            if (pVersion == _renderVersion) _renderCoroutine = null;
        }

        private void BuildLinks(List<CourtPyramidNodeModel> pNodes, Color pColor, Vector2 pOffset)
        {
            if (pNodes == null || pNodes.Count <= 1) return;
            List<IGrouping<int, CourtPyramidNodeModel>> rows = pNodes
                .GroupBy(p => p.Rank)
                .OrderBy(p => p.Key)
                .ToList();
            for (int row = 1; row < rows.Count; row++)
            {
                CourtPyramidNodeModel[] parents = rows[row - 1].ToArray();
                foreach (CourtPyramidNodeModel child in rows[row])
                {
                    CourtPyramidNodeModel parent = parents
                        .OrderBy(p => Mathf.Abs(p.X - child.X))
                        .First();
                    CreateLink(new Vector2(parent.X, parent.Y - CourtActorNodeView.Height) + pOffset,
                        new Vector2(child.X, child.Y) + pOffset, pColor);
                }
            }
        }

        private void CreateLink(Vector2 pFrom, Vector2 pTo, Color pColor)
        {
            var obj = new GameObject("CourtRankLink", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(_canvasRect, false);
            obj.transform.SetAsFirstSibling();
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Vector2 delta = pTo - pFrom;
            rect.anchoredPosition = (pFrom + pTo) * 0.5f;
            rect.sizeDelta = new Vector2(delta.magnitude, 2f);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            Image image = obj.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = new Color(pColor.r, pColor.g, pColor.b, 0.48f);
            image.raycastTarget = false;
            _links.Add(obj);
        }

        private void HideNodesAndLinks()
        {
            foreach (CourtActorNodeView node in _nodePool)
                if (node != null) node.gameObject.SetActive(false);
            foreach (GameObject link in _links)
                if (link != null) Destroy(link);
            _links.Clear();
        }

        private void CancelPendingRender()
        {
            _renderVersion++;
            if (_renderCoroutine == null) return;
            StopCoroutine(_renderCoroutine);
            _renderCoroutine = null;
        }

        private void ResetCanvas()
        {
            if (_canvasRect == null) return;
            _canvasRect.anchoredPosition = new Vector2(0f, -SummaryHeight - CanvasTopGap);
            _canvasRect.localScale = Vector3.one;
        }

        private static Text EnsureText(Transform pParent, string pName, int pSize, TextAnchor pAnchor)
        {
            Transform existing = pParent.Find(pName);
            GameObject obj = existing != null
                ? existing.gameObject
                : new GameObject(pName, typeof(RectTransform), typeof(Text));
            if (existing == null) obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private void EnsureSummaryFlag(Transform pParent)
        {
            Transform existing = pParent.Find("KingdomFlag");
            GameObject flag = existing != null
                ? existing.gameObject
                : new GameObject("KingdomFlag", typeof(RectTransform), typeof(Image));
            if (existing == null) flag.transform.SetParent(pParent, false);
            RectTransform rect = flag.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -8f);
            rect.sizeDelta = new Vector2(28f, 28f);
            _summaryFlagBackground = flag.GetComponent<Image>();
            _summaryFlagBackground.preserveAspect = true;
            _summaryFlagBackground.raycastTarget = false;

            Transform existingIcon = flag.transform.Find("Icon");
            GameObject icon = existingIcon != null
                ? existingIcon.gameObject
                : new GameObject("Icon", typeof(RectTransform), typeof(Image));
            if (existingIcon == null) icon.transform.SetParent(flag.transform, false);
            RectTransform iconRect = icon.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            _summaryFlagIcon = icon.GetComponent<Image>();
            _summaryFlagIcon.preserveAspect = true;
            _summaryFlagIcon.raycastTarget = false;
        }

        private static void LayoutSummaryText(Text pText, float pX, float pY, float pWidth, float pHeight)
        {
            if (pText == null) return;
            RectTransform rect = pText.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(pX, -pY);
            rect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static string Percent(float pValue)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(pValue) * 100f) + "%";
        }

        private static string TierName(string pTier)
        {
            switch (pTier ?? "")
            {
                case CourtTier.SanShengLiuBu:
                    return AW_L10n.Text("aw_court_tier_sanshengliubu", "Three Departments and Six Ministries");
                case CourtTier.SanGongJiuQing:
                    return AW_L10n.Text("aw_court_tier_sangongjiuqing", "Three Excellencies and Nine Ministers");
                default:
                    return AW_L10n.Text("aw_court_button_primitive", "Primitive Council");
            }
        }

        private static string SchoolName(string pSchoolId)
        {
            switch (pSchoolId ?? "")
            {
                case CourtSchoolId.Ru: return AW_L10n.Text("aw_court_school_ru", "Ru School");
                case CourtSchoolId.Legalist: return AW_L10n.Text("aw_court_school_fa", "Legalist School");
                case CourtSchoolId.Dao: return AW_L10n.Text("aw_court_school_dao", "Daoist School");
                case CourtSchoolId.Mohist: return AW_L10n.Text("aw_court_school_mo", "Mohist School");
                case CourtSchoolId.Military: return AW_L10n.Text("aw_court_school_bing", "Military School");
                case CourtSchoolId.Diplomat: return AW_L10n.Text("aw_court_school_zongheng", "Diplomat School");
                case CourtSchoolId.Agrarian: return AW_L10n.Text("aw_court_school_nong", "Agrarian School");
                case CourtSchoolId.YinYang: return AW_L10n.Text("aw_court_school_yinyang", "Yin-Yang School");
                case CourtSchoolId.Logician: return AW_L10n.Text("aw_court_school_ming", "Logician School");
                case CourtSchoolId.Medical: return AW_L10n.Text("aw_court_school_medical", "Medical School");
                case CourtSchoolId.Syncretist: return AW_L10n.Text("aw_court_school_syncretist", "Syncretist School");
                case CourtSchoolId.Merchant: return AW_L10n.Text("aw_court_school_merchant", "Merchant School");
                case CourtSchoolId.Craftsman: return AW_L10n.Text("aw_court_school_craftsman", "Craftsman School");
                case CourtSchoolId.Historian: return AW_L10n.Text("aw_court_school_historian", "Historian School");
                default: return AW_L10n.Text("aw_policy_idle", "Idle");
            }
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            string hex = HistoryColors.FromKingdom(pKingdom);
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color))
                return new Color(color.r, color.g, color.b, 1f);
            return Color.white;
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);
            return _whiteSprite;
        }

        private sealed class CourtWindowDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private RectTransform _target;
            private Vector2 _startPointer;
            private Vector2 _startPosition;

            public void Setup(RectTransform pTarget) { _target = pTarget; }

            public void OnBeginDrag(PointerEventData pEventData)
            {
                if (_target == null) return;
                _startPointer = pEventData.position;
                _startPosition = _target.anchoredPosition;
            }

            public void OnDrag(PointerEventData pEventData)
            {
                if (_target == null) return;
                _target.anchoredPosition = _startPosition + pEventData.position - _startPointer;
            }
        }

        private sealed class CourtWindowResizeHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private Func<Vector2> _getSize;
            private Action<Vector2> _setSize;
            private Vector2 _startPointer;
            private Vector2 _startSize;

            public void Setup(Func<Vector2> pGetSize, Action<Vector2> pSetSize)
            {
                _getSize = pGetSize;
                _setSize = pSetSize;
            }

            public void OnBeginDrag(PointerEventData pEventData)
            {
                _startPointer = pEventData.position;
                _startSize = _getSize?.Invoke() ?? new Vector2(DefaultWidth, DefaultHeight);
            }

            public void OnDrag(PointerEventData pEventData)
            {
                Vector2 delta = pEventData.position - _startPointer;
                _setSize?.Invoke(new Vector2(_startSize.x + delta.x, _startSize.y - delta.y));
            }
        }
    }
}
