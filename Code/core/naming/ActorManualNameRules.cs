using System;

namespace AncientWarfare3.core.naming
{
    internal enum ActorManualNameMode
    {
        Xia,
        SiniticMerged,
        NonXia
    }

    internal enum ActorManualNameEditorState
    {
        Display,
        Editing
    }

    internal enum ActorManualNameEditorEvent
    {
        NameSelected,
        FocusChanged,
        WindowClosed
    }

    internal static class ActorManualNameEditorRules
    {
        internal static ActorManualNameEditorState Resolve(
            ActorManualNameEditorState pCurrent,
            ActorManualNameEditorEvent pEvent,
            bool anyEditorFieldFocused)
        {
            if (pEvent == ActorManualNameEditorEvent.NameSelected)
                return ActorManualNameEditorState.Editing;
            if (pEvent == ActorManualNameEditorEvent.WindowClosed)
                return ActorManualNameEditorState.Display;
            if (pEvent == ActorManualNameEditorEvent.FocusChanged &&
                pCurrent == ActorManualNameEditorState.Editing &&
                !anyEditorFieldFocused)
                return ActorManualNameEditorState.Display;
            return pCurrent;
        }
    }

    internal sealed class ActorManualNameDraft
    {
        internal ActorManualNameDraft(bool pIsValid, string pGivenName,
            string pFamilyOrClanName, string pDisplayName)
        {
            IsValid = pIsValid;
            GivenName = pGivenName ?? string.Empty;
            FamilyOrClanName = pFamilyOrClanName ?? string.Empty;
            DisplayName = pDisplayName ?? string.Empty;
        }

        internal bool IsValid { get; }
        internal string GivenName { get; }
        internal string FamilyOrClanName { get; }
        internal string DisplayName { get; }
    }

    internal static class ActorManualNameRules
    {
        internal static ActorManualNameDraft CreateDraft(
            ActorManualNameMode pMode, string pFirstField, string pSecondField)
        {
            string first = Normalize(pFirstField);
            string second = Normalize(pSecondField);
            bool familyFirst = pMode != ActorManualNameMode.NonXia;
            string family = familyFirst ? first : second;
            string given = familyFirst ? second : first;
            bool valid = given.Length > 0;
            return new ActorManualNameDraft(valid, given, family,
                valid ? CreateDisplayName(pMode, given, family) : string.Empty);
        }

        internal static string CreateDisplayName(ActorManualNameMode pMode,
            string pGivenName, string pFamilyOrClanName)
        {
            string given = Normalize(pGivenName);
            string family = Normalize(pFamilyOrClanName);
            if (given.Length == 0) return family;
            if (family.Length == 0) return given;
            return pMode != ActorManualNameMode.NonXia
                ? family + given
                : given + " " + family;
        }

        private static string Normalize(string pValue)
        {
            return string.Join(" ", (pValue ?? string.Empty)
                .Trim()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
