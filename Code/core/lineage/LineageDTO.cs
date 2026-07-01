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
        public double created_time;
        public long founder_actor_id = -1;
        public string founder_name;
        public long origin_kingdom_id = -1;
        public string origin_kingdom_name;
        public string origin_kingdom_color;
        public long origin_city_id = -1;
        public string origin_city_name;
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
        public string founder_name;
        public long origin_kingdom_id = -1;
        public string origin_kingdom_name;
        public string origin_kingdom_color;
        public long origin_city_id = -1;
        public string origin_city_name;
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
        public long   founder_actor_id;
        public string founder_name;
        public long   capital_city_id;
        public string capital_city_name;
        public bool   is_alive;
        public double founded_time;
        public double destroyed_time;
    }

    /// <summary>
    ///     一个"时期"分段(历史分段折叠用),两用:
    ///     - 国家史:一段朝代。有王=一个王统治期(king_name=王名);无王=空位期(按时间区间)。
    ///     - 城市史:一段归属期。is_city_period=true,king_name 承载"所属国名";无归属国(中立/野)时 has_king=false。
    /// </summary>
    internal class ReignPeriod
    {
        public bool   has_king;
        public string king_name;           // 国家史:王名(若有谥号则显谥号);城市史:所属国名
        public string king_color = "";
        public long   king_actor_id = -1;  // 国家史:该王 actor id,用于跳转个人传记
        public string posthumous_title = "";// 谥号(如"周武王"),非空时 UI 优先显示
        public string posthumous_color = "";
        public double start_time;
        public double end_time = -1;
        public string year_prefix_snapshot;
        public bool   is_city_period;
        public string period_color = "";
        public System.Collections.Generic.List<HistoryEntry> events = new System.Collections.Generic.List<HistoryEntry>();
    }

    /// <summary>编年史列表统一行:或是朝代段头(可折叠),或是一条事件。国家史用段头分组;人物/城市史只用事件行。</summary>
    internal class HistoryRow
    {
        public bool   is_header;
        public bool   is_filter;          // true=分类筛选条（人物传记顶部）
        public bool   is_action;          // true=操作按钮行（如国家史里打开君主传记）
        public string text;
        public bool   expanded;
        public int    reign_index = -1;
        public int    dynasty_index = -1;
        public long   action_actor_id = -1;
        public string target_type = "";
        public long   target_id = -1;
        public string tooltip_title = "";
        public string tooltip_desc = "";
        public bool   dim;
    }

    /// <summary>国家史两层折叠用:一个朝代 + 其下若干王段。</summary>
    internal class DynastyView
    {
        public int    dynasty_index;      // 用于两级折叠 toggle 的序号
        public string dynasty_name = "";
        public string dynasty_color = "";
        public string kingdom_color = "";
        public double start_time;
        public double end_time = -1;
        public System.Collections.Generic.List<ReignPeriod> reigns
            = new System.Collections.Generic.List<ReignPeriod>();
    }

    /// <summary>氏族大树分支折叠探测结果(只看一层子代的轻量标记,不递归全查)。</summary>
    internal class BranchProbe
    {
        public bool has_children;   // 有直接子代(决定是否显示 +/− 折叠钮)
        public bool any_alive;      // 直接子代里有活人(全死则自动折叠)
        public bool any_descendant_alive; // 子树里有活人(死祖先下仍有活后代时不误折叠)
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
        public string kingdom_banner_id;
        public int    kingdom_color_id = -1;
        public int    kingdom_banner_icon_id = -1;
        public int    kingdom_banner_background_id = -1;
        public long   original_clan_id = -1;
        public string clan_color_text;
        public int    clan_color_id = -1;
        public int    clan_banner_icon_id = -1;
        public int    clan_banner_background_id = -1;
        public string city_name;
        public long   shi_id = -1;
        public int    noble_distance = 99;
        public int    head;          // 头像数据(可选,用于自绘头像)
        public int    skin;
        public int    skin_set;
        public int    phenotype_index;   // 死者画像重建用真实肤色(活人从 actor 实时取)
        public int    phenotype_shade;
        public long   founded_branch_shi_id = -1; // 称王分封:该人开的新氏支 id(原树显示"建立分支X氏"+点击跳转)。无则 -1
        public string death_cause = "";
        public int    tree_generation = 0;
        public string relation_label = "";

        public List<FamilyTreeNode> parents = new();
        public List<FamilyTreeNode> children = new();
    }
}
