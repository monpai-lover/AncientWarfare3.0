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
            int replenishmentShortage = 0, string manpowerText = "")
        {
            string shortage = replenishmentShortage > 0
                ? "（待补" + replenishmentShortage + "）"
                : string.Empty;
            string result = (nativeArmyName ?? string.Empty).Trim() + " #" +
                            Math.Max(0, memberCount) + shortage +
                            "\n统帅: " +
                            (captainName ?? string.Empty).Trim() +
                            "\n任务: " +
                            (operationText ?? string.Empty).Trim();
            string manpower = (manpowerText ?? string.Empty).Trim();
            return manpower.Length == 0 ? result : result + "\n" + manpower;
        }

        public static string ComposeManpowerText(int shortage,
            int reserveSupply)
        {
            return Math.Max(0, shortage) + " / " +
                   Math.Max(0, reserveSupply);
        }

        public static int CombineReserveSupply(int cityReserve,
            int armyReserve)
        {
            long total = (long)Math.Max(0, cityReserve) +
                         Math.Max(0, armyReserve);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        public static string ComposeManpowerText(string shortageLabel,
            string reserveSupplyLabel, int shortage, int reserveSupply)
        {
            return (shortageLabel ?? string.Empty).Trim() + ": " +
                   Math.Max(0, shortage) + " / " +
                   (reserveSupplyLabel ?? string.Empty).Trim() + ": " +
                   Math.Max(0, reserveSupply);
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

        public static ArmyRtsState ResolvePendingState(bool hasProjection,
            ArmyRtsState projectionState, int memberCount,
            int minimumOperationalForce, bool hasCombatActivity)
        {
            if (hasProjection) return projectionState;
            if (hasCombatActivity) return ArmyRtsState.Assault;
            return ResolvePendingState(hasProjection, projectionState,
                memberCount, minimumOperationalForce);
        }

        public static ArmyRtsState ResolvePendingState(bool hasProjection,
            ArmyRtsState projectionState, int memberCount,
            int minimumOperationalForce, bool hasCombatActivity,
            bool hasReplenishmentOperation)
        {
            if (hasProjection) return projectionState;
            if (hasCombatActivity) return ArmyRtsState.Assault;
            return hasReplenishmentOperation
                ? ResolvePendingState(hasProjection, projectionState,
                    memberCount, minimumOperationalForce)
                : ArmyRtsState.Idle;
        }

        public static string PendingOperationLocalizationKey(
            ArmyRtsState pState)
        {
            return pState == ArmyRtsState.Replenish
                ? "aw_army_rts_state_replenish"
                : "aw_army_rts_state_awaiting_orders";
        }

        public static string PendingOperationLocalizationKey(
            ArmyRtsState pState, bool royalGuardArmy)
        {
            return royalGuardArmy
                ? "task_unit_aw_guard_protect_king"
                : PendingOperationLocalizationKey(pState);
        }

        public static string PendingOperationLocalizationKey(
            ArmyRtsState pState, bool royalGuardArmy,
            bool activeDeployment, bool deploymentArrived)
        {
            if (royalGuardArmy)
                return "task_unit_aw_guard_protect_king";
            if (activeDeployment)
                return deploymentArrived
                    ? "aw_war_deployment_ready"
                    : "task_unit_aw_war_deployment";
            return PendingOperationLocalizationKey(pState);
        }

        public static int ResolvePendingShortage(bool hasMission,
            int missionTargetStrength, bool activeDeployment,
            int standingTargetStrength, int memberCount,
            int minimumOperationalForce)
        {
            _ = activeDeployment;
            int target = Math.Max(0, standingTargetStrength);
            if (hasMission && missionTargetStrength > 0)
                target = Math.Max(target, missionTargetStrength);
            if (target <= 0) target = minimumOperationalForce;
            return Math.Max(0, Math.Max(0, target) -
                               Math.Max(0, memberCount));
        }

        public static bool ShouldDisplayReserveManpower(
            bool royalGuardArmy)
        {
            return !royalGuardArmy;
        }

        public static bool ShouldDisplayReserveManpower(
            bool royalGuardArmy, bool hasMission,
            bool hasReplenishmentOperation)
        {
            return !royalGuardArmy &&
                   (hasMission || hasReplenishmentOperation);
        }
    }
}
