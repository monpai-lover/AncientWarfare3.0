using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class NameDecisionWindow : AbstractWindow<NameDecisionWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(420f, 280f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(720f, 520f);

        private const float WindowMarginX = 42f;
        private const float WindowMarginY = 58f;
        private const float CandidateGap = 4f;
        private const float CandidateHeight = 23f;

        private static long _kingdomId = -1L;

        private readonly List<NameDecisionCandidateItem> _candidatePool =
            new List<NameDecisionCandidateItem>();
        private readonly List<string> _candidateNames = new List<string>();
        private Vector2 _windowSize = DefaultSize;
        private WideWindowChrome _windowChrome;
        private RectTransform _root;
        private RectTransform _candidateViewport;
        private RectTransform _candidateContent;
        private ScrollRect _candidateScroll;
        private Text _preview;
        private Text _candidateCaption;
        private Text _inputLabel;
        private InputField _input;
        private Text _error;
        private Text _cost;
        private Button _confirm;
        private Text _confirmText;
        private Button _cancel;
        private NameDecisionViewModel _model;
        private EraDecisionSnapshot _snapshot;
        private bool _pending;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.NAME_DECISION);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.NAME_DECISION,
                () => Instance?.FocusExisting());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyWindowLayout();
            _windowChrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size =>
                {
                    _windowSize = size;
                    ApplyWindowLayout();
                }, DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void FocusExisting()
        {
            try { _input?.ActivateInputField(); }
            catch { }
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyWindowLayout();
            _pending = false;
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            _snapshot = YearNameService.PrepareVoluntaryDecision(kingdom);
            _candidateNames.Clear();
            if (_snapshot != null)
                _candidateNames.AddRange(_snapshot.AvailableHistoricalNames);

            string stateName = _snapshot?.StateName ?? kingdom?.name ?? "";
            string initial = _snapshot?.InitialEra ?? "";
            _model = NameDecisionViewModel.ForEra(stateName, initial,
                _snapshot?.PoliticalPoints ?? 0);
            _model.SetUsedNames(_snapshot?.UsedNames);
            if (_snapshot != null && _snapshot.BlockReason != EraChangeBlockReason.None)
                _model.ApplyBlockReason(_snapshot.BlockReason);

            _input.onValueChanged.RemoveAllListeners();
            _input.text = initial;
            _input.onValueChanged.AddListener(OnInputChanged);
            _candidateScroll.verticalNormalizedPosition = 1f;
            RenderCandidates();
            RenderState();
        }

        private void OnInputChanged(string pValue)
        {
            if (_model == null) return;
            _model.SetInput(pValue);
            if (_snapshot != null && _snapshot.BlockReason != EraChangeBlockReason.None)
                _model.ApplyBlockReason(_snapshot.BlockReason);
            RefreshCandidateSelection();
            RenderState();
        }

        private void SelectCandidate(string pValue)
        {
            if (_pending || string.IsNullOrEmpty(pValue)) return;
            _input.text = pValue;
            try { _input.ActivateInputField(); }
            catch { }
        }

        private void Confirm()
        {
            if (_pending || _model == null || !_model.CanConfirm) return;
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.king?.data == null)
            {
                _model.ApplyBlockReason(EraChangeBlockReason.NotHereditaryEmperor);
                RenderState();
                return;
            }

            _pending = true;
            RenderState();
            EraChangeResult result = YearNameService.TryChangeEra(
                kingdom, kingdom.king, _model.Input, EraChangeKind.Voluntary,
                EraChangeReason.PlayerRequested);
            if (result.Success)
            {
                GetComponent<ScrollWindow>()?.clickHide();
                return;
            }
            _pending = false;
            _model.ApplyBlockReason(result.BlockReason);
            RenderState();
        }

        private void Cancel()
        {
            if (_pending) return;
            GetComponent<ScrollWindow>()?.clickHide();
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            ContentSizeFitter fitter = ContentTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            var rootObject = new GameObject("NameDecisionRoot", typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _preview = CreateText(_root, "Preview", 14, TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.42f, 1f));
            _preview.fontStyle = FontStyle.Bold;
            _candidateCaption = CreateText(_root, "CandidateCaption", 9,
                TextAnchor.MiddleLeft, new Color(0.88f, 0.84f, 0.72f, 1f));
            BuildCandidateScroller();
            _inputLabel = CreateText(_root, "InputLabel", 9,
                TextAnchor.MiddleLeft, Color.white);
            _input = BuildInput(_root);
            _error = CreateText(_root, "Error", 8, TextAnchor.UpperLeft,
                new Color(1f, 0.55f, 0.45f, 1f));
            _error.resizeTextForBestFit = true;
            _error.resizeTextMinSize = 6;
            _error.resizeTextMaxSize = 8;
            _cost = CreateText(_root, "Cost", 8, TextAnchor.MiddleLeft,
                new Color(0.82f, 0.82f, 0.76f, 1f));
            _confirm = BuildButton(_root, "Confirm", out _confirmText, Confirm);
            _cancel = BuildButton(_root, "Cancel", out _, Cancel);
        }

        private void BuildCandidateScroller()
        {
            var viewportObject = new GameObject("CandidateViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect));
            viewportObject.transform.SetParent(_root, false);
            _candidateViewport = viewportObject.GetComponent<RectTransform>();
            Image image = viewportObject.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(image, 0.82f);
            image.raycastTarget = true;

            var contentObject = new GameObject("CandidateContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(_candidateViewport, false);
            _candidateContent = contentObject.GetComponent<RectTransform>();
            _candidateContent.anchorMin = new Vector2(0f, 1f);
            _candidateContent.anchorMax = new Vector2(0f, 1f);
            _candidateContent.pivot = new Vector2(0f, 1f);

            _candidateScroll = viewportObject.GetComponent<ScrollRect>();
            _candidateScroll.viewport = _candidateViewport;
            _candidateScroll.content = _candidateContent;
            _candidateScroll.horizontal = false;
            _candidateScroll.vertical = true;
            _candidateScroll.movementType = ScrollRect.MovementType.Clamped;
            _candidateScroll.scrollSensitivity = 18f;
        }

        private static InputField BuildInput(Transform pParent)
        {
            var inputObject = new GameObject("EraInput", typeof(RectTransform),
                typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(inputObject.GetComponent<Image>(), 0.9f);

            Text value = CreateText(inputObject.transform, "Text", 10,
                TextAnchor.MiddleLeft, Color.white);
            Stretch(value.rectTransform, new Vector2(5f, 1f), new Vector2(-5f, -1f));
            Text placeholder = CreateText(inputObject.transform, "Placeholder", 9,
                TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.42f));
            Stretch(placeholder.rectTransform, new Vector2(5f, 1f),
                new Vector2(-5f, -1f));
            placeholder.text = AW_L10n.Text("aw_title_era_placeholder", "2-4 Han characters");

            InputField input = inputObject.GetComponent<InputField>();
            input.textComponent = value;
            input.placeholder = placeholder;
            input.characterLimit = 4;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Button BuildButton(Transform pParent, string pName,
            out Text pText, Action pAction)
        {
            var buttonObject = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(buttonObject.GetComponent<Image>(), 0.96f);
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => pAction?.Invoke());
            pText = CreateText(buttonObject.transform, "Text", 9,
                TextAnchor.MiddleCenter, Color.white);
            Stretch(pText.rectTransform, Vector2.zero, Vector2.zero);
            pText.text = pName == "Confirm"
                ? AW_L10n.Text("aw_title_confirm", "Confirm")
                : AW_L10n.Text("aw_title_cancel", "Cancel");
            return button;
        }

        private void ApplyWindowLayout()
        {
            if (_root == null) return;
            float width = Mathf.Max(1f, _windowSize.x - WindowMarginX);
            float height = Mathf.Max(1f, _windowSize.y - WindowMarginY);
            RectTransform background = BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
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
                    "aw_title_era_window", "Change Era");
                scrollWindow.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect = nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(width, height);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeScrollComponent = nativeScroll?.GetComponent<ScrollRect>();
            if (nativeScrollComponent != null)
            {
                nativeScrollComponent.horizontal = false;
                nativeScrollComponent.vertical = false;
            }
            Transform nativeScrollbar =
                BackgroundTransform?.Find("Scroll View/Scrollbar Vertical");
            if (nativeScrollbar != null)
            {
                foreach (Graphic graphic in
                         nativeScrollbar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            }
            RectTransform nativeViewport = ContentTransform?.parent as RectTransform;
            if (nativeViewport != null) nativeViewport.sizeDelta = new Vector2(width, height);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null) nativeContent.sizeDelta = new Vector2(width, height);

            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);

            SetRect(_preview.rectTransform, 8f, 4f, width - 16f, 30f);
            SetRect(_candidateCaption.rectTransform, 8f, 36f, width - 16f, 18f);
            float bottomArea = 92f;
            float candidateTop = 54f;
            float candidateHeight = Mathf.Max(58f, height - candidateTop - bottomArea);
            SetRect(_candidateViewport, 8f, candidateTop, width - 16f, candidateHeight);
            float inputTop = candidateTop + candidateHeight + 7f;
            SetRect(_inputLabel.rectTransform, 8f, inputTop, 74f, 22f);
            SetRect(_input.GetComponent<RectTransform>(), 80f, inputTop,
                Mathf.Max(100f, width - 184f), 22f);
            SetRect(_cost.rectTransform, width - 98f, inputTop, 90f, 22f);
            SetRect(_error.rectTransform, 8f, inputTop + 25f,
                width - 16f, 24f);
            SetRect(_confirm.GetComponent<RectTransform>(), width - 174f,
                height - 30f, 78f, 25f);
            SetRect(_cancel.GetComponent<RectTransform>(), width - 88f,
                height - 30f, 78f, 25f);
            LayoutCandidates();
            _windowChrome?.RepositionResizeHandle();
        }

        private void RenderCandidates()
        {
            while (_candidatePool.Count < _candidateNames.Count)
            {
                var itemObject = new GameObject("EraCandidate",
                    typeof(RectTransform), typeof(Image), typeof(Button),
                    typeof(NameDecisionCandidateItem));
                itemObject.transform.SetParent(_candidateContent, false);
                _candidatePool.Add(itemObject.GetComponent<NameDecisionCandidateItem>());
            }
            for (int i = 0; i < _candidatePool.Count; i++)
            {
                if (i >= _candidateNames.Count)
                {
                    _candidatePool[i].Clear();
                    continue;
                }
                string value = _candidateNames[i];
                _candidatePool[i].Setup(value,
                    string.Equals(value, _model?.Input, StringComparison.Ordinal),
                    SelectCandidate);
            }
            LayoutCandidates();
        }

        private void LayoutCandidates()
        {
            if (_candidateViewport == null || _candidateContent == null) return;
            float width = Mathf.Max(1f, _candidateViewport.rect.width - 8f);
            int columns = Math.Max(1, Mathf.FloorToInt(
                (width + CandidateGap) / (78f + CandidateGap)));
            float cellWidth = (width - (columns - 1) * CandidateGap) / columns;
            int rows = Math.Max(1, (_candidateNames.Count + columns - 1) / columns);
            float contentHeight = rows * CandidateHeight +
                                  Math.Max(0, rows - 1) * CandidateGap + 8f;
            _candidateContent.sizeDelta = new Vector2(width, contentHeight);
            for (int i = 0; i < _candidateNames.Count && i < _candidatePool.Count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                _candidatePool[i].SetLayout(new Vector2(
                        4f + column * (cellWidth + CandidateGap),
                        -4f - row * (CandidateHeight + CandidateGap)),
                    new Vector2(cellWidth, CandidateHeight));
            }
        }

        private void RefreshCandidateSelection()
        {
            for (int i = 0; i < _candidateNames.Count && i < _candidatePool.Count; i++)
                _candidatePool[i].SetSelected(string.Equals(_candidateNames[i],
                    _model?.Input, StringComparison.Ordinal));
        }

        private void RenderState()
        {
            if (_model == null) return;
            _preview.text = AW_L10n.Text("aw_title_preview", "Appellation") +
                            ": " + _model.Preview;
            _candidateCaption.text = string.Format(AW_L10n.Text(
                    "aw_title_era_candidates", "Unused historical eras: {0}"),
                _candidateNames.Count);
            _inputLabel.text = AW_L10n.Text("aw_title_era_input", "Era name");
            _cost.text = string.Format(AW_L10n.Text(
                    "aw_title_era_cost", "Cost 30 / {0}"),
                _snapshot?.PoliticalPoints ?? 0);
            _error.text = string.IsNullOrEmpty(_model.ErrorKey)
                ? ""
                : AW_L10n.Text(_model.ErrorKey, ErrorFallback(_model.ErrorKey));
            bool canConfirm = !_pending && _model.CanConfirm;
            _confirm.interactable = canConfirm;
            _confirmText.text = _pending
                ? AW_L10n.Text("aw_title_committing", "Committing...")
                : AW_L10n.Text("aw_title_confirm", "Confirm");
            Image confirmImage = _confirm.GetComponent<Image>();
            AW_UIStyle.ApplyButton(confirmImage, canConfirm ? 0.96f : 0.48f);
            _cancel.interactable = !_pending;
        }

        private static string ErrorFallback(string pKey)
        {
            return pKey switch
            {
                "aw_title_error_not_hereditary_emperor" => "Only a hereditary emperor may change the era.",
                "aw_title_error_below_empire_rank" => "The ruler has not reached imperial rank.",
                "aw_title_error_not_independent" => "A vassal follows the suzerain chronology.",
                "aw_title_error_at_war" => "The era cannot be changed during war.",
                "aw_title_error_cooldown" => "Ten years must pass between voluntary era changes.",
                "aw_title_error_insufficient_points" => "Thirty political points are required.",
                "aw_title_error_invalid_name" => "Enter 2-4 Han characters.",
                "aw_title_error_duplicate_name" => "This Shi has already used that era name.",
                "aw_title_error_archive_unavailable" => "The lineage archive is unavailable.",
                "aw_title_error_missing_shi" => "The ruler has no valid Shi identity.",
                "aw_title_error_missing_reign" => "No active reign record exists.",
                "aw_title_error_persistence_failed" => "The era could not be committed.",
                _ => "The era cannot be changed."
            };
        }

        private static Text CreateText(Transform pParent, string pName,
            int pFontSize, TextAnchor pAnchor, Color pColor)
        {
            var textObject = new GameObject(pName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(pParent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pFontSize;
            text.alignment = pAnchor;
            text.color = pColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth),
                Mathf.Max(1f, pHeight));
        }

        private static void Stretch(RectTransform pRect, Vector2 pMin,
            Vector2 pMax)
        {
            pRect.anchorMin = Vector2.zero;
            pRect.anchorMax = Vector2.one;
            pRect.offsetMin = pMin;
            pRect.offsetMax = pMax;
        }
    }
}
