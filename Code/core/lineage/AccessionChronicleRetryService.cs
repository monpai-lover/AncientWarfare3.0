using System;
using System.Collections.Generic;
using AncientWarfare3.core.asyncwork;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class AccessionChronicleRetryService
    {
        private const int RetryDelayFrames = 64;
        private const int InspectionBudget = 4;

        private sealed class PendingAccession
        {
            internal long WorldGeneration;
            internal long KingdomId;
            internal long ActorId;
            internal int NextEligibleFrame;
        }

        private static readonly Dictionary<long, PendingAccession> Pending =
            new Dictionary<long, PendingAccession>();
        private static readonly Queue<long> Order = new Queue<long>();
        private static readonly HashSet<long> Queued = new HashSet<long>();

        internal static void Track(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            long kingdomId = pKingdom.id;
            Pending[kingdomId] = new PendingAccession
            {
                WorldGeneration = AWAsyncRuntime.WorldGeneration,
                KingdomId = kingdomId,
                ActorId = pActor.data.id,
                NextEligibleFrame = Time.frameCount + RetryDelayFrames
            };
            EnqueueUnique(kingdomId);
        }

        internal static void Complete(Kingdom pKingdom, long pActorId)
        {
            long kingdomId = pKingdom?.data?.id ?? -1L;
            if (kingdomId < 0L || !Pending.TryGetValue(kingdomId,
                    out PendingAccession pending) ||
                pending.ActorId != pActorId) return;
            Pending.Remove(kingdomId);
        }

        internal static void ProcessAuthorityCycle()
        {
            if (Order.Count == 0 ||
                World.world?.kingdoms == null || World.world?.units == null)
                return;
            int inspected = Math.Min(InspectionBudget, Order.Count);
            for (int i = 0; i < inspected; i++)
            {
                long kingdomId = Order.Dequeue();
                Queued.Remove(kingdomId);
                if (!Pending.TryGetValue(kingdomId,
                        out PendingAccession pending)) continue;
                if (pending.WorldGeneration != AWAsyncRuntime.WorldGeneration)
                {
                    Pending.Remove(kingdomId);
                    continue;
                }
                if (Time.frameCount < pending.NextEligibleFrame)
                {
                    EnqueueUnique(kingdomId);
                    continue;
                }

                Kingdom kingdom = null;
                Actor actor = null;
                try
                {
                    kingdom = World.world.kingdoms.get(pending.KingdomId);
                    actor = World.world.units.get(pending.ActorId);
                }
                catch { }
                if (kingdom?.data == null || actor?.data == null ||
                    kingdom.isRekt() || actor.isRekt() ||
                    kingdom.king != actor)
                {
                    Pending.Remove(kingdomId);
                    HeirService.ClearAccessionModeSnapshot(kingdom,
                        pending.ActorId);
                    continue;
                }

                pending.NextEligibleFrame = Time.frameCount +
                    RetryDelayFrames;
                try { ChronicleEvents.OnKingChanged(kingdom, actor); }
                catch { }
                if (Pending.ContainsKey(kingdomId))
                    EnqueueUnique(kingdomId);
                return;
            }
        }

        private static void EnqueueUnique(long pKingdomId)
        {
            if (!Queued.Add(pKingdomId)) return;
            Order.Enqueue(pKingdomId);
        }

        internal static void Reset()
        {
            Pending.Clear();
            Order.Clear();
            Queued.Clear();
        }
    }
}
