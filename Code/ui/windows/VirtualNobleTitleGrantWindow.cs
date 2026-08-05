using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class VirtualNobleTitleGrantWindow : AbstractWindow<VirtualNobleTitleGrantWindow>
    {
        private const string WindowId = "aw_virtual_title_grant";
        private static long _actorId = -1L;
        private RectTransform _root;
        private InputField _input;
        private Text _identity;
        private Text _feedback;
        private Button _grant;
        private Text _grantText;
        private bool _pending;

        internal static void Open(long pActorId)
        {
            _actorId = pActorId;
            if (Instance == null) CreateAndInit(WindowId);
            AW_LineageWindowIds.SafeShow(WindowId,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            AW3MultiplayerCommandFacade.Changed += OnCommandChanged;
        }

        private void OnDestroy() => AW3MultiplayerCommandFacade.Changed -= OnCommandChanged;

        public override void OnNormalEnable() => Refresh();

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
                    actor.data.id, _input.text));
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

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            var rootObject = new GameObject("VirtualNobleTitleRoot", typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _identity = CreateText(_root, "Identity", 12, TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.42f, 1f));
            _identity.fontStyle = FontStyle.Bold;
            CreateText(_root, "Prompt", 9, TextAnchor.MiddleLeft, Color.white).text =
                AW_L10n.Text("aw_virtual_title_prompt", "Enter an inheritable title (up to 64 characters)");
            _input = BuildInput(_root);
            _feedback = CreateText(_root, "Feedback", 8, TextAnchor.MiddleLeft,
                new Color(1f, 0.58f, 0.48f, 1f));
            _grant = BuildButton(_root, "Grant", out _grantText, Confirm);
            Button cancel = BuildButton(_root, "Cancel", out Text cancelText, Cancel);
            cancelText.text = AW_L10n.Text("aw_title_cancel", "Cancel");
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform bg = BackgroundTransform?.GetComponent<RectTransform>();
            if (bg != null) bg.sizeDelta = new Vector2(380f, 210f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.text = AW_L10n.Text("aw_virtual_title_grant", "Grant Virtual Title");
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(338f, 150f);
            SetRect(_identity, 8f, 4f, 322f, 24f);
            SetRect(_root.Find("Prompt") as RectTransform, 8f, 32f, 322f, 22f);
            SetRect(_input.GetComponent<RectTransform>(), 8f, 58f, 322f, 26f);
            SetRect(_feedback, 8f, 88f, 322f, 24f);
            SetRect(_grant.GetComponent<RectTransform>(), 150f, 120f, 82f, 25f);
            Transform cancel = _root.Find("Cancel");
            if (cancel != null) SetRect(cancel as RectTransform, 240f, 120f, 82f, 25f);
        }

        private static InputField BuildInput(Transform parent)
        {
            var obj = new GameObject("TitleInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            obj.transform.SetParent(parent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.9f);
            Text value = CreateText(obj.transform, "Text", 10, TextAnchor.MiddleLeft, Color.white);
            value.rectTransform.offsetMin = new Vector2(5f, 1f);
            value.rectTransform.offsetMax = new Vector2(-5f, -1f);
            Text placeholder = CreateText(obj.transform, "Placeholder", 9, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.42f));
            placeholder.text = AW_L10n.Text("aw_virtual_title_placeholder", "Example: Marquis of An");
            placeholder.rectTransform.offsetMin = new Vector2(5f, 1f);
            placeholder.rectTransform.offsetMax = new Vector2(-5f, -1f);
            InputField input = obj.GetComponent<InputField>();
            input.textComponent = value;
            input.placeholder = placeholder;
            input.characterLimit = VirtualNobleTitleRules.MaximumTitleLength;
            input.lineType = InputField.LineType.SingleLine;
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
