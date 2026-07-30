#if AW3_FACADE_ISOLATED
using System;
using System.IO;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.patch;

public static class ThreadHelper
{
    public static bool MainThread;
    public static bool isMainThread() => MainThread;
}

public static class SaveManager
{
    public static int Calls;
    public static int PreviewCalls;
    public static int DataCalls;
    public static int CurrentWorldCalls;
    public static int MetaCalls;
    public static int StatsCalls;
    public static int MapWriteCalls;
    public static Action<string, bool, bool> SaveAction;
    public static Action PreviewAction;
    public static Action SaveMapDataPostfix;
    public static Action<string> MetaAction;
    public static Action<string> StatsAction;
    public static Action<string> MapWriteAction;

    public static void saveWorldToDirectory(string directory, bool compress,
        bool checkFolder)
    {
        Calls++;
        PreviewCalls++;
        PreviewAction?.Invoke();
        SaveAction(directory, compress, checkFolder);
    }

    public static object saveMapData(string directory, bool compress)
    {
        Calls++;
        DataCalls++;
        SaveAction(directory, compress, false);
        SaveMapDataPostfix?.Invoke();
        return null;
    }

    public static SavedMap currentWorldToSavedMap()
    {
        CurrentWorldCalls++;
        AW_SavePatch.EventOrder.Add("encode");
        return new SavedMap();
    }

    public static void saveMetaData(object metadata, string directory)
    {
        MetaCalls++;
        MetaAction(directory);
    }

    public static void saveStatsIn(string directory)
    {
        StatsCalls++;
        StatsAction(directory);
    }

    public static void WriteMap(string path)
    {
        MapWriteCalls++;
        MapWriteAction(path);
    }
}

public sealed class SavedMap
{
    public object getMeta() => new object();

    public void toZip(string path) => SaveManager.WriteMap(path);
}

public static class Toolbox
{
    public static void MoveSafely(string source, string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(source, destination);
    }
}

namespace AncientWarfare3.patch
{
    internal static class AW_SavePatch
    {
        public static bool PrepareSuccess;
        public static string PrepareError;
        public static int PrepareCalls;
        public static bool ScopeActive;
        public static int ScopeDisposeCalls;
        public static List<string> EventOrder = new List<string>();

        internal static bool TryPrepareForSave(out string error)
        {
            PrepareCalls++;
            EventOrder.Add("prepare");
            error = PrepareError ?? string.Empty;
            return PrepareSuccess;
        }

        internal static IDisposable EnterMultiplayerSnapshotSave()
        {
            EventOrder.Add("barrier");
            EventOrder.Add("simulation");
            EventOrder.Add("async");
            ScopeActive = true;
            return new SaveScope();
        }

        public static void Reset()
        {
            PrepareSuccess = true;
            PrepareError = string.Empty;
            PrepareCalls = 0;
            ScopeActive = false;
            ScopeDisposeCalls = 0;
            EventOrder.Clear();
        }

        private sealed class SaveScope : IDisposable
        {
            public void Dispose()
            {
                ScopeActive = false;
                ScopeDisposeCalls++;
            }
        }
    }
}

namespace AncientWarfare3.core.db
{
    public sealed class LineageArchiveManager
    {
        public const string DB_FILE_NAME = "aw3_lineage_archive.db";
        public static readonly LineageArchiveManager Instance = new();
        public static bool ExportSuccess;
        public static string ExportError;
        public static int ExportCalls;
        public static Action<string> ExportAction;

        internal bool TryExportLineageArchive(string directory,
            out string error)
        {
            ExportCalls++;
            AW_SavePatch.EventOrder.Add("export");
            error = ExportError ?? string.Empty;
            if (ExportSuccess) ExportAction?.Invoke(directory);
            return ExportSuccess;
        }

        public static void Reset()
        {
            ExportSuccess = true;
            ExportError = string.Empty;
            ExportCalls = 0;
            ExportAction = null;
        }
    }

    internal static class LineageArchivePragmaService
    {
        public static bool TryValidateSnapshot(string path,
            out bool journalModeInvalid, out string error)
        {
            journalModeInvalid = false;
            error = string.Empty;
            return true;
        }
    }
}

public static class AW3MultiplayerSnapshotFacadeIsolatedTests
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "aw3-facade-isolated-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            DestinationMustBeEmpty(root);
            PreparationFailureStopsCapture(root);
            WorldSaveFailureIsStructured(root);
            MissingWorldFileIsStructured(root);
            LineageExportFailureIsStructured(root);
            SnapshotCaptureSkipsPreviewAndUsesDataSave(root);
            Console.WriteLine("AW3 multiplayer snapshot isolated tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void DestinationMustBeEmpty(string root)
    {
        Reset();
        File.WriteAllText(Path.Combine(root, "owned-by-another-job"), "x");
        var result = AW3MultiplayerSnapshotFacade.TryCreateGeneration(root);
        Equal(AW3MultiplayerSnapshotError.DestinationNotEmpty, result.Error,
            "capture rejects a non-empty caller-owned generation");
        Equal(0, AW_SavePatch.PrepareCalls,
            "non-empty destination is rejected before save preparation");
        File.Delete(Path.Combine(root, "owned-by-another-job"));
    }

    private static void PreparationFailureStopsCapture(string root)
    {
        Reset();
        AW_SavePatch.PrepareSuccess = false;
        AW_SavePatch.PrepareError = "queue-timeout";
        var result = AW3MultiplayerSnapshotFacade.TryCreateGeneration(root);
        Equal(AW3MultiplayerSnapshotError.SavePreparationFailed, result.Error,
            "persistent queue failure has a stable error");
        Equal("queue-timeout", result.Detail,
            "persistent queue failure preserves diagnostic detail");
        Equal(0, SaveManager.Calls,
            "world save does not start after preparation failure");
        Equal(0, LineageArchiveManager.ExportCalls,
            "lineage export does not start after preparation failure");
    }

    private static void WorldSaveFailureIsStructured(string root)
    {
        Reset();
        SaveManager.SaveAction = (_, _, _) =>
            throw new IOException("world-save-failed");
        SaveManager.MetaAction = _ =>
            throw new IOException("world-save-failed");
        SaveManager.MapWriteAction = _ =>
            throw new IOException("world-save-failed");
        var result = AW3MultiplayerSnapshotFacade.TryCreateGeneration(root);
        Equal(AW3MultiplayerSnapshotError.WorldSaveFailed, result.Error,
            "thrown WorldBox save failure has a stable error");
        Equal(true, result.Detail.Contains("System.IO.IOException"),
            "WorldBox save failure preserves the exception type");
        Equal(true, result.Detail.Contains("world-save-failed"),
            "WorldBox save failure preserves the exception message");
        Equal(true, result.Detail.Contains(
                nameof(WorldSaveFailureIsStructured)),
            "WorldBox save failure preserves the throwing stack frame");
        Equal(false, AW_SavePatch.ScopeActive,
            "multiplayer save scope is disposed after WorldBox failure");
        Equal(1, AW_SavePatch.ScopeDisposeCalls,
            "multiplayer save scope is disposed exactly once");
        Equal(0, LineageArchiveManager.ExportCalls,
            "lineage export does not run after WorldBox failure");
    }

    private static void MissingWorldFileIsStructured(string root)
    {
        Reset();
        SaveManager.SaveAction = (directory, _, _) =>
            File.WriteAllBytes(Path.Combine(directory, "map_stats.s3db"),
                new byte[] { 1 });
        SaveManager.MapWriteAction = _ => { };
        var result = AW3MultiplayerSnapshotFacade.TryCreateGeneration(root);
        Equal(AW3MultiplayerSnapshotError.MapFileMissing, result.Error,
            "WorldBox swallowed map IO failure is detected by file validation");
        Equal(0, LineageArchiveManager.ExportCalls,
            "lineage export is skipped when WorldBox files are incomplete");
        Clear(root);
    }

    private static void LineageExportFailureIsStructured(string root)
    {
        Reset();
        SaveManager.SaveAction = WriteWorldFiles;
        LineageArchiveManager.ExportSuccess = false;
        LineageArchiveManager.ExportError = "backup-failed";
        var result = AW3MultiplayerSnapshotFacade.TryCreateGeneration(root);
        Equal(AW3MultiplayerSnapshotError.LineageExportFailed, result.Error,
            "lineage online backup failure has a stable error");
        Equal("backup-failed", result.Detail,
            "lineage export failure preserves diagnostic detail");
        Clear(root);
    }

    private static void SnapshotCaptureSkipsPreviewAndUsesDataSave(string root)
    {
        Reset();
        SaveManager.PreviewAction = () =>
            throw new ArgumentNullException("bytes");
        SaveManager.SaveMapDataPostfix = () =>
            throw new ArgumentNullException("bytes");
        SaveManager.SaveAction = (directory, compress, checkFolder) =>
        {
            Equal(true, AW_SavePatch.ScopeActive,
                "standard save runs inside the multiplayer save scope");
            Equal(true, compress, "multiplayer map uses map.wbox compression");
            Equal(false, checkFolder,
                "caller-owned pending directory bypasses normal folder reuse");
            WriteWorldFiles(directory, compress, checkFolder);
        };
        LineageArchiveManager.ExportAction = directory =>
        {
            Equal(true, AW_SavePatch.ScopeActive,
                "snapshot barrier remains active through lineage export");
            File.WriteAllBytes(Path.Combine(directory,
                LineageArchiveManager.DB_FILE_NAME), new byte[] { 3 });
        };

        var result = AW3MultiplayerSnapshotFacade.TryCreateGeneration(root);
        Equal(true, result.IsSuccess,
            "complete standard save and lineage export succeed");
        Equal(1, AW_SavePatch.PrepareCalls,
            "persistent save state is prepared exactly once");
        Equal(0, SaveManager.Calls,
            "snapshot bypasses the patched high-level WorldBox save entry");
        Equal(0, SaveManager.PreviewCalls,
            "multiplayer snapshot never invokes preview encoding");
        Equal(0, SaveManager.DataCalls,
            "third-party saveMapData postfixes cannot enter snapshot capture");
        Equal(1, SaveManager.CurrentWorldCalls,
            "snapshot captures the current WorldBox state exactly once");
        Equal(1, SaveManager.MetaCalls,
            "snapshot writes standard WorldBox metadata exactly once");
        Equal(1, SaveManager.StatsCalls,
            "snapshot writes the standard WorldBox stats database exactly once");
        Equal(1, SaveManager.MapWriteCalls,
            "snapshot writes the standard compressed WorldBox map exactly once");
        Equal(1, LineageArchiveManager.ExportCalls,
            "lineage online backup runs exactly once");
        Equal("barrier,simulation,async,prepare,encode,export",
            string.Join(",", AW_SavePatch.EventOrder),
            "snapshot enters and drains both boundaries before preparation and encoding");
        Equal(false, AW_SavePatch.ScopeActive,
            "multiplayer save scope is closed before returning");
    }

    private static void WriteWorldFiles(string directory, bool compress,
        bool checkFolder)
    {
        File.WriteAllBytes(Path.Combine(directory, "map.wbox"),
            new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(directory, "map_stats.s3db"),
            new byte[] { 2 });
    }

    private static void Reset()
    {
        ThreadHelper.MainThread = true;
        SaveManager.Calls = 0;
        SaveManager.PreviewCalls = 0;
        SaveManager.DataCalls = 0;
        SaveManager.CurrentWorldCalls = 0;
        SaveManager.MetaCalls = 0;
        SaveManager.StatsCalls = 0;
        SaveManager.MapWriteCalls = 0;
        SaveManager.SaveAction = WriteWorldFiles;
        SaveManager.PreviewAction = null;
        SaveManager.SaveMapDataPostfix = null;
        SaveManager.MetaAction = directory => File.WriteAllText(
            Path.Combine(directory, "map.meta"), "{}");
        SaveManager.StatsAction = directory => File.WriteAllBytes(
            Path.Combine(directory, "map_stats.s3db"), new byte[] { 2 });
        SaveManager.MapWriteAction = path =>
            File.WriteAllBytes(path, new byte[] { 1 });
        AW_SavePatch.Reset();
        LineageArchiveManager.Reset();
    }

    private static void Clear(string root)
    {
        foreach (string path in Directory.GetFiles(root)) File.Delete(path);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(
                name + ": expected " + expected + ", got " + actual);
    }
}
#else
using System;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;

public static class AW3MultiplayerSnapshotFacadeTests
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "aw3-multiplayer-snapshot-facade-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = AW3MultiplayerSnapshotFacade.TryCreateGeneration(root);

            Equal(false, result.IsSuccess,
                "capture outside the WorldBox main thread must fail");
            Equal(AW3MultiplayerSnapshotError.WrongThread, result.Error,
                "capture outside the WorldBox main thread has a stable error");
            Equal(false, Directory.Exists(root),
                "wrong-thread rejection must not create the destination");

            ValidateGenerationFiles(root);
            ExportLiveWalDatabase(root);

            Console.WriteLine("AW3 multiplayer snapshot facade tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ExportLiveWalDatabase(string root)
    {
        string sourcePath = Path.Combine(root, "live-source.db");
        string exportDirectory = Path.Combine(root, "export;owned");
        Directory.CreateDirectory(exportDirectory);

        using (var source = new SQLiteConnection(
                   "Data Source=" + sourcePath +
                   ";Version=3;New=True;Pooling=False;"))
        {
            source.Open();
            Execute(source,
                "PRAGMA journal_mode=WAL;" +
                "PRAGMA wal_autocheckpoint=0;" +
                "CREATE TABLE live_rows(id INTEGER PRIMARY KEY, value TEXT);" +
                "INSERT INTO live_rows(value) VALUES ('wal-only');");
            Equal(true, File.Exists(sourcePath + "-wal"),
                "test source retains a live WAL sidecar");

            var manager = new LineageArchiveManager();
            FieldInfo databaseField = typeof(LineageArchiveManager).GetField(
                "_db", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo exportMethod = typeof(LineageArchiveManager).GetMethod(
                "TryExportLineageArchive",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Equal(true, databaseField != null,
                "lineage manager retains its live connection field");
            Equal(true, exportMethod != null,
                "lineage manager exposes one internal checked export path");
            databaseField.SetValue(manager, source);

            object[] exportArguments = { exportDirectory, null };
            bool exported = (bool)exportMethod.Invoke(manager, exportArguments);
            Equal(true, exported,
                "online backup exports the live lineage database");
            Equal(string.Empty, Convert.ToString(exportArguments[1]),
                "successful export has no error detail");

            string destinationPath = Path.Combine(exportDirectory,
                LineageArchiveManager.DB_FILE_NAME);
            Equal(1L, ScalarLong(destinationPath,
                    "SELECT COUNT(*) FROM live_rows WHERE value='wal-only';"),
                "online backup includes committed rows still represented by WAL");
            Equal("delete", ScalarText(destinationPath,
                    "PRAGMA journal_mode;"),
                "exported archive uses a single-file journal mode");
            Equal("ok", ScalarText(destinationPath, "PRAGMA quick_check;"),
                "exported archive passes quick_check");
            Equal(false, File.Exists(destinationPath + "-wal"),
                "export closes without a destination WAL sidecar");
            Equal(false, File.Exists(destinationPath + "-shm"),
                "export closes without a destination SHM sidecar");

            Execute(source,
                "INSERT INTO live_rows(value) VALUES ('second-export');");
            exportArguments = new object[] { exportDirectory, null };
            exported = (bool)exportMethod.Invoke(manager, exportArguments);
            Equal(true, exported,
                "normal saves can atomically replace an earlier archive");
            Equal(2L, ScalarLong(destinationPath,
                    "SELECT COUNT(*) FROM live_rows;"),
                "replacement export contains the new committed state");

            databaseField.SetValue(manager, null);
            exportArguments = new object[] { exportDirectory, null };
            exported = (bool)exportMethod.Invoke(manager, exportArguments);
            Equal(false, exported,
                "missing live database returns a structured export failure");
            Equal(true, !string.IsNullOrWhiteSpace(
                    Convert.ToString(exportArguments[1])),
                "failed export reports error detail");
            Equal(2L, ScalarLong(destinationPath,
                    "SELECT COUNT(*) FROM live_rows;"),
                "failed export preserves the last valid archive");

            using (var closedSource = new SQLiteConnection(
                       "Data Source=:memory:;Version=3;New=True;"))
            {
                closedSource.Open();
                closedSource.Close();
                databaseField.SetValue(manager, closedSource);
                exportArguments = new object[] { exportDirectory, null };
                exported = (bool)exportMethod.Invoke(manager, exportArguments);
                Equal(false, exported,
                    "closed live database returns a structured export failure");
                Equal(true, !string.IsNullOrWhiteSpace(
                        Convert.ToString(exportArguments[1])),
                    "closed source failure reports error detail");
                Equal(2L, ScalarLong(destinationPath,
                        "SELECT COUNT(*) FROM live_rows;"),
                    "closed source failure preserves the last valid archive");
            }
        }
    }

    private static void Execute(SQLiteConnection connection, string sql)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    private static long ScalarLong(string databasePath, string sql)
    {
        using (var connection = OpenReadOnly(databasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    private static string ScalarText(string databasePath, string sql)
    {
        using (var connection = OpenReadOnly(databasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()).ToLowerInvariant();
        }
    }

    private static SQLiteConnection OpenReadOnly(string databasePath)
    {
        var connection = new SQLiteConnection(
            new SQLiteConnectionStringBuilder
            {
                DataSource = databasePath,
                Version = 3,
                ReadOnly = true,
                FailIfMissing = true,
                Pooling = false
            }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void ValidateGenerationFiles(string root)
    {
        Equal(AW3MultiplayerSnapshotError.InvalidDirectory,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(null).Error,
            "null generation directory has a stable error");

        root = Path.Combine(root, "validate;owned");
        Directory.CreateDirectory(root);
        Equal(AW3MultiplayerSnapshotError.MapFileMissing,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "missing map has a stable error");

        string mapPath = Path.Combine(root, "map.wbox");
        File.WriteAllBytes(mapPath, Array.Empty<byte>());
        Equal(AW3MultiplayerSnapshotError.MapFileEmpty,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "empty map has a stable error");

        File.WriteAllBytes(mapPath, new byte[] { 1 });
        string statsPath = Path.Combine(root, "map_stats.s3db");
        Equal(AW3MultiplayerSnapshotError.StatsFileMissing,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "missing stats database has a stable error");

        File.WriteAllBytes(statsPath, Array.Empty<byte>());
        Equal(AW3MultiplayerSnapshotError.StatsFileEmpty,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "empty stats database has a stable error");

        File.WriteAllBytes(statsPath, new byte[] { 2 });
        string archivePath = Path.Combine(root, "aw3_lineage_archive.db");
        Equal(AW3MultiplayerSnapshotError.LineageArchiveMissing,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "missing lineage archive has a stable error");

        File.WriteAllBytes(archivePath, Array.Empty<byte>());
        Equal(AW3MultiplayerSnapshotError.LineageArchiveEmpty,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "empty lineage archive has a stable error");

        File.WriteAllBytes(archivePath, new byte[] { 3, 4, 5 });
        Equal(AW3MultiplayerSnapshotError.LineageArchiveQuickCheckFailed,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "corrupt lineage archive fails quick_check");

        CreateArchive(archivePath, "WAL");
        Equal(AW3MultiplayerSnapshotError.LineageArchiveJournalModeInvalid,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "snapshot lineage archive must use a single-file journal mode");

        CreateArchive(archivePath, "DELETE");
        File.WriteAllBytes(archivePath + "-wal", new byte[] { 6 });
        Equal(AW3MultiplayerSnapshotError.LineageArchiveSidecarPresent,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "lineage WAL sidecar is rejected");
        File.Delete(archivePath + "-wal");

        File.WriteAllBytes(archivePath + "-shm", new byte[] { 7 });
        Equal(AW3MultiplayerSnapshotError.LineageArchiveSidecarPresent,
            AW3MultiplayerSnapshotFacade.ValidateGeneration(root).Error,
            "lineage SHM sidecar is rejected");
        File.Delete(archivePath + "-shm");

        var result = AW3MultiplayerSnapshotFacade.ValidateGeneration(root);
        Equal(true, result.IsSuccess, "valid three-file generation succeeds");
        Equal(Path.GetFullPath(root), result.PendingDirectory,
            "result returns canonical generation directory");
        Equal(Path.GetFullPath(mapPath), result.MapPath,
            "result returns canonical map path");
        Equal(Path.GetFullPath(statsPath), result.StatsPath,
            "result returns canonical stats path");
        Equal(Path.GetFullPath(archivePath), result.LineageArchivePath,
            "result returns canonical lineage path");
        Equal(false, File.Exists(archivePath + "-wal"),
            "validation leaves no WAL sidecar");
        Equal(false, File.Exists(archivePath + "-shm"),
            "validation leaves no SHM sidecar");
    }

    private static void CreateArchive(string path, string journalMode)
    {
        SQLiteConnection.ClearAllPools();
        DeleteIfPresent(path + "-wal");
        DeleteIfPresent(path + "-shm");
        DeleteIfPresent(path);

        using (var connection = new SQLiteConnection(
                   new SQLiteConnectionStringBuilder
                   {
                       DataSource = path,
                       Version = 3,
                       Pooling = false
                   }.ConnectionString))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "PRAGMA journal_mode=" + journalMode + ";" +
                    "CREATE TABLE lineage_test(id INTEGER PRIMARY KEY, value TEXT);" +
                    "INSERT INTO lineage_test(value) VALUES ('persisted');";
                command.ExecuteNonQuery();
            }
        }

        SQLiteConnection.ClearAllPools();
        DeleteIfPresent(path + "-wal");
        DeleteIfPresent(path + "-shm");
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(
                name + ": expected " + expected + ", got " + actual);
    }
}
#endif
