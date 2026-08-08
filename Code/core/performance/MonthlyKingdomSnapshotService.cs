using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal static class MonthlyKingdomSnapshotService
    {
        private static IReadOnlyList<Kingdom> _snapshot =
            Array.Empty<Kingdom>();
        private static MapBox _world;
        private static int _monthKey = int.MinValue;

        internal static IReadOnlyList<Kingdom> Get(int pMonthKey)
        {
            MapBox world = World.world;
            if (world?.kingdoms == null) return Array.Empty<Kingdom>();
            if (ReferenceEquals(_world, world) && _monthKey == pMonthKey)
                return _snapshot;

            var snapshot = new List<Kingdom>();
            foreach (Kingdom kingdom in world.kingdoms)
                snapshot.Add(kingdom);
            _world = world;
            _monthKey = pMonthKey;
            _snapshot = snapshot;
            return _snapshot;
        }

        internal static void Reset()
        {
            _snapshot = Array.Empty<Kingdom>();
            _world = null;
            _monthKey = int.MinValue;
        }
    }
}
