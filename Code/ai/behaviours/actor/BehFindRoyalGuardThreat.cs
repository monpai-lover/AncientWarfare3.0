using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public class BehFindRoyalGuardThreat : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            RoyalGuardService.EnsureGuardNotInNormalArmy(pActor);
            Actor target = RoyalGuardService.FindThreatNearKing(pActor);
            if (target == null)
            {
                pActor.beh_actor_target = null;
                // 没有威胁时主动切换到跟随 task，而不是返回 Stop 让原版系统重新选择
                // （原版可能会再次选中 PROTECT task 导致死循环）
                if (pActor?.ai != null && !pActor.isTask(GuardContent.TASK_FOLLOW_KING))
                {
                    try
                    {
                        pActor.ai.setTask(GuardContent.TASK_FOLLOW_KING);
                    }
                    catch { }
                }
                RoyalGuardService.WaitAfterGuardNoThreat(pActor);
                return BehResult.RepeatStep;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }
}
