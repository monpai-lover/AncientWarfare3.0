using System;

namespace AncientWarfare3.attributes
{
    /// <summary>标记一个类对应一张 SQLite 表(移植自 AW2)。EventsManager 反射扫描此特性自动建表。</summary>
    public class TableDefAttribute : Attribute
    {
        public TableDefAttribute(string pName)
        {
            Name = pName;
        }

        public string Name { get; private set; }
    }
}
