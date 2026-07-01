namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     编年史事件类型(event_type)与人物事件分类(category)的集中常量。
    ///     避免字符串散落各处;UI 筛选、写入、查询共用同一套值。
    /// </summary>
    internal static class ChronicleCategory
    {
        public const string LIFE  = "life";   // 人生:出生/得子/死亡
        public const string HONOR = "honor";  // 荣耀:登基/封城主/退位/上谥
        public const string CLAN  = "clan";   // 氏族:成家主/逐出氏族
        public const string WAR   = "war";    // 战事:叛乱/入伍/重要击杀
        public const string BOND  = "bond";   // 羁绊:恋爱/牵绊离世
    }

    /// <summary>人物传记事件类型。</summary>
    internal static class PersonEvent
    {
        public const string BIRTH             = "birth";
        public const string HAD_CHILD         = "had_child";
        public const string BECOME_KING       = "become_king";
        public const string DEATH             = "death";
        public const string BECOME_LEADER     = "become_leader";
        public const string BECOME_CLAN_CHIEF = "become_clan_chief";
        public const string EXILED_CLAN       = "exiled_clan";
        public const string REBELLION         = "rebellion";
        public const string ENLISTED          = "enlisted";
        public const string IMPORTANT_KILL    = "important_kill";
        public const string FELL_IN_LOVE      = "fell_in_love";
        public const string BOND_DEATH        = "bond_death";
        public const string ABDICATE          = "abdicate";
        public const string POSTHUMOUS        = "posthumous";
    }

    /// <summary>国家历史事件类型。</summary>
    internal static class KingdomEvent
    {
        public const string FOUND        = "found";
        public const string RULE_CHANGE  = "rule_change";
        public const string DESTROYED    = "destroyed";
        public const string KING_DIED    = "king_died";
        public const string ABDICATE     = "abdicate";
        public const string CITY_GAINED  = "city_gained";
        public const string CITY_LOST    = "city_lost";
        public const string WAR_START    = "war_start";
        public const string WAR_END      = "war_end";
        public const string REBELLION    = "rebellion";
        public const string DYNASTY_CHANGE = "dynasty_change";
        public const string ERA_CHANGE   = "era_change";
        public const string NOTABLE_DEATH = "notable_death"; // 重要人物(王/城主/名人)被杀,国家史留痕
        public const string POSTHUMOUS   = "posthumous";
    }

    /// <summary>城市历史事件类型。</summary>
    internal static class CityEvent
    {
        public const string CITY_FOUND    = "city_found";
        public const string CITY_TRANSFER = "city_transfer";
    }
}
