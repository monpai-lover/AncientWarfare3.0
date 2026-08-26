using System;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.county;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CountyRenameWindow :
        AbstractWindow<CountyRenameWindow>
    {
        private static long _countyId = -1L;
        private static long _kingdomId = -1L;
        private Text _currentName;
        private InputField _input;
        private Text _status;
        private Button _confirm;
        private Button _restore;
        private bool _pending;
        private bool _refreshRequested;

        internal static void Open(long pCountyId)
        {
            CountyRecord county = CountyAdministrationStore.FindById(
                pCountyId);
            City city = county == null ? null : World.world?.cities?.
                FirstOrDefault(item => item?.data?.id == county.CityId);
            if (county == null || city?.kingdom?.data == null) return;
            _countyId = county.CountyId;
            _kingdomId = city.kingdom.id;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.COUNTY_RENAME);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.COUNTY_RENAME,
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
            CountyRecord county = CountyAdministrationStore.FindById(
                _countyId);
            if (county == null)
            {
                _currentName.text = AW_L10n.Text(
                    "aw_county_rename_inactive", "County no longer exists");
                _status.text = _currentName.text;
                _confirm.interactable = false;
                _restore.interactable = false;
                return;
            }
            _currentName.text = AW_L10n.Text("aw_county_rename_current",
                "Current county") + ": " + (county.Name ?? string.Empty);
            _input.onValueChanged.RemoveAllListeners();
            _input.text = county.Name ?? string.Empty;
            _input.onValueChanged.AddListener(_ => ValidateInput());
            _restore.interactable = !_pending && county.ManualName;
            ValidateInput();
            try { _input.ActivateInputField(); }
            catch { }
        }

        private void ValidateInput()
        {
            CountyRecord county = CountyAdministrationStore.FindById(
                _countyId);
            if (county == null)
            {
                _confirm.interactable = false;
                return;
            }
            CountyRenameEntry[] entries = CountyAdministrationStore.ForRegion(
                    county.RegionId).Select(item => new CountyRenameEntry(
                    item.CountyId, item.RegionId, item.Name, item.Active))
                .ToArray();
            CountyRenameValidationResult result = CountyRenameRules.Validate(
                _input.text, county.CountyId, county.RegionId, entries,
                out string normalized);
            _confirm.interactable = !_pending &&
                result == CountyRenameValidationResult.Success &&
                !string.Equals(normalized, county.Name,
                    StringComparison.Ordinal);
            _status.text = result == CountyRenameValidationResult.Empty
                ? AW_L10n.Text("aw_county_rename_empty",
                    "Enter a county name")
                : result == CountyRenameValidationResult.Duplicate
                    ? AW_L10n.Text("aw_county_rename_duplicate",
                        "This name is already used in the same region")
                    : string.Empty;
        }

        private void Confirm()
        {
            Dispatch(_input.text, false);
        }

        private void RestoreHistorical()
        {
            Dispatch(string.Empty, true);
        }

        private void Dispatch(string pName, bool pRestore)
        {
            if (_pending) return;
            _pending = true;
            _confirm.interactable = false;
            _restore.interactable = false;
            AW3CommandResult result = AW3MultiplayerCommandFacade.
                DispatchFromUi(AW3CommandRequest.RenameCounty(_kingdomId,
                    _countyId, pName, pRestore));
            if (result.Status == AW3CommandStatus.Accepted)
            {
                GetComponent<ScrollWindow>()?.clickHide();
                return;
            }
            if (result.Status == AW3CommandStatus.Pending)
            {
                _status.text = AW_L10n.Text("aw_county_rename_pending",
                    "Applying county name...");
                return;
            }
            _pending = false;
            _status.text = ErrorText(result.DetailCode);
            ValidateInput();
        }

        private static string ErrorText(int pDetailCode)
        {
            CountyRenameResult result = Enum.IsDefined(
                    typeof(CountyRenameResult), pDetailCode)
                ? (CountyRenameResult)pDetailCode
                : CountyRenameResult.PersistenceFailed;
            switch (result)
            {
                case CountyRenameResult.CountyNotFound:
                    return AW_L10n.Text("aw_county_rename_inactive",
                        "County no longer exists");
                case CountyRenameResult.Unauthorized:
                    return AW_L10n.Text("aw_county_rename_unauthorized",
                        "This county is not controlled by the selected realm");
                case CountyRenameResult.EmptyName:
                    return AW_L10n.Text("aw_county_rename_empty",
                        "Enter a county name");
                case CountyRenameResult.DuplicateName:
                    return AW_L10n.Text("aw_county_rename_duplicate",
                        "This name is already used in the same region");
                case CountyRenameResult.InvalidRegion:
                    return AW_L10n.Text("aw_county_rename_invalid_region",
                        "County has no valid de jure region");
                default:
                    return AW_L10n.Text("aw_county_rename_failed",
                        "County rename failed");
            }
        }

        private void Cancel()
        {
            if (!_pending) GetComponent<ScrollWindow>()?.clickHide();
        }

        private void BuildUi()
        {
            if (_currentName != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in ContentTransform.
                         GetComponents<LayoutGroup>())
                layout.enabled = false;
            RectTransform background = BackgroundTransform as RectTransform;
            Vector2 windowSize = new Vector2(380f, 220f);
            if (background != null) background.sizeDelta = windowSize;
            Transform titleBackground = BackgroundTransform?.Find(
                "TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<
                RectTransform>();
            if (titleRect != null)
            {
                titleBackground.gameObject.SetActive(true);
                titleRect.sizeDelta = new Vector2(windowSize.x * 0.56f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow scroll = GetComponent<ScrollWindow>();
            if (scroll?.titleText != null)
            {
                scroll.titleText.text = AW_L10n.Text(
                    "aw_county_rename_title", "Rename County");
                scroll.titleText.transform.localPosition = new Vector3(0f,
                    windowSize.y * 0.5f - 16f, 0f);
                scroll.titleText.raycastTarget = false;
            }

            _currentName = MakeText("CurrentName", ContentTransform, 14,
                TextAnchor.MiddleCenter);
            Position(_currentName.rectTransform, 20f, -14f, 318f, 30f);
            Text label = MakeText("InputLabel", ContentTransform, 9,
                TextAnchor.MiddleLeft);
            label.text = AW_L10n.Text("aw_county_rename_input",
                "New county name");
            Position(label.rectTransform, 20f, -52f, 100f, 22f);
            _input = MakeInput(ContentTransform);
            Position(_input.GetComponent<RectTransform>(), 122f, -52f,
                216f, 24f);
            _status = MakeText("Status", ContentTransform, 8,
                TextAnchor.UpperLeft);
            _status.color = new Color(1f, 0.62f, 0.48f, 1f);
            Position(_status.rectTransform, 20f, -84f, 318f, 32f);
            _restore = MakeButton("Restore", ContentTransform,
                AW_L10n.Text("aw_county_restore_historical",
                    "Restore Historical Name"), RestoreHistorical);
            Position(_restore.GetComponent<RectTransform>(), 20f, -126f,
                142f, 26f);
            _confirm = MakeButton("Confirm", ContentTransform,
                AW_L10n.Text("aw_county_rename_confirm", "Confirm"), Confirm);
            Position(_confirm.GetComponent<RectTransform>(), 176f, -126f,
                76f, 26f);
            Button cancel = MakeButton("Cancel", ContentTransform,
                AW_L10n.Text("aw_county_rename_cancel", "Cancel"), Cancel);
            Position(cancel.GetComponent<RectTransform>(), 262f, -126f,
                76f, 26f);
        }

        private static InputField MakeInput(Transform pParent)
        {
            GameObject obj = new GameObject("CountyNameInput",
                typeof(RectTransform), typeof(Image), typeof(InputField));
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
            placeholder.text = AW_L10n.Text("aw_county_rename_placeholder",
                "Enter a new county name");
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(6f, 1f);
            placeholder.rectTransform.offsetMax = new Vector2(-6f, -1f);
            input.placeholder = placeholder;
            input.characterLimit = 16;
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
