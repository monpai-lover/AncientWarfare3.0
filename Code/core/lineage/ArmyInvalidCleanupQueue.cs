using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyInvalidCleanupQueue
    {
        private const int MaxMembershipDeferrals = 4;

        private sealed class ShellCleanupContext
        {
            public Army Army;
            public City CityHint;
            public Kingdom KingdomHint;
            public int Deferrals;
        }

        private static readonly HashSet<Army> Pending = new HashSet<Army>();
        private static readonly Dictionary<Army, ShellCleanupContext>
            PendingShell = new Dictionary<Army, ShellCleanupContext>();
        public static void ClearRuntime()
        {
            Pending.Clear();
            PendingShell.Clear();
        }

        public static void Schedule(Army pArmy)
        {
            if (pArmy == null || !Pending.Add(pArmy)) return;
            string key = "invalid_army:" + RuntimeHelpers.GetHashCode(pArmy);
            DeferredRuntimeWorkService.EnqueueCoalesced(key,
                DeferredWorkClass.Runtime, () => Remove(pArmy));
        }

        public static void ScheduleShell(Army pArmy, City pCityHint = null,
            Kingdom pKingdomHint = null)
        {
            MarkNonReplacingShell(pArmy);
            ScheduleShellInspection(pArmy, pCityHint, pKingdomHint);
        }

        private static void MarkNonReplacingShell(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.set(LineageKeys.AW_ARMY_NON_REPLACING_SHELL, true);
            ArmyFieldIndexService.OnArmyChanged(pArmy);
        }

        private static void ScheduleShellInspection(Army pArmy,
            City pCityHint, Kingdom pKingdomHint)
        {
            if (pArmy == null) return;
            if (PendingShell.TryGetValue(pArmy,
                    out ShellCleanupContext existing))
            {
                if (existing.CityHint?.data == null && pCityHint?.data != null)
                    existing.CityHint = pCityHint;
                if (existing.KingdomHint?.data == null &&
                    pKingdomHint?.data != null)
                    existing.KingdomHint = pKingdomHint;
                return;
            }

            var context = new ShellCleanupContext
            {
                Army = pArmy,
                CityHint = pCityHint,
                KingdomHint = pKingdomHint
            };
            PendingShell[pArmy] = context;
            EnqueueShellCheck(context);
        }

        public static void BeginPostLoadSweep(IEnumerable<Army> pSnapshot)
        {
            // Vanilla performs the empty-army existence check after loading.
            // Do not mirror that sweep in an AW3 deferred queue.
        }

        public static void RemoveFailedCreation(Army pArmy, Actor pCaptain,
            City pCity)
        {
            if (pArmy == null) return;
            using (ArmyCaptainDisposalScope.Open(pArmy))
            {
                try
                {
                    if (pCaptain?.army == pArmy) pCaptain.setArmy(null);
                }
                catch { }
                try
                {
                    if (pCity?.getArmy() == pArmy) pCity.setArmy(null);
                }
                catch { }
                try { pArmy.setCaptain(null); } catch { }
                ScheduleShell(pArmy, pCity, pCity?.kingdom);
            }
        }

        private static void EnqueueShellCheck(ShellCleanupContext pContext)
        {
            if (pContext?.Army == null) return;
            string key = "invalid_army_shell:" +
                         RuntimeHelpers.GetHashCode(pContext.Army);
            DeferredRuntimeWorkService.EnqueueCoalesced(key,
                DeferredWorkClass.Runtime, () => InspectShell(pContext.Army));
        }

        private static void InspectShell(Army pArmy)
        {
            if (pArmy == null || !PendingShell.TryGetValue(pArmy,
                    out ShellCleanupContext context)) return;
            bool dirty = false;
            try { dirty = pArmy.isDirtyUnits(); }
            catch { }
            if (ArmyLifecycleRules.ShouldDeferEmptyCheck(dirty,
                    context.Deferrals, MaxMembershipDeferrals))
            {
                context.Deferrals++;
                EnqueueShellCheck(context);
                return;
            }

            try
            {
                int listedCount = 0;
                try { listedCount = pArmy.units?.Count ?? 0; }
                catch { }
                if (ArmyLifecycleRules.
                        ShouldQueueArmyShellForCleanup(listedCount))
                    AWArmyService.RemoveArmyObject(pArmy,
                        pClearCityReference: true, context.CityHint,
                        context.KingdomHint,
                        pRequestReplacement: false);
            }
            finally
            {
                PendingShell.Remove(pArmy);
            }
        }

        private static void Remove(Army pArmy)
        {
            Pending.Remove(pArmy);
            if (pArmy == null) return;
            ArmyManager manager = World.world?.armies;
            if (manager == null) return;
            if (pArmy.data == null)
            {
                try { manager.checkLists(); } catch { }
                return;
            }
            AWArmyService.RemoveArmyObject(pArmy,
                pClearCityReference: true);
        }
    }
}
