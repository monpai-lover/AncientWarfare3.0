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
        private static long _kingdomId = -1L;
        private static readonly Vector2 DefaultSize = new Vector2(560f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RectTransform _canvasRect;
        private RectTransform _workspaceRect;
        private RectTransform _toolPanel;
        private InputField _courtNameInput;
        private InputField _officeNameInput;
        private AWStringDropdown _importDropdown;
        private string _selectedImportFile = string.Empty;
        private Text _status;
        private CustomCourtTemplate _template;
        private CourtWorkflowVacancyCard _edgeSource;
        private CourtWorkflowVacancyCard _edgeTarget;
        private WideWindowChrome _chrome;

        public static void Open(long kingdomId)
        {
            _kingdomId = kingdomId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.CUSTOM_COURT_WORKFLOW);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.CUSTOM_COURT_WORKFLOW,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
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
            _toolPanel = new GameObject("CourtWorkflowTools",
                typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _toolPanel.SetParent(_root, false);
            _toolPanel.GetComponent<Image>().color =
                new Color(0.12f, 0.09f, 0.06f, 0.98f);
            Text nameLabel = CreateText(_toolPanel, "CourtNameLabel", 9,
                TextAnchor.MiddleLeft);
            nameLabel.text = AW_L10n.Text("aw_custom_court_name",
                "Court name");
            _courtNameInput = CreateInput(_toolPanel, "CourtNameInput");
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
            Layout(nameLabel.rectTransform, 8f, 6f, 148f, 16f);
            Layout(_courtNameInput.GetComponent<RectTransform>(), 8f, 24f,
                148f, 20f);
            Layout(officeNameLabel.rectTransform, 8f, 50f, 148f, 16f);
            Layout(_officeNameInput.GetComponent<RectTransform>(), 8f, 68f,
                148f, 20f);
            Layout(add.GetComponent<RectTransform>(), 8f, 94f, 148f, 22f);
            Layout(manage.GetComponent<RectTransform>(), 8f, 120f, 148f, 22f);
            Layout(prerequisite.GetComponent<RectTransform>(), 8f, 146f,
                148f, 22f);
            Layout(save.GetComponent<RectTransform>(), 8f, 172f, 148f, 22f);
            Layout(export.GetComponent<RectTransform>(), 8f, 198f, 148f, 22f);
            Layout(_importDropdown.RectTransform, 8f, 224f, 148f, 22f);
            Layout(apply.GetComponent<RectTransform>(), 8f, 250f, 148f, 22f);
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
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);

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
                    "aw_custom_court_workflow_title", "Custom Court Workflow");
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
            _root.anchoredPosition = new Vector2(0f, -8f);
            _toolPanel.anchorMin = _toolPanel.anchorMax = new Vector2(1f, 1f);
            _toolPanel.pivot = new Vector2(1f, 1f);
            _toolPanel.anchoredPosition = new Vector2(-864f, -4f);
            _toolPanel.sizeDelta = new Vector2(164f,
                Mathf.Max(1f, viewportHeight - 8f));
            _toolPanel.SetAsLastSibling();
            Layout(_status.rectTransform, 8f, 278f, 148f,
                Mathf.Max(1f, _toolPanel.sizeDelta.y - 286f));
            _canvasRect.anchorMin = _canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            _canvasRect.pivot = new Vector2(0.5f, 0.5f);
            _canvasRect.sizeDelta = new Vector2(contentWidth,
                viewportHeight);
            _canvasRect.anchoredPosition = new Vector2(-480f, 0f);
            _canvasRect.GetComponent<TreeDragPanHandler>().Setup(_workspaceRect,
                _canvasRect);
            _chrome?.RepositionResizeHandle();
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            if (_template == null) _template = NewTemplate();
            if (_courtNameInput != null)
                _courtNameInput.text = _template.Name?.Chinese ??
                    _template.Name?.English ?? string.Empty;
            RenderCards();
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
            return template;
        }

        private void RenderCards()
        {
            if (_workspaceRect == null || _template?.Offices == null) return;
            foreach (Transform child in _workspaceRect)
                if (child.GetComponent<CourtWorkflowVacancyCard>() != null ||
                    child.GetComponent<CourtWorkflowEdgeView>() != null)
                    Destroy(child.gameObject);
            CourtWorkflowCanvas canvas = _workspaceRect.GetComponent<
                CourtWorkflowCanvas>();
            canvas.Clear();
            foreach (CustomCourtOffice office in _template.Offices)
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
            }
            RenderEdges();
            RefreshSelectionVisuals();
        }

        private void RenderEdges()
        {
            if (_workspaceRect == null || _template?.Edges == null) return;
            foreach (Transform child in _workspaceRect)
                if (child.GetComponent<CourtWorkflowEdgeView>() != null)
                    Destroy(child.gameObject);
            CourtWorkflowCanvas canvas = _workspaceRect.GetComponent<
                CourtWorkflowCanvas>();
            foreach (CustomCourtEdge edge in _template.Edges)
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
            var edge = new CustomCourtEdge
            {
                FromOfficeId = _edgeSource.Office.Id,
                ToOfficeId = _edgeTarget.Office.Id,
                Kind = kind
            };
            _template.Edges.Add(edge);
            if (CustomCourtTemplateRules.Validate(_template) ==
                CustomCourtTemplateValidationError.Cycle)
            {
                _template.Edges.RemoveAt(_template.Edges.Count - 1);
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
            int number = _template.Offices.Count + 1;
            string name = _officeNameInput?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
                name = AW_L10n.Text("aw_custom_court_office_default",
                    "Office") + " " + number;
            _template.Offices.Add(new CustomCourtOffice
            {
                Id = "custom_office_" + number,
                Name = new CustomCourtLocalizedText
                {
                    Chinese = name,
                    English = name
                },
                Layer = CourtOfficeLayer.Central,
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
                _canvasRect.rect.center);
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
            if (card?.Office == null || string.IsNullOrEmpty(name)) return;
            card.Office.Name = card.Office.Name ?? new CustomCourtLocalizedText();
            card.Office.Name.Chinese = name;
            card.Office.Name.English = name;
            card.RefreshText();
            SetStatus(AW_L10n.Text("aw_custom_court_office_renamed",
                "Office renamed."));
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
            string officeId = card?.Office?.Id;
            if (string.IsNullOrEmpty(officeId)) return;
            _template.Offices.RemoveAll(office => office != null &&
                string.Equals(office.Id, officeId, StringComparison.Ordinal));
            _template.Edges.RemoveAll(edge => edge == null ||
                string.Equals(edge.FromOfficeId, officeId,
                    StringComparison.Ordinal) ||
                string.Equals(edge.ToOfficeId, officeId,
                    StringComparison.Ordinal));
            _edgeSource = null;
            _edgeTarget = null;
            if (_officeNameInput != null) _officeNameInput.text = string.Empty;
            RenderCards();
        }

        private void OpenOfficeSettings(CourtWorkflowVacancyCard card)
        {
            if (card?.Office == null || _template == null) return;
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

        private string TemplateRoot()
        {
            return CustomCourtTemplatePathService.RootPath;
        }

        private void ExportTemplate()
        {
            if (!SyncCourtNameFromInput()) return;
            var store = new CustomCourtTemplateStore(TemplateRoot());
            CustomCourtTemplateValidationError error;
            if (store.TrySave(_template, out error, out string savedPath))
            {
                _selectedImportFile = Path.GetFileName(savedPath);
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
            if (!SyncCourtNameFromInput()) return;
            _template.Revision = Math.Max(1, _template.Revision + 1);
            var store = new CustomCourtTemplateStore(TemplateRoot());
            CustomCourtTemplateValidationError error;
            SetStatus(store.TrySave(_template, out error)
                ? AW_L10n.Text("aw_custom_court_saved", "Court saved.")
                : AW_L10n.Text("aw_custom_court_invalid", "Template is invalid."));
        }

        private void RefreshImportFiles()
        {
            var store = new CustomCourtTemplateStore(TemplateRoot());
            string[] files = store.ListFileNames();
            var options = files.Select(file => new AWStringDropdownOption
            {
                Id = file,
                Label = Path.GetFileNameWithoutExtension(file),
                Enabled = true
            }).ToArray();
            _importDropdown?.SetOptions(options, _selectedImportFile,
                files.Length == 0
                    ? AW_L10n.Text("aw_custom_court_import_no_files",
                        "No JSON files")
                    : AW_L10n.Text("aw_custom_court_import_select",
                        "Import JSON"));
        }

        private void ImportTemplate(AWStringDropdownOption option)
        {
            if (option == null || string.IsNullOrEmpty(option.Id)) return;
            var store = new CustomCourtTemplateStore(TemplateRoot());
            CustomCourtTemplate imported;
            CustomCourtTemplateValidationError error;
            if (store.TryLoadFile(option.Id, out imported, out error))
            {
                _template = imported;
                _selectedImportFile = option.Id;
                if (_courtNameInput != null)
                    _courtNameInput.text = _template.Name?.Chinese ??
                        _template.Name?.English ?? string.Empty;
                RenderCards();
                RefreshImportFiles();
                SetStatus(AW_L10n.Text("aw_custom_court_imported",
                    "Template imported."));
            }
            else SetStatus(AW_L10n.Text("aw_custom_court_import_invalid",
                "The selected JSON file is invalid."));
        }

        public void ApplyCustomCourtTemplate()
        {
            if (!SyncCourtNameFromInput()) return;
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            bool applied = CustomCourtRuntime.TryApply(kingdom, _template,
                new Dictionary<string, long>());
            SetStatus(applied
                ? AW_L10n.Text("aw_custom_court_applied", "Template applied.")
                : AW_L10n.Text("aw_custom_court_invalid", "Template is invalid."));
            if (applied) StartCoroutine(ReturnToCourtAfterApply());
        }

        private bool SyncCourtNameFromInput()
        {
            string name = _courtNameInput?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                SetStatus(AW_L10n.Text("aw_custom_court_name_required",
                    "Enter a court name before saving."));
                return false;
            }
            _template.Name = _template.Name ?? new CustomCourtLocalizedText();
            _template.Name.Chinese = name;
            _template.Name.English = name;
            return true;
        }

        private IEnumerator ReturnToCourtAfterApply()
        {
            yield return null;
            CourtWindow.OpenAndRefresh(_kingdomId);
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

        private static void AttachTooltip(Button button, string titleKey,
            string titleFallback, string descriptionKey,
            string descriptionFallback)
        {
            if (button == null) return;
            TipButton tip = button.GetComponent<TipButton>() ??
                button.gameObject.AddComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(button.gameObject,
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
