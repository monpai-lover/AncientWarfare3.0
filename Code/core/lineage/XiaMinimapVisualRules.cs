namespace AncientWarfare3.core.lineage
{
    public static class XiaMinimapVisualRules
    {
        public const string DefaultHeirIconPath =
            "civ/icons/minimap_heir";
        public const string XiaHeirIconPath =
            "civ/icons/minimap_heir_xia";
        public const string XiaKingIconPath =
            "civ/icons/minimap_king_xia";

        public static string ResolveHeirIconPath(bool cultureIntegrated)
        {
            return cultureIntegrated ? XiaHeirIconPath : DefaultHeirIconPath;
        }

        public static string ResolveKingIconPath(bool cultureIntegrated,
            bool hasAttackTarget, bool hasPlot, bool kingdomHasEnemies)
        {
            if (cultureIntegrated) return XiaKingIconPath;
            if (hasAttackTarget) return "civ/icons/minimap_king_angry";
            if (hasPlot) return "civ/icons/minimap_king_surprised";
            return kingdomHasEnemies
                ? "civ/icons/minimap_king_normal"
                : "civ/icons/minimap_king_happy";
        }
    }
}
