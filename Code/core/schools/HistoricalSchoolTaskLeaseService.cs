using System;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolTaskLeaseService
    {
        private static readonly HistoricalSchoolTaskLeaseBook Leases =
            new HistoricalSchoolTaskLeaseBook();

        public static int Count => Leases.Count;

        public static bool TrySchedule(
            Actor pActor,
            string pActivityId,
            string pTaskId,
            string pSchoolId,
            long pCityId,
            string pVenueKey,
            WorldTile pTarget,
            long pStartFrame,
            long pExpiryFrame)
        {
            if (pActor?.data == null || pTarget == null) return false;
            var lease = new HistoricalSchoolTaskLease(
                pActor.data.id,
                pActivityId,
                pTaskId,
                pSchoolId,
                pCityId,
                pVenueKey,
                pStartFrame,
                pExpiryFrame);
            if (!Leases.TryAcquire(lease)) return false;
            try
            {
                pActor.scheduleTask(pTaskId, pTarget);
                return true;
            }
            catch
            {
                ReleaseExact(pActor.data.id, pActivityId);
                return false;
            }
        }

        public static bool IsCurrent(
            long pActorId,
            string pActivityId,
            string pTaskId = null)
        {
            return Leases.IsCurrent(pActorId, pActivityId, pTaskId);
        }

        public static bool TryGet(
            long pActorId,
            out HistoricalSchoolTaskLease pLease)
        {
            return Leases.TryGet(pActorId, out pLease);
        }

        public static bool ReleaseExact(long pActorId, string pActivityId)
        {
            if (!Leases.TryRelease(pActorId, pActivityId,
                    out HistoricalSchoolTaskLease lease)) return false;
            HistoricalSchoolVenueService.Release(lease.VenueKey);
            return true;
        }

        public static bool ReleaseActor(long pActorId)
        {
            if (!Leases.TryReleaseActor(pActorId,
                    out HistoricalSchoolTaskLease lease)) return false;
            HistoricalSchoolVenueService.Release(lease.VenueKey);
            return true;
        }

        public static bool TryTakeExpired(
            long pFrame,
            out HistoricalSchoolTaskLease pLease)
        {
            if (!Leases.TryExpireOne(pFrame, out pLease)) return false;
            HistoricalSchoolVenueService.Release(pLease.VenueKey);
            return true;
        }

        public static void Clear()
        {
            Leases.Clear();
        }
    }
}
