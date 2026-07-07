namespace AncientWarfare3.core.lineage
{
    public static class ForeignOccupationDetectionRules
    {
        public const string TypeForeignEntry = "foreign_entry";
        public const string TypePseudoDynasty = "pseudo_dynasty";
        public const string TypeNormalConquest = "normal_conquest";

        public static bool TryDetectOccupation(
            bool ownerIsXia,
            bool legalCore,
            float mandateCoreControlRatio,
            bool cityHasXiaIdentity,
            bool differentCultureOrLanguage,
            bool sameOwnerOriginCity,
            out string type)
        {
            type = "";
            if (ownerIsXia) return false;

            if (sameOwnerOriginCity && !cityHasXiaIdentity && !differentCultureOrLanguage)
                return false;

            if (legalCore && mandateCoreControlRatio >= 0.65f)
            {
                type = TypePseudoDynasty;
                return true;
            }

            if (legalCore || cityHasXiaIdentity)
            {
                type = TypeForeignEntry;
                return true;
            }

            if (differentCultureOrLanguage)
            {
                type = TypeNormalConquest;
                return true;
            }

            return false;
        }
    }
}
