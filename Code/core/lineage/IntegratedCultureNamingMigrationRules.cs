using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    public enum IntegratedCultureNamingMigrationAction
    {
        Skip,
        RecordProfileOnly,
        ApplyGeneratedName
    }

    public static class IntegratedCultureNamingMigrationRules
    {
        public static bool ShouldUseXiaPersonalNaming(bool integrated,
            bool fullyIntegrated)
        {
            return integrated || fullyIntegrated;
        }

        public static bool ShouldApplyXiaDisplay(bool nativeXia,
            bool usesLineage, bool foreignPseudoDynasty,
            bool cultureIntegrated)
        {
            return nativeXia || usesLineage || foreignPseudoDynasty ||
                   cultureIntegrated;
        }

        public static bool ShouldStartNewXiaBranch(
            NamingProfileId sourceProfile, NamingProfileId targetProfile,
            bool surnameChanged)
        {
            bool westernSource = sourceProfile == NamingProfileId.Western ||
                                 sourceProfile == NamingProfileId.OrcNomadic;
            return westernSource && targetProfile == NamingProfileId.Xia &&
                   surnameChanged;
        }

        public static IntegratedCultureNamingMigrationAction Decide(
            bool alive, bool sameCulture, bool xiaProfile, bool alreadyXia,
            bool customName, bool authoredHistorical)
        {
            if (!alive || !sameCulture || !xiaProfile || alreadyXia)
                return IntegratedCultureNamingMigrationAction.Skip;
            if (customName || authoredHistorical)
                return IntegratedCultureNamingMigrationAction.RecordProfileOnly;
            return IntegratedCultureNamingMigrationAction.ApplyGeneratedName;
        }
    }
}
