using System;

namespace AncientWarfare3.core.pathfinding
{
    internal enum AWDockPassengerState
    {
        WaitingBoat,
        Boarding,
        Sailing,
        Unloading,
        Completed,
        Failed
    }

    internal static class AWDockTransportRules
    {
        private const float MinimumTransportGainTiles = 8f;

        internal static bool ShouldRefreshWorldRegistry(bool pWorldScanCompleted,
            int pEndpointCount)
        {
            return !pWorldScanCompleted && pEndpointCount < 2;
        }

        internal static bool ShouldAttemptDockLookup(bool pSameIsland)
        {
            return !pSameIsland;
        }

        internal static bool ShouldPreferTransport(float landRouteTiles,
            float transportRouteTiles)
        {
            return IsFiniteNonNegative(landRouteTiles) &&
                   IsFiniteNonNegative(transportRouteTiles) &&
                   transportRouteTiles + Math.Max(MinimumTransportGainTiles,
                       landRouteTiles * 0.1f) < landRouteTiles;
        }

        internal static float EstimateRouteTiles(int startX, int startY,
            int entryX, int entryY, int exitX, int exitY, int targetX,
            int targetY)
        {
            return Distance(startX, startY, entryX, entryY) +
                   Distance(entryX, entryY, exitX, exitY) +
                   Distance(exitX, exitY, targetX, targetY);
        }

        internal static bool CanCreatePhysicalRoute(int startTileId,
            int endTileId, bool sameIsland, bool actorAlreadyEmbarked)
        {
            return startTileId >= 0 && endTileId >= 0 &&
                   startTileId != endTileId && !sameIsland &&
                   !actorAlreadyEmbarked;
        }

        internal static AWDockPassengerState NextState(
            AWDockPassengerState current, bool alive, bool targetValid,
            bool insideBoat, bool requestExists, bool reachedDestination,
            bool timedOut)
        {
            if (!alive || !targetValid || timedOut)
                return AWDockPassengerState.Failed;
            if (reachedDestination && !insideBoat && !requestExists)
                return AWDockPassengerState.Completed;
            if (insideBoat) return AWDockPassengerState.Sailing;
            if (current == AWDockPassengerState.Sailing && !requestExists)
                return AWDockPassengerState.Unloading;
            return requestExists
                ? AWDockPassengerState.WaitingBoat
                : AWDockPassengerState.Failed;
        }

        private static bool IsFiniteNonNegative(float pValue)
        {
            return !float.IsNaN(pValue) && !float.IsInfinity(pValue) &&
                   pValue >= 0f;
        }

        private static float Distance(int pLeftX, int pLeftY, int pRightX,
            int pRightY)
        {
            float dx = pLeftX - pRightX;
            float dy = pLeftY - pRightY;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
