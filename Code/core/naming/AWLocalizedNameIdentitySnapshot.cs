namespace AncientWarfare3.core.naming
{
    internal sealed class AWLocalizedNameIdentitySnapshot
    {
        internal AWLocalizedNameIdentitySnapshot(string pNativeName,
            string pChineseName, string pGivenName,
            string pFamilyComponent, string pGeneratorId, long pCultureId,
            int pSchemaVersion)
        {
            NativeName = pNativeName ?? string.Empty;
            ChineseName = pChineseName ?? string.Empty;
            GivenName = pGivenName ?? string.Empty;
            FamilyComponent = pFamilyComponent ?? string.Empty;
            GeneratorId = pGeneratorId ?? string.Empty;
            CultureId = pCultureId;
            SchemaVersion = pSchemaVersion;
        }

        internal string NativeName { get; }
        internal string ChineseName { get; }
        internal string GivenName { get; }
        internal string FamilyComponent { get; }
        internal string GeneratorId { get; }
        internal long CultureId { get; }
        internal int SchemaVersion { get; }

        internal AWLocalizedNameIdentitySnapshot WithNamesAndSchema(
            string pNativeName, string pChineseName, int pSchemaVersion)
        {
            return new AWLocalizedNameIdentitySnapshot(pNativeName,
                pChineseName, GivenName, FamilyComponent, GeneratorId,
                CultureId, pSchemaVersion);
        }
    }
}
