using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>姓族总览条目(姓窗口列表用)。统计参考原版 ClanWindow:总/存活/贵族/氏支数。</summary>
    internal class SurnameOverview
    {
        public string family_name;
        public int total;          // 总人数(活+死)
        public int alive;          // 存活人数
        public int noble;          // 当前贵族数(status=noble)
        public int shi_count;      // 该姓下氏支数量
        public double earliest_time; // 最早成立时间(姓族 created_time 最小值)
    }

    /// <summary>氏支条目(姓详情窗的氏支列表用)。</summary>
    internal class ShiBranchInfo
    {
        public long shi_id;
        public long lineage_id;
        public string clan_name;
        public string source_type;
        public int total;
        public int alive;
        public int noble;
        public double created_time;
        public long founder_actor_id;
    }

    /// <summary>成员条目(姓/氏支成员列表用)。活人死人统一结构。</summary>
    internal class MemberInfo
    {
        public long id;
        public string display_name;
        public string family_name;
        public string clan_name;
        public string status;
        public int sex;
        public bool is_alive;
        public double birth_time;
        public double death_time;
        public string kingdom_name;
        public string city_name;
        public long shi_id;
    }

    /// <summary>家族树三层节点(父母 / 本人 / 子女)。死者用 SQL 档案渲染。</summary>
    internal class FamilyTreeNode
    {
        public long id;
        public string display_name;
        public int sex;
        public bool is_alive;
        public string status;

        public string clan_name;     // 氏(显示用)
        public double birth_time;
        public double death_time;    // -1 表示在世/未记录
        public string kingdom_name;
        public string city_name;
        public long   shi_id = -1;
        public int    noble_distance = 99;
        public int    head;          // 头像数据(可选,用于自绘头像)
        public int    skin;
        public int    skin_set;

        public List<FamilyTreeNode> parents = new();
        public List<FamilyTreeNode> children = new();
    }
}
