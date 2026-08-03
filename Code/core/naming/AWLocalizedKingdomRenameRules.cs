namespace AncientWarfare3.core.naming
{
    internal sealed class AWLocalizedNameEditDecision
    {
        internal AWLocalizedNameEditDecision(string pNativeName,
            string pChineseName)
        {
            NativeName = pNativeName ?? string.Empty;
            ChineseName = pChineseName ?? string.Empty;
        }

        internal string NativeName { get; }
        internal string ChineseName { get; }
    }

    internal static class AWLocalizedKingdomRenameRules
    {
        internal static AWLocalizedNameEditDecision ResolveEdit(
            string pLanguage, string pEditedName, string pNativeName,
            string pChineseName)
        {
            string edited = (pEditedName ?? string.Empty).Trim();
            string native = (pNativeName ?? string.Empty).Trim();
            string chinese = (pChineseName ?? string.Empty).Trim();
            return AWNamingLanguageRules.IsChinesePresentation(pLanguage)
                ? new AWLocalizedNameEditDecision(native, edited)
                : new AWLocalizedNameEditDecision(edited, chinese);
        }

        internal static string ResolveSharedProjection(string pLanguage,
            string pAuthorityNativeName, string pAuthorityChineseName,
            string pLegacyFallback)
        {
            string projected = AWLocalizedNameProjectionRules.Select(
                pLanguage, pAuthorityNativeName, pAuthorityChineseName);
            return string.IsNullOrWhiteSpace(projected)
                ? (pLegacyFallback ?? string.Empty).Trim()
                : projected;
        }

        internal static string ResolveSettlementBase(
            string pOriginalProjection, string pRivalProjection,
            string pLegacyFallback)
        {
            if (!string.IsNullOrWhiteSpace(pOriginalProjection))
                return pOriginalProjection.Trim();
            return !string.IsNullOrWhiteSpace(pRivalProjection)
                ? pRivalProjection.Trim()
                : (pLegacyFallback ?? string.Empty).Trim();
        }

        internal static bool ShouldCommitManualEdit(bool originalAccepted,
            bool hasManualEditContext)
        {
            return originalAccepted && hasManualEditContext;
        }
    }
}
