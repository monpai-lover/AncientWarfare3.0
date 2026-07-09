namespace AncientWarfare3.core.court
{
    public static class CourtOfficeLayer
    {
        public const string Primitive = "primitive";
        public const string Central = "central";
        public const string City = "city";
        public const string Military = "military";
        public const string Censor = "censor";
    }

    public static class CourtOfficeId
    {
        public const string Chancellor = "chancellor";
        public const string Censor = "censor";
        public const string Marshal = "marshal";
        public const string Justice = "justice";
        public const string Steward = "steward";
        public const string Erudite = "erudite";
        public const string Governor = "governor";
        public const string GranaryOfficer = "granary_officer";
        public const string Constable = "constable";
    }

    public static class CourtSchoolId
    {
        public const string None = "";
        public const string PrimitiveMinister = "primitive_minister";
        public const string Warrior = "warrior";
        public const string Elder = "elder";
        public const string Shaman = "shaman";
        public const string Hermit = "hermit";
        public const string Ru = "ru";
        public const string Legalist = "fa";
        public const string Dao = "dao";
        public const string Mohist = "mo";
        public const string Military = "bing";
        public const string Diplomat = "zongheng";
        public const string Agrarian = "nong";
        public const string YinYang = "yinyang";
        public const string Logician = "ming";
    }

    public static class CourtTraitId
    {
        public const string Ru = "aw_school_ru";
        public const string Legalist = "aw_school_fa";
        public const string Dao = "aw_school_dao";
        public const string Mohist = "aw_school_mo";
        public const string Military = "aw_school_bing";
        public const string Diplomat = "aw_school_zongheng";
        public const string Agrarian = "aw_school_nong";
        public const string YinYang = "aw_school_yinyang";
        public const string Logician = "aw_school_ming";
    }

    public static class CourtEvents
    {
        public const string Founded = "court_founded";
        public const string PrimitiveUpgraded = "court_primitive_upgraded";
        public const string OfficerAppointed = "court_officer_appointed";
        public const string OfficerDismissed = "court_officer_dismissed";
        public const string FactionDominant = "court_faction_dominant";
        public const string ReformEvent = "court_reform_event";
        public const string CityBureauChanged = "court_city_bureau_changed";
    }
}
