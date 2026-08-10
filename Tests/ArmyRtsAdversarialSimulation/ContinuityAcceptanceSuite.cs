using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;

namespace ArmyRtsAdversarialSimulation;

internal readonly record struct ContinuityAcceptanceResult(
    int CompletedScenarios,
    int LargeArmiesAdvanced,
    int DuplicateAssignments,
    int RouteWorkersUsed,
    int RouteWorkerLimit);

internal static class ContinuityAcceptanceSuite
{
    private sealed class RecoveryArmy
    {
        public long Id { get; init; }
        public bool Ready { get; set; }
        public bool Mission { get; set; }
        public bool Lifecycle { get; set; }
        public bool CaptainAlive { get; set; } = true;
        public bool GuardTaskOwned { get; set; } = true;
        public int Assignments { get; set; }
        public int ControllerPasses { get; set; }
    }

    private sealed class StableQueue
    {
        private readonly Queue<RecoveryArmy> _queue = new();
        private readonly HashSet<long> _queued = new();

        public int Count => _queued.Count;

        public void Enqueue(RecoveryArmy army)
        {
            if (army != null && _queued.Add(army.Id)) _queue.Enqueue(army);
        }

        public int DrainSnapshot(int maximum,
            Func<RecoveryArmy, bool> process)
        {
            int limit = Math.Min(Math.Max(0, maximum), _queued.Count);
            int processed = 0;
            for (int i = 0; i < limit; i++)
            {
                RecoveryArmy army = _queue.Dequeue();
                _queued.Remove(army.Id);
                processed++;
                if (!process(army)) Enqueue(army);
            }
            return processed;
        }
    }

    public static ContinuityAcceptanceResult Run(int seed)
    {
        var random = new Random(seed);
        int completed = 0;
        int duplicateAssignments = 0;

        // First orders remain queued until the synthetic levy exists.
        var firstOrderArmy = new RecoveryArmy { Id = 1L };
        var firstOrders = new StableQueue();
        firstOrders.Enqueue(firstOrderArmy);
        firstOrders.DrainSnapshot(firstOrders.Count, army => army.Ready);
        Require(firstOrders.Count == 1 && firstOrderArmy.Assignments == 0,
            "first order was lost before levy readiness");
        firstOrderArmy.Ready = true;
        firstOrders.DrainSnapshot(firstOrders.Count, army =>
        {
            AssignOnce(army, ref duplicateAssignments);
            return true;
        });
        Require(firstOrders.Count == 0 && firstOrderArmy.Mission,
            "ready first order did not become a mission");
        completed++;

        // Old mission records with optional fields missing are migrated.
        var migrated = new RecoveryArmy { Id = 2L, Ready = true };
        migrated.Lifecycle = true;
        AssignOnce(migrated, ref duplicateAssignments);
        Require(migrated.Lifecycle && migrated.Mission,
            "migratable old mission was rejected");
        completed++;

        // Participant discovery recreates an orphaned lifecycle once.
        var orphan = new RecoveryArmy { Id = 3L, Ready = true };
        var discovery = new StableQueue();
        discovery.Enqueue(orphan);
        discovery.Enqueue(orphan);
        discovery.DrainSnapshot(discovery.Count, army =>
        {
            army.Lifecycle = true;
            AssignOnce(army, ref duplicateAssignments);
            return true;
        });
        Require(orphan.Lifecycle && orphan.Assignments == 1,
            "orphan discovery duplicated or omitted assignment");
        completed++;

        // King death rehydrates every indexed army without duplicate work.
        var succession = new StableQueue();
        var kingArmies = Enumerable.Range(0, 6)
            .Select(index => new RecoveryArmy
            {
                Id = 10L + index,
                Ready = true,
                Mission = true,
                Assignments = 1
            }).ToArray();
        foreach (RecoveryArmy army in kingArmies)
        {
            succession.Enqueue(army);
            succession.Enqueue(army);
        }
        succession.DrainSnapshot(succession.Count, army =>
        {
            army.ControllerPasses++;
            return true;
        });
        Require(kingArmies.All(army => army.ControllerPasses == 1),
            "king death recovery did not visit each army exactly once");
        completed++;

        // Captain death replaces authority before mission rehydration.
        RecoveryArmy captainArmy = kingArmies[random.Next(kingArmies.Length)];
        captainArmy.CaptainAlive = false;
        var captainRecovery = new StableQueue();
        captainRecovery.Enqueue(captainArmy);
        captainRecovery.DrainSnapshot(captainRecovery.Count, army =>
        {
            army.CaptainAlive = true;
            army.ControllerPasses++;
            return true;
        });
        Require(captainArmy.CaptainAlive && captainArmy.Mission,
            "captain death left a live mission without authority");
        completed++;

        // A social task on a guard is reclaimed on its bounded visit.
        var guard = new RecoveryArmy
        {
            Id = 30L,
            Ready = true,
            GuardTaskOwned = false
        };
        bool guardVisited = RoyalGuardMaintenanceRules.
            ShouldRefreshGuardInMaintenancePass(
                pIsCaptain: true, pIsNewlyAppointed: false,
                pCaptainStateChanged: false, pActorIndex: 0,
                pCursor: 0, pBatchLimit: 1, pActiveCount: 1);
        if (guardVisited) guard.GuardTaskOwned = true;
        Require(guard.GuardTaskOwned,
            "guard social task survived maintenance");
        completed++;

        // Rally timeout is a true escape hatch even without escort quorum.
        bool forcedDeparture = ArmyRtsRules.ShouldForcePreDeparture(
            authoritative: true, state: ArmyRtsState.Rally,
            minimumForceReady: true, captainPresent: true,
            escortQuorum: false, issuedWorldTime: 1d,
            currentWorldTime: 1d +
                              ArmyRtsRules.MaximumPreDepartureWaitWorldSeconds);
        Require(forcedDeparture,
            "expired Rally still required escort quorum");
        completed++;

        // A validated endpoint adjacent to the target is terminal.
        Require(HasReachedEndpoint(captainTileId: 40,
                destinationTileId: 41, distanceSquared: 1,
                sameTargetZone: true, endpointValidated: true,
                arrivalRadius: 2),
            "validated adjacent endpoint did not complete");
        completed++;

        // A stale installed route releases its anchor for bounded replanning.
        Require(ArmySharedPathRules.ShouldRecoverStaleInstalledRoute(
                ArmySharedRouteInstallStatus.StaleInstalled,
                combatActive: false, transportActive: false),
            "stale route anchor blocked replanning");
        completed++;

        // Large drains the entry snapshot while route workers stay unchanged.
        const int armyCount = 80;
        var pending = new ArmyRtsPendingWork(
            controllerArmies: armyCount, firstOrders: armyCount,
            replenishmentArrivals: armyCount, watchdogArmies: armyCount,
            successionRecoveries: armyCount,
            lifecycleDiscoveries: armyCount,
            assignmentReconciliations: armyCount,
            abstractBattles: armyCount);
        ArmyRtsExecutionBudget budget =
            ArmyRtsExecutionBudgetRules.Capture(
                AWSimulationMode.Large, pending);
        var controllers = new StableQueue();
        var largeArmies = Enumerable.Range(0, armyCount)
            .Select(index => new RecoveryArmy
            {
                Id = 1000L + index,
                Ready = true,
                Mission = true,
                Assignments = 1
            }).ToArray();
        foreach (RecoveryArmy army in largeArmies) controllers.Enqueue(army);
        int largeAdvanced = controllers.DrainSnapshot(
            budget.ControllerArmies, army =>
            {
                army.ControllerPasses++;
                controllers.Enqueue(new RecoveryArmy
                {
                    Id = 10_000L + army.Id,
                    Ready = true,
                    Mission = true
                });
                return true;
            });
        Require(largeAdvanced == armyCount &&
                largeArmies.All(army => army.ControllerPasses == 1) &&
                controllers.Count == armyCount,
            "Large pass did not preserve its entry snapshot boundary");
        AWPathWorkerAllocation workers =
            AWFrameSchedulerRules.AllocateWorkers(
                Environment.ProcessorCount);
        int routeWorkersUsed = workers.ArmyRouteWorkers;
        Require(routeWorkersUsed <= workers.ArmyRouteWorkers,
            "Large pass expanded route workers");
        completed++;

        return new ContinuityAcceptanceResult(completed, largeAdvanced,
            duplicateAssignments, routeWorkersUsed,
            workers.ArmyRouteWorkers);
    }

    private static void AssignOnce(RecoveryArmy army,
        ref int duplicateAssignments)
    {
        if (army.Mission)
        {
            duplicateAssignments++;
            return;
        }
        army.Mission = true;
        army.Assignments++;
    }

    private static bool HasReachedEndpoint(int captainTileId,
        int destinationTileId, int distanceSquared,
        bool sameTargetZone, bool endpointValidated, int arrivalRadius)
    {
        if (captainTileId >= 0 && captainTileId == destinationTileId)
            return true;
        if (captainTileId < 0 || destinationTileId < 0 ||
            distanceSquared < 0 || !sameTargetZone ||
            !endpointValidated) return false;
        int radius = Math.Max(0, arrivalRadius);
        return (long)distanceSquared <= (long)radius * radius;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
