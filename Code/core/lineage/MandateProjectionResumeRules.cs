namespace AncientWarfare3.core.lineage
{
    public enum MandateProjectionDisposition
    {
        Current,
        TerminalHistoryOnly
    }

    public enum MandateLegalCoreReplayDisposition
    {
        Skip,
        CaptureLegacySnapshot,
        ProjectDurableSnapshot
    }

    public static class MandateAuthorityMutationRules
    {
        public static bool CanMutate(bool replicaSession)
        {
            return !replicaSession;
        }
    }

    public static class MandateProjectionResumeRules
    {
        public static bool ShouldRun(bool databaseReady, bool worldReady,
            bool replicaSession, int batchLimit)
        {
            return databaseReady && worldReady && !replicaSession &&
                   batchLimit > 0;
        }

        public static bool CanMutateOutbox(bool replicaSession)
        {
            return MandateAuthorityMutationRules.CanMutate(replicaSession);
        }

        public static bool ShouldStartAnnualCycle(int lastCycleYear,
            int currentYear, bool replicaSession)
        {
            return !replicaSession && currentYear != lastCycleYear;
        }

        public static MandateProjectionDisposition ResolveDisposition(
            bool reportActive, long reportPeriodId, long reportKingdomId,
            long pendingPeriodId, long pendingKingdomId, bool kingdomAlive)
        {
            return reportActive && kingdomAlive &&
                   reportPeriodId == pendingPeriodId &&
                   reportKingdomId == pendingKingdomId
                ? MandateProjectionDisposition.Current
                : MandateProjectionDisposition.TerminalHistoryOnly;
        }

        public static long ResolveRuntimeActorId(
            MandateProjectionDisposition disposition,
            long installedActorId, long declarationActorId)
        {
            return disposition == MandateProjectionDisposition.Current &&
                   installedActorId >= 0L
                ? installedActorId
                : -1L;
        }

        public static long ResolveHistoryActorId(long declarationActorId)
        {
            return declarationActorId;
        }

        public static bool ShouldPublishEffect(
            MandateProjectionDisposition disposition, string effect)
        {
            if (disposition == MandateProjectionDisposition.Current)
                return true;
            switch (effect)
            {
                case "old_kingdom_history":
                case "old_mandate_event":
                case "new_mandate_event":
                case "new_kingdom_history":
                case "new_person_history":
                case "legal_cores":
                    return true;
                default:
                    return false;
            }
        }

        public static MandateLegalCoreReplayDisposition
            ResolveLegalCoreReplay(MandateProjectionDisposition disposition,
                string coreSnapshotSource)
        {
            if (!string.IsNullOrEmpty(coreSnapshotSource))
                return MandateLegalCoreReplayDisposition.
                    ProjectDurableSnapshot;
            return disposition == MandateProjectionDisposition.Current
                ? MandateLegalCoreReplayDisposition.CaptureLegacySnapshot
                : MandateLegalCoreReplayDisposition.Skip;
        }
    }
}
