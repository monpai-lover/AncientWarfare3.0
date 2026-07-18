namespace AncientWarfare3.content
{
    public static class TraitIconUsageRules
    {
        public static string IconForTrait(string pTraitId)
        {
            switch (pTraitId)
            {
                case "figure": return "ui/Icons/traits/iconhistorical";
                case "aw_general": return "ui/Icons/traits/icondajiang";
                case "aw_army_commander": return "ui/Icons/traits/iconjiang";
                case "formerking": return "ui/Icons/traits/iconformerking";
                case "zhuhou": return "ui/Icons/traits/iconzhuhou";
                case "fanwang": return "ui/Icons/traits/iconzhuhou";
                default: return "";
            }
        }
    }
}
