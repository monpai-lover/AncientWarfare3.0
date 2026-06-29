using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public const string DB_FILE_NAME = "aw3_lineage_archive.db";

        private static LineageArchiveManager _instance;
        private SQLiteConnection _db;

        public bool InitializeSuccessful { get; private set; } = true;

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

        /// <summary>运行时库路径:&lt;modFolder&gt;/.runtime/aw3_lineage_archive.db。</summary>
        public static string RuntimeDbPath
        {
            get
            {
                string modFolder = ModClass.Instance.GetDeclaration().FolderPath;
                string runtimeDir = Path.Combine(modFolder, ".runtime");
                if (!Directory.Exists(runtimeDir)) Directory.CreateDirectory(runtimeDir);
                return Path.Combine(runtimeDir, DB_FILE_NAME);
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

        /// <summary>从给定存档目录恢复库:有则复制覆盖运行时库并打开,无则建新空库。</summary>
        public void LoadFromSaveDirectory(string pSaveFolder)
        {
            try
            {
                string savedDb = Path.Combine(pSaveFolder, DB_FILE_NAME);
                if (!File.Exists(savedDb))
                {
                    CreateDataBase(); // 老存档没有档案库,起一个空的
                    return;
                }

                CloseAndDeleteRuntimeDb();
                string runtime = RuntimeDbPath;
                File.Copy(savedDb, runtime, overwrite: true);
                _db = new SQLiteConnection("data source=" + runtime);
                _db.Open();
                InitializeSuccessful = true;
            }
            catch (Exception e)
            {
                InitializeSuccessful = false;
                ModClass.LogWarning("LineageArchiveManager: 从存档恢复数据库失败");
                ModClass.LogWarning(e.Message);
            }
        }

        /// <summary>把当前运行时库复制进存档目录(随存档持久化)。</summary>
        public void SaveToSaveDirectory(string pSaveFolder)
        {
            try
            {
                if (_db == null) return;
                if (!Directory.Exists(pSaveFolder)) Directory.CreateDirectory(pSaveFolder);
                string dest = Path.Combine(pSaveFolder, DB_FILE_NAME);

                // SQLite 在连接打开时直接复制文件可能漏掉未刷盘数据,先用 backup API 落盘到目标。
                using var destConn = new SQLiteConnection("data source=" + dest);
                destConn.Open();
                _db.BackupDatabase(destConn, "main", "main", -1, null, 0);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("LineageArchiveManager: 保存数据库到存档失败");
                ModClass.LogWarning(e.Message);
            }
        }

        private void CloseAndDeleteRuntimeDb()
        {
            _db?.Close();
            _db = null;
            string path = RuntimeDbPath;
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>反射扫描本程序集所有 [TableDef] 类,按字段类型建表。</summary>
        private void InitializeTables()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
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

                if (cols.Count > 0)
                    _db.CreateTable(table_def.Name, cols);
            }
        }
    }
}
