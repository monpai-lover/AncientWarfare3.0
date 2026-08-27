using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CustomCourtWorkflowWindow :
        AbstractWindow<CustomCourtWorkflowWindow>
    {
        private enum RegionalTitleTarget
        {
            Region,
            Governor,
            LocalLevel
        }

        private const float ToolbarScale = 0.8f;
        private const float ToolbarWidth = 164f;
        private const float ToolbarContentHeight = 598f;
        private const float ToolbarScrollbarWidth = 6f;
        private static long _kingdomId = -1L;
        private static long _cityId = -1L;
        private static bool _localEntryMode;
        private static readonly Vector2 DefaultSize = new Vector2(560f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RectTransform _canvasRect;
        private RectTransform _workspaceRect;
        private RectTransform _toolViewport;
        private RectTransform _toolPanel;
        private ScrollRect _toolScrollRect;
        private Scrollbar _toolScrollbar;
        private InputField _courtNameInput;
        private InputField _officeNameInput;
        private Text _regionTitleLabel;
        private InputField _regionTitleChineseInput;
        private InputField _regionTitleEnglishInput;
        private Text _governorTitleLabel;
        private InputField _governorTitleChineseInput;
        private InputField _governorTitleEnglishInput;
        private Text _localLevelTitleLabel;
        private InputField _localLevelTitleChineseInput;
        private InputField _localLevelTitleEnglishInput;
        private Button _wholePresetButton;
        private Button _backButton;
        private Text _wholePresetButtonText;
        private AWStringDropdown _importDropdown;
        private AWStringDropdown _contextDropdown;
        private AWStringDropdown _localTemplateDropdown;
        private AWStringDropdown _localDefaultDropdown;
        private AWStringDropdown _replacementDropdown;
        private Text _nameLabel;
        private Button _createLocalTemplateButton;
        private Button _duplicateLocalTemplateButton;
        private Button _deleteLocalTemplateButton;
        private string _selectedCentralImportFile = string.Empty;
        private string _selectedLocalImportFile = string.Empty;
        private string _selectedLocalTemplateId = string.Empty;
        private string _selectedWholePresetId = string.Empty;
        private string _replacementTemplateId = string.Empty;
        private bool _editingLocal;
        private long _loadedKingdomId = -1L;
        private long _loadedCityId = -1L;
        private bool _loadedLocalEntryMode;
        private readonly Dictionary<string, string> _pendingReplacements =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private Text _status;
        private CustomCourtTemplate _template;
        private CustomCourtWorkflowLayout _layout;
        private CourtWorkflowVacancyCard _edgeSource;
        private CourtWorkflowVacancyCard _edgeTarget;
        private WideWindowChrome _chrome;
        private bool _focusGraphOnNextRender;

        private sealed class CityTemplateBindingChange
        {
            internal City City;
            internal string PreviousId = string.Empty;
            internal bool PreviousManual;
        }

        public static void Open(long kingdomId)
        {
            Open(kingdomId, -1L, localMode: false);
        }

        public static void Open(long kingdomId, long cityId,
            bool localMode)
        {
            _kingdomId = kingdomId;
            _cityId = localMode ? cityId : -1L;
            _localEntryMode = localMode;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.CUSTOM_COURT_WORKFLOW);
            if (Instance != null) Instance._focusGraphOnNextRender = true;
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.CUSTOM_COURT_WORKFLOW,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            _backButton = CreateBackButton();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size => { _windowSize = size; ApplyLayout(); },
                DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable() => Refresh();

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup group in ContentTransform.GetComponents<LayoutGroup>())
                group.enabled = false;
            foreach (ContentSizeFitter fitter in
                     ContentTransform.GetComponents<ContentSizeFitter>())
                fitter.enabled = false;
            GameObject root = new GameObject("CustomCourtWorkflowRoot",
                typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();
            _canvasRect = new GameObject("CourtWorkflowCanvas",
                typeof(RectTransform), typeof(Image),
                typeof(TreeDragPanHandler)).GetComponent<RectTransform>();
            _canvasRect.SetParent(_root, false);
            _canvasRect.GetComponent<Image>().color =
                new Color(0.08f, 0.07f, 0.055f, 0.98f);
            _workspaceRect = new GameObject("CourtWorkflowWorkspace",
                typeof(RectTransform), typeof(CourtWorkflowCanvas))
                .GetComponent<RectTransform>();
            _workspaceRect.SetParent(_canvasRect, false);
            _workspaceRect.anchorMin = _workspaceRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            _workspaceRect.pivot = new Vector2(0.5f, 0.5f);
            _workspaceRect.sizeDelta = new Vector2(2000f, 1500f);

            _toolViewport = new GameObject("CourtWorkflowToolViewport",
                typeof(RectTransform), typeof(Image),
                typeof(RectMask2D), typeof(ScrollRect))
                .GetComponent<RectTransform>();
            _toolViewport.SetParent(_root, false);
            _toolViewport.GetComponent<Image>().color =
                new Color(0.12f, 0.09f, 0.06f, 0.98f);
            _toolScrollRect = _toolViewport.GetComponent<ScrollRect>();
            _toolScrollRect.viewport = _toolViewport;
            _toolScrollRect.horizontal = false;
            _toolScrollRect.vertical = true;
            _toolScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _toolScrollRect.inertia = true;
            _toolScrollRect.scrollSensitivity = 18f;
            _toolScrollbar = CreateToolbarScrollbar(_toolViewport);

            _toolPanel = new GameObject("CourtWorkflowTools",
                typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _toolPanel.SetParent(_toolViewport, false);
            _toolPanel.GetComponent<Image>().color =
                new Color(0.12f, 0.09f, 0.06f, 0.98f);
            _toolScrollRect.content = _toolPanel;
            _contextDropdown = AWStringDropdown.Create(_toolPanel,
                "CourtContext", 148f, 22f, SelectEditorContext);
            _localTemplateDropdown = AWStringDropdown.Create(_toolPanel,
                "LocalTemplate", 148f, 22f, SelectLocalTemplate);
            _localDefaultDropdown = AWStringDropdown.Create(_toolPanel,
                "LocalDefaultKind", 148f, 22f, SelectLocalDefaultKind);
            _replacementDropdown = AWStringDropdown.Create(_toolPanel,
                "LocalReplacement", 148f, 22f, SelectReplacementTemplate);
            _nameLabel = CreateText(_toolPanel, "CourtNameLabel", 9,
                TextAnchor.MiddleLeft);
            _nameLabel.text = AW_L10n.Text("aw_custom_court_name",
                "Court name");
            _courtNameInput = CreateInput(_toolPanel, "CourtNameInput");
            _wholePresetButton = CreateButton(_toolPanel, "WholeCourtPreset",
                "aw_custom_court_whole_preset", "Whole court preset",
                CycleWholePreset);
            _wholePresetButtonText = _wholePresetButton.transform.Find("Text")
                ?.GetComponent<Text>();
            AttachTooltip(_wholePresetButton,
                "aw_custom_court_whole_preset", "Whole court preset",
                "aw_custom_court_whole_preset_select",
                "Cycle through the unlocked whole-court presets");
            _regionTitleLabel = CreateText(_toolPanel, "RegionTitleLabel", 9,
                TextAnchor.MiddleLeft);
            _regionTitleLabel.text = AW_L10n.Text(
                "aw_custom_court_region_title", "Region title");
            _regionTitleChineseInput = CreateInput(_toolPanel,
                "RegionTitleChineseInput", "aw_custom_court_language_zh",
                "中");
            _regionTitleChineseInput.onEndEdit.AddListener(
                ApplyRegionTitleChinese);
            _regionTitleEnglishInput = CreateInput(_toolPanel,
                "RegionTitleEnglishInput", "aw_custom_court_language_en",
                "EN");
            _regionTitleEnglishInput.onEndEdit.AddListener(
                ApplyRegionTitleEnglish);
            _governorTitleLabel = CreateText(_toolPanel,
                "GovernorTitleLabel", 9, TextAnchor.MiddleLeft);
            _governorTitleLabel.text = AW_L10n.Text(
                "aw_custom_court_governor_title", "Governor title");
            _governorTitleChineseInput = CreateInput(_toolPanel,
                "GovernorTitleChineseInput", "aw_custom_court_language_zh",
                "中");
            _governorTitleChineseInput.onEndEdit.AddListener(
                ApplyGovernorTitleChinese);
            _governorTitleEnglishInput = CreateInput(_toolPanel,
                "GovernorTitleEnglishInput", "aw_custom_court_language_en",
                "EN");
            _governorTitleEnglishInput.onEndEdit.AddListener(
                ApplyGovernorTitleEnglish);
            _localLevelTitleLabel = CreateText(_toolPanel,
                "LocalLevelTitleLabel", 9, TextAnchor.MiddleLeft);
            _localLevelTitleLabel.text = AW_L10n.Text(
                "aw_custom_court_local_level_title", "City level");
            _localLevelTitleChineseInput = CreateInput(_toolPanel,
                "LocalLevelTitleChineseInput", "aw_custom_court_language_zh",
                "中");
            _localLevelTitleChineseInput.onEndEdit.AddListener(
                ApplyLocalLevelTitleChinese);
            _localLevelTitleEnglishInput = CreateInput(_toolPanel,
                "LocalLevelTitleEnglishInput", "aw_custom_court_language_en",
                "EN");
            _localLevelTitleEnglishInput.onEndEdit.AddListener(
                ApplyLocalLevelTitleEnglish);
            Text officeNameLabel = CreateText(_toolPanel, "OfficeNameLabel", 9,
                TextAnchor.MiddleLeft);
            officeNameLabel.text = AW_L10n.Text("aw_custom_court_office_name",
                "Office name");
            _officeNameInput = CreateInput(_toolPanel, "OfficeNameInput",
                "aw_custom_court_office_name_placeholder", "Office name");
            _officeNameInput.onEndEdit.AddListener(ApplyOfficeName);
            Button add = CreateButton(_toolPanel, "AddOffice",
                "aw_custom_court_add_office", "Add Office", AddOffice);
            Button manage = CreateButton(_toolPanel, "ManagementEdge",
                "aw_custom_court_management_edge", "Management",
                CreateManagementEdge);
            Button prerequisite = CreateButton(_toolPanel, "PrerequisiteEdge",
                "aw_custom_court_prerequisite_edge", "Prerequisite",
                CreateAppointmentPrerequisiteEdge);
            manage.GetComponent<Image>().color =
                new Color(0.08f, 0.28f, 0.34f, 1f);
            prerequisite.GetComponent<Image>().color =
                new Color(0.36f, 0.22f, 0.08f, 1f);
            AttachTooltip(manage, "aw_custom_court_management_edge",
                "Management", "aw_custom_court_management_edge_desc",
                "The first office manages the second office and controls the hierarchy shown in the court.");
            AttachTooltip(prerequisite,
                "aw_custom_court_prerequisite_edge", "Prerequisite",
                "aw_custom_court_prerequisite_edge_desc",
                "The first office must be held before appointment to the second office.");
            _createLocalTemplateButton = CreateButton(_toolPanel,
                "CreateLocalTemplate", "aw_custom_local_court_create",
                "New Local", CreateLocalTemplate);
            _duplicateLocalTemplateButton = CreateButton(_toolPanel,
                "DuplicateLocalTemplate", "aw_custom_local_court_duplicate",
                "Duplicate", DuplicateLocalTemplate);
            _deleteLocalTemplateButton = CreateButton(_toolPanel,
                "DeleteLocalTemplate", "aw_custom_local_court_delete",
                "Delete Local", DeleteLocalTemplate);
            Button save = CreateButton(_toolPanel, "Save",
                "aw_custom_court_save", "Save", SaveTemplate);
            Button export = CreateButton(_toolPanel, "Export",
                "aw_custom_court_export", "Export", ExportTemplate);
            _importDropdown = AWStringDropdown.Create(_toolPanel,
                "ImportJson", 148f, 22f, ImportTemplate);
            Button apply = CreateButton(_toolPanel, "Apply",
                "aw_custom_court_apply", "Apply", ApplyCustomCourtTemplate);
            _status = CreateText(_toolPanel, "Status", 9,
                TextAnchor.UpperLeft);
            AttachWorkflowTooltips(_contextDropdown, _localTemplateDropdown,
                _localDefaultDropdown, _replacementDropdown, _courtNameInput,
                _officeNameInput, add, _createLocalTemplateButton,
                _duplicateLocalTemplateButton, _deleteLocalTemplateButton,
                save, export, _importDropdown, apply);
            AttachTooltip(_regionTitleChineseInput.gameObject,
                "aw_custom_court_region_title", "Region title",
                "aw_custom_court_region_title_desc",
                "Name of the runtime grouping made from adjacent cities.");
            AttachTooltip(_regionTitleEnglishInput.gameObject,
                "aw_custom_court_region_title", "Region title",
                "aw_custom_court_region_title_desc",
                "Name of the runtime grouping made from adjacent cities.");
            AttachTooltip(_governorTitleChineseInput.gameObject,
                "aw_custom_court_governor_title", "Governor title",
                "aw_custom_court_governor_title_desc",
                "Title shown for the seat city's leader when acting as regional governor.");
            AttachTooltip(_governorTitleEnglishInput.gameObject,
                "aw_custom_court_governor_title", "Governor title",
                "aw_custom_court_governor_title_desc",
                "Title shown for the seat city's leader when acting as regional governor.");
            AttachTooltip(_localLevelTitleChineseInput.gameObject,
                "aw_custom_court_local_level_title", "City level",
                "aw_custom_court_local_level_title_desc",
                "Administrative level shown beside the unchanged city name.");
            AttachTooltip(_localLevelTitleEnglishInput.gameObject,
                "aw_custom_court_local_level_title", "City level",
                "aw_custom_court_local_level_title_desc",
                "Administrative level shown beside the unchanged city name.");
            Layout(_contextDropdown.RectTransform, 8f, 6f, 148f, 22f);
            Layout(_localTemplateDropdown.RectTransform, 8f, 32f, 148f, 22f);
            Layout(_localDefaultDropdown.RectTransform, 8f, 58f, 148f, 22f);
            Layout(_replacementDropdown.RectTransform, 8f, 84f, 148f, 22f);
            Layout(_nameLabel.rectTransform, 8f, 110f, 148f, 16f);
            Layout(_courtNameInput.GetComponent<RectTransform>(), 8f, 128f,
                148f, 20f);
            Layout(_wholePresetButton.GetComponent<RectTransform>(), 8f, 154f,
                148f, 22f);
            Layout(_regionTitleLabel.rectTransform, 8f, 180f, 48f, 16f);
            Layout(_regionTitleChineseInput.GetComponent<RectTransform>(),
                58f, 178f, 46f, 20f);
            Layout(_regionTitleEnglishInput.GetComponent<RectTransform>(),
                108f, 178f, 48f, 20f);
            Layout(_governorTitleLabel.rectTransform, 8f, 204f, 48f, 16f);
            Layout(_governorTitleChineseInput.GetComponent<RectTransform>(),
                58f, 202f, 46f, 20f);
            Layout(_governorTitleEnglishInput.GetComponent<RectTransform>(),
                108f, 202f, 48f, 20f);
            Layout(_localLevelTitleLabel.rectTransform, 8f, 228f, 48f, 16f);
            Layout(_localLevelTitleChineseInput.GetComponent<RectTransform>(),
                58f, 226f, 46f, 20f);
            Layout(_localLevelTitleEnglishInput.GetComponent<RectTransform>(),
                108f, 226f, 48f, 20f);
            Layout(officeNameLabel.rectTransform, 8f, 256f, 148f, 16f);
            Layout(_officeNameInput.GetComponent<RectTransform>(), 8f, 274f,
                148f, 20f);
            Layout(add.GetComponent<RectTransform>(), 8f, 300f, 148f, 22f);
            Layout(manage.GetComponent<RectTransform>(), 8f, 326f, 148f, 22f);
            Layout(prerequisite.GetComponent<RectTransform>(), 8f, 352f,
                148f, 22f);
            Layout(_createLocalTemplateButton.GetComponent<RectTransform>(),
                8f, 378f, 72f, 22f);
            Layout(_duplicateLocalTemplateButton.GetComponent<RectTransform>(),
                84f, 378f, 72f, 22f);
            Layout(_deleteLocalTemplateButton.GetComponent<RectTransform>(),
                8f, 404f, 148f, 22f);
            Layout(save.GetComponent<RectTransform>(), 8f, 430f, 148f, 22f);
            Layout(export.GetComponent<RectTransform>(), 8f, 456f, 148f, 22f);
            Layout(_importDropdown.RectTransform, 8f, 482f, 148f, 22f);
            Layout(apply.GetComponent<RectTransform>(), 8f, 508f, 148f, 22f);
        }

        private void ApplyLayout()
        {
            float contentWidth = Mathf.Max(1f, _windowSize.x - 42f);
            float viewportHeight = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null)
                background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
            {
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
                if (_backButton != null)
                    _backButton.transform.localPosition =
                        close.localPosition + new Vector3(-34f, 0f, 0f);
            }

            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.52f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow scrollWindow = GetComponent<ScrollWindow>();
            if (scrollWindow?.titleText != null)
            {
                scrollWindow.titleText.text = AW_L10n.Text(
                    _localEntryMode
                        ? "aw_custom_local_court_workflow_title"
                        : "aw_custom_court_workflow_title",
                    _localEntryMode
                        ? "Custom Local Government"
                        : "Custom Court Workflow");
                scrollWindow.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                scrollWindow.titleText.raycastTarget = false;
            }
            Transform scroll = BackgroundTransform?.Find("Scroll View");
            RectTransform scrollRect = scroll?.GetComponent<RectTransform>();
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(contentWidth,
                    viewportHeight);
                scrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect scrollComponent = scroll?.GetComponent<ScrollRect>();
            if (scrollComponent != null)
            {
                scrollComponent.horizontal = false;
                scrollComponent.vertical = false;
            }
            Transform viewport = ContentTransform?.parent;
            RectTransform viewportRect = viewport?.GetComponent<RectTransform>();
            if (viewportRect != null)
                viewportRect.sizeDelta = new Vector2(contentWidth, viewportHeight);
            if (viewport != null && viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform contentRect = ContentTransform as RectTransform;
            if (contentRect != null)
                contentRect.sizeDelta = new Vector2(contentWidth, viewportHeight);
            if (_root == null) return;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(contentWidth, viewportHeight);
            _root.anchoredPosition = new Vector2(-510f, 0f);
            _layout = CustomCourtWorkflowLayoutRules.Resolve(contentWidth,
                viewportHeight, ToolbarWidth, ToolbarScale,
                ToolbarScrollbarWidth);
            _canvasRect.anchorMin = Vector2.zero;
            _canvasRect.anchorMax = Vector2.one;
            _canvasRect.pivot = new Vector2(0.5f, 0.5f);
            _canvasRect.anchoredPosition = Vector2.zero;
            _canvasRect.sizeDelta = Vector2.zero;
            _canvasRect.offsetMin = Vector2.zero;
            _canvasRect.offsetMax = Vector2.zero;
            _toolViewport.anchorMin = new Vector2(0f, 0f);
            _toolViewport.anchorMax = new Vector2(0f, 1f);
            _toolViewport.pivot = new Vector2(0f, 0.5f);
            _toolViewport.anchoredPosition = Vector2.zero;
            _toolViewport.sizeDelta = new Vector2(
                _layout.ToolbarViewportWidth, 0f);
            _toolViewport.SetAsLastSibling();
            _toolPanel.anchorMin = _toolPanel.anchorMax =
                new Vector2(0f, 1f);
            _toolPanel.pivot = new Vector2(0f, 1f);
            _toolPanel.anchoredPosition = Vector2.zero;
            _toolPanel.sizeDelta = new Vector2(ToolbarWidth,
                ToolbarContentHeight);
            _toolPanel.localScale = Vector3.one * ToolbarScale;
            if (_toolScrollbar != null)
            {
                RectTransform scrollbarRect =
                    _toolScrollbar.GetComponent<RectTransform>();
                scrollbarRect.anchorMin = new Vector2(1f, 0f);
                scrollbarRect.anchorMax = Vector2.one;
                scrollbarRect.pivot = new Vector2(1f, 0.5f);
                scrollbarRect.anchoredPosition = Vector2.zero;
                scrollbarRect.sizeDelta = new Vector2(
                    ToolbarScrollbarWidth, 0f);
                _toolScrollbar.transform.SetAsLastSibling();
            }
            Layout(_status.rectTransform, 8f, 550f, 148f,
                Mathf.Max(1f, ToolbarContentHeight - 558f));
            _canvasRect.GetComponent<TreeDragPanHandler>().Setup(_workspaceRect,
                _canvasRect);
            _chrome?.RepositionResizeHandle();
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            bool contextChanged = _loadedKingdomId != _kingdomId ||
                                  _loadedCityId != _cityId ||
                                  _loadedLocalEntryMode != _localEntryMode;
            if (_template == null || _loadedKingdomId != _kingdomId)
            {
                Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
                _template = CustomCourtRuntime.TryGetSnapshot(kingdom,
                    out CustomCourtTemplate applied)
                    ? CustomCourtTemplateJsonCodec.Normalize(applied)
                    : NewTemplate();
                _pendingReplacements.Clear();
                _selectedWholePresetId = string.Empty;
            }
            EnsureTemplateShape();
            if (contextChanged)
            {
                _editingLocal = _localEntryMode;
                ApplyLayout();
                _edgeSource = _edgeTarget = null;
                _replacementTemplateId = string.Empty;
                if (_officeNameInput != null)
                    _officeNameInput.text = string.Empty;
                if (_localEntryMode)
                    SelectEntryCityTemplate();
                if (_toolScrollRect != null)
                    _toolScrollRect.verticalNormalizedPosition = 1f;
            }
            _loadedKingdomId = _kingdomId;
            _loadedCityId = _cityId;
            _loadedLocalEntryMode = _localEntryMode;
            RefreshContextControls();
            SyncNameInputFromContext();
            RenderCards(contextChanged || _focusGraphOnNextRender);
            _focusGraphOnNextRender = false;
            RefreshWholePresetOptions();
            RefreshImportFiles();
            SetStatus(AW_L10n.Text("aw_custom_court_ready", "Ready"));
        }

        private CustomCourtTemplate NewTemplate()
        {
            var template = new CustomCourtTemplate
            {
                Id = "custom_court_" + _kingdomId.ToString(
                    CultureInfo.InvariantCulture),
                Revision = 1,
                Name = new CustomCourtLocalizedText
                {
                    Chinese = "自定义朝廷",
                    English = "Custom Court"
                }
            };
            template.Offices.Add(new CustomCourtOffice
            {
                Id = "custom_office_1",
                Layer = CourtOfficeLayer.Central,
                Grade = 10,
                Slots = 1,
                Layout = CanvasCenterLayout()
            });
            template.LocalTemplates.Add(NewLocalTemplate("minzhou",
                "民州", CustomLocalCourtDefaultKind.CivilDefault));
            template.LocalTemplates.Add(NewLocalTemplate("junfu",
                "军府", CustomLocalCourtDefaultKind.MilitaryDefault));
            return template;
        }

        private List<CustomCourtOffice> ActiveOffices => _editingLocal
            ? ActiveLocalTemplate?.Offices
            : _template?.Offices;

        private List<CustomCourtEdge> ActiveEdges => _editingLocal
            ? ActiveLocalTemplate?.Edges
            : _template?.Edges;

        private CustomLocalCourtTemplate ActiveLocalTemplate =>
            _template?.LocalTemplates?.FirstOrDefault(template =>
                template != null && string.Equals(template.Id,
                    _selectedLocalTemplateId, StringComparison.Ordinal));

        private void EnsureTemplateShape()
        {
            _template.Offices = _template.Offices ??
                new List<CustomCourtOffice>();
            _template.Edges = _template.Edges ??
                new List<CustomCourtEdge>();
            _template.LocalTemplates = _template.LocalTemplates ??
                new List<CustomLocalCourtTemplate>();
            _template.RegionalGovernmentLayer =
                _template.RegionalGovernmentLayer ??
                new CustomCourtRegionalGovernmentLayer();
            _template.RegionalGovernmentLayer.Layout =
                _template.RegionalGovernmentLayer.Layout ??
                new CustomCourtOfficeLayout { X = 1000f, Y = 900f };
            _template.RegionalGovernmentLayer.ManagementOfficeIds =
                _template.RegionalGovernmentLayer.ManagementOfficeIds ??
                new List<string>();
            if (_template.LocalTemplates.Count == 0)
                _template.LocalTemplates.Add(NewLocalTemplate("minzhou",
                    "民州", CustomLocalCourtDefaultKind.CivilDefault));
            if (ActiveLocalTemplate == null)
                _selectedLocalTemplateId = _template.LocalTemplates
                    .FirstOrDefault()?.Id ?? string.Empty;
            foreach (CustomLocalCourtTemplate local in
                     _template.LocalTemplates.Where(local => local != null))
            {
                local.Offices = local.Offices ??
                    new List<CustomCourtOffice>();
                local.Edges = local.Edges ?? new List<CustomCourtEdge>();
            }
        }

        private CustomLocalCourtTemplate NewLocalTemplate(string pId,
            string pName, CustomLocalCourtDefaultKind pDefaultKind)
        {
            CustomLocalCourtTemplate local = pDefaultKind ==
                CustomLocalCourtDefaultKind.MilitaryDefault
                    ? CustomLocalGovernmentPresetRules.CreateMilitary(pId)
                    : CustomLocalGovernmentPresetRules.CreateCivil(pId);
            if (pDefaultKind == CustomLocalCourtDefaultKind.ManualOnly)
            {
                local.Name = new CustomCourtLocalizedText
                {
                    Chinese = pName,
                    English = pName
                };
            }
            local.DefaultKind = pDefaultKind;
            return local;
        }

        private void SelectEntryCityTemplate()
        {
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            City city;
            try { city = World.world?.cities?.get(_cityId); }
            catch { city = null; }
            if (city?.data == null || city.kingdom != kingdom ||
                !CustomCourtRuntime.TryGetLocalTemplate(kingdom, city,
                    out CustomLocalCourtTemplate local) || local == null)
                return;
            _selectedLocalTemplateId = local.Id;
        }

        private void RefreshContextControls()
        {
            _contextDropdown?.SetOptions(new[]
            {
                new AWStringDropdownOption
                {
                    Id = "central",
                    Label = AW_L10n.Text("aw_court_layer_central",
                        "Central Court")
                },
                new AWStringDropdownOption
                {
                    Id = "local",
                    Label = AW_L10n.Text("aw_court_layer_city",
                        "Local Bureaus")
                }
            }, _editingLocal ? "local" : "central",
                AW_L10n.Text("aw_custom_court_context", "Edit layer"));
            _contextDropdown?.gameObject.SetActive(!_localEntryMode);

            bool localMode = _editingLocal;
            _localTemplateDropdown?.gameObject.SetActive(localMode);
            _localDefaultDropdown?.gameObject.SetActive(localMode);
            _replacementDropdown?.gameObject.SetActive(localMode);
            _createLocalTemplateButton?.gameObject.SetActive(localMode);
            _duplicateLocalTemplateButton?.gameObject.SetActive(localMode);
            _deleteLocalTemplateButton?.gameObject.SetActive(localMode);
            _wholePresetButton?.gameObject.SetActive(!localMode);
            _regionTitleLabel?.gameObject.SetActive(true);
            _regionTitleChineseInput?.gameObject.SetActive(true);
            _regionTitleEnglishInput?.gameObject.SetActive(true);
            _governorTitleLabel?.gameObject.SetActive(true);
            _governorTitleChineseInput?.gameObject.SetActive(true);
            _governorTitleEnglishInput?.gameObject.SetActive(true);
            _localLevelTitleLabel?.gameObject.SetActive(true);
            _localLevelTitleChineseInput?.gameObject.SetActive(true);
            _localLevelTitleEnglishInput?.gameObject.SetActive(true);
            if (_nameLabel != null)
                _nameLabel.text = localMode
                    ? AW_L10n.Text("aw_custom_local_court_name",
                        "Local template name")
                    : AW_L10n.Text("aw_custom_court_name", "Court name");
            if (!localMode) return;

            AWStringDropdownOption[] templates = _template.LocalTemplates
                .Where(template => template != null)
                .Take(CustomLocalCourtTemplateRules.MaximumTemplates)
                .Select(template => new AWStringDropdownOption
                {
                    Id = template.Id,
                    Label = CustomLocalCourtTemplateRules.CityTypeName(
                        template,
                        HistoryLocalizationRules.CurrentLanguage() == "en")
                }).ToArray();
            _localTemplateDropdown?.SetOptions(templates,
                _selectedLocalTemplateId,
                AW_L10n.Text("aw_local_court_choose_template",
                    "Choose local government"));
            CustomLocalCourtTemplate selected = ActiveLocalTemplate;
            _localDefaultDropdown?.SetOptions(new[]
            {
                LocalDefaultOption(CustomLocalCourtDefaultKind.ManualOnly,
                    "aw_custom_local_court_manual", "Manual only"),
                LocalDefaultOption(CustomLocalCourtDefaultKind.CivilDefault,
                    "aw_custom_local_court_civil", "Civil default"),
                LocalDefaultOption(CustomLocalCourtDefaultKind.MilitaryDefault,
                    "aw_custom_local_court_military", "Military default")
            }, ((int)(selected?.DefaultKind ??
                CustomLocalCourtDefaultKind.ManualOnly)).ToString(
                    CultureInfo.InvariantCulture),
                AW_L10n.Text("aw_custom_local_court_default_kind",
                    "Default use"));
            AWStringDropdownOption[] replacements = templates.Where(option =>
                option.Id != _selectedLocalTemplateId).ToArray();
            if (!replacements.Any(option => option.Id ==
                    _replacementTemplateId))
                _replacementTemplateId = string.Empty;
            _replacementDropdown?.SetOptions(replacements,
                _replacementTemplateId,
                AW_L10n.Text("aw_custom_local_court_replacement",
                    "Replacement when deleting"));
        }

        private static AWStringDropdownOption LocalDefaultOption(
            CustomLocalCourtDefaultKind pKind, string pKey,
            string pFallback)
        {
            return new AWStringDropdownOption
            {
                Id = ((int)pKind).ToString(CultureInfo.InvariantCulture),
                Label = AW_L10n.Text(pKey, pFallback)
            };
        }

        private void SyncNameInputFromContext()
        {
            CustomCourtLocalizedText name = _editingLocal
                ? ActiveLocalTemplate?.Name
                : _template?.Name;
            if (_courtNameInput != null)
                _courtNameInput.text = name?.Chinese ?? name?.English ??
                    string.Empty;
            if (_template?.RegionalGovernmentLayer != null)
            {
                CustomCourtRegionalGovernmentLayer layer =
                    _template.RegionalGovernmentLayer;
                if (_regionTitleChineseInput != null)
                    _regionTitleChineseInput.text =
                        layer.RegionTitle?.Chinese ?? string.Empty;
                if (_regionTitleEnglishInput != null)
                    _regionTitleEnglishInput.text =
                        layer.RegionTitle?.English ?? string.Empty;
                if (_governorTitleChineseInput != null)
                    _governorTitleChineseInput.text =
                        layer.GovernorTitle?.Chinese ?? string.Empty;
                if (_governorTitleEnglishInput != null)
                    _governorTitleEnglishInput.text =
                        layer.GovernorTitle?.English ?? string.Empty;
                if (_localLevelTitleChineseInput != null)
                    _localLevelTitleChineseInput.text =
                        layer.LocalLevelTitle?.Chinese ?? string.Empty;
                if (_localLevelTitleEnglishInput != null)
                    _localLevelTitleEnglishInput.text =
                        layer.LocalLevelTitle?.English ?? string.Empty;
            }
        }

        private void SelectEditorContext(AWStringDropdownOption pOption)
        {
            bool local = pOption?.Id == "local";
            if (_editingLocal == local) return;
            _editingLocal = local;
            ApplyLayout();
            _edgeSource = _edgeTarget = null;
            if (_officeNameInput != null)
                _officeNameInput.text = string.Empty;
            EnsureTemplateShape();
            RefreshContextControls();
            RefreshImportFiles();
            SyncNameInputFromContext();
            RenderCards(pFocusGraph: true);
        }

        private void SelectLocalTemplate(AWStringDropdownOption pOption)
        {
            if (pOption == null || string.IsNullOrEmpty(pOption.Id)) return;
            _selectedLocalTemplateId = pOption.Id;
            _replacementTemplateId = string.Empty;
            _edgeSource = _edgeTarget = null;
            RefreshContextControls();
            SyncNameInputFromContext();
            RenderCards(pFocusGraph: true);
        }

        private void SelectLocalDefaultKind(AWStringDropdownOption pOption)
        {
            CustomLocalCourtTemplate local = ActiveLocalTemplate;
            if (local == null || !int.TryParse(pOption?.Id,
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int raw) || !Enum.IsDefined(
                    typeof(CustomLocalCourtDefaultKind), raw)) return;
            var kind = (CustomLocalCourtDefaultKind)raw;
            if (kind != CustomLocalCourtDefaultKind.ManualOnly)
                foreach (CustomLocalCourtTemplate other in
                         _template.LocalTemplates.Where(other =>
                             other != null && other != local &&
                             other.DefaultKind == kind))
                    other.DefaultKind = CustomLocalCourtDefaultKind.ManualOnly;
            local.DefaultKind = kind;
            RefreshContextControls();
        }

        private void SelectReplacementTemplate(
            AWStringDropdownOption pOption)
        {
            _replacementTemplateId = pOption?.Id ?? string.Empty;
        }

        private void CreateLocalTemplate()
        {
            EnsureTemplateShape();
            if (_template.LocalTemplates.Count >=
                CustomLocalCourtTemplateRules.MaximumTemplates)
            {
                SetStatus(AW_L10n.Text("aw_custom_local_court_limit",
                    "The local template limit has been reached."));
                return;
            }
            int number = 1;
            string id;
            do { id = "local_" + number++; }
            while (_template.LocalTemplates.Any(template => template != null &&
                       template.Id == id) ||
                   _pendingReplacements.ContainsKey(id));
            string name = AW_L10n.Text("aw_custom_local_court_default_name",
                "Local Government") + " " + (number - 1);
            _template.LocalTemplates.Add(NewLocalTemplate(id, name,
                CustomLocalCourtDefaultKind.ManualOnly));
            _selectedLocalTemplateId = id;
            _replacementTemplateId = string.Empty;
            RefreshContextControls();
            SyncNameInputFromContext();
            RenderCards(pFocusGraph: true);
        }

        private void DuplicateLocalTemplate()
        {
            CustomLocalCourtTemplate source = ActiveLocalTemplate;
            if (source == null || _template.LocalTemplates.Count >=
                CustomLocalCourtTemplateRules.MaximumTemplates) return;
            CustomCourtTemplate clonePackage =
                CustomCourtTemplateJsonCodec.Normalize(_template);
            CustomLocalCourtTemplate clone = clonePackage.LocalTemplates
                .First(template => template.Id == source.Id);
            int number = 1;
            string id;
            do { id = source.Id + "_copy_" + number++; }
            while (_template.LocalTemplates.Any(template => template != null &&
                       template.Id == id));
            CustomLocalCourtTemplateRules.RebaseOfficeIds(clone, id);
            clone.DefaultKind = CustomLocalCourtDefaultKind.ManualOnly;
            string suffix = AW_L10n.Text("aw_custom_local_court_copy",
                "Copy");
            clone.Name.Chinese = (clone.Name.Chinese ?? source.Id) + " " +
                                 suffix;
            clone.Name.English = (clone.Name.English ?? source.Id) + " " +
                                 suffix;
            _template.LocalTemplates.Add(clone);
            _selectedLocalTemplateId = id;
            _replacementTemplateId = string.Empty;
            RefreshContextControls();
            SyncNameInputFromContext();
            RenderCards(pFocusGraph: true);
        }

        private void DeleteLocalTemplate()
        {
            CustomLocalCourtTemplate local = ActiveLocalTemplate;
            if (local == null || _template.LocalTemplates.Count <= 1) return;
            int inUse = CountCitiesUsing(local.Id);
            if (!CustomLocalCourtTemplateRules.CanDeleteTemplate(local.Id,
                    _replacementTemplateId, inUse))
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_local_court_replacement_required",
                    "Choose a replacement for cities using this template."));
                return;
            }
            if (inUse > 0)
                _pendingReplacements[local.Id] = _replacementTemplateId;
            foreach (string pendingId in _pendingReplacements.Where(pair =>
                         pair.Value == local.Id).Select(pair => pair.Key)
                     .ToArray())
                _pendingReplacements[pendingId] = _replacementTemplateId;
            _template.LocalTemplates.Remove(local);
            _selectedLocalTemplateId = _replacementTemplateId;
            if (string.IsNullOrEmpty(_selectedLocalTemplateId))
                _selectedLocalTemplateId = _template.LocalTemplates[0].Id;
            _replacementTemplateId = string.Empty;
            _edgeSource = _edgeTarget = null;
            RefreshContextControls();
            SyncNameInputFromContext();
            RenderCards(pFocusGraph: true);
        }

        private int CountCitiesUsing(string pTemplateId)
        {
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null) return 0;
            int count = 0;
            try
            {
                foreach (City city in kingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    city.data.get(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                        out string id, string.Empty);
                    if (id == pTemplateId) count++;
                }
            }
            catch { return count; }
            return count + _pendingReplacements.Count(pair =>
                pair.Value == pTemplateId);
        }

        private void RenderCards(bool pFocusGraph = false)
        {
            if (_workspaceRect == null || ActiveOffices == null) return;
            foreach (Transform child in _workspaceRect)
                if (child.GetComponent<CourtWorkflowVacancyCard>() != null ||
                    child.GetComponent<CourtWorkflowEdgeView>() != null)
                    Destroy(child.gameObject);
            CourtWorkflowCanvas canvas = _workspaceRect.GetComponent<
                CourtWorkflowCanvas>();
            canvas.Clear();
            var renderedOffices = new List<CustomCourtOffice>(ActiveOffices
                .Where(office => office != null));
            CustomCourtOffice regionalLayer = _editingLocal
                ? CreateRegionalLayerOffice()
                : null;
            if (_editingLocal && regionalLayer != null)
                renderedOffices.Add(regionalLayer);
            foreach (CustomCourtOffice office in renderedOffices)
            {
                CourtWorkflowVacancyCard card = CourtWorkflowVacancyCard.Create(
                    _workspaceRect, office, SelectCard, OpenOfficeSettings,
                    DeleteOffice,
                    _ => RenderEdges());
                canvas.AddCard(card);
                RectTransform rect = card.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(CourtWorkflowVacancyCard.Width,
                    CourtWorkflowVacancyCard.Height);
                if (office.Layout == null)
                    office.Layout = new CustomCourtOfficeLayout();
                rect.anchoredPosition = new Vector2(office.Layout.X,
                    -office.Layout.Y);
                if (_editingLocal && card.IsRegionalLayerCard)
                    card.enabled = false;
            }
            RenderEdges();
            RefreshSelectionVisuals();
            if (pFocusGraph) FocusActiveGraph(canvas);
        }

        private CustomCourtOffice CreateRegionalLayerOffice()
        {
            CustomCourtRegionalGovernmentLayer layer =
                _template?.RegionalGovernmentLayer;
            if (layer == null) return null;
            CustomCourtOfficeLayout layout = layer.Layout ??
                new CustomCourtOfficeLayout { X = 1000f, Y = 900f };
            layer.Layout = layout;
            if (_editingLocal)
            {
                List<CustomCourtOffice> offices = ActiveOffices?
                    .Where(office => office?.Layout != null).ToList() ??
                    new List<CustomCourtOffice>();
                if (offices.Count > 0)
                    layout = new CustomCourtOfficeLayout
                    {
                        X = offices.Average(office => office.Layout.X),
                        Y = CustomCourtRegionalLayerLayoutRules
                            .AboveLocalOffices(offices.Select(office =>
                                office.Layout.Y), layout.Y)
                    };
            }
            return new CustomCourtOffice
            {
                Id = "regional_government_layer",
                Name = layer.GovernorTitle ?? new CustomCourtLocalizedText
                {
                    Chinese = "州牧",
                    English = "Prefectural Governor"
                },
                Layer = CourtOfficeLayer.Regional,
                Grade = 1,
                Slots = 1,
                Layout = layout
            };
        }

        private void FocusActiveGraph(CourtWorkflowCanvas pCanvas)
        {
            if (_workspaceRect == null || _canvasRect == null ||
                pCanvas?.Cards == null || pCanvas.Cards.Count == 0) return;
            _workspaceRect.localScale = Vector3.one;
            bool found = false;
            Vector2 minimum = Vector2.zero;
            Vector2 maximum = Vector2.zero;
            foreach (CourtWorkflowVacancyCard card in pCanvas.Cards)
            {
                RectTransform rect = card?.GetComponent<RectTransform>();
                if (rect == null) continue;
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 local = _canvasRect.InverseTransformPoint(corner);
                    if (!found)
                    {
                        minimum = maximum = local;
                        found = true;
                    }
                    else
                    {
                        minimum = Vector2.Min(minimum, local);
                        maximum = Vector2.Max(maximum, local);
                    }
                }
            }
            if (!found) return;
            Vector2 graphCenter = (minimum + maximum) * 0.5f;
            Vector2 visibleCenter = _canvasRect.rect.center + new Vector2(
                _layout.VisibleCanvasCenterOffsetX, 0f);
            _workspaceRect.anchoredPosition += visibleCenter - graphCenter;
        }

        private void RenderEdges()
        {
            if (_workspaceRect == null || ActiveEdges == null) return;
            foreach (Transform child in _workspaceRect)
                if (child.GetComponent<CourtWorkflowEdgeView>() != null)
                    Destroy(child.gameObject);
            CourtWorkflowCanvas canvas = _workspaceRect.GetComponent<
                CourtWorkflowCanvas>();
            foreach (CustomCourtEdge edge in RenderedEdges())
            {
                CourtWorkflowVacancyCard from = FindCard(canvas,
                    edge?.FromOfficeId);
                CourtWorkflowVacancyCard to = FindCard(canvas,
                    edge?.ToOfficeId);
                if (from == null || to == null) continue;
                GameObject viewObject = new GameObject("CourtWorkflowEdge",
                    typeof(RectTransform), typeof(Image),
                    typeof(CourtWorkflowEdgeView));
                viewObject.transform.SetParent(_workspaceRect, false);
                viewObject.transform.SetAsFirstSibling();
                Image edgeImage = viewObject.GetComponent<Image>();
                edgeImage.raycastTarget = false;
                viewObject.GetComponent<CourtWorkflowEdgeView>().Bind(edge,
                    from.GetComponent<RectTransform>(),
                    to.GetComponent<RectTransform>());
            }
        }

        private IEnumerable<CustomCourtEdge> RenderedEdges()
        {
            foreach (CustomCourtEdge edge in ActiveEdges ??
                     new List<CustomCourtEdge>())
                if (edge != null) yield return edge;
            if (_editingLocal)
            {
                CustomCourtEdge regionalRoot = EnsureRegionalLayerRootEdge();
                if (regionalRoot != null) yield return regionalRoot;
                yield break;
            }
        }

        private CustomCourtEdge EnsureRegionalLayerRootEdge()
        {
            if (!_editingLocal) return null;
            List<CustomCourtOffice> offices = (ActiveOffices ??
                    new List<CustomCourtOffice>())
                .Where(office => office != null &&
                    office.Id != "regional_government_layer")
                .ToList();
            if (offices.Count == 0) return null;
            var managedTargets = new HashSet<string>((ActiveEdges ??
                    new List<CustomCourtEdge>())
                .Where(edge => edge != null && edge.Kind ==
                    CustomCourtEdgeKind.Management)
                .Select(edge => edge.ToOfficeId), StringComparer.Ordinal);
            CustomCourtOffice root = offices.FirstOrDefault(office =>
                !managedTargets.Contains(office.Id)) ?? offices[0];
            return new CustomCourtEdge
            {
                FromOfficeId = "regional_government_layer",
                ToOfficeId = root.Id,
                Kind = CustomCourtEdgeKind.Management
            };
        }

        private static CourtWorkflowVacancyCard FindCard(
            CourtWorkflowCanvas canvas, string officeId)
        {
            foreach (CourtWorkflowVacancyCard card in canvas.Cards)
                if (card?.Office != null && string.Equals(card.Office.Id,
                    officeId, StringComparison.Ordinal)) return card;
            return null;
        }

        private void SelectCard(CourtWorkflowVacancyCard card)
        {
            if (_editingLocal && card != null && card.IsRegionalLayerCard)
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_court_regional_read_only",
                    "The regional superior is configured in the central court."));
                return;
            }
            if (_edgeSource == null) _edgeSource = card;
            else if (_edgeTarget == null && card != _edgeSource) _edgeTarget = card;
            else { _edgeSource = card; _edgeTarget = null; }
            if (_officeNameInput != null && card?.Office != null)
                _officeNameInput.text = OfficeDisplayName(card.Office);
            RefreshSelectionVisuals();
            SetStatus(_edgeTarget == null
                ? "1: " + (_edgeSource?.Office?.Id ?? "")
                : "2: " + _edgeTarget.Office.Id);
        }

        private void RefreshSelectionVisuals()
        {
            CourtWorkflowCanvas canvas = _workspaceRect?.GetComponent<
                CourtWorkflowCanvas>();
            if (canvas == null) return;
            foreach (CourtWorkflowVacancyCard card in canvas.Cards)
            {
                if (card == _edgeSource) card.SetSelectionState(1);
                else if (card == _edgeTarget) card.SetSelectionState(2);
                else card.SetSelectionState(0);
            }
        }

        public void CreateManagementEdge()
        {
            CreateEdge(CustomCourtEdgeKind.Management);
        }

        public void CreateAppointmentPrerequisiteEdge()
        {
            CreateEdge(CustomCourtEdgeKind.AppointmentPrerequisite);
        }

        private void CreateEdge(CustomCourtEdgeKind kind)
        {
            if (_edgeSource?.Office == null || _edgeTarget?.Office == null)
            {
                SetStatus(AW_L10n.Text("aw_custom_court_select_two",
                    "Select two office cards first."));
                return;
            }
            bool sourceRegional = _edgeSource.IsRegionalLayerCard;
            bool targetRegional = _edgeTarget.IsRegionalLayerCard;
            if (sourceRegional || targetRegional)
            {
                if (_editingLocal || kind != CustomCourtEdgeKind.Management ||
                    sourceRegional || !targetRegional)
                {
                    SetStatus(AW_L10n.Text(
                        "aw_custom_court_regional_management_only",
                        "The regional layer only accepts central management connections."));
                    return;
                }
                string officeId = _edgeSource.Office.Id;
                List<string> managers = _template.RegionalGovernmentLayer
                    .ManagementOfficeIds;
                if (!managers.Contains(officeId)) managers.Add(officeId);
                _edgeSource = _edgeTarget = null;
                RenderEdges();
                RefreshSelectionVisuals();
                SetStatus(AW_L10n.Text("aw_custom_court_edge_added",
                    "Connection added."));
                return;
            }
            var edge = new CustomCourtEdge
            {
                FromOfficeId = _edgeSource.Office.Id,
                ToOfficeId = _edgeTarget.Office.Id,
                Kind = kind
            };
            ActiveEdges.Add(edge);
            if (CustomCourtTemplateRules.Validate(_template) ==
                CustomCourtTemplateValidationError.Cycle)
            {
                ActiveEdges.RemoveAt(ActiveEdges.Count - 1);
                SetStatus(AW_L10n.Text("aw_custom_court_cycle",
                    "That connection would create a cycle."));
                return;
            }
            SetStatus(AW_L10n.Text("aw_custom_court_edge_added", "Connection added."));
            _edgeSource = _edgeTarget = null;
            RenderEdges();
            RefreshSelectionVisuals();
        }

        private void AddOffice()
        {
            List<CustomCourtOffice> offices = ActiveOffices;
            if (offices == null) return;
            int number = offices.Count + 1;
            string name = _officeNameInput?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
                name = AW_L10n.Text("aw_custom_court_office_default",
                    "Office") + " " + number;
            string idPrefix = _editingLocal ? "local_office_" :
                "custom_office_";
            while (offices.Any(office => office != null &&
                       office.Id == idPrefix + number)) number++;
            offices.Add(new CustomCourtOffice
            {
                Id = idPrefix + number,
                Name = new CustomCourtLocalizedText
                {
                    Chinese = name,
                    English = name
                },
                Layer = _editingLocal ? CourtOfficeLayer.City :
                    CourtOfficeLayer.Central,
                Grade = 10,
                Slots = 1,
                Layout = CanvasCenterLayout()
            });
            _edgeSource = _edgeTarget = null;
            if (_officeNameInput != null) _officeNameInput.text = string.Empty;
            RenderCards();
        }

        private CustomCourtOfficeLayout CanvasCenterLayout()
        {
            if (_canvasRect == null || _workspaceRect == null)
                return new CustomCourtOfficeLayout { X = 1000f, Y = 700f };
            Vector3 worldCenter = _canvasRect.TransformPoint(
                _canvasRect.rect.center + new Vector2(
                    _layout.VisibleCanvasCenterOffsetX, 0f));
            Vector3 localCenter = _workspaceRect.InverseTransformPoint(
                worldCenter);
            return new CustomCourtOfficeLayout
            {
                X = localCenter.x - _workspaceRect.rect.xMin,
                Y = _workspaceRect.rect.yMax - localCenter.y -
                    CourtWorkflowVacancyCard.Height * 0.5f
            };
        }

        private void ApplyOfficeName(string value)
        {
            CourtWorkflowVacancyCard card = _edgeTarget ?? _edgeSource;
            string name = value?.Trim() ?? string.Empty;
            if (card?.Office == null || card.IsRegionalLayerCard ||
                string.IsNullOrEmpty(name)) return;
            card.Office.Name = card.Office.Name ?? new CustomCourtLocalizedText();
            card.Office.Name.Chinese = name;
            card.Office.Name.English = name;
            card.RefreshText();
            SetStatus(AW_L10n.Text("aw_custom_court_office_renamed",
                "Office renamed."));
        }

        private void ApplyRegionTitleChinese(string pValue)
        {
            ApplyRegionalLocalizedTitleFromInput(pValue,
                RegionalTitleTarget.Region, pEnglish: false);
        }

        private void ApplyRegionTitleEnglish(string pValue)
        {
            ApplyRegionalLocalizedTitleFromInput(pValue,
                RegionalTitleTarget.Region, pEnglish: true);
        }

        private void ApplyGovernorTitleChinese(string pValue)
        {
            ApplyRegionalLocalizedTitleFromInput(pValue,
                RegionalTitleTarget.Governor, pEnglish: false);
        }

        private void ApplyGovernorTitleEnglish(string pValue)
        {
            ApplyRegionalLocalizedTitleFromInput(pValue,
                RegionalTitleTarget.Governor, pEnglish: true);
        }

        private void ApplyLocalLevelTitleChinese(string pValue)
        {
            ApplyRegionalLocalizedTitleFromInput(pValue,
                RegionalTitleTarget.LocalLevel, pEnglish: false);
        }

        private void ApplyLocalLevelTitleEnglish(string pValue)
        {
            ApplyRegionalLocalizedTitleFromInput(pValue,
                RegionalTitleTarget.LocalLevel, pEnglish: true);
        }

        private void ApplyRegionalLocalizedTitleFromInput(string pValue,
            RegionalTitleTarget pTarget, bool pEnglish)
        {
            if (ApplyRegionalLocalizedTitle(pValue, pTarget, pEnglish))
                RenderCards();
        }

        private bool ApplyRegionalLocalizedTitle(string pValue,
            RegionalTitleTarget pTarget, bool pEnglish)
        {
            if (_template?.RegionalGovernmentLayer == null)
                return false;
            string value = pValue?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(value)) return false;
            CustomCourtRegionalGovernmentLayer layer =
                _template.RegionalGovernmentLayer;
            CustomCourtLocalizedText text = pTarget ==
                                             RegionalTitleTarget.Governor
                ? layer.GovernorTitle
                : pTarget == RegionalTitleTarget.LocalLevel
                    ? layer.LocalLevelTitle
                    : layer.RegionTitle;
            if (text == null) text = new CustomCourtLocalizedText();
            string oldValue = pEnglish ? text.English : text.Chinese;
            if (string.Equals(oldValue, value, StringComparison.Ordinal))
                return false;
            if (pEnglish) text.English = value;
            else text.Chinese = value;
            if (pTarget == RegionalTitleTarget.Governor)
                layer.GovernorTitle = text;
            else if (pTarget == RegionalTitleTarget.LocalLevel)
                layer.LocalLevelTitle = text;
            else layer.RegionTitle = text;
            return true;
        }

        private void SyncRegionalTitlesFromInputs()
        {
            bool changed = ApplyRegionalLocalizedTitle(
                _regionTitleChineseInput?.text, RegionalTitleTarget.Region,
                pEnglish: false);
            changed |= ApplyRegionalLocalizedTitle(
                _regionTitleEnglishInput?.text, RegionalTitleTarget.Region,
                pEnglish: true);
            changed |= ApplyRegionalLocalizedTitle(
                _governorTitleChineseInput?.text,
                RegionalTitleTarget.Governor, pEnglish: false);
            changed |= ApplyRegionalLocalizedTitle(
                _governorTitleEnglishInput?.text,
                RegionalTitleTarget.Governor, pEnglish: true);
            changed |= ApplyRegionalLocalizedTitle(
                _localLevelTitleChineseInput?.text,
                RegionalTitleTarget.LocalLevel, pEnglish: false);
            changed |= ApplyRegionalLocalizedTitle(
                _localLevelTitleEnglishInput?.text,
                RegionalTitleTarget.LocalLevel, pEnglish: true);
            if (changed) RenderCards();
        }

        private static string OfficeDisplayName(CustomCourtOffice office)
        {
            if (office == null) return string.Empty;
            string value = HistoryLocalizationRules.CurrentLanguage() == "en"
                ? office.Name?.English
                : office.Name?.Chinese;
            if (string.IsNullOrWhiteSpace(value)) value = office.Name?.English;
            if (string.IsNullOrWhiteSpace(value)) value = office.Name?.Chinese;
            return string.IsNullOrWhiteSpace(value) ? office.Id : value;
        }

        private void DeleteOffice(CourtWorkflowVacancyCard card)
        {
            if (card != null && card.IsRegionalLayerCard)
            {
                SetStatus(AW_L10n.Text("aw_custom_court_regional_protected",
                    "The dynamic regional layer cannot be deleted."));
                return;
            }
            string officeId = card?.Office?.Id;
            if (string.IsNullOrEmpty(officeId)) return;
            if (_editingLocal && ActiveLocalTemplate != null)
                CustomLocalCourtTemplateRules.EnsureChiefOfficeId(
                    ActiveLocalTemplate);
            if (_editingLocal && ActiveLocalTemplate != null &&
                string.Equals(ActiveLocalTemplate.ChiefOfficeId, officeId,
                    StringComparison.Ordinal))
            {
                SetStatus(AW_L10n.Text("aw_custom_court_chief_protected",
                    "The city chief seat is fixed and cannot be deleted."));
                return;
            }
            ActiveOffices?.RemoveAll(office => office != null &&
                string.Equals(office.Id, officeId, StringComparison.Ordinal));
            ActiveEdges?.RemoveAll(edge => edge == null ||
                string.Equals(edge.FromOfficeId, officeId,
                    StringComparison.Ordinal) ||
                string.Equals(edge.ToOfficeId, officeId,
                    StringComparison.Ordinal));
            _template?.RegionalGovernmentLayer?.ManagementOfficeIds?
                .RemoveAll(id => string.Equals(id, officeId,
                    StringComparison.Ordinal));
            _edgeSource = null;
            _edgeTarget = null;
            if (_officeNameInput != null) _officeNameInput.text = string.Empty;
            RenderCards();
        }

        private void OpenOfficeSettings(CourtWorkflowVacancyCard card)
        {
            if (card?.Office == null || card.IsRegionalLayerCard ||
                _template == null) return;
            CustomCourtOfficeSettingsWindow.Open(_kingdomId, _template,
                card.Office,
                draft =>
                {
                    CustomCourtOfficeSettingsRules.CopyEditableSettings(
                        card.Office, draft);
                    card.RefreshText();
                    if (_officeNameInput != null &&
                        (card == _edgeSource || card == _edgeTarget))
                        _officeNameInput.text = OfficeDisplayName(card.Office);
                    RenderEdges();
                    RefreshSelectionVisuals();
                    SetStatus(AW_L10n.Text(
                        "aw_custom_court_office_settings_saved",
                        "Office settings saved."));
                });
        }

        private string ActiveTemplateRoot()
        {
            return _editingLocal
                ? CustomCourtTemplatePathService.LocalRootPath
                : CustomCourtTemplatePathService.CentralRootPath;
        }

        private string ActiveSelectedImportFile
        {
            get => _editingLocal
                ? _selectedLocalImportFile
                : _selectedCentralImportFile;
            set
            {
                if (_editingLocal) _selectedLocalImportFile = value ??
                    string.Empty;
                else _selectedCentralImportFile = value ?? string.Empty;
            }
        }

        private bool TryBuildActiveDocument(
            out CustomCourtTemplate pDocument)
        {
            pDocument = null;
            if (_editingLocal)
            {
                CustomLocalCourtTemplate local = ActiveLocalTemplate;
                if (local == null) return false;
                pDocument = CustomCourtTemplateDocumentRules
                    .CreateLocalDocument(local, _template?.Revision ?? 1);
                return true;
            }
            pDocument = CustomCourtTemplateDocumentRules
                .CreateCentralDocument(_template);
            return true;
        }

        private void ExportTemplate()
        {
            if (!SyncContextNameFromInput()) return;
            if (!TryBuildActiveDocument(out CustomCourtTemplate document))
            {
                SetStatus(AW_L10n.Text("aw_custom_court_invalid",
                    "Template is invalid."));
                return;
            }
            var store = new CustomCourtTemplateStore(ActiveTemplateRoot());
            CustomCourtTemplateValidationError error;
            if (store.TrySave(document, out error, out string savedPath))
            {
                ActiveSelectedImportFile = Path.GetFileName(savedPath);
                RefreshImportFiles();
                SetStatus(string.Format(CultureInfo.CurrentCulture,
                    AW_L10n.Text("aw_custom_court_exported_path",
                        "Template exported: {0}"), savedPath));
            }
            else SetStatus(AW_L10n.Text("aw_custom_court_invalid",
                "Template is invalid."));
        }

        private void SaveTemplate()
        {
            if (!SyncContextNameFromInput()) return;
            _template.Revision = Math.Max(1, _template.Revision + 1);
            if (!TryBuildActiveDocument(out CustomCourtTemplate document))
            {
                SetStatus(AW_L10n.Text("aw_custom_court_invalid",
                    "Template is invalid."));
                return;
            }
            var store = new CustomCourtTemplateStore(ActiveTemplateRoot());
            CustomCourtTemplateValidationError error;
            SetStatus(store.TrySave(document, out error)
                ? AW_L10n.Text("aw_custom_court_saved", "Court saved.")
                : AW_L10n.Text("aw_custom_court_invalid", "Template is invalid."));
        }

        private void RefreshImportFiles()
        {
            var store = new CustomCourtTemplateStore(ActiveTemplateRoot());
            string[] files = store.ListFileNames();
            var options = files.Select(file => new AWStringDropdownOption
            {
                Id = file,
                Label = Path.GetFileNameWithoutExtension(file),
                Enabled = true
            }).ToArray();
            string emptyKey = _editingLocal
                ? "aw_custom_local_court_import_no_files"
                : "aw_custom_court_import_central_no_files";
            string emptyFallback = _editingLocal
                ? "No local government JSON files"
                : "No central court JSON files";
            string selectKey = _editingLocal
                ? "aw_custom_local_court_import_select"
                : "aw_custom_court_import_central_select";
            string selectFallback = _editingLocal
                ? "Import local government JSON"
                : "Import central court JSON";
            _importDropdown?.SetOptions(options, ActiveSelectedImportFile,
                files.Length == 0
                    ? AW_L10n.Text(emptyKey, emptyFallback)
                    : AW_L10n.Text(selectKey, selectFallback));
        }

        private void RefreshWholePresetOptions()
        {
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            ICourtProfile runtimeProfile = CourtProfileRegistry.For(kingdom);
            string currentInstitution = ResolveWholePresetInstitution(
                kingdom, runtimeProfile);
            ICourtProfile profile = ResolveWholePresetProfile(kingdom,
                currentInstitution);
            if (kingdom?.data == null || profile == null)
            {
                SetWholePresetButtonState(
                    Array.Empty<CustomCourtWholePresetOption>());
                return;
            }

            SetWholePresetButtonState(CustomCourtWholePresetRules.Options(
                profile.Id, currentInstitution));
        }

        private static string ResolveWholePresetInstitution(Kingdom kingdom,
            ICourtProfile runtimeProfile)
        {
            string resolved = CourtInstitutionService.GetInstitution(kingdom);
            if (runtimeProfile != null || kingdom?.data == null)
                return resolved;
            kingdom.data.get(LineageKeys.COURT_INSTITUTION,
                out string stored, string.Empty);
            return CourtInstitutionRules.IsKnown(stored) ? stored : resolved;
        }

        private static ICourtProfile ResolveWholePresetProfile(
            Kingdom kingdom, string institutionId)
        {
            ICourtProfile runtime = CourtProfileRegistry.For(kingdom);
            CourtProfileId resolved =
                CustomCourtWholePresetRules.ResolveProfile(
                    runtime?.Id ?? CourtProfileId.None, institutionId);
            return runtime ?? CourtProfileRegistry.For(resolved);
        }

        private void SetWholePresetButtonState(
            IReadOnlyList<CustomCourtWholePresetOption> pOptions)
        {
            CustomCourtWholePresetOption selected =
                CustomCourtWholePresetRules.SelectAvailablePreset(pOptions,
                    _selectedWholePresetId);
            bool available = !string.IsNullOrEmpty(selected.InstitutionId);
            if (_wholePresetButton == null) return;
            _wholePresetButton.interactable = available;
            if (_wholePresetButtonText != null)
                _wholePresetButtonText.text = available
                    ? string.Format(CultureInfo.CurrentCulture,
                        AW_L10n.Text("aw_custom_court_whole_preset_cycle",
                            "Whole court: {0}"),
                        CourtInstitutionService.InstitutionName(
                            selected.InstitutionId))
                    : AW_L10n.Text("aw_custom_court_whole_preset_unavailable",
                        "Whole-court presets unavailable");
        }

        private void CycleWholePreset()
        {
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            ICourtProfile runtimeProfile = CourtProfileRegistry.For(kingdom);
            string currentInstitution = ResolveWholePresetInstitution(
                kingdom, runtimeProfile);
            ICourtProfile profile = ResolveWholePresetProfile(kingdom,
                currentInstitution);
            if (kingdom?.data == null || profile == null)
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_court_whole_preset_unavailable",
                    "Whole-court presets unavailable."));
                return;
            }

            CustomCourtWholePresetOption next =
                CustomCourtWholePresetRules.NextUnlockedPreset(
                    CustomCourtWholePresetRules.Options(profile.Id,
                        currentInstitution),
                    _selectedWholePresetId);
            if (string.IsNullOrEmpty(next.InstitutionId))
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_court_whole_preset_unavailable",
                    "Whole-court presets unavailable."));
                return;
            }
            if (!ApplyWholePreset(next.InstitutionId, profile, kingdom)) return;
            _selectedWholePresetId = next.InstitutionId;
            RefreshWholePresetOptions();
        }

        private bool ApplyWholePreset(string pInstitutionId,
            ICourtProfile pProfile, Kingdom pKingdom)
        {
            if (string.IsNullOrEmpty(pInstitutionId) || pProfile == null ||
                pKingdom?.data == null || !SyncContextNameFromInput())
                return false;

            CustomCourtOfficeLayout center = CanvasCenterLayout();
            bool replaced = CustomCourtWholePresetRules.TryReplace(_template,
                pProfile, pInstitutionId,
                definition => PresetOfficeName(pInstitutionId, definition),
                center.X, center.Y, out CustomCourtTemplate replacement);
            if (!replaced)
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_court_whole_preset_empty",
                    "The selected whole-court preset has no offices."));
                return false;
            }

            _template = replacement;
            EnsureTemplateShape();
            _edgeSource = null;
            _edgeTarget = null;
            if (_officeNameInput != null)
                _officeNameInput.text = string.Empty;
            RenderCards(pFocusGraph: true);
            SetStatus(string.Format(CultureInfo.CurrentCulture,
                AW_L10n.Text("aw_custom_court_whole_preset_loaded",
                    "Whole-court preset loaded: {0}"),
                CourtInstitutionService.InstitutionName(pInstitutionId)));
            return true;
        }

        private static CustomCourtLocalizedText PresetOfficeName(
            string institutionId, CourtOfficeDefinition definition)
        {
            string fallback = AW_L10n.Text(definition?.LocalizationKey,
                definition?.Id ?? string.Empty);
            string displayName = AW_L10n.Text(
                CourtInstitutionRules.OfficeLocalizationKey(institutionId,
                    definition?.Id), fallback);
            return new CustomCourtLocalizedText
            {
                Chinese = displayName,
                English = displayName
            };
        }

        private void ImportTemplate(AWStringDropdownOption option)
        {
            if (option == null || string.IsNullOrEmpty(option.Id)) return;
            var store = new CustomCourtTemplateStore(ActiveTemplateRoot());
            CustomCourtTemplate imported;
            CustomCourtTemplateValidationError error;
            if (store.TryLoadFile(option.Id, out imported, out error))
            {
                bool merged = _editingLocal
                    ? CustomCourtTemplateDocumentRules.TryApplyLocalDocument(
                        _template, imported, out CustomCourtTemplate localResult,
                        out string importedLocalId) &&
                      AcceptLocalImport(localResult, importedLocalId)
                    : CustomCourtTemplateDocumentRules.TryApplyCentralDocument(
                        _template, imported,
                        out CustomCourtTemplate centralResult) &&
                      AcceptCentralImport(centralResult);
                if (!merged)
                {
                    SetStatus(AW_L10n.Text("aw_custom_court_import_invalid",
                        "The selected JSON file is invalid."));
                    return;
                }
                _loadedKingdomId = _kingdomId;
                EnsureTemplateShape();
                if (ActiveLocalTemplate == null)
                    _selectedLocalTemplateId = _template.LocalTemplates
                        .FirstOrDefault()?.Id ?? string.Empty;
                ActiveSelectedImportFile = option.Id;
                RefreshContextControls();
                SyncNameInputFromContext();
                RenderCards(pFocusGraph: true);
                RefreshImportFiles();
                SetStatus(AW_L10n.Text("aw_custom_court_imported",
                    "Template imported."));
            }
            else SetStatus(AW_L10n.Text("aw_custom_court_import_invalid",
                "The selected JSON file is invalid."));
        }

        private bool AcceptCentralImport(CustomCourtTemplate pImported)
        {
            if (pImported == null) return false;
            _template = pImported;
            _selectedWholePresetId = string.Empty;
            return true;
        }

        private bool AcceptLocalImport(CustomCourtTemplate pImported,
            string pTemplateId)
        {
            if (pImported == null || string.IsNullOrEmpty(pTemplateId))
                return false;
            _template = pImported;
            _selectedLocalTemplateId = pTemplateId;
            _replacementTemplateId = string.Empty;
            return true;
        }

        public void ApplyCustomCourtTemplate()
        {
            if (!SyncContextNameFromInput()) return;
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (!TryStageDeletedTemplateRebindings(kingdom,
                    out List<CityTemplateBindingChange> staged))
            {
                SetStatus(AW_L10n.Text("aw_custom_court_invalid",
                    "Template is invalid."));
                return;
            }
            CustomCourtTemplateScope scope = _editingLocal
                ? CustomCourtTemplateScope.LocalGovernment
                : CustomCourtTemplateScope.CentralCourt;
            _template.Scope = scope;
            bool applied = scope == CustomCourtTemplateScope.LocalGovernment
                ? CustomCourtRuntime.TryApplyLocal(kingdom, _template,
                    new Dictionary<string, long>())
                : CustomCourtRuntime.TryApplyCentral(kingdom, _template,
                    new Dictionary<string, long>());
            if (!applied) RollbackTemplateRebindings(staged);
            else _pendingReplacements.Clear();
            SetStatus(applied
                ? AW_L10n.Text("aw_custom_court_applied", "Template applied.")
                : AW_L10n.Text("aw_custom_court_invalid", "Template is invalid."));
            if (applied) StartCoroutine(ReturnToCourtAfterApply());
        }

        private bool SyncContextNameFromInput()
        {
            string name = _courtNameInput?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                SetStatus(AW_L10n.Text("aw_custom_court_name_required",
                    "Enter a name before saving."));
                return false;
            }
            if (_editingLocal)
            {
                CustomLocalCourtTemplate local = ActiveLocalTemplate;
                if (local == null) return false;
                local.Name = local.Name ?? new CustomCourtLocalizedText();
                local.Name.Chinese = name;
                local.Name.English = name;
                RefreshContextControls();
            }
            else
            {
                _template.Name = _template.Name ??
                    new CustomCourtLocalizedText();
                _template.Name.Chinese = name;
                _template.Name.English = name;
                SyncRegionalTitlesFromInputs();
            }
            return true;
        }

        private bool TryStageDeletedTemplateRebindings(Kingdom pKingdom,
            out List<CityTemplateBindingChange> pChanges)
        {
            pChanges = new List<CityTemplateBindingChange>();
            if (pKingdom?.data == null || _pendingReplacements.Count == 0)
                return pKingdom?.data != null;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    city.data.get(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                        out string currentId, string.Empty);
                    if (!_pendingReplacements.TryGetValue(currentId,
                            out string replacementId)) continue;
                    if (!_template.LocalTemplates.Any(template =>
                            template != null && template.Id == replacementId))
                    {
                        RollbackTemplateRebindings(pChanges);
                        return false;
                    }
                    city.data.get(
                        LineageKeys.CITY_LOCAL_COURT_TEMPLATE_MANUAL,
                        out bool previousManual, false);
                    pChanges.Add(new CityTemplateBindingChange
                    {
                        City = city,
                        PreviousId = currentId,
                        PreviousManual = previousManual
                    });
                    city.data.set(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                        replacementId);
                }
                return true;
            }
            catch (Exception exception)
            {
                RollbackTemplateRebindings(pChanges);
                ModClass.LogWarning("Local court template rebind failed: " +
                                    exception.Message);
                return false;
            }
        }

        private static void RollbackTemplateRebindings(
            IEnumerable<CityTemplateBindingChange> pChanges)
        {
            foreach (CityTemplateBindingChange change in
                     (pChanges ?? Array.Empty<CityTemplateBindingChange>())
                     .Reverse())
            {
                if (change?.City?.data == null) continue;
                change.City.data.set(
                    LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                    change.PreviousId);
                change.City.data.set(
                    LineageKeys.CITY_LOCAL_COURT_TEMPLATE_MANUAL,
                    change.PreviousManual);
            }
        }

        private IEnumerator ReturnToCourtAfterApply()
        {
            yield return null;
            ReturnToCourt();
        }

        private void ReturnToCourt()
        {
            if (_localEntryMode && _cityId >= 0L)
                CourtWindow.OpenCity(_kingdomId, _cityId);
            else CourtWindow.OpenAndRefresh(_kingdomId);
        }

        private void SetStatus(string value)
        {
            if (_status != null) _status.text = value ?? string.Empty;
        }

        private static Text CreateText(Transform parent, string name, int size,
            TextAnchor alignment)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.94f, 0.86f, 0.68f, 1f);
            text.raycastTarget = false;
            return text;
        }

        private static InputField CreateInput(Transform parent, string name,
            string placeholderKey = "aw_custom_court_name_placeholder",
            string placeholderFallback = "Court name")
        {
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(InputField));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = new Color(0.14f, 0.1f, 0.06f,
                1f);
            Text value = CreateText(obj.transform, "Text", 10,
                TextAnchor.MiddleLeft);
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = new Vector2(5f, 1f);
            value.rectTransform.offsetMax = new Vector2(-5f, -1f);
            Text placeholder = CreateText(obj.transform, "Placeholder", 9,
                TextAnchor.MiddleLeft);
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(5f, 1f);
            placeholder.rectTransform.offsetMax = new Vector2(-5f, -1f);
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.text = AW_L10n.Text(placeholderKey,
                placeholderFallback);
            InputField input = obj.GetComponent<InputField>();
            input.textComponent = value;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 32;
            return input;
        }

        private static Button CreateButton(Transform parent, string name,
            string key, string fallback, UnityEngine.Events.UnityAction action)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = new Color(0.22f, 0.16f, 0.09f, 1f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(action);
            Text text = CreateText(obj.transform, "Text", 9, TextAnchor.MiddleCenter);
            text.text = AW_L10n.Text(key, fallback);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(2f, 1f);
            text.rectTransform.offsetMax = new Vector2(-2f, -1f);
            return button;
        }

        private Button CreateBackButton()
        {
            Transform parent = BackgroundTransform?.parent;
            if (parent == null) return null;
            Transform existing = parent.Find("CustomCourtBackBackground");
            GameObject obj = existing != null
                ? existing.gameObject
                : new GameObject("CustomCourtBackBackground",
                    typeof(RectTransform), typeof(Image), typeof(Button),
                    typeof(TipButton));
            if (existing == null) obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(30f, 30f);
            Image background = obj.GetComponent<Image>();
            AW_UIStyle.ApplyButton(background, 0.98f);
            background.raycastTarget = true;
            Button button = obj.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(ReturnToCourt);

            Transform iconTransform = obj.transform.Find("Icon");
            GameObject iconObject = iconTransform != null
                ? iconTransform.gameObject
                : new GameObject("Icon", typeof(RectTransform), typeof(Image));
            if (iconTransform == null)
                iconObject.transform.SetParent(obj.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(5f, 5f);
            iconRect.offsetMax = new Vector2(-5f, -5f);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(
                              "ui/icons/iconArrowMetaLeft") ??
                          SpriteTextureLoader.getSprite(
                              "ui/icons/iconArrowMetaRight") ??
                          SpriteTextureLoader.getSprite(
                              "ui/icons/iconKingdomList");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (icon.sprite != null && icon.sprite.name.IndexOf("Right",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                iconRect.localScale = new Vector3(-1f, 1f, 1f);
            AttachTooltip(obj, "aw_custom_court_back", "Back",
                "aw_custom_court_back_desc",
                "Return to the court window that opened this editor.");
            return button;
        }

        private static void AttachWorkflowTooltips(
            AWStringDropdown context, AWStringDropdown localTemplate,
            AWStringDropdown localDefault, AWStringDropdown replacement,
            InputField courtName, InputField officeName, Button add,
            Button createLocal, Button duplicateLocal, Button deleteLocal,
            Button save, Button export, AWStringDropdown import, Button apply)
        {
            AttachTooltip(context?.gameObject, "aw_custom_court_context",
                "Edit layer", "aw_custom_court_context_desc",
                "Switch between central court and local government templates.");
            AttachTooltip(localTemplate?.gameObject,
                "aw_local_court_choose_template", "Choose local government",
                "aw_custom_local_court_template_desc",
                "Choose the local-government template to edit.");
            AttachTooltip(localDefault?.gameObject,
                "aw_custom_local_court_default_kind", "Default use",
                "aw_custom_local_court_default_kind_desc",
                "Choose whether this template is assigned automatically to civil or military cities.");
            AttachTooltip(replacement?.gameObject,
                "aw_custom_local_court_replacement",
                "Replacement when deleting",
                "aw_custom_local_court_replacement_desc",
                "Choose the template used by cities after deleting this template.");
            AttachTooltip(courtName?.gameObject, "aw_custom_court_name",
                "Court name", "aw_custom_court_name_desc",
                "Set the displayed name of the active court or local-government template.");
            AttachTooltip(officeName?.gameObject,
                "aw_custom_court_office_name", "Office name",
                "aw_custom_court_office_name_desc",
                "Set the name used by a new or selected office card.");
            AttachTooltip(add?.gameObject, "aw_custom_court_add_office",
                "Add Office", "aw_custom_court_add_office_desc",
                "Create a new office card at the center of the visible canvas.");
            AttachTooltip(createLocal?.gameObject,
                "aw_custom_local_court_create", "New local template",
                "aw_custom_local_court_create_desc",
                "Create an empty local-government template.");
            AttachTooltip(duplicateLocal?.gameObject,
                "aw_custom_local_court_duplicate", "Duplicate template",
                "aw_custom_local_court_duplicate_desc",
                "Copy the selected local-government template and all its offices.");
            AttachTooltip(deleteLocal?.gameObject,
                "aw_custom_local_court_delete", "Delete local template",
                "aw_custom_local_court_delete_desc",
                "Delete the selected template after choosing a replacement when required.");
            AttachTooltip(save?.gameObject, "aw_custom_court_save", "Save",
                "aw_custom_court_save_desc",
                "Save the current template as JSON without applying it.");
            AttachTooltip(export?.gameObject, "aw_custom_court_export",
                "Export", "aw_custom_court_export_desc",
                "Export the current template to the mod CourtJson folder.");
            AttachTooltip(import?.gameObject,
                "aw_custom_court_import_select", "Import JSON",
                "aw_custom_court_import_select_desc",
                "Choose a JSON file from the mod CourtJson folder to import.");
            AttachTooltip(apply?.gameObject, "aw_custom_court_apply", "Apply",
                "aw_custom_court_apply_desc",
                "Apply this court and its local-government templates to the current kingdom.");
        }

        private Scrollbar CreateToolbarScrollbar(Transform pParent)
        {
            var barObject = new GameObject("CourtWorkflowToolbarScrollbar",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(pParent, false);
            Image track = barObject.GetComponent<Image>();
            track.color = new Color(0.08f, 0.075f, 0.065f, 0.98f);

            var slidingObject = new GameObject("Sliding Area",
                typeof(RectTransform));
            slidingObject.transform.SetParent(barObject.transform, false);
            RectTransform sliding = slidingObject.GetComponent<RectTransform>();
            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = new Vector2(1f, 1f);
            sliding.offsetMax = new Vector2(-1f, -1f);

            var handleObject = new GameObject("Handle",
                typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(sliding, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.76f, 0.61f, 0.28f, 1f);

            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            _toolScrollRect.verticalScrollbar = scrollbar;
            _toolScrollRect.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static void AttachTooltip(Button button, string titleKey,
            string titleFallback, string descriptionKey,
            string descriptionFallback)
        {
            AttachTooltip(button?.gameObject, titleKey, titleFallback,
                descriptionKey, descriptionFallback);
        }

        private static void AttachTooltip(GameObject target, string titleKey,
            string titleFallback, string descriptionKey,
            string descriptionFallback)
        {
            if (target == null) return;
            TipButton tip = target.GetComponent<TipButton>() ??
                target.AddComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(target,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = AW_L10n.Text(titleKey, titleFallback),
                    tip_description = AW_L10n.Text(descriptionKey,
                        descriptionFallback)
                });
        }

        private static void Layout(RectTransform rect, float x, float y,
            float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
