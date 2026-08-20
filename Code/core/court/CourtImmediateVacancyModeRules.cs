namespace AncientWarfare3.core.court
{
    internal enum CourtImmediateVacancyMode : byte
    {
        DirectFill = 0,
        QueueWesternElection = 1
    }

    internal static class CourtImmediateVacancyModeRules
    {
        internal static CourtImmediateVacancyMode Resolve(
            bool pWesternElective)
        {
            return pWesternElective
                ? CourtImmediateVacancyMode.QueueWesternElection
                : CourtImmediateVacancyMode.DirectFill;
        }

        internal static bool ShouldReportQueued(int pNewlyQueued)
        {
            return pNewlyQueued > 0;
        }

        internal static bool ShouldReportFilled(int pFilled)
        {
            return pFilled > 0;
        }

        internal static bool IsCentralEntry(long pCityId)
        {
            return pCityId < 0L;
        }
    }
}
