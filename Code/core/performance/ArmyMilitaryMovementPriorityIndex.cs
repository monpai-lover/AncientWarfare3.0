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
        private static readonly Dictionary<long, int> ProcessedFrameByActor =
            new Dictionary<long, int>();
        private static readonly HashSet<long> ProcessedThisMilitaryStep =
            new HashSet<long>();
        private static readonly HashSet<long> VanillaTaxiActors =
            new HashSet<long>();
        private static int _rtsMemberCount;

        internal static void Register(long actorId,
            ArmyMilitaryMovementPriorityKind kind)
        {
            if (actorId < 0L) return;
            if (Entries.ContainsKey(actorId))
            {
                if (Entries[actorId] != kind)
                {
                    if (Entries[actorId] ==
                        ArmyMilitaryMovementPriorityKind.RtsMember)
                        _rtsMemberCount--;
                    if (kind == ArmyMilitaryMovementPriorityKind.RtsMember)
                        _rtsMemberCount++;
                }
                Entries[actorId] = kind;
                return;
            }
            Entries.Add(actorId, kind);
            if (kind == ArmyMilitaryMovementPriorityKind.RtsMember)
                _rtsMemberCount++;
            Order.Add(actorId);
        }

        internal static void Unregister(long actorId)
        {
            if (actorId < 0L) return;
            bool wasRtsMember = Entries.TryGetValue(actorId,
                out ArmyMilitaryMovementPriorityKind existingKind) &&
                existingKind == ArmyMilitaryMovementPriorityKind.RtsMember;
            if (Entries.Remove(actorId))
            {
                if (wasRtsMember)
                    _rtsMemberCount--;
                int index = Order.IndexOf(actorId);
                if (index >= 0) Order.RemoveAt(index);
            }
            ProcessedFrameByActor.Remove(actorId);
            ProcessedThisMilitaryStep.Remove(actorId);
            VanillaTaxiActors.Remove(actorId);
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

        internal static int Count => Entries.Count;
        internal static int RtsMemberCount => Math.Max(0, _rtsMemberCount);

        internal static void BeginCycle()
        {
            BeginFrame(UnityEngine.Time.frameCount);
        }

        internal static void BeginFrame(int frameId)
        {
            _ = frameId;
        }

        internal static void BeginMilitaryStep()
        {
            ProcessedThisMilitaryStep.Clear();
        }

        internal static void MarkProcessed(long actorId)
        {
            MarkProcessed(actorId, UnityEngine.Time.frameCount);
        }

        internal static void MarkProcessed(long actorId, int frameId)
        {
            if (actorId < 0L) return;
            ProcessedFrameByActor[actorId] = frameId;
            ProcessedThisMilitaryStep.Add(actorId);
        }

        internal static bool WasProcessed(long actorId)
        {
            return WasProcessed(actorId, UnityEngine.Time.frameCount);
        }

        internal static bool WasProcessed(long actorId, int frameId)
        {
            return actorId >= 0L &&
                   ProcessedFrameByActor.TryGetValue(actorId,
                       out int processedFrame) &&
                   processedFrame == frameId;
        }

        internal static bool WasProcessedInMilitaryStep(long actorId)
        {
            return actorId >= 0L &&
                   ProcessedThisMilitaryStep.Contains(actorId);
        }

        internal static void Clear()
        {
            Entries.Clear();
            Order.Clear();
            ProcessedFrameByActor.Clear();
            ProcessedThisMilitaryStep.Clear();
            VanillaTaxiActors.Clear();
            _rtsMemberCount = 0;
        }
    }
}
