using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyMembershipReconciliationService
    {
        private const int ArmiesPerFrame = 8;
        private static readonly Queue<Army> Pending = new Queue<Army>();
        private static readonly HashSet<Army> Enqueued = new HashSet<Army>();

        public static void Enqueue(Army pArmy)
        {
            if (pArmy?.data == null || !Enqueued.Add(pArmy)) return;
            Pending.Enqueue(pArmy);
        }

        public static void EnqueueAll(ArmyManager pManager)
        {
            if (pManager == null) return;
            try
            {
                foreach (Army army in pManager)
                    Enqueue(army);
            }
            catch { }
        }

        public static void ProcessFrame()
        {
            if (!RuntimeStable()) return;
            int budget = System.Math.Min(ArmiesPerFrame, Pending.Count);
            for (int i = 0; i < budget; i++)
            {
                Army army = Pending.Dequeue();
                Enqueued.Remove(army);
                Reconcile(army);
            }
        }

        public static void ClearRuntime()
        {
            Pending.Clear();
            Enqueued.Clear();
        }

        internal static bool ReleaseForeignMember(Actor pActor,
            Army pArmy)
        {
            if (pActor?.data == null || pArmy?.data == null) return false;
            bool changed = false;
            bool ownedByArmy = ReferenceEquals(pActor.army, pArmy);
            using (ArmyCaptainDisposalScope.Open(pArmy))
            {
                try
                {
                    if (ReferenceEquals(pArmy.getCaptain(), pActor))
                    {
                        pArmy.setCaptain(null);
                        changed = true;
                    }
                }
                catch { }

                if (ownedByArmy)
                {
                    try { pActor.removeFromArmy(); }
                    catch
                    {
                        try { pActor.setArmy(null); }
                        catch { }
                    }
                    changed |= !ReferenceEquals(pActor.army, pArmy);
                }
                try
                {
                    if (pArmy.units.Remove(pActor)) changed = true;
                }
                catch { }
            }

            // A stale one-sided old-roster entry must not clear RTS or
            // deployment state owned by the actor's newer current army.
            if (!ownedByArmy) return changed;
            ArmyRtsControllerService.ReleaseActor(pActor);
            ArmyDeploymentService.ReleaseActor(pActor, restoreJob: true);
            TemporaryLevyService.OnActorInvalidated(pActor);
            WartimeGarrisonService.OnActorInvalidated(pActor);
            MandateMilitaryPhaseService.Clear(pActor);
            return changed;
        }

        private static void Reconcile(Army pArmy)
        {
            if (pArmy?.data == null) return;
            bool alive;
            try { alive = pArmy.isAlive(); }
            catch { return; }
            if (!alive) return;

            Kingdom intended = AWArmyService.GetIntendedKingdom(pArmy);
            long intendedKingdomId = intended?.id ?? -1L;
            if (ArmyMembershipOwnershipRules.Decide(true,
                    intendedKingdomId, intendedKingdomId) ==
                ArmyMembershipOwnershipDecision.Defer)
            {
                Enqueue(pArmy);
                return;
            }

            var units = new List<Actor>();
            try
            {
                foreach (Actor actor in pArmy.units)
                    units.Add(actor);
            }
            catch { return; }

            bool changed = false;
            for (int i = 0; i < units.Count; i++)
            {
                Actor actor = units[i];
                long actorKingdomId = actor?.kingdom?.id ?? -1L;
                ArmyMembershipOwnershipDecision decision =
                    ArmyMembershipOwnershipRules.Decide(true,
                        intendedKingdomId, actorKingdomId);
                if (decision == ArmyMembershipOwnershipDecision.Release)
                    changed |= ReleaseForeignMember(actor, pArmy);
            }

            if (!changed) return;
            ArmyRtsControllerService.OnArmyRosterChanged(pArmy);
            ArmyStrategicIndexService.OnArmyRosterChanged(pArmy);
        }

        private static bool RuntimeStable()
        {
            return Config.game_loaded && !SmoothLoader.isLoading() &&
                   World.world != null &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
