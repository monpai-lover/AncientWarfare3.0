using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_MapModeTooltipPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TooltipLibrary), "showKingdom")]
        public static bool ShowKingdom_Prefix(Tooltip pTooltip, string pType, TooltipData pData)
        {
            Kingdom kingdom = pData?.kingdom;
            if (kingdom?.data == null) return true;
            return !TryShowMapModeTooltip(pTooltip, null, kingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TooltipLibrary), "showCity", new[] { typeof(Tooltip), typeof(string), typeof(TooltipData) })]
        public static bool ShowCity_Prefix(Tooltip pTooltip, string pType, TooltipData pData)
        {
            City city = pData?.city;
            Kingdom kingdom = city?.kingdom;
            if (kingdom?.data == null) return true;
            return !TryShowMapModeTooltip(pTooltip, city, kingdom);
        }

        private static bool TryShowMapModeTooltip(Tooltip pTooltip, City city, Kingdom kingdom)
        {
            string selected = GetSelectedMapModePower();
            if (selected == WarCoreMapModeService.POWER_ID ||
                (selected == null && WarCoreMapModeService.IsActive()))
            {
                if (WarCoreMapModeService.IsClaimLayerSelected())
                {
                    ShowMapModeTooltip(pTooltip, kingdom, "aw_claim_mapmode_tooltip",
                        "Claim Map", WarClaimMapModeService.BuildTooltip(kingdom), "#E8C36A");
                    return true;
                }

                ShowMapModeTooltip(pTooltip, kingdom, "aw_core_mapmode_tooltip",
                    "Core Map", WarCoreMapModeService.BuildTooltip(kingdom), "#8FE8A0");
                return true;
            }

            if (selected == WarClaimMapModeService.POWER_ID ||
                (selected == null && WarClaimMapModeService.IsActive()))
            {
                ShowMapModeTooltip(pTooltip, kingdom, "aw_claim_mapmode_tooltip",
                    "Claim Map", WarClaimMapModeService.BuildTooltip(kingdom), "#E8C36A");
                return true;
            }

            if (selected == MandateCoreMapModeService.POWER_ID ||
                (selected == null && MandateCoreMapModeService.IsActive()))
            {
                ShowMapModeTooltip(pTooltip, kingdom, "aw_mandate_core_mapmode_tooltip",
                    "Mandate Core Map", MandateCoreMapModeService.BuildTooltip(kingdom), "#A8DDE8");
                return true;
            }

            if (selected == MandateDynastyMapModeService.POWER_ID ||
                (selected == null && MandateDynastyMapModeService.IsActive()))
            {
                ShowMapModeTooltip(pTooltip, kingdom, "aw_mandate_dynasty_mapmode_tooltip",
                    "Mandate Realm Map", MandateDynastyMapModeService.BuildTooltip(kingdom), "#E8D28A");
                return true;
            }

            if (selected == VassalMapModeService.POWER_ID ||
                (selected == null && VassalMapModeService.IsActive()))
            {
                ShowMapModeTooltip(pTooltip, kingdom, "aw_vassal_mapmode_tooltip",
                    "Vassal Map", VassalMapModeService.BuildTooltip(kingdom), "#E8D28A");
                return true;
            }

            if (selected == TechMapModeService.POWER_ID ||
                (selected == null && TechMapModeService.IsActive()))
            {
                if (TechMapModeService.IsDevelopmentLayerSelected())
                {
                    if (city?.data != null)
                    {
                        ShowCityMapModeTooltip(pTooltip, city, "aw_development_mapmode_tooltip",
                            "Development Map", DevelopmentMapModeService.BuildTooltip(city, kingdom), "#A8D98B");
                        return true;
                    }

                    ShowMapModeTooltip(pTooltip, kingdom, "aw_development_mapmode_tooltip",
                        "Development Map", DevelopmentMapModeService.BuildTooltip(null, kingdom), "#A8D98B");
                    return true;
                }

                if (city?.data != null)
                {
                    ShowCityMapModeTooltip(pTooltip, city, "aw_tech_mapmode_tooltip",
                        "Technology Map", CityTechService.BuildCityTooltip(city), "#D8E889");
                    return true;
                }

                ShowMapModeTooltip(pTooltip, kingdom, "aw_tech_mapmode_tooltip",
                    "Technology Map", TechMapModeService.BuildTooltip(kingdom), "#D8E889");
                return true;
            }

            if (selected == DevelopmentMapModeService.POWER_ID ||
                (selected == null && DevelopmentMapModeService.IsActive()))
            {
                if (city?.data != null)
                {
                    ShowCityMapModeTooltip(pTooltip, city, "aw_development_mapmode_tooltip",
                        "Development Map", DevelopmentMapModeService.BuildTooltip(city, kingdom), "#A8D98B");
                    return true;
                }

                ShowMapModeTooltip(pTooltip, kingdom, "aw_development_mapmode_tooltip",
                    "Development Map", DevelopmentMapModeService.BuildTooltip(null, kingdom), "#A8D98B");
                return true;
            }

            return false;
        }

        private static void ShowMapModeTooltip(Tooltip pTooltip, Kingdom pKingdom, string pTitleKey,
            string pFallbackTitle, string pBody, string pColor)
        {
            string title = AW_L10n.Text(pTitleKey, pFallbackTitle);
            pTooltip.setTitle(title, "", pColor);
            try { pTooltip.setSpeciesIcon(pKingdom.getSpeciesIcon()); } catch { }
            HideOriginalStatsRow(pTooltip);
            LoadBanners(pTooltip, pKingdom);

            pTooltip.clearTextRows();
            if (pTooltip.stats_container != null) pTooltip.stats_container.SetActive(false);

            string body = StripDuplicateHeader(pBody, title);
            string text = string.IsNullOrEmpty(body) ? pKingdom.name : pKingdom.name + "\n" + body;
            pTooltip.setDescription(text);
        }

        private static void ShowCityMapModeTooltip(Tooltip pTooltip, City pCity, string pTitleKey,
            string pFallbackTitle, string pBody, string pColor)
        {
            Kingdom kingdom = pCity?.kingdom;
            string title = AW_L10n.Text(pTitleKey, pFallbackTitle);
            pTooltip.setTitle(title, "", pColor);
            try
            {
                if (kingdom?.data != null) pTooltip.setSpeciesIcon(kingdom.getSpeciesIcon());
            }
            catch { }
            HideOriginalStatsRow(pTooltip);
            if (kingdom?.data != null) LoadBanners(pTooltip, kingdom);

            pTooltip.clearTextRows();
            if (pTooltip.stats_container != null) pTooltip.stats_container.SetActive(false);

            string body = StripDuplicateHeader(pBody, title);
            string cityName = pCity?.data?.name ?? "";
            string kingdomName = kingdom?.name ?? "";
            string header = string.IsNullOrEmpty(kingdomName) ? cityName : cityName + " - " + kingdomName;
            string text = string.IsNullOrEmpty(body) ? header : header + "\n" + body;
            pTooltip.setDescription(text);
        }

        private static bool IsSelected(string pPowerId)
        {
            try { return World.world != null && World.world.isSelectedPower(pPowerId); }
            catch { return false; }
        }

        private static string GetSelectedMapModePower()
        {
            string active = AWMapModeCoordinator.ActivePowerId();
            return string.IsNullOrEmpty(active) ? null : active;
        }

        private static string StripDuplicateHeader(string pBody, string pTitle)
        {
            string body = (pBody ?? "").Trim();
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(pTitle)) return body;
            if (body == pTitle) return "";
            string prefix = pTitle + "\n";
            return body.StartsWith(prefix) ? body.Substring(prefix.Length).TrimStart() : body;
        }

        private static void HideOriginalStatsRow(Tooltip pTooltip)
        {
            try
            {
                var stats = pTooltip.transform.FindRecursive("Stats");
                if (stats != null) stats.gameObject.SetActive(false);
            }
            catch { }
        }

        private static void LoadBanners(Tooltip pTooltip, Kingdom pKingdom)
        {
            try
            {
                KingdomBanner[] banners = pTooltip.transform.FindAllRecursive<KingdomBanner>();
                for (int i = 0; i < banners.Length; i++)
                    banners[i].load(pKingdom);
            }
            catch { }
        }
    }
}
