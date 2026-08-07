using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWDockRouteRegistry
    {
        private readonly ConcurrentDictionary<AWDockEndpointKey,
            AWDockEndpoint> _docks = new ConcurrentDictionary<AWDockEndpointKey,
                AWDockEndpoint>();

        internal bool Register(AWDockEndpoint pDock)
        {
            if (!pDock.IsValid) return false;
            _docks[pDock.Key] = pDock;
            return true;
        }

        internal bool Replace(long pDockId,
            IReadOnlyCollection<AWDockEndpoint> pEndpoints)
        {
            if (pDockId <= 0) return false;
            Remove(pDockId);
            bool registered = false;
            if (pEndpoints == null) return false;
            foreach (AWDockEndpoint endpoint in pEndpoints)
            {
                if (endpoint.Id != pDockId) continue;
                registered |= Register(endpoint);
            }
            return registered;
        }

        internal bool Remove(long pDockId)
        {
            if (pDockId <= 0) return false;
            bool removed = false;
            foreach (AWDockEndpointKey key in _docks.Keys)
            {
                if (key.DockId != pDockId) continue;
                removed |= _docks.TryRemove(key, out _);
            }
            return removed;
        }

        internal bool Remove(AWDockEndpoint pDock)
        {
            return pDock.IsValid && _docks.TryRemove(pDock.Key, out _);
        }

        internal AWDockEndpoint[] Snapshot()
        {
            return _docks.Values.ToArray();
        }

        internal void Clear() => _docks.Clear();
    }
}
