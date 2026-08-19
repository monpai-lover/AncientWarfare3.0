using System;
using AncientWarfare3.core.performance;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class FramePrioritySchedulerConfirmDialog : MonoBehaviour
    {
        private static FramePrioritySchedulerConfirmDialog _active;

        private InputField _input;
        private Button _confirm;
        private Text _error;
        private Action _accepted;

        internal static void Show(Transform pParent, Action pAccepted)
        {
            if (pParent == null || pAccepted == null) return;
            CloseActive();
            GameObject root = new GameObject(
                "FramePrioritySchedulerConfirmDialog",
                typeof(RectTransform), typeof(Image),
                typeof(FramePrioritySchedulerConfirmDialog));
            root.transform.SetParent(pParent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            root.transform.SetAsLastSibling();

            _active = root.GetComponent<FramePrioritySchedulerConfirmDialog>();
            _active.Build(pAccepted);
        }

        internal static void CloseActive()
        {
            if (_active == null) return;
            FramePrioritySchedulerConfirmDialog active = _active;
            _active = null;
            if (active != null) Destroy(active.gameObject);
        }

        private void Build(Action pAccepted)
        {
            _accepted = pAccepted;
            RectTransform panel = CreatePanel(transform);
            CreateText(panel, "Title", new Vector2(0f, 72f),
                new Vector2(300f, 36f), 16, TextAnchor.MiddleCenter,
                AW_L10n.Text("aw_frame_scheduler_confirm_title",
                    "Enable frame-priority scheduling"));
            CreateText(panel, "Prompt", new Vector2(0f, 31f),
                new Vector2(300f, 42f), 10, TextAnchor.MiddleCenter,
                AW_L10n.Text("aw_frame_scheduler_confirm_prompt",
                    "Type yes to enable this experimental scheduler."));
            _input = CreateInput(panel);
            _error = CreateText(panel, "Error", new Vector2(0f, -29f),
                new Vector2(300f, 22f), 9, TextAnchor.MiddleCenter, "");
            _error.color = new Color(0.95f, 0.3f, 0.25f, 1f);

            _confirm = CreateButton(panel, "Confirm", new Vector2(72f, -67f),
                AW_L10n.Text("aw_confirm", "Confirm"), TryConfirm);
            CreateButton(panel, "Cancel", new Vector2(-72f, -67f),
                AW_L10n.Text("aw_cancel", "Cancel"), CloseActive);
            _input.onValueChanged.AddListener(_ => Refresh());
            _input.onEndEdit.AddListener(value =>
            {
                if (Input.GetKey(KeyCode.Return) ||
                    Input.GetKey(KeyCode.KeypadEnter)) TryConfirm();
            });
            Refresh();
            try
            {
                _input.Select();
                _input.ActivateInputField();
            }
            catch { }
        }

        private void TryConfirm()
        {
            if (!FramePrioritySchedulerConfirmationRules.IsAccepted(
                    _input?.text))
            {
                if (_error != null)
                    _error.text = AW_L10n.Text(
                        "aw_frame_scheduler_confirm_error",
                        "Enter exactly: yes");
                Refresh();
                return;
            }

            Action accepted = _accepted;
            _accepted = null;
            CloseActive();
            accepted?.Invoke();
        }

        private void Refresh()
        {
            bool accepted = FramePrioritySchedulerConfirmationRules.IsAccepted(
                _input?.text);
            if (_confirm != null) _confirm.interactable = accepted;
            if (accepted && _error != null) _error.text = "";
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_active, this)) _active = null;
            _accepted = null;
        }

        private static RectTransform CreatePanel(Transform pParent)
        {
            GameObject panel = new GameObject("Panel", typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(pParent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(340f, 210f);
            rect.anchoredPosition = Vector2.zero;
            Image image = panel.GetComponent<Image>();
            image.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
            image.type = Image.Type.Sliced;
            image.color = new Color(0.23f, 0.25f, 0.21f, 1f);
            return rect;
        }

        private static InputField CreateInput(Transform pParent)
        {
            GameObject obj = new GameObject("ConfirmationInput",
                typeof(RectTransform), typeof(Image), typeof(InputField));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(230f, 30f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            Image background = obj.GetComponent<Image>();
            background.sprite = SpriteTextureLoader.getSprite(
                "ui/special/inputFieldBackground");
            background.type = Image.Type.Sliced;

            Text value = CreateText(rect, "Text", Vector2.zero,
                new Vector2(210f, 26f), 12, TextAnchor.MiddleLeft, "");
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            Text placeholder = CreateText(rect, "Placeholder", Vector2.zero,
                new Vector2(210f, 26f), 11, TextAnchor.MiddleLeft,
                AW_L10n.Text("aw_frame_scheduler_confirm_placeholder",
                    "Type yes"));
            placeholder.color = new Color(1f, 1f, 1f, 0.38f);

            InputField input = obj.GetComponent<InputField>();
            input.textComponent = value;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 16;
            return input;
        }

        private static Button CreateButton(Transform pParent, string pName,
            Vector2 pPosition, string pLabel, Action pAction)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(112f, 30f);
            rect.anchoredPosition = pPosition;
            Image image = obj.GetComponent<Image>();
            image.sprite = SpriteTextureLoader.getSprite("ui/special/button");
            image.type = Image.Type.Sliced;
            Button button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => pAction?.Invoke());
            CreateText(rect, "Text", Vector2.zero, rect.sizeDelta, 11,
                TextAnchor.MiddleCenter, pLabel);
            return button;
        }

        private static Text CreateText(Transform pParent, string pName,
            Vector2 pPosition, Vector2 pSize, int pFontSize,
            TextAnchor pAlignment, string pValue)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPosition;
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font ??
                        Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = pFontSize;
            text.alignment = pAlignment;
            text.color = Color.white;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pFontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = pValue ?? "";
            return text;
        }
    }
}
