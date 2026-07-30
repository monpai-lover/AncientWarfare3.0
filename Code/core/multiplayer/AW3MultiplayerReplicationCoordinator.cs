using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using AncientWarfare3.api.multiplayer;
#if !AW3_RULES_TESTS
using AncientWarfare3.core.db;
#endif

namespace AncientWarfare3.core.multiplayer
{
    internal sealed class AW3MultiplayerArchiveFileMetadata
    {
        private readonly byte[] _sha256;

        internal AW3MultiplayerArchiveFileMetadata(string pArchivePath,
            long pFileLength, int pPageSize, long pPageCount,
            byte[] pSha256, int pSchemaVersion, int pCatalogVersion)
        {
            if (string.IsNullOrWhiteSpace(pArchivePath))
                throw new ArgumentException("Archive path is required.",
                    nameof(pArchivePath));
            if (pFileLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(pFileLength));
            if (pPageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pPageSize));
            if (pPageCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(pPageCount));
            if (pSha256 == null || pSha256.Length != 32)
                throw new ArgumentException(
                    "Archive SHA-256 must contain 32 bytes.",
                    nameof(pSha256));
            if (pSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(pSchemaVersion));
            if (pCatalogVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(pCatalogVersion));

            ArchivePath = Path.GetFullPath(pArchivePath);
            FileLength = pFileLength;
            PageSize = pPageSize;
            PageCount = pPageCount;
            _sha256 = (byte[])pSha256.Clone();
            SchemaVersion = pSchemaVersion;
            CatalogVersion = pCatalogVersion;
        }

        internal string ArchivePath { get; }
        internal long FileLength { get; }
        internal int PageSize { get; }
        internal long PageCount { get; }
        internal byte[] Sha256 => (byte[])_sha256.Clone();
        internal byte[] Sha256Buffer => _sha256;
        internal int SchemaVersion { get; }
        internal int CatalogVersion { get; }
    }

    internal interface IAW3MultiplayerReplicationStore
    {
        bool IsMainThread { get; }

        bool TryCaptureArchive(string pDestinationDirectory,
            out AW3MultiplayerArchiveFileMetadata pMetadata,
            out string pError);

        bool TryInstallArchive(string pVerifiedArchivePath,
            byte[] pExpectedSha256,
            out AW3MultiplayerArchiveFileMetadata pMetadata,
            out string pError);

        bool TryRebuildAfterReplicationInstall(out string pError);

        void RefreshCurrentWindows();
    }

    internal static class AW3MultiplayerReplicationCoordinator
    {
        internal static AW3MultiplayerArchiveCaptureResult
            CaptureArchiveKeyframe(string pEmptyDestinationDirectory,
                Guid pEpoch, long pRevision,
                IAW3MultiplayerReplicationStore pStore)
        {
            if (pStore == null)
                return CaptureFailure(
                    AW3MultiplayerReplicationError.CaptureFailed,
                    "AW3 replication store is required.", pEpoch, pRevision);
            if (!pStore.IsMainThread)
                return CaptureFailure(
                    AW3MultiplayerReplicationError.WrongThread,
                    "Archive capture requires the WorldBox main thread.",
                    pEpoch, pRevision);
            if (pEpoch == Guid.Empty)
                return CaptureFailure(
                    AW3MultiplayerReplicationError.InvalidEpoch,
                    "Archive capture epoch is required.", pEpoch, pRevision);
            if (pRevision <= 0)
                return CaptureFailure(
                    AW3MultiplayerReplicationError.InvalidRevision,
                    "Archive revision must be positive.", pEpoch, pRevision);
            if (!TryValidateEmptyDirectory(pEmptyDestinationDirectory,
                    out string directory, out string directoryError))
                return CaptureFailure(
                    AW3MultiplayerReplicationError.InvalidDirectory,
                    directoryError, pEpoch, pRevision);

            try
            {
                if (!pStore.TryCaptureArchive(directory,
                        out AW3MultiplayerArchiveFileMetadata metadata,
                        out string captureError))
                    return CaptureFailure(
                        AW3MultiplayerReplicationError.CaptureFailed,
                        StableDetail(captureError,
                            "AW3 archive capture failed."),
                        pEpoch, pRevision);
                if (!IsValidMetadata(metadata) ||
                    !IsContained(directory, metadata.ArchivePath) ||
                    !File.Exists(metadata.ArchivePath))
                    return CaptureFailure(
                        AW3MultiplayerReplicationError.InvalidArchive,
                        "Captured archive metadata is invalid.",
                        pEpoch, pRevision);
                return CaptureSuccess(pEpoch, pRevision, metadata);
            }
            catch (Exception error)
            {
                return CaptureFailure(
                    AW3MultiplayerReplicationError.CaptureFailed,
                    StableDetail(error.Message,
                        "AW3 archive capture failed."),
                    pEpoch, pRevision);
            }
        }

        internal static AW3MultiplayerArchiveInstallResult
            InstallArchiveKeyframe(string pVerifiedArchivePath,
                Guid pEpoch, long pRevision, byte[] pExpectedSha256,
                IAW3MultiplayerReplicationStore pStore)
        {
            if (pStore == null)
                return InstallFailure(
                    AW3MultiplayerReplicationError.InstallFailed,
                    "AW3 replication store is required.", pEpoch, pRevision);
            if (!pStore.IsMainThread)
                return InstallFailure(
                    AW3MultiplayerReplicationError.WrongThread,
                    "Archive install requires the WorldBox main thread.",
                    pEpoch, pRevision);
            if (pEpoch == Guid.Empty)
                return InstallFailure(
                    AW3MultiplayerReplicationError.InvalidEpoch,
                    "Archive install epoch is required.", pEpoch, pRevision);
            if (pRevision <= 0)
                return InstallFailure(
                    AW3MultiplayerReplicationError.InvalidRevision,
                    "Archive revision must be positive.", pEpoch, pRevision);
            if (pExpectedSha256 == null || pExpectedSha256.Length != 32)
                return InstallFailure(
                    AW3MultiplayerReplicationError.HashMismatch,
                    "Expected archive SHA-256 must contain 32 bytes.",
                    pEpoch, pRevision);
            if (!TryValidateArchivePath(pVerifiedArchivePath,
                    out string archivePath, out string archiveError))
                return InstallFailure(
                    AW3MultiplayerReplicationError.InvalidArchive,
                    archiveError, pEpoch, pRevision);

            try
            {
                byte[] expectedSha256 = (byte[])pExpectedSha256.Clone();
                if (!pStore.TryInstallArchive(archivePath, expectedSha256,
                        out AW3MultiplayerArchiveFileMetadata metadata,
                        out string installError))
                    return InstallFailure(
                        AW3MultiplayerReplicationError.InstallFailed,
                        StableDetail(installError,
                            "AW3 archive installation failed."),
                        pEpoch, pRevision);
                if (!IsValidMetadata(metadata))
                    return InstallFailure(
                        AW3MultiplayerReplicationError.InvalidArchive,
                        "Installed archive metadata is invalid.",
                        pEpoch, pRevision);
                if (!FixedTimeEquals(expectedSha256,
                        metadata.Sha256Buffer))
                    return InstallFailure(
                        AW3MultiplayerReplicationError.HashMismatch,
                        "Installed archive SHA-256 does not match.",
                        pEpoch, pRevision);
                if (!pStore.TryRebuildAfterReplicationInstall(
                        out string rebuildError))
                    return InstallFailure(
                        AW3MultiplayerReplicationError.ProjectionRebuildFailed,
                        StableDetail(rebuildError,
                            "AW3 projection rebuild failed."),
                        pEpoch, pRevision);
                try
                {
                    pStore.RefreshCurrentWindows();
                }
                catch (Exception error)
                {
                    return InstallFailure(
                        AW3MultiplayerReplicationError.WindowRefreshFailed,
                        StableDetail(error.Message,
                            "AW3 window refresh failed."),
                        pEpoch, pRevision);
                }
                return InstallSuccess(pEpoch, pRevision, metadata);
            }
            catch (Exception error)
            {
                return InstallFailure(
                    AW3MultiplayerReplicationError.InstallFailed,
                    StableDetail(error.Message,
                        "AW3 archive installation failed."),
                    pEpoch, pRevision);
            }
        }

        private static bool TryValidateEmptyDirectory(string pValue,
            out string pDirectory, out string pError)
        {
            pDirectory = string.Empty;
            pError = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(pValue) ||
                    !Path.IsPathRooted(pValue))
                {
                    pError = "Capture destination must be an absolute path.";
                    return false;
                }
                pDirectory = Path.GetFullPath(pValue);
                if (!Directory.Exists(pDirectory))
                {
                    pError = "Capture destination directory is missing.";
                    return false;
                }
                if ((File.GetAttributes(pDirectory) &
                     FileAttributes.ReparsePoint) != 0)
                {
                    pError = "Capture destination cannot be a reparse point.";
                    return false;
                }
                if (Directory.GetFileSystemEntries(pDirectory).Length != 0)
                {
                    pError = "Capture destination directory must be empty.";
                    return false;
                }
                return true;
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is IOException ||
                                          error is UnauthorizedAccessException ||
                                          error is NotSupportedException)
            {
                pError = StableDetail(error.Message,
                    "Capture destination is invalid.");
                return false;
            }
        }

        private static bool TryValidateArchivePath(string pValue,
            out string pPath, out string pError)
        {
            pPath = string.Empty;
            pError = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(pValue) ||
                    !Path.IsPathRooted(pValue))
                {
                    pError = "Verified archive path must be absolute.";
                    return false;
                }
                pPath = Path.GetFullPath(pValue);
                if (!File.Exists(pPath))
                {
                    pError = "Verified archive is missing.";
                    return false;
                }
                FileAttributes attributes = File.GetAttributes(pPath);
                if ((attributes & (FileAttributes.Directory |
                                   FileAttributes.ReparsePoint)) != 0)
                {
                    pError = "Verified archive must be a regular file.";
                    return false;
                }
                return true;
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is IOException ||
                                          error is UnauthorizedAccessException ||
                                          error is NotSupportedException)
            {
                pError = StableDetail(error.Message,
                    "Verified archive path is invalid.");
                return false;
            }
        }

        private static bool IsContained(string pDirectory, string pPath)
        {
            string directory = Path.GetFullPath(pDirectory).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(pPath);
            return path.StartsWith(directory,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidMetadata(
            AW3MultiplayerArchiveFileMetadata pMetadata)
        {
            return pMetadata != null && pMetadata.FileLength > 0 &&
                   pMetadata.PageSize > 0 && pMetadata.PageCount > 0 &&
                   pMetadata.Sha256Buffer.Length == 32 &&
                   pMetadata.SchemaVersion > 0 &&
                   pMetadata.CatalogVersion ==
                   AW3MultiplayerReplicationMetadata.CatalogVersion;
        }

        private static string StableDetail(string pValue, string pFallback)
        {
            return string.IsNullOrWhiteSpace(pValue) ? pFallback : pValue;
        }

        private static bool FixedTimeEquals(byte[] pLeft, byte[] pRight)
        {
            if (pLeft == null || pRight == null ||
                pLeft.Length != pRight.Length)
                return false;
            var difference = 0;
            for (var index = 0; index < pLeft.Length; index++)
                difference |= pLeft[index] ^ pRight[index];
            return difference == 0;
        }

        private static AW3MultiplayerArchiveCaptureResult CaptureSuccess(
            Guid pEpoch, long pRevision,
            AW3MultiplayerArchiveFileMetadata pMetadata)
        {
            return new AW3MultiplayerArchiveCaptureResult(
                AW3MultiplayerReplicationError.None, string.Empty, pEpoch,
                pRevision, pMetadata.ArchivePath, pMetadata.FileLength,
                pMetadata.PageSize, pMetadata.PageCount, pMetadata.Sha256Buffer,
                pMetadata.SchemaVersion, pMetadata.CatalogVersion);
        }

        private static AW3MultiplayerArchiveCaptureResult CaptureFailure(
            AW3MultiplayerReplicationError pError, string pDetail,
            Guid pEpoch, long pRevision)
        {
            return new AW3MultiplayerArchiveCaptureResult(pError, pDetail,
                pEpoch, pRevision, string.Empty, 0, 0, 0,
                Array.Empty<byte>(), 0, 0);
        }

        private static AW3MultiplayerArchiveInstallResult InstallSuccess(
            Guid pEpoch, long pRevision,
            AW3MultiplayerArchiveFileMetadata pMetadata)
        {
            return new AW3MultiplayerArchiveInstallResult(
                AW3MultiplayerReplicationError.None, string.Empty, pEpoch,
                pRevision, pMetadata.ArchivePath, pMetadata.FileLength,
                pMetadata.PageSize, pMetadata.PageCount, pMetadata.Sha256Buffer,
                pMetadata.SchemaVersion, pMetadata.CatalogVersion);
        }

        private static AW3MultiplayerArchiveInstallResult InstallFailure(
            AW3MultiplayerReplicationError pError, string pDetail,
            Guid pEpoch, long pRevision)
        {
            return new AW3MultiplayerArchiveInstallResult(pError, pDetail,
                pEpoch, pRevision, string.Empty, 0, 0, 0,
                Array.Empty<byte>(), 0, 0);
        }
    }

    internal static class AW3MultiplayerArchiveInspector
    {
        internal static bool TryInspect(string pArchivePath,
            out AW3MultiplayerArchiveFileMetadata pMetadata,
            out string pError)
        {
            pMetadata = null;
            pError = string.Empty;
            try
            {
                string path = Path.GetFullPath(pArchivePath);
                if (!File.Exists(path) ||
                    (File.GetAttributes(path) &
                     (FileAttributes.Directory |
                      FileAttributes.ReparsePoint)) != 0)
                {
                    pError = "Archive must be a regular file.";
                    return false;
                }
                if (File.Exists(path + "-wal") || File.Exists(path + "-shm"))
                {
                    pError = "Archive has WAL or SHM sidecars.";
                    return false;
                }

                long fileLength = new FileInfo(path).Length;
                if (fileLength <= 0)
                {
                    pError = "Archive file is empty.";
                    return false;
                }
                byte[] digest;
                using (var stream = new FileStream(path, FileMode.Open,
                           FileAccess.Read, FileShare.Read))
                using (SHA256 sha256 = SHA256.Create())
                    digest = sha256.ComputeHash(stream);

                int pageSize;
                long pageCount;
                int schemaVersion;
                using (var connection = new SQLiteConnection(
                           new SQLiteConnectionStringBuilder
                           {
                               DataSource = path,
                               Version = 3,
                               ReadOnly = true,
                               FailIfMissing = true,
                               Pooling = false
                           }.ConnectionString))
                {
                    connection.Open();
                    using (var quickCheck = connection.CreateCommand())
                    {
                        quickCheck.CommandText = "PRAGMA quick_check;";
                        using var reader = quickCheck.ExecuteReader();
                        if (!reader.Read() ||
                            !string.Equals(reader.GetString(0), "ok",
                                StringComparison.OrdinalIgnoreCase) ||
                            reader.Read())
                        {
                            pError = "Archive quick_check did not return ok.";
                            return false;
                        }
                    }
                    string journalMode = ReadTextPragma(connection,
                        "PRAGMA journal_mode;");
                    if (!string.Equals(journalMode, "delete",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        pError = "Archive journal_mode is " + journalMode + ".";
                        return false;
                    }
                    pageSize = checked((int)ReadLongPragma(connection,
                        "PRAGMA page_size;"));
                    pageCount = ReadLongPragma(connection,
                        "PRAGMA page_count;");
                    schemaVersion = checked((int)ReadLongPragma(connection,
                        "PRAGMA schema_version;"));
                }
                pMetadata = new AW3MultiplayerArchiveFileMetadata(path,
                    fileLength, pageSize, pageCount, digest, schemaVersion,
                    AW3MultiplayerReplicationMetadata.CatalogVersion);
                return true;
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is IOException ||
                                          error is UnauthorizedAccessException ||
                                          error is SQLiteException ||
                                          error is OverflowException ||
                                          error is CryptographicException)
            {
                pError = string.IsNullOrWhiteSpace(error.Message)
                    ? "Archive inspection failed."
                    : error.Message;
                return false;
            }
        }

        internal static byte[] ComputeSha256(string pPath)
        {
            using var stream = new FileStream(pPath, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(stream);
        }

        private static long ReadLongPragma(SQLiteConnection pConnection,
            string pCommandText)
        {
            using var command = pConnection.CreateCommand();
            command.CommandText = pCommandText;
            object value = command.ExecuteScalar();
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static string ReadTextPragma(SQLiteConnection pConnection,
            string pCommandText)
        {
            using var command = pConnection.CreateCommand();
            command.CommandText = pCommandText;
            return Convert.ToString(command.ExecuteScalar(),
                CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

#if !AW3_RULES_TESTS
    internal sealed class AW3MultiplayerReplicationWorldStore :
        IAW3MultiplayerReplicationStore
    {
        public bool IsMainThread => ThreadHelper.isMainThread();

        public bool TryCaptureArchive(string pDestinationDirectory,
            out AW3MultiplayerArchiveFileMetadata pMetadata,
            out string pError)
        {
            pMetadata = null;
            if (!LineageArchiveManager.Instance.TryExportLineageArchive(
                    pDestinationDirectory, out pError))
                return false;
            string path = Path.Combine(pDestinationDirectory,
                LineageArchiveManager.DB_FILE_NAME);
            return AW3MultiplayerArchiveInspector.TryInspect(path,
                out pMetadata, out pError);
        }

        public bool TryInstallArchive(string pVerifiedArchivePath,
            byte[] pExpectedSha256,
            out AW3MultiplayerArchiveFileMetadata pMetadata,
            out string pError)
        {
            pMetadata = null;
            if (!AW3MultiplayerArchiveInspector.TryInspect(
                    pVerifiedArchivePath, out pMetadata, out pError))
                return false;
            if (!LineageArchiveManager.Instance.TryInstallReplicationArchive(
                    pVerifiedArchivePath, pExpectedSha256,
                    out _, out pError))
                return false;
            return true;
        }

        public bool TryRebuildAfterReplicationInstall(out string pError)
        {
            AW3RestoreResult result =
                AW3RuntimeRestorePipeline.TryRebuildAfterReplicationInstall(
                    strict: true);
            pError = result.Success ? string.Empty :
                result.FailedStage + ": " + result.Detail;
            return result.Success;
        }

        public void RefreshCurrentWindows()
        {
            AW3RuntimeRestorePipeline.RefreshCurrentWindows();
        }
    }
#endif
}
