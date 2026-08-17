using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CustomCourtOfficeSettingsWindow :
        AbstractWindow<CustomCourtOfficeSettingsWindow>
    {
        private sealed class EffectRow
        {
            public CustomCourtEffectId Id;
            public bool Enabled;
            public CustomCourtEffectScope Scope;
            public CustomCourtEffectMode Mode;
            public Button EnabledButton;
            public Text EnabledText;
            public Button ScopeButton;
            public Text ScopeText;
            public Button ModeButton;
            public Text ModeText;
            public InputField ValueInput;
        }

        private static readonly Vector2 DefaultSize = new Vector2(558f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(558f, 360f);
        private static readonly Vector2 MaximumSize = new Vector2(738f, 504f);
        private static CustomCourtTemplate _template;
        private static CustomCourtOffice _source;
        private static Action<CustomCourtOffice> _confirmed;

        private readonly List<EffectRow> _effectRows = new List<EffectRow>();
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RectTransform _basePanel;
        private RectTransform _effectsPanel;
        private Button _baseTab;
        private Button _effectsTab;
        private Text _baseTabText;
        private Text _effectsTabText;
        private InputField _nameInput;
        private InputField _gradeInput;
        private InputField _slotsInput;
        private InputField _minimumRankInput;
        private InputField _traitInput;
        private Button _layerButton;
        private Text _layerText;
        private Button _militaryButton;
        private Text _militaryText;
        private Button _preferredSchoolButton;
        private Text _preferredSchoolText;
        private Button _requiredSchoolButton;
        private Text _requiredSchoolText;
        private Button _requiredOfficeButton;
        private Text _requiredOfficeText;
        private Text _status;
        private Button _confirmButton;
        private Button _cancelButton;
        private CustomCourtOffice _draft;
        private WideWindowChrome _chrome;
        private bool _showEffects;

        public static void Open(CustomCourtTemplate template,
            CustomCourtOffice office, Action<CustomCourtOffice> confirmed)
        {
            _template = template;
            _source = office;
            _confirmed = confirmed;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.CUSTOM_COURT_OFFICE_SETTINGS);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.CUSTOM_COURT_OFFICE_SETTINGS,
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

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) CancelOfficeSettings();
        }

        private void Refresh()
        {
            EnsureUi();
            _draft = CustomCourtOfficeSettingsRules.CloneOffice(_source);
            if (_draft == null)
            {
                CancelOfficeSettings();
                return;
            }
            BindDraft();
            ShowTab(false);
            ApplyLayout();
            SetStatus(string.Empty);
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup group in
                     ContentTransform.GetComponents<LayoutGroup>())
                group.enabled = false;
            foreach (ContentSizeFitter fitter in
                     ContentTransform.GetComponents<ContentSizeFitter>())
                fitter.enabled = false;

            _root = new GameObject("CustomCourtOfficeSettingsRoot",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(ContentTransform, false);
            _baseTab = CreateButton(_root, "BaseAndRequirementsTab",
                out _baseTabText, () => ShowTab(false));
            _effectsTab = CreateButton(_root, "FunctionalEffectsTab",
                out _effectsTabText, () => ShowTab(true));
            AttachTooltip(_baseTab.gameObject,
                "aw_custom_court_settings_base_tab", "Base and appointment",
                "aw_custom_court_settings_base_tab_desc",
                "Edit office identity appointment rules and eligibility.");
            AttachTooltip(_effectsTab.gameObject,
                "aw_custom_court_settings_effects_tab", "Functional effects",
                "aw_custom_court_settings_effects_tab_desc",
                "Configure the live bonuses granted by an occupied office.");

            _basePanel = CreatePanel(_root, "BaseAndRequirementsPanel");
            _effectsPanel = CreatePanel(_root, "FunctionalEffectsPanel");
            BuildBasePanel();
            BuildEffectsPanel();

            _status = CreateText(_root, "Status", 9,
                TextAnchor.MiddleLeft, new Color(1f, 0.62f, 0.38f, 1f));
            _confirmButton = CreateButton(_root, "ConfirmOfficeSettings",
                out Text confirmText, ConfirmOfficeSettings);
            confirmText.text = AW_L10n.Text("aw_custom_court_settings_confirm",
                "Confirm");
            AttachTooltip(_confirmButton.gameObject,
                "aw_custom_court_settings_confirm", "Confirm",
                "aw_custom_court_settings_confirm_desc",
                "Validate these settings and return them to the court editor.");
            _cancelButton = CreateButton(_root, "CancelOfficeSettings",
                out Text cancelText, CancelOfficeSettings);
            cancelText.text = AW_L10n.Text("aw_custom_court_settings_cancel",
                "Cancel");
            AttachTooltip(_cancelButton.gameObject,
                "aw_custom_court_settings_cancel", "Cancel",
                "aw_custom_court_settings_cancel_desc",
                "Discard changes made in this window.");
        }

        private void BuildBasePanel()
        {
            _nameInput = AddInputField(_basePanel, "OfficeName", 0, 0,
                "aw_custom_court_office_name", "Office name",
                "aw_custom_court_office_name_desc",
                "The name displayed for this office in the court.");
            _layerButton = AddCycleField(_basePanel, "Layer", 0, 1,
                "aw_custom_court_settings_layer", "Layer",
                "aw_custom_court_settings_layer_desc",
                "The court section and hierarchy layer containing this office.",
                CycleLayer, out _layerText);
            _gradeInput = AddInputField(_basePanel, "Grade", 0, 2,
                "aw_custom_court_settings_grade", "Grade",
                "aw_custom_court_settings_grade_desc",
                "The formal grade used to rank this office against others.");
            _slotsInput = AddInputField(_basePanel, "Slots", 0, 3,
                "aw_custom_court_settings_slots", "Slots",
                "aw_custom_court_settings_slots_desc",
                "The number of officials who may hold this office at once.");
            _militaryButton = AddCycleField(_basePanel, "MilitaryCapable", 0,
                4, "aw_custom_court_settings_military", "Military capable",
                "aw_custom_court_settings_military_desc",
                "Whether holders of this office may perform military duties.",
                ToggleMilitary, out _militaryText);

            _preferredSchoolButton = AddCycleField(_basePanel,
                "PreferredSchool", 1, 0,
                "aw_custom_court_settings_preferred_school",
                "Preferred school",
                "aw_custom_court_settings_preferred_school_desc",
                "Candidates from this school receive appointment preference.",
                CyclePreferredSchool,
                out _preferredSchoolText);
            _minimumRankInput = AddInputField(_basePanel, "MinimumRank", 1, 1,
                "aw_custom_court_settings_minimum_rank", "Minimum rank",
                "aw_custom_court_settings_minimum_rank_desc",
                "The lowest career rank eligible for appointment.");
            _requiredSchoolButton = AddCycleField(_basePanel,
                "RequiredSchool", 1, 2,
                "aw_custom_court_settings_required_school", "Required school",
                "aw_custom_court_settings_required_school_desc",
                "Only candidates belonging to this school may be appointed.",
                CycleRequiredSchool, out _requiredSchoolText);
            _traitInput = AddInputField(_basePanel, "RequiredTrait", 1, 3,
                "aw_custom_court_settings_required_trait",
                "Required trait ID",
                "aw_custom_court_settings_required_trait_desc",
                "Only candidates with this exact trait ID may be appointed.");
            _requiredOfficeButton = AddCycleField(_basePanel,
                "RequiredOffice", 1, 4,
                "aw_custom_court_settings_required_office", "Required office",
                "aw_custom_court_settings_required_office_desc",
                "The candidate must already hold this prerequisite office.",
                CycleRequiredOffice, out _requiredOfficeText);
        }

        private void BuildEffectsPanel()
        {
            string[] headerKeys =
            {
                "aw_custom_court_settings_effect",
                "aw_custom_court_settings_enabled",
                "aw_custom_court_settings_scope",
                "aw_custom_court_settings_mode",
                "aw_custom_court_settings_value"
            };
            string[] fallbacks = { "Effect", "Enabled", "Scope", "Mode", "Value" };
            string[] descriptionKeys =
            {
                "aw_custom_court_settings_effect_desc",
                "aw_custom_court_settings_enabled_desc",
                "aw_custom_court_settings_scope_desc",
                "aw_custom_court_settings_mode_desc",
                "aw_custom_court_settings_value_desc"
            };
            string[] descriptionFallbacks =
            {
                "The office bonus being configured.",
                "Enable or disable this office bonus.",
                "Choose which part of the realm receives the bonus.",
                "Choose flat percentage or multiplier calculation.",
                "Enter the magnitude used by the selected calculation mode."
            };
            float[] x = { 8f, 126f, 194f, 304f, 414f };
            float[] widths = { 112f, 62f, 104f, 104f, 82f };
            for (int i = 0; i < headerKeys.Length; i++)
            {
                Text header = CreateText(_effectsPanel, "Header" + i, 8,
                    TextAnchor.MiddleCenter,
                    new Color(0.86f, 0.78f, 0.62f, 1f));
                header.text = AW_L10n.Text(headerKeys[i], fallbacks[i]);
                AttachTooltip(header.gameObject, headerKeys[i], fallbacks[i],
                    descriptionKeys[i], descriptionFallbacks[i]);
                SetRect(header.rectTransform, x[i], 5f, widths[i], 18f);
            }

            foreach (CustomCourtEffectId id in Enum.GetValues(
                         typeof(CustomCourtEffectId)))
                _effectRows.Add(CreateEffectRow(id, _effectRows.Count));
        }

        private EffectRow CreateEffectRow(CustomCourtEffectId id, int index)
        {
            var row = new EffectRow { Id = id };
            float y = 25f + index * 37f;
            GameObject background = new GameObject("EffectRow_" + id,
                typeof(RectTransform), typeof(Image), typeof(TipButton));
            background.transform.SetParent(_effectsPanel, false);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            SetRect(backgroundRect, 6f, y, 488f, 32f);
            AW_UIStyle.ApplyPanel(background.GetComponent<Image>(), 0.82f);
            TipButton tip = background.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(background,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = EffectName(id),
                    tip_description = EffectDescription(id)
                });

            Text name = CreateText(background.transform, "Name", 9,
                TextAnchor.MiddleLeft, Color.white);
            name.text = EffectName(id);
            SetRect(name.rectTransform, 4f, 7f, 108f, 22f);
            row.EnabledButton = CreateButton(background.transform, "Enabled",
                out row.EnabledText, () => ToggleEffect(row));
            AttachTooltip(row.EnabledButton.gameObject,
                "aw_custom_court_settings_enabled", "Enabled",
                "aw_custom_court_settings_enabled_desc",
                "Enable or disable this office bonus.");
            SetRect(row.EnabledButton.GetComponent<RectTransform>(), 116f, 5f,
                62f, 22f);
            row.ScopeButton = CreateButton(background.transform, "Scope",
                out row.ScopeText, () => CycleEffectScope(row));
            AttachTooltip(row.ScopeButton.gameObject,
                "aw_custom_court_settings_scope", "Scope",
                "aw_custom_court_settings_scope_desc",
                "Choose which part of the realm receives this bonus.");
            SetRect(row.ScopeButton.GetComponent<RectTransform>(), 184f, 5f,
                104f, 22f);
            row.ModeButton = CreateButton(background.transform, "Mode",
                out row.ModeText, () => CycleEffectMode(row));
            AttachTooltip(row.ModeButton.gameObject,
                "aw_custom_court_settings_mode", "Mode",
                "aw_custom_court_settings_mode_desc",
                "Choose flat percentage or multiplier calculation.");
            SetRect(row.ModeButton.GetComponent<RectTransform>(), 294f, 5f,
                104f, 22f);
            row.ValueInput = CreateInput(background.transform, "Value");
            AttachTooltip(row.ValueInput.gameObject,
                "aw_custom_court_settings_value", "Value",
                "aw_custom_court_settings_value_desc",
                "Enter the magnitude used by the selected calculation mode.");
            SetRect(row.ValueInput.GetComponent<RectTransform>(), 404f, 5f,
                82f, 22f);
            return row;
        }

        private void BindDraft()
        {
            _nameInput.text = DisplayName(_draft);
            _gradeInput.text = _draft.Grade.ToString(CultureInfo.InvariantCulture);
            _slotsInput.text = _draft.Slots.ToString(CultureInfo.InvariantCulture);
            _minimumRankInput.text = (_draft.Requirements?.MinimumRank ?? 0)
                .ToString(CultureInfo.InvariantCulture);
            _traitInput.text = _draft.Requirements?.RequiredTraitId ?? string.Empty;
            RefreshBaseValues();

            foreach (EffectRow row in _effectRows)
            {
                CustomCourtOfficeEffect effect = _draft.Effects?.FirstOrDefault(
                    item => item != null && item.Id == row.Id);
                row.Enabled = effect != null;
                row.Mode = effect?.Mode ?? CustomCourtEffectMode.AddPercent;
                IReadOnlyList<CustomCourtEffectScope> scopes =
                    CustomCourtOfficeSettingsRules.AllowedScopes(row.Id);
                row.Scope = effect != null && scopes.Contains(effect.Scope)
                    ? effect.Scope
                    : scopes.FirstOrDefault();
                row.ValueInput.text = (effect?.Value ?? 0f).ToString("0.##",
                    CultureInfo.InvariantCulture);
                RefreshEffectRow(row);
            }
        }

        private void ShowTab(bool effects)
        {
            _showEffects = effects;
            _basePanel.gameObject.SetActive(!effects);
            _effectsPanel.gameObject.SetActive(effects);
            _baseTabText.text = AW_L10n.Text(
                "aw_custom_court_settings_base_tab", "Base and appointment");
            _effectsTabText.text = AW_L10n.Text(
                "aw_custom_court_settings_effects_tab", "Functional effects");
            _baseTab.GetComponent<Image>().color = effects
                ? new Color(0.18f, 0.13f, 0.08f, 1f)
                : new Color(0.38f, 0.27f, 0.12f, 1f);
            _effectsTab.GetComponent<Image>().color = effects
                ? new Color(0.38f, 0.27f, 0.12f, 1f)
                : new Color(0.18f, 0.13f, 0.08f, 1f);
        }

        private void ConfirmOfficeSettings()
        {
            if (_draft == null) return;
            if (!TryInt(_gradeInput.text, 1, 100, out int grade) ||
                !TryInt(_slotsInput.text, 1, 32, out int slots) ||
                !TryInt(_minimumRankInput.text, 0, 100, out int minimumRank))
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_court_settings_invalid_number",
                    "One or more numeric values are invalid."));
                return;
            }
            string name = _nameInput.text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_court_settings_name_required",
                    "Enter an office name."));
                return;
            }

            var effects = new List<CustomCourtOfficeEffect>();
            foreach (EffectRow row in _effectRows)
            {
                if (!row.Enabled) continue;
                if (!TryFloat(row.ValueInput.text, out float value))
                {
                    SetStatus(AW_L10n.Text(
                        "aw_custom_court_settings_invalid_effect_value",
                        "An effect value is invalid."));
                    return;
                }
                value = CustomCourtTemplateRules.ClampEffectValue(row.Mode,
                    value);
                effects.Add(new CustomCourtOfficeEffect
                {
                    Id = row.Id,
                    Mode = row.Mode,
                    Scope = row.Scope,
                    Value = value
                });
            }

            _draft.Name = _draft.Name ?? new CustomCourtLocalizedText();
            _draft.Name.Chinese = name;
            _draft.Name.English = name;
            _draft.Grade = grade;
            _draft.Slots = slots;
            _draft.Requirements = _draft.Requirements ??
                new CustomCourtOfficeRequirement();
            _draft.Requirements.MinimumRank = minimumRank;
            _draft.Requirements.RequiredTraitId =
                _traitInput.text?.Trim() ?? string.Empty;
            _draft.Effects = CustomCourtOfficeSettingsRules.NormalizeEffects(
                effects);

            CustomCourtTemplateValidationError error =
                CustomCourtOfficeSettingsRules.ValidateDraft(_draft);
            if (error != CustomCourtTemplateValidationError.None)
            {
                SetStatus(AW_L10n.Text(
                    "aw_custom_court_settings_validation_failed",
                    "Office settings are invalid.") + " " + error);
                return;
            }

            Action<CustomCourtOffice> callback = _confirmed;
            CustomCourtOffice result =
                CustomCourtOfficeSettingsRules.CloneOffice(_draft);
            callback?.Invoke(result);
            ReturnToEditor();
        }

        private void CancelOfficeSettings()
        {
            ReturnToEditor();
        }

        private static void ReturnToEditor()
        {
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.CUSTOM_COURT_WORKFLOW);
        }

        private void CycleLayer()
        {
            _draft.Layer = CustomCourtOfficeSettingsRules.NextLayer(
                _draft.Layer);
            RefreshBaseValues();
        }

        private void ToggleMilitary()
        {
            _draft.MilitaryCapable = !_draft.MilitaryCapable;
            RefreshBaseValues();
        }

        private void CyclePreferredSchool()
        {
            _draft.PreferredSchoolId = NextSchool(_draft.PreferredSchoolId);
            RefreshBaseValues();
        }

        private void CycleRequiredSchool()
        {
            _draft.Requirements = _draft.Requirements ??
                new CustomCourtOfficeRequirement();
            _draft.Requirements.RequiredSchoolId = NextSchool(
                _draft.Requirements.RequiredSchoolId);
            RefreshBaseValues();
        }

        private void CycleRequiredOffice()
        {
            _draft.Requirements = _draft.Requirements ??
                new CustomCourtOfficeRequirement();
            string[] ids = new[] { string.Empty }.Concat(
                    (_template?.Offices ?? new List<CustomCourtOffice>())
                    .Where(office => office != null && office.Id != _draft.Id)
                    .Select(office => office.Id))
                .Distinct(StringComparer.Ordinal).ToArray();
            _draft.Requirements.RequiredOfficeId = Next(ids,
                _draft.Requirements.RequiredOfficeId);
            RefreshBaseValues();
        }

        private void ToggleEffect(EffectRow row)
        {
            row.Enabled = !row.Enabled;
            RefreshEffectRow(row);
        }

        private void CycleEffectScope(EffectRow row)
        {
            row.Scope = CustomCourtOfficeSettingsRules.NextScope(row.Id,
                row.Scope);
            RefreshEffectRow(row);
        }

        private void CycleEffectMode(EffectRow row)
        {
            row.Mode = CustomCourtOfficeSettingsRules.NextMode(row.Mode);
            float current = 0f;
            TryFloat(row.ValueInput.text, out current);
            row.ValueInput.text = CustomCourtTemplateRules.ClampEffectValue(
                row.Mode, current).ToString("0.##", CultureInfo.InvariantCulture);
            RefreshEffectRow(row);
        }

        private void RefreshBaseValues()
        {
            _layerText.text = LayerName(_draft.Layer);
            _militaryText.text = BoolText(_draft.MilitaryCapable);
            _preferredSchoolText.text = SchoolName(_draft.PreferredSchoolId);
            _requiredSchoolText.text = SchoolName(
                _draft.Requirements?.RequiredSchoolId);
            _requiredOfficeText.text = OfficeName(
                _draft.Requirements?.RequiredOfficeId);
        }

        private void RefreshEffectRow(EffectRow row)
        {
            row.EnabledText.text = BoolText(row.Enabled);
            row.ScopeText.text = ScopeName(row.Scope);
            row.ModeText.text = ModeName(row.Mode);
            row.ScopeButton.interactable = row.Enabled;
            row.ModeButton.interactable = row.Enabled;
            row.ValueInput.interactable = row.Enabled;
            row.EnabledButton.GetComponent<Image>().color = row.Enabled
                ? new Color(0.18f, 0.42f, 0.24f, 1f)
                : new Color(0.25f, 0.15f, 0.09f, 1f);
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground as RectTransform;
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
                    "aw_custom_court_office_settings", "Office settings");
                scrollWindow.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            RectTransform scrollRect = BackgroundTransform?.Find("Scroll View")
                ?.GetComponent<RectTransform>();
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(width, height);
                scrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeScroll = BackgroundTransform?.Find("Scroll View")
                ?.GetComponent<ScrollRect>();
            if (nativeScroll != null)
            {
                nativeScroll.horizontal = false;
                nativeScroll.vertical = false;
            }
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null) viewport.sizeDelta = new Vector2(width, height);
            RectTransform content = ContentTransform as RectTransform;
            if (content != null) content.sizeDelta = new Vector2(width, height);

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            SetRect(_baseTab.GetComponent<RectTransform>(), 8f, 5f,
                (width - 20f) * 0.5f, 25f);
            SetRect(_effectsTab.GetComponent<RectTransform>(),
                12f + (width - 20f) * 0.5f, 5f,
                (width - 20f) * 0.5f, 25f);
            SetRect(_basePanel, 8f, 35f, width - 16f,
                Mathf.Max(1f, height - 82f));
            SetRect(_effectsPanel, 8f, 35f, width - 16f,
                Mathf.Max(1f, height - 82f));
            LayoutBaseFields(width - 16f);
            SetRect(_status.rectTransform, 10f, height - 42f,
                Mathf.Max(1f, width - 190f), 30f);
            SetRect(_confirmButton.GetComponent<RectTransform>(), width - 172f,
                height - 35f, 78f, 25f);
            SetRect(_cancelButton.GetComponent<RectTransform>(), width - 86f,
                height - 35f, 78f, 25f);
            _chrome?.RepositionResizeHandle();
        }

        private void LayoutBaseFields(float panelWidth)
        {
            float columnWidth = (panelWidth - 18f) * 0.5f;
            LayoutColumn(_basePanel, 0, 6f, columnWidth);
            LayoutColumn(_basePanel, 1, 12f + columnWidth, columnWidth);
        }

        private static void LayoutColumn(RectTransform panel, int column,
            float x, float width)
        {
            string[][] names =
            {
                new[] { "OfficeName", "Layer", "Grade", "Slots", "MilitaryCapable" },
                new[] { "PreferredSchool", "MinimumRank", "RequiredSchool", "RequiredTrait", "RequiredOffice" }
            };
            for (int row = 0; row < names[column].Length; row++)
            {
                Transform field = panel.Find(names[column][row]);
                if (field == null) continue;
                SetRect(field.GetComponent<RectTransform>(), x,
                    6f + row * 40f, width, 38f);
            }
        }

        private InputField AddInputField(Transform parent, string name,
            int column, int row, string labelKey, string fallback,
            string descriptionKey, string descriptionFallback)
        {
            RectTransform field = CreateField(parent, name, labelKey, fallback,
                descriptionKey, descriptionFallback);
            InputField input = CreateInput(field, "Input");
            AttachTooltip(input.gameObject, labelKey, fallback,
                descriptionKey, descriptionFallback);
            SetFieldControlRect(input.GetComponent<RectTransform>());
            return input;
        }

        private Button AddCycleField(Transform parent, string name, int column,
            int row, string labelKey, string fallback, string descriptionKey,
            string descriptionFallback, Action action,
            out Text valueText)
        {
            RectTransform field = CreateField(parent, name, labelKey, fallback,
                descriptionKey, descriptionFallback);
            Button button = CreateButton(field, "Value", out valueText, action);
            AttachTooltip(button.gameObject, labelKey, fallback,
                descriptionKey, descriptionFallback);
            SetFieldControlRect(button.GetComponent<RectTransform>());
            return button;
        }

        private static RectTransform CreateField(Transform parent, string name,
            string labelKey, string fallback, string descriptionKey,
            string descriptionFallback)
        {
            RectTransform field = new GameObject(name, typeof(RectTransform))
                .GetComponent<RectTransform>();
            field.SetParent(parent, false);
            Text label = CreateText(field, "Label", 8, TextAnchor.MiddleLeft,
                new Color(0.86f, 0.78f, 0.62f, 1f));
            label.text = AW_L10n.Text(labelKey, fallback);
            AttachTooltip(label.gameObject, labelKey, fallback,
                descriptionKey, descriptionFallback);
            SetFieldLabelRect(label.rectTransform);
            return field;
        }

        private static RectTransform CreatePanel(Transform parent, string name)
        {
            RectTransform panel = new GameObject(name, typeof(RectTransform),
                typeof(Image)).GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            AW_UIStyle.ApplyPanel(panel.GetComponent<Image>(), 0.9f);
            return panel;
        }

        private static InputField CreateInput(Transform parent, string name)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(InputField));
            obj.transform.SetParent(parent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.9f);
            Text value = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleLeft, Color.white);
            Stretch(value.rectTransform, new Vector2(5f, 1f),
                new Vector2(-5f, -1f));
            InputField input = obj.GetComponent<InputField>();
            input.textComponent = value;
            input.characterLimit = 64;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Button CreateButton(Transform parent, string name,
            out Text text, Action action)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => action?.Invoke());
            text = CreateText(obj.transform, "Text", 8,
                TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform, new Vector2(2f, 1f),
                new Vector2(-2f, -1f));
            return button;
        }

        private static void AttachTooltip(GameObject target, string titleKey,
            string titleFallback, string descriptionKey,
            string descriptionFallback)
        {
            if (target == null) return;
            Graphic graphic = target.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = true;
            TipButton tip = target.GetComponent<TipButton>() ??
                target.AddComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(target, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text(titleKey, titleFallback),
                    tip_description = AW_L10n.Text(descriptionKey,
                        descriptionFallback)
                });
        }

        private static Text CreateText(Transform parent, string name,
            int fontSize, TextAnchor anchor, Color color)
        {
            Text text = new GameObject(name, typeof(RectTransform), typeof(Text))
                .GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = LocalizedTextManager.current_font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void SetRect(RectTransform rect, float x, float y,
            float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(Mathf.Max(1f, width),
                Mathf.Max(1f, height));
        }

        private static void SetFieldLabelRect(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(4f, 0f);
            rect.sizeDelta = new Vector2(-8f, 15f);
        }

        private static void SetFieldControlRect(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(4f, -16f);
            rect.sizeDelta = new Vector2(-8f, 20f);
        }

        private static void Stretch(RectTransform rect, Vector2 min,
            Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        private void SetStatus(string value)
        {
            if (_status != null) _status.text = value ?? string.Empty;
        }

        private static bool TryInt(string value, int min, int max,
            out int result)
        {
            if (!int.TryParse(value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out result)) return false;
            result = Math.Max(min, Math.Min(max, result));
            return true;
        }

        private static bool TryFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out result) ||
                float.TryParse(value, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out result);
        }

        private static string NextSchool(string current)
        {
            string[] values = new[] { CourtSchoolId.None }.Concat(
                CourtSchoolRegistry.All.Select(school => school.Id)).ToArray();
            return Next(values, current);
        }

        private static string Next(IReadOnlyList<string> values, string current)
        {
            if (values == null || values.Count == 0) return string.Empty;
            int index = -1;
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], current,
                        StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            return values[(index + 1) % values.Count];
        }

        private static string DisplayName(CustomCourtOffice office)
        {
            string name = HistoryLocalizationRules.CurrentLanguage() == "en"
                ? office?.Name?.English
                : office?.Name?.Chinese;
            if (string.IsNullOrWhiteSpace(name)) name = office?.Name?.Chinese;
            if (string.IsNullOrWhiteSpace(name)) name = office?.Name?.English;
            return name ?? office?.Id ?? string.Empty;
        }

        private string OfficeName(string officeId)
        {
            if (string.IsNullOrEmpty(officeId))
                return AW_L10n.Text("aw_custom_court_settings_none", "None");
            return DisplayName(_template?.Offices?.FirstOrDefault(office =>
                office != null && office.Id == officeId)) ?? officeId;
        }

        private static string BoolText(bool value)
        {
            return value
                ? AW_L10n.Text("aw_custom_court_settings_enabled", "Enabled")
                : AW_L10n.Text("aw_custom_court_settings_disabled", "Disabled");
        }

        private static string LayerName(string layer)
        {
            switch (layer)
            {
                case CourtOfficeLayer.Primitive:
                    return AW_L10n.Text("aw_court_layer_primitive", "Primitive");
                case CourtOfficeLayer.City:
                    return AW_L10n.Text("aw_court_layer_city", "Local bureaus");
                case CourtOfficeLayer.Military:
                    return AW_L10n.Text("aw_court_layer_military", "Military");
                case CourtOfficeLayer.Censor:
                    return AW_L10n.Text("aw_court_layer_censor", "Censorate");
                case CourtOfficeLayer.Feudatory:
                    return AW_L10n.Text("aw_court_layer_feudatory", "Feudatory");
                default:
                    return AW_L10n.Text("aw_court_layer_central", "Central court");
            }
        }

        private static string SchoolName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return AW_L10n.Text("aw_custom_court_settings_none", "None");
            return AW_L10n.Text("aw_court_school_" + id, id);
        }

        private static string ScopeName(CustomCourtEffectScope scope)
        {
            return AW_L10n.Text("aw_custom_court_scope_" +
                scope.ToString().ToLowerInvariant(), scope.ToString());
        }

        private static string ModeName(CustomCourtEffectMode mode)
        {
            return AW_L10n.Text("aw_custom_court_mode_" +
                mode.ToString().ToLowerInvariant(), mode.ToString());
        }

        private static string EffectName(CustomCourtEffectId id)
        {
            return AW_L10n.Text("aw_custom_court_effect_" +
                id.ToString().ToLowerInvariant(), id.ToString());
        }

        private static string EffectDescription(CustomCourtEffectId id)
        {
            return AW_L10n.Text("aw_custom_court_effect_" +
                id.ToString().ToLowerInvariant() + "_desc", EffectName(id));
        }
    }
}
