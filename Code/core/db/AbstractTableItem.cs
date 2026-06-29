using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Reflection;
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    public abstract class BaseTableItem
    {
    }

    /// <summary>
    ///     SQLite 表行的强类型映射基类(移植自 AW2)。
    ///     字段 ↔ 列名映射:无 [TableItemDef(Name)] 则列名 = 字段名大写。
    ///     ReadFromReader 把一行 SQLiteDataReader 数据反射填回字段。
    ///
    ///     与 AW2 区别:① field_name_to_column_name 改为实例字段(AW2 是 static,多表会串字段);
    ///     ② 用标准反射 FieldInfo.SetValue 代替 NML 的 SetField 扩展。
    /// </summary>
    public abstract class AbstractTableItem<T> : BaseTableItem where T : AbstractTableItem<T>, new()
    {
        private readonly Dictionary<string, string> field_name_to_column_name = new();

        private static readonly Dictionary<Type, string> type_to_table_name = new();

        private void EnsureFieldMap()
        {
            if (field_name_to_column_name.Count != 0) return;
            var fields = GetType().GetFields();
            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<TableItemDefAttribute>() ?? new TableItemDefAttribute();
                field_name_to_column_name[field.Name] =
                    string.IsNullOrEmpty(attr.Name) ? field.Name.ToUpper() : attr.Name;
            }
        }

        public virtual void ReadFromReader(SQLiteDataReader reader)
        {
            EnsureFieldMap();

            foreach (var pair in field_name_to_column_name)
            {
                int ordinal;
                try
                {
                    ordinal = reader.GetOrdinal(pair.Value);
                }
                catch (IndexOutOfRangeException)
                {
                    continue; // 该列不在结果集
                }

                if (ordinal < 0 || reader.IsDBNull(ordinal)) continue;

                var field = GetType().GetField(pair.Key);
                if (field == null) continue;

                var type = reader.GetFieldType(ordinal);
                object value;
                if (type == typeof(string)) value = reader.GetString(ordinal);
                else if (type == typeof(int)) value = reader.GetInt32(ordinal);
                else if (type == typeof(long)) value = reader.GetInt64(ordinal);
                else if (type == typeof(float)) value = reader.GetFloat(ordinal);
                else if (type == typeof(double)) value = reader.GetDouble(ordinal);
                else if (type == typeof(bool)) value = reader.GetBoolean(ordinal);
                else value = reader[ordinal];

                SetFieldConverted(field, value);
            }
        }

        /// <summary>把 SQLite 返回值转换到字段实际类型再赋值(SQLite 列类型与 C# 字段类型可能不完全一致)。</summary>
        private void SetFieldConverted(FieldInfo pField, object pValue)
        {
            try
            {
                var target = pField.FieldType;
                if (target == typeof(long)) pField.SetValue(this, Convert.ToInt64(pValue));
                else if (target == typeof(int)) pField.SetValue(this, Convert.ToInt32(pValue));
                else if (target == typeof(float)) pField.SetValue(this, Convert.ToSingle(pValue));
                else if (target == typeof(double)) pField.SetValue(this, Convert.ToDouble(pValue));
                else if (target == typeof(bool)) pField.SetValue(this, Convert.ToBoolean(pValue));
                else if (target == typeof(string)) pField.SetValue(this, Convert.ToString(pValue));
                else pField.SetValue(this, pValue);
            }
            catch
            {
                // 类型不兼容时跳过该字段,不让单个列错误炸掉整行读取
            }
        }

        public static string GetTableName()
        {
            if (type_to_table_name.TryGetValue(typeof(T), out var name)) return name;
            name = typeof(T).GetCustomAttribute<TableDefAttribute>().Name;
            type_to_table_name[typeof(T)] = name;
            return name;
        }
    }
}
