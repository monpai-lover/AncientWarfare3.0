using System;

namespace AncientWarfare3.core.schools
{
    [Flags]
    public enum HistoricalSchoolRevisionMask
    {
        None = 0,
        Residence = 1,
        Presence = 2,
        Service = 4,
        Structure = 8,
        Score = 16,
        Activity = 32
    }

    public static class HistoricalSchoolRevisionRules
    {
        public static HistoricalSchoolRevisionMask ClassifyAffiliation(
            long pOldResidence,
            long pNewResidence,
            bool pOldPresent,
            bool pNewPresent,
            long pOldService,
            long pNewService)
        {
            HistoricalSchoolRevisionMask result = HistoricalSchoolRevisionMask.None;
            if (pOldResidence != pNewResidence)
                result |= HistoricalSchoolRevisionMask.Residence;
            if (pOldPresent != pNewPresent)
                result |= HistoricalSchoolRevisionMask.Presence;
            if (pOldService != pNewService)
                result |= HistoricalSchoolRevisionMask.Service;
            return result;
        }
    }
}
