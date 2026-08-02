namespace AncientWarfare3.core.policy
{
    public static class AWMapModeNameplateRules
    {
        private static readonly MetaType[] RequiredNameplateMetaTypes =
        {
            AWMapModeMetaTypes.Tech,
            AWMapModeMetaTypes.Vassal,
            AWMapModeMetaTypes.WarCore,
            AWMapModeMetaTypes.WarClaim,
            AWMapModeMetaTypes.MandateDynasty,
            AWMapModeMetaTypes.MandateCore,
            AWMapModeMetaTypes.Development,
            AWMapModeMetaTypes.School,
            AWMapModeMetaTypes.Feudatory
        };

        private static readonly string[] DefaultZoneOptionLocaleIds =
        {
            "ui_zone_mode_kingdoms"
        };

        private static readonly string[] TechZoneOptionLocaleIds =
        {
            "aw_tech_mapmode_option_tech",
            "aw_tech_mapmode_option_development"
        };

        private static readonly string[] WarZoneOptionLocaleIds =
        {
            "aw_war_mapmode_option_core",
            "aw_war_mapmode_option_claim"
        };

        private static readonly string[] HierarchicalVassalZoneOptionLocaleIds =
        {
            "aw3_hierarchical_vassal_map_option_country",
            "aw3_hierarchical_vassal_map_option_city"
        };

        internal static MetaType[] GetRequiredNameplateMetaTypes()
        {
            return RequiredNameplateMetaTypes;
        }

        public static int[] GetRequiredNameplateMetaTypeIds()
        {
            var result = new int[RequiredNameplateMetaTypes.Length];
            for (int i = 0; i < RequiredNameplateMetaTypes.Length; i++)
                result[i] = (int)RequiredNameplateMetaTypes[i];
            return result;
        }

        public static string[] GetDefaultZoneOptionLocaleIds()
        {
            return DefaultZoneOptionLocaleIds;
        }

        public static string[] GetTechZoneOptionLocaleIds()
        {
            return TechZoneOptionLocaleIds;
        }

        public static string[] GetWarZoneOptionLocaleIds()
        {
            return WarZoneOptionLocaleIds;
        }

        public static string[] GetHierarchicalVassalZoneOptionLocaleIds()
        {
            return HierarchicalVassalZoneOptionLocaleIds;
        }
    }
}
