using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyMembershipReconciliationService
    {
        private const int ArmiesPerFrame = 8;
        private const int DelayedRetriesPerFrame = 16;
        private const int MaxUnknownOwnerRetries = 6;
        private readonly struct DelayedRetry
        {
            public readonly Army Army;
            public readonly long ReadyFrame;

            public DelayedRetry(Army pArmy, long pReadyFrame)
            {
                Army = pArmy;
                ReadyFrame = pReadyFrame;
            }
        }

        private static readonly Queue<Army> Pending = new Queue<Army>();
        private static readonly HashSet<Army> Enqueued = new HashSet<Army>();
        private static readonly Queue<DelayedRetry> Delayed =
            new Queue<DelayedRetry>();
        private static readonly HashSet<Army> DelayedArmies =
            new HashSet<Army>();
        private static readonly Dictionary<Army, int> UnknownOwnerAttempts =
            new Dictionary<Army, int>();
        private static long _frame;

        public static void Enqueue(Army pArmy)
        {
            if (pArmy?.data == null) return;
            UnknownOwnerAttempts.Remove(pArmy);
            DelayedArmies.Remove(pArmy);
            EnqueueCore(pArmy);
        }

        private static void EnqueueCore(Army pArmy)
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
                {
                    if (army?.data == null) continue;
                    if (UnknownOwnerAttempts.TryGetValue(army,
                            out int attempts) &&
                        attempts >= MaxUnknownOwnerRetries) continue;
                    EnqueueCore(army);
                }
            }
            catch { }
        }

        public static void ProcessFrame()
        {
            if (!RuntimeStable()) return;
            if (_frame < long.MaxValue) _frame++;
            PromoteDelayedRetries();
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
            Delayed.Clear();
            DelayedArmies.Clear();
            UnknownOwnerAttempts.Clear();
            _frame = 0L;
        }

        internal static bool ReleaseForeignMember(Actor pActor,
            Army pArmy)
        {
            if (pArmy?.data == null) return false;
            bool changed = false;
            bool actorValid = pActor?.data != null;
            if (!actorValid)
            {
                try { return pArmy.units.Remove(pActor); }
                catch { return false; }
            }
            Army currentArmy = pActor.army;
            bool ownedByArmy = ReferenceEquals(currentArmy, pArmy);
            bool ownedByNewArmy = currentArmy?.data != null &&
                                  !ownedByArmy;
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
                    if (ReferenceEquals(pActor.army, pArmy))
                    {
                        Enqueue(pArmy);
                        return changed;
                    }
                    changed = true;
                }
                try
                {
                    if (pArmy.units.Remove(pActor)) changed = true;
                }
                catch { }
            }

            // A stale one-sided old-roster entry must not clear RTS or
            // deployment state owned by the actor's newer current army.
            if (ownedByNewArmy) return changed;
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
            bool ownerKnown = intendedKingdomId >= 0L;

            var units = new List<Actor>();
            try
            {
                foreach (Actor actor in pArmy.units)
                    units.Add(actor);
            }
            catch { return; }

            bool changed = false;
            bool ownerRetryRequired = false;
            for (int i = 0; i < units.Count; i++)
            {
                Actor actor = units[i];
                bool actorValid = actor?.data != null;
                bool backlinkMatches = actorValid &&
                                       ReferenceEquals(actor.army, pArmy);
                long actorKingdomId = actor?.kingdom?.id ?? -1L;
                ArmyMembershipOwnershipDecision decision =
                    ArmyMembershipOwnershipRules.Decide(true,
                        intendedKingdomId, actorKingdomId);
                if (!ownerKnown && actorValid && backlinkMatches)
                    ownerRetryRequired = true;
                if (ArmyMembershipOwnershipRules.ShouldReleaseRosterEntry(
                        actorValid, backlinkMatches, decision))
                    changed |= ReleaseForeignMember(actor, pArmy);
            }

            if (ownerKnown)
                UnknownOwnerAttempts.Remove(pArmy);
            else if (ownerRetryRequired)
                ScheduleUnknownOwnerRetry(pArmy);

            if (!changed) return;
            ArmyRtsControllerService.OnArmyRosterChanged(pArmy);
            ArmyStrategicIndexService.OnArmyRosterChanged(pArmy);
        }

        private static void ScheduleUnknownOwnerRetry(Army pArmy)
        {
            if (pArmy?.data == null || DelayedArmies.Contains(pArmy)) return;
            UnknownOwnerAttempts.TryGetValue(pArmy, out int attempts);
            attempts++;
            UnknownOwnerAttempts[pArmy] = attempts;
            if (attempts > MaxUnknownOwnerRetries) return;
            long readyFrame = _frame +
                ArmyMembershipOwnershipRules.UnknownOwnerRetryDelayFrames(
                    attempts);
            DelayedArmies.Add(pArmy);
            Delayed.Enqueue(new DelayedRetry(pArmy, readyFrame));
        }

        private static void PromoteDelayedRetries()
        {
            int scan = System.Math.Min(DelayedRetriesPerFrame,
                Delayed.Count);
            while (scan-- > 0 && Delayed.Count > 0)
            {
                DelayedRetry retry = Delayed.Dequeue();
                if (!DelayedArmies.Contains(retry.Army)) continue;
                if (retry.ReadyFrame > _frame)
                {
                    Delayed.Enqueue(retry);
                    continue;
                }
                DelayedArmies.Remove(retry.Army);
                EnqueueCore(retry.Army);
            }
        }

        private static bool RuntimeStable()
        {
            return Config.game_loaded && !SmoothLoader.isLoading() &&
                   World.world != null &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
