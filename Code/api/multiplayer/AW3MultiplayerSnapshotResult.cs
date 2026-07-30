namespace AncientWarfare3.api.multiplayer
{
    public sealed class AW3MultiplayerSnapshotResult
    {
        private AW3MultiplayerSnapshotResult(
            AW3MultiplayerSnapshotError pError, string pDetail,
            string pPendingDirectory, string pMapPath, string pStatsPath,
            string pLineageArchivePath)
        {
            Error = pError;
            Detail = pDetail ?? string.Empty;
            PendingDirectory = pPendingDirectory ?? string.Empty;
            MapPath = pMapPath ?? string.Empty;
            StatsPath = pStatsPath ?? string.Empty;
            LineageArchivePath = pLineageArchivePath ?? string.Empty;
        }

        public bool IsSuccess => Error == AW3MultiplayerSnapshotError.None;
        public AW3MultiplayerSnapshotError Error { get; }
        public string Detail { get; }
        public string PendingDirectory { get; }
        public string MapPath { get; }
        public string StatsPath { get; }
        public string LineageArchivePath { get; }

        internal static AW3MultiplayerSnapshotResult Failure(
            AW3MultiplayerSnapshotError pError, string pDetail)
        {
            return new AW3MultiplayerSnapshotResult(pError, pDetail,
                string.Empty, string.Empty, string.Empty, string.Empty);
        }

        internal static AW3MultiplayerSnapshotResult Success(
            string pPendingDirectory, string pMapPath, string pStatsPath,
            string pLineageArchivePath)
        {
            return new AW3MultiplayerSnapshotResult(
                AW3MultiplayerSnapshotError.None, string.Empty,
                pPendingDirectory, pMapPath, pStatsPath, pLineageArchivePath);
        }
    }
}
