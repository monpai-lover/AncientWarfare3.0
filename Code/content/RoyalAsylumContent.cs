using System;
using AncientWarfare3.ai.behaviours.actor;
using ai.behaviours;
using UnityEngine;

namespace AncientWarfare3.content
{
    internal static class RoyalAsylumContent
    {
        public const string StatusId = "aw_royal_asylum";
        public const string ActorJobId = "aw_royal_asylum_job";
        public const string RoamTaskId = "aw_royal_asylum_roam";

        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterStatus();
            RegisterJob();
            RegisterTask();
        }

        private static void RegisterStatus()
        {
            if (AssetManager.status.get(StatusId) != null) return;
            AssetManager.status.add(new StatusAsset
            {
                id = StatusId,
                duration = 1000000f,
                allow_timer_reset = true,
                can_be_cured = false,
                path_icon = "ui/Icons/iconLoyalty",
                locale_id = "status_title_" + StatusId,
                locale_description = "status_description_" + StatusId,
                sprite_list = Array.Empty<Sprite>()
            });
        }

        private static void RegisterJob()
        {
            if (AssetManager.job_actor.has(ActorJobId)) return;
            ActorJob job = AssetManager.job_actor.add(new ActorJob { id = ActorJobId });
            job.addTask(RoamTaskId);
            job.addTask("wait");
            job.addTask("check_if_stuck_on_small_land");
        }

        private static void RegisterTask()
        {
            if (AssetManager.tasks_actor.has(RoamTaskId)) return;
            var roam = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = RoamTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1f,
                locale_key = "task_unit_" + RoamTaskId
            });
            roam.setIcon("ui/Icons/iconLoyalty");
            roam.addBeh(new BehRoyalAsylumRoam());
            roam.addBeh(new BehGoToTileTarget());
            roam.addBeh(new BehRandomWait(4f, 8f));
        }
    }
}
