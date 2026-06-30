namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     actor.data / kingdom.data 上 AW3 姓氏系统使用的自定义字段键(随存档序列化)。
    ///     沿用 AW2 兼容命名(family_name/clan_name/chinese_family_name),其余用 aw_ 前缀。
    /// </summary>
    internal static class LineageKeys
    {
        public const string FAMILY_NAME = "family_name";              // 姓(血统姓)
        public const string CLAN_NAME = "clan_name";                  // 氏(AW3 的氏,非原版 Clan)
        public const string CHINESE_FAMILY_NAME = "chinese_family_name"; // 中文命名兼容字段

        public const string GIVEN_NAME = "aw_given_name";             // 单名(所有改全名操作的基底)
        public const string LINEAGE_ID = "aw_lineage_id";             // 姓族谱系 id(long)
        public const string SHI_ID = "aw_shi_id";                     // 氏支 id(long)
        public const string NOBLE_DISTANCE = "aw_noble_distance";     // 距最近贵族祖先的父系代数,本人贵族=0
        public const string NAME_INTEGRATED = "aw_name_integrated";   // 是否被合流规则处理过(bool)
        public const string LINEAGE_STATUS = "aw_lineage_status";     // none/noble/common_lineage/slave_lineage
        public const string EAGER_BUILDER = "aw_eager_builder";       // 积极建城候选(多余 male 子嗣) flag,bool

        // kingdom.data
        public const string KINGDOM_INTEGRATED = "aw_name_integrated"; // 该国是否完成姓氏合流(bool)
        public const string CHRONICLE_LAST_KING_ID = "aw_chronicle_last_king"; // 编年史:该国上次登记的王 id(防同王重复记换君)
        // 称王分封:称王者 actor.data 上标记"我建立的新氏支 id"(原氏族树在他的位置显示"建立分支X氏"+点击跳转新支用)。
        public const string FOUNDED_BRANCH_SHI_ID = "aw_founded_branch_shi"; // long,无则 -1

        // 贵族特质 id(批B XiaTraits 注册)
        public const string TRAIT_GUIZU = "guizu";

        // noble_distance 达到该值且本人非贵族 → 退回平民,移除 guizu
        public const int NOBLE_DECAY_DISTANCE = 3;

        // ── kingdom.data 自定义字段(随存档序列化) ──
        public const string KINGDOM_HEIR_ID = "aw_heir_id";          // 继承人 actor id(long)
        public const string IS_HEIR = "aw_is_heir";                  // actor.data:本人是否当前某国继承人(bool,unit_heir 皮肤 + minimap 用)
        public const string KINGDOM_YEAR_NAME = "aw_year_name";      // 年号中间字(string)
        public const string KINGDOM_YEAR_START = "aw_year_start";    // 年号起始 world_time(double)
        public const string KINGDOM_TITLE = "aw_title";              // 头衔等级(int,见 KingdomTitle)
    }

    /// <summary>姓族身份状态。</summary>
    internal static class LineageStatus
    {
        public const string NONE = "none";
        public const string NOBLE = "noble";
        public const string COMMON = "common_lineage";
        public const string SLAVE = "slave_lineage";
    }

    /// <summary>贵族晋升触发来源。</summary>
    internal enum NobleTrigger
    {
        King,        // 成为国王
        CityLeader,  // 成为城主
        Figure       // 重要成名者 / 历史人物降临
    }

    /// <summary>氏支来源类型(ShiBranch.source_type)。</summary>
    internal static class ShiSourceType
    {
        public const string ENFEOFFED = "enfeoffed";       // 封地
        public const string INHERITED = "inherited";       // 继承
        public const string RANDOM = "random";             // 随机
        public const string INTEGRATION = "integration";   // 合流
        public const string SPECIAL_FIGURE = "special_figure"; // 名人/降临
        public const string KING_FOUNDED = "king_founded"; // 称王分封(建新国/夺别国 → 脱离原氏开新支)
    }
}
