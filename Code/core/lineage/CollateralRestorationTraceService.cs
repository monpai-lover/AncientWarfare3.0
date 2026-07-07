using System.Collections.Generic;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class CollateralRestorationTraceService
    {
        private const int MaxTraceDepth = 96;

        public static bool CanRestoreToLegitimateShi(Actor pActor, long pLegitimateLineage, long pLegitimateShi)
        {
            if (pActor?.data == null || pLegitimateLineage < 0 || pLegitimateShi < 0) return false;
            long actorId = pActor.data.id;
            long lineage = GetActorLineageId(actorId, pActor);
            if (lineage != pLegitimateLineage) return false;

            long shi = GetActorShiId(actorId, pActor);
            if (shi == pLegitimateShi) return true;
            if (shi < 0) return false;

            ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(shi);
            if (branch == null) return false;
            if (branch.lineage_id != pLegitimateLineage) return false;
            if (branch.source_type != ShiSourceType.KING_FOUNDED) return false;
            if (branch.founder_actor_id < 0) return false;
            if (!IsActorDescendedFromOrSelf(actorId, branch.founder_actor_id)) return false;

            return ActorOrAncestorBelongsToShi(branch.founder_actor_id, pLegitimateShi) ||
                   BranchFounderDescendsFromLegitimateFounder(branch.founder_actor_id, pLegitimateShi);
        }

        public static bool BelongsToLegitimateShi(Actor pActor, long pLegitimateShi)
        {
            if (pActor?.data == null || pLegitimateShi < 0) return false;
            return GetActorShiId(pActor.data.id, pActor) == pLegitimateShi;
        }

        private static bool BranchFounderDescendsFromLegitimateFounder(long pBranchFounderId, long pLegitimateShi)
        {
            ShiBranchInfo legitimate = LineageQuery.GetShiBranchInfo(pLegitimateShi);
            if (legitimate == null || legitimate.founder_actor_id < 0) return false;
            if (pBranchFounderId == legitimate.founder_actor_id) return true;
            return LineageQuery.GetAncestorPathToFounder(pBranchFounderId, legitimate.founder_actor_id).Count > 0;
        }

        private static bool IsActorDescendedFromOrSelf(long pActorId, long pFounderId)
        {
            if (pActorId < 0 || pFounderId < 0) return false;
            if (pActorId == pFounderId) return true;
            return LineageQuery.GetAncestorPathToFounder(pActorId, pFounderId).Count > 0;
        }

        private static bool ActorOrAncestorBelongsToShi(long pActorId, long pShiId)
        {
            if (pActorId < 0 || pShiId < 0) return false;

            var visited = new HashSet<long>();
            var queue = new Queue<(long id, int depth)>();
            queue.Enqueue((pActorId, 0));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.depth > MaxTraceDepth || !visited.Add(current.id)) continue;
                if (GetActorShiId(current.id, null) == pShiId) return true;

                foreach (long parentId in LineageQuery.GetParentIds(current.id))
                {
                    if (parentId >= 0) queue.Enqueue((parentId, current.depth + 1));
                }
            }

            return false;
        }

        private static long GetActorLineageId(long pActorId, Actor pLive)
        {
            if (pLive?.data != null)
            {
                pLive.data.get(LineageKeys.LINEAGE_ID, out long liveLineage, -1L);
                if (liveLineage >= 0) return liveLineage;
            }

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            return row?.lineage_id ?? -1L;
        }

        private static long GetActorShiId(long pActorId, Actor pLive)
        {
            if (pLive?.data != null)
            {
                pLive.data.get(LineageKeys.SHI_ID, out long liveShi, -1L);
                if (liveShi >= 0) return liveShi;
            }
            return LineageQuery.GetActorShiId(pActorId);
        }
    }
}
