using System.Collections.Concurrent;
using System.Linq;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWDockRouteRegistry
    {
        private readonly ConcurrentDictionary<long, AWDockEndpoint> _docks =
            new ConcurrentDictionary<long, AWDockEndpoint>();

        internal bool Register(AWDockEndpoint pDock)
        {
            // The registry stores physical dock portals only. Shore
            // fallbacks (Id == 0) live in the transport service's separate
            // endpoint list and must not pollute the portal index.
            if (!pDock.IsDockPortal) return false;
            _docks[pDock.Id] = pDock;
            return true;
        }

        internal bool Remove(long pDockId)
        {
            return pDockId > 0 && _docks.TryRemove(pDockId, out _);
        }

        internal AWDockEndpoint[] Snapshot()
        {
            return _docks.Values.ToArray();
        }

        internal void Clear() => _docks.Clear();
    }
}
