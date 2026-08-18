using System;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolAcademyRepairTicket
    {
        public long InstitutionId { get; set; }
        public long CityId { get; set; }
        public long BuildingId { get; set; }
        public int TileX { get; set; }
        public int TileY { get; set; }
        public HistoricalSchoolAcademyPhysicalState State { get; set; }
        public long OwnerKingdomId { get; set; }
        public string OperationKey { get; set; }
    }

    public enum HistoricalSchoolAcademyPhysicalState
    {
        Active,
        RepairPending,
        Rebuilding,
        Inactive
    }

    public enum HistoricalSchoolAcademyPlacementChoice
    {
        None,
        OriginalTile,
        Fallback
    }

    public enum HistoricalSchoolAcademyRepairDisposition
    {
        Repair,
        RebindOwner,
        Cancel
    }

    public static class HistoricalSchoolAcademyRepairRules
    {
        public const int MaximumRepairsPerYearSlice = 2;

        public static bool IsLiveAcademy(bool buildingExists, bool isAcademy,
            bool isAlive, bool isOnRemove, bool isRemoved, bool isRuin,
            bool isUsable, bool isAbandoned)
        {
            return buildingExists && isAcademy && isAlive && !isOnRemove &&
                   !isRemoved && !isRuin && isUsable && !isAbandoned;
        }

        public static bool ShouldCaptureDestruction(bool isAcademy,
            long cityId, long buildingId, int tileX, int tileY)
        {
            return isAcademy && cityId >= 0 && buildingId >= 0 &&
                   tileX >= 0 && tileY >= 0;
        }

        public static bool ShouldClearConstructionBinding(long destroyedBuildingId,
            long constructionBuildingId)
        {
            return destroyedBuildingId >= 0 &&
                   destroyedBuildingId == constructionBuildingId;
        }

        public static bool ShouldRestoreMissingTicket(
            HistoricalSchoolAcademyPhysicalState state, bool ticketExists)
        {
            return !ticketExists &&
                   (state == HistoricalSchoolAcademyPhysicalState.RepairPending ||
                    state == HistoricalSchoolAcademyPhysicalState.Rebuilding);
        }

        public static string OperationKey(long institutionId, long cityId)
        {
            return "school_academy_repair:" + institutionId + ":" + cityId;
        }

        public static HistoricalSchoolAcademyPlacementChoice ChoosePlacement(
            bool originalTileValid, bool fallbackAvailable)
        {
            if (originalTileValid)
                return HistoricalSchoolAcademyPlacementChoice.OriginalTile;
            return fallbackAvailable
                ? HistoricalSchoolAcademyPlacementChoice.Fallback
                : HistoricalSchoolAcademyPlacementChoice.None;
        }

        public static int RepairBudget(int pendingCount)
        {
            return Math.Min(MaximumRepairsPerYearSlice, Math.Max(0, pendingCount));
        }

        public static bool CanStartConstruction(bool constructionSlotOccupied)
        {
            return !constructionSlotOccupied;
        }

        public static HistoricalSchoolAcademyRepairDisposition ResolveDisposition(
            bool cityExists, bool cityUsable, bool ownerChanged)
        {
            if (!cityExists || !cityUsable)
                return HistoricalSchoolAcademyRepairDisposition.Cancel;
            return ownerChanged
                ? HistoricalSchoolAcademyRepairDisposition.RebindOwner
                : HistoricalSchoolAcademyRepairDisposition.Repair;
        }

        public static string ToStorage(HistoricalSchoolAcademyPhysicalState state)
        {
            switch (state)
            {
                case HistoricalSchoolAcademyPhysicalState.RepairPending:
                    return "repair_pending";
                case HistoricalSchoolAcademyPhysicalState.Rebuilding:
                    return "rebuilding";
                case HistoricalSchoolAcademyPhysicalState.Inactive:
                    return "inactive";
                default:
                    return "active";
            }
        }

        public static HistoricalSchoolAcademyPhysicalState FromStorage(string state)
        {
            if (string.Equals(state, "repair_pending", StringComparison.Ordinal))
                return HistoricalSchoolAcademyPhysicalState.RepairPending;
            if (string.Equals(state, "rebuilding", StringComparison.Ordinal))
                return HistoricalSchoolAcademyPhysicalState.Rebuilding;
            if (string.Equals(state, "inactive", StringComparison.Ordinal))
                return HistoricalSchoolAcademyPhysicalState.Inactive;
            return HistoricalSchoolAcademyPhysicalState.Active;
        }
    }
}
