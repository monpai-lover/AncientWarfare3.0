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
    }
}
