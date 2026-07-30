using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

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
                case "rebel_claimant":
                    return AW_L10n.Text("aw_mandate_marker_rebel", "Rebel Mandate");
                case "pseudo_foreign":
                    return AW_L10n.Text("aw_mandate_marker_pseudo", "Pseudo-dynastic Mandate");
                default:
                    return AW_L10n.Text("aw_mandate_marker_true", "True Mandate");
            }
        }
    }
}
