namespace AncientWarfare3.core.naming
{
    internal enum AWLocalizedNameLegacySource
    {
        Unknown,
        NativeOriginal,
        ChineseProjection
    }

    internal sealed class AWLocalizedNameMigrationDecision
    {
        internal AWLocalizedNameMigrationDecision(string pNativeName,
            string pChineseName, int pSchemaVersion,
            bool pNeedsNativeGeneration, bool pNeedsChineseGeneration,
            bool pNeedsPersistence, bool pDeferredForEvidence,
            string pProjectedName)
        {
            NativeName = pNativeName ?? string.Empty;
            ChineseName = pChineseName ?? string.Empty;
            SchemaVersion = pSchemaVersion;
            NeedsNativeGeneration = pNeedsNativeGeneration;
            NeedsChineseGeneration = pNeedsChineseGeneration;
            NeedsPersistence = pNeedsPersistence;
            DeferredForEvidence = pDeferredForEvidence;
            ProjectedName = pProjectedName ?? string.Empty;
        }

        internal string NativeName { get; }
        internal string ChineseName { get; }
        internal int SchemaVersion { get; }
        internal bool NeedsNativeGeneration { get; }
        internal bool NeedsChineseGeneration { get; }
        internal bool NeedsPersistence { get; }
        internal bool DeferredForEvidence { get; }
        internal string ProjectedName { get; }
    }

    internal static class AWLocalizedNameMigrationRules
    {
        internal const int CurrentSchemaVersion = 1;

        internal static AWLocalizedNameMigrationDecision Resolve(
            string pCurrentDisplayName, string pNativeName,
            string pChineseName, int pSchemaVersion,
            AWLocalizedNameLegacySource pLegacySource, string pLanguage)
        {
            string current = (pCurrentDisplayName ?? string.Empty).Trim();
            string native = (pNativeName ?? string.Empty).Trim();
            string chinese = (pChineseName ?? string.Empty).Trim();
            if (native.Length == 0 && chinese.Length == 0)
            {
                if (pLegacySource == AWLocalizedNameLegacySource.Unknown)
                    return new AWLocalizedNameMigrationDecision(native,
                        chinese, pSchemaVersion, false, false, false, true,
                        current);
                if (pLegacySource ==
                    AWLocalizedNameLegacySource.ChineseProjection)
                    chinese = current;
                else
                    native = current;
            }

            bool chinesePresentation = AWNamingLanguageRules
                .IsChinesePresentation(pLanguage);
            bool needsNativeGeneration = native.Length == 0 &&
                chinese.Length > 0 && !chinesePresentation;
            bool needsChineseGeneration = chinese.Length == 0 &&
                native.Length > 0 && chinesePresentation;
            int schema = CurrentSchemaVersion;
            string projected = AWLocalizedNameProjectionRules.Select(pLanguage,
                native, chinese);
            bool changed = pSchemaVersion != schema ||
                !string.Equals(native, pNativeName ?? string.Empty,
                    System.StringComparison.Ordinal) ||
                !string.Equals(chinese, pChineseName ?? string.Empty,
                    System.StringComparison.Ordinal);

            return new AWLocalizedNameMigrationDecision(native, chinese,
                schema, needsNativeGeneration, needsChineseGeneration, changed,
                false, projected);
        }
    }
}
