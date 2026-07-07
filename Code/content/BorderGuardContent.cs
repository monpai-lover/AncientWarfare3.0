using AncientWarfare3.ai.behaviours.actor;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class BorderGuardContent
    {
        public const string ACTOR_JOB_BORDER_GUARD = "aw_border_guard_job";
        public const string TASK_BORDER_ATTACK = "aw_border_guard_attack";
        public const string TASK_BORDER_PATROL = "aw_border_guard_patrol";

        public static void Init()
        {
            RegisterActorJob();
            RegisterTasks();
        }

        private static void RegisterActorJob()
        {
            if (AssetManager.job_actor.has(ACTOR_JOB_BORDER_GUARD)) return;

            ActorJob job = AssetManager.job_actor.add(new ActorJob
            {
                id = ACTOR_JOB_BORDER_GUARD
            });
            job.addTask(TASK_BORDER_ATTACK);
            job.addTask(TASK_BORDER_PATROL);
            job.addTask("wait");
            job.addTask("check_if_stuck_on_small_land");
        }

        private static void RegisterTasks()
        {
            if (!AssetManager.tasks_actor.has(TASK_BORDER_ATTACK))
            {
                var attack = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = TASK_BORDER_ATTACK,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    in_combat = true,
                    speed_multiplier = 1.1f,
                    locale_key = "task_unit_move"
                });
                attack.setIcon("ui/Icons/iconWar");
                attack.addBeh(new BehFindBorderGuardThreat());
                attack.addBeh(new BehGoToActorTarget(GoToActorTargetType.RaycastWithAttackRange, false, true, true));
                attack.addBeh(new BehBorderGuardAttackThreat());
            }

            if (!AssetManager.tasks_actor.has(TASK_BORDER_PATROL))
            {
                var patrol = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = TASK_BORDER_PATROL,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    speed_multiplier = 1.0f,
                    locale_key = "task_unit_move"
                });
                patrol.setIcon("ui/Icons/iconTarget");
                patrol.addBeh(new BehBorderGuardPatrol());
                patrol.addBeh(new BehGoToTileTarget());
                patrol.addBeh(new BehRandomWait(4f, 8f));
            }
        }
    }
}
