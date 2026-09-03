using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CityStateRenameWindow :
        AbstractWindow<CityStateRenameWindow>
    {
        private static long _cityId = -1L;
        private static long _kingdomId = -1L;

        private InputField _cityInput;
        private InputField _stateInput;
        private Text _status;
        private Button _confirm;
        private bool _pending;
        private bool _refreshRequested;

        internal static void Open(long pCityId)
        {
            City city;
            try { city = World.world?.cities?.get(pCityId); }
            catch { return; }
            if (city?.data == null || city.isRekt() ||
                city.kingdom?.data == null) return;
            _cityId = pCityId;
            _kingdomId = city.kingdom.id;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.CITY_STATE_RENAME);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.CITY_STATE_RENAME,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            BuildUi();
            AW3MultiplayerCommandFacade.Changed += OnCommandChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandChanged;
        }

        public override void OnNormalEnable() => Refresh();

        private void Update()
        {
            if (!_refreshRequested) return;
            _refreshRequested = false;
            _pending = false;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandChanged()
        {
            if (_pending) _refreshRequested = true;
        }

        private void Refresh()
        {
            BuildUi();
            City city;
            try { city = World.world?.cities?.get(_cityId); }
            catch { city = null; }
            if (city?.data == null || city.isRekt())
            {
                _status.text = AW_L10n.Text("aw_city_state_rename_inactive",
                    "City no longer exists");
                _confirm.interactable = false;
                return;
            }

            _cityInput.onValueChanged.RemoveAllListeners();
            _cityInput.text = city.data.name ?? string.Empty;
            _cityInput.onValueChanged.AddListener(_ => ValidateInputs());

            bool hasRegion = DeJureRegionStore.TryGetForCity(_cityId,
                out DeJureRegion region);
            _stateInput.gameObject.SetActive(hasRegion);
            Transform stateLabel = ContentTransform?.Find("StateLabel");
            if (stateLabel != null) stateLabel.gameObject.SetActive(hasRegion);
            if (hasRegion)
            {
                _stateInput.onValueChanged.RemoveAllListeners();
                _stateInput.text = region.RegionName ?? string.Empty;
                _stateInput.onValueChanged.AddListener(_ => ValidateInputs());
            }

            ValidateInputs();
            try { _cityInput.ActivateInputField(); }
            catch { }
        }

        private void ValidateInputs()
        {
            City city;
            try { city = World.world?.cities?.get(_cityId); }
            catch { city = null; }
            if (city?.data == null)
            {
                _confirm.interactable = false;
                _status.text = string.Empty;
                return;
            }

            bool hasRegion = DeJureRegionStore.TryGetForCity(_cityId,
                out DeJureRegion region);
            string cityName = _cityInput.text;
            string stateName = hasRegion ? _stateInput.text : string.Empty;
            CityStateRenameValidation validation = hasRegion
                ? CityStateRenameRules.ValidateFields(cityName, stateName)
                : (CityStateRenameRules.Normalize(cityName).Length == 0
                    ? CityStateRenameValidation.EmptyCityName
                    : CityStateRenameValidation.Success);

            if (validation == CityStateRenameValidation.EmptyCityName)
            {
                _status.text = AW_L10n.Text("aw_city_state_rename_empty_city",
                    "Enter a city name");
                _confirm.interactable = false;
                return;
            }
            if (validation == CityStateRenameValidation.EmptyStateName)
            {
                _status.text = AW_L10n.Text("aw_city_state_rename_empty_state",
                    "Enter a state name");
                _confirm.interactable = false;
                return;
            }

            string normalizedCity = CityStateRenameRules.Normalize(cityName);
            string normalizedState = CityStateRenameRules.Normalize(stateName);
            bool cityChanged = !string.Equals(normalizedCity,
                city.data.name ?? string.Empty, StringComparison.Ordinal);
            bool stateChanged = hasRegion && !string.Equals(normalizedState,
                region?.RegionName ?? string.Empty, StringComparison.Ordinal);
            _confirm.interactable = !_pending && (cityChanged || stateChanged);
            _status.text = string.Empty;
        }

        private void Confirm()
        {
            if (_pending) return;
            bool hasRegion = DeJureRegionStore.TryGetForCity(_cityId,
                out _);
            string stateName = hasRegion ? _stateInput.text : string.Empty;
            _pending = true;
            _confirm.interactable = false;
            _status.text = AW_L10n.Text("aw_city_state_rename_pending",
                "Applying rename...");
            AW3CommandResult result = AW3MultiplayerCommandFacade
                .DispatchFromUi(AW3CommandRequest.RenameCityState(
                    _kingdomId, _cityId, _cityInput.text, stateName));
            if (result.Status == AW3CommandStatus.Accepted)
            {
                GetComponent<ScrollWindow>()?.clickHide();
                return;
            }
            if (result.Status == AW3CommandStatus.Pending) return;
            _pending = false;
            _status.text = ErrorText(result.DetailCode);
            ValidateInputs();
        }

        private void Cancel()
        {
            if (!_pending) GetComponent<ScrollWindow>()?.clickHide();
        }

        private static string ErrorText(int pDetailCode)
        {
            CityStateRenameResult result = Enum.IsDefined(
                    typeof(CityStateRenameResult), pDetailCode)
                ? (CityStateRenameResult)pDetailCode
                : CityStateRenameResult.CommitFailed;
            switch (result)
            {
                case CityStateRenameResult.CityNotFound:
                    return AW_L10n.Text("aw_city_state_rename_inactive",
                        "City no longer exists");
                case CityStateRenameResult.Unauthorized:
                    return AW_L10n.Text("aw_city_state_rename_unauthorized",
                        "City does not belong to this realm");
                case CityStateRenameResult.EmptyCityName:
                    return AW_L10n.Text("aw_city_state_rename_empty_city",
                        "Enter a city name");
                case CityStateRenameResult.EmptyStateName:
                    return AW_L10n.Text("aw_city_state_rename_empty_state",
                        "Enter a state name");
                case CityStateRenameResult.NoChange:
                    return string.Empty;
                default:
                    return AW_L10n.Text("aw_city_state_rename_failed",
                        "Rename failed");
            }
        }

        private void BuildUi()
        {
            if (_cityInput != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in ContentTransform
                         .GetComponents<LayoutGroup>())
                layout.enabled = false;

            RectTransform bg = BackgroundTransform as RectTransform;
            Vector2 windowSize = new Vector2(380f, 240f);
            if (bg != null) bg.sizeDelta = windowSize;

            Transform titleBg = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBg?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleBg.gameObject.SetActive(true);
                titleRect.sizeDelta = new Vector2(windowSize.x * 0.56f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow scroll = GetComponent<ScrollWindow>();
            if (scroll?.titleText != null)
            {
                scroll.titleText.text = AW_L10n.Text(
                    "aw_city_state_rename_title", "Rename City / State");
                scroll.titleText.transform.localPosition = new Vector3(0f,
                    windowSize.y * 0.5f - 16f, 0f);
                scroll.titleText.raycastTarget = false;
            }

            Text cityLabel = MakeText("CityLabel", ContentTransform, 9,
                TextAnchor.MiddleLeft);
            cityLabel.text = AW_L10n.Text("aw_city_state_rename_city_label",
                "City name");
            Position(cityLabel.rectTransform, 20f, -20f, 100f, 22f);

            _cityInput = MakeInput("CityNameInput", ContentTransform,
                AW_L10n.Text("aw_city_state_rename_city_placeholder",
                    "Enter city name"));
            Position(_cityInput.GetComponent<RectTransform>(), 122f, -20f,
                216f, 24f);

            Text stateLabel = MakeText("StateLabel", ContentTransform, 9,
                TextAnchor.MiddleLeft);
            stateLabel.text = AW_L10n.Text(
                "aw_city_state_rename_state_label", "State name");
            Position(stateLabel.rectTransform, 20f, -52f, 100f, 22f);

            _stateInput = MakeInput("StateNameInput", ContentTransform,
                AW_L10n.Text("aw_city_state_rename_state_placeholder",
                    "Enter state name"));
            Position(_stateInput.GetComponent<RectTransform>(), 122f, -52f,
                216f, 24f);

            _status = MakeText("Status", ContentTransform, 8,
                TextAnchor.UpperLeft);
            _status.color = new Color(1f, 0.62f, 0.48f, 1f);
            Position(_status.rectTransform, 20f, -84f, 318f, 36f);

            _confirm = MakeButton("Confirm", ContentTransform,
                AW_L10n.Text("aw_city_state_rename_confirm", "Confirm"),
                Confirm);
            Position(_confirm.GetComponent<RectTransform>(), 20f, -130f,
                130f, 26f);

            Button cancel = MakeButton("Cancel", ContentTransform,
                AW_L10n.Text("aw_city_state_rename_cancel", "Cancel"),
                Cancel);
            Position(cancel.GetComponent<RectTransform>(), 166f, -130f,
                130f, 26f);
        }

        private static InputField MakeInput(string pName, Transform pParent,
            string pPlaceholder)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(InputField));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.92f);
            Text value = MakeText("Text", obj.transform, 10,
                TextAnchor.MiddleLeft);
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = new Vector2(6f, 1f);
            value.rectTransform.offsetMax = new Vector2(-6f, -1f);
            InputField input = obj.GetComponent<InputField>();
            input.textComponent = value;
            Text placeholder = MakeText("Placeholder", obj.transform, 10,
                TextAnchor.MiddleLeft);
            placeholder.text = pPlaceholder;
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(6f, 1f);
            placeholder.rectTransform.offsetMax = new Vector2(-6f, -1f);
            input.placeholder = placeholder;
            input.characterLimit = 20;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Button MakeButton(string pName, Transform pParent,
            string pText, Action pAction)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => pAction?.Invoke());
            Text text = MakeText("Text", obj.transform, 8,
                TextAnchor.MiddleCenter);
            text.text = pText;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(2f, 1f);
            text.rectTransform.offsetMax = new Vector2(-2f, -1f);
            return button;
        }

        private static Text MakeText(string pName, Transform pParent,
            int pSize, TextAnchor pAnchor)
        {
            Text text = new GameObject(pName, typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(pParent, false);
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void Position(RectTransform pRect, float pX,
            float pY, float pWidth, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }
    }
}
