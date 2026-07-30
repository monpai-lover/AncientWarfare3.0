namespace AncientWarfare3.core.lineage
{
    public static class WarTypeAssetRules
    {
        public static string ResolveWarNameTemplate(string pWarType, string pTemplate)
        {
            if (IsKnownVanillaWarNameTemplate(pTemplate) || IsKnownAwWarNameTemplate(pTemplate)) return pTemplate;
            return "war_conquest";
        }

        public static string ResolveWarNameLocale(string pLocalizedType)
        {
            if (string.IsNullOrEmpty(pLocalizedType)) return "war_name_conquest";
            return pLocalizedType.StartsWith("war_type_")
                ? "war_name_" + pLocalizedType.Substring("war_type_".Length)
                : pLocalizedType + "_name";
        }

        private static bool IsKnownVanillaWarNameTemplate(string pTemplate)
        {
            switch (pTemplate)
            {
                case "war_conquest":
                case "war_spite":
                case "war_inspire":
                case "war_rebellion":
                case "war_whisper":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsKnownAwWarNameTemplate(string pTemplate)
        {
            switch (pTemplate)
            {
                case "war_reclaim":
                case "war_restoration":
                case "war_jingnan":
                case "war_succession_dispute":
                case "war_coup_restoration":
                    return true;
                default:
                    return false;
            }
        }
    }
}
