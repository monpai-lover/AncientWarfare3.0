using System;

namespace AncientWarfare3.api.multiplayer
{
    public enum AW3MultiplayerReplicationError : byte
    {
        None = 0,
        WrongThread = 1,
        InvalidDirectory = 2,
        InvalidEpoch = 3,
        InvalidRevision = 4,
        InvalidArchive = 5,
        HashMismatch = 6,
        CaptureFailed = 7,
        InstallFailed = 8,
        ProjectionRebuildFailed = 9,
        WindowRefreshFailed = 10
    }

    public static class AW3MultiplayerReplicationMetadata
    {
        public const int CatalogVersion = 1;
    }

    public sealed class AW3MultiplayerArchiveCaptureResult
    {
        private readonly byte[] _sha256;

        internal AW3MultiplayerArchiveCaptureResult(
            AW3MultiplayerReplicationError pError, string pDetail,
            Guid pEpoch, long pRevision, string pArchivePath,
            long pFileLength, int pPageSize, long pPageCount,
            byte[] pSha256, int pSchemaVersion, int pCatalogVersion)
        {
            Error = pError;
            Detail = pDetail ?? string.Empty;
            Epoch = pEpoch;
            Revision = pRevision;
            ArchivePath = pArchivePath ?? string.Empty;
            FileLength = pFileLength;
            PageSize = pPageSize;
            PageCount = pPageCount;
            _sha256 = pSha256 == null ? Array.Empty<byte>() :
                (byte[])pSha256.Clone();
            SchemaVersion = pSchemaVersion;
            CatalogVersion = pCatalogVersion;
        }

        public bool IsSuccess => Error == AW3MultiplayerReplicationError.None;
        public AW3MultiplayerReplicationError Error { get; }
        public string Detail { get; }
        public Guid Epoch { get; }
        public long Revision { get; }
        public string ArchivePath { get; }
        public long FileLength { get; }
        public int PageSize { get; }
        public long PageCount { get; }
        public byte[] Sha256 => (byte[])_sha256.Clone();
        public int SchemaVersion { get; }
        public int CatalogVersion { get; }
    }

    public sealed class AW3MultiplayerArchiveInstallResult
    {
        private readonly byte[] _sha256;

        internal AW3MultiplayerArchiveInstallResult(
            AW3MultiplayerReplicationError pError, string pDetail,
            Guid pEpoch, long pRevision, string pArchivePath,
            long pFileLength, int pPageSize, long pPageCount,
            byte[] pSha256, int pSchemaVersion, int pCatalogVersion)
        {
            Error = pError;
            Detail = pDetail ?? string.Empty;
            Epoch = pEpoch;
            Revision = pRevision;
            ArchivePath = pArchivePath ?? string.Empty;
            FileLength = pFileLength;
            PageSize = pPageSize;
            PageCount = pPageCount;
            _sha256 = pSha256 == null ? Array.Empty<byte>() :
                (byte[])pSha256.Clone();
            SchemaVersion = pSchemaVersion;
            CatalogVersion = pCatalogVersion;
        }

        public bool IsSuccess => Error == AW3MultiplayerReplicationError.None;
        public AW3MultiplayerReplicationError Error { get; }
        public string Detail { get; }
        public Guid Epoch { get; }
        public long Revision { get; }
        public string ArchivePath { get; }
        public long FileLength { get; }
        public int PageSize { get; }
        public long PageCount { get; }
        public byte[] Sha256 => (byte[])_sha256.Clone();
        public int SchemaVersion { get; }
        public int CatalogVersion { get; }
    }
}
