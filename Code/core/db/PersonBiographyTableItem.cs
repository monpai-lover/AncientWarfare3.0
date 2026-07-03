using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    /// Person biography event table. One row is one life event snapshot.
    /// </summary>
    [TableDef("PersonBiography")]
    public class PersonBiographyTableItem : AbstractTableItem<PersonBiographyTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long actor_id = -1;
        public double world_time;
        public string year_prefix;
        public string year_prefix_rich = "";
        public string subject_name;
        public string subject_color = "";
        public string content;
        public string content_rich = "";
        public string event_type;
        public string category = "";
        [TableItemDef(pDefaultValue: "-1")] public int age_at_event = -1;
        [TableItemDef(pDefaultValue: "0")] public int is_king_at_event = 0;
        public string role_snapshot = "";
        public string role_label = "";
        public long context_kingdom_id = -1;
        public string context_kingdom_name = "";
        public string context_kingdom_color = "";
        public string target_type = "";
        [TableItemDef(pDefaultValue: "-1")] public long target_id = -1;
    }
}
