namespace AncientWarfare3.api.multiplayer
{
    public enum AW3MultiplayerSnapshotError : byte
    {
        None = 0,
        WrongThread = 1,
        InvalidDirectory = 2,
        DestinationNotEmpty = 3,
        SavePreparationFailed = 4,
        WorldSaveFailed = 5,
        LineageExportFailed = 6,
        MapFileMissing = 7,
        MapFileEmpty = 8,
        StatsFileMissing = 9,
        StatsFileEmpty = 10,
        LineageArchiveMissing = 11,
        LineageArchiveEmpty = 12,
        LineageArchiveSidecarPresent = 13,
        LineageArchiveQuickCheckFailed = 14,
        LineageArchiveJournalModeInvalid = 15,
        IoFailure = 16
    }
}
