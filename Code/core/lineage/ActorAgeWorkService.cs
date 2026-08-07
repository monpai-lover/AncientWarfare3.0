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
            ActorAgeWorkState current = Capture(pActor);
            bool hasPrevious = States.TryGetValue(actorId,
                out ActorAgeWorkState previous);
            bool force = DirtyActors.Remove(actorId) || !hasPrevious;
            ActorAgeWorkStage stages = ActorAgeWorkRules.Resolve(previous,
                current, force);
            if (stages == ActorAgeWorkStage.None)
            {
                States[actorId] = current;
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
                States[actorId] = Capture(pActor);
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

        private static ActorAgeWorkState Capture(Actor pActor)
        {
            bool adult = pActor.isAdult();
            bool permanent = StandingArmyPeacetimeService
                .IsCareerStandingSoldier(pActor);
            bool emergency = StandingArmyPeacetimeService
                .HasMilitaryEmergency(pActor);
            bool dynasticEligible = IsDynasticEligible(pActor);
            string professionId = pActor.profession_asset?.id ?? "";
            int profession = StringComparer.Ordinal.GetHashCode(professionId);
            bool needsAnnualReproductionRecovery =
                DynasticReproductionRules.IsSexualReproductionTask(
                    pActor.ai?.task?.id);
            int year;
            try { year = Math.Max(0, Date.getCurrentYear()); }
            catch { year = 0; }
            return new ActorAgeWorkState(adult, profession, permanent,
                emergency, dynasticEligible,
                StandingArmyPeacetimeService.ShouldUsePeacetimeJob(pActor),
                DynasticReproductionService
                    .ShouldReleaseExistingMilitaryRole(pActor),
                needsAnnualReproductionRecovery, year);
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
