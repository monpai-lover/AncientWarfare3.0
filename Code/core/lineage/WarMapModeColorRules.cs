namespace AncientWarfare3.core.lineage
{
    public static class WarMapModeColorRules
    {
        public static string CoreColorKey(string pStatus)
        {
            switch (pStatus ?? "")
            {
                case "core":
                    return "core";
                case "pending_core":
                    return "pending_core";
                case "owned_non_core":
                    return "owned_non_core";
                default:
                    return "";
            }
        }

        public static string ClaimColorKey(string pStatus)
        {
            switch (pStatus ?? "")
            {
                case "strong_claim":
                    return "strong_claim";
                case "weak_claim":
                    return "weak_claim";
                case "pending_claim":
                    return "pending_claim";
                default:
                    return "";
            }
        }

        public static string CoreHexForStatus(string pStatus)
        {
            switch (pStatus ?? "")
            {
                case "core": return "#226B3A";
                case "pending_core": return "#D7A928";
                case "owned_non_core": return "#B3124B";
                default: return "#242424";
            }
        }

        public static string ClaimHexForStatus(string pStatus)
        {
            switch (pStatus ?? "")
            {
                case "strong_claim": return "#226B3A";
                case "weak_claim": return "#D7A928";
                case "pending_claim": return "#E08226";
                default: return "#242424";
            }
        }
    }
}
