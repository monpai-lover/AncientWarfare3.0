using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;

namespace AncientWarfare3.utils
{
    /// <summary>
    ///     SQLite 操作辅助(移植自 AW2,去掉对象池优化以减少对游戏内部类型的依赖)。
    ///     提供 SQLiteConnection 的扩展方法:CreateTable / Insert / UpdateValue / CheckKeyExist。
    /// </summary>
    public class ColumnVal
    {
        public string Name;
        public object Value;

        public static ColumnVal Create(string pName, object pValue)
        {
            return new ColumnVal { Name = pName, Value = pValue };
        }
    }

    public class SimpleColumnConstraint
    {
        public enum CheckType
        {
            Equal,
            LessThan,
            GreatThan
        }

        public string    Name;
        public CheckType  Type;
        public object     Value;

        public static SimpleColumnConstraint CreateEq(string pName, object pValue)
        {
            return new SimpleColumnConstraint { Name = pName, Value = pValue, Type = CheckType.Equal };
        }

        public static SimpleColumnConstraint CreateGt(string pName, object pValue)
        {
            return new SimpleColumnConstraint { Name = pName, Value = pValue, Type = CheckType.GreatThan };
        }

        public static SimpleColumnConstraint CreateLt(string pName, object pValue)
        {
            return new SimpleColumnConstraint { Name = pName, Value = pValue, Type = CheckType.LessThan };
        }
    }

    public static class SQLiteHelper
    {
        public enum ColumnType
        {
            NULL,
            INTEGER,
            REAL,
            TEXT,
            BLOB
        }

        private static readonly Dictionary<string, TableInfo> _tableInfos = new();

        public static void Insert(this SQLiteConnection pThis, string pTableName, params ColumnVal[] pValues)
        {
            if (pThis == null) return;

            var table = _tableInfos[pTableName];
            using var cmd = new SQLiteCommand(pThis);
            cmd.CommandText = table.InsertPrepareCMD;
            cmd.Prepare();
            foreach (var value in pValues)
            {
                cmd.Parameters.AddWithValue(table.ColumnNameToParamName[value.Name], value.Value);
            }

            cmd.ExecuteNonQuery();
        }

        public static bool CheckKeyExist(this SQLiteConnection pThis, string pTableName,
            params SimpleColumnConstraint[] pConstraints)
        {
            if (pThis == null) return false;

            var table = _tableInfos[pTableName];
            using var cmd = new SQLiteCommand(pThis);
            StringBuilder cmd_builder = new();
            cmd_builder.Append($"SELECT COUNT(*) FROM {pTableName} WHERE ");

            var need_and = false;
            foreach (var value in pConstraints)
            {
                if (need_and)
                    cmd_builder.Append(" AND ");
                else
                    need_and = true;
                cmd_builder.Append(value.Name);
                switch (value.Type)
                {
                    case SimpleColumnConstraint.CheckType.Equal:
                        cmd_builder.Append('=');
                        break;
                    case SimpleColumnConstraint.CheckType.LessThan:
                        cmd_builder.Append('<');
                        break;
                    case SimpleColumnConstraint.CheckType.GreatThan:
                        cmd_builder.Append('>');
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                cmd_builder.Append('@');
                cmd_builder.Append(value.Name);
            }

            cmd.CommandText = cmd_builder.ToString();
            cmd.Prepare();
            foreach (var value in pConstraints)
            {
                cmd.Parameters.AddWithValue(table.ColumnNameToParamName[value.Name], value.Value);
            }

            return (long)cmd.ExecuteScalar() > 0;
        }

        public static void UpdateValue(this SQLiteConnection pThis, string pTableName,
            List<SimpleColumnConstraint> pConstraints = null, params ColumnVal[] pValues)
        {
            if (pThis == null) return;

            var table = _tableInfos[pTableName];
            using var cmd = new SQLiteCommand(pThis);
            cmd.CommandText = table.GetUpdatePrepareCMD(pValues, pConstraints);
            cmd.Prepare();
            foreach (var value in pValues)
            {
                cmd.Parameters.AddWithValue(table.ColumnNameToParamName[value.Name], value.Value);
            }

            if (pConstraints != null)
                foreach (var value in pConstraints)
                {
                    cmd.Parameters.AddWithValue(table.ColumnNameToConstraintParamName[value.Name], value.Value);
                }

            cmd.ExecuteNonQuery();
        }

        public static void CreateTable(this SQLiteConnection pThis, string pTableName, List<ColumnDef> pCols)
        {
            StringBuilder cmd_builder = new();

            cmd_builder.Append($"CREATE TABLE {pTableName}");
            cmd_builder.Append('(');

            var primary_found = false;
            var need_comma = false;
            foreach (var col in pCols)
            {
                if (need_comma)
                    cmd_builder.Append(", ");
                else
                    need_comma = true;
                cmd_builder.Append(col.Name);
                cmd_builder.Append(' ');
                cmd_builder.Append(col.ValueType.ToString());

                if (col.IsPrimary)
                {
                    if (primary_found) throw new ArgumentException($"Repeat Primary Key {col.Name}");

                    primary_found = true;
                    cmd_builder.Append(" PRIMARY KEY");
                }

                if (col.IsUnique) cmd_builder.Append(" UNIQUE");

                if (col.IsNotNull) cmd_builder.Append(" NOT NULL");

                if (!string.IsNullOrEmpty(col.Default))
                {
                    cmd_builder.Append(" DEFAULT ");
                    if (col.ValueType == ColumnType.TEXT)
                    {
                        cmd_builder.Append('\'');
                        cmd_builder.Append(col.Default);
                        cmd_builder.Append('\'');
                    }
                    else
                    {
                        cmd_builder.Append(col.Default);
                    }
                }

                if (!string.IsNullOrEmpty(col.Check))
                {
                    cmd_builder.Append(" CHECK(");
                    cmd_builder.Append(col.Check);
                    cmd_builder.Append(')');
                }
            }

            cmd_builder.Append(')');

            using var cmd = new SQLiteCommand(pThis);
            cmd.CommandText = cmd_builder.ToString();
            cmd.ExecuteNonQuery();
            _tableInfos[pTableName] = new TableInfo(pTableName, pCols);
        }

        /// <summary>
        ///     仅注册表元信息(_tableInfos),不执行 CREATE —— 用于从存档**加载已有库**时,
        ///     让 Insert/UpdateValue 能拿到 prepare 语句(否则 _tableInfos 为空,Insert 抛 KeyNotFound)。
        /// </summary>
        public static void RegisterTable(string pTableName, List<ColumnDef> pCols)
        {
            _tableInfos[pTableName] = new TableInfo(pTableName, pCols);
        }

        /// <summary>
        ///     幂等补列:对已存在的表,PRAGMA 取现有列,把 pCols 里缺的列 ALTER TABLE ADD COLUMN 补上。
        ///     用于存档由旧版本(无新列)迁到新版本(代码加了字段)时,避免 INSERT 报 no such column。
        ///     SQLite 的 ADD COLUMN 不支持加 PRIMARY KEY/UNIQUE,这里只补普通列(新加字段都是普通列)。
        /// </summary>
        public static void AddMissingColumns(this SQLiteConnection pThis, string pTableName, List<ColumnDef> pCols)
        {
            if (pThis == null) return;

            var existing = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SQLiteCommand(pThis))
            {
                cmd.CommandText = $"PRAGMA table_info({pTableName})";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) existing.Add(reader.GetString(1)); // 列1 = name
            }
            if (existing.Count == 0) return; // 表不存在(理论上加载库都有),交给上层建表

            foreach (var col in pCols)
            {
                if (existing.Contains(col.Name)) continue;
                if (col.IsPrimary) continue; // 主键不能 ADD COLUMN
                using var cmd = new SQLiteCommand(pThis);
                cmd.CommandText = $"ALTER TABLE {pTableName} ADD COLUMN {col.Name} {col.ValueType}";
                cmd.ExecuteNonQuery();
            }
        }

        private class TableInfo
        {
            public readonly Dictionary<string, string> ColumnNameToConstraintParamName = new();
            public readonly Dictionary<string, string> ColumnNameToParamName = new();
            public readonly string Name;
            private readonly Dictionary<string, string> UpdatePrepareCMD = new();
            public List<ColumnDef> ColumnDefs;
            public string InsertPrepareCMD;

            public TableInfo(string pName, List<ColumnDef> pColumnDefs)
            {
                Name = pName;
                ColumnDefs = pColumnDefs;
                GeneratePrepareCMD(pColumnDefs);
            }

            private StringBuilder GenerateUpdateCMD(ColumnVal[] pCols)
            {
                var cmd_builder = new StringBuilder();
                cmd_builder.Append("UPDATE ");
                cmd_builder.Append(Name);
                cmd_builder.Append(" SET");

                var need_comma = false;
                foreach (var col in pCols)
                {
                    if (need_comma)
                        cmd_builder.Append(',');
                    else
                        need_comma = true;

                    cmd_builder.Append(' ');
                    cmd_builder.Append(col.Name);
                    cmd_builder.Append('=');
                    cmd_builder.Append('@');
                    cmd_builder.Append(col.Name);
                }

                return cmd_builder;
            }

            public string GetUpdatePrepareCMD(ColumnVal[] pCols, List<SimpleColumnConstraint> pConstraints)
            {
                StringBuilder hash_builder = new();
                foreach (var col in pCols)
                {
                    hash_builder.Append(col.Name);
                    hash_builder.Append(',');
                }

                var hash = hash_builder.ToString();
                StringBuilder cmd_builder = null;
                if (UpdatePrepareCMD.ContainsKey(hash)) goto CHECK_CONSTRAINT;

                cmd_builder = GenerateUpdateCMD(pCols);
                UpdatePrepareCMD[hash] = cmd_builder.ToString();

                CHECK_CONSTRAINT:
                if (pConstraints is { Count: > 0 })
                {
                    cmd_builder ??= new StringBuilder(UpdatePrepareCMD[hash]);
                    cmd_builder.Append(" WHERE(");

                    var need_and = false;
                    foreach (var cos in pConstraints)
                    {
                        if (need_and)
                            cmd_builder.Append(" AND ");
                        else
                            need_and = true;
                        cmd_builder.Append(cos.Name);
                        switch (cos.Type)
                        {
                            case SimpleColumnConstraint.CheckType.Equal:
                                cmd_builder.Append('=');
                                break;
                            case SimpleColumnConstraint.CheckType.LessThan:
                                cmd_builder.Append('<');
                                break;
                            case SimpleColumnConstraint.CheckType.GreatThan:
                                cmd_builder.Append('>');
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        if (!ColumnNameToConstraintParamName.TryGetValue(cos.Name, out var param_name))
                        {
                            param_name = "@COS_" + cos.Name;
                            ColumnNameToConstraintParamName[cos.Name] = param_name;
                        }

                        cmd_builder.Append(param_name);
                    }

                    cmd_builder.Append(')');
                    return cmd_builder.ToString();
                }

                return UpdatePrepareCMD[hash];
            }

            private void GeneratePrepareCMD(List<ColumnDef> pColumnDefs)
            {
                var prepare_cmd_builder = new StringBuilder();
                prepare_cmd_builder.Append($"INSERT INTO {Name}");

                prepare_cmd_builder.Append('(');
                var need_comma = false;
                foreach (var col in pColumnDefs)
                {
                    if (need_comma)
                        prepare_cmd_builder.Append(", ");
                    else
                        need_comma = true;
                    prepare_cmd_builder.Append(col.Name);
                }

                prepare_cmd_builder.Append(')');

                prepare_cmd_builder.Append(" VALUES(");
                need_comma = false;
                foreach (var col in pColumnDefs)
                {
                    if (need_comma)
                        prepare_cmd_builder.Append(", ");
                    else
                        need_comma = true;

                    var param_name = "@" + col.Name;
                    prepare_cmd_builder.Append(param_name);
                    ColumnNameToParamName[col.Name] = param_name;
                }

                prepare_cmd_builder.Append(')');
                InsertPrepareCMD = prepare_cmd_builder.ToString();
            }
        }

        public struct ColumnDef
        {
            public string Name;
            public ColumnType ValueType;
            public bool IsPrimary;
            public bool IsUnique;
            public bool IsNotNull;
            public string Default;
            public string Check;

            public ColumnDef(string pName, ColumnType pValueType = ColumnType.TEXT, bool pIsPrimary = false,
                bool pIsUnique = false, bool pIsNotNull = true, string pDefault = "", string pCheck = "")
            {
                Name = pName;
                ValueType = pValueType;
                IsPrimary = pIsPrimary;
                IsUnique = pIsUnique;
                IsNotNull = pIsNotNull;
                Default = pDefault;
                Check = pCheck;
            }
        }
    }
}
