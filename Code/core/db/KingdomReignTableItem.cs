using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>君主世系表：每个在位君主一行。开局/换君 INSERT，退位/驾崩/亡国 UPDATE end。</summary>
    [TableDef("KingdomReign")]
    public class KingdomReignTableItem : AbstractTableItem<KingdomReignTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long reign_id;

        public long   kingdom_id     = -1;
        public string kingdom_color   = "";
        public long   king_actor_id  = -1;
        public long   shi_id = -1;
        public long   dynasty_id = -1;
        public long   mandate_period_id = -1;
        public int    highest_title;
        public string state_name_snapshot = "";
        public string king_name      = "";
        public string king_color     = "";
        public int    reign_index    = 0;        // 该国第几任君主（1起）
        public double start_time;
        [TableItemDef(pDefaultValue: "-1")] public double end_time;
        public string year_name_stem = "";       // 开始时年号词干快照
        public string year_name_color = "";
        public string end_reason     = "";       // died / abdicated / deposed / kingdom_fell
        public int    start_population = 0;      // 开始时王国人口（综合国力谥号基准）
        public int    start_city_count = 0;      // 开始时城池数（综合国力谥号基准）
        public int    start_army_count = 0;
        public int    end_population = 0;
        public int    end_city_count = 0;
        public int    end_army_count = 0;
        public int    is_founder = 0;
        public int    war_wins = 0;
        public int    war_losses = 0;
        public int    lost_capital = 0;
        public string death_cause = "";
    }
}
