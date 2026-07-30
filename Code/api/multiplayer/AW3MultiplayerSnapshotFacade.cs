using System;
using System.IO;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.patch;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerSnapshotFacade
    {
        private const string MapFileName = "map.wbox";
        private const string StatsFileName = "map_stats.s3db";

        public static AW3MultiplayerSnapshotResult TryCreateGeneration(
            string pPendingDirectory)
        {
            if (!ThreadHelper.isMainThread())
                return AW3MultiplayerSnapshotResult.Failure(
                    AW3MultiplayerSnapshotError.WrongThread,
                    "Snapshot capture must run on the WorldBox main thread.");

            var destinationResult = ValidateEmptyDestination(
                pPendingDirectory, out string pendingDirectory);
            if (destinationResult != null) return destinationResult;

            try
            {
                using (AW_SavePatch.EnterMultiplayerSnapshotSave())
                {
                    if (!AW_SavePatch.TryPrepareForSave(
                            out string preparationError))
                        return Failure(
                            AW3MultiplayerSnapshotError.SavePreparationFailed,
                            preparationError);

                    try
                    {
                        SavedMap savedMap =
                            SaveManager.currentWorldToSavedMap();
                        SaveManager.saveMetaData(savedMap.getMeta(),
                            pendingDirectory);
                        SaveManager.saveStatsIn(pendingDirectory);
                        string captureMapPath = Path.Combine(pendingDirectory,
                            MapFileName);
                        string temporaryMapPath = captureMapPath + ".tmp";
                        savedMap.toZip(temporaryMapPath);
                        if (!File.Exists(temporaryMapPath))
                            return Failure(
                                AW3MultiplayerSnapshotError.MapFileMissing,
                                temporaryMapPath);
                        Toolbox.MoveSafely(temporaryMapPath, captureMapPath);
                    }
                    catch (Exception error)
                    {
                        return Failure(
                            AW3MultiplayerSnapshotError.WorldSaveFailed,
                            error.ToString());
                    }

                    string mapPath = Path.Combine(pendingDirectory,
                        MapFileName);
                    var fileError = ValidateRequiredFile(mapPath,
                        AW3MultiplayerSnapshotError.MapFileMissing,
                        AW3MultiplayerSnapshotError.MapFileEmpty);
                    if (fileError != null) return fileError;

                    string statsPath = Path.Combine(pendingDirectory,
                        StatsFileName);
                    fileError = ValidateRequiredFile(statsPath,
                        AW3MultiplayerSnapshotError.StatsFileMissing,
                        AW3MultiplayerSnapshotError.StatsFileEmpty);
                    if (fileError != null) return fileError;

                    try
                    {
                        if (!LineageArchiveManager.Instance
                                .TryExportLineageArchive(pendingDirectory,
                                    out string exportError))
                            return Failure(
                                AW3MultiplayerSnapshotError
                                    .LineageExportFailed,
                                exportError);
                    }
                    catch (Exception error)
                    {
                        return Failure(
                            AW3MultiplayerSnapshotError.LineageExportFailed,
                            error.Message);
                    }

                    return ValidateGeneration(pendingDirectory);
                }
            }
            catch (Exception error)
            {
                return Failure(
                    AW3MultiplayerSnapshotError.SavePreparationFailed,
                    error.Message);
            }
        }

        public static AW3MultiplayerSnapshotResult ValidateGeneration(
            string pPendingDirectory)
        {
            string directory;
            try
            {
                if (string.IsNullOrWhiteSpace(pPendingDirectory))
                    return Failure(AW3MultiplayerSnapshotError.InvalidDirectory,
                        "Generation directory is required.");

                directory = Path.GetFullPath(pPendingDirectory);
                if (!Directory.Exists(directory))
                    return Failure(AW3MultiplayerSnapshotError.InvalidDirectory,
                        "Generation directory does not exist.");
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is NotSupportedException ||
                                          error is PathTooLongException ||
                                          error is IOException ||
                                          error is UnauthorizedAccessException)
            {
                return Failure(AW3MultiplayerSnapshotError.InvalidDirectory,
                    error.Message);
            }

            string mapPath = Path.Combine(directory, MapFileName);
            var fileError = ValidateRequiredFile(mapPath,
                AW3MultiplayerSnapshotError.MapFileMissing,
                AW3MultiplayerSnapshotError.MapFileEmpty);
            if (fileError != null) return fileError;

            string statsPath = Path.Combine(directory, StatsFileName);
            fileError = ValidateRequiredFile(statsPath,
                AW3MultiplayerSnapshotError.StatsFileMissing,
                AW3MultiplayerSnapshotError.StatsFileEmpty);
            if (fileError != null) return fileError;

            string archivePath = Path.Combine(directory,
                LineageArchiveManager.DB_FILE_NAME);
            fileError = ValidateRequiredFile(archivePath,
                AW3MultiplayerSnapshotError.LineageArchiveMissing,
                AW3MultiplayerSnapshotError.LineageArchiveEmpty);
            if (fileError != null) return fileError;

            if (HasLineageSidecar(archivePath))
                return Failure(
                    AW3MultiplayerSnapshotError.LineageArchiveSidecarPresent,
                    "Lineage archive WAL or SHM sidecar is present.");

            bool journalModeInvalid;
            string validationError;
            if (!LineageArchivePragmaService.TryValidateSnapshot(archivePath,
                    out journalModeInvalid, out validationError))
                return Failure(journalModeInvalid
                        ? AW3MultiplayerSnapshotError
                            .LineageArchiveJournalModeInvalid
                        : AW3MultiplayerSnapshotError
                            .LineageArchiveQuickCheckFailed,
                    validationError);

            if (HasLineageSidecar(archivePath))
                return Failure(
                    AW3MultiplayerSnapshotError.LineageArchiveSidecarPresent,
                    "Lineage validation left a WAL or SHM sidecar.");

            return AW3MultiplayerSnapshotResult.Success(directory,
                Path.GetFullPath(mapPath), Path.GetFullPath(statsPath),
                Path.GetFullPath(archivePath));
        }

        private static AW3MultiplayerSnapshotResult ValidateRequiredFile(
            string pPath, AW3MultiplayerSnapshotError pMissing,
            AW3MultiplayerSnapshotError pEmpty)
        {
            try
            {
                if (!File.Exists(pPath)) return Failure(pMissing, pPath);
                if (new FileInfo(pPath).Length <= 0)
                    return Failure(pEmpty, pPath);
                return null;
            }
            catch (Exception error) when (error is IOException ||
                                          error is UnauthorizedAccessException)
            {
                return Failure(AW3MultiplayerSnapshotError.IoFailure,
                    error.Message);
            }
        }

        private static AW3MultiplayerSnapshotResult ValidateEmptyDestination(
            string pPendingDirectory, out string pCanonicalDirectory)
        {
            pCanonicalDirectory = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(pPendingDirectory))
                    return Failure(AW3MultiplayerSnapshotError.InvalidDirectory,
                        "Generation directory is required.");

                string directory = Path.GetFullPath(pPendingDirectory);
                if (!Directory.Exists(directory) ||
                    (File.GetAttributes(directory) &
                     FileAttributes.ReparsePoint) != 0)
                    return Failure(AW3MultiplayerSnapshotError.InvalidDirectory,
                        "Generation directory must be an existing real directory.");
                if (Directory.EnumerateFileSystemEntries(directory).Any())
                    return Failure(
                        AW3MultiplayerSnapshotError.DestinationNotEmpty,
                        "Generation directory must be empty.");

                pCanonicalDirectory = directory;
                return null;
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is NotSupportedException ||
                                          error is PathTooLongException ||
                                          error is IOException ||
                                          error is UnauthorizedAccessException)
            {
                return Failure(AW3MultiplayerSnapshotError.InvalidDirectory,
                    error.Message);
            }
        }

        private static bool HasLineageSidecar(string pArchivePath)
        {
            return File.Exists(pArchivePath + "-wal") ||
                   File.Exists(pArchivePath + "-shm");
        }

        private static AW3MultiplayerSnapshotResult Failure(
            AW3MultiplayerSnapshotError pError, string pDetail)
        {
            return AW3MultiplayerSnapshotResult.Failure(pError, pDetail);
        }
    }
}
