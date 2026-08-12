using AncientWarfare3.ai.behaviours.actor;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class StandingArmyPeacetimeContent
    {
        public const string JobId = "aw_standing_army_peacetime_job";
        public const string PatrolTaskId =
            "aw_standing_army_peacetime_patrol";

        public static void Init()
        {
            if (!AssetManager.job_actor.has(JobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = JobId
                });
                job.addTask(PatrolTaskId);
            }

            if (AssetManager.tasks_actor.has(PatrolTaskId)) return;
            var patrol = AssetManager.tasks_actor.add(
                new BehaviourTaskActor
                {
                    id = PatrolTaskId,
                    cancellable_by_reproduction = true,
                    cancellable_by_socialize = false,
                    speed_multiplier = 0.8f,
                    locale_key = "task_unit_move"
                });
            patrol.setIcon("ui/Icons/iconCity");
            patrol.addBeh(new BehStandingArmyPeacetimePatrol());
            patrol.addBeh(new BehGoToTileTarget());
        }
    }
}
