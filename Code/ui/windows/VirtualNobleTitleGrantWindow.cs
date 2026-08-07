using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class VirtualNobleTitleGrantWindow : AbstractWindow<VirtualNobleTitleGrantWindow>
    {
        private const string GrantWindowId = "aw_virtual_title_grant";
        private static readonly Vector2 DefaultSize = new Vector2(520f, 320f);
        private static readonly Vector2 MinimumSize = new Vector2(440f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(760f, 520f);
        private static long _actorId = -1L;
        private Vector2 _windowSize = DefaultSize;
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private InputField _input;
        private Text _identity;
        private Text _feedback;
        private Button _grant;
        private Text _grantText;
        private Button _hereditary;
        private Text _hereditaryText;
        private Button _cancel;
        private bool _pending;
        private bool _isHereditary = true;
        private int _focusInputFrame = -1;

        internal static void Open(long pActorId)
        {
            _actorId = pActorId;
            if (Instance == null) CreateAndInit(GrantWindowId);
            AW_LineageWindowIds.SafeShow(GrantWindowId,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize =>
                {
                    _windowSize = pSize;
                    ApplyLayout();
                }, DefaultSize, MinimumSize, MaximumSize);
            AW3MultiplayerCommandFacade.Changed += OnCommandChanged;
        }

        private void OnDestroy() => AW3MultiplayerCommandFacade.Changed -= OnCommandChanged;

        public override void OnNormalEnable()
        {
            Refresh();
            _focusInputFrame = Time.frameCount + 1;
        }

        private void Update()
        {
            if (_focusInputFrame < 0 || Time.frameCount < _focusInputFrame)
                return;
            _focusInputFrame = -1;
            FocusInput();
        }

        private void FocusInput()
        {
            if (_input == null || !_input.interactable) return;
            try
            {
                EventSystem.current?.SetSelectedGameObject(_input.gameObject);
                _input.Select();
                _input.ActivateInputField();
            }
            catch { }
        }

        private void OnCommandChanged()
        {
            if (!_pending) return;
            _pending = false;
            if (isActiveAndEnabled) Refresh();
        }

        private void Refresh()
        {
            Actor actor = World.world?.units?.get(_actorId);
            Kingdom kingdom = actor?.kingdom;
            if (_identity != null)
                _identity.text = actor?.data != null
                    ? actor.getName() + "  [" + (kingdom?.name ?? "") + "]"
                    : AW_L10n.Text("aw_unknown_actor", "Unknown actor");
            if (_feedback != null) _feedback.text = "";
            if (_input != null) _input.text = "";
            if (_grant != null) _grant.interactable = !_pending &&
                actor?.data != null && actor.isAlive() && kingdom?.data != null;
            if (_grantText != null)
                _grantText.text = AW_L10n.Text("aw_virtual_title_grant_action", "Grant");
            if (_hereditaryText != null)
                _hereditaryText.text = ResolveHereditaryText();
        }

        private void Confirm()
        {
            if (_pending || _input == null) return;
            Actor actor = World.world?.units?.get(_actorId);
            Kingdom kingdom = actor?.kingdom;
            if (actor?.data == null || kingdom?.data == null || !actor.isAlive()) return;
            _pending = true;
            AW3CommandResult result = AW3MultiplayerCommandFacade.DispatchFromUi(
                AW3CommandRequest.GrantVirtualNobleTitle(kingdom.id,
                    actor.data.id, _input.text, _isHereditary));
            if (result.Status == AW3CommandStatus.Accepted)
            {
                _pending = false;
                GetComponent<ScrollWindow>()?.clickHide();
                return;
            }
            if (result.Status == AW3CommandStatus.Pending) return;
            _pending = false;
            if (_feedback != null)
                _feedback.text = AW_L10n.Text(result.MessageKey,
                    AW_L10n.Text("aw_virtual_title_error_generic",
                        "Unable to grant the virtual title"));
            if (_grant != null) _grant.interactable = true;
        }

        private void Cancel()
        {
            if (!_pending) GetComponent<ScrollWindow>()?.clickHide();
        }

        private void CycleHereditary()
        {
            _isHereditary = !_isHereditary;
            if (_hereditaryText != null)
                _hereditaryText.text = ResolveHereditaryText();
        }

        private string ResolveHereditaryText()
        {
            return AW_L10n.Text("aw_virtual_title_hereditary", "Hereditary") +
                ": " + AW_L10n.Text(_isHereditary
                    ? "aw_virtual_title_hereditary_on"
                    : "aw_virtual_title_hereditary_off",
                    _isHereditary ? "Yes" : "No");
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

            var rootObject = new GameObject("VirtualNobleTitleRoot", typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _identity = CreateText(_root, "Identity", 12, TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.42f, 1f));
            _identity.fontStyle = FontStyle.Bold;
            CreateText(_root, "Prompt", 9, TextAnchor.MiddleLeft, Color.white).text =
                AW_L10n.Text("aw_virtual_title_prompt", "Enter an inheritable title (up to 64 characters)");
            _input = BuildInput(_root);
            _hereditary = BuildButton(_root, "Hereditary",
                out _hereditaryText, CycleHereditary);
            _feedback = CreateText(_root, "Feedback", 8, TextAnchor.MiddleLeft,
                new Color(1f, 0.58f, 0.48f, 1f));
            _grant = BuildButton(_root, "Grant", out _grantText, Confirm);
            _cancel = BuildButton(_root, "Cancel", out Text cancelText, Cancel);
            cancelText.text = AW_L10n.Text("aw_title_cancel", "Cancel");
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform bg = BackgroundTransform?.GetComponent<RectTransform>();
            if (bg != null) bg.sizeDelta = _windowSize;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find("TitleBackground");
            RectTransform titleRect = titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.56f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text("aw_virtual_title_grant", "Grant Virtual Title");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                window.titleText.raycastTarget = false;
            }
            DisableNativeScroll(width, height);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            SetRect(_identity, 10f, 8f, width - 20f, 28f);
            SetRect(_root.Find("Prompt") as RectTransform, 10f, 44f,
                width - 20f, 22f);
            SetRect(_input.GetComponent<RectTransform>(), 10f, 70f,
                width - 20f, 30f);
            SetRect(_hereditary.GetComponent<RectTransform>(), 10f, 106f,
                Mathf.Min(220f, width - 20f), 28f);
            SetRect(_feedback, 10f, 140f, width - 20f,
                Mathf.Max(24f, height - 208f));
            SetRect(_grant.GetComponent<RectTransform>(), width - 252f,
                height - 34f, 116f, 28f);
            if (_cancel != null) SetRect(_cancel.GetComponent<RectTransform>(),
                width - 126f,
                height - 34f, 116f, 28f);
            _chrome?.RepositionResizeHandle();
        }

        private void DisableNativeScroll(float pWidth, float pHeight)
        {
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeRect = nativeScroll?.GetComponent<RectTransform>();
            if (nativeRect != null)
            {
                nativeRect.sizeDelta = new Vector2(pWidth, pHeight);
                nativeRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect native = nativeScroll?.GetComponent<ScrollRect>();
            if (native != null)
            {
                native.horizontal = false;
                native.vertical = false;
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
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null) viewport.sizeDelta = new Vector2(pWidth, pHeight);
            RectTransform content = ContentTransform as RectTransform;
            if (content != null) content.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static InputField BuildInput(Transform parent)
        {
            var obj = new GameObject("TitleInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            obj.transform.SetParent(parent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.9f);
            Text value = CreateText(obj.transform, "Text", 10, TextAnchor.MiddleLeft, Color.white);
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = new Vector2(5f, 1f);
            value.rectTransform.offsetMax = new Vector2(-5f, -1f);
            Text placeholder = CreateText(obj.transform, "Placeholder", 9, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.42f));
            placeholder.text = AW_L10n.Text("aw_virtual_title_placeholder", "Example: Marquis of An");
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(5f, 1f);
            placeholder.rectTransform.offsetMax = new Vector2(-5f, -1f);
            InputField input = obj.GetComponent<InputField>();
            input.targetGraphic = obj.GetComponent<Image>();
            input.textComponent = value;
            input.placeholder = placeholder;
            input.characterLimit = VirtualNobleTitleRules.MaximumTitleLength;
            input.lineType = InputField.LineType.SingleLine;
            input.readOnly = false;
            input.interactable = true;
            return input;
        }

        private static Button BuildButton(Transform parent, string name,
            out Text text, Action action)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.96f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => action?.Invoke());
            text = CreateText(obj.transform, "Text", 9, TextAnchor.MiddleCenter, Color.white);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(3f, 1f);
            text.rectTransform.offsetMax = new Vector2(-3f, -1f);
            text.text = name == "Grant"
                ? AW_L10n.Text("aw_virtual_title_grant_action", "Grant")
                : AW_L10n.Text("aw_title_cancel", "Cancel");
            return button;
        }

        private static Text CreateText(Transform parent, string name, int size,
            TextAnchor anchor, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(Component pComponent, float x, float y,
            float width, float height)
        {
            RectTransform rect = pComponent?.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
