using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    public static class OfficialShiRules
    {
        public const int HistoricalOfficeShiPercent = 20;

        public static bool ShouldGrantOfficialShi(bool hasValidShi)
        {
            return !hasValidShi;
        }

        public static bool ShouldUseHistoricalOfficeShi(int pRoll, string pOfficeId)
        {
            return pRoll >= 0 && pRoll < HistoricalOfficeShiPercent &&
                   !string.IsNullOrEmpty(HistoricalOfficeShi(pOfficeId));
        }

        public static string HistoricalOfficeShi(string pOfficeId)
        {
            // Only offices with attested office- or occupation-derived Chinese surnames belong here.
            switch (pOfficeId ?? "")
            {
                case CourtOfficeId.Chancellor: return "相";
                case CourtOfficeId.Censor: return "史";
                case CourtOfficeId.Marshal: return "司马";
                case CourtOfficeId.Justice: return "司寇";
                case CourtOfficeId.Steward: return "司徒";
                case CourtOfficeId.GranaryOfficer: return "仓";
                case CourtOfficeId.Constable: return "尉";
                case CourtOfficeId.ImperialPhysician: return "医";
                case CourtOfficeId.ImperialAstrologer: return "太史";
                case "general": return "将";
                default: return "";
            }
        }

        public static bool ShouldReuseParentVisibleClan(bool hasValidShi,
            bool parentHasSameShi, bool parentHasVisibleClan)
        {
            return hasValidShi && parentHasSameShi && parentHasVisibleClan;
        }

        public static bool ShouldSyncDescendant(long parentLineageId, long parentShiId,
            long childLineageId, long childShiId)
        {
            if (parentLineageId < 0 || parentShiId < 0) return false;
            if (childLineageId >= 0 && childLineageId != parentLineageId) return false;
            return childShiId < 0 || childShiId == parentShiId;
        }
    }
}
