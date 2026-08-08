using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ActorAgeWorkService
    {
        private static readonly Dictionary<long, ActorAgeWorkState> States =
            new Dictionary<long, ActorAgeWorkState>();
        private static readonly HashSet<long> DirtyActors =
            new HashSet<long>();
        private static MapBox _world;

        public static void Process(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !pActor.isAlive()) return;
            EnsureWorld();

            long actorId = pActor.data.id;
            bool hasPrevious = States.TryGetValue(actorId,
                out ActorAgeWorkState previous);
            bool dirty = DirtyActors.Remove(actorId);
            bool dynasticEligible = IsDynasticEligible(pActor);
            bool warrior = SafeIsWarrior(pActor);
            bool reproductionRecoveryActive =
                HasAnnualReproductionRecovery(pActor);
            if (!ActorAgeWorkRules.ShouldTrack(hasPrevious, dirty,
                    dynasticEligible, warrior,
                    reproductionRecoveryActive)) return;

            ActorAgeWorkState current = Capture(pActor, dynasticEligible,
                warrior, reproductionRecoveryActive);
            bool force = dirty || !hasPrevious;
            ActorAgeWorkStage stages = ActorAgeWorkRules.Resolve(previous,
                current, force);
            if (stages == ActorAgeWorkStage.None)
            {
                StoreOrRemove(actorId, current, dynasticEligible, warrior,
                    reproductionRecoveryActive);
                return;
            }

            if ((stages & ActorAgeWorkStage.DynasticTitle) != 0)
                DynasticTitleService.OnAgeUpdated(pActor);
            if ((stages & ActorAgeWorkStage.StandingArmyJob) != 0)
                StandingArmyPeacetimeService.RefreshJob(pActor);
            if ((stages & ActorAgeWorkStage.MilitaryRoleRelease) != 0)
                DynasticReproductionService.ReleaseExistingMilitaryRole(
                    pActor, current.ShouldReleaseMilitaryRole);

            if (pActor.data != null && pActor.isAlive() && !pActor.isRekt())
            {
                dynasticEligible = IsDynasticEligible(pActor);
                warrior = SafeIsWarrior(pActor);
                reproductionRecoveryActive =
                    HasAnnualReproductionRecovery(pActor);
                ActorAgeWorkState refreshed = Capture(pActor,
                    dynasticEligible, warrior,
                    reproductionRecoveryActive);
                StoreOrRemove(actorId, refreshed, dynasticEligible, warrior,
                    reproductionRecoveryActive);
            }
            else
                Remove(actorId);
        }

        public static void MarkDirty(Actor pActor)
        {
            if (pActor?.data == null) return;
            EnsureWorld();
            DirtyActors.Add(pActor.data.id);
        }

        public static void Remove(long pActorId)
        {
            if (pActorId < 0L) return;
            States.Remove(pActorId);
            DirtyActors.Remove(pActorId);
        }

        public static void Reset()
        {
            States.Clear();
            DirtyActors.Clear();
            _world = World.world;
        }

        private static ActorAgeWorkState Capture(Actor pActor,
            bool pDynasticEligible, bool pWarrior,
            bool pReproductionRecoveryActive)
        {
            bool adult = pActor.isAdult();
            bool permanent = pWarrior && StandingArmyPeacetimeService
                .IsCareerStandingSoldier(pActor);
            bool emergency = pWarrior && StandingArmyPeacetimeService
                .HasMilitaryEmergency(pActor);
            string professionId = pActor.profession_asset?.id ?? "";
            int profession = StringComparer.Ordinal.GetHashCode(professionId);
            int year;
            try { year = Math.Max(0, Date.getCurrentYear()); }
            catch { year = 0; }
            return new ActorAgeWorkState(adult, profession, permanent,
                emergency, pDynasticEligible,
                pWarrior && StandingArmyPeacetimeService
                    .ShouldUsePeacetimeJob(pActor),
                pWarrior && DynasticReproductionService
                    .ShouldReleaseExistingMilitaryRole(pActor),
                pReproductionRecoveryActive, year);
        }

        private static bool SafeIsWarrior(Actor pActor)
        {
            try { return pActor?.isWarrior() == true; }
            catch { return false; }
        }

        private static bool HasAnnualReproductionRecovery(Actor pActor)
        {
            return DynasticReproductionRules.IsSexualReproductionTask(
                pActor?.ai?.task?.id);
        }

        private static void StoreOrRemove(long pActorId,
            ActorAgeWorkState pState, bool pDynasticEligible,
            bool pWarrior, bool pReproductionRecoveryActive)
        {
            if (ActorAgeWorkRules.ShouldTrack(false, false,
                    pDynasticEligible, pWarrior,
                    pReproductionRecoveryActive))
                States[pActorId] = pState;
            else
                Remove(pActorId);
        }

        private static bool IsDynasticEligible(Actor pActor)
        {
            pActor.data.get(LineageKeys.ROYAL_CHILD,
                out bool royalChild, false);
            return royalChild || FeudatoryService.IsActivePrince(pActor);
        }

        private static void EnsureWorld()
        {
            MapBox world = World.world;
            if (ReferenceEquals(_world, world)) return;
            States.Clear();
            DirtyActors.Clear();
            _world = world;
        }
    }
}
