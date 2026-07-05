namespace AncientWarfare3.core.policy
{
    internal static class AWMapModeMetaTypes
    {
        public const MetaType Tech = (MetaType)210;
        public const MetaType Vassal = (MetaType)211;
        public const MetaType WarCore = (MetaType)212;
        public const MetaType WarClaim = (MetaType)213;
        public const MetaType MandateDynasty = (MetaType)214;
        public const MetaType MandateCore = (MetaType)215;
        public const MetaType Development = (MetaType)216;

        public const string TechId = "aw3_tech_map";
        public const string VassalId = "aw3_vassal_map";
        public const string WarCoreId = "aw3_war_core_map";
        public const string WarClaimId = "aw3_war_claim_map";
        public const string MandateDynastyId = "aw3_mandate_dynasty_map";
        public const string MandateCoreId = "aw3_mandate_core_map";
        public const string DevelopmentId = "aw3_development_map";

        public static bool TryAsString(MetaType pType, out string pResult)
        {
            switch (pType)
            {
                case Tech:
                    pResult = TechId;
                    return true;
                case Vassal:
                    pResult = VassalId;
                    return true;
                case WarCore:
                    pResult = WarCoreId;
                    return true;
                case WarClaim:
                    pResult = WarClaimId;
                    return true;
                case MandateDynasty:
                    pResult = MandateDynastyId;
                    return true;
                case MandateCore:
                    pResult = MandateCoreId;
                    return true;
                case Development:
                    pResult = DevelopmentId;
                    return true;
                default:
                    pResult = "";
                    return false;
            }
        }

        public static bool IsAwMetaId(string pId)
        {
            switch (pId)
            {
                case TechId:
                case VassalId:
                case WarCoreId:
                case WarClaimId:
                case MandateDynastyId:
                case MandateCoreId:
                case DevelopmentId:
                    return true;
                default:
                    return false;
            }
        }
    }
}
