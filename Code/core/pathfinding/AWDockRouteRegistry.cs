using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWDockRouteRegistry
    {
        private readonly ConcurrentDictionary<long, AWDockEndpoint> _docks =
            new ConcurrentDictionary<long, AWDockEndpoint>();

        internal int Count => _docks.Count;

        internal bool Register(AWDockEndpoint pDock)
        {
            if (!pDock.IsValid) return false;
            _docks[pDock.Id] = pDock;
            return true;
        }

        internal bool Remove(long pDockId)
        {
            return pDockId > 0 && _docks.TryRemove(pDockId, out _);
        }

        internal void Clear() => _docks.Clear();

        internal bool TryFindCandidate(int pStartTileId, int pTargetTileId,
            int pWaterComponent, out AWDockRouteCandidate pCandidate)
        {
            pCandidate = default;
            if (pStartTileId < 0 || pTargetTileId < 0 ||
                pStartTileId == pTargetTileId || pWaterComponent < 0) return false;
            AWDockEndpoint entry = default;
            AWDockEndpoint exit = default;
            foreach (AWDockEndpoint dock in _docks.Values)
            {
                if (dock.WaterComponent != pWaterComponent) continue;
                if (dock.TileId == pStartTileId &&
                    (!entry.IsValid || dock.Id < entry.Id)) entry = dock;
                if (dock.TileId == pTargetTileId &&
                    (!exit.IsValid || dock.Id < exit.Id)) exit = dock;
            }
            if (!entry.IsValid || !exit.IsValid || entry.Id == exit.Id) return false;
            pCandidate = new AWDockRouteCandidate(entry, exit,
                pStartTileId == pTargetTileId ? 0f : 1f);
            return pCandidate.IsValid;
        }
    }
}
