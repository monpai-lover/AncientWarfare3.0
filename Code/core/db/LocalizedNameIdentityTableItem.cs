using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("LocalizedNameIdentity")]
    public class LocalizedNameIdentityTableItem :
        AbstractTableItem<LocalizedNameIdentityTableItem>
    {
        // Schema creation is owned by LocalizedNameIdentitySchema because the
        // generic reflection path cannot express a composite primary key.
        public string identity_key = "";
        public string meta_type = "";
        public long object_id = -1;
        public string native_name = "";
        public string chinese_name = "";
        public string given_name = "";
        public string family_component = "";
        public string generator_id = "";
        public long culture_id = -1;
        public int schema_version = 0;
        public double updated_time = -1;
    }
}
