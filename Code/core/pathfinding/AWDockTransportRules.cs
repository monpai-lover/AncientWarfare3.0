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
        internal static bool ShouldAttemptDockLookup(bool pSameIsland)
        {
            return !pSameIsland;
        }

        internal static bool ShouldRebuildTopology(bool pTopologyDirty,
            long pPreviousSourceRevision, long pCurrentSourceRevision,
            int pDirtyTileCount, int pLastRebuildFrame, int pCurrentFrame)
        {
            if (pDirtyTileCount > 0 || pLastRebuildFrame == pCurrentFrame)
                return false;
            return pTopologyDirty || pPreviousSourceRevision !=
                   pCurrentSourceRevision;
        }

        internal static float EstimateRouteTiles(int pStartX, int pStartY,
            int pEntryX, int pEntryY, int pExitX, int pExitY,
            int pTargetX, int pTargetY)
        {
            return Distance(pStartX, pStartY, pEntryX, pEntryY) +
                   Distance(pEntryX, pEntryY, pExitX, pExitY) +
                   Distance(pExitX, pExitY, pTargetX, pTargetY);
        }

        internal static float EstimateRouteTiles(int pStartX, int pStartY,
            int pEntryLandX, int pEntryLandY, int pEntryOceanX,
            int pEntryOceanY, int pExitOceanX, int pExitOceanY,
            int pExitLandX, int pExitLandY, int pTargetX, int pTargetY)
        {
            return Distance(pStartX, pStartY, pEntryLandX, pEntryLandY) +
                   Distance(pEntryLandX, pEntryLandY,
                       pEntryOceanX, pEntryOceanY) +
                   Distance(pEntryOceanX, pEntryOceanY,
                       pExitOceanX, pExitOceanY) +
                   Distance(pExitOceanX, pExitOceanY,
                       pExitLandX, pExitLandY) +
                   Distance(pExitLandX, pExitLandY, pTargetX, pTargetY);
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

        private static float Distance(int pLeftX, int pLeftY,
            int pRightX, int pRightY)
        {
            float dx = pLeftX - pRightX;
            float dy = pLeftY - pRightY;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
