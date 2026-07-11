using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.core.lineage;

namespace AW3FocusedRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectDeferredWorkRules();
            ExpectDeferredWorkQueue();
            ExpectCaptureScanRules();
            ExpectFillSideEffectRules();
            ExpectRepublicTerminology();
            ExpectXiaAllianceNaming();
            Console.WriteLine("AW3 focused rule tests passed.");
            return 0;
        }

        private static void ExpectDeferredWorkQueue()
        {
            DeferredRuntimeWorkService.ClearRuntimeState();
            var values = new List<int>();
            DeferredRuntimeWorkService.EnqueueCoalesced("actor:1", DeferredWorkClass.Persistent,
                () => values.Add(1));
            DeferredRuntimeWorkService.EnqueueCoalesced("actor:1", DeferredWorkClass.Persistent,
                () => values.Add(2));
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () => values.Add(3));
            DeferredRuntimeWorkService.DrainFrame(1000, 2);
            if (values.Count != 2 || values[0] != 2 || values[1] != 3)
                throw new Exception("Coalesced work must keep the latest action without reordering ordered work.");

            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Runtime, () => values.Add(4));
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () => values.Add(5));
            DeferredRuntimeWorkService.FlushPersistent();
            if (values.Count != 3 || values[2] != 5 || DeferredRuntimeWorkService.PendingCount != 1)
                throw new Exception("Save flushing must execute persistent work and retain runtime-only work.");

            DeferredRuntimeWorkService.DrainFrame(1000, 1);
            if (values.Count != 4 || values[3] != 4 || DeferredRuntimeWorkService.PendingCount != 0)
                throw new Exception("The retained runtime item must execute on a later frame.");
        }

        private static void ExpectDeferredWorkRules()
        {
            if (!DeferredRuntimeWorkRules.ShouldStopDrain(1, 1, 0, 10))
                throw new Exception("The item budget must stop a drain.");
            if (!DeferredRuntimeWorkRules.ShouldStopDrain(0, 1, 11, 10))
                throw new Exception("The elapsed budget must stop a drain.");
            if (DeferredRuntimeWorkRules.ShouldStopDrain(0, 1, 9, 10))
                throw new Exception("Available budget must allow work.");
            if (!DeferredRuntimeWorkRules.ShouldRetry(1, 2) ||
                DeferredRuntimeWorkRules.ShouldRetry(2, 2))
                throw new Exception("Retry count must be bounded.");
            if (DeferredRuntimeWorkRules.CoalescingKey("guard_state", 42) != "guard_state:42")
                throw new Exception("Coalescing keys must be stable.");
        }

        private static void ExpectCaptureScanRules()
        {
            if (SlaveCaptureScanRules.ChunkRadius(80, 16) != 5 ||
                SlaveCaptureScanRules.ChunkCount(5) != 121)
                throw new Exception("An 80-tile search must cover 121 candidate chunks.");
            SlaveCaptureScanRules.OffsetForIndex(0, 5, out int x0, out int y0);
            SlaveCaptureScanRules.OffsetForIndex(120, 5, out int x1, out int y1);
            if (x0 != -5 || y0 != -5 || x1 != 5 || y1 != 5)
                throw new Exception("Chunk cursor endpoints are incorrect.");
            if (!SlaveCaptureScanRules.ShouldReuseResult(true, true, true, true, 5, 10) ||
                SlaveCaptureScanRules.ShouldReuseResult(true, false, true, true, 5, 10))
                throw new Exception("Cached targets must be fully revalidated.");
            if (!SlaveCaptureScanRules.ShouldPause(128, 128, 0, 10) ||
                !SlaveCaptureScanRules.ShouldPause(0, 128, 11, 10))
                throw new Exception("Unit and elapsed budgets must both stop scanning.");
        }

        private static void ExpectFillSideEffectRules()
        {
            if (!SlaveArmyFillSideEffectRules.ShouldDeferPerActorSideEffects(true, true) ||
                SlaveArmyFillSideEffectRules.ShouldDeferPerActorSideEffects(false, true))
                throw new Exception("Only slave promotions inside a fill batch are deferred.");
            if (!SlaveArmyFillSideEffectRules.ShouldRefreshArmyOnce(2) ||
                SlaveArmyFillSideEffectRules.ShouldRefreshArmyOnce(0))
                throw new Exception("A changed fill batch refreshes its army exactly once.");
        }

        private static void ExpectRepublicTerminology()
        {
            if (GovernmentTitleRules.RulerKey(true) != "aw_republic_head" ||
                GovernmentTitleRules.RulerKey(false) != "aw_label_king")
                throw new Exception("Republic ruler labels must override monarchy labels.");
            if (GovernmentTitleRules.SuccessorKey(true, true) != "aw_republic_elder" ||
                GovernmentTitleRules.SuccessorKey(false, true) != HeirTitleRules.TaiziKey ||
                GovernmentTitleRules.SuccessorKey(false, false) != HeirTitleRules.ShiziKey)
                throw new Exception("Republic must take precedence over Mandate succession titles.");
            if (GovernmentTitleRules.RoleSnapshot(true, true, false, false) != "republic_head" ||
                GovernmentTitleRules.RoleSnapshot(true, false, true, false) != "republic_elder" ||
                GovernmentTitleRules.RoleSnapshot(false, true, false, false) != "king")
                throw new Exception("History role snapshots must freeze event-time government.");
            if (GovernmentTitleRules.BuildSocialTitle("\u9f50", true, false) != "\u9f50 \u5143\u9996" ||
                GovernmentTitleRules.BuildSocialTitle("\u9f50", false, true) != "\u9f50 \u5143\u8001")
                throw new Exception("Republic social titles are incorrect.");
            if (!GovernmentTitleRules.IsRepublicSocialTitle("\u9f50 \u5143\u9996") ||
                !GovernmentTitleRules.IsRepublicSocialTitle("\u5143\u8001") ||
                GovernmentTitleRules.IsRepublicSocialTitle("\u9f50 \u56fd\u738b") ||
                GovernmentTitleRules.IsRepublicSocialTitle("\u5143\u8001\u9662"))
                throw new Exception("Republic social-title detection must match exact role suffixes.");
        }

        private static void ExpectXiaAllianceNaming()
        {
            if (!XiaAllianceNamingRules.ShouldUseXiaName(true, false) ||
                !XiaAllianceNamingRules.ShouldUseXiaName(false, true) ||
                !XiaAllianceNamingRules.ShouldUseXiaName(true, true) ||
                XiaAllianceNamingRules.ShouldUseXiaName(false, false))
                throw new Exception("Either Xia founder must activate Xia alliance naming.");
            if (XiaAllianceNamingRules.ShouldRenameAfterCreation(false, true) ||
                !XiaAllianceNamingRules.ShouldRenameAfterCreation(true, true))
                throw new Exception("Naming runs once and only with a valid generated name.");
        }
    }
}
