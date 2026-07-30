using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyCaptainDisposalScope
    {
        private static readonly Dictionary<Army, int> ActiveArmies =
            new Dictionary<Army, int>();

        public static IDisposable Open(Army pArmy)
        {
            if (pArmy == null) return EmptyLease.Instance;
            ActiveArmies.TryGetValue(pArmy, out int depth);
            ActiveArmies[pArmy] = depth + 1;
            return new Lease(pArmy);
        }

        public static bool IsActive(Army pArmy)
        {
            return pArmy != null && ActiveArmies.TryGetValue(pArmy,
                out int depth) && depth > 0;
        }

        public static void ClearRuntime()
        {
            ActiveArmies.Clear();
        }

        private sealed class Lease : IDisposable
        {
            private Army _army;

            public Lease(Army pArmy)
            {
                _army = pArmy;
            }

            public void Dispose()
            {
                Army army = _army;
                if (army == null) return;
                _army = null;
                if (!ActiveArmies.TryGetValue(army, out int depth) ||
                    depth <= 1)
                {
                    ActiveArmies.Remove(army);
                    return;
                }
                ActiveArmies[army] = depth - 1;
            }
        }

        private sealed class EmptyLease : IDisposable
        {
            public static readonly EmptyLease Instance = new EmptyLease();

            public void Dispose()
            {
            }
        }
    }
}
