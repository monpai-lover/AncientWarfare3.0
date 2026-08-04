using System;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct AsyncStrategyAdmissionToken
    {
        private AsyncStrategyAdmissionToken(long leaseId,
            int previousMarker, int reservedMarker, int previousCursor,
            int reservedCursor, bool hasCursor)
        {
            LeaseId = leaseId;
            PreviousMarker = previousMarker;
            ReservedMarker = reservedMarker;
            PreviousCursor = previousCursor;
            ReservedCursor = reservedCursor;
            HasCursor = hasCursor;
        }

        public long LeaseId { get; }
        public int PreviousMarker { get; }
        public int ReservedMarker { get; }
        public int PreviousCursor { get; }
        public int ReservedCursor { get; }
        public bool HasCursor { get; }
        public bool IsValid => LeaseId > 0L;

        public static bool TryCreateCadence(long leaseId,
            int previousMarker, int reservedMarker,
            out AsyncStrategyAdmissionToken token)
        {
            token = default;
            if (leaseId <= 0L) return false;
            token = new AsyncStrategyAdmissionToken(leaseId, previousMarker,
                reservedMarker, -1, -1, hasCursor: false);
            return true;
        }

        public static bool TryCreateDiplomacy(long leaseId,
            int previousMarker, int reservedMarker, int previousCursor,
            int cursorCount, long expectedResponderId,
            long observedResponderId, out AsyncStrategyAdmissionToken token)
        {
            token = default;
            if (leaseId <= 0L || cursorCount <= 0 ||
                (expectedResponderId >= 0L &&
                 observedResponderId != expectedResponderId))
                return false;
            int normalizedCursor = Math.Max(0, previousCursor) % cursorCount;
            token = new AsyncStrategyAdmissionToken(leaseId, previousMarker,
                reservedMarker, normalizedCursor,
                (normalizedCursor + 1) % cursorCount, hasCursor: true);
            return true;
        }

        public bool TryRollback(long currentLeaseId, ref int currentMarker)
        {
            if (!IsValid || HasCursor || currentLeaseId != LeaseId ||
                currentMarker != ReservedMarker)
                return false;
            currentMarker = PreviousMarker;
            return true;
        }

        public bool TryRollback(long currentLeaseId, ref int currentMarker,
            ref int currentCursor)
        {
            if (!IsValid || !HasCursor || currentLeaseId != LeaseId ||
                currentMarker != ReservedMarker ||
                currentCursor != ReservedCursor)
                return false;
            currentMarker = PreviousMarker;
            currentCursor = PreviousCursor;
            return true;
        }
    }
}
