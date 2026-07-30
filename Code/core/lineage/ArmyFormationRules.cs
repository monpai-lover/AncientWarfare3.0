using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ArmyFormationOffset
    {
        public ArmyFormationOffset(int pX, int pY)
        {
            X = pX;
            Y = pY;
        }

        public int X { get; }
        public int Y { get; }
    }

    public readonly struct ArmyFormationObservationProgress
    {
        public ArmyFormationObservationProgress(int pMemberCount,
            int pCursor, bool pComplete, int pRestartCount)
        {
            MemberCount = pMemberCount;
            Cursor = pCursor;
            Complete = pComplete;
            RestartCount = pRestartCount;
        }

        public int MemberCount { get; }
        public int Cursor { get; }
        public bool Complete { get; }
        public int RestartCount { get; }
    }

    public static class ArmyFormationRules
    {
        public const int MaximumTrackedMembers = 128;
        public const int MaximumFormationWidth = 17;
        public const int LocalRadius = 8;
        public const int PlacementRadius = 13;
        public const int PlacementAttempts = 4;
        public const int LooseEscortHoldRadius = 4;
        public const int LooseEscortOuterRadius = 8;
        public const int MaximumFallbackRecoveryAttempts = 96;
        public const int QuorumPercent =
            ArmyRtsRules.DeploymentQuorumPercent;
        private static readonly ArmyFormationOffset[][] OffsetsByWidth =
            BuildOffsetsByWidth();

        public static int StableSlotOrder(long armyId, long actorId)
        {
            unchecked
            {
                ulong value = (ulong)armyId + 0x9E3779B97F4A7C15UL;
                value ^= (ulong)actorId + 0x9E3779B97F4A7C15UL +
                         (value << 6) + (value >> 2);
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (int)(value % MaximumTrackedMembers);
            }
        }

        public static int ClampTerrainWidth(int desiredWidth,
            int terrainWidth)
        {
            int desired = Math.Max(1, desiredWidth);
            int available = Math.Max(1, terrainWidth);
            return Math.Min(MaximumFormationWidth,
                Math.Min(desired, available));
        }

        public static ArmyFormationOffset LocalOffset(int slotOrder,
            int desiredWidth, int terrainWidth)
        {
            int width = ClampTerrainWidth(desiredWidth, terrainWidth);
            ArmyFormationOffset[] offsets = OffsetsByWidth[width];
            int order = NormalizeSlot(slotOrder);
            return offsets[order];
        }

        public static ArmyFormationOffset PlacementOffset(int slotOrder,
            int desiredWidth, int terrainWidth, int attempt)
        {
            int width = ClampTerrainWidth(desiredWidth, terrainWidth);
            ArmyFormationOffset[] offsets = OffsetsByWidth[width];
            int normalizedAttempt = Math.Max(0,
                Math.Min(PlacementAttempts - 1, attempt));
            int order = NormalizeSlot(slotOrder) +
                        normalizedAttempt * MaximumTrackedMembers;
            return offsets[order];
        }

        public static bool HasQuorum(int living, int ready)
        {
            if (living <= 0 || ready <= 0) return false;
            return (long)Math.Min(ready, living) * 100L >=
                   (long)living * QuorumPercent;
        }

        public static bool ShouldOwnEscortFollow(ArmyRtsState pState,
            bool pImmediateCombat, bool pTransportOwned)
        {
            if (pImmediateCombat || pTransportOwned) return false;
            switch (pState)
            {
                case ArmyRtsState.Rally:
                case ArmyRtsState.March:
                case ArmyRtsState.Deploy:
                case ArmyRtsState.Hold:
                case ArmyRtsState.Assault:
                case ArmyRtsState.Pursue:
                case ArmyRtsState.Retreat:
                case ArmyRtsState.Regroup:
                case ArmyRtsState.Replenish:
                    return true;
                default:
                    return false;
            }
        }

        public static bool HasEscortDeploymentReadiness(
            bool pRouteArrived, bool pOperationalStrengthReady)
        {
            return pRouteArrived && pOperationalStrengthReady;
        }

        public static bool IsInsideLooseEscort(float pDistanceSquared)
        {
            return pDistanceSquared >= 0f &&
                   pDistanceSquared <=
                   LooseEscortHoldRadius * LooseEscortHoldRadius;
        }

        public static bool TryGetFallbackRecoveryOffset(int pAttempt,
            out ArmyFormationOffset pOffset)
        {
            pOffset = new ArmyFormationOffset(0, 0);
            if (pAttempt < 0 || pAttempt >=
                MaximumFallbackRecoveryAttempts) return false;
            int remaining = pAttempt;
            for (int radius = 1; radius <= LooseEscortOuterRadius;
                 radius++)
            {
                int perimeter = radius * 8;
                if (remaining >= perimeter)
                {
                    remaining -= perimeter;
                    continue;
                }
                int sideLength = radius * 2;
                int side = remaining / sideLength;
                int along = remaining % sideLength;
                switch (side)
                {
                    case 0:
                        pOffset = new ArmyFormationOffset(-radius + along,
                            -radius);
                        return true;
                    case 1:
                        pOffset = new ArmyFormationOffset(radius,
                            -radius + along);
                        return true;
                    case 2:
                        pOffset = new ArmyFormationOffset(radius - along,
                            radius);
                        return true;
                    default:
                        pOffset = new ArmyFormationOffset(-radius,
                            radius - along);
                        return true;
                }
            }
            return false;
        }

        public static ArmyFormationOffset LooseEscortOffset(
            int pSlotOrder)
        {
            int order = NormalizeSlot(pSlotOrder);
            for (int radius = 2; radius <= LooseEscortOuterRadius;
                 radius++)
            {
                int perimeter = radius * 8;
                if (order >= perimeter)
                {
                    order -= perimeter;
                    continue;
                }
                int side = order / (radius * 2);
                int along = order % (radius * 2);
                switch (side)
                {
                    case 0:
                        return new ArmyFormationOffset(-radius + along,
                            -radius);
                    case 1:
                        return new ArmyFormationOffset(radius,
                            -radius + along);
                    case 2:
                        return new ArmyFormationOffset(radius - along,
                            radius);
                    default:
                        return new ArmyFormationOffset(-radius,
                            radius - along);
                }
            }
            return new ArmyFormationOffset(0, LooseEscortOuterRadius);
        }

        public static bool IsEligibleFormationMember(bool actorValid,
            bool belongsToArmy, bool currentProfessionIsWarrior,
            bool isCivilAuthority, bool isCurrentCaptain)
        {
            if (!actorValid || !belongsToArmy) return false;
            return isCurrentCaptain || currentProfessionIsWarrior &&
                   !isCivilAuthority;
        }

        public static bool ShouldRestartObservation(
            bool deploymentEligibilityChanged, bool deploymentEligible,
            bool formationGeometryChanged)
        {
            return deploymentEligibilityChanged ||
                   deploymentEligible && formationGeometryChanged;
        }

        public static bool ShouldRestartObservationForAnchorUpdate(
            bool allowObservationRestart,
            bool deploymentEligibilityChanged, bool deploymentEligible,
            bool formationGeometryChanged)
        {
            // The member observation is deliberately batched. A moving
            // captain updates the anchor every controller pass, so restarting
            // on geometry would prevent armies above one batch from ever
            // completing their observation. Position and layout are refreshed
            // incrementally; only a deployment-mode transition invalidates
            // readiness for the whole roster.
            return allowObservationRestart && deploymentEligibilityChanged;
        }

        public static bool ShouldObserveRoster(bool observationComplete,
            int recordedMemberCount, int currentMemberCount)
        {
            return !observationComplete ||
                   Math.Max(0, recordedMemberCount) !=
                   Math.Max(0, currentMemberCount);
        }

        public static ArmyFormationObservationProgress
            DescribeObservationProgress(int memberCount, int cursor,
                bool observationComplete, int restartCount)
        {
            int normalizedMembers = Math.Max(0, memberCount);
            int normalizedCursor = Math.Max(0,
                Math.Min(normalizedMembers, cursor));
            bool complete = observationComplete;
            if (complete) normalizedCursor = normalizedMembers;
            return new ArmyFormationObservationProgress(normalizedMembers,
                normalizedCursor, complete, Math.Max(0, restartCount));
        }

        public static bool IsMemberDeployed(bool deploymentEligible,
            bool actorIsCaptain, float anchorDistanceSquared,
            float slotDistanceSquared, int toleranceSquared)
        {
            if (!deploymentEligible) return false;
            float distanceSquared = actorIsCaptain
                ? anchorDistanceSquared
                : slotDistanceSquared;
            return distanceSquared <= Math.Max(0, toleranceSquared);
        }

        public static bool CanDirectCorrect(float distanceSquared)
        {
            return distanceSquared > 0f &&
                   distanceSquared <= LocalRadius * LocalRadius;
        }

        public static void ClampVectorToRadius(int pX, int pY,
            int pRadius, out int pClampedX, out int pClampedY)
        {
            int radius = Math.Max(0, pRadius);
            long radiusSquared = (long)radius * radius;
            long distanceSquared = (long)pX * pX + (long)pY * pY;
            if (distanceSquared <= radiusSquared)
            {
                pClampedX = pX;
                pClampedY = pY;
                return;
            }
            if (radius == 0)
            {
                pClampedX = 0;
                pClampedY = 0;
                return;
            }

            double scale = radius / Math.Sqrt(distanceSquared);
            pClampedX = (int)Math.Truncate(pX * scale);
            pClampedY = (int)Math.Truncate(pY * scale);
            while ((long)pClampedX * pClampedX +
                   (long)pClampedY * pClampedY > radiusSquared)
            {
                if (Math.Abs(pClampedX) >= Math.Abs(pClampedY))
                    pClampedX -= Math.Sign(pClampedX);
                else
                    pClampedY -= Math.Sign(pClampedY);
            }
        }

        private static ArmyFormationOffset[][] BuildOffsetsByWidth()
        {
            var result = new ArmyFormationOffset[
                MaximumFormationWidth + 1][];
            result[0] = new[] { new ArmyFormationOffset(0, 0) };
            for (int width = 1; width <= MaximumFormationWidth; width++)
            {
                var offsets = new List<ArmyFormationOffset>();
                var occupied = new HashSet<int>();
                AddPreferredBand(offsets, occupied, width);
                AddCircularOffsets(offsets, occupied, LocalRadius);
                AddCircularOffsets(offsets, occupied, PlacementRadius);
                int required = MaximumTrackedMembers * PlacementAttempts;
                if (offsets.Count < required)
                    throw new InvalidOperationException(
                        "Formation placement radius is too small.");
                result[width] = offsets.GetRange(0, required).ToArray();
            }
            return result;
        }

        private static void AddPreferredBand(
            List<ArmyFormationOffset> pOffsets, HashSet<int> pOccupied,
            int pWidth)
        {
            int minimumX = -(pWidth / 2);
            int radiusSquared = LocalRadius * LocalRadius;
            for (int y = 0; y <= LocalRadius; y++)
                AddRow(pOffsets, pOccupied, minimumX, pWidth, y,
                    radiusSquared);
            for (int y = -1; y >= -LocalRadius; y--)
                AddRow(pOffsets, pOccupied, minimumX, pWidth, y,
                    radiusSquared);
        }

        private static void AddCircularOffsets(
            List<ArmyFormationOffset> pOffsets, HashSet<int> pOccupied,
            int pRadius)
        {
            int radiusSquared = pRadius * pRadius;
            for (int ring = 1; ring <= pRadius; ring++)
            {
                for (int y = -ring; y <= ring; y++)
                {
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring ||
                            x * x + y * y > radiusSquared) continue;
                        AddUnique(pOffsets, pOccupied, x, y);
                    }
                }
            }
        }

        private static void AddRow(List<ArmyFormationOffset> pOffsets,
            HashSet<int> pOccupied, int pMinimumX, int pWidth, int pY,
            int pRadiusSquared)
        {
            for (int column = 0; column < pWidth; column++)
            {
                int x = pMinimumX + column;
                if (x * x + pY * pY > pRadiusSquared) continue;
                AddUnique(pOffsets, pOccupied, x, pY);
            }
        }

        private static void AddUnique(List<ArmyFormationOffset> pOffsets,
            HashSet<int> pOccupied, int pX, int pY)
        {
            if (pX == 0 && pY == 0) return;
            int key = (pX + PlacementRadius) *
                      (PlacementRadius * 2 + 1) + pY + PlacementRadius;
            if (!pOccupied.Add(key)) return;
            pOffsets.Add(new ArmyFormationOffset(pX, pY));
        }

        private static int NormalizeSlot(int pSlotOrder)
        {
            int slot = pSlotOrder % MaximumTrackedMembers;
            return slot < 0 ? slot + MaximumTrackedMembers : slot;
        }
    }
}
