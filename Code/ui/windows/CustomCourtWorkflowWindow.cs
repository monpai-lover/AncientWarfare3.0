using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AncientWarfare3.core.court;
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
        private static readonly Vector2 DefaultSize = new Vector2(920f, 620f);
        private static readonly Vector2 MinimumSize = new Vector2(620f, 420f);
        private static readonly Vector2 MaximumSize = new Vector2(1400f, 900f);
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RectTransform _canvasRect;
        private RectTransform _toolPanel;
        private InputField _courtNameInput;
        private Text _status;
        private CustomCourtTemplate _template;
        private CourtWorkflowOfficeCard _edgeSource;
        private CourtWorkflowOfficeCard _edgeTarget;
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
            GameObject root = new GameObject("CustomCourtWorkflowRoot",
                typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();
            _canvasRect = new GameObject("CourtWorkflowCanvas",
                typeof(RectTransform), typeof(Image), typeof(CourtWorkflowCanvas),
                typeof(TreeDragPanHandler)).GetComponent<RectTransform>();
            _canvasRect.SetParent(_root, false);
            _canvasRect.GetComponent<Image>().color =
                new Color(0.035f, 0.045f, 0.06f, 0.98f);
            _toolPanel = new GameObject("CourtWorkflowTools",
                typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _toolPanel.SetParent(_root, false);
            _toolPanel.GetComponent<Image>().color =
                new Color(0.09f, 0.1f, 0.13f, 0.98f);
            Text nameLabel = CreateText(_toolPanel, "CourtNameLabel", 9,
                TextAnchor.MiddleLeft);
            nameLabel.text = AW_L10n.Text("aw_custom_court_name",
                "Court name");
            _courtNameInput = CreateInput(_toolPanel, "CourtNameInput");
            Button add = CreateButton(_toolPanel, "AddOffice",
                "aw_custom_court_add_office", "Add Office", AddOffice);
            Button manage = CreateButton(_toolPanel, "ManagementEdge",
                "aw_custom_court_management_edge", "Management",
                CreateManagementEdge);
            Button prerequisite = CreateButton(_toolPanel, "PrerequisiteEdge",
                "aw_custom_court_prerequisite_edge", "Prerequisite",
                CreateAppointmentPrerequisiteEdge);
            Button save = CreateButton(_toolPanel, "Save",
                "aw_custom_court_save", "Save", SaveTemplate);
            Button export = CreateButton(_toolPanel, "Export",
                "aw_custom_court_export", "Export", ExportTemplate);
            Button importButton = CreateButton(_toolPanel, "Import",
                "aw_custom_court_import", "Import", ImportTemplate);
            Button apply = CreateButton(_toolPanel, "Apply",
                "aw_custom_court_apply", "Apply", ApplyCustomCourtTemplate);
            _status = CreateText(_toolPanel, "Status", 9,
                TextAnchor.UpperLeft);
            Layout(nameLabel.rectTransform, 8f, 8f, 148f, 18f);
            Layout(_courtNameInput.GetComponent<RectTransform>(), 8f, 28f,
                148f, 22f);
            Layout(add.GetComponent<RectTransform>(), 8f, 58f, 148f, 24f);
            Layout(manage.GetComponent<RectTransform>(), 8f, 88f, 148f, 24f);
            Layout(prerequisite.GetComponent<RectTransform>(), 8f, 118f,
                148f, 24f);
            Layout(save.GetComponent<RectTransform>(), 8f, 148f, 148f, 24f);
            Layout(export.GetComponent<RectTransform>(), 8f, 178f, 148f, 24f);
            Layout(importButton.GetComponent<RectTransform>(), 8f, 208f,
                148f, 24f);
            Layout(apply.GetComponent<RectTransform>(), 8f, 238f, 148f, 24f);
            Layout(_status.rectTransform, 8f, 274f, 148f, 110f);
        }

        private void ApplyLayout()
        {
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null)
                background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            if (_root == null) return;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(_windowSize.x - 24f,
                _windowSize.y - 56f);
            _root.anchoredPosition = new Vector2(0f, -8f);
            _toolPanel.anchorMin = _toolPanel.anchorMax = new Vector2(1f, 1f);
            _toolPanel.pivot = new Vector2(1f, 1f);
            _toolPanel.anchoredPosition = new Vector2(-4f, -4f);
            _toolPanel.sizeDelta = new Vector2(164f,
                Mathf.Max(1f, _root.sizeDelta.y - 8f));
            _canvasRect.anchorMin = new Vector2(0f, 0f);
            _canvasRect.anchorMax = new Vector2(1f, 1f);
            _canvasRect.offsetMin = new Vector2(4f, 4f);
            _canvasRect.offsetMax = new Vector2(-172f, -4f);
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
                Layout = new CustomCourtOfficeLayout { X = 80f, Y = 80f }
            });
            return template;
        }

        private void RenderCards()
        {
            if (_canvasRect == null || _template?.Offices == null) return;
            foreach (Transform child in _canvasRect)
                if (child.GetComponent<CourtWorkflowOfficeCard>() != null ||
                    child.GetComponent<CourtWorkflowEdgeView>() != null)
                    Destroy(child.gameObject);
            CourtWorkflowCanvas canvas = _canvasRect.GetComponent<
                CourtWorkflowCanvas>();
            canvas.Clear();
            foreach (CustomCourtOffice office in _template.Offices)
            {
                CourtWorkflowOfficeCard card = CourtWorkflowOfficeCard.Create(
                    _canvasRect, office, SelectCard, DeleteOffice,
                    _ => RenderEdges());
                canvas.AddCard(card);
                RectTransform rect = card.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(148f, 54f);
                if (office.Layout == null)
                    office.Layout = new CustomCourtOfficeLayout();
                rect.anchoredPosition = new Vector2(office.Layout.X,
                    -office.Layout.Y);
            }
            RenderEdges();
        }

        private void RenderEdges()
        {
            if (_canvasRect == null || _template?.Edges == null) return;
            foreach (Transform child in _canvasRect)
                if (child.GetComponent<CourtWorkflowEdgeView>() != null)
                    Destroy(child.gameObject);
            CourtWorkflowCanvas canvas = _canvasRect.GetComponent<
                CourtWorkflowCanvas>();
            foreach (CustomCourtEdge edge in _template.Edges)
            {
                CourtWorkflowOfficeCard from = FindCard(canvas,
                    edge?.FromOfficeId);
                CourtWorkflowOfficeCard to = FindCard(canvas,
                    edge?.ToOfficeId);
                if (from == null || to == null) continue;
                GameObject viewObject = new GameObject("CourtWorkflowEdge",
                    typeof(RectTransform), typeof(Image),
                    typeof(CourtWorkflowEdgeView));
                viewObject.transform.SetParent(_canvasRect, false);
                viewObject.transform.SetAsFirstSibling();
                viewObject.GetComponent<CourtWorkflowEdgeView>().Bind(edge,
                    from.GetComponent<RectTransform>(),
                    to.GetComponent<RectTransform>());
            }
        }

        private static CourtWorkflowOfficeCard FindCard(
            CourtWorkflowCanvas canvas, string officeId)
        {
            foreach (CourtWorkflowOfficeCard card in canvas.Cards)
                if (card?.Office != null && string.Equals(card.Office.Id,
                    officeId, StringComparison.Ordinal)) return card;
            return null;
        }

        private void SelectCard(CourtWorkflowOfficeCard card)
        {
            if (_edgeSource == null) _edgeSource = card;
            else if (_edgeTarget == null && card != _edgeSource) _edgeTarget = card;
            else { _edgeSource = card; _edgeTarget = null; }
            SetStatus(_edgeTarget == null
                ? "1: " + (_edgeSource?.Office?.Id ?? "")
                : "2: " + _edgeTarget.Office.Id);
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
        }

        private void AddOffice()
        {
            int number = _template.Offices.Count + 1;
            _template.Offices.Add(new CustomCourtOffice
            {
                Id = "custom_office_" + number,
                Layer = CourtOfficeLayer.Central,
                Grade = 10,
                Slots = 1,
                Layout = new CustomCourtOfficeLayout
                {
                    X = 80f + number * 24f, Y = 80f + number * 24f
                }
            });
            RenderCards();
        }

        private void DeleteOffice(CourtWorkflowOfficeCard card)
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
            RenderCards();
        }

        private string TemplateRoot()
        {
            return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "WorldBox",
                "AncientWarfare3.0", "court-templates");
        }

        private void ExportTemplate()
        {
            var store = new CustomCourtTemplateStore(TemplateRoot());
            CustomCourtTemplateValidationError error;
            SetStatus(store.TrySave(_template, out error)
                ? AW_L10n.Text("aw_custom_court_exported", "Template exported.")
                : AW_L10n.Text("aw_custom_court_invalid", "Template is invalid."));
        }

        private void SaveTemplate()
        {
            string name = _courtNameInput?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                SetStatus(AW_L10n.Text("aw_custom_court_name_required",
                    "Enter a court name before saving."));
                return;
            }
            _template.Name = _template.Name ?? new CustomCourtLocalizedText();
            _template.Name.Chinese = name;
            _template.Name.English = name;
            _template.Revision = Math.Max(1, _template.Revision + 1);
            var store = new CustomCourtTemplateStore(TemplateRoot());
            CustomCourtTemplateValidationError error;
            SetStatus(store.TrySave(_template, out error)
                ? AW_L10n.Text("aw_custom_court_saved", "Court saved.")
                : AW_L10n.Text("aw_custom_court_invalid", "Template is invalid."));
        }

        private void ImportTemplate()
        {
            var store = new CustomCourtTemplateStore(TemplateRoot());
            CustomCourtTemplate imported;
            CustomCourtTemplateValidationError error;
            if (store.TryLoad(_template.Id, out imported, out error))
            {
                _template = imported;
                if (_courtNameInput != null)
                    _courtNameInput.text = _template.Name?.Chinese ??
                        _template.Name?.English ?? string.Empty;
                RenderCards();
                SetStatus(AW_L10n.Text("aw_custom_court_imported", "Template imported."));
            }
            else SetStatus(AW_L10n.Text("aw_custom_court_not_found", "Template not found."));
        }

        public void ApplyCustomCourtTemplate()
        {
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            bool applied = CustomCourtRuntime.TryApply(kingdom, _template,
                new Dictionary<string, long>());
            SetStatus(applied
                ? AW_L10n.Text("aw_custom_court_applied", "Template applied.")
                : AW_L10n.Text("aw_custom_court_invalid", "Template is invalid."));
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
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        private static InputField CreateInput(Transform parent, string name)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(InputField));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = new Color(0.14f, 0.16f, 0.2f,
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
            placeholder.text = AW_L10n.Text("aw_custom_court_name_placeholder",
                "Court name");
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
                typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = new Color(0.18f, 0.2f, 0.25f, 1f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(action);
            Text text = CreateText(obj.transform, "Text", 9, TextAnchor.MiddleCenter);
            text.text = AW_L10n.Text(key, fallback);
            return button;
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
