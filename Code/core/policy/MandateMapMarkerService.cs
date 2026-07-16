using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class MandateMapMarkerService
    {
        public static string GetMarkerIcon(Kingdom pKingdom)
        {
            bool valid = pKingdom?.data != null && !pKingdom.isRekt() &&
                         pKingdom.isCiv() && !pKingdom.isNeutral();
            if (!valid) return "";

            bool currentMandate =
                MandateService.TryGetRuntimeMarkerKind(pKingdom.id, out string markerKind);
            bool rebel = MandateRebelService.IsRebelKingdom(pKingdom);
            pKingdom.data.get(LineageKeys.MANDATE_ORIGIN_TYPE, out string origin, "");
            pKingdom.data.get(LineageKeys.MANDATE_CLAIMANT_KIND, out string claimant, "");
            return MandateMapMarkerRules.ResolveIcon(valid, currentMandate, markerKind,
                rebel, origin, claimant);
        }

        public static string MarkerLabel(string pKind)
        {
            switch (pKind)
            {
                case "rebel_claimant": return "\u4E49\u519B\u5929\u547D";
                case "pseudo_foreign": return "\u4F2A\u671D\u5929\u547D";
                default: return "\u771F\u5929\u547D";
            }
        }
    }
}
