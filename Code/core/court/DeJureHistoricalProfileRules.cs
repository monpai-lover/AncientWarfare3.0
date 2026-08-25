using System;

namespace AncientWarfare3.core.court
{
    public sealed class DeJureHistoricalMigrationResult
    {
        public string RegionName { get; }
        public string HistoricalStateId { get; }
        public string HistoricalCommanderyId { get; }
        public long SeatCityId { get; }
        public bool SeatLocked { get; }
        public string RegionNameSource { get; }

        public DeJureHistoricalMigrationResult(string pRegionName,
            string pStateId, string pCommanderyId, long pSeatCityId,
            bool pSeatLocked, string pRegionNameSource)
        {
            RegionName = pRegionName ?? string.Empty;
            HistoricalStateId = pStateId ?? string.Empty;
            HistoricalCommanderyId = pCommanderyId ?? string.Empty;
            SeatCityId = pSeatCityId;
            SeatLocked = pSeatLocked;
            RegionNameSource = pRegionNameSource ?? string.Empty;
        }
    }

    public static class DeJureHistoricalProfileRules
    {
        public const string HistoricalDefault = "HistoricalDefault";
        public const string LegacyPreserved = "LegacyPreserved";
        public const string ManualSeatRename = "ManualSeatRename";

        public static DeJureHistoricalMigrationResult Migrate(
            string pExistingRegionName, string pExistingStateId,
            long pExistingSeatCityId, string pCommanderyId,
            string pHistoricalStateName, long pFallbackSeatCityId)
        {
            bool hasName = !string.IsNullOrWhiteSpace(pExistingRegionName);
            bool hasSeat = pExistingSeatCityId >= 0L;
            string name = hasName ? pExistingRegionName.Trim()
                : (pHistoricalStateName ?? string.Empty).Trim();
            string source = hasName ? LegacyPreserved : HistoricalDefault;
            long seat = hasSeat ? pExistingSeatCityId : pFallbackSeatCityId;
            return new DeJureHistoricalMigrationResult(name,
                (pExistingStateId ?? string.Empty).Trim(),
                (pCommanderyId ?? string.Empty).Trim(), seat, seat >= 0L,
                source);
        }

        public static bool ShouldApplyTrackedSeatRename(bool pIsSeat,
            bool pTrackedRename, bool pSeatLocked)
        {
            return pIsSeat && pTrackedRename && pSeatLocked;
        }

        public static bool ShouldDeriveRegionNameOnRead()
        {
            return false;
        }
    }
}
