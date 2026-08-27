using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarriorArmyMembershipService
    {
        private static readonly Queue<Actor> PendingActors =
            new Queue<Actor>();
        private static readonly HashSet<Actor> EnqueuedActors =
            new HashSet<Actor>();
        private static readonly HashSet<Actor> WaitingForArmy =
            new HashSet<Actor>();
        private static readonly Dictionary<long, HashSet<Army>>
            AvailableArmiesByKingdom = new Dictionary<long, HashSet<Army>>();

        public static void Enqueue(Actor pActor)
        {
            if (pActor?.data == null || !EnqueuedActors.Add(pActor)) return;
            try { RegisterArmy(pActor.army); }
            catch { }
            PendingActors.Enqueue(pActor);
        }

        public static void NotifyArmyAvailable(Army pArmy)
        {
            RegisterArmy(pArmy);
            if (pArmy?.data == null) return;
            var waiting = new List<Actor>(WaitingForArmy);
            WaitingForArmy.Clear();
            for (int i = 0; i < waiting.Count; i++) Enqueue(waiting[i]);
        }

        // Save restore is the only bounded full rebuild. Normal gameplay is
        // maintained by the newArmy, actor, and setArmy lifecycle hooks.
        public static void RebuildAfterLoad(IEnumerable<Army> pArmies)
        {
            if (pArmies == null) return;
            foreach (Army army in pArmies) RegisterArmy(army);
        }

        public static void ProcessAuthorityCycle()
        {
            if (!RuntimeStable()) return;
            int budget = WarriorArmyMembershipRules.ResolveActorBudget(
                PendingActors.Count);
            if (budget <= 0) return;
            for (int i = 0; i < budget; i++)
            {
                Actor actor = PendingActors.Dequeue();
                EnqueuedActors.Remove(actor);
                TryRepair(actor);
            }
        }

        public static void ClearRuntime()
        {
            PendingActors.Clear();
            EnqueuedActors.Clear();
            WaitingForArmy.Clear();
            AvailableArmiesByKingdom.Clear();
        }

        private static void TryRepair(Actor pActor)
        {
            if (pActor?.data == null) return;
            bool alive = false;
            bool warrior = false;
            Army current = null;
            try
            {
                alive = pActor.isAlive() && !pActor.isRekt();
                warrior = pActor.isWarrior();
                current = pActor.army;
            }
            catch { }
            Kingdom kingdom = null;
            try { kingdom = pActor.kingdom; }
            catch { }
            if (!WarriorArmyMembershipRules.ShouldReconcileActor(
                    actorValid: true, alive: alive, warrior: warrior,
                    hasArmy: HasArmyMembership(pActor),
                    hasKingdom: kingdom?.data != null)) return;

            long kingdomId = kingdom.id;
            Army target = null;
            if (current?.data != null &&
                WarriorArmyMembershipRules.IsEligibleTargetArmy(
                    armyValid: true, alive: IsLiveArmy(current),
                    ordinaryArmy: ArmyNativeNameService.IsOrdinaryArmy(current),
                    actorKingdomId: kingdomId,
                    armyKingdomId: AWArmyService.GetIntendedKingdom(current)?.id ?? -1L))
                target = current;
            else
                target = FindAvailableArmy(kingdomId);

            if (target == null)
            {
                WaitingForArmy.Add(pActor);
                return;
            }

            AWArmyService.AddToArmy(pActor, target);
            try
            {
                if (ReferenceEquals(pActor.army, target) &&
                    !target.units.Contains(pActor))
                    target.listUnit(pActor);
            }
            catch { }
        }

        public static void NotifyActorArmyChanged(Actor pActor)
        {
            if (pActor?.data == null) return;
            try { RegisterArmy(pActor.army); }
            catch { }
        }

        private static void RegisterArmy(Army pArmy)
        {
            if (pArmy?.data == null || !IsLiveArmy(pArmy) ||
                !ArmyNativeNameService.IsOrdinaryArmy(pArmy)) return;
            Kingdom owner = null;
            try { owner = AWArmyService.GetIntendedKingdom(pArmy); }
            catch { }
            if (owner?.data == null) return;
            if (!AvailableArmiesByKingdom.TryGetValue(owner.id,
                    out HashSet<Army> armies))
            {
                armies = new HashSet<Army>();
                AvailableArmiesByKingdom.Add(owner.id, armies);
            }
            armies.Add(pArmy);
        }

        private static Army FindAvailableArmy(long pKingdomId)
        {
            if (!AvailableArmiesByKingdom.TryGetValue(pKingdomId,
                    out HashSet<Army> armies) || armies.Count == 0)
                return null;
            var stale = new List<Army>();
            foreach (Army army in armies)
            {
                Kingdom owner = null;
                try { owner = AWArmyService.GetIntendedKingdom(army); }
                catch { }
                if (!WarriorArmyMembershipRules.IsEligibleTargetArmy(
                        armyValid: army?.data != null,
                        alive: IsLiveArmy(army),
                        ordinaryArmy: ArmyNativeNameService.IsOrdinaryArmy(army),
                        actorKingdomId: pKingdomId,
                        armyKingdomId: owner?.id ?? -1L))
                {
                    stale.Add(army);
                    continue;
                }
                Army result = army;
                for (int i = 0; i < stale.Count; i++) armies.Remove(stale[i]);
                return result;
            }
            for (int i = 0; i < stale.Count; i++) armies.Remove(stale[i]);
            if (armies.Count == 0) AvailableArmiesByKingdom.Remove(pKingdomId);
            return null;
        }

        private static bool HasArmyMembership(Actor pActor)
        {
            if (pActor?.data == null) return false;
            Army army = null;
            try { army = pActor.army; }
            catch { }
            if (army?.data == null) return false;
            try { return army.units != null && army.units.Contains(pActor); }
            catch { return false; }
        }

        private static bool IsLiveArmy(Army pArmy)
        {
            try { return pArmy?.data != null && pArmy.isAlive(); }
            catch { return false; }
        }

        private static bool RuntimeStable()
        {
            return Config.game_loaded && !SmoothLoader.isLoading() &&
                   World.world != null &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
