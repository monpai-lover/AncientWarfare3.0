using System.Reflection;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.core.policy
{
    internal static class MandateMapMarkerService
    {
        private const string IconMandate = "moh_nameplate";
        private const string IconRebel = "ui/Icons/traits/iconrebel";
        private const string IconPseudo = "ui/wars/Mandate_of_Heaven";

        private static readonly MethodInfo ShowSpecialMethod =
            AccessTools.Method(typeof(NameplateText), "showSpecial");

        public static void ApplyNameplate(NameplateText pNameplate, Kingdom pKingdom)
        {
            string icon = GetMarkerIcon(pKingdom);
            if (string.IsNullOrEmpty(icon) || ShowSpecialMethod == null || pNameplate == null) return;
            try { ShowSpecialMethod.Invoke(pNameplate, new object[] { icon }); }
            catch { }
        }

        public static string GetMarkerIcon(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral())
                return "";

            MandateReport report = MandateService.ReadReport();
            if (report.active && pKingdom.id == report.kingdom_id)
                return IconForKind(report.map_marker_kind);

            if (MandateRebelService.IsRebelKingdom(pKingdom))
                return IconRebel;

            pKingdom.data.get(LineageKeys.MANDATE_ORIGIN_TYPE, out string origin, "");
            pKingdom.data.get(LineageKeys.MANDATE_CLAIMANT_KIND, out string claimant, "");
            if (origin == "pseudo_foreign" || claimant == "foreign_pseudo")
                return IconPseudo;

            return "";
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

        private static string IconForKind(string pKind)
        {
            switch (pKind)
            {
                case "rebel_claimant": return IconRebel;
                case "pseudo_foreign": return IconPseudo;
                default: return IconMandate;
            }
        }
    }
}
