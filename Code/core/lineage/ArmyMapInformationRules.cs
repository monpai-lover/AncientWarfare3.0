using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class ArmyMapInformationRules
    {
        public const int MaximumVisibleArmies = 24;
        public const int MaximumEntriesRefreshedPerFrame = 8;
        public const int MaximumMinimapMarkers = 12;
        private const int MinimapMarkerCellSpan = 12;

        public static bool ShouldDisplay(ArmyRtsMode pMode,
            bool pMapInformationEnabled, long pSelectedKingdomId)
        {
            return pMode == ArmyRtsMode.On && pMapInformationEnabled &&
                   pSelectedKingdomId >= 0L;
        }

        public static bool ShouldDisplayEntry(bool mapInformationEnabled,
            bool armyAlive, bool captainAlive, string nativeArmyName)
        {
            return mapInformationEnabled && armyAlive && captainAlive &&
                   !string.IsNullOrWhiteSpace(nativeArmyName);
        }

        public static bool ShouldDisplayMinimapMarker(bool mapInformationEnabled,
            bool armyAlive, bool captainAlive)
        {
            return mapInformationEnabled && armyAlive && captainAlive;
        }

        public static bool TryReserveMinimapCell(HashSet<long> reservedCells,
            int tileX, int tileY)
        {
            if (reservedCells == null || tileX < 0 || tileY < 0) return false;
            int cellX = tileX / MinimapMarkerCellSpan;
            int cellY = tileY / MinimapMarkerCellSpan;
            long cellKey = ((long)cellX << 32) | (uint)cellY;
            return reservedCells.Add(cellKey);
        }

        public static string ComposeText(string nativeArmyName, int memberCount,
            string captainName, string operationText,
            int replenishmentShortage = 0)
        {
            string shortage = replenishmentShortage > 0
                ? "（待补" + replenishmentShortage + "）"
                : string.Empty;
            return (nativeArmyName ?? string.Empty).Trim() + " #" +
                   Math.Max(0, memberCount) + shortage + "\n统帅: " +
                   (captainName ?? string.Empty).Trim() + "\n任务: " +
                   (operationText ?? string.Empty).Trim();
        }

        public static int ResolveReplenishmentShortage(bool replenishing,
            int memberCount, int targetStrength)
        {
            return replenishing
                ? Math.Max(0, Math.Max(0, targetStrength) -
                              Math.Max(0, memberCount))
                : 0;
        }

        public static ArmyRtsState ResolvePendingState(bool hasProjection,
            ArmyRtsState projectionState, int memberCount,
            int minimumOperationalForce)
        {
            if (hasProjection) return projectionState;
            return Math.Max(0, memberCount) <
                   Math.Max(1, minimumOperationalForce)
                ? ArmyRtsState.Replenish
                : ArmyRtsState.Idle;
        }

        public static string PendingOperationLocalizationKey(
            ArmyRtsState pState)
        {
            return pState == ArmyRtsState.Replenish
                ? "aw_army_rts_state_replenish"
                : "aw_army_rts_state_awaiting_orders";
        }
    }
}
