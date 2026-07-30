using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class FeudatoryWindow : AbstractWindow<FeudatoryWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(560f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);
        private static long _kingdomId = -1L;
        private readonly List<FeudatoryListItem> _rowPool = new();
        private readonly List<Button> _cityButtonPool = new();
        private Vector2 _windowSize = DefaultSize;
        private long _selectedFeudatoryId = -1L;
        private long _selectedCityId = -1L;
        private RectTransform _root;
        private RectTransform _leftViewport;
        private RectTransform _leftContent;
        private RectTransform _rightViewport;
        private RectTransform _rightContent;
        private Image _divider;
        private Text _empty;
        private Text _detailTitle;
        private Text _detailBody;
        private Text _cityHeader;
        private FeudatoryPortraitPanel _portrait;
        private WideWindowChrome _chrome;
        private RectTransform _actionBar;
        private Button _relocateButton;
        private Button _reclaimButton;
        private Button _abolishButton;
        private Text _relocateText;
        private Text _reclaimText;
        private Text _abolishText;
        private TipButton _relocateTip;
        private TipButton _reclaimTip;
        private TipButton _abolishTip;
        private bool _abolishArmed;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId)
        {
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null || mandate.id != pKingdomId) return;
            _kingdomId = mandate.id;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.FEUDATORIES);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.FEUDATORIES,
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
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (!_commandRefreshRequested) return;
            _commandRefreshRequested = false;
            _commandPending = false;
            _abolishArmed = false;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            if (_commandPending) _commandRefreshRequested = true;
        }

        public void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            IReadOnlyList<FeudatorySnapshot> rows =
                FeudatoryService.GetByKingdom(_kingdomId);
            FeudatorySnapshot selected = null;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].FeudatoryId == _selectedFeudatoryId)
                {
                    selected = rows[i];
                    break;
                }
            if (selected == null && rows.Count > 0)
            {
                selected = rows[0];
                _selectedFeudatoryId = selected.FeudatoryId;
                _selectedCityId = -1L;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                while (_rowPool.Count <= i)
                    _rowPool.Add(FeudatoryListItem.Create(_leftContent));
                _rowPool[i].Bind(rows[i],
                    rows[i].FeudatoryId == _selectedFeudatoryId,
                    SelectFeudatory);
            }
            for (int i = rows.Count; i < _rowPool.Count; i++)
                _rowPool[i].Unbind();

            _empty.gameObject.SetActive(rows.Count == 0);
            if (selected == null)
            {
                _portrait.Unbind();
                _detailTitle.text = "";
                _detailBody.text = "";
                _cityHeader.text = "";
                HideCityButtons();
                if (_actionBar != null) _actionBar.gameObject.SetActive(false);
                return;
            }
            RenderDetail(selected);
        }

        private void RenderDetail(FeudatorySnapshot pSnapshot)
        {
            string princeTitle =
                DynasticTitleService.ResolveLivingTitle(
                    pSnapshot.PrinceActorId);
            _portrait.Bind(pSnapshot.PrinceActorId, pSnapshot.PrinceName,
                princeTitle, pSnapshot.PrinceShiLabel);
            _detailTitle.text = string.IsNullOrEmpty(pSnapshot.FeudatoryName)
                ? pSnapshot.SeatName
                : pSnapshot.FeudatoryName;
            string successor = string.IsNullOrEmpty(pSnapshot.SuccessorName)
                ? AW_L10n.Text("aw_feudatory_successor_none",
                    "No heir apparent")
                : pSnapshot.SuccessorName;
            _detailBody.text =
                AW_L10n.Text("aw_feudatory_mapmode_autonomy", "Autonomy") +
                ": " + pSnapshot.Autonomy +
                "    " + AW_L10n.Text("aw_feudatory_mapmode_loyalty", "Loyalty") +
                ": " + pSnapshot.Loyalty +
                "\n" + AW_L10n.Text("aw_feudatory_garrison", "Garrison") +
                ": " + pSnapshot.GarrisonSize +
                "    " + AW_L10n.Text("aw_feudatory_successor",
                    "Heir Apparent") +
                ": " + successor +
                "\n" + AW_L10n.Text("aw_feudatory_remittance",
                    "Central remittance") + ": " +
                Mathf.RoundToInt(
                    FeudatoryAutonomyRules.CentralRemittanceMultiplier(
                        pSnapshot.Autonomy) * 100f) + "%" +
                BuildSelectedCityLine(pSnapshot);
            _cityHeader.text = AW_L10n.Text("aw_feudatory_cities", "Cities") +
                               "  " + pSnapshot.CityRows.Count + "/" +
                               FeudatoryRules.MaximumCities;
            for (int i = 0; i < pSnapshot.CityRows.Count; i++)
            {
                while (_cityButtonPool.Count <= i)
                    _cityButtonPool.Add(CreateCityButton(_rightContent));
                BindCityButton(_cityButtonPool[i], pSnapshot.CityRows[i], i);
            }
            for (int i = pSnapshot.CityRows.Count; i < _cityButtonPool.Count; i++)
                _cityButtonPool[i].gameObject.SetActive(false);
            _actionBar.gameObject.SetActive(true);
            RefreshActionButtons();
        }

        private string BuildSelectedCityLine(FeudatorySnapshot pSnapshot)
        {
            if (_selectedCityId < 0) return "";
            for (int i = 0; i < pSnapshot.CityRows.Count; i++)
            {
                FeudatoryCityDisplayRow row = pSnapshot.CityRows[i];
                if (row.CityId != _selectedCityId) continue;
                string governor = string.IsNullOrEmpty(row.GovernorName)
                    ? AW_L10n.Text("aw_feudatory_governor_vacant", "Vacant")
                    : row.GovernorName;
                return "\n" + AW_L10n.Text("aw_feudatory_selected_city",
                           "Selected city") + ": " + row.CityName +
                       "    " + AW_L10n.Text("aw_feudatory_governor",
                           "Governor") + ": " + governor;
            }
            _selectedCityId = -1L;
            return "";
        }

        private void SelectFeudatory(long pFeudatoryId)
        {
            if (_selectedFeudatoryId != pFeudatoryId)
            {
                _selectedCityId = -1L;
                _abolishArmed = false;
            }
            _selectedFeudatoryId = pFeudatoryId;
            Refresh();
        }

        private void SelectCity(long pCityId)
        {
            City city = FindCity(pCityId);
            if (city?.data == null || city.isRekt()) return;
            SelectedMetas.selected_city = city;
            _selectedCityId = pCityId;
            Refresh();
        }

        private void RelocateSelectedFeudatory()
        {
            if (_commandPending) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.RelocateFeudatory(_kingdomId,
                        _selectedFeudatoryId));
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted) return;
            _selectedCityId = -1L;
            _abolishArmed = false;
            Refresh();
        }

        private void ReclaimSelectedCity()
        {
            if (_commandPending || _selectedCityId < 0) return;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.ReclaimFeudatoryCity(_kingdomId,
                        _selectedFeudatoryId, _selectedCityId));
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted) return;
            _selectedCityId = -1L;
            _abolishArmed = false;
            Refresh();
        }

        private void AbolishSelectedFeudatory()
        {
            if (_commandPending) return;
            if (!_abolishArmed)
            {
                _abolishArmed = true;
                RefreshActionButtons();
                return;
            }
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.AbolishFeudatory(_kingdomId,
                        _selectedFeudatoryId));
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted) return;
            _selectedFeudatoryId = -1L;
            _selectedCityId = -1L;
            _abolishArmed = false;
            Refresh();
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            var root = new GameObject("FeudatoryRoot", typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();

            CreateScrollArea(_root, "FeudatoryList", out _leftViewport,
                out _leftContent);
            CreateScrollArea(_root, "FeudatoryDetail", out _rightViewport,
                out _rightContent);
            var divider = new GameObject("Divider", typeof(RectTransform),
                typeof(Image));
            divider.transform.SetParent(_root, false);
            _divider = divider.GetComponent<Image>();
            _divider.color = new Color(.72f, .57f, .28f, .72f);
            _divider.raycastTarget = false;

            _empty = CreateText(_leftContent, "Empty", 9,
                TextAnchor.MiddleCenter);
            _empty.text = AW_L10n.Text("aw_feudatory_empty",
                "No active feudatories");
            _empty.color = new Color(.67f, .65f, .60f, 1f);
            var emptyLayout = _empty.gameObject.AddComponent<LayoutElement>();
            emptyLayout.preferredHeight = 44f;

            _portrait = FeudatoryPortraitPanel.Create(_rightContent);
            _detailTitle = CreateText(_rightContent, "DetailTitle", 13,
                TextAnchor.UpperLeft);
            _detailBody = CreateText(_rightContent, "DetailBody", 9,
                TextAnchor.UpperLeft);
            _cityHeader = CreateText(_rightContent, "CityHeader", 10,
                TextAnchor.UpperLeft);
            _actionBar = new GameObject("GovernanceActions",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _actionBar.SetParent(_rightContent, false);
            _relocateButton = CreateActionButton(_actionBar, "Relocate",
                RelocateSelectedFeudatory, out _relocateText,
                out _relocateTip);
            _reclaimButton = CreateActionButton(_actionBar, "Reclaim",
                ReclaimSelectedCity, out _reclaimText, out _reclaimTip);
            _abolishButton = CreateActionButton(_actionBar, "Abolish",
                AbolishSelectedFeudatory, out _abolishText,
                out _abolishTip);
            SetWindowTitle();
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float contentWidth = Mathf.Max(1f, _windowSize.x - 42f);
            float contentHeight = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find(
                "TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * .52f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeScrollComponent =
                nativeScroll?.GetComponent<ScrollRect>();
            if (nativeScrollComponent != null)
            {
                nativeScrollComponent.horizontal = false;
                nativeScrollComponent.vertical = false;
            }
            Transform nativeScrollbar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (nativeScrollbar != null)
                foreach (Graphic graphic in
                         nativeScrollbar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            RectTransform nativeViewport = ContentTransform?.parent as RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(contentWidth,
                    contentHeight);

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(contentWidth, contentHeight);
            float leftWidth = Mathf.Clamp(_root.sizeDelta.x * .34f, 150f, 240f);
            float rightX = leftWidth + 12f;
            float rightWidth = Mathf.Max(210f, _root.sizeDelta.x - rightX);
            Layout(_leftViewport, 0f, 0f, leftWidth, _root.sizeDelta.y);
            Layout(_divider.rectTransform, leftWidth + 5f, 0f, 2f,
                _root.sizeDelta.y);
            Layout(_rightViewport, rightX, 0f, rightWidth,
                _root.sizeDelta.y);
            _leftContent.sizeDelta = new Vector2(leftWidth - 10f,
                _leftContent.sizeDelta.y);
            float detailHeight = Mathf.Max(_root.sizeDelta.y, 390f);
            _rightContent.sizeDelta = new Vector2(rightWidth - 10f,
                detailHeight);
            Layout(_portrait.GetComponent<RectTransform>(), 0f, 0f,
                rightWidth - 12f, 76f);
            Layout(_detailTitle.rectTransform, 0f, 82f, rightWidth - 12f, 24f);
            Layout(_detailBody.rectTransform, 0f, 110f, rightWidth - 12f, 88f);
            Layout(_cityHeader.rectTransform, 0f, 204f, rightWidth - 12f, 20f);
            for (int i = 0; i < _cityButtonPool.Count; i++)
                Layout(_cityButtonPool[i].GetComponent<RectTransform>(), 0f,
                    228f + i * 26f, rightWidth - 12f, 23f);
            float actionY = 228f + FeudatoryRules.MaximumCities * 26f + 7f;
            float actionWidth = rightWidth - 12f;
            Layout(_actionBar, 0f, actionY, actionWidth, 25f);
            float buttonWidth = Mathf.Max(54f, (actionWidth - 8f) / 3f);
            Layout(_relocateButton?.GetComponent<RectTransform>(), 0f, 0f,
                buttonWidth, 24f);
            Layout(_reclaimButton?.GetComponent<RectTransform>(),
                buttonWidth + 4f, 0f, buttonWidth, 24f);
            Layout(_abolishButton?.GetComponent<RectTransform>(),
                (buttonWidth + 4f) * 2f, 0f, buttonWidth, 24f);
            _chrome?.RepositionResizeHandle();
        }

        private void RefreshActionButtons()
        {
            Kingdom kingdom = FindKingdom(_kingdomId);
            bool relocate = kingdom?.data != null &&
                            FeudatoryService.CanRelocateFeudatory(kingdom,
                                _selectedFeudatoryId) && !_commandPending;
            bool reclaim = kingdom?.data != null && _selectedCityId >= 0 &&
                           FeudatoryService.CanReclaimFeudatoryCity(kingdom,
                               _selectedFeudatoryId, _selectedCityId) &&
                           !_commandPending;
            bool abolish = kingdom?.data != null &&
                           FeudatoryService.CanAbolishFeudatory(kingdom,
                               _selectedFeudatoryId) && !_commandPending;
            SetActionButton(_relocateButton, _relocateText, _relocateTip,
                relocate, "aw_feudatory_action_relocate", "Relocate",
                "aw_feudatory_action_relocate_desc",
                "Relocate to a smaller inner contiguous territory");
            SetActionButton(_reclaimButton, _reclaimText, _reclaimTip,
                reclaim, "aw_feudatory_action_reclaim", "Reclaim",
                "aw_feudatory_action_reclaim_desc",
                "Return the selected city to direct rule");
            SetActionButton(_abolishButton, _abolishText, _abolishTip,
                abolish,
                _abolishArmed
                    ? "aw_feudatory_action_confirm_abolish"
                    : "aw_feudatory_action_abolish",
                _abolishArmed ? "Confirm" : "Abolish",
                "aw_feudatory_action_abolish_desc",
                "Return all cities to direct rule");
        }

        private static Button CreateActionButton(Transform pParent,
            string pName, UnityEngine.Events.UnityAction pAction,
            out Text pText, out TipButton pTip)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent, false);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            pText = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleCenter);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(3f, 1f);
            pText.rectTransform.offsetMax = new Vector2(-3f, -1f);
            pTip = obj.GetComponent<TipButton>();
            pTip.type = AW_RawTooltip.TYPE;
            return button;
        }

        private static void SetActionButton(Button pButton, Text pText,
            TipButton pTip, bool pEnabled, string pTitleKey,
            string pTitleFallback, string pDescriptionKey,
            string pDescriptionFallback)
        {
            if (pButton == null || pText == null) return;
            string title = AW_L10n.Text(pTitleKey, pTitleFallback);
            string description = AW_L10n.Text(pDescriptionKey,
                pDescriptionFallback);
            pButton.interactable = pEnabled;
            pText.text = title;
            AW_UIStyle.ApplyButton(pButton.GetComponent<Image>(),
                pEnabled ? .96f : .48f);
            if (pTip == null) return;
            pTip.enabled = true;
            pTip.hoverAction = () => Tooltip.show(pTip.gameObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = title,
                    tip_description = pEnabled
                        ? description
                        : description + "\n" + AW_L10n.Text(
                            "aw_feudatory_action_unavailable", "Unavailable")
                });
        }

        private static void CreateScrollArea(Transform pParent, string pName,
            out RectTransform pViewport, out RectTransform pContent)
        {
            var root = new GameObject(pName, typeof(RectTransform),
                typeof(ScrollRect));
            root.transform.SetParent(pParent, false);
            pViewport = root.GetComponent<RectTransform>();
            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(Mask));
            viewport.transform.SetParent(root.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            Image image = viewport.GetComponent<Image>();
            image.color = new Color(.06f, .055f, .045f, .5f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            pContent = content.GetComponent<RectTransform>();
            pContent.anchorMin = new Vector2(0f, 1f);
            pContent.anchorMax = new Vector2(0f, 1f);
            pContent.pivot = new Vector2(0f, 1f);
            pContent.anchoredPosition = Vector2.zero;
            pContent.sizeDelta = Vector2.zero;

            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = pContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22f;
            if (pName == "FeudatoryList")
            {
                var layout = content.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 3f;
                layout.padding = new RectOffset(3, 3, 3, 3);
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                var fitter = content.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private Button CreateCityButton(Transform pParent)
        {
            var obj = new GameObject("FeudatoryCity", typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            obj.GetComponent<Image>().color = new Color(.16f, .145f, .11f, .94f);
            Text label = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(7f, 0f);
            label.rectTransform.offsetMax = new Vector2(-5f, 0f);
            return obj.GetComponent<Button>();
        }

        private void BindCityButton(Button pButton,
            FeudatoryCityDisplayRow pRow, int pIndex)
        {
            pButton.onClick.RemoveAllListeners();
            long cityId = pRow.CityId;
            pButton.onClick.AddListener(() => SelectCity(cityId));
            Text label = pButton.GetComponentInChildren<Text>();
            string governor = string.IsNullOrEmpty(pRow.GovernorName)
                ? AW_L10n.Text("aw_feudatory_governor_vacant", "Vacant")
                : pRow.GovernorName;
            label.text = pRow.CityName + "  |  " +
                         AW_L10n.Text("aw_feudatory_governor", "Governor") +
                         ": " + governor;
            pButton.GetComponent<Image>().color = pRow.CityId == _selectedCityId
                ? new Color(.30f, .24f, .13f, .98f)
                : new Color(.16f, .145f, .11f, .94f);
            pButton.gameObject.SetActive(true);
            Layout(pButton.GetComponent<RectTransform>(), 0f,
                228f + pIndex * 26f, _rightContent.sizeDelta.x, 23f);
        }

        private void HideCityButtons()
        {
            for (int i = 0; i < _cityButtonPool.Count; i++)
                _cityButtonPool[i].gameObject.SetActive(false);
        }

        private void SetWindowTitle()
        {
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText == null) return;
            window.titleText.text = AW_L10n.Text("aw_feudatory_window_title",
                "Feudatories");
            window.titleText.raycastTarget = false;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try
            {
                CityManager cities = World.world?.cities;
                return cities?.get(pCityId);
            }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
