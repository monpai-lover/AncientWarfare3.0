using System;
using System.Runtime.CompilerServices;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.ui;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.patch.naming
{
    /// <summary>
    ///     城市名横幅点开后拆成两个输入框：左=城市本名，右=州名。
    ///
    ///     与 actor 的「姓/氏 + 名」双框（<see cref="AW_ActorManualNamePatch"/>）
    ///     完全同构 —— 平时显示合并后的单个名字，点击进入编辑态才展开成两栏，
    ///     失焦即提交并收起。这里复用那套的布局、触发器与状态机语义。
    ///
    ///     提交走权威命令 <c>RenameCityState</c>，不直接写 City.data.name，
    ///     这样多人游戏下由主机裁决，且州名的持久化与历史记录由
    ///     <see cref="CityStateRenameService"/> 统一负责。
    ///
    ///     钩点选 <c>WindowMetaGeneric&lt;City, CityData&gt;.loadNameInput</c>：
    ///     它是 virtual、每次开窗都跑，且此时 _name_input 已就绪。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_CityStateNamePatch
    {
        private const string SecondInputName = "AW_CityStateNameSecondInput";
        private const string LabelName = "AW_CityStateNameLabel";
        private const float Gap = 6f;

        private static readonly
            ConditionalWeakTable<CityWindow, EditorState> States =
                new ConditionalWeakTable<CityWindow, EditorState>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(WindowMetaGeneric<City, CityData>),
            "loadNameInput")]
        private static void LoadNameInput_Postfix(
            WindowMetaGeneric<City, CityData> __instance)
        {
            if (!(__instance is CityWindow window)) return;
            City city = SelectedMetas.selected_city;
            NameInput first = AccessNameInput(__instance);
            if (city?.data == null || city.isRekt() || first == null) return;

            EditorState state = States.GetOrCreateValue(window);
            EnsureSecondInput(first, state);
            EnsureTrigger(window, first, state);
            if (state.Second == null || state.Trigger == null) return;
            state.CityId = city.data.id;
            ShowDisplay(window, first, state);
        }

        private static NameInput AccessNameInput(
            WindowMetaGeneric<City, CityData> pWindow)
        {
            try
            {
                return pWindow?.gameObject.transform
                    .FindRecursive("NameInputElement")?
                    .GetComponent<NameInput>();
            }
            catch { return null; }
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

        private static void EnsureTrigger(CityWindow pWindow,
            NameInput pFirst, EditorState pState)
        {
            GameObject target = pFirst.inputField?.gameObject;
            if (target == null) return;
            if (pState.Trigger == null)
                pState.Trigger =
                    target.GetComponent<CityStateNameEditTrigger>() ??
                    target.AddComponent<CityStateNameEditTrigger>();
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

        private static void ShowDisplay(CityWindow pWindow, NameInput pFirst,
            EditorState pState)
        {
            if (!pState.LayoutCaptured || pState.Second == null) return;
            City city = ResolveCity(pState.CityId);
            RectTransform first = pFirst.GetComponent<RectTransform>();
            if (city?.data == null || first == null) return;

            pState.Suppress = true;
            try
            {
                pState.State = CityStateNameEditorState.Display;
                pFirst.inputField.onEndEdit.RemoveAllListeners();
                pState.Second.inputField.onEndEdit.RemoveAllListeners();
                first.sizeDelta = pState.OriginalSize;
                first.anchoredPosition = pState.OriginalPosition;
                pFirst.can_be_empty = false;
                pFirst.setText((city.data.name ?? string.Empty).Trim());
                SetLabelVisible(pFirst, false);
                SetLabelVisible(pState.Second, false);
                pState.Second.gameObject.SetActive(false);
                if (city.data.custom_name) pFirst.SetOutline();
            }
            finally
            {
                pState.Suppress = false;
            }
        }

        private static void EnterEditing(CityWindow pWindow, NameInput pFirst,
            EditorState pState)
        {
            City city = ResolveCity(pState.CityId);
            if (pState.Suppress || city?.data == null || city.isRekt())
                return;
            CityStateNameEditorState next = CityStateNameEditorRules.Resolve(
                pState.State, CityStateNameEditorEvent.NameSelected,
                pAnyEditorFieldFocused: true);
            if (pState.State == next) return;
            pState.State = next;
            Layout(pFirst, pState);
            PopulateEditing(city, pFirst, pState);
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
            RectTransform second =
                pState.Second.GetComponent<RectTransform>();
            if (first == null || second == null) return;
            float fullWidth = pState.OriginalSize.x;
            if (fullWidth <= Gap + 40f) fullWidth = first.rect.width;
            if (fullWidth <= Gap + 40f) fullWidth = 160f;
            float fieldWidth = Mathf.Max(40f, (fullWidth - Gap) * 0.5f);
            float offset = (fieldWidth + Gap) * 0.5f;
            first.sizeDelta = new Vector2(fieldWidth, pState.OriginalSize.y);
            second.sizeDelta = first.sizeDelta;
            first.anchoredPosition =
                pState.OriginalPosition + Vector2.left * offset;
            second.anchoredPosition =
                pState.OriginalPosition + Vector2.right * offset;
            second.localScale = pFirst.transform.localScale;
            pState.Second.gameObject.SetActive(true);
        }

        private static void PopulateEditing(City pCity, NameInput pFirst,
            EditorState pState)
        {
            pState.HasRegion = DeJureRegionStore.TryGetForCity(
                pCity.data.id, out DeJureRegion region);
            pState.Suppress = true;
            try
            {
                pFirst.inputField.onEndEdit.RemoveAllListeners();
                pState.Second.inputField.onEndEdit.RemoveAllListeners();
                pFirst.can_be_empty = false;
                pState.Second.can_be_empty = !pState.HasRegion;
                pFirst.setText((pCity.data.name ?? string.Empty).Trim());
                pState.Second.setText(
                    (region?.RegionName ?? string.Empty).Trim());
                SetFieldLabel(pFirst, "aw_city_state_name_city");
                SetFieldLabel(pState.Second, "aw_city_state_name_state");
                // 无 region 的城市没有州名可编辑，右栏直接隐藏。
                pState.Second.gameObject.SetActive(pState.HasRegion);
                if (pCity.data.custom_name)
                {
                    pFirst.SetOutline();
                    if (pState.HasRegion) pState.Second.SetOutline();
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
                pState.State != CityStateNameEditorState.Editing) return;
            pState.Trigger?.ScheduleFocusCheck();
        }

        private static void CommitAndCollapse(CityWindow pWindow,
            NameInput pFirst, EditorState pState)
        {
            if (pState.Suppress ||
                pState.State != CityStateNameEditorState.Editing) return;
            City city = ResolveCity(pState.CityId);
            if (city?.data == null || city.isRekt())
            {
                pState.State = CityStateNameEditorState.Display;
                return;
            }

            CityStateNameEditorState next = CityStateNameEditorRules.Resolve(
                pState.State, CityStateNameEditorEvent.FocusChanged,
                pAnyEditorFieldFocused: false);
            if (next != CityStateNameEditorState.Display) return;

            CityStateNameDraft draft = CityStateNameFieldRules.CreateDraft(
                pState.HasRegion, pFirst.inputField.text,
                pState.Second.inputField.text);
            if (!draft.IsValid)
            {
                WorldTip.showNow(AW_L10n.Text(
                        draft.CityName.Length == 0
                            ? "aw_city_state_rename_empty_city"
                            : "aw_city_state_rename_empty_state",
                        "Name cannot be empty"),
                    pTranslate: false, "top");
                FocusEmptyField(pFirst, pState, draft);
                return;
            }

            if (!Commit(city, draft)) return;
            ShowDisplay(pWindow, pFirst, pState);
        }

        /// <summary>
        ///     两个字段都没变就不派发（否则服务会返回 NoChange 当成失败）。
        ///     失败时保持编辑态，让玩家能改回去。
        /// </summary>
        private static bool Commit(City pCity, CityStateNameDraft pDraft)
        {
            bool hasRegion = DeJureRegionStore.TryGetForCity(pCity.data.id,
                out DeJureRegion region);
            bool cityChanged = !string.Equals(
                (pCity.data.name ?? string.Empty).Trim(), pDraft.CityName,
                StringComparison.Ordinal);
            bool stateChanged = hasRegion && !string.Equals(
                (region?.RegionName ?? string.Empty).Trim(), pDraft.StateName,
                StringComparison.Ordinal);
            if (!cityChanged && !stateChanged) return true;

            long kingdomId = pCity.kingdom?.id ?? -1L;
            if (kingdomId < 0L) return true;

            AW3CommandResult result = AW3MultiplayerCommandFacade
                .DispatchFromUi(AW3CommandRequest.RenameCityState(kingdomId,
                    pCity.data.id, pDraft.CityName, pDraft.StateName));
            if (result.Status == AW3CommandStatus.Accepted ||
                result.Status == AW3CommandStatus.Pending) return true;

            WorldTip.showNow(AW_L10n.Text("aw_city_state_rename_failed",
                "Rename failed"), pTranslate: false, "top");
            return false;
        }

        private static void CloseEditor(CityWindow pWindow, NameInput pFirst,
            EditorState pState)
        {
            if (pState.Suppress ||
                pState.State != CityStateNameEditorState.Editing) return;
            CommitAndCollapse(pWindow, pFirst, pState);
            pState.State = CityStateNameEditorRules.Resolve(pState.State,
                CityStateNameEditorEvent.WindowClosed,
                pAnyEditorFieldFocused: false);
        }

        private static void FocusEmptyField(NameInput pFirst,
            EditorState pState, CityStateNameDraft pDraft)
        {
            NameInput target = pDraft.CityName.Length == 0
                ? pFirst
                : pState.Second;
            try
            {
                target.inputField.Select();
                target.inputField.ActivateInputField();
            }
            catch { }
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId < 0L) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
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
            internal CityStateNameEditTrigger Trigger;
            internal Vector2 OriginalSize;
            internal Vector2 OriginalPosition;
            internal bool LayoutCaptured;
            internal bool Suppress;
            internal bool HasRegion;
            internal long CityId = -1L;
            internal CityStateNameEditorState State =
                CityStateNameEditorState.Display;
        }
    }

    internal sealed class CityStateNameEditTrigger : MonoBehaviour,
        IPointerClickHandler
    {
        internal Action Clicked;
        internal Func<bool> IsAnyEditorFieldFocused;
        internal Action FocusLost;
        internal Action Disabled;
        private bool _focusCheckPending;

        public void OnPointerClick(PointerEventData pEventData)
        {
            Clicked?.Invoke();
        }

        internal void ScheduleFocusCheck()
        {
            _focusCheckPending = true;
        }

        private void LateUpdate()
        {
            if (!_focusCheckPending) return;
            _focusCheckPending = false;
            bool isFocused = IsAnyEditorFieldFocused?.Invoke() ?? false;
            if (!isFocused) FocusLost?.Invoke();
        }

        private void OnDisable()
        {
            _focusCheckPending = false;
            Disabled?.Invoke();
        }
    }
}
