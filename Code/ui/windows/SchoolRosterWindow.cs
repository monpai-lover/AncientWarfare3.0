using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class SchoolRosterWindow : AbstractWindow<SchoolRosterWindow>
    {
        private const float DefaultWidth = 720f;
        private const float DefaultHeight = 440f;
        private const float MinWidth = 520f;
        private const float MinHeight = 320f;
        private const float MaxWidth = 1000f;
        private const float MaxHeight = 700f;
        private const float WindowMarginX = 36f;
        private const float WindowMarginY = 58f;
        private const float InnerMargin = 8f;
        private const float SelectorWidth = 190f;
        private const float PanelGap = 8f;
        private const float SummaryHeight = 58f;
        private const float CanvasPadding = 30f;
        private const float HorizontalSpacing = 158f;
        private const float VerticalSpacing = 132f;
        private const int ColumnsPerRow = 6;
        private const int PortraitsPerFrame = 8;
        private const int LinkSegmentsPerFrame = 24;
        private const float PortraitCullInterval = .2f;

        private static string _requestedSchool = CourtSchoolId.Ru;
        private static Sprite _whiteSprite;

        private readonly List<SchoolListItem> _schoolItems = new List<SchoolListItem>();
        private readonly List<SchoolRosterNodeView> _nodePool =
            new List<SchoolRosterNodeView>();
        private readonly List<GameObject> _linkPool = new List<GameObject>();

        private Vector2 _windowSize = new Vector2(DefaultWidth, DefaultHeight);
        private RectTransform _selectorPanel;
        private RectTransform _selectorViewport;
        private RectTransform _schoolListContent;
        private RectTransform _mainPanel;
        private RectTransform _summaryPanel;
        private Text _summaryTitle;
        private Text _summaryBody;
        private RectTransform _canvasViewport;
        private RectTransform _canvasRect;
        private RectTransform _dragSurface;
        private Text _emptyText;
        private RectTransform _resizeHandle;
        private Vector2 _nodeOffset;
        private Vector2 _initialCanvasPan;
        private string _selectedSchool = CourtSchoolId.Ru;
        private string _displayedSchool = "";
        private long _displayedMembershipVersion = -1L;
        private long _displayedResidenceRevision = -1L;
        private Coroutine _renderCoroutine;
        private int _renderVersion;
        private int _activeLinkCount;
        private bool _resetCanvasOnRefresh;
        private float _nextPortraitCullTime;

        public static void Open(string pSchoolId = CourtSchoolId.Ru)
        {
            _requestedSchool = CourtSchoolRegistry.Find(pSchoolId) == null
                ? CourtSchoolId.Ru
                : pSchoolId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.SCHOOL_ROSTER);
            if (Instance != null) Instance._resetCanvasOnRefresh = true;
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.SCHOOL_ROSTER,
                () => Instance?.ApplyRequestAndRefresh());
        }

        public static void ResetWorldCache()
        {
            Instance?.ResetWorldState();
        }

        protected override void Init()
        {
            EnsureUi();
            InstallWindowHandlers();
            ApplyWindowLayout();
        }

        public override void OnNormalEnable()
        {
            ApplyRequestAndRefresh();
        }

        public override void OnNormalDisable()
        {
            CancelPendingRender();
            HideNodesAndLinks();
        }

        private void Update()
        {
            if (!isActiveAndEnabled || World.world == null) return;
            if (_displayedMembershipVersion != SchoolMembershipService.Version ||
                _displayedResidenceRevision !=
                HistoricalAffiliationService.ResidenceRevision)
            {
                Refresh();
                return;
            }
            if (Time.unscaledTime < _nextPortraitCullTime) return;
            _nextPortraitCullTime = Time.unscaledTime + PortraitCullInterval;
            RefreshPortraitVisibility();
        }

        private void ApplyRequestAndRefresh()
        {
            _selectedSchool = CourtSchoolRegistry.Find(_requestedSchool) == null
                ? CourtSchoolId.Ru
                : _requestedSchool;
            _resetCanvasOnRefresh = true;
            Refresh();
        }

        private void EnsureUi()
        {
            if (ContentTransform == null) return;
            foreach (LayoutGroup layout in ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;

            _selectorPanel = Panel("SchoolRosterSelector", ContentTransform);
            _mainPanel = Panel("SchoolRosterMain", ContentTransform);
            BuildSchoolSelector();
            BuildSummary();
            BuildCanvas();
        }

        private void BuildSchoolSelector()
        {
            var viewportObject = new GameObject("SchoolRosterSelectorViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportObject.transform.SetParent(_selectorPanel, false);
            _selectorViewport = viewportObject.GetComponent<RectTransform>();
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, .14f);

            var contentObject = new GameObject("SchoolRosterSelectorContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(_selectorViewport, false);
            _schoolListContent = contentObject.GetComponent<RectTransform>();
            _schoolListContent.anchorMin = new Vector2(0f, 1f);
            _schoolListContent.anchorMax = new Vector2(1f, 1f);
            _schoolListContent.pivot = new Vector2(.5f, 1f);
            _schoolListContent.anchoredPosition = Vector2.zero;

            ScrollRect scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.viewport = _selectorViewport;
            scroll.content = _schoolListContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            foreach (CourtSchoolDefinition unused in CourtSchoolRegistry.All)
                _schoolItems.Add(SchoolListItem.Create(_schoolListContent));
        }

        private void BuildSummary()
        {
            _summaryPanel = Panel("SchoolRosterSummary", _mainPanel);
            Image image = _summaryPanel.GetComponent<Image>();
            image.color = new Color(.075f, .065f, .05f, .98f);
            _summaryTitle = Text("Title", _summaryPanel, 13, TextAnchor.UpperLeft);
            _summaryBody = Text("Body", _summaryPanel, 9, TextAnchor.UpperLeft);
        }

        private void BuildCanvas()
        {
            var viewportObject = new GameObject("SchoolRosterCanvasViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(_mainPanel, false);
            _canvasViewport = viewportObject.GetComponent<RectTransform>();
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(.025f, .022f, .018f, .96f);

            var surfaceObject = new GameObject("SchoolRosterDragSurface", typeof(RectTransform),
                typeof(Image), typeof(TreeDragPanHandler));
            surfaceObject.transform.SetParent(_canvasViewport, false);
            _dragSurface = surfaceObject.GetComponent<RectTransform>();
            Image surfaceImage = surfaceObject.GetComponent<Image>();
            surfaceImage.sprite = WhiteSprite();
            surfaceImage.color = new Color(0f, 0f, 0f, .001f);
            surfaceImage.raycastTarget = true;

            var canvasObject = new GameObject("SchoolRosterCanvas", typeof(RectTransform),
                typeof(TreeDragPanHandler));
            canvasObject.transform.SetParent(_canvasViewport, false);
            _canvasRect = canvasObject.GetComponent<RectTransform>();
            _canvasRect.anchorMin = new Vector2(0f, 1f);
            _canvasRect.anchorMax = new Vector2(0f, 1f);
            _canvasRect.pivot = new Vector2(0f, 1f);
            _canvasRect.anchoredPosition = Vector2.zero;
            _canvasRect.sizeDelta = Vector2.one;

            TreeDragPanHandler canvasPan = canvasObject.GetComponent<TreeDragPanHandler>();
            canvasPan.Setup(_canvasRect, _canvasViewport);
            TreeDragPanHandler surfacePan = surfaceObject.GetComponent<TreeDragPanHandler>();
            surfacePan.Setup(_canvasRect, _canvasViewport);

            _emptyText = Text("Empty", _canvasRect, 11, TextAnchor.MiddleCenter);
            _emptyText.text = AW_L10n.Text("aw_school_roster_empty",
                "No living members in this school");
            _emptyText.raycastTarget = false;
        }

        private void ApplyWindowLayout()
        {
            float contentWidth = Mathf.Max(1f, _windowSize.x - WindowMarginX);
            float contentHeight = Mathf.Max(1f, _windowSize.y - WindowMarginY);
            RectTransform background = BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;

            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * .52f, 30f);
                titleRect.localPosition = new Vector3(0f, _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow scrollWindow = GetComponent<ScrollWindow>();
            if (scrollWindow?.titleText != null)
            {
                scrollWindow.titleText.text = AW_L10n.Text("aw_school_roster_title",
                    "School Members");
                scrollWindow.titleText.transform.localPosition =
                    new Vector3(0f, _windowSize.y * .5f - 16f, 0f);
            }

            Transform scrollTransform = BackgroundTransform?.Find("Scroll View");
            RectTransform scrollRect = scrollTransform?.GetComponent<RectTransform>();
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(contentWidth, contentHeight);
                scrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect originalScroll = scrollTransform?.GetComponent<ScrollRect>();
            if (originalScroll != null) originalScroll.enabled = false;
            Transform originalScrollbar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (originalScrollbar != null)
            {
                foreach (Graphic graphic in originalScrollbar.GetComponentsInChildren<Graphic>(
                             true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            }
            RectTransform originalViewport = ContentTransform?.parent as RectTransform;
            if (originalViewport != null)
            {
                originalViewport.sizeDelta = new Vector2(contentWidth, contentHeight);
                if (originalViewport.GetComponent<RectMask2D>() == null)
                    originalViewport.gameObject.AddComponent<RectMask2D>();
            }
            RectTransform content = ContentTransform?.GetComponent<RectTransform>();
            if (content != null) content.sizeDelta = new Vector2(contentWidth, contentHeight);

            float panelHeight = Mathf.Max(1f, contentHeight - InnerMargin * 2f);
            float selectorWidth = Mathf.Min(SelectorWidth,
                Mathf.Max(140f, contentWidth * .32f));
            float mainWidth = Mathf.Max(1f,
                contentWidth - InnerMargin * 2f - selectorWidth - PanelGap);
            Place(_selectorPanel, InnerMargin, -InnerMargin, selectorWidth, panelHeight);
            Place(_mainPanel, InnerMargin + selectorWidth + PanelGap, -InnerMargin,
                mainWidth, panelHeight);
            Place(_selectorViewport, 4f, -4f, selectorWidth - 8f, panelHeight - 8f);
            foreach (SchoolListItem item in _schoolItems)
                item.SetAvailableWidth(selectorWidth - 8f);
            _schoolListContent.sizeDelta = new Vector2(0f,
                CourtSchoolRegistry.All.Count * (SchoolListItem.Height + 2f));
            Place(_summaryPanel, 0f, 0f, mainWidth, SummaryHeight);
            Place(_summaryTitle.GetComponent<RectTransform>(), 10f, -6f,
                mainWidth - 20f, 20f);
            Place(_summaryBody.GetComponent<RectTransform>(), 10f, -28f,
                mainWidth - 20f, 27f);
            Place(_canvasViewport, 0f, -SummaryHeight - PanelGap, mainWidth,
                Mathf.Max(1f, panelHeight - SummaryHeight - PanelGap));
            Fill(_dragSurface);
            RectTransform emptyRect = _emptyText?.GetComponent<RectTransform>();
            if (emptyRect != null)
                Place(emptyRect, 0f, 0f, Mathf.Max(1f, _canvasViewport.sizeDelta.x),
                    Mathf.Max(1f, _canvasViewport.sizeDelta.y));
            if (_resizeHandle != null) _resizeHandle.anchoredPosition = new Vector2(-2f, 2f);
        }

        private void InstallWindowHandlers()
        {
            RectTransform root = BackgroundTransform?.parent?.GetComponent<RectTransform>() ??
                                 GetComponent<RectTransform>();
            Transform title = BackgroundTransform?.Find("TitleBackground");
            if (title != null && root != null)
            {
                Image image = title.GetComponent<Image>();
                if (image != null) image.raycastTarget = true;
                RosterWindowDragHandler drag = title.GetComponent<RosterWindowDragHandler>() ??
                                               title.gameObject.AddComponent<RosterWindowDragHandler>();
                drag.Setup(root);
            }

            var handle = new GameObject("SchoolRosterResizeHandle", typeof(RectTransform),
                typeof(Image), typeof(RosterWindowResizeHandler));
            handle.transform.SetParent(BackgroundTransform, false);
            _resizeHandle = handle.GetComponent<RectTransform>();
            _resizeHandle.anchorMin = new Vector2(1f, 0f);
            _resizeHandle.anchorMax = new Vector2(1f, 0f);
            _resizeHandle.pivot = new Vector2(1f, 0f);
            _resizeHandle.sizeDelta = new Vector2(18f, 18f);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.sprite = WhiteSprite();
            handleImage.color = new Color(.84f, .68f, .34f, .72f);
            RosterWindowResizeHandler resize = handle.GetComponent<RosterWindowResizeHandler>();
            resize.Setup(() => _windowSize, size =>
            {
                _windowSize = new Vector2(Mathf.Clamp(size.x, MinWidth, MaxWidth),
                    Mathf.Clamp(size.y, MinHeight, MaxHeight));
                ApplyWindowLayout();
            });
        }

        private void Refresh()
        {
            if (_canvasRect == null || CourtSchoolRegistry.Find(_selectedSchool) == null) return;
            CancelPendingRender();
            HideNodesAndLinks();
            UpdateSchoolSelector();

            SchoolRosterReadModel model = SchoolRosterReadModelService.Build(_selectedSchool,
                HorizontalSpacing, VerticalSpacing, ColumnsPerRow);
            bool switchedSchool = !string.Equals(_displayedSchool, model.SchoolId,
                StringComparison.Ordinal);
            _displayedSchool = model.SchoolId;
            _displayedMembershipVersion = model.MembershipVersion;
            _displayedResidenceRevision = model.ResidenceRevision;
            UpdateSummary(model);
            LayoutCanvas(model.Nodes);
            List<SchoolRosterLinkSegment> linkSegments = BuildLinks(model);
            TrimPools(model.Nodes.Count, linkSegments.Count);
            _emptyText.gameObject.SetActive(model.Nodes.Count == 0);
            if (switchedSchool || _resetCanvasOnRefresh) ResetCanvas();
            _resetCanvasOnRefresh = false;
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(model.SchoolId);
            Sprite schoolIcon = string.IsNullOrEmpty(definition?.IconPath)
                ? null
                : SpriteTextureLoader.getSprite(definition.IconPath);
            int version = _renderVersion;
            _renderCoroutine = StartCoroutine(RenderNodesBatched(model.Nodes, linkSegments,
                schoolIcon, version));
        }

        private void UpdateSchoolSelector()
        {
            IReadOnlyList<CourtSchoolDefinition> schools = CourtSchoolRegistry.All;
            for (int i = 0; i < _schoolItems.Count; i++)
            {
                SchoolListItem item = _schoolItems[i];
                if (i >= schools.Count)
                {
                    item.gameObject.SetActive(false);
                    continue;
                }
                CourtSchoolDefinition school = schools[i];
                RectTransform rect = item.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(0f,
                    -i * (SchoolListItem.Height + 2f));
                item.BindRoster(school,
                    SchoolMembershipService.LivingMembers(school.Id).Length,
                    school.Id == _selectedSchool, SelectSchool);
                item.gameObject.SetActive(true);
            }
        }

        private void SelectSchool(string pSchoolId)
        {
            if (CourtSchoolRegistry.Find(pSchoolId) == null || pSchoolId == _selectedSchool)
                return;
            _selectedSchool = pSchoolId;
            _requestedSchool = pSchoolId;
            _resetCanvasOnRefresh = true;
            Refresh();
        }

        private void UpdateSummary(SchoolRosterReadModel pModel)
        {
            CourtSchoolDefinition school = CourtSchoolRegistry.Find(pModel?.SchoolId);
            if (school == null) return;
            _summaryTitle.color = Parse(school.ColorHex, Color.white);
            _summaryTitle.text = AW_L10n.Text(school.NameKey, school.Id);
            _summaryBody.text = AW_L10n.Text("aw_school_roster_members", "Living Members") +
                                ": " + pModel.Nodes.Count + "    " +
                                AW_L10n.Text("aw_school_roster_teachers", "Teachers") +
                                ": " + pModel.TeacherCount + "    " +
                                AW_L10n.Text("aw_school_roster_excluded", "Invalid Records") +
                                ": " + pModel.ExcludedCount;
        }

        private void LayoutCanvas(IReadOnlyList<SchoolRosterReadNode> pNodes)
        {
            if (pNodes == null || pNodes.Count == 0)
            {
                _nodeOffset = new Vector2(CanvasPadding, -CanvasPadding);
                _initialCanvasPan = Vector2.zero;
                _canvasRect.sizeDelta = new Vector2(
                    Mathf.Max(1f, _canvasViewport.sizeDelta.x),
                    Mathf.Max(1f, _canvasViewport.sizeDelta.y));
                return;
            }

            float minX = pNodes.Min(p => p.Layout.X - SchoolRosterNodeView.Width * .5f);
            float maxX = pNodes.Max(p => p.Layout.X + SchoolRosterNodeView.Width * .5f);
            float minY = pNodes.Min(p => p.Layout.Y - SchoolRosterNodeView.Height);
            float maxY = pNodes.Max(p => p.Layout.Y);
            SchoolRosterCanvasPlacement placement = SchoolRosterRules.PlaceCanvas(
                _canvasViewport.sizeDelta.x, _canvasViewport.sizeDelta.y,
                minX, maxX, minY, maxY, CanvasPadding);
            _nodeOffset = new Vector2(placement.NodeOffsetX, placement.NodeOffsetY);
            _initialCanvasPan = new Vector2(placement.InitialPanX, placement.InitialPanY);
            _canvasRect.sizeDelta = new Vector2(placement.CanvasWidth,
                placement.CanvasHeight);
        }

        private List<SchoolRosterLinkSegment> BuildLinks(SchoolRosterReadModel pModel)
        {
            var result = new List<SchoolRosterLinkSegment>();
            if (pModel?.Links == null || pModel.Links.Count == 0) return result;
            Dictionary<long, SchoolRosterReadNode> byActor = pModel.Nodes
                .ToDictionary(p => p.Layout.ActorId);
            Color color = Parse(CourtSchoolRegistry.Find(pModel.SchoolId)?.ColorHex,
                new Color(.75f, .62f, .34f, 1f));
            color.a = .52f;
            foreach (SchoolRosterLink link in pModel.Links)
            {
                if (!byActor.TryGetValue(link.TeacherActorId,
                        out SchoolRosterReadNode teacher) ||
                    !byActor.TryGetValue(link.StudentActorId,
                        out SchoolRosterReadNode student) ||
                    teacher.Layout.Row >= student.Layout.Row) continue;
                float fromX = teacher.Layout.X + _nodeOffset.x;
                float fromY = teacher.Layout.Y + _nodeOffset.y - SchoolRosterNodeView.Height;
                float toX = student.Layout.X + _nodeOffset.x;
                float toY = student.Layout.Y + _nodeOffset.y;
                float middleY = (fromY + toY) * .5f;
                result.Add(new SchoolRosterLinkSegment(fromX, fromY, fromX, middleY,
                    color));
                result.Add(new SchoolRosterLinkSegment(fromX, middleY, toX, middleY,
                    color));
                result.Add(new SchoolRosterLinkSegment(toX, middleY, toX, toY, color));
            }
            return result;
        }

        private void AddLinkSegment(SchoolRosterLinkSegment pSegment)
        {
            GameObject line = AcquireLink();
            line.transform.SetAsFirstSibling();
            RectTransform rect = line.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(.5f, .5f);
            bool horizontal = Mathf.Abs(pSegment.FromY - pSegment.ToY) < .01f;
            rect.anchoredPosition = new Vector2((pSegment.FromX + pSegment.ToX) * .5f,
                (pSegment.FromY + pSegment.ToY) * .5f);
            rect.sizeDelta = horizontal
                ? new Vector2(Mathf.Max(2f,
                    Mathf.Abs(pSegment.ToX - pSegment.FromX) + 2f), 2f)
                : new Vector2(2f, Mathf.Max(2f,
                    Mathf.Abs(pSegment.ToY - pSegment.FromY) + 2f));
            line.GetComponent<Image>().color = pSegment.Color;
            line.SetActive(true);
        }

        private GameObject AcquireLink()
        {
            GameObject result;
            if (_activeLinkCount < _linkPool.Count)
                result = _linkPool[_activeLinkCount];
            else
            {
                result = new GameObject("SchoolRosterLink", typeof(RectTransform),
                    typeof(Image));
                result.transform.SetParent(_canvasRect, false);
                Image image = result.GetComponent<Image>();
                image.sprite = WhiteSprite();
                image.raycastTarget = false;
                _linkPool.Add(result);
            }
            _activeLinkCount++;
            return result;
        }

        private IEnumerator RenderNodesBatched(IReadOnlyList<SchoolRosterReadNode> pNodes,
            IReadOnlyList<SchoolRosterLinkSegment> pLinkSegments, Sprite pSchoolIcon,
            int pVersion)
        {
            int linkIndex = 0;
            while (linkIndex < (pLinkSegments?.Count ?? 0))
            {
                if (pVersion != _renderVersion) yield break;
                int linkEnd = Mathf.Min(linkIndex + LinkSegmentsPerFrame,
                    pLinkSegments.Count);
                for (; linkIndex < linkEnd; linkIndex++)
                    AddLinkSegment(pLinkSegments[linkIndex]);
                if (linkIndex < pLinkSegments.Count) yield return null;
            }

            int index = 0;
            while (index < (pNodes?.Count ?? 0))
            {
                if (pVersion != _renderVersion) yield break;
                int end = Mathf.Min(index + PortraitsPerFrame, pNodes.Count);
                for (; index < end; index++)
                {
                    SchoolRosterNodeView view = GetNode(index);
                    RectTransform rect = view.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(
                        pNodes[index].Layout.X + _nodeOffset.x,
                        pNodes[index].Layout.Y + _nodeOffset.y);
                    if (view.Bind(pNodes[index], pSchoolIcon))
                    {
                        view.gameObject.SetActive(true);
                        if (!view.SetPortraitVisible(IsNodeVisible(rect)))
                            _displayedMembershipVersion = -1L;
                    }
                    else
                        _displayedMembershipVersion = -1L;
                }
                if (index < pNodes.Count) yield return null;
            }
            if (pVersion == _renderVersion) _renderCoroutine = null;
        }

        private void RefreshPortraitVisibility()
        {
            int portraitBudget = PortraitsPerFrame;
            foreach (SchoolRosterNodeView view in _nodePool)
            {
                if (view == null || !view.gameObject.activeSelf) continue;
                RectTransform rect = view.GetComponent<RectTransform>();
                bool visible = IsNodeVisible(rect);
                if (!visible)
                {
                    if (!view.SetPortraitVisible(false))
                        _displayedMembershipVersion = -1L;
                    continue;
                }
                if (!view.HasPortrait && view.CanAttemptPortrait)
                {
                    if (portraitBudget <= 0) continue;
                    portraitBudget--;
                }
                if (!view.SetPortraitVisible(true))
                    _displayedMembershipVersion = -1L;
            }
        }

        private bool IsNodeVisible(RectTransform pNode)
        {
            if (pNode == null || _canvasRect == null || _canvasViewport == null) return false;
            float scale = Mathf.Max(.01f, _canvasRect.localScale.x);
            Vector2 canvasPosition = _canvasRect.anchoredPosition;
            float centerX = canvasPosition.x + pNode.anchoredPosition.x * scale;
            float top = canvasPosition.y + pNode.anchoredPosition.y * scale;
            float halfWidth = SchoolRosterNodeView.Width * scale * .5f;
            float bottom = top - SchoolRosterNodeView.Height * scale;
            const float margin = 24f;
            return centerX + halfWidth >= -margin &&
                   centerX - halfWidth <= _canvasViewport.rect.width + margin &&
                   top >= -_canvasViewport.rect.height - margin && bottom <= margin;
        }

        private SchoolRosterNodeView GetNode(int pIndex)
        {
            while (_nodePool.Count <= pIndex)
                _nodePool.Add(SchoolRosterNodeView.Create(_canvasRect));
            return _nodePool[pIndex];
        }

        private void CancelPendingRender()
        {
            _renderVersion++;
            if (_renderCoroutine == null) return;
            StopCoroutine(_renderCoroutine);
            _renderCoroutine = null;
        }

        private void HideNodesAndLinks()
        {
            foreach (SchoolRosterNodeView node in _nodePool) node?.Unbind();
            foreach (GameObject link in _linkPool)
                if (link != null) link.SetActive(false);
            _activeLinkCount = 0;
        }

        private void ResetCanvas()
        {
            if (_canvasRect == null) return;
            _canvasRect.anchoredPosition = _initialCanvasPan;
            _canvasRect.localScale = Vector3.one;
        }

        private void ResetWorldState()
        {
            CancelPendingRender();
            HideNodesAndLinks();
            _displayedSchool = "";
            _displayedMembershipVersion = -1L;
            _displayedResidenceRevision = -1L;
            _resetCanvasOnRefresh = true;
            TrimPools(0, 0, pForce: true);
        }

        private void TrimPools(int pRequiredNodes, int pRequiredLinks, bool pForce = false)
        {
            int nodeLimit = pForce ? 0 : Math.Max(32, pRequiredNodes * 2 + 8);
            for (int i = _nodePool.Count - 1; i >= nodeLimit; i--)
            {
                SchoolRosterNodeView node = _nodePool[i];
                if (node != null) UnityEngine.Object.Destroy(node.gameObject);
                _nodePool.RemoveAt(i);
            }
            int linkLimit = pForce ? 0 : Math.Max(96, pRequiredLinks * 2 + 24);
            for (int i = _linkPool.Count - 1; i >= linkLimit; i--)
            {
                GameObject link = _linkPool[i];
                if (link != null) UnityEngine.Object.Destroy(link);
                _linkPool.RemoveAt(i);
            }
            _activeLinkCount = Math.Min(_activeLinkCount, _linkPool.Count);
        }

        private static RectTransform Panel(string pName, Transform pParent)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(image, .98f);
            return obj.GetComponent<RectTransform>();
        }

        private static Text Text(string pName, Transform pParent, int pFontSize,
            TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAnchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private static void Place(RectTransform pRect, float pX, float pY, float pWidth,
            float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth), Mathf.Max(1f, pHeight));
        }

        private static void Fill(RectTransform pRect)
        {
            if (pRect == null) return;
            pRect.anchorMin = Vector2.zero;
            pRect.anchorMax = Vector2.one;
            pRect.pivot = new Vector2(.5f, .5f);
            pRect.offsetMin = Vector2.zero;
            pRect.offsetMax = Vector2.zero;
        }

        private static Color Parse(string pHex, Color pFallback)
        {
            return !string.IsNullOrEmpty(pHex) &&
                   ColorUtility.TryParseHtmlString(pHex, out Color color)
                ? color
                : pFallback;
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(.5f, .5f), 1f);
            return _whiteSprite;
        }

        private readonly struct SchoolRosterLinkSegment
        {
            public SchoolRosterLinkSegment(float pFromX, float pFromY, float pToX,
                float pToY, Color pColor)
            {
                FromX = pFromX;
                FromY = pFromY;
                ToX = pToX;
                ToY = pToY;
                Color = pColor;
            }

            public float FromX { get; }
            public float FromY { get; }
            public float ToX { get; }
            public float ToY { get; }
            public Color Color { get; }
        }

        private sealed class RosterWindowDragHandler : MonoBehaviour,
            IBeginDragHandler, IDragHandler
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

        private sealed class RosterWindowResizeHandler : MonoBehaviour,
            IBeginDragHandler, IDragHandler
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
                _setSize?.Invoke(new Vector2(_startSize.x + delta.x,
                    _startSize.y - delta.y));
            }
        }
    }
}
