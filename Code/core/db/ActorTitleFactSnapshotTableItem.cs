using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("ActorTitleFactSnapshot")]
    public class ActorTitleFactSnapshotTableItem : AbstractTableItem<ActorTitleFactSnapshotTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long actor_id;

        public int diplomacy;
        public int warfare;
        public int stewardship;
        public int intelligence;
        public int health;
        public int combat;
        public long trait_flags;
        public double decided_time;
    }
}
