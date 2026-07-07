using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehRoyalGuardFollowKing : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            WorldTile tile = RoyalGuardService.GetFollowTile(pActor);
            if (tile == null)
                return BehResult.Stop;

            if (!RoyalGuardActionRules.ShouldIssueFollowMove(
                    pHasTarget: true,
                    pTargetIsCurrentTile: tile == pActor.current_tile))
            {
                RoyalGuardService.WaitAfterGuardFollowIdle(pActor);
                return BehResult.Stop;
            }

            pActor.beh_tile_target = tile;
            return BehResult.Continue;
        }
    }
}
