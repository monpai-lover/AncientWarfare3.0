using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyInvalidCleanupQueue
    {
        private const int MaxMembershipDeferrals = 4;
        private const int PostLoadBatchSize = 16;

        private sealed class EmptyCleanupContext
        {
            public Army Army;
            public City CityHint;
            public Kingdom KingdomHint;
            public int Deferrals;
            public bool ForceShellCleanup;
        }

        private static readonly HashSet<Army> Pending = new HashSet<Army>();
        private static readonly Dictionary<Army, EmptyCleanupContext>
            PendingEmpty = new Dictionary<Army, EmptyCleanupContext>();
        private static readonly Queue<Army> PostLoadCandidates =
            new Queue<Army>();

        public static void ClearRuntime()
        {
            Pending.Clear();
            PendingEmpty.Clear();
            PostLoadCandidates.Clear();
        }

        public static void Schedule(Army pArmy)
        {
            if (pArmy == null || !Pending.Add(pArmy)) return;
            string key = "invalid_army:" + RuntimeHelpers.GetHashCode(pArmy);
            DeferredRuntimeWorkService.EnqueueCoalesced(key,
                DeferredWorkClass.Runtime, () => Remove(pArmy));
        }

        public static void ScheduleIfEmpty(Army pArmy, City pCityHint = null,
            Kingdom pKingdomHint = null)
        {
            ScheduleEmptyInspection(pArmy, pCityHint, pKingdomHint,
                pForceShellCleanup: false);
        }

        public static void ScheduleShell(Army pArmy, City pCityHint = null,
            Kingdom pKingdomHint = null)
        {
            MarkNonReplacingShell(pArmy);
            ScheduleEmptyInspection(pArmy, pCityHint, pKingdomHint,
                pForceShellCleanup: true);
        }

        private static void MarkNonReplacingShell(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.set(LineageKeys.AW_ARMY_NON_REPLACING_SHELL, true);
            ArmyFieldIndexService.OnArmyChanged(pArmy);
        }

        private static void ScheduleEmptyInspection(Army pArmy,
            City pCityHint, Kingdom pKingdomHint,
            bool pForceShellCleanup)
        {
            if (pArmy == null) return;
            if (PendingEmpty.TryGetValue(pArmy,
                    out EmptyCleanupContext existing))
            {
                if (existing.CityHint?.data == null && pCityHint?.data != null)
                    existing.CityHint = pCityHint;
                if (existing.KingdomHint?.data == null &&
                    pKingdomHint?.data != null)
                    existing.KingdomHint = pKingdomHint;
                existing.ForceShellCleanup |= pForceShellCleanup;
                return;
            }

            var context = new EmptyCleanupContext
            {
                Army = pArmy,
                CityHint = pCityHint,
                KingdomHint = pKingdomHint,
                ForceShellCleanup = pForceShellCleanup
            };
            PendingEmpty[pArmy] = context;
            EnqueueEmptyCheck(context);
        }

        public static void BeginPostLoadSweep(IEnumerable<Army> pSnapshot)
        {
            PostLoadCandidates.Clear();
            if (pSnapshot != null)
            {
                foreach (Army army in pSnapshot)
                    if (army != null)
                        PostLoadCandidates.Enqueue(army);
            }
            SchedulePostLoadBatch();
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

        private static void EnqueueEmptyCheck(EmptyCleanupContext pContext)
        {
            if (pContext?.Army == null) return;
            string key = "empty_army:" +
                         RuntimeHelpers.GetHashCode(pContext.Army);
            DeferredRuntimeWorkService.EnqueueCoalesced(key,
                DeferredWorkClass.Runtime, () => InspectEmpty(pContext.Army));
        }

        private static void InspectEmpty(Army pArmy)
        {
            if (pArmy == null || !PendingEmpty.TryGetValue(pArmy,
                    out EmptyCleanupContext context)) return;
            bool dirty = false;
            try { dirty = pArmy.isDirtyUnits(); }
            catch { }
            if (ArmyLifecycleRules.ShouldDeferEmptyCheck(dirty,
                    context.Deferrals, MaxMembershipDeferrals))
            {
                context.Deferrals++;
                EnqueueEmptyCheck(context);
                return;
            }

            try
            {
                if (context.ForceShellCleanup)
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
                else
                {
                    AWArmyService.TryRemoveEmptyArmy(pArmy,
                        context.CityHint, context.KingdomHint);
                }
            }
            finally
            {
                PendingEmpty.Remove(pArmy);
            }
        }

        private static void SchedulePostLoadBatch()
        {
            if (PostLoadCandidates.Count == 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "empty_army_post_load_sweep", DeferredWorkClass.Runtime,
                ProcessPostLoadBatch);
        }

        private static void ProcessPostLoadBatch()
        {
            int remaining = PostLoadBatchSize;
            while (remaining-- > 0 && PostLoadCandidates.Count > 0)
            {
                Army army = PostLoadCandidates.Dequeue();
                if (AWArmyService.IsNonReplacingShell(army))
                    ScheduleShell(army);
                else
                    ScheduleIfEmpty(army);
            }
            SchedulePostLoadBatch();
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
