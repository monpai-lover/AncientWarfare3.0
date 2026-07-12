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

        // 三省六部(Sui-Tang tier)
        public const string Zhongshu = "zhongshu"; // 中书省
        public const string Menxia = "menxia";     // 门下省
        public const string Shangshu = "shangshu"; // 尚书省
        public const string Libu = "libu";         // 吏部
        public const string Hubu = "hubu";         // 户部
        public const string Ribu = "ribu";         // 礼部
        public const string Bingbu = "bingbu";     // 兵部
        public const string Xingbu = "xingbu";     // 刑部
        public const string Gongbu = "gongbu";     // 工部
        public const string ImperialPhysician = "imperial_physician";
        public const string ImperialAstrologer = "imperial_astrologer";
    }

    // 官场按科技分历史层级:原始朝会 → 三公九卿 → 三省六部。
    public static class CourtTier
    {
        public const string Primitive = "primitive";
        public const string SanGongJiuQing = "san_gong_jiu_qing"; // 三公九卿
        public const string SanShengLiuBu = "san_sheng_liu_bu";   // 三省六部
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
        public const string Medical = "medical";
        public const string Syncretist = "syncretist";
        public const string Merchant = "merchant";
        public const string Craftsman = "craftsman";
        public const string Historian = "historian";
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
        public const string Medical = "aw_school_medical";
        public const string Syncretist = "aw_school_syncretist";
        public const string Merchant = "aw_school_merchant";
        public const string Craftsman = "aw_school_craftsman";
        public const string Historian = "aw_school_historian";
    }

    public static class CourtStatusId
    {
        public const string RoyalMedicalCare = "aw_royal_medical_care";
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
