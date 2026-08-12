using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehRoyalGuardFollowKing : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            // 每次执行时确保禁卫军仍在正确的 job 上，防护其他系统清除 job
            if (pActor?.ai != null && pActor.ai.job?.id != GuardContent.ACTOR_JOB_KING_GUARD)
            {
                RoyalGuardService.EnsureProtectKingTask(pActor);
                return BehResult.Continue;
            }

            WorldTile tile = RoyalGuardService.GetFollowTile(pActor);
            if (tile == null)
            {
                // 没有有效目标时等待而不是停止，避免 task 被清除
                RoyalGuardService.WaitAfterGuardFollowIdle(pActor);
                return BehResult.Continue;
            }

            if (!RoyalGuardActionRules.ShouldIssueFollowMove(
                    pHasTarget: true,
                    pTargetIsCurrentTile: tile == pActor.current_tile))
            {
                RoyalGuardService.WaitAfterGuardFollowIdle(pActor);
                return BehResult.Continue;
            }

            pActor.beh_tile_target = tile;
            return BehResult.Continue;
        }
    }
}
