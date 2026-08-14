using System;
using System.Collections.Generic;
using life.taxi;

namespace AncientWarfare3.core.performance
{
    internal enum ArmyMilitaryMovementPriorityKind
    {
        RtsMember,
        RoyalGuard
    }

    internal static class ArmyMilitaryMovementPriorityIndex
    {
        private static readonly Dictionary<long, ArmyMilitaryMovementPriorityKind>
            Entries = new Dictionary<long, ArmyMilitaryMovementPriorityKind>();
        private static readonly List<long> Order = new List<long>();
        private static readonly HashSet<long> ProcessedThisCycle =
            new HashSet<long>();
        private static readonly HashSet<long> VanillaTaxiActors =
            new HashSet<long>();

        internal static void Register(long actorId,
            ArmyMilitaryMovementPriorityKind kind)
        {
            if (actorId < 0L) return;
            if (Entries.ContainsKey(actorId))
            {
                Entries[actorId] = kind;
                return;
            }
            Entries.Add(actorId, kind);
            Order.Add(actorId);
        }

        internal static void Unregister(long actorId)
        {
            if (!Entries.Remove(actorId)) return;
            int index = Order.IndexOf(actorId);
            if (index >= 0) Order.RemoveAt(index);
        }

        internal static void CopySnapshot(List<long> destination)
        {
            if (destination == null) return;
            destination.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < Order.Count; i++)
                {
                    long actorId = Order[i];
                    if (!Entries.TryGetValue(actorId,
                            out ArmyMilitaryMovementPriorityKind kind))
                        continue;
                    bool isGuard = ArmyMilitaryMovementPriorityRules
                        .ResolveP0PriorityRank(kind ==
                            ArmyMilitaryMovementPriorityKind.RoyalGuard) == 0;
                    if ((pass == 0) != isGuard) continue;
                    destination.Add(actorId);
                }
            }
        }

        internal static void RefreshVanillaTaxiSnapshot()
        {
            VanillaTaxiActors.Clear();
            try
            {
                for (int i = 0; i < TaxiManager.list.Count; i++)
                {
                    TaxiRequest request = TaxiManager.list[i];
                    if (request == null) continue;
                    HashSet<Actor> actors = request.getActors();
                    if (actors == null) continue;
                    foreach (Actor actor in actors)
                    {
                        if (actor?.data != null)
                            VanillaTaxiActors.Add(actor.data.id);
                    }
                }
            }
            catch { }
        }

        internal static bool HasVanillaTaxiOwnership(long actorId)
        {
            return actorId >= 0L && VanillaTaxiActors.Contains(actorId);
        }

        internal static void MarkVanillaTaxiOwnership(long actorId)
        {
            if (actorId >= 0L) VanillaTaxiActors.Add(actorId);
        }

        internal static bool TryGetKind(long actorId,
            out ArmyMilitaryMovementPriorityKind kind)
        {
            return Entries.TryGetValue(actorId, out kind);
        }

        internal static void BeginCycle()
        {
            ProcessedThisCycle.Clear();
        }

        internal static void MarkProcessed(long actorId)
        {
            if (actorId >= 0L) ProcessedThisCycle.Add(actorId);
        }

        internal static bool WasProcessed(long actorId)
        {
            return actorId >= 0L && ProcessedThisCycle.Contains(actorId);
        }

        internal static void Clear()
        {
            Entries.Clear();
            Order.Clear();
            ProcessedThisCycle.Clear();
            VanillaTaxiActors.Clear();
        }
    }
}
