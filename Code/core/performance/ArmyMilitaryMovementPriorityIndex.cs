using System;
using System.Collections.Generic;

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
        private static int Cursor;

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
            if (Order.Count == 0) Cursor = 0;
            else if (Cursor >= Order.Count) Cursor %= Order.Count;
        }

        internal static int TakeNextSlice(int simulationBatchSize,
            List<long> destination)
        {
            if (destination == null) return 0;
            destination.Clear();
            int count = ArmyMilitaryMovementPriorityRules.ResolveP0SliceCount(
                Order.Count, simulationBatchSize);
            for (int i = 0; i < count && Order.Count > 0; i++)
            {
                if (Cursor >= Order.Count) Cursor = 0;
                destination.Add(Order[Cursor++]);
            }
            return destination.Count;
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
            Cursor = 0;
        }
    }
}
