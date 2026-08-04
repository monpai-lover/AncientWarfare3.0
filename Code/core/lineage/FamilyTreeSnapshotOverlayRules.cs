namespace AncientWarfare3.core.lineage
{
    internal static class FamilyTreeSnapshotOverlayRules
    {
        public static bool ResolveAlive(bool snapshotAlive,
            bool hasPendingArchive, bool pendingArchiveAlive,
            bool liveKnownDead, bool runtimeAuthorityReady,
            bool runtimeActorMissing)
        {
            if (runtimeAuthorityReady && runtimeActorMissing) return false;
            bool archiveAlive = hasPendingArchive
                ? pendingArchiveAlive
                : snapshotAlive;
            return archiveAlive && !liveKnownDead;
        }
    }
}
