using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct RemainingTerritoryRevaluationPage
    {
        public RemainingTerritoryRevaluationPage(string pNextControlKey,
            bool pHasMore, int pProcessedCount, int pRemovedCount,
            int pRevaluedCount)
        {
            NextControlKey = pNextControlKey ?? "";
            HasMore = pHasMore;
            ProcessedCount = pProcessedCount;
            RemovedCount = pRemovedCount;
            RevaluedCount = pRevaluedCount;
        }

        public string NextControlKey { get; }
        public bool HasMore { get; }
        public int ProcessedCount { get; }
        public int RemovedCount { get; }
        public int RevaluedCount { get; }
    }

    internal static class WarRemainingTerritoryOrchestration
    {
        public const int RevaluationPageSize = 32;

        public static bool ApplyToEverySharedActiveWar<TWar>(
            IEnumerable<TWar> pWars, Func<TWar, long> pWarId,
            Func<TWar, bool> pIsSharedActive,
            Func<TWar, bool> pApply)
        {
            if (pWars == null || pWarId == null ||
                pIsSharedActive == null || pApply == null) return false;
            var seen = new HashSet<long>();
            bool changed = false;
            foreach (TWar war in pWars)
            {
                if (!pIsSharedActive(war)) continue;
                long warId = pWarId(war);
                if (warId < 0 || !seen.Add(warId)) continue;
                changed |= pApply(war);
            }
            return changed;
        }

        public static int ApplyPermanentTransfer<TWar, TOwner>(
            IEnumerable<TWar> pOldOwnerWars,
            IEnumerable<TWar> pNewOwnerWars,
            TOwner pOldOwner, TOwner pNewOwner,
            Func<TWar, long> pWarId, Func<TWar, bool> pIsActive,
            Func<TWar, TOwner, bool> pIsParticipant,
            Action<TWar, TOwner> pUpdate)
        {
            if (pWarId == null || pIsActive == null ||
                pIsParticipant == null || pUpdate == null) return 0;
            var affected = new Dictionary<long, TWar>();
            AddActiveWars(affected, pOldOwnerWars, pWarId, pIsActive);
            AddActiveWars(affected, pNewOwnerWars, pWarId, pIsActive);
            bool sameOwner = EqualityComparer<TOwner>.Default.Equals(
                pOldOwner, pNewOwner);
            int updates = 0;
            foreach (TWar war in affected.Values)
            {
                if (pIsParticipant(war, pOldOwner))
                {
                    pUpdate(war, pOldOwner);
                    updates++;
                }
                if (!sameOwner && pIsParticipant(war, pNewOwner))
                {
                    pUpdate(war, pNewOwner);
                    updates++;
                }
            }
            return updates;
        }

        public static RemainingTerritoryRevaluationPage
            ProcessRevaluationPage<TControl>(
                IReadOnlyList<TControl> pPage,
                Func<TControl, string> pControlKey,
                Func<TControl, bool> pOwnerStillOwns,
                Func<TControl, bool> pRemove,
                Func<TControl, bool> pRevalue)
        {
            if (pPage == null || pPage.Count == 0)
                return new RemainingTerritoryRevaluationPage("", false,
                    0, 0, 0);
            if (pPage.Count > RevaluationPageSize)
                throw new ArgumentOutOfRangeException(nameof(pPage));
            if (pControlKey == null || pOwnerStillOwns == null ||
                pRemove == null || pRevalue == null)
                throw new ArgumentNullException(nameof(pControlKey));

            int removed = 0;
            int revalued = 0;
            for (int i = 0; i < pPage.Count; i++)
            {
                TControl control = pPage[i];
                if (!pOwnerStillOwns(control))
                {
                    if (pRemove(control)) removed++;
                }
                else if (pRevalue(control))
                {
                    revalued++;
                }
            }
            return new RemainingTerritoryRevaluationPage(
                pControlKey(pPage[pPage.Count - 1]),
                pPage.Count == RevaluationPageSize, pPage.Count,
                removed, revalued);
        }

        public static bool ShouldRebaseOwnerChange(bool pFromLoad,
            bool pApplyingReplica)
        {
            return !pFromLoad && !pApplyingReplica;
        }

        private static void AddActiveWars<TWar>(
            IDictionary<long, TWar> pAffected, IEnumerable<TWar> pWars,
            Func<TWar, long> pWarId, Func<TWar, bool> pIsActive)
        {
            if (pWars == null) return;
            foreach (TWar war in pWars)
            {
                if (!pIsActive(war)) continue;
                long warId = pWarId(war);
                if (warId >= 0) pAffected[warId] = war;
            }
        }
    }
}
