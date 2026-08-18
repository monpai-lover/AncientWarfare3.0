using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class BanditAmnestySettlementWindow :
        AbstractWindow<BanditAmnestySettlementWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(620f, 390f);
        private static readonly Vector2 MinimumSize = new Vector2(560f, 350f);
        private static readonly Vector2 MaximumSize = new Vector2(780f, 560f);
        private static long _banditId = -1L;
        private static long _originId = -1L;

        private readonly List<string> _officeIds = new List<string>();
        private Vector2 _windowSize = DefaultSize;
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private Text _identity;
        private Text _feedback;
        private Button _noneButton;
        private Button _officeModeButton;
        private Button _titleModeButton;
        private Button _officeButton;
        private Text _officeText;
        private InputField _titleInput;
        private Button _hereditaryButton;
        private Text _hereditaryText;
        private Button _confirmButton;
        private Button _cancelButton;
        private BanditAmnestyRewardKind _rewardKind =
            BanditAmnestyRewardKind.None;
        private int _officeIndex;
        private bool _hereditary = true;
        private bool _submitting;

        internal static void Open(long pBanditKingdomId,
            long pOriginKingdomId)
        {
            _banditId = pBanditKingdomId;
            _originId = pOriginKingdomId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.
                    BANDIT_AMNESTY_SETTLEMENT);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.BANDIT_AMNESTY_SETTLEMENT,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size =>
                {
                    _windowSize = size;
                    ApplyLayout();
                }, DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            Kingdom bandit = ResolveKingdom(_banditId);
            Kingdom origin = ResolveKingdom(_originId);
            Actor leader = bandit?.king;
            _officeIds.Clear();
            if (origin?.data != null && leader?.data != null)
                _officeIds.AddRange(CourtService.
                    GetPromiseableAmnestyOffices(origin, leader));
            _officeIndex = Mathf.Clamp(_officeIndex, 0,
                Mathf.Max(0, _officeIds.Count - 1));
            _submitting = false;
            if (_identity != null)
                _identity.text = bandit?.data != null && origin?.data != null
                    ? (bandit.name ?? "") + "  ->  " + (origin.name ?? "") +
                      "\n" + (leader?.getName() ?? "")
                    : AW_L10n.Text("aw_bandit_amnesty_target_missing",
                        "The amnesty target is no longer available");
            if (_feedback != null) _feedback.text = "";
            RefreshControls();
        }

        private void SelectNone()
        {
            _rewardKind = BanditAmnestyRewardKind.None;
            RefreshControls();
        }

        private void SelectOffice()
        {
            _rewardKind = BanditAmnestyRewardKind.Office;
            RefreshControls();
        }

        private void SelectTitle()
        {
            _rewardKind = BanditAmnestyRewardKind.VirtualTitle;
            RefreshControls();
        }

        private void CycleOffice()
        {
            if (_officeIds.Count > 0)
                _officeIndex = (_officeIndex + 1) % _officeIds.Count;
            RefreshControls();
        }

        private void CycleHereditary()
        {
            _hereditary = !_hereditary;
            RefreshControls();
        }

        private void Confirm()
        {
            if (_submitting) return;
            var offer = new PeasantRebelBanditAmnestyOffer
            {
                RewardKind = _rewardKind,
                OfficeId = _rewardKind == BanditAmnestyRewardKind.Office &&
                           _officeIds.Count > 0
                    ? _officeIds[_officeIndex]
                    : "",
                TitleText = _titleInput?.text ?? "",
                Hereditary = _hereditary
            };
            _submitting = true;
            RefreshControls();
            AW3CommandResult result = AW3MultiplayerCommandFacade.
                DispatchFromUi(AW3CommandRequest.GrantBanditAmnesty(
                    _banditId, _originId, offer.RewardKind.ToString(),
                    offer.OfficeId, offer.TitleText, offer.Hereditary));
            if (!result.Accepted)
            {
                _submitting = false;
                if (_feedback != null)
                    _feedback.text = AW_L10n.Text(result.MessageKey,
                        AW_L10n.Text("aw_bandit_amnesty_reward_failed",
                            "The amnesty settlement could not be completed"));
                RefreshControls();
                return;
            }
            _submitting = false;
            GetComponent<ScrollWindow>()?.clickHide();
        }

        private void Cancel()
        {
            if (!_submitting) GetComponent<ScrollWindow>()?.clickHide();
        }

        private void RefreshControls()
        {
            SetModeVisual(_noneButton,
                _rewardKind == BanditAmnestyRewardKind.None);
            SetModeVisual(_officeModeButton,
                _rewardKind == BanditAmnestyRewardKind.Office);
            SetModeVisual(_titleModeButton,
                _rewardKind == BanditAmnestyRewardKind.VirtualTitle);
            bool officeMode = _rewardKind == BanditAmnestyRewardKind.Office;
            bool titleMode = _rewardKind ==
                             BanditAmnestyRewardKind.VirtualTitle;
            if (_officeButton != null)
                _officeButton.interactable = officeMode &&
                    _officeIds.Count > 0 && !_submitting;
            if (_officeText != null)
                _officeText.text = _officeIds.Count == 0
                    ? AW_L10n.Text("aw_bandit_amnesty_no_vacant_office",
                        "No eligible vacant office")
                    : CourtInstitutionService.OfficeName(
                        ResolveKingdom(_originId),
                        _officeIds[_officeIndex]);
            if (_titleInput != null)
                _titleInput.interactable = titleMode && !_submitting;
            if (_hereditaryButton != null)
                _hereditaryButton.interactable = titleMode && !_submitting;
            if (_hereditaryText != null)
                _hereditaryText.text = AW_L10n.Text(
                    "aw_virtual_title_hereditary", "Hereditary") + ": " +
                    AW_L10n.Text(_hereditary
                        ? "aw_virtual_title_hereditary_on"
                        : "aw_virtual_title_hereditary_off",
                        _hereditary ? "Yes" : "No");
            if (_confirmButton != null)
                _confirmButton.interactable = !_submitting &&
                    (!officeMode || _officeIds.Count > 0);
        }

        private static void SetModeVisual(Button pButton, bool pSelected)
        {
            Image image = pButton?.GetComponent<Image>();
            if (image == null) return;
            image.color = pSelected
                ? new Color(0.82f, 0.59f, 0.22f, 1f)
                : new Color(0.28f, 0.25f, 0.22f, 1f);
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            ContentSizeFitter fitter =
                ContentTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            var rootObject = new GameObject("BanditAmnestySettlementRoot",
                typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _identity = CreateText(_root, "Identity", 12,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.42f, 1f));
            _identity.fontStyle = FontStyle.Bold;
            CreateText(_root, "RewardLabel", 9, TextAnchor.MiddleLeft,
                Color.white).text = AW_L10n.Text(
                "aw_bandit_amnesty_reward", "Promised reward");
            _noneButton = CreateButton(_root, "NoReward",
                AW_L10n.Text("aw_bandit_amnesty_reward_none", "No promise"),
                SelectNone);
            _officeModeButton = CreateButton(_root, "OfficeMode",
                AW_L10n.Text("aw_bandit_amnesty_reward_office", "Office"),
                SelectOffice);
            _titleModeButton = CreateButton(_root, "TitleMode",
                AW_L10n.Text("aw_bandit_amnesty_reward_title", "Noble title"),
                SelectTitle);
            _officeButton = CreateButton(_root, "Office", "",
                CycleOffice, out _officeText);
            _titleInput = CreateInput(_root);
            _hereditaryButton = CreateButton(_root, "Hereditary", "",
                CycleHereditary, out _hereditaryText);
            _feedback = CreateText(_root, "Feedback", 9,
                TextAnchor.MiddleLeft,
                new Color(1f, 0.58f, 0.48f, 1f));
            _confirmButton = CreateButton(_root, "Confirm",
                AW_L10n.Text("aw_bandit_amnesty_confirm", "Grant amnesty"),
                Confirm);
            _cancelButton = CreateButton(_root, "Cancel",
                AW_L10n.Text("aw_title_cancel", "Cancel"), Cancel);
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background =
                BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.text = AW_L10n.Text(
                    "aw_bandit_amnesty_settlement_title",
                    "Bandit Amnesty Settlement");
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            SetRect(_identity, 10f, 8f, width - 20f, 48f);
            SetRect(_root.Find("RewardLabel") as RectTransform, 12f, 62f,
                width - 24f, 22f);
            float third = (width - 32f) / 3f;
            SetRect(_noneButton, 10f, 88f, third, 32f);
            SetRect(_officeModeButton, 16f + third, 88f, third, 32f);
            SetRect(_titleModeButton, 22f + third * 2f, 88f, third, 32f);
            SetRect(_officeButton, 10f, 132f, width - 20f, 32f);
            SetRect(_titleInput, 10f, 176f, width - 20f, 32f);
            SetRect(_hereditaryButton, 10f, 218f,
                Mathf.Min(240f, width - 20f), 32f);
            SetRect(_feedback, 10f, 258f, width - 20f,
                Mathf.Max(24f, height - 312f));
            SetRect(_confirmButton, width - 252f, height - 34f, 116f, 30f);
            SetRect(_cancelButton, width - 126f, height - 34f, 116f, 30f);
            _chrome?.RepositionResizeHandle();
        }

        private static Button CreateButton(Transform pParent, string pName,
            string pText, Action pAction)
        {
            return CreateButton(pParent, pName, pText, pAction, out _);
        }

        private static Button CreateButton(Transform pParent, string pName,
            string pText, Action pAction, out Text pLabel)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => pAction?.Invoke());
            pLabel = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleCenter, Color.white);
            pLabel.text = pText ?? "";
            pLabel.rectTransform.anchorMin = Vector2.zero;
            pLabel.rectTransform.anchorMax = Vector2.one;
            pLabel.rectTransform.offsetMin = new Vector2(4f, 2f);
            pLabel.rectTransform.offsetMax = new Vector2(-4f, -2f);
            return button;
        }

        private static InputField CreateInput(Transform pParent)
        {
            var obj = new GameObject("TitleInput", typeof(RectTransform),
                typeof(Image), typeof(InputField));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.9f);
            Text value = CreateText(obj.transform, "Text", 10,
                TextAnchor.MiddleLeft, Color.white);
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = new Vector2(6f, 1f);
            value.rectTransform.offsetMax = new Vector2(-6f, -1f);
            Text placeholder = CreateText(obj.transform, "Placeholder", 9,
                TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.45f));
            placeholder.text = AW_L10n.Text(
                "aw_bandit_amnesty_title_placeholder",
                "Enter the promised noble title");
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(6f, 1f);
            placeholder.rectTransform.offsetMax = new Vector2(-6f, -1f);
            InputField input = obj.GetComponent<InputField>();
            input.targetGraphic = obj.GetComponent<Image>();
            input.textComponent = value;
            input.placeholder = placeholder;
            input.characterLimit = VirtualNobleTitleRules.MaximumTitleLength;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor, Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = pColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(Component pComponent, float pX, float pY,
            float pWidth, float pHeight)
        {
            RectTransform rect = pComponent?.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(pX, -pY);
            rect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId <= 0L) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
