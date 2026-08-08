using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    // AW3 does not port Cultiway's status-target index. These hooks preserve
    // the spatial membership lifecycle without adding an unused ECS index.
    internal static class AWNearbyStatusTargetIndex
    {
        internal static void BeginUnitMembershipRebuild() { }

        internal static void AbortUnitMembershipRebuild() { }

        internal static void NotifyUnitMembershipRebuilt(int pVersion,
            bool pFusedIndexPrepared) { }

        internal static bool TryApplyChunkMembershipChanges(
            int pPreviousVersion, int pNextVersion,
            IReadOnlyList<int> pDirtyChunks, MapChunk[] pChunks)
        {
            return true;
        }

        internal static bool ShouldAddUnitMembership(Actor pActor)
        {
            return false;
        }

        internal static void AddUnitMembership(Actor pActor,
            MapChunk pChunk, int pUnitIndex) { }
    }
}
