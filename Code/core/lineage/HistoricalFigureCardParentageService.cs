using System;

namespace AncientWarfare3.core.lineage
{
    public enum HistoricalFigureCardParentSlot
    {
        Father,
        Mother
    }

    public static class HistoricalFigureCardParentageService
    {
        public static bool ShouldCreateSyntheticParent(string pDisplayName)
        {
            return !string.IsNullOrWhiteSpace(pDisplayName) &&
                   !IsUnknown(pDisplayName);
        }

        public static string SyntheticParentId(string pDeploymentId,
            HistoricalFigureCardParentSlot pSlot)
        {
            if (string.IsNullOrWhiteSpace(pDeploymentId)) return "";
            return pDeploymentId.Trim() + ":" +
                (pSlot == HistoricalFigureCardParentSlot.Father ? "father" : "mother");
        }

        public static string DisplayName(string pDisplayName)
        {
            return ShouldCreateSyntheticParent(pDisplayName)
                ? pDisplayName.Trim()
                : "史料不详";
        }

        private static bool IsUnknown(string pDisplayName)
        {
            string value = pDisplayName.Trim();
            return value == "不详" || value == "不詳" || value == "未知" ||
                   value == "史料不详" || value == "史料不詳";
        }
    }
}
