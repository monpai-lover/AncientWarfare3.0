namespace AncientWarfare3.core.lineage
{
    public static class HeirTitleSelectionRules
    {
        public const string ShiziKey = "aw_heir_shizi";
        public const string TaiziKey = "aw_heir_taizi";
        public const string LiuhouKey = "aw_heir_liuhou";
        public const string SijunKey = "aw_heir_sijun";
        public const string MilitaryAcclaimMode = "military_acclaim";
        public const string CivilAcclaimMode = "civil_acclaim";

        public static string TitleKey(bool isEmpireOrMandate,
            string successionMode)
        {
            if (successionMode == MilitaryAcclaimMode) return LiuhouKey;
            if (successionMode == CivilAcclaimMode) return SijunKey;
            return isEmpireOrMandate ? TaiziKey : ShiziKey;
        }

        public static string DefaultTitleText(bool isEmpireOrMandate,
            string successionMode)
        {
            if (successionMode == MilitaryAcclaimMode) return "\u7559\u540e";
            if (successionMode == CivilAcclaimMode) return "\u55e3\u541b";
            return isEmpireOrMandate ? "\u592a\u5b50" : "\u4e16\u5b50";
        }

        public static string RoleSnapshot(bool isEmpireOrMandate)
        {
            return isEmpireOrMandate ? "heir_taizi" : "heir_shizi";
        }
    }
}
