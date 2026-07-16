using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.lineage;
using ai.behaviours;
using UnityEngine;

namespace AncientWarfare3.content
{
    internal static class WarMobilizationContent
    {
        public const string DeploymentJobId = "aw_war_deployment_job";
        public const string DeploymentTaskId = "aw_war_deployment";
        public const string NoticeLogId = "aw_war_notice_issued";

        public static void Init()
        {
            RegisterDeploymentJob();
            RegisterNoticeLog();
        }

        public static void AnnounceNotice(Kingdom pAttacker, Kingdom pDefender)
        {
            if (pAttacker?.data == null || pDefender?.data == null) return;
            try
            {
                WorldLogAsset asset = AssetManager.world_log_library.get(NoticeLogId);
                if (asset == null) return;
                var message = new WorldLogMessage(asset, pAttacker.name ?? "", pDefender.name ?? "")
                {
                    kingdom = pAttacker
                };
                WorldTile tile = pAttacker.capital?.getTile();
                if (tile != null) message.location = tile.pos;
                message.add();
            }
            catch { }
        }

        private static void RegisterDeploymentJob()
        {
            if (!AssetManager.job_actor.has(DeploymentJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob { id = DeploymentJobId });
                job.addTask(DeploymentTaskId);
                job.addTask("wait");
                job.addTask("check_if_stuck_on_small_land");
            }

            if (AssetManager.tasks_actor.has(DeploymentTaskId)) return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = DeploymentTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1.15f,
                locale_key = "task_unit_aw_war_deployment"
            });
            task.setIcon("ui/Icons/iconWar");
            task.addBeh(new BehWarDeploymentMove());
            task.addBeh(new BehGoToTileTarget());
            task.addBeh(new BehWarDeploymentArrive());
            task.addBeh(new BehRandomWait(2f, 4f));
        }

        private static void RegisterNoticeLog()
        {
            if (AssetManager.world_log_library.get(NoticeLogId) != null) return;
            AssetManager.world_log_library.add(new WorldLogAsset
            {
                id = NoticeLogId,
                locale_id = NoticeLogId,
                group = "wars",
                path_icon = "ui/Icons/iconWar",
                color = Toolbox.color_log_neutral,
                text_replacer = (WorldLogMessage pMessage, ref string pText) =>
                {
                    pText = pText.Replace("$attacker$", pMessage.special1 ?? "")
                        .Replace("$defender$", pMessage.special2 ?? "");
                }
            });
        }
    }
}
