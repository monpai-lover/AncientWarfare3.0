using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    [Flags]
    internal enum DeJureDirtyReason
    {
        None = 0,
        Ownership = 1,
        Capital = 2,
        CityRoster = 4,
        Membership = 8,
        Seat = 16,
        Name = 32,
        Retirement = 64,
        Merge = 128,
        WorldLoad = 256
    }

    internal static class DeJureRegionMaintenanceService
    {
        private const int MaxRetries = 5;
        private const int MaxBackoff = 16;
        private sealed class Ticket
        {
            internal long Id;
            internal DeJureDirtyReason Reasons;
            internal int RetryCount;
            internal long NextCycle;
            internal bool Dormant;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<long, Ticket> DirtyKingdomIds =
            new Dictionary<long, Ticket>();
        private static readonly Dictionary<long, Ticket> DirtyRegionIds =
            new Dictionary<long, Ticket>();
        private static long _cycle;

        internal static int PendingCount
        {
            get
            {
                lock (Gate) return DirtyKingdomIds.Count + DirtyRegionIds.Count;
            }
        }

        internal static void MarkKingdomDirty(long pKingdomId,
            DeJureDirtyReason pReason)
        {
            Mark(DirtyKingdomIds, pKingdomId, pReason);
        }

        internal static void MarkRegionDirty(long pRegionId,
            DeJureDirtyReason pReason)
        {
            Mark(DirtyRegionIds, pRegionId, pReason);
        }

        internal static int ProcessAuthorityCycle(int pItemBudget)
        {
            if (pItemBudget <= 0) return 0;
            List<Ticket> kingdoms;
            List<Ticket> regions;
            lock (Gate)
            {
                _cycle++;
                kingdoms = TakeDue(DirtyKingdomIds, pItemBudget);
                int remaining = Math.Max(0, pItemBudget - kingdoms.Count);
                regions = TakeDue(DirtyRegionIds, remaining);
            }

            int completed = 0;
            foreach (Ticket ticket in kingdoms)
            {
                if (ProcessTicket(ticket, isKingdom: true)) completed++;
            }
            foreach (Ticket ticket in regions)
            {
                if (ProcessTicket(ticket, isKingdom: false)) completed++;
            }
            return completed;
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                DirtyKingdomIds.Clear();
                DirtyRegionIds.Clear();
                _cycle = 0L;
            }
        }

        internal static void ClearRuntime() => Reset();

        private static void Mark(Dictionary<long, Ticket> pQueue, long pId,
            DeJureDirtyReason pReason)
        {
            if (pId < 0L || pReason == DeJureDirtyReason.None) return;
            lock (Gate)
            {
                if (!pQueue.TryGetValue(pId, out Ticket ticket))
                {
                    pQueue[pId] = new Ticket
                    {
                        Id = pId,
                        Reasons = pReason,
                        NextCycle = _cycle + 1L
                    };
                    return;
                }
                ticket.Reasons |= pReason;
                ticket.Dormant = false;
                ticket.RetryCount = 0;
                ticket.NextCycle = _cycle + 1L;
            }
        }

        private static List<Ticket> TakeDue(Dictionary<long, Ticket> pQueue,
            int pBudget)
        {
            var result = new List<Ticket>();
            if (pBudget <= 0) return result;
            foreach (Ticket ticket in pQueue.Values)
            {
                if (result.Count >= pBudget) break;
                if (ticket.Dormant || ticket.NextCycle > _cycle) continue;
                result.Add(ticket);
            }
            foreach (Ticket ticket in result) pQueue.Remove(ticket.Id);
            return result;
        }

        private static bool ProcessTicket(Ticket pTicket, bool isKingdom)
        {
            bool success = false;
            try
            {
                success = isKingdom
                    ? DeJureRegionStore.ProcessDirtyKingdom(pTicket.Id,
                        pTicket.Reasons)
                    : DeJureRegionStore.ProcessDirtyRegion(pTicket.Id,
                        pTicket.Reasons);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("De jure maintenance ticket failed: " +
                    (isKingdom ? "kingdom=" : "region=") + pTicket.Id +
                    ", retry=" + pTicket.RetryCount + ", " + error.Message);
            }
            if (success) return true;
            lock (Gate)
            {
                pTicket.RetryCount++;
                if (pTicket.RetryCount >= MaxRetries)
                {
                    pTicket.Dormant = true;
                    pTicket.NextCycle = long.MaxValue;
                }
                else
                {
                    int shift = Math.Min(pTicket.RetryCount - 1, 4);
                    int delay = Math.Min(MaxBackoff, 1 << shift);
                    pTicket.NextCycle = _cycle + delay;
                }
                (isKingdom ? DirtyKingdomIds : DirtyRegionIds)[pTicket.Id] =
                    pTicket;
            }
            return false;
        }
    }
}
