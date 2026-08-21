using AncientWarfare3.ai.behaviours.actor;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class IslandEscapeContent
    {
        internal const string JobId = "aw_island_escape";
        internal const string TaskId = "aw_island_escape_transport";

        internal static void Init()
        {
            if (!AssetManager.job_actor.has(JobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = JobId
                });
                job.addTask(TaskId);
            }
            if (AssetManager.tasks_actor.has(TaskId)) return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = TaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1.1f,
                locale_key = "task_unit_aw_island_escape_transport"
            });
            task.setIcon("ui/Icons/iconArrowAttackTarget");
            task.addBeh(new BehIslandEscapeTransport());
            task.addBeh(new BehGoToTileTarget
            {
                limit_pathfinding_regions = 6
            });
            task.addBeh(new BehIslandEscapeArrival());
        }
    }
}
