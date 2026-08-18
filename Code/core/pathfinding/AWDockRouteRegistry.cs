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
            if (!pDock.IsValid) return false;
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
