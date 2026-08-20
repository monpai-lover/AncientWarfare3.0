namespace AncientWarfare3.core.court
{
    internal enum CourtImmediateVacancyOutcome : byte
    {
        InvalidKingdom = 0,
        Unavailable = 1,
        NoChange = 2,
        Filled = 3,
        Queued = 4
    }
}
