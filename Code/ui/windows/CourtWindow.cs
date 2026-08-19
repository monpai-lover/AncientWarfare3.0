using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
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
        private const float SummaryHeight = 112f;
        private const float CanvasTopGap = 10f;
        private const float CanvasLeftInset = 8f;
        private const float CanvasPadding = 24f;
        private const int PortraitsPerFrame = 8;
        private const float CityCardGap = 12f;

        private static long _kingdomId = -1L;
        private static long _cityId = -1L;
        private static Sprite _whiteSprite;
        private readonly List<CourtActorNodeView> _nodePool = new List<CourtActorNodeView>();
        private readonly List<GameObject> _linkPool = new List<GameObject>();
        private readonly List<CourtCityGovernmentCard> _cityCardPool =
            new List<CourtCityGovernmentCard>();
        private readonly Queue<CourtActorNodeView> _portraitRetries =
            new Queue<CourtActorNodeView>();
        private Vector2 _windowSize = new Vector2(DefaultWidth, DefaultHeight);
        private RectTransform _canvasRect;
        private GameObject _dragSurface;
        private RectTransform _summaryRect;
        private Text _summaryPrimary;
        private Text _summarySecondary;
        private Image _summaryFlagBackground;
        private Image _summaryFlagIcon;
        private Button _kingdomBack;
        private Button _civilServiceExamButton;
        private Text _civilServiceExamText;
        private TipButton _civilServiceExamTip;
        private Button _householdButton;
        private Text _householdText;
        private TipButton _householdTip;
        private Button _customCourtWorkflowButton;
        private AWStringDropdown _localTemplateDropdown;
        private Text _centralSectionLabel;
        private Text _militarySectionLabel;
        private Text _localSectionLabel;
        private Image _militarySectionDivider;
        private Image _localSectionDivider;
        private WideWindowChrome _windowChrome;
        private long _displayedKingdomId = -1L;
        private long _displayedCityId = -1L;
        private Coroutine _renderCoroutine;
        private int _renderVersion;
        private bool _resetCanvasOnRefresh;
        private int _activeLinkCount;

        public static void Open(long pKingdomId)
        {
            OpenInternal(pKingdomId, -1L, pRefreshImmediately: false);
        }

        public static void OpenAndRefresh(long pKingdomId)
        {
            OpenInternal(pKingdomId, -1L, pRefreshImmediately: true);
        }

        public static void OpenCity(long pKingdomId, long pCityId)
        {
            OpenInternal(pKingdomId, pCityId, pRefreshImmediately: true);
        }

        private static void OpenInternal(long pKingdomId, long pCityId,
            bool pRefreshImmediately)
        {
            _kingdomId = pKingdomId;
            _cityId = pCityId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.COURT);
            if (Instance != null) Instance._resetCanvasOnRefresh = true;
            bool wasCurrent = ScrollWindow.isCurrentWindow(AW_LineageWindowIds.COURT);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.COURT,
                () => { if (Instance != null) Instance.Refresh(); });
            if (pRefreshImmediately && !wasCurrent && Instance != null)
                Instance.Refresh();
        }

        protected override void Init()
        {
            ConfigureWindow();
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (!isActiveAndEnabled || _portraitRetries.Count == 0) return;
            RetryMissingPortraits(PortraitsPerFrame);
        }

        private void RetryMissingPortraits(int pBudget)
        {
            if (pBudget <= 0 || FamilyTreeNodeView.GetAvatarPrefab() == null) return;
            int count = Math.Min(pBudget, _portraitRetries.Count);
            while (count-- > 0)
            {
                CourtActorNodeView node = _portraitRetries.Dequeue();
                if (node == null || !node.gameObject.activeSelf || !node.NeedsPortrait)
                    continue;
                if (!node.TryEnsurePortrait()) _portraitRetries.Enqueue(node);
            }
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
            _kingdomBack = EnsureButton(summary.transform, "BackToKingdom",
                AW_L10n.Text("aw_back_to_kingdom", "Back to Kingdom"),
                BackToKingdom);
            _civilServiceExamButton = EnsureButton(summary.transform,
                "CivilServiceExam", "", OpenCivilServiceExam);
            _civilServiceExamText = _civilServiceExamButton.transform
                .Find("Text")?.GetComponent<Text>();
            _civilServiceExamTip =
                _civilServiceExamButton.GetComponent<TipButton>() ??
                _civilServiceExamButton.gameObject.AddComponent<TipButton>();
            _civilServiceExamTip.type = AW_RawTooltip.TYPE;
            _householdButton = EnsureButton(summary.transform,
                "RulerHousehold", "", OpenRulerHousehold);
            _householdText = _householdButton.transform.Find("Text")
                ?.GetComponent<Text>();
            _householdTip = _householdButton.GetComponent<TipButton>() ??
                _householdButton.gameObject.AddComponent<TipButton>();
            _householdTip.type = AW_RawTooltip.TYPE;
            _customCourtWorkflowButton = EnsureButton(summary.transform,
                "CustomCourtWorkflow",
                AW_L10n.Text("aw_custom_court_workflow", "Court Editor"),
                OpenCustomCourtWorkflow);
            if (_localTemplateDropdown == null)
                _localTemplateDropdown = AWStringDropdown.Create(
                    summary.transform, "LocalCourtTemplate", 158f, 22f,
                    SelectLocalTemplate);
            _localTemplateDropdown.gameObject.SetActive(false);

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
            _canvasRect.anchorMin = new Vector2(0f, 1f);
            _canvasRect.anchorMax = new Vector2(0f, 1f);
            _canvasRect.pivot = new Vector2(0f, 1f);
            _canvasRect.sizeDelta = Vector2.one;
            RemoveOrphanedLinkChildren();
            EnsureSectionMarkers(canvas.transform);

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
                LayoutSummaryText(_summaryPrimary, 44f, 4f,
                    Mathf.Max(1f, pContentWidth - 294f), 25f);
                LayoutSummaryText(_summarySecondary, 44f, 29f,
                    Mathf.Max(1f, pContentWidth - 136f), 79f);
                LayoutSummaryButton(_kingdomBack,
                    Mathf.Max(44f, pContentWidth - 84f), 4f, 76f, 23f);
                LayoutSummaryButton(_civilServiceExamButton,
                    Mathf.Max(44f, pContentWidth - 166f), 4f, 76f, 23f);
                LayoutSummaryButton(_householdButton,
                    Mathf.Max(44f, pContentWidth - 248f), 4f, 76f, 23f);
                LayoutSummaryButton(_customCourtWorkflowButton,
                    Mathf.Max(44f, pContentWidth - 84f), 31f, 76f, 23f);
                if (_localTemplateDropdown != null)
                {
                    RectTransform dropdown =
                        _localTemplateDropdown.RectTransform;
                    dropdown.anchorMin = dropdown.anchorMax =
                        new Vector2(0f, 1f);
                    dropdown.pivot = new Vector2(0f, 1f);
                    dropdown.anchoredPosition = new Vector2(
                        Mathf.Max(44f, pContentWidth - 166f), -58f);
                }
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
            _windowChrome?.RepositionResizeHandle();
        }

        private void InstallWindowHandlers()
        {
            _windowChrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size =>
                {
                    _windowSize = size;
                    ApplyWindowLayout();
                },
                new Vector2(DefaultWidth, DefaultHeight),
                new Vector2(MinWidth, MinHeight),
                new Vector2(MaxWidth, MaxHeight));
        }

        private void Refresh()
        {
            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                ApplyWindowLayout();
                EnsureUi();
                Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
                City city = ResolveCityContext(kingdom, _cityId);
                if (_cityId >= 0 && city == null) _cityId = -1L;
                bool switched = _displayedKingdomId != _kingdomId ||
                                _displayedCityId != _cityId;
                _displayedKingdomId = _kingdomId;
                _displayedCityId = _cityId;
                if (CourtPyramidRules.ShouldResetCanvas(switched, _resetCanvasOnRefresh)) ResetCanvas();
                _resetCanvasOnRefresh = false;
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

                LocalCourtReadModel local = city == null ? null :
                    CourtReadModelService.BuildLocal(kingdom, city);
                UpdateWindowTitle(kingdom, local);
                CourtSnapshot snapshot = CourtService.GetSnapshot(kingdom);
                if (local == null) UpdateSummary(kingdom, snapshot);
                else UpdateLocalSummary(kingdom, local);
                List<CourtPyramidNodeModel> nodes = local == null
                    ? CourtReadModelService.Build(kingdom)
                    : local.Nodes;
                CourtPyramidCanvasBounds bounds = CourtPyramidRules.CalculateCanvasBounds(nodes,
                    CourtActorNodeView.Width, CourtActorNodeView.Height, CanvasPadding);
                List<LocalCourtReadModel> cityGovernments = local == null
                    ? CourtReadModelService.BuildLocalGovernments(kingdom)
                    : new List<LocalCourtReadModel>();
                Vector2 canvasSize = CalculateCanvasSize(bounds,
                    cityGovernments.Count);
                _canvasRect.sizeDelta = canvasSize;
                Vector2 nodeOffset = new Vector2(bounds.OffsetX, bounds.OffsetY);
                IReadOnlyList<CustomCourtEdge> localEdges = local != null &&
                    !string.IsNullOrEmpty(local.TemplateId)
                    ? local.Edges : null;
                BuildLinks(nodes, kingdom, KingdomColor(kingdom), nodeOffset,
                    bounds, localEdges, local != null);
                LayoutSectionMarkers(nodes, bounds, nodeOffset, KingdomColor(kingdom));
                if (local == null)
                    RenderCityGovernmentCards(cityGovernments, kingdom,
                        bounds, canvasSize);
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

        private static City ResolveCityContext(Kingdom pKingdom,
            long pCityId)
        {
            if (pKingdom?.data == null || pCityId < 0) return null;
            City city;
            try { city = World.world?.cities?.get(pCityId); }
            catch { return null; }
            return city?.data != null && !city.isRekt() &&
                   city.kingdom == pKingdom ? city : null;
        }

        private void UpdateLocalSummary(Kingdom pKingdom,
            LocalCourtReadModel pLocal)
        {
            string bannerId = "";
            try { bannerId = pKingdom.getActorAsset()?.banner_id ?? ""; }
            catch { }
            KingdomFlagBuilder.Build(bannerId, pKingdom.data.banner_icon_id,
                pKingdom.data.banner_background_id,
                HistoryColors.FromKingdom(pKingdom), pKingdom.data.color_id,
                _summaryFlagBackground, _summaryFlagIcon);
            UpdateCustomCourtWorkflowEntry(localMode: true);
            _summaryPrimary.color = KingdomColor(pKingdom);
            _summaryPrimary.text = pLocal.CityName + "  |  " +
                pLocal.CityTypeName + "  |  " +
                string.Format(AW_L10n.Text("aw_local_court_seats",
                        "Officials {0}/{1}"), pLocal.ActiveSeats,
                    pLocal.TotalSeats) + "  |  " +
                AW_L10n.Text("aw_court_efficiency", "Court Efficiency") +
                " " + Mathf.FloorToInt(pLocal.Efficiency) + "  |  " +
                AW_L10n.Text("aw_corruption_local", "Local corruption") +
                " " + (pLocal.CityCorruption?.Score ?? 0);
            _summarySecondary.text =
                AW_L10n.Text("aw_local_court_city_type", "City Type") +
                ": " + pLocal.CityTypeName + "\n" +
                AW_L10n.Text("aw_court_school", "School") + ": " +
                SchoolName(pLocal.LocalSchoolId) + "\n" +
                AW_L10n.Text("aw_corruption_country", "Country corruption") +
                ": " + (pLocal.CountryCorruption?.Score ?? 0) + "  " +
                AW_L10n.Text("aw_corruption_local", "Local corruption") +
                ": " + (pLocal.CityCorruption?.Score ?? 0);
            if (_civilServiceExamButton != null)
                _civilServiceExamButton.gameObject.SetActive(false);
            if (_householdButton != null)
                _householdButton.gameObject.SetActive(false);
            UpdateLocalTemplateOptions(pKingdom, pLocal);
        }

        private void UpdateLocalTemplateOptions(Kingdom pKingdom,
            LocalCourtReadModel pLocal)
        {
            IReadOnlyList<CustomLocalCourtTemplate> templates =
                CustomCourtRuntime.ResolvedLocalTemplates(pKingdom);
            bool available = templates != null && templates.Count > 0;
            _localTemplateDropdown.gameObject.SetActive(available);
            if (!available) return;
            _localTemplateDropdown.SetOptions(templates
                .Where(template => template != null)
                .Take(CustomLocalCourtTemplateRules.MaximumTemplates)
                .Select(template => new AWStringDropdownOption
                {
                    Id = template.Id,
                    Label = CustomLocalCourtTemplateRules.CityTypeName(
                        template,
                        HistoryLocalizationRules.CurrentLanguage() == "en")
                }), pLocal.TemplateId,
                AW_L10n.Text("aw_local_court_choose_template",
                    "Choose local government"));
        }

        private void SelectLocalTemplate(AWStringDropdownOption pOption)
        {
            if (pOption == null || _cityId < 0) return;
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            City city = ResolveCityContext(kingdom, _cityId);
            if (city == null || !CustomCourtRuntime.TrySetLocalTemplate(
                    kingdom, city, pOption.Id, pManual: true)) return;
            _resetCanvasOnRefresh = false;
            Refresh();
        }

        private static Vector2 CalculateCanvasSize(
            CourtPyramidCanvasBounds pBounds, int pCityCardCount)
        {
            if (pCityCardCount <= 0)
                return new Vector2(pBounds.Width, pBounds.Height);
            int columns = Math.Min(4, Math.Max(1, pCityCardCount));
            int rows = Mathf.CeilToInt(pCityCardCount / (float)columns);
            float cityWidth = CanvasPadding * 2f + columns *
                CourtCityGovernmentCard.Width + (columns - 1) * CityCardGap;
            float cityHeight = 28f + rows * CourtCityGovernmentCard.Height +
                               Math.Max(0, rows - 1) * CityCardGap;
            return new Vector2(Mathf.Max(pBounds.Width, cityWidth),
                pBounds.Height + cityHeight + CanvasPadding);
        }

        private void RenderCityGovernmentCards(
            IReadOnlyList<LocalCourtReadModel> pCities, Kingdom pKingdom,
            CourtPyramidCanvasBounds pBounds, Vector2 pCanvasSize)
        {
            if (pCities == null || pCities.Count == 0) return;
            int columns = Math.Min(4, Math.Max(1, pCities.Count));
            float rowWidth = columns * CourtCityGovernmentCard.Width +
                (columns - 1) * CityCardGap;
            float startX = (pCanvasSize.x - rowWidth) * 0.5f;
            float startY = -pBounds.Height - 26f;
            for (int index = 0; index < pCities.Count; index++)
            {
                CourtCityGovernmentCard card = GetCityCard(index);
                int row = index / columns;
                int column = index % columns;
                RectTransform rect = card.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(startX + column *
                    (CourtCityGovernmentCard.Width + CityCardGap),
                    startY - row * (CourtCityGovernmentCard.Height +
                                    CityCardGap));
                card.Bind(pCities[index], pKingdom,
                    cityId => OpenCity(pKingdom.id, cityId));
                card.gameObject.SetActive(true);
                if (card.LeaderNode?.NeedsPortrait == true)
                    _portraitRetries.Enqueue(card.LeaderNode);
            }
            if (_localSectionLabel != null)
            {
                _localSectionLabel.gameObject.SetActive(true);
                _localSectionLabel.text = AW_L10n.Text(
                    "aw_court_layer_city", "Local Bureaus");
                _localSectionLabel.color = KingdomColor(pKingdom);
                LayoutCanvasText(_localSectionLabel, startX,
                    startY + 20f, rowWidth, 18f);
                _localSectionLabel.transform.SetAsLastSibling();
            }
        }

        private CourtCityGovernmentCard GetCityCard(int pIndex)
        {
            while (_cityCardPool.Count <= pIndex)
                _cityCardPool.Add(CourtCityGovernmentCard.Create(_canvasRect));
            return _cityCardPool[pIndex];
        }

        private void UpdateSummary(Kingdom pKingdom, CourtSnapshot pSnapshot)
        {
            UpdateCustomCourtWorkflowEntry(localMode: false);
            if (_localTemplateDropdown != null)
                _localTemplateDropdown.gameObject.SetActive(false);
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
            string institution = CourtInstitutionService.InstitutionName(pKingdom);
            string courtIdentity = CustomCourtRuntime.HasInstance(pKingdom)
                ? CustomCourtRuntime.DisplayName(pKingdom,
                    AW_L10n.Text("aw_custom_court_workflow", "Custom Court"))
                : institution + " · " + tier;
            _summaryPrimary.color = KingdomColor(pKingdom);
            _summaryPrimary.text = pKingdom.name + "  |  " + government + "  |  " +
                                   courtIdentity + "  |  " +
                                   AW_L10n.Text("aw_court_efficiency", "Court Efficiency") + " " +
                                   Mathf.FloorToInt(pSnapshot.efficiency);
            string schools = SchoolName(pSnapshot.dominant_school);
            if (!string.IsNullOrEmpty(pSnapshot.secondary_school))
                schools += " / " + SchoolName(pSnapshot.secondary_school);
            string aristocraticGroups = AristocraticGroupSummary(pKingdom);
            string institutionEffects =
                CourtInstitutionService.EffectSummary(pKingdom);
            string politicalPoints = AW_L10n.Text(
                                         "aw_inheritance_political_points",
                                         "Political points") + ": " +
                                     KingdomPolicyService.GetPoliticalPoints(
                                         pKingdom).ToString("0.#");
            CorruptionCountrySnapshot corruption = CorruptionService.ReadCountry(pKingdom);
            _summarySecondary.text = politicalPoints + "\n" +
                                     AW_L10n.Text("aw_corruption_country", "Country corruption") +
                                     ": " + corruption.Score + "  " +
                                     AW_L10n.Text("aw_corruption_high_streak", "High streak") +
                                     ": " + corruption.HighStreakYears + "\n" +
                                     AW_L10n.Text(
                                          "aw_court_institution_effects",
                                          "Institution Effects") + ": " +
                                     institutionEffects + "\n" +
                                     AW_L10n.Text("aw_court_dominant_school", "Dominant Schools") + ": " +
                                     schools + "\n" + aristocraticGroups + "\n" +
                                     AW_L10n.Text("aw_court_direction_livelihood", "Livelihood") + " " +
                                     Percent(pSnapshot.livelihood) + "  " +
                                     AW_L10n.Text("aw_school_direction_war", "War") + " " +
                                     Percent(pSnapshot.war) + "  " +
                                     AW_L10n.Text("aw_court_direction_aggression", "Aggression") + " " +
                                     Percent(pSnapshot.aggression) + "  " +
                                     AW_L10n.Text("aw_court_direction_peace", "Peace") + " " +
                                     Percent(pSnapshot.peace) + "  " +
                                     AW_L10n.Text("aw_school_direction_order", "Order") + " " +
                                     Percent(pSnapshot.order) + "  " +
                                     AW_L10n.Text("aw_school_direction_commerce", "Commerce") + " " +
                                     Percent(pSnapshot.commerce) + "  " +
                                      AW_L10n.Text("aw_school_direction_technology", "Technology") + " " +
                                      Percent(pSnapshot.technology);
            UpdateCivilServiceExamEntry(pKingdom);
            UpdateHouseholdEntry(pKingdom);
        }

        private void UpdateHouseholdEntry(Kingdom pKingdom)
        {
            if (_householdButton == null) return;
            bool republic = RepublicGovernmentService.IsRepublic(pKingdom);
            _householdButton.gameObject.SetActive(!republic);
            if (republic) return;
            string label = AW_L10n.Text("aw_household_button", "Household");
            if (_householdText != null) _householdText.text = label;
            if (_householdTip != null)
                _householdTip.hoverAction = () => Tooltip.show(
                    _householdButton.gameObject, AW_RawTooltip.TYPE,
                    new TooltipData
                    {
                        tip_name = label,
                        tip_description = AW_L10n.Text(
                            "aw_household_button_desc",
                            "View the ruler's principal wife and consorts.")
                    });
        }

        private void UpdateCivilServiceExamEntry(Kingdom pKingdom)
        {
            if (_civilServiceExamButton == null) return;
            _civilServiceExamButton.gameObject.SetActive(true);
            bool hasNineRank = CourtService.HasNineRankSystem(pKingdom);
            bool hasExamTechnology = KingdomPolicyService.IsCompleted(
                pKingdom, PolicyNodeKind.Tech,
                CivilServiceQualificationService.TechnologyId);
            bool unlocked = hasNineRank && hasExamTechnology;
            _civilServiceExamButton.interactable = unlocked;
            string modeKey = CivilServiceExamReadModel.ModeLocalizationKey(
                pKingdom);
            string label = AW_L10n.Text(modeKey, "Examination");
            if (_civilServiceExamText != null)
                _civilServiceExamText.text = label;
            AW_UIStyle.ApplyButton(
                _civilServiceExamButton.GetComponent<Image>(),
                unlocked ? .96f : .48f);

            string description = !hasNineRank
                ? AW_L10n.Text(
                    "aw_civil_service_exam_locked_nine_rank",
                    "Requires the Nine-Rank System.")
                : !hasExamTechnology
                    ? AW_L10n.Text(
                        "aw_civil_service_exam_locked_policy",
                        "Requires Civil Service Examinations research.")
                    : AW_L10n.Text("aw_civil_service_exam_entry_desc",
                        "Open the current and historical examination rolls.");
            _civilServiceExamTip.enabled = true;
            _civilServiceExamTip.hoverAction = () => Tooltip.show(
                _civilServiceExamButton.gameObject, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = label,
                    tip_description = description
                });
        }

        private static string AristocraticGroupSummary(Kingdom pKingdom)
        {
            IReadOnlyList<CourtAristocraticGroup> groups =
                CourtAristocraticGroupService.GetCachedGroups(pKingdom);
            string label = AW_L10n.Text("aw_court_aristocratic_groups",
                "Ministerial Clans");
            if (groups.Count == 0)
                return label + ": " + AW_L10n.Text(
                    "aw_court_aristocratic_groups_none",
                    "No established ministerial clan");
            string format = AW_L10n.Text("aw_court_aristocratic_group_item",
                "{0} clan P{1}·{2}");
            return label + ": " + string.Join("  ", groups.Select(group =>
                string.Format(format, ShiDisplayName(group.ShiName),
                    group.Power, group.MemberCount)));
        }

        private static string ShiDisplayName(string pShiName)
        {
            string name = (pShiName ?? "").Trim();
            return name.EndsWith("氏", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - 1)
                : name;
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
                        if (view.NeedsPortrait) _portraitRetries.Enqueue(view);
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

        private void BuildLinks(List<CourtPyramidNodeModel> pNodes,
            Kingdom pKingdom, Color pColor, Vector2 pOffset,
            CourtPyramidCanvasBounds pBounds,
            IReadOnlyList<CustomCourtEdge> pLocalEdges = null,
            bool pLocalContext = false)
        {
            if (pNodes == null || pNodes.Count <= 1) return;
            CustomCourtTemplate snapshot = null;
            bool customGraph = pLocalEdges != null ||
                CustomCourtRuntime.TryGetSnapshot(pKingdom, out snapshot);
            if (!customGraph)
                foreach (CourtPyramidLinkSegment segment in
                         (pLocalContext
                             ? CourtPyramidRules.BuildLocalOrthogonalLinks(
                                 pNodes, CourtActorNodeView.Height)
                             : CourtPyramidRules.BuildOrthogonalLinks(pNodes,
                                 CourtActorNodeView.Height)))
                    CreateLink(segment, pOffset, pColor, pBounds);
            if (!customGraph) return;
            Color managementColor = new Color(0.22f, 0.82f, 0.94f, 1f);
            foreach (CourtPyramidLinkSegment segment in
                     CourtPyramidRules.BuildCustomManagementLinks(pNodes,
                         pLocalEdges ?? snapshot.Edges,
                         CourtActorNodeView.Height))
                CreateLink(segment, pOffset, managementColor, pBounds, 3f,
                    0.88f);
        }

        private void CreateLink(CourtPyramidLinkSegment pSegment, Vector2 pOffset, Color pColor,
            CourtPyramidCanvasBounds pBounds, float pThickness = 2f,
            float pAlpha = 0.48f)
        {
            CourtPyramidRenderedLink placement = CourtPyramidRules.PlaceLink(
                pSegment, pOffset.x, pOffset.y, pThickness);
            if (!CourtPyramidRules.IsRenderedLinkInsideCanvas(placement, pBounds.Width, pBounds.Height)) return;

            GameObject obj = AcquireLink();
            obj.transform.SetAsFirstSibling();
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(placement.CenterX, placement.CenterY);
            rect.sizeDelta = new Vector2(placement.Width, placement.Height);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;
            Image image = obj.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = new Color(pColor.r, pColor.g, pColor.b, pAlpha);
            image.raycastTarget = false;
        }

        private GameObject AcquireLink()
        {
            GameObject obj = null;
            while (_activeLinkCount < _linkPool.Count && obj == null)
                obj = _linkPool[_activeLinkCount++];
            if (obj == null)
            {
                obj = new GameObject("CourtRankLink", typeof(RectTransform), typeof(Image));
                _linkPool.Add(obj);
                _activeLinkCount = _linkPool.Count;
            }
            obj.transform.SetParent(_canvasRect, false);
            obj.SetActive(true);
            return obj;
        }

        private void HideNodesAndLinks()
        {
            _portraitRetries.Clear();
            foreach (CourtActorNodeView node in _nodePool)
                if (node != null) node.gameObject.SetActive(false);
            foreach (CourtCityGovernmentCard card in _cityCardPool)
                if (card != null) card.gameObject.SetActive(false);
            foreach (GameObject link in _linkPool)
                if (link != null) link.SetActive(false);
            if (_centralSectionLabel != null) _centralSectionLabel.gameObject.SetActive(false);
            if (_militarySectionLabel != null) _militarySectionLabel.gameObject.SetActive(false);
            if (_localSectionLabel != null) _localSectionLabel.gameObject.SetActive(false);
            if (_militarySectionDivider != null) _militarySectionDivider.gameObject.SetActive(false);
            if (_localSectionDivider != null) _localSectionDivider.gameObject.SetActive(false);
            _activeLinkCount = 0;
            RemoveOrphanedLinkChildren();
        }

        private void EnsureSectionMarkers(Transform pCanvas)
        {
            _centralSectionLabel = EnsureText(pCanvas, "CentralSectionLabel", 10, TextAnchor.UpperLeft);
            _centralSectionLabel.fontStyle = FontStyle.Bold;
            _militarySectionLabel = EnsureText(pCanvas, "MilitarySectionLabel", 10, TextAnchor.UpperLeft);
            _militarySectionLabel.fontStyle = FontStyle.Bold;
            _localSectionLabel = EnsureText(pCanvas, "LocalSectionLabel", 10, TextAnchor.UpperLeft);
            _localSectionLabel.fontStyle = FontStyle.Bold;

            _militarySectionDivider = EnsureSectionDivider(pCanvas,
                "MilitarySectionDivider");
            _localSectionDivider = EnsureSectionDivider(pCanvas,
                "LocalSectionDivider");
        }

        private void UpdateWindowTitle(Kingdom pKingdom,
            LocalCourtReadModel pLocal)
        {
            ScrollWindow scrollWindow = GetComponent<ScrollWindow>();
            if (scrollWindow?.titleText == null) return;
            bool western = CourtProfileRegistry.For(pKingdom)?.Id ==
                           CourtProfileId.Western;
            string fallback = western
                ? AW_L10n.Text("aw_court_title_western", "Western Court")
                : AW_L10n.Text("aw_court_title", "Court of the Hundred Schools");
            scrollWindow.titleText.text = pLocal == null
                ? CustomCourtRuntime.DisplayName(pKingdom, fallback)
                : pLocal.CityName + " - " + pLocal.CityTypeName;
        }

        private static Image EnsureSectionDivider(Transform pCanvas,
            string pName)
        {
            Transform existing = pCanvas.Find(pName);
            GameObject divider = existing != null ? existing.gameObject :
                new GameObject(pName, typeof(RectTransform), typeof(Image));
            if (existing == null) divider.transform.SetParent(pCanvas, false);
            Image image = divider.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.raycastTarget = false;
            return image;
        }

        private void LayoutSectionMarkers(List<CourtPyramidNodeModel> pNodes,
            CourtPyramidCanvasBounds pBounds, Vector2 pOffset, Color pColor)
        {
            bool hasCentral = pNodes.Any(p => !CourtPyramidRules.IsLocalNode(p) &&
                !CourtPyramidRules.IsMilitaryNode(p));
            bool hasMilitary = pNodes.Any(CourtPyramidRules.IsMilitaryNode);
            bool hasLocal = pNodes.Any(CourtPyramidRules.IsLocalNode);
            if (_centralSectionLabel != null)
            {
                _centralSectionLabel.gameObject.SetActive(hasCentral);
                _centralSectionLabel.text = AW_L10n.Text("aw_court_layer_central", "Central Court");
                _centralSectionLabel.color = new Color(pColor.r, pColor.g, pColor.b, 0.9f);
                LayoutCanvasText(_centralSectionLabel, 8f, -4f, Mathf.Max(1f, pBounds.Width - 16f), 18f);
                _centralSectionLabel.transform.SetAsLastSibling();
            }

            float militaryDividerY = CourtPyramidRules.MilitarySectionDividerY(
                pNodes, CourtActorNodeView.Height) + pOffset.y;
            bool showMilitaryMarker = hasCentral && hasMilitary &&
                !float.IsNaN(militaryDividerY);
            if (_militarySectionLabel != null)
            {
                _militarySectionLabel.gameObject.SetActive(showMilitaryMarker);
                _militarySectionLabel.text = AW_L10n.Text(
                    "aw_court_layer_military", "Military Bureau");
                _militarySectionLabel.color = new Color(pColor.r, pColor.g,
                    pColor.b, 0.9f);
                LayoutCanvasText(_militarySectionLabel, 8f,
                    militaryDividerY + 8f, 82f, 18f);
                _militarySectionLabel.transform.SetAsLastSibling();
            }
            LayoutSectionDivider(_militarySectionDivider,
                showMilitaryMarker, militaryDividerY, pBounds, pColor);

            float dividerY = CourtPyramidRules.LocalSectionDividerY(
                pNodes, CourtActorNodeView.Height) + pOffset.y;
            bool showLocalMarker = hasCentral && hasLocal && !float.IsNaN(dividerY);
            if (_localSectionLabel != null)
            {
                _localSectionLabel.gameObject.SetActive(showLocalMarker);
                _localSectionLabel.text = AW_L10n.Text("aw_court_layer_city", "Local Bureaus");
                _localSectionLabel.color = new Color(pColor.r, pColor.g, pColor.b, 0.9f);
                LayoutCanvasText(_localSectionLabel, 8f, dividerY + 8f, 82f, 18f);
                _localSectionLabel.transform.SetAsLastSibling();
            }
            LayoutSectionDivider(_localSectionDivider, showLocalMarker,
                dividerY, pBounds, pColor);
        }

        private static void LayoutSectionDivider(Image pDivider, bool pVisible,
            float pY, CourtPyramidCanvasBounds pBounds, Color pColor)
        {
            if (pDivider == null) return;
            pDivider.gameObject.SetActive(pVisible);
            if (!pVisible) return;
            RectTransform rect = pDivider.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(92f, pY);
            rect.sizeDelta = new Vector2(Mathf.Max(1f,
                pBounds.Width - 104f), 2f);
            pDivider.color = new Color(pColor.r, pColor.g, pColor.b, 0.52f);
            pDivider.transform.SetAsLastSibling();
        }

        private void RemoveOrphanedLinkChildren()
        {
            if (_canvasRect == null) return;
            for (int i = _canvasRect.childCount - 1; i >= 0; i--)
            {
                Transform child = _canvasRect.GetChild(i);
                if (child == null || child.name != "CourtRankLink" ||
                    _linkPool.Contains(child.gameObject)) continue;
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
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
            _canvasRect.anchoredPosition = new Vector2(CanvasLeftInset, -SummaryHeight - CanvasTopGap);
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

        private static Button EnsureButton(Transform pParent, string pName,
            string pLabel, Action pAction)
        {
            Transform existing = pParent.Find(pName);
            GameObject obj = existing != null
                ? existing.gameObject
                : new GameObject(pName, typeof(RectTransform), typeof(Image),
                    typeof(Button), typeof(TipButton));
            if (existing == null) obj.transform.SetParent(pParent, false);
            if (obj.GetComponent<TipButton>() == null)
                obj.AddComponent<TipButton>();
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => pAction?.Invoke());
            Text label = EnsureText(obj.transform, "Text", 8,
                TextAnchor.MiddleCenter);
            label.text = pLabel ?? "";
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(3f, 1f);
            label.rectTransform.offsetMax = new Vector2(-3f, -1f);
            return button;
        }

        private static void LayoutSummaryButton(Button pButton, float pX,
            float pY, float pWidth, float pHeight)
        {
            if (pButton == null) return;
            RectTransform rect = pButton.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(pX, -pY);
            rect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private void BackToKingdom()
        {
            AW_LineageWindowIds.ShowKingdom(_kingdomId);
        }

        private void OpenCivilServiceExam()
        {
            CivilServiceExamWindow.Open(_kingdomId);
        }

        private void OpenRulerHousehold()
        {
            RulerHouseholdWindow.Open(_kingdomId);
        }

        private void OpenCustomCourtWorkflow()
        {
            if (_cityId >= 0L)
                CustomCourtWorkflowWindow.Open(_kingdomId, _cityId,
                    localMode: true);
            else CustomCourtWorkflowWindow.Open(_kingdomId);
        }

        private void UpdateCustomCourtWorkflowEntry(bool localMode)
        {
            if (_customCourtWorkflowButton == null) return;
            string key = localMode
                ? "aw_custom_local_court_workflow"
                : "aw_custom_court_workflow";
            string fallback = localMode
                ? "Custom Local Government"
                : "Custom Court";
            Text label = _customCourtWorkflowButton.transform.Find("Text")
                ?.GetComponent<Text>();
            if (label != null) label.text = AW_L10n.Text(key, fallback);
            TipButton tip = _customCourtWorkflowButton.GetComponent<TipButton>()
                            ?? _customCourtWorkflowButton.gameObject
                                .AddComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(
                _customCourtWorkflowButton.gameObject, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text(key, fallback),
                    tip_description = localMode
                        ? AW_L10n.Text("aw_custom_local_court_workflow_desc",
                            "Edit this city's resolved local government template.")
                        : AW_L10n.Text("aw_custom_court_whole_preset_select",
                            "Edit the realm's custom court and local governments.")
                });
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

        private static void LayoutCanvasText(Text pText, float pX, float pY, float pWidth, float pHeight)
        {
            if (pText == null) return;
            RectTransform rect = pText.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(pX, pY);
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
                case CourtTier.EasternZhou:
                    return AW_L10n.Text("aw_court_tier_easternzhou", "Eastern Zhou Six Ministers");
                case CourtInstitutionId.WesternPrimitive:
                    return AW_L10n.Text("aw_court_tier_western_primitive", "Household Council");
                case CourtInstitutionId.WesternBureaucratic:
                case CourtInstitutionId.WesternBase:
                    return AW_L10n.Text("aw_court_tier_western_bureaucratic", "Bureaucratic Court");
                case CourtInstitutionId.WesternElective:
                    return AW_L10n.Text("aw_court_tier_western_elective", "Elective Offices");
                case CourtInstitutionId.WesternFeudalBureaucratic:
                case CourtInstitutionId.WesternFeudal:
                    return AW_L10n.Text("aw_court_tier_western_feudal_bureaucratic", "Feudal Bureaucratic Court");
                case CourtInstitutionId.WesternRoyalDirect:
                    return AW_L10n.Text("aw_court_tier_western_royal_direct", "Royal Administration");
                default:
                    return AW_L10n.Text("aw_court_button_locked", "Court Locked");
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

    }
}
