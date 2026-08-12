using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehStandingArmyPeacetimePatrol :
        BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!StandingArmyPeacetimeService.ShouldUsePeacetimeJob(
                    pActor))
            {
                StandingArmyPeacetimeService.RestoreMilitaryJob(pActor);
                return BehResult.Stop;
            }

            WorldTile tile = StandingArmyPeacetimeService.GetPatrolTile(
                pActor);
            if (StandingArmyRules.ShouldRetryPeacetimePatrol(
                    pHasPatrolTarget: tile != null,
                    pTargetIsCurrentTile: tile == pActor?.current_tile))
            {
                pActor?.makeWait(StandingArmyRules.PeacetimePatrolRetrySeconds);
                return BehResult.RepeatStep;
            }

            pActor.beh_tile_target = tile;
            return BehResult.Continue;
        }
    }
}
