using System;

namespace AncientWarfare3.core.court
{
    /// <summary>
    ///     城市名横幅进入编辑态后拆成两个输入框（城市本名 / 州名）时的
    ///     编辑器状态机。与 <c>ActorManualNameEditorRules</c> 同构 ——
    ///     actor 的「姓/氏 + 名」双框用的就是这套，这里照搬语义，
    ///     避免两处行为不一致。
    /// </summary>
    internal enum CityStateNameEditorState
    {
        Display,
        Editing
    }

    internal enum CityStateNameEditorEvent
    {
        NameSelected,
        FocusChanged,
        WindowClosed
    }

    internal static class CityStateNameEditorRules
    {
        internal static CityStateNameEditorState Resolve(
            CityStateNameEditorState pCurrent,
            CityStateNameEditorEvent pEvent,
            bool pAnyEditorFieldFocused)
        {
            if (pEvent == CityStateNameEditorEvent.NameSelected)
                return CityStateNameEditorState.Editing;
            if (pEvent == CityStateNameEditorEvent.WindowClosed)
                return CityStateNameEditorState.Display;
            if (pEvent == CityStateNameEditorEvent.FocusChanged &&
                pCurrent == CityStateNameEditorState.Editing &&
                !pAnyEditorFieldFocused)
                return CityStateNameEditorState.Display;
            return pCurrent;
        }
    }

    /// <summary>
    ///     一次双字段编辑的快照。<see cref="HasRegion"/> 为假时该城市不属于
    ///     任何 de jure region，州名字段不参与校验也不提交。
    /// </summary>
    internal sealed class CityStateNameDraft
    {
        internal CityStateNameDraft(bool pIsValid, bool pHasRegion,
            string pCityName, string pStateName)
        {
            IsValid = pIsValid;
            HasRegion = pHasRegion;
            CityName = pCityName ?? string.Empty;
            StateName = pStateName ?? string.Empty;
        }

        internal bool IsValid { get; }
        internal bool HasRegion { get; }
        internal string CityName { get; }
        internal string StateName { get; }
    }

    internal static class CityStateNameFieldRules
    {
        /// <summary>
        ///     两个输入框 → 校验过的草稿。城市名恒为必填；州名只有在城市
        ///     确实归属某个 region 时才必填，否则原样丢弃。
        /// </summary>
        internal static CityStateNameDraft CreateDraft(bool pHasRegion,
            string pCityField, string pStateField)
        {
            string city = CityStateRenameRules.Normalize(pCityField);
            string state = pHasRegion
                ? CityStateRenameRules.Normalize(pStateField)
                : string.Empty;
            bool valid = city.Length > 0 &&
                         (!pHasRegion || state.Length > 0);
            return new CityStateNameDraft(valid, pHasRegion, city, state);
        }
    }
}
