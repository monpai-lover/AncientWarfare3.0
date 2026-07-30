namespace AncientWarfare3.core.lineage
{
    public static class WarParticipantLifecycleRules
    {
        public static bool CanJoin(bool alreadyOnSide,
            bool initializingMainBelligerent,
            bool exitLookupSucceeded, bool hasSeparatePeaceExit)
        {
            if (alreadyOnSide || initializingMainBelligerent) return true;
            return exitLookupSucceeded && !hasSeparatePeaceExit;
        }

        public static bool ShouldRollbackJoin(bool wasOnSideBeforeJoin,
            bool joinedAfterCall, bool sourceRequired,
            bool sourceWriteSucceeded)
        {
            return !wasOnSideBeforeJoin && joinedAfterCall &&
                   sourceRequired && !sourceWriteSucceeded;
        }

        public static bool RequiresDurableJoinSource(bool hasSource,
            WarParticipantEntrySourceKind sourceKind)
        {
            return hasSource && sourceKind !=
                   WarParticipantEntrySourceKind.MainBelligerent;
        }

        public static bool ShouldQueueRollbackRepair(
            bool rollbackVerified, bool membershipLookupSucceeded,
            bool remainsOnSideAfterRollback)
        {
            return !membershipLookupSucceeded ||
                   (!rollbackVerified && remainsOnSideAfterRollback);
        }

        public static bool ShouldNotifyRollbackDeparture(
            bool participantServicesStarted,
            bool membershipLookupSucceeded,
            bool remainsOnSideAfterRollback)
        {
            return participantServicesStarted && membershipLookupSucceeded &&
                   !remainsOnSideAfterRollback;
        }

        public static bool ShouldNotifyDeparture(
            bool wasOnSideBeforeRemove, bool remainsOnSideAfterRemove)
        {
            return wasOnSideBeforeRemove && !remainsOnSideAfterRemove;
        }
    }
}
