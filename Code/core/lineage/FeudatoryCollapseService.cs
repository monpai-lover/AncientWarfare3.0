using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class FeudatoryCollapseService
    {
        public static void ScheduleOnMandateCollapse(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            IReadOnlyList<FeudatorySnapshot> rows =
                FeudatoryService.GetByKingdom(pKingdom.id);
            if (rows.Count == 0) return;

            var ids = new long[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                ids[i] = rows[i].FeudatoryId;
            Array.Sort(ids);
            for (int i = 0; i < ids.Length; i++)
                Schedule(ids[i], pKingdom.id, pAttempt: 0);
        }

        private static void Schedule(long pFeudatoryId, long pParentKingdomId,
            int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("mandate_collapse_feudatory", pFeudatoryId),
                DeferredWorkClass.Runtime,
                () => Process(pFeudatoryId, pParentKingdomId, pAttempt));
        }

        private static void Process(long pFeudatoryId, long pParentKingdomId,
            int pAttempt)
        {
            if (!FeudatoryService.TryGet(pFeudatoryId,
                    out FeudatorySnapshot snapshot) ||
                snapshot.EmpireKingdomId != pParentKingdomId)
                return;
            if (FeudatoryJingnanService.TryActivateForMandateCollapse(
                    pFeudatoryId, out _))
                return;

            int nextAttempt = pAttempt + 1;
            if (MandateFeudatoryCompletionRules.ShouldRetryCollapse(nextAttempt))
                Schedule(pFeudatoryId, pParentKingdomId, nextAttempt);
        }
    }
}
