using System;
using System.Runtime.CompilerServices;
using AncientWarfare3.core.naming;
using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.patch.naming
{
    [HarmonyPatch]
    internal static class AW_ActorManualNamePatch
    {
        private const string SecondInputName =
            "AW_ActorManualNameSecondInput";
        private const string LabelName = "AW_ActorManualNameLabel";
        private const float Gap = 6f;
        private static readonly ConditionalWeakTable<UnitWindow, EditorState>
            States = new ConditionalWeakTable<UnitWindow, EditorState>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(UnitWindow), "loadNameInput")]
        private static void LoadNameInput_Postfix(UnitWindow __instance)
        {
            Actor actor = __instance?.actor;
            NameInput first = __instance?.name_input;
            if (actor?.data == null || actor.isRekt() || first == null)
                return;

            EditorState state = States.GetOrCreateValue(__instance);
            EnsureSecondInput(first, state);
            EnsureTrigger(__instance, first, state);
            if (state.Second == null || state.Trigger == null) return;
            state.ActorId = actor.data.id;
            ShowDisplay(__instance, first, state);
        }

        private static void EnsureSecondInput(NameInput pFirst,
            EditorState pState)
        {
            if (pState.Second != null) return;
            Transform existing = pFirst.transform.parent?.Find(
                SecondInputName);
            if (existing != null)
                pState.Second = existing.GetComponent<NameInput>();
            if (pState.Second == null)
            {
                pState.Second = UnityEngine.Object.Instantiate(pFirst,
                    pFirst.transform.parent);
                pState.Second.name = SecondInputName;
            }
            RectTransform rect = pFirst.GetComponent<RectTransform>();
            if (rect == null) return;
            pState.OriginalSize = rect.sizeDelta;
            pState.OriginalPosition = rect.anchoredPosition;
            pState.LayoutCaptured = true;
        }

        private static void EnsureTrigger(UnitWindow pWindow,
            NameInput pFirst, EditorState pState)
        {
            GameObject target = pFirst.inputField?.gameObject;
            if (target == null) return;
            if (pState.Trigger == null)
                pState.Trigger = target.GetComponent<
                                         ActorManualNameEditTrigger>() ??
                                 target.AddComponent<
                                     ActorManualNameEditTrigger>();
            pState.Trigger.Clicked = () =>
                EnterEditing(pWindow, pFirst, pState);
            pState.Trigger.IsAnyEditorFieldFocused = () =>
                pFirst.inputField != null && pFirst.inputField.isFocused ||
                pState.Second?.inputField != null &&
                pState.Second.inputField.isFocused;
            pState.Trigger.FocusLost = () =>
                CommitAndCollapse(pWindow, pFirst, pState);
            pState.Trigger.Disabled = () =>
                CloseEditor(pWindow, pFirst, pState);
        }

        private static void ShowDisplay(UnitWindow pWindow,
            NameInput pFirst, EditorState pState)
        {
            if (!pState.LayoutCaptured || pState.Second == null) return;
            Actor actor = pWindow?.actor;
            RectTransform first = pFirst.GetComponent<RectTransform>();
            if (actor?.data == null || first == null) return;
            if (!ActorManualNameInputSynchronizer.CanRewrite(
                    pFirst.inputField))
            {
                pState.Trigger?.ScheduleRetry(() =>
                    ShowDisplay(pWindow, pFirst, pState));
                return;
            }

            pState.Suppress = true;
            try
            {
                pState.State = ActorManualNameEditorState.Display;
                pFirst.inputField.onEndEdit.RemoveAllListeners();
                pState.Second.inputField.onEndEdit.RemoveAllListeners();
                first.sizeDelta = pState.OriginalSize;
                first.anchoredPosition = pState.OriginalPosition;
                pFirst.can_be_empty = false;
                ActorManualNameInputSynchronizer.TryRewrite(
                    pFirst.inputField, actor.getName().Trim());
                SetLabelVisible(pFirst, false);
                SetLabelVisible(pState.Second, false);
                pState.Second.gameObject.SetActive(false);
                if (actor.data.custom_name) pFirst.SetOutline();
            }
            finally
            {
                pState.Suppress = false;
            }
        }

        private static void EnterEditing(UnitWindow pWindow,
            NameInput pFirst, EditorState pState)
        {
            Actor actor = pWindow?.actor;
            if (pState.Suppress || actor?.data == null || actor.isRekt() ||
                actor.data.id != pState.ActorId) return;
            ActorManualNameEditorState next = ActorManualNameEditorRules
                .Resolve(pState.State,
                    ActorManualNameEditorEvent.NameSelected,
                    anyEditorFieldFocused: true);
            if (pState.State == next) return;
            if (!ActorManualNameInputSynchronizer.CanRewrite(
                    pFirst.inputField) ||
                !ActorManualNameInputSynchronizer.CanRewrite(
                    pState.Second?.inputField))
            {
                pState.Trigger?.ScheduleRetry(() =>
                    EnterEditing(pWindow, pFirst, pState));
                return;
            }
            pState.State = next;
            Layout(pFirst, pState);
            PopulateEditing(pWindow, actor, pFirst, pState);
            try
            {
                pFirst.inputField.Select();
                pFirst.inputField.ActivateInputField();
            }
            catch { }
        }

        private static void Layout(NameInput pFirst, EditorState pState)
        {
            if (!pState.LayoutCaptured || pState.Second == null) return;
            RectTransform first = pFirst.GetComponent<RectTransform>();
            RectTransform second = pState.Second.GetComponent<RectTransform>();
            if (first == null || second == null) return;
            float fullWidth = pState.OriginalSize.x;
            if (fullWidth <= Gap + 40f) fullWidth = first.rect.width;
            if (fullWidth <= Gap + 40f) fullWidth = 160f;
            float fieldWidth = Mathf.Max(40f, (fullWidth - Gap) * 0.5f);
            float offset = (fieldWidth + Gap) * 0.5f;
            first.sizeDelta = new Vector2(fieldWidth,
                pState.OriginalSize.y);
            second.sizeDelta = first.sizeDelta;
            first.anchoredPosition = pState.OriginalPosition +
                                     Vector2.left * offset;
            second.anchoredPosition = pState.OriginalPosition +
                                      Vector2.right * offset;
            second.localScale = pFirst.transform.localScale;
            pState.Second.gameObject.SetActive(true);
        }

        private static void PopulateEditing(UnitWindow pWindow,
            Actor pActor, NameInput pFirst, EditorState pState)
        {
            pState.Suppress = true;
            try
            {
                pFirst.inputField.onEndEdit.RemoveAllListeners();
                pState.Second.inputField.onEndEdit.RemoveAllListeners();
                pState.Mode = ActorManualRenameService.ResolveMode(pActor);
                bool xiaMode = pState.Mode == ActorManualNameMode.Xia;
                ActorManualNameDraft draft = ActorManualRenameService
                    .Capture(pActor);
                pFirst.can_be_empty = xiaMode;
                pState.Second.can_be_empty = !xiaMode;
                ActorManualNameInputSynchronizer.TryRewrite(
                    pFirst.inputField, xiaMode
                    ? draft.FamilyOrClanName
                    : draft.GivenName);
                ActorManualNameInputSynchronizer.TryRewrite(
                    pState.Second.inputField, xiaMode
                    ? draft.GivenName
                    : draft.FamilyOrClanName);
                SetFieldLabel(pFirst, xiaMode
                    ? "aw_actor_name_family_or_shi"
                    : "aw_actor_name_given");
                SetFieldLabel(pState.Second, xiaMode
                    ? "aw_actor_name_given"
                    : "aw_actor_name_family_or_shi");
                if (pActor.data.custom_name)
                {
                    pFirst.SetOutline();
                    pState.Second.SetOutline();
                }
            }
            finally
            {
                pState.Suppress = false;
            }

            pFirst.inputField.onEndEdit.AddListener(_ =>
                ScheduleFocusCheck(pState));
            pState.Second.inputField.onEndEdit.AddListener(_ =>
                ScheduleFocusCheck(pState));
        }

        private static void ScheduleFocusCheck(EditorState pState)
        {
            if (pState.Suppress ||
                pState.State != ActorManualNameEditorState.Editing) return;
            pState.Trigger?.ScheduleFocusCheck();
        }

        private static void CommitAndCollapse(UnitWindow pWindow,
            NameInput pFirst, EditorState pState)
        {
            if (pState.Suppress ||
                pState.State != ActorManualNameEditorState.Editing) return;
            Actor actor = pWindow?.actor;
            if (actor?.data == null || actor.data.id != pState.ActorId)
            {
                pState.State = ActorManualNameEditorState.Display;
                return;
            }

            ActorManualNameEditorState next = ActorManualNameEditorRules
                .Resolve(pState.State,
                    ActorManualNameEditorEvent.FocusChanged,
                    anyEditorFieldFocused: false);
            if (next != ActorManualNameEditorState.Display) return;
            if (!ActorManualRenameService.TryCommit(actor,
                    pFirst.inputField.text,
                    pState.Second.inputField.text, out string error))
            {
                if (!string.IsNullOrEmpty(error))
                    WorldTip.showNow(AW_L10n.Text(
                            "aw_actor_name_invalid",
                            "Given name cannot be empty"),
                        pTranslate: false, "top");
                FocusGivenField(pFirst, pState);
                return;
            }
            ShowDisplay(pWindow, pFirst, pState);
        }

        private static void CloseEditor(UnitWindow pWindow,
            NameInput pFirst, EditorState pState)
        {
            if (pState.Suppress ||
                pState.State != ActorManualNameEditorState.Editing) return;
            CommitAndCollapse(pWindow, pFirst, pState);
            pState.State = ActorManualNameEditorRules.Resolve(pState.State,
                ActorManualNameEditorEvent.WindowClosed,
                anyEditorFieldFocused: false);
        }

        private static void FocusGivenField(NameInput pFirst,
            EditorState pState)
        {
            NameInput given = pState.Mode == ActorManualNameMode.Xia
                ? pState.Second
                : pFirst;
            try
            {
                given.inputField.Select();
                given.inputField.ActivateInputField();
            }
            catch { }
        }

        private static void SetFieldLabel(NameInput pInput, string pKey)
        {
            string text = AW_L10n.Text(pKey, pKey);
            if (pInput.inputField.placeholder is Text placeholder)
                placeholder.text = text;
            Transform existing = pInput.transform.Find(LabelName);
            Text label = existing?.GetComponent<Text>();
            if (label == null)
            {
                var obj = new GameObject(LabelName, typeof(RectTransform),
                    typeof(Text));
                obj.transform.SetParent(pInput.transform, false);
                label = obj.GetComponent<Text>();
                label.font = pInput.textField.font;
                label.fontSize = Mathf.Max(8, pInput.textField.fontSize - 3);
                label.alignment = TextAnchor.LowerLeft;
                label.raycastTarget = false;
                RectTransform rect = label.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(2f, 1f);
                rect.sizeDelta = new Vector2(-4f, 14f);
            }
            label.gameObject.SetActive(true);
            label.text = text;
            label.color = pInput.textField.color;
        }

        private static void SetLabelVisible(NameInput pInput, bool pVisible)
        {
            Transform label = pInput?.transform.Find(LabelName);
            if (label != null) label.gameObject.SetActive(pVisible);
        }

        private sealed class EditorState
        {
            internal NameInput Second;
            internal ActorManualNameEditTrigger Trigger;
            internal Vector2 OriginalSize;
            internal Vector2 OriginalPosition;
            internal bool LayoutCaptured;
            internal bool Suppress;
            internal long ActorId = -1L;
            internal ActorManualNameMode Mode;
            internal ActorManualNameEditorState State =
                ActorManualNameEditorState.Display;
        }
    }

    internal sealed class ActorManualNameEditTrigger : MonoBehaviour,
        IPointerClickHandler
    {
        internal Action Clicked;
        internal Func<bool> IsAnyEditorFieldFocused;
        internal Action FocusLost;
        internal Action Disabled;
        private bool _focusCheckPending;
        private Action _retry;

        public void OnPointerClick(PointerEventData pEventData)
        {
            Clicked?.Invoke();
        }

        internal void ScheduleFocusCheck()
        {
            _focusCheckPending = true;
        }

        internal void ScheduleRetry(Action pRetry)
        {
            _retry = pRetry;
        }

        private void LateUpdate()
        {
            Action retry = _retry;
            _retry = null;
            retry?.Invoke();
            if (!_focusCheckPending) return;
            _focusCheckPending = false;
            bool isFocused = IsAnyEditorFieldFocused?.Invoke() ?? false;
            if (!isFocused) FocusLost?.Invoke();
        }

        private void OnDisable()
        {
            _focusCheckPending = false;
            _retry = null;
            Disabled?.Invoke();
        }
    }
}
