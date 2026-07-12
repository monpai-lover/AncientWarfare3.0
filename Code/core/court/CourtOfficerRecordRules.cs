namespace AncientWarfare3.core.court
{
    public static class CourtOfficerRecordRules
    {
        public static bool ShouldInsertNewActiveRecord(bool hasActiveRecord, bool sameKingdom,
            bool sameOffice, bool sameLayer, bool sameCity)
        {
            return !hasActiveRecord || !sameKingdom || !sameOffice || !sameLayer || !sameCity;
        }

        public static bool ShouldInsertNewActiveRecord(bool hasActiveRecord, bool sameKingdom,
            bool sameOffice, bool sameLayer)
        {
            return ShouldInsertNewActiveRecord(hasActiveRecord, sameKingdom, sameOffice, sameLayer,
                sameCity: true);
        }

        public static bool ShouldCloseActiveRecord(bool hasActiveRecord)
        {
            return hasActiveRecord;
        }

        public static int ActiveFlag(bool pActive)
        {
            return pActive ? 1 : 0;
        }

        public static bool IsSameCareerTrack(string pExistingLayer, string pNewLayer)
        {
            return string.Equals(pExistingLayer ?? "", pNewLayer ?? "",
                System.StringComparison.Ordinal);
        }
    }
}
