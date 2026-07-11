using System;
using AncientWarfare3.core.lineage;

namespace AW3FocusedRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectDeferredWorkRules();
            ExpectCaptureScanRules();
            ExpectFillSideEffectRules();
            Console.WriteLine("AW3 focused rule tests passed.");
            return 0;
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
    }
}
