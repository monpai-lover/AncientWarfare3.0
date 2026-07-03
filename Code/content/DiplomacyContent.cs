namespace AncientWarfare3.content
{
    internal static class DiplomacyContent
    {
        public static void Init()
        {
            AddWarType("reclaim", "war_reclaim", "war_type_reclaim", "ui/wars/war_reclaim", true);
            AddWarType("vassal_war", "war_conquest", "war_type_vassal_war", "ui/wars/war_vassal", false);
            AddWarType("independence_war", "war_conquest", "war_type_independence_war", "ui/wars/war_independent", false);
        }

        private static void AddWarType(string pId, string pNameTemplate, string pLocalizedType, string pIcon,
            bool pAllianceJoin)
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
                alliance_join = pAllianceJoin
            };
            AssetManager.war_types_library.add(asset);
        }
    }
}
