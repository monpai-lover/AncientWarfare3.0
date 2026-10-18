using System.Data;
using System.Data.SQLite;
using System.Reflection;
using System.Security.Cryptography;
using AncientWarfare3;
using AncientWarfare3.attributes;
using AncientWarfare3.core.db;
using AncientWarfare3.core.historyapi;

internal static class ArchiveAvailabilityTests
{
    internal static void QuarantineAndRetry()
    {
        WithArchive(archive =>
        {
            SQLiteConnection previous = archive.OperatingDB;
            Exec(previous, "INSERT INTO BoundaryMarker(ID) VALUES(7)");
            string path = LineageArchiveManager.RuntimeDbPath;
            long epoch = LineageArchiveManager.RuntimeDatabaseEpoch;
            string save = Path.Combine(ModClass.FolderPath, "save");
            Directory.CreateDirectory(save);
            string destination = Path.Combine(save, LineageArchiveManager.DB_FILE_NAME);
            File.WriteAllText(destination, "existing-save-must-not-be-replaced");
            using var readStarted = new ManualResetEventSlim();
            using var releaseRead = new ManualResetEventSlim();
            Task<bool> priorRead = Task.Run(() => AW3HistoryReadConnection.TryRead(db =>
            {
                long count = Count(db);
                readStarted.Set();
                if (!releaseRead.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test read release");
                return count;
            }, out long ignored));
            Require(readStarted.Wait(TimeSpan.FromSeconds(3)), "old archive read started");
            using (var holder = new Program.HeldCheckpoint())
            {
                SetCheckpoint("_running", holder.Task);
                try
                {
                    archive.CreateDataBase(); // actual Shutdown timeout is caught by actual CreateDataBase
                    bool stillPrivate = ReferenceEquals(GetPrivateDb(archive), previous) &&
                        previous.State == ConnectionState.Open && File.Exists(path) && Count(previous) == 1;
                    bool publicHidden = !archive.IsOperational && archive.OperatingDB == null;
                    bool exported = archive.TryExportLineageArchive(save, out string exportError);
                    bool newRead = AW3HistoryReadConnection.TryRead(db => Count(db), out long ignored);
                    releaseRead.Set();
                    Require(priorRead.Wait(TimeSpan.FromSeconds(3)), "old read completed after invalidation");
                    Require(stillPrivate, "failed create must preserve the old file and connection for safe shutdown retry");
                    Require(publicHidden && !exported && !newRead && !priorRead.Result &&
                        LineageArchiveManager.RuntimeDatabaseEpoch != epoch &&
                        File.ReadAllText(destination) == "existing-save-must-not-be-replaced",
                        $"failed archive leaked: hidden={publicHidden}, export={exported}, newRead={newRead}, oldRead={priorRead.Result}, epochChanged={LineageArchiveManager.RuntimeDatabaseEpoch != epoch}");
                }
                finally
                {
                    releaseRead.Set();
                    holder.Release();
                    priorRead.Wait(TimeSpan.FromSeconds(3));
                }
            }
            archive.CreateDataBase();
            Require(archive.IsOperational && archive.OperatingDB != null && Count(archive.OperatingDB) == 0,
                "successful retry must initialize a new empty schema and reopen public access");
            Require(!ReferenceEquals(GetPrivateDb(archive), previous), "retry replaces old private connection");
            Require(archive.TryExportLineageArchive(save, out string error), "healthy archive exports after retry: " + error);
        });
    }

    internal static void LoadAndInstallAfterDisabled()
    {
        WithArchive(archive =>
        {
            Exec(archive.OperatingDB, "INSERT INTO BoundaryMarker(ID) VALUES(17)");
            string save = Path.Combine(ModClass.FolderPath, "valid-save");
            Require(archive.TryExportLineageArchive(save, out string error), "create valid snapshot: " + error);
            archive.DisableRuntimeArchive();
            Require(archive.OperatingDB == null, "disabled archive has no public connection");
            Require(archive.TryLoadFromSaveDirectory(save, out error), "load succeeds while archive starts disabled: " + error);
            Require(archive.IsOperational && Count(archive.OperatingDB) == 1, "loaded schema becomes public only after successful setup");
            archive.DisableRuntimeArchive();
            string snapshot = Path.Combine(save, LineageArchiveManager.DB_FILE_NAME);
            byte[] hash = SHA256.HashData(File.ReadAllBytes(snapshot));
            Require(archive.TryInstallReplicationArchive(snapshot, hash, out string installed, out error),
                "replication install succeeds from disabled state: " + error);
            Require(archive.IsOperational && Count(archive.OperatingDB) == 1,
                "replication schema initialization does not depend on public getter access");
        });
    }

    private static void WithArchive(Action<LineageArchiveManager> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "aw3-checkpoint-archive-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        ModClass.FolderPath = root;
        typeof(LineageArchiveManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        LineageArchiveManager archive = LineageArchiveManager.Instance;
        try
        {
            Require(archive.IsOperational, "initial real archive creation succeeds");
            test(archive);
        }
        finally
        {
            archive.DisableRuntimeArchive();
            typeof(LineageArchiveManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            Directory.Delete(root, true);
        }
    }

    private static SQLiteConnection GetPrivateDb(LineageArchiveManager archive) =>
        (SQLiteConnection)typeof(LineageArchiveManager).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(archive);
    private static void SetCheckpoint(string name, object value) => typeof(LineageArchiveCheckpointService)
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private static void Exec(SQLiteConnection db, string sql) { using var command = new SQLiteCommand(sql, db); command.ExecuteNonQuery(); }
    private static long Count(SQLiteConnection db) { using var command = new SQLiteCommand("SELECT COUNT(*) FROM BoundaryMarker", db); return Convert.ToInt64(command.ExecuteScalar()); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

[TableDef("BoundaryMarker")]
internal sealed class BoundaryMarker
{
    [TableItemDef(pIsPrimary: true)] public long id;
}
