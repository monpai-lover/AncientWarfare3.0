using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class SlaveryContent
    {
        public const string CITIZEN_JOB_SLAVE_CATCHER = "aw_slave_catcher";
        public const string ACTOR_JOB_SLAVE_CATCHER = "aw_slave_catcher_job";
        public const string TASK_CATCH_SLAVE = "aw_slave_catcher_capture";

        public static CitizenJobAsset SlaveCatcherJob { get; private set; }

        public static void Init()
        {
            RegisterCitizenJob();
            RegisterActorJob();
            RegisterTasks();
        }

        private static void RegisterCitizenJob()
        {
            if (AssetManager.citizen_job_library.has(CITIZEN_JOB_SLAVE_CATCHER))
            {
                SlaveCatcherJob = AssetManager.citizen_job_library.get(CITIZEN_JOB_SLAVE_CATCHER);
                return;
            }

            SlaveCatcherJob = AssetManager.citizen_job_library.add(new CitizenJobAsset
            {
                id = CITIZEN_JOB_SLAVE_CATCHER,
                common_job = false,
                ok_for_king = false,
                ok_for_leader = false,
                unit_job_default = ACTOR_JOB_SLAVE_CATCHER,
                debug_option = DebugOption.CitizenJobAttacker,
                path_icon = "ui/policy/start_slaves",
                should_be_assigned = SlaveService.CanBeSlaveCatcher
            });
        }

        private static void RegisterActorJob()
        {
            if (AssetManager.job_actor.has(ACTOR_JOB_SLAVE_CATCHER)) return;

            ActorJob job = AssetManager.job_actor.add(new ActorJob
            {
                id = ACTOR_JOB_SLAVE_CATCHER
            });
            job.addTask(TASK_CATCH_SLAVE);
            job.addTask("try_to_return_home");
            job.addTask("wait");
            job.addTask("check_if_stuck_on_small_land");
        }

        private static void RegisterTasks()
        {
            if (AssetManager.tasks_actor.has(TASK_CATCH_SLAVE)) return;

            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = TASK_CATCH_SLAVE,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1.1f,
                locale_key = "task_unit_move"
            });
            task.setIcon("ui/policy/start_slaves");
            task.addBeh(new BehFindSlaveCaptureTarget());
            task.addBeh(new BehGoToActorTarget(GoToActorTargetType.SameTile, false, true, true));
            task.addBeh(new BehCatchTargetAsSlave());
            task.addBeh(new BehRandomWait(1f, 2f));
        }
    }
}
