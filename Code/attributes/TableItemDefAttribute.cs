using System;

namespace AncientWarfare3.attributes
{
    /// <summary>
    ///     标记 TableItem 类的一个字段对应的 SQL 列属性(移植自 AW2)。
    ///     不打此特性的字段默认列名 = 字段名大写、非主键、可空。
    /// </summary>
    public class TableItemDefAttribute : Attribute
    {
        public TableItemDefAttribute(string pName = "", bool pIsPrimary = false, bool pIsUnique = false,
            bool pIsNotNull = false, string pDefaultValue = "", string pCheck = "")
        {
            Name = pName;
            IsPrimary = pIsPrimary;
            IsUnique = pIsUnique;
            IsNotNull = pIsNotNull;
            DefaultValue = pDefaultValue;
            Check = pCheck;
        }

        public string Name { get; private set; }
        public bool IsPrimary { get; private set; }
        public bool IsUnique { get; private set; }
        public bool IsNotNull { get; private set; }
        public string DefaultValue { get; private set; }
        public string Check { get; private set; }
    }
}
