namespace AncientWarfare3.core.lineage
{
    public static class WarIconPathRules
    {
        public static string ResolveWarIconPath(string pWarType, string pPath)
        {
            if (pWarType == "aw_normal_war" && pPath == "ui/wars/war_conquest") return "wars/war_conquest";
            if (pWarType == "general_rebellion_war" && pPath == "ui/wars/war_rebellion") return "wars/war_rebellion";
            return pPath ?? "";
        }

        public static string ResolveTargetIconPath(string pKind)
        {
            switch (pKind ?? "")
            {
                case "take_mandate": return "ui/Icons/traits/iconTianming";
                case "mandate_conquest": return "wars/war_conquest";
                case "take_core_city": return "ui/plots/plot_reclaim";
                case "press_claim_city": return "ui/plots/plot_reclaim";
                case "restore_kingdom": return "ui/plots/plot_usurpation";
                case "force_vassal": return "ui/plots/plot_vassal_war";
                case "independence": return "ui/plots/plot_Independence_War";
                case "no_cb_punitive": return "ui/wars/war_reclaim";
                case "fabricate_core": return "ui/icons/iconKnowledge";
                case "fabricate_weak_claim": return "ui/icons/iconKnowledge";
                case "fabricate_strong_claim": return "ui/icons/iconKnowledge";
                default: return "ui/icons/iconDiplomacy";
            }
        }
    }
}
