using System;
using System.Runtime.CompilerServices;
using AncientWarfare3.core.naming;
using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;
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
            if (state.Second == null) return;
            Layout(first, state);
            Bind(__instance, actor, first, state);
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
            second.gameObject.SetActive(true);
        }

        private static void Bind(UnitWindow pWindow, Actor pActor,
            NameInput pFirst, EditorState pState)
        {
            pState.Suppress = true;
            try
            {
                pFirst.inputField.onEndEdit.RemoveAllListeners();
                pState.Second.inputField.onEndEdit.RemoveAllListeners();
                ActorManualNameMode mode = ActorManualRenameService
                    .ResolveMode(pActor);
                bool xiaMode = mode switch
                {
                    ActorManualNameMode.Xia => true,
                    ActorManualNameMode.NonXia => false,
                    _ => false
                };
                ActorManualNameDraft draft = ActorManualRenameService
                    .Capture(pActor);
                string firstValue = xiaMode
                    ? draft.FamilyOrClanName
                    : draft.GivenName;
                string secondValue = xiaMode
                    ? draft.GivenName
                    : draft.FamilyOrClanName;
                pFirst.can_be_empty = xiaMode;
                pState.Second.can_be_empty = !xiaMode;
                pFirst.setText(firstValue);
                pState.Second.setText(secondValue);
                SetFieldLabel(pFirst, xiaMode
                    ? "aw_actor_name_family_or_shi"
                    : "aw_actor_name_given");
                SetFieldLabel(pState.Second,
                    xiaMode
                        ? "aw_actor_name_given"
                        : "aw_actor_name_family_or_shi");
                pState.ActorId = pActor.data.id;
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
                Commit(pWindow, pFirst, pState));
            pState.Second.inputField.onEndEdit.AddListener(_ =>
                Commit(pWindow, pFirst, pState));
        }

        private static void Commit(UnitWindow pWindow, NameInput pFirst,
            EditorState pState)
        {
            if (pState.Suppress) return;
            Actor actor = pWindow?.actor;
            if (actor?.data == null || actor.data.id != pState.ActorId)
                return;
            string firstValue = pFirst.inputField.text;
            string secondValue = pState.Second.inputField.text;
            if (!ActorManualRenameService.TryCommit(actor, firstValue,
                    secondValue, out string error))
            {
                if (!string.IsNullOrEmpty(error))
                    WorldTip.showNow(AW_L10n.Text(
                        "aw_actor_name_invalid",
                        "Given name cannot be empty"),
                        pTranslate: false, "top");
                return;
            }
            Bind(pWindow, actor, pFirst, pState);
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
            label.text = text;
            label.color = pInput.textField.color;
        }

        private sealed class EditorState
        {
            internal NameInput Second;
            internal Vector2 OriginalSize;
            internal Vector2 OriginalPosition;
            internal bool LayoutCaptured;
            internal bool Suppress;
            internal long ActorId = -1L;
        }
    }
}
