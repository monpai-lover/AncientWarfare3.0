namespace AncientWarfare3.core.naming
{
    internal interface IAWLocalizedNameMigrationReadiness
    {
        bool IsGeneratorAvailable(string pGeneratorId);

        bool IsPersistedTraditionProfileReady(string pMetaType,
            long pObjectId, long pCultureId, string pGeneratorId);
    }

    internal sealed class AWLocalizedNameGenerationAdmission
    {
        internal AWLocalizedNameGenerationAdmission(bool pIsAdmitted,
            bool pDeferredForGenerator,
            bool pDeferredForTraditionProfile)
        {
            IsAdmitted = pIsAdmitted;
            DeferredForGenerator = pDeferredForGenerator;
            DeferredForTraditionProfile = pDeferredForTraditionProfile;
        }

        internal bool IsAdmitted { get; }
        internal bool DeferredForGenerator { get; }
        internal bool DeferredForTraditionProfile { get; }
    }

    internal static class AWLocalizedNameMigrationAdmissionRules
    {
        internal static AWLocalizedNameGenerationAdmission Resolve(
            string pGeneratorId, string pMetaType, long pObjectId,
            long pCultureId,
            IAWLocalizedNameMigrationReadiness pReadiness)
        {
            if (string.IsNullOrWhiteSpace(pGeneratorId))
                return new AWLocalizedNameGenerationAdmission(false, true,
                    false);
            if (pReadiness == null)
                return new AWLocalizedNameGenerationAdmission(false, false,
                    true);
            if (!pReadiness.IsGeneratorAvailable(pGeneratorId))
                return new AWLocalizedNameGenerationAdmission(false, true,
                    false);
            if (!pReadiness.IsPersistedTraditionProfileReady(pMetaType,
                    pObjectId, pCultureId, pGeneratorId))
                return new AWLocalizedNameGenerationAdmission(false, false,
                    true);
            return new AWLocalizedNameGenerationAdmission(true, false, false);
        }
    }
}
