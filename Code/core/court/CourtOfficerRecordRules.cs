namespace AncientWarfare3.core.court
{
    public static class CourtOfficerRecordRules
    {
        public static bool ShouldInsertNewActiveRecord(bool hasActiveRecord, bool sameKingdom,
            bool sameOffice, bool sameLayer)
        {
            return !hasActiveRecord || !sameKingdom || !sameOffice || !sameLayer;
        }

        public static bool ShouldCloseActiveRecord(bool hasActiveRecord)
        {
            return hasActiveRecord;
        }

        public static int ActiveFlag(bool pActive)
        {
            return pActive ? 1 : 0;
        }
    }
}
