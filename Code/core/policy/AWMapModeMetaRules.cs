namespace AncientWarfare3.core.policy
{
    public static class AWMapModeMetaRules
    {
        public static int ResolveRuntimeMetaTypeId()
        {
            return ResolveRuntimeMetaTypeId("");
        }

        public static int ResolveRuntimeMetaTypeId(string pPowerId)
        {
            return (int)ResolveRuntimeMetaType(pPowerId);
        }

        public static MetaType ResolveRuntimeMetaType()
        {
            return ResolveRuntimeMetaType("");
        }

        public static MetaType ResolveRuntimeMetaType(string pPowerId)
        {
            switch (pPowerId ?? "")
            {
                case TechMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.Tech;
                case VassalMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.Vassal;
                case WarCoreMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.WarCore;
                case WarClaimMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.WarClaim;
                case MandateDynastyMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.MandateDynasty;
                case MandateCoreMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.MandateCore;
                case DevelopmentMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.Development;
                case SchoolMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.School;
                case FeudatoryMapModeService.POWER_ID:
                    return AWMapModeMetaTypes.Feudatory;
                default:
                    return MetaType.Kingdom;
            }
        }

        public static bool IsRuntimeMeta(MetaType pActual, MetaType pExpected)
        {
            if (pActual == pExpected) return true;
            return ShouldRenderWithVanillaKingdomAsset() && pActual == MetaType.Kingdom;
        }

        public static string ResolveOptionId(string pPowerId)
        {
            return string.IsNullOrEmpty(pPowerId) ? "" : "map_" + pPowerId;
        }

        public static string ResolveAssetOptionId(string pPowerId)
        {
            switch (pPowerId ?? "")
            {
                case DevelopmentMapModeService.POWER_ID:
                    return ResolveOptionId(TechMapModeService.POWER_ID);
                case WarClaimMapModeService.POWER_ID:
                    return ResolveOptionId(WarCoreMapModeService.POWER_ID);
                default:
                    return ResolveOptionId(pPowerId);
            }
        }

        public static string ResolvePowerOptionZoneId(string pPowerId)
        {
            return pPowerId ?? "";
        }

        public static bool ShouldRenderWithVanillaKingdomAsset()
        {
            return false;
        }

        public static bool ShouldUseMainZoneForColorContext()
        {
            return false;
        }

        public static bool ShouldOverrideKingdomGetColor()
        {
            return false;
        }

        public static bool ShouldUseCityTooltipForPowerId(string pPowerId)
        {
            switch (pPowerId ?? "")
            {
                case TechMapModeService.POWER_ID:
                case DevelopmentMapModeService.POWER_ID:
                case WarCoreMapModeService.POWER_ID:
                case WarClaimMapModeService.POWER_ID:
                case MandateCoreMapModeService.POWER_ID:
                case SchoolMapModeService.POWER_ID:
                case FeudatoryMapModeService.POWER_ID:
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldUseCityTooltipForMapMode(MetaType pMapMode)
        {
            return pMapMode == AWMapModeMetaTypes.Tech ||
                   pMapMode == AWMapModeMetaTypes.Development ||
                   pMapMode == AWMapModeMetaTypes.WarCore ||
                   pMapMode == AWMapModeMetaTypes.WarClaim ||
                   pMapMode == AWMapModeMetaTypes.MandateCore ||
                   pMapMode == AWMapModeMetaTypes.School ||
                   pMapMode == AWMapModeMetaTypes.Feudatory;
        }

        public static string BuildFocusedCityStatusCacheKey(long pFocusId, long pCityId)
        {
            if (pFocusId < 0 || pCityId < 0) return "";
            return pFocusId + ":" + pCityId;
        }

        public static string BuildCityStatusCacheKey(long pCityId)
        {
            return pCityId < 0 ? "" : pCityId.ToString();
        }

        public static string NormalizeMapColorHex(string pHex)
        {
            string hex = (pHex ?? "").Trim();
            return string.IsNullOrEmpty(hex) ? "#242424" : hex.ToUpperInvariant();
        }
    }
}
