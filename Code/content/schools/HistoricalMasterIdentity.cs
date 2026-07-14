namespace AncientWarfare3.content.schools
{
    public enum HistoricalMasterFamilyEvidence
    {
        Unknown = 0,
        KnownDistinct = 1,
        KnownSame = 2
    }

    public readonly struct HistoricalMasterCanonicalIdentity
    {
        public HistoricalMasterCanonicalIdentity(string pCanonicalName, string pShiName,
            string pGivenName, string pFamilyName,
            HistoricalMasterFamilyEvidence pFamilyEvidence, bool pMilitaryEligible)
        {
            CanonicalName = pCanonicalName ?? "";
            ShiName = pShiName ?? "";
            GivenName = pGivenName ?? "";
            FamilyName = pFamilyName ?? "";
            FamilyEvidence = pFamilyEvidence;
            MilitaryEligible = pMilitaryEligible;
        }

        public string CanonicalName { get; }
        public string ShiName { get; }
        public string GivenName { get; }
        public string FamilyName { get; }
        public HistoricalMasterFamilyEvidence FamilyEvidence { get; }
        public bool MilitaryEligible { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(CanonicalName) &&
                               !string.IsNullOrWhiteSpace(ShiName) &&
                               !string.IsNullOrWhiteSpace(GivenName) &&
                               CanonicalName == ShiName + GivenName && FamilyIsValid();

        private bool FamilyIsValid()
        {
            if (FamilyEvidence == HistoricalMasterFamilyEvidence.Unknown)
                return string.IsNullOrEmpty(FamilyName);
            if (string.IsNullOrWhiteSpace(FamilyName)) return false;
            if (FamilyEvidence == HistoricalMasterFamilyEvidence.KnownSame)
                return FamilyName == ShiName;
            return FamilyEvidence == HistoricalMasterFamilyEvidence.KnownDistinct &&
                   FamilyName != ShiName;
        }
    }
}
