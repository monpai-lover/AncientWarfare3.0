namespace AncientWarfare3.content
{
    internal static class DiplomacyContent
    {
        public static void Init()
        {
            AddWarType("aw_normal_war", "war_conquest", "war_type_aw_normal_war", "ui/wars/war_conquest", true);
            AddWarType("reclaim", "war_reclaim", "war_type_reclaim", "ui/wars/war_reclaim", true);
            AddWarType("restoration_war", "war_reclaim", "war_type_restoration_war", "ui/wars/war_reclaim", true);
            AddWarType("vassal_war", "war_conquest", "war_type_vassal_war", "ui/wars/war_vassal", false);
            AddWarType("independence_war", "war_conquest", "war_type_independence_war", "ui/wars/war_independent", false);
            AddWarType("tianming", "war_conquest", "war_type_tianming", "ui/Icons/traits/iconTianming", true);
            AddWarType("tianmingrebel", "war_conquest", "war_type_tianmingrebel", "ui/wars/war_tianmingrebel", false,
                pRebellion: true);
            AddWarType("general_rebellion_war", "war_conquest", "war_type_general_rebellion_war",
                "ui/wars/war_rebellion", false, pRebellion: true);
            AddWarType("fief_independence_war", "war_conquest", "war_type_fief_independence_war",
                "ui/wars/war_independent", false, pRebellion: true);
        }

        private static void AddWarType(string pId, string pNameTemplate, string pLocalizedType, string pIcon,
            bool pAllianceJoin, bool pRebellion = false)
        {
            try
            {
                if (AssetManager.war_types_library.get(pId) != null) return;
            }
            catch { }

            var asset = new WarTypeAsset
            {
                id = pId,
                name_template = pNameTemplate,
                localized_type = pLocalizedType,
                path_icon = pIcon,
                kingdom_for_name_attacker = true,
                forced_war = false,
                total_war = false,
                alliance_join = pAllianceJoin,
                rebellion = pRebellion
            };
            AssetManager.war_types_library.add(asset);
        }
    }
}
