using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class GuardContent
    {
        public const string CITIZEN_JOB_KING_GUARD = "aw_king_guard";
        public const string ACTOR_JOB_KING_GUARD = "aw_king_guard_job";
        public const string TASK_PROTECT_KING = "aw_guard_protect_king";
        public const string TASK_FOLLOW_KING = "aw_guard_follow_king";

        public static CitizenJobAsset KingGuardJob { get; private set; }

        public static void Init()
        {
            RegisterCitizenJob();
            RegisterActorJob();
            RegisterTasks();
        }

        private static void RegisterCitizenJob()
        {
            if (AssetManager.citizen_job_library.has(CITIZEN_JOB_KING_GUARD))
            {
                KingGuardJob = AssetManager.citizen_job_library.get(CITIZEN_JOB_KING_GUARD);
                return;
            }

            KingGuardJob = AssetManager.citizen_job_library.add(new CitizenJobAsset
            {
                id = CITIZEN_JOB_KING_GUARD,
                common_job = false,
                ok_for_king = false,
                ok_for_leader = false,
                unit_job_default = ACTOR_JOB_KING_GUARD,
                debug_option = DebugOption.CitizenJobAttacker,
                path_icon = "ui/Icons/iconArmor",
                should_be_assigned = RoyalGuardService.IsRoyalGuard
            });
        }

        private static void RegisterActorJob()
        {
            if (AssetManager.job_actor.has(ACTOR_JOB_KING_GUARD)) return;

            ActorJob job = AssetManager.job_actor.add(new ActorJob
            {
                id = ACTOR_JOB_KING_GUARD
            });
            job.addTask(TASK_PROTECT_KING);
            job.addTask(TASK_FOLLOW_KING);
            job.addTask("wait");
            job.addTask("check_if_stuck_on_small_land");
        }

        private static void RegisterTasks()
        {
            if (!AssetManager.tasks_actor.has(TASK_PROTECT_KING))
            {
                var protect = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = TASK_PROTECT_KING,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    in_combat = true,
                    speed_multiplier = 1.15f,
                    locale_key = "task_unit_move"
                });
                protect.setIcon("ui/Icons/iconArmor");
                protect.addBeh(new BehFindRoyalGuardThreat());
                protect.addBeh(new BehGoToActorTarget(GoToActorTargetType.RaycastWithAttackRange, false, true, true));
                protect.addBeh(new BehRoyalGuardAttackThreat());
            }

            if (!AssetManager.tasks_actor.has(TASK_FOLLOW_KING))
            {
                var follow = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = TASK_FOLLOW_KING,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    speed_multiplier = 1.1f,
                    locale_key = "task_unit_move"
                });
                follow.setIcon("ui/Icons/iconLoyalty");
                follow.addBeh(new BehRoyalGuardFollowKing());
                follow.addBeh(new BehGoToTileTarget());
                follow.addBeh(new BehRandomWait(RoyalGuardActionRules.FollowIdleWaitMin,
                    RoyalGuardActionRules.FollowIdleWaitMax));
            }
        }
    }
}
