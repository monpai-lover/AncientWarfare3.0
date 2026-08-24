using System;

namespace AncientWarfare3.core.performance
{
    public readonly struct ArmyRtsPendingWork
    {
        public ArmyRtsPendingWork(int controllerArmies, int firstOrders,
            int replenishmentArrivals, int watchdogArmies,
            int successionRecoveries, int lifecycleDiscoveries,
            int assignmentReconciliations, int abstractBattles)
        {
            ControllerArmies = controllerArmies;
            FirstOrders = firstOrders;
            ReplenishmentArrivals = replenishmentArrivals;
            WatchdogArmies = watchdogArmies;
            SuccessionRecoveries = successionRecoveries;
            LifecycleDiscoveries = lifecycleDiscoveries;
            AssignmentReconciliations = assignmentReconciliations;
            AbstractBattles = abstractBattles;
        }

        public int ControllerArmies { get; }
        public int FirstOrders { get; }
        public int ReplenishmentArrivals { get; }
        public int WatchdogArmies { get; }
        public int SuccessionRecoveries { get; }
        public int LifecycleDiscoveries { get; }
        public int AssignmentReconciliations { get; }
        public int AbstractBattles { get; }
    }

    public readonly struct ArmyRtsExecutionBudget
    {
        public ArmyRtsExecutionBudget(int controllerArmies, int firstOrders,
            int replenishmentArrivals, int watchdogArmies,
            int successionRecoveries, int lifecycleDiscoveries,
            int assignmentReconciliations, int abstractBattles)
        {
            ControllerArmies = controllerArmies;
            FirstOrders = firstOrders;
            ReplenishmentArrivals = replenishmentArrivals;
            WatchdogArmies = watchdogArmies;
            SuccessionRecoveries = successionRecoveries;
            LifecycleDiscoveries = lifecycleDiscoveries;
            AssignmentReconciliations = assignmentReconciliations;
            AbstractBattles = abstractBattles;
        }

        public int ControllerArmies { get; }
        public int FirstOrders { get; }
        public int ReplenishmentArrivals { get; }
        public int WatchdogArmies { get; }
        public int SuccessionRecoveries { get; }
        public int LifecycleDiscoveries { get; }
        public int AssignmentReconciliations { get; }
        public int AbstractBattles { get; }
    }

    public static class ArmyRtsExecutionBudgetRules
    {
        public static ArmyRtsExecutionBudget Capture(
            AWSimulationMode pMode, ArmyRtsPendingWork pPending)
        {
            return new ArmyRtsExecutionBudget(
                // In large-step mode every active army must receive its
                // command refresh in the same authority cycle. A fixed 32
                // item cap starves the remainder and surfaces as awaiting
                // orders during an active war.
                ResolveSnapshotBudget(pMode, pPending.ControllerArmies, 32),
                ResolveSnapshotBudget(pMode, pPending.FirstOrders, 1),
                ResolveSnapshotBudget(pMode,
                    pPending.ReplenishmentArrivals, 4),
                ResolveSnapshotBudget(pMode, pPending.WatchdogArmies, 2),
                ResolveSnapshotBudget(pMode,
                    pPending.SuccessionRecoveries, 8),
                ResolveSnapshotBudget(pMode,
                    pPending.LifecycleDiscoveries, 8),
                ResolveSnapshotBudget(pMode,
                    pPending.AssignmentReconciliations, 8),
                ResolveSnapshotBudget(pMode, pPending.AbstractBattles, 4));
        }

        public static int ResolveSnapshotBudget(AWSimulationMode pMode,
            int pPending, int pNativeCap)
        {
            int pending = Math.Max(0, pPending);
            if (pMode == AWSimulationMode.Large) return pending;
            return Math.Min(pending, Math.Max(0, pNativeCap));
        }
    }
}
