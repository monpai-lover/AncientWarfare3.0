using System;
using System.IO;

namespace AncientWarfare3.core.lineage
{
    [Flags]
    public enum CityReservePoolFinalRestoreActions
    {
        None = 0,
        ValidateMembers = 1,
        PopulateActorPool = 2,
        RestoreWarEmergency = 4
    }

    public static class CityReservePoolPersistenceRules
    {
        public const int CurrentVersion = 2;
        public const string SnapshotFileName =
            "aw3_city_reserve_pools.json";

        public static bool CanUseSnapshotVersion(int version)
        {
            return version == CurrentVersion;
        }

        public static bool ShouldWriteSnapshot(bool worldReady,
            bool directoryValid)
        {
            return worldReady && directoryValid;
        }

        public static bool ShouldRestoreSnapshot(bool snapshotExists,
            bool worldLoaded)
        {
            return snapshotExists && worldLoaded;
        }

        public static bool ShouldRunFinalRebuild(bool worldLoaded,
            bool actorCallbacksComplete)
        {
            return worldLoaded && actorCallbacksComplete;
        }

        public static CityReservePoolFinalRestoreActions
            ResolveFinalRestoreActions(bool snapshotRestored,
                bool actorCallbacksComplete, bool formalWarActive)
        {
            if (snapshotRestored || !actorCallbacksComplete)
                return CityReservePoolFinalRestoreActions.None;
            CityReservePoolFinalRestoreActions actions =
                CityReservePoolFinalRestoreActions.ValidateMembers |
                CityReservePoolFinalRestoreActions.PopulateActorPool;
            if (formalWarActive)
                actions |= CityReservePoolFinalRestoreActions.
                    RestoreWarEmergency;
            return actions;
        }

        public static string ResolveSnapshotPath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return string.Empty;
            return Path.Combine(Path.GetFullPath(directory), SnapshotFileName);
        }
    }
}
