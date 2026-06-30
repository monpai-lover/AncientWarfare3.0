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

    /// <summary>王国档案条目(全王国列表用)。从 KingdomArchive 表读,含旗帜重建所需快照。</summary>
    internal class KingdomArchiveInfo
    {
        public long   kingdom_id;
        public string kingdom_name;
        public string color_text;
        public int    color_id;
        public int    banner_icon_id;
        public int    banner_background_id;
        public string banner_id;
        public bool   is_alive;
        public double founded_time;
        public double destroyed_time;
    }

    /// <summary>一个朝代时期(国家历史分段折叠用)。有王=一个王统治期;无王=一段空位期(按时间区间)。</summary>
    internal class ReignPeriod
    {
        public bool   has_king;
        public string king_name;
        public double start_time;
        public double end_time = -1;       // -1 = 至今/未结束
        public string year_prefix_snapshot; // 该段起始事件的 year_prefix 快照(含年号+纪年,亡国也准)
        public System.Collections.Generic.List<HistoryEntry> events = new System.Collections.Generic.List<HistoryEntry>();
    }

    /// <summary>编年史列表统一行:或是朝代段头(可折叠),或是一条事件。国家史用段头分组;人物/城市史只用事件行。</summary>
    internal class HistoryRow
    {
        public bool   is_header;       // true=朝代段头,false=事件行
        public string text;            // 显示文本(段头标题 / 事件正文)
        public bool   expanded;        // 段头:当前是否展开(决定 +/− 与其事件是否显示)
        public int    reign_index = -1; // 段头:对应 ReignPeriod 序号(toggle 用)
        public bool   dim;             // 事件行缩进/淡显(归属某段)
    }

    /// <summary>氏族大树分支折叠探测结果(只看一层子代的轻量标记,不递归全查)。</summary>
    internal class BranchProbe
    {
        public bool has_children;   // 有直接子代(决定是否显示 +/− 折叠钮)
        public bool any_alive;      // 直接子代里有活人(全死则自动折叠)
        public bool any_important;  // 直接子代里有 king / city leader / heir(无重要人物则自动折叠)
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
        public long   kingdom_id = -1; // 所属国 id(用于名字着色 / banner 跳转;已亡国仍存 id 但 get 返 null)
        public string kingdom_name;
        public string kingdom_color;   // 所属国文字色 hex(随档案存,亡国后名字仍用此色)
        public string city_name;
        public long   shi_id = -1;
        public int    noble_distance = 99;
        public int    head;          // 头像数据(可选,用于自绘头像)
        public int    skin;
        public int    skin_set;
        public int    phenotype_index;   // 死者画像重建用真实肤色(活人从 actor 实时取)
        public int    phenotype_shade;
        public long   founded_branch_shi_id = -1; // 称王分封:该人开的新氏支 id(原树显示"建立分支X氏"+点击跳转)。无则 -1

        public List<FamilyTreeNode> parents = new();
        public List<FamilyTreeNode> children = new();
    }
}
