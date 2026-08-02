using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using AncientWarfare3.attributes;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     姓族/氏支/家族树档案的 SQLite 管理器(架构移植自 AW2 EventsManager,适配新版)。
    ///
    ///     职责:
    ///     - 维护一个 SQLite 连接(运行时库放 mod 的 .runtime/ 目录)。
    ///     - 启动时反射扫描本程序集所有 [TableDef] 类,自动建表(列类型由字段类型推断)。
    ///     - 提供 OperatingDB 给各表读写。
    ///     - 支持随游戏存档持久化:存档时把运行时库复制进存档目录,读档时复制回来(由 AW_SavePatch 调用)。
    ///
    ///     与 AW2 区别:DB 不再是固定 .tmp.db 每局删重建;改为运行时库 + 随存档复制,实现跨存档保留。
    /// </summary>
    public class LineageArchiveManager
    {
        public const string DB_FILE_NAME = LineageRuntimePathRules.DbFileName;
        public const string MISSING_ARCHIVE_ERROR = "Lineage archive is missing.";

        private static LineageArchiveManager _instance;
        private static long _runtimeDatabaseEpoch;
        private SQLiteConnection _db;

        public bool InitializeSuccessful { get; private set; } = true;
        public bool IsOperational => InitializeSuccessful && _db != null;
        public static long RuntimeDatabaseEpoch =>
            Interlocked.Read(ref _runtimeDatabaseEpoch);

        public static bool IsMissingArchiveError(string pError)
        {
            return string.Equals(pError, MISSING_ARCHIVE_ERROR,
                StringComparison.Ordinal);
        }

        public static LineageArchiveManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = new LineageArchiveManager();
                _instance.CreateDataBase();
                return _instance;
            }
        }

        /// <summary>当前 SQLite 连接(可能为 null,调用方需判空)。</summary>
        public SQLiteConnection OperatingDB
        {
            get
            {
                _ = Instance;
                return _db;
            }
        }

        /// <summary>当前进程隔离的族谱运行时库路径。</summary>
        public static string RuntimeDbPath
        {
            get
            {
                string modFolder = ModClass.Instance.GetDeclaration().FolderPath;
                string path = LineageRuntimePathRules.Resolve(modFolder,
                    System.Diagnostics.Process.GetCurrentProcess().Id);
                string runtimeDir = Path.GetDirectoryName(path);
                if (!Directory.Exists(runtimeDir)) Directory.CreateDirectory(runtimeDir);
                return path;
            }
        }

        /// <summary>新建运行时库(删旧库重建空表)。新世界 / 读不到存档库时用。</summary>
        public void CreateDataBase()
        {
            try
            {
                CloseAndDeleteRuntimeDb();
                string path = RuntimeDbPath;
                SQLiteConnection.CreateFile(path);
                _db = new SQLiteConnection("data source=" + path);
                _db.Open();
                LineageArchivePragmaService.Configure(_db);
                Interlocked.Increment(ref _runtimeDatabaseEpoch);
                InitializeTables();
                InitializeSuccessful = true;
            }
            catch (Exception e)
            {
                InitializeSuccessful = false;
                ModClass.LogWarning("LineageArchiveManager: 创建数据库失败,档案将不会被保存");
                ModClass.LogWarning(e.Message);
                ModClass.LogWarning(e.StackTrace);
            }
        }

        /// <summary>从给定存档目录恢复库。失败时停用运行时档案，绝不以空库代替存档。</summary>
        public void LoadFromSaveDirectory(string pSaveFolder)
        {
            if (TryLoadFromSaveDirectory(pSaveFolder, out string error)) return;

            DisableRuntimeArchive();
            ModClass.LogWarning(
                "LineageArchiveManager: failed to restore archive from save");
            ModClass.LogWarning(error);
        }

        /// <summary>
        ///     隔离无法确认归属的运行时档案。仅显式创建新世界时才允许随后建立空库。
        /// </summary>
        public void DisableRuntimeArchive()
        {
            try { CloseAndDeleteRuntimeDb(); }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "LineageArchiveManager: failed to quarantine runtime archive");
                ModClass.LogWarning(error.Message);
            }
            finally
            {
                InitializeSuccessful = false;
            }
        }

        public bool TryLoadFromSaveDirectory(string pSaveFolder,
            out string pError)
        {
            pError = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(pSaveFolder))
                {
                    pError = "Save directory is required.";
                    return false;
                }

                string savedDb = Path.Combine(Path.GetFullPath(pSaveFolder),
                    DB_FILE_NAME);
                if (!File.Exists(savedDb))
                {
                    pError = MISSING_ARCHIVE_ERROR;
                    return false;
                }

                CloseAndDeleteRuntimeDb();
                string runtime = RuntimeDbPath;
                File.Copy(savedDb, runtime, overwrite: true);
                OpenRuntimeDatabase(runtime);
                EnsureLoadedSchema(); // 注册表元信息 + 旧档案幂等补列(否则 Insert 抛 KeyNotFound / no such column)
                InitializeSuccessful = true;
                return true;
            }
            catch (Exception error)
            {
                InitializeSuccessful = false;
                pError = string.IsNullOrWhiteSpace(error.Message)
                    ? "Lineage archive installation failed."
                    : error.Message;
                try { CloseAndDeleteRuntimeDb(); }
                catch { }
                return false;
            }
        }

        /// <summary>把当前运行时库复制进存档目录(随存档持久化)。</summary>
        public void SaveToSaveDirectory(string pSaveFolder)
        {
            if (!TryExportLineageArchive(pSaveFolder, out string error))
            {
                ModClass.LogWarning("LineageArchiveManager: lineage export failed");
                ModClass.LogWarning(error);
            }
        }

        internal bool TryExportLineageArchive(string pSaveFolder,
            out string pError)
        {
            pError = string.Empty;
            string temporary = null;

            try
            {
                if (_db == null)
                {
                    pError = "Live lineage archive is unavailable.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(pSaveFolder))
                {
                    pError = "Save directory is required.";
                    return false;
                }

                string directory = Path.GetFullPath(pSaveFolder);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                string destination = Path.Combine(directory, DB_FILE_NAME);
                temporary = destination + ".tmp";
                DeleteDatabaseFile(temporary);

                using (var destinationConnection = new SQLiteConnection(
                           LineageArchivePragmaService
                               .SnapshotTargetConnectionString(temporary)))
                {
                    destinationConnection.Open();
                    LineageArchivePragmaService.ConfigureSnapshotTarget(
                        destinationConnection);
                    _db.BackupDatabase(destinationConnection, "main", "main",
                        -1, null, 0);
                    LineageArchivePragmaService.ConfigureSnapshotTarget(
                        destinationConnection);
                }

                DeleteDatabaseSidecars(temporary);
                if (!LineageArchivePragmaService.TryValidateSnapshot(temporary,
                        out bool journalModeInvalid, out string validationError))
                {
                    pError = journalModeInvalid
                        ? "Snapshot lineage archive journal mode is invalid: " +
                          validationError
                        : "Snapshot lineage archive quick_check failed: " +
                          validationError;
                    return false;
                }
                if (HasDatabaseSidecars(temporary))
                {
                    pError = "Snapshot lineage archive left WAL or SHM sidecars.";
                    return false;
                }

                DeleteDatabaseSidecars(destination);
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null);
                else
                    File.Move(temporary, destination);
                temporary = null;

                DeleteDatabaseSidecars(destination);
                if (HasDatabaseSidecars(destination))
                {
                    pError = "Published lineage archive has WAL or SHM sidecars.";
                    return false;
                }

                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporary))
                    DeleteDatabaseFileBestEffort(temporary);
            }
        }

        internal bool TryInstallReplicationArchive(
            string pVerifiedArchivePath, byte[] expectedSha256,
            out string pInstalledPath, out string pError)
        {
            pInstalledPath = string.Empty;
            pError = string.Empty;
            string staging = null;
            string backup = null;
            string runtime = null;
            bool runtimeMoved = false;

            try
            {
                if (expectedSha256 == null || expectedSha256.Length != 32)
                {
                    pError = "Expected archive SHA-256 must contain 32 bytes.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(pVerifiedArchivePath) ||
                    !Path.IsPathRooted(pVerifiedArchivePath))
                {
                    pError = "Verified archive path must be absolute.";
                    return false;
                }

                string source = Path.GetFullPath(pVerifiedArchivePath);
                if (!File.Exists(source) ||
                    (File.GetAttributes(source) &
                     (FileAttributes.Directory |
                      FileAttributes.ReparsePoint)) != 0)
                {
                    pError = "Verified archive must be a regular file.";
                    return false;
                }
                if (HasDatabaseSidecars(source))
                {
                    pError = "Verified archive has WAL or SHM sidecars.";
                    return false;
                }
                if (!LineageArchivePragmaService.TryValidateSnapshot(source,
                        out bool sourceJournalInvalid,
                        out string sourceValidationError))
                {
                    pError = sourceJournalInvalid
                        ? "Verified archive journal mode is invalid: " +
                          sourceValidationError
                        : "Verified archive quick_check failed: " +
                          sourceValidationError;
                    return false;
                }
                if (!HashMatches(source, expectedSha256))
                {
                    pError = "Verified archive SHA-256 does not match.";
                    return false;
                }

                runtime = RuntimeDbPath;
                if (string.Equals(source, Path.GetFullPath(runtime),
                        StringComparison.OrdinalIgnoreCase))
                {
                    pError = "Verified archive cannot be the live runtime file.";
                    return false;
                }
                string nonce = Guid.NewGuid().ToString("N");
                staging = runtime + ".replication-install-" + nonce + ".tmp";
                backup = runtime + ".replication-backup-" + nonce + ".bak";
                DeleteDatabaseFile(staging);
                DeleteDatabaseFile(backup);
                File.Copy(source, staging, overwrite: false);
                DeleteDatabaseSidecars(staging);

                if (!LineageArchivePragmaService.TryValidateSnapshot(staging,
                        out bool stagingJournalInvalid,
                        out string stagingValidationError))
                {
                    pError = stagingJournalInvalid
                        ? "Staged archive journal mode is invalid: " +
                          stagingValidationError
                        : "Staged archive quick_check failed: " +
                          stagingValidationError;
                    return false;
                }
                if (!HashMatches(staging, expectedSha256))
                {
                    pError = "Staged archive SHA-256 does not match.";
                    return false;
                }
                if (HasDatabaseSidecars(staging))
                {
                    pError = "Staged archive left WAL or SHM sidecars.";
                    return false;
                }
                if (_db != null &&
                    !LineageArchivePragmaService.CheckpointForSave(_db))
                {
                    pError = "Live archive checkpoint failed before install.";
                    return false;
                }

                CloseRuntimeDatabase();
                DeleteDatabaseSidecars(runtime);
                if (File.Exists(runtime))
                {
                    File.Move(runtime, backup);
                    runtimeMoved = true;
                }
                File.Move(staging, runtime);
                staging = null;

                try
                {
                    OpenRuntimeDatabase(runtime);
                    EnsureLoadedSchema();
                    InitializeSuccessful = true;
                }
                catch
                {
                    CloseRuntimeDatabase();
                    DeleteDatabaseFileBestEffort(runtime);
                    if (runtimeMoved && File.Exists(backup))
                    {
                        File.Move(backup, runtime);
                        runtimeMoved = false;
                        OpenRuntimeDatabase(runtime);
                        EnsureLoadedSchema();
                        InitializeSuccessful = true;
                        backup = null;
                    }
                    throw;
                }

                DeleteDatabaseFileBestEffort(backup);
                backup = null;
                pInstalledPath = runtime;
                return true;
            }
            catch (Exception error)
            {
                if (_db == null && runtimeMoved &&
                    !string.IsNullOrEmpty(runtime) &&
                    !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    try
                    {
                        DeleteDatabaseFileBestEffort(runtime);
                        File.Move(backup, runtime);
                        runtimeMoved = false;
                        OpenRuntimeDatabase(runtime);
                        EnsureLoadedSchema();
                        InitializeSuccessful = true;
                        backup = null;
                    }
                    catch (Exception rollbackError)
                    {
                        InitializeSuccessful = false;
                        pError = "Replication install and rollback failed: " +
                                 error.Message + "; " + rollbackError.Message;
                        return false;
                    }
                }
                else if (_db == null)
                {
                    InitializeSuccessful = false;
                }

                pError = string.IsNullOrWhiteSpace(error.Message)
                    ? "Replication archive installation failed."
                    : error.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(staging))
                    DeleteDatabaseFileBestEffort(staging);
            }
        }

        private void OpenRuntimeDatabase(string pPath)
        {
            _db = new SQLiteConnection("data source=" + pPath);
            _db.Open();
            LineageArchivePragmaService.Configure(_db);
            Interlocked.Increment(ref _runtimeDatabaseEpoch);
        }

        private void CloseRuntimeDatabase()
        {
            if (_db == null) return;
            _db.Close();
            _db.Dispose();
            _db = null;
            Interlocked.Increment(ref _runtimeDatabaseEpoch);
        }

        private static bool HashMatches(string pPath, byte[] pExpected)
        {
            using var stream = new FileStream(pPath, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            using SHA256 sha256 = SHA256.Create();
            byte[] actual = sha256.ComputeHash(stream);
            if (actual.Length != pExpected.Length) return false;
            var difference = 0;
            for (var index = 0; index < actual.Length; index++)
                difference |= actual[index] ^ pExpected[index];
            return difference == 0;
        }

        private void CloseAndDeleteRuntimeDb()
        {
            CloseRuntimeDatabase();
            string path = RuntimeDbPath;
            if (File.Exists(path)) File.Delete(path);
            DeleteDatabaseSidecars(path);
        }

        private static void DeleteDatabaseSidecars(string pPath)
        {
            string wal = pPath + "-wal";
            string sharedMemory = pPath + "-shm";
            if (File.Exists(wal)) File.Delete(wal);
            if (File.Exists(sharedMemory)) File.Delete(sharedMemory);
        }

        private static bool HasDatabaseSidecars(string pPath)
        {
            return File.Exists(pPath + "-wal") || File.Exists(pPath + "-shm");
        }

        private static void DeleteDatabaseFile(string pPath)
        {
            if (File.Exists(pPath)) File.Delete(pPath);
            DeleteDatabaseSidecars(pPath);
        }

        private static void DeleteDatabaseFileBestEffort(string pPath)
        {
            try { DeleteDatabaseFile(pPath); }
            catch { }
        }

        /// <summary>反射扫描本程序集所有 [TableDef] 类,按字段类型**建表**(新库用)。</summary>
        private void InitializeTables()
        {
            LocalizedNameIdentitySchema.Ensure(_db);
            foreach (var (tableName, cols) in EnumerateTableSchemas())
                if (cols.Count > 0) _db.CreateTable(tableName, cols);
            LineageArchiveIndexManager.EnsureIndexes(_db);
        }

        /// <summary>
        ///     从存档**加载已有库**后调用:① 注册表元信息(_tableInfos,否则 Insert 抛 KeyNotFound);
        ///     ② 幂等补列(旧版本存档缺新代码加的列,如 KINGDOM_COLOR,补上避免 INSERT no such column)。
        /// </summary>
        private void EnsureLoadedSchema()
        {
            LocalizedNameIdentitySchema.Ensure(_db);
            foreach (var (tableName, cols) in EnumerateTableSchemas())
            {
                if (cols.Count == 0) continue;
                if (!_db.TableExists(tableName))
                {
                    _db.CreateTable(tableName, cols);
                    continue;
                }

                SQLiteHelper.RegisterTable(tableName, cols);
                _db.AddMissingColumns(tableName, cols);
            }
            LineageArchiveIndexManager.EnsureIndexes(_db);
        }

        /// <summary>反射出每个 [TableDef] 类对应的 (表名, 列定义)。建表 / 补列共用。</summary>
        private static IEnumerable<(string, List<SQLiteHelper.ColumnDef>)> EnumerateTableSchemas()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                if (type == typeof(LocalizedNameIdentityTableItem)) continue;
                var table_def = type.GetCustomAttribute<TableDefAttribute>();
                if (table_def == null) continue;

                var fields = type.GetFields();
                var cols = (
                    from field in fields
                    let attribute = field.GetCustomAttribute<TableItemDefAttribute>() ?? new TableItemDefAttribute()
                    let col_type = field.FieldType.Name.ToLower() switch
                    {
                        "string"  => SQLiteHelper.ColumnType.TEXT,
                        "boolean" => SQLiteHelper.ColumnType.INTEGER,
                        "byte"    => SQLiteHelper.ColumnType.INTEGER,
                        "sbyte"   => SQLiteHelper.ColumnType.INTEGER,
                        "int16"   => SQLiteHelper.ColumnType.INTEGER,
                        "uint16"  => SQLiteHelper.ColumnType.INTEGER,
                        "int32"   => SQLiteHelper.ColumnType.INTEGER,
                        "uint32"  => SQLiteHelper.ColumnType.INTEGER,
                        "int64"   => SQLiteHelper.ColumnType.INTEGER,
                        "uint64"  => SQLiteHelper.ColumnType.INTEGER,
                        "single"  => SQLiteHelper.ColumnType.REAL,
                        "double"  => SQLiteHelper.ColumnType.REAL,
                        _         => SQLiteHelper.ColumnType.BLOB
                    }
                    let name = string.IsNullOrEmpty(attribute.Name) ? field.Name.ToUpper() : attribute.Name
                    select new SQLiteHelper.ColumnDef(name, col_type, attribute.IsPrimary, attribute.IsUnique,
                        attribute.IsNotNull, attribute.DefaultValue, attribute.Check)
                ).ToList();

                yield return (table_def.Name, cols);
            }
        }
    }
}
