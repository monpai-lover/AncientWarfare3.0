using System;
using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.lineage;
using ai.behaviours;
using UnityEngine;

namespace AncientWarfare3.content
{
    internal static class FeudatoryContent
    {
        public const string TraitId = "fanwang";
        public const string ActorJobId = "aw_job_feudatory_prince";
        public const string RoamTaskId = "aw_task_feudatory_prince_roam";

        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterTrait();
            RegisterJob();
            RegisterTask();
        }

        private static void RegisterTrait()
        {
            if (AssetManager.traits.get(TraitId) != null) return;
            var trait = new ActorTrait
            {
                id = TraitId,
                path_icon = TraitIconUsageRules.IconForTrait(TraitId),
                rate_birth = 0,
                rate_inherit = 0,
                needs_to_be_explored = false,
                unlocked_with_achievement = false,
                group_id = XiaTraitGroups.AW2
            };
            trait.base_stats["stewardship"] = 3f;
            trait.base_stats["diplomacy"] = 2f;
            AssetManager.traits.add(trait);
            trait.unlock();
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
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = RoamTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1f,
                locale_key = "task_unit_" + RoamTaskId
            });
            task.setIcon("ui/Icons/traits/iconzhuhou");
            task.addBeh(new BehFeudatoryPrinceRoam());
            task.addBeh(new BehGoToTileTarget());
            task.addBeh(new BehRandomWait(4f, 8f));
        }
    }
}
