using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomXiaizationState")]
    public class KingdomXiaizationStateTableItem : AbstractTableItem<KingdomXiaizationStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;

        public string kingdom_name = "";
        public string kingdom_color = "";
        public string original_species = "";
        public int xiaization_level = 0;
        public string legitimacy_type = "";
        public string court_culture_id = "";
        public string court_language_id = "";
        public int adopted_rites = 0;
        public int adopted_law = 0;
        public int stable_years = 0;
        public double start_time = -1;
        public double updated_time = -1;
    }
}
