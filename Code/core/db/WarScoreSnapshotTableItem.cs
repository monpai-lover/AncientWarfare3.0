using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarScoreSnapshot")]
    public sealed class WarScoreSnapshotTableItem :
        AbstractTableItem<WarScoreSnapshotTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long war_id;
        public long attacker_kingdom_id = -1;
        public long defender_kingdom_id = -1;
        public int score;
        public int city_score;
        public int battle_score;
        public int goal_score;
        public int loss_score;
        public int attacker_losses;
        public int defender_losses;
        public int duration_years;
        [TableItemDef(pDefaultValue: "-2147483648")]
        public int last_calibrated_year = int.MinValue;
        public int attacker_exhaustion_relief;
        public int defender_exhaustion_relief;
        public int attacker_exhaustion;
        public int defender_exhaustion;
        public int active = 1;
        public string winner = "";
        public double started_time = -1d;
        public double updated_time = -1d;
        public double ended_time = -1d;
        public long revision;
    }
}
