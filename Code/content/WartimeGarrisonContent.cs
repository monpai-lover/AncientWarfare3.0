using AncientWarfare3.ai.behaviours.actor;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class WartimeGarrisonContent
    {
        public const string JobId = "aw_wartime_garrison_job";
        public const string EngageTaskId = "aw_wartime_garrison_engage";
        public const string ReinforceTaskId =
            "aw_wartime_garrison_reinforce";
        public const string PatrolTaskId = "aw_wartime_garrison_patrol";

        public static void Init()
        {
            if (!AssetManager.job_actor.has(JobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = JobId
                });
                job.addTask(EngageTaskId);
                job.addTask(ReinforceTaskId);
                job.addTask(PatrolTaskId);
                job.addTask("wait");
                job.addTask("check_if_stuck_on_small_land");
            }

            if (!AssetManager.tasks_actor.has(EngageTaskId))
            {
                var engage = AssetManager.tasks_actor.add(
                    new BehaviourTaskActor
                    {
                        id = EngageTaskId,
                        cancellable_by_reproduction = false,
                        cancellable_by_socialize = false,
                        in_combat = true,
                        speed_multiplier = 1.05f,
                        locale_key = "task_unit_aw_wartime_garrison"
                    });
                engage.setIcon("ui/Icons/iconWar");
                engage.addBeh(new BehFindWartimeGarrisonThreat());
                engage.addBeh(new BehGoToActorTarget(
                    GoToActorTargetType.RaycastWithAttackRange,
                    pPathOnWater: false, pCheckCanAttackTarget: true,
                    pCalibrateTargetPosition: true));
                engage.addBeh(new BehWartimeGarrisonAttackThreat());
            }

            if (!AssetManager.tasks_actor.has(ReinforceTaskId))
            {
                var reinforce = AssetManager.tasks_actor.add(
                    new BehaviourTaskActor
                    {
                        id = ReinforceTaskId,
                        cancellable_by_reproduction = false,
                        cancellable_by_socialize = false,
                        in_combat = true,
                        speed_multiplier = 1.05f,
                        locale_key = "task_unit_aw_wartime_garrison"
                    });
                reinforce.setIcon("ui/Icons/iconWar");
                reinforce.addBeh(new BehWartimeGarrisonReinforce());
                reinforce.addBeh(new BehGoToTileTarget());
                reinforce.addBeh(new BehRandomWait(1f, 2f));
            }

            if (!AssetManager.tasks_actor.has(PatrolTaskId))
            {
                var patrol = AssetManager.tasks_actor.add(
                    new BehaviourTaskActor
                    {
                        id = PatrolTaskId,
                        cancellable_by_reproduction = false,
                        cancellable_by_socialize = false,
                        speed_multiplier = 0.85f,
                        locale_key = "task_unit_aw_wartime_garrison"
                    });
                patrol.setIcon("ui/Icons/iconCity");
                patrol.addBeh(new BehWartimeGarrisonPatrol());
                patrol.addBeh(new BehGoToTileTarget());
                patrol.addBeh(new BehRandomWait(2f, 5f));
            }
        }
    }
}
