using System.Reflection;
using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.core.policy
{
    internal static class MandateMapMarkerService
    {
        private const string IconMandate = "moh_nameplate";
        private const string IconRebel = "ui/Icons/traits/iconrebel";
        private const string IconPseudo = "ui/wars/Mandate_of_Heaven";

        private static readonly FieldInfo SpeciesIconField =
            AccessTools.Field(typeof(NameplateText), "_icon_species");

        private static readonly FieldInfo ShowSpeciesIconField =
            AccessTools.Field(typeof(NameplateText), "_show_icon_species");

        private static readonly FieldInfo SpecialIconField =
            AccessTools.Field(typeof(NameplateText), "_icon_special");

        private static readonly FieldInfo ShowSpecialIconField =
            AccessTools.Field(typeof(NameplateText), "_show_icon_special");

        public static void ApplyNameplate(NameplateText pNameplate, Kingdom pKingdom)
        {
            if (pNameplate == null) return;
            string icon = GetMarkerIcon(pKingdom);
            if (string.IsNullOrEmpty(icon))
            {
                ClearSpecialIcon(pNameplate, icon);
                return;
            }
            ReplaceSpeciesIcon(pNameplate, icon);
            ClearSpecialIcon(pNameplate, icon);
        }

        private static void ReplaceSpeciesIcon(NameplateText pNameplate, string pIconPath)
        {
            if (SpeciesIconField == null || ShowSpeciesIconField == null) return;
            try
            {
                if (pNameplate.is_mini) return;
                Image icon = SpeciesIconField.GetValue(pNameplate) as Image;
                if (!MandateMapMarkerRules.ShouldReplaceSpeciesIcon(pIconPath, icon != null)) return;
                Sprite sprite = SpriteTextureLoader.getSprite(pIconPath);
                if (sprite == null) return;
                ShowSpeciesIconField.SetValue(pNameplate, true);
                icon.sprite = sprite;
            }
            catch { }
        }

        private static void ClearSpecialIcon(NameplateText pNameplate, string pIconPath)
        {
            if (SpecialIconField == null && ShowSpecialIconField == null) return;
            try
            {
                Image icon = SpecialIconField?.GetValue(pNameplate) as Image;
                bool hasSpecialTarget = icon != null || ShowSpecialIconField != null;
                if (!MandateMapMarkerRules.ShouldClearSpecialIcon(pIconPath, hasSpecialTarget)) return;
                ShowSpecialIconField?.SetValue(pNameplate, false);
                if (icon != null) icon.sprite = null;
            }
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
