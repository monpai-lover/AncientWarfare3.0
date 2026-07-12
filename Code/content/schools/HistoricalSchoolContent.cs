using System;
using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;
using ai.behaviours;
using UnityEngine;

namespace AncientWarfare3.content.schools
{
    internal static class HistoricalSchoolContent
    {
        public const string MasterTraitId = "aw_historical_school_master";
        public const string GuestStatusId = "aw_school_guest";
        public const string DebateStatusId = "aw_school_debate";
        public const string VoyageStatusId = "aw_school_voyage";
        public const string CitizenJobId = "aw_historical_school_scholar";
        public const string ActorJobId = "aw_historical_school_job";
        public const string TravelTaskId = "aw_historical_school_travel";
        public const string DescentLogId = "aw_school_master_descended";
        public const string DeathLogId = "aw_school_master_died";
        public const string LectureLogId = "aw_school_lecture";
        public const string DebateLogId = "aw_school_debate_result";

        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            foreach (string error in HistoricalSchoolMasterRegistry.Validate())
                ModClass.LogWarning("Historical school registry: " + error);
            RegisterTrait();
            RegisterStatuses();
            RegisterJobs();
            RegisterTasks();
            RegisterWorldLogs();
            HistoricalSchoolRuntime.LoadState();
        }

        public static void AnnounceDescent(Actor pActor, City pCity)
        {
            Announce(DescentLogId, pActor, pCity, LogKingdom(pActor));
        }

        public static void AnnounceDeath(Actor pActor, City pCity)
        {
            Announce(DeathLogId, pActor, pCity, LogKingdom(pActor));
        }

        private static Kingdom LogKingdom(Actor pActor)
        {
            if (pActor?.data == null) return null;
            return HistoricalAffiliationService.Get(pActor.data.id) != null
                ? HistoricalAffiliationService.HomeKingdom(pActor)
                : pActor.kingdom;
        }

        private static void RegisterTrait()
        {
            if (AssetManager.traits.get(MasterTraitId) != null) return;
            var trait = new ActorTrait
            {
                id = MasterTraitId,
                path_icon = "ui/Icons/iconXias",
                rate_birth = 0,
                rate_inherit = 0,
                needs_to_be_explored = false,
                unlocked_with_achievement = false,
                group_id = XiaTraitGroups.AW2
            };
            trait.base_stats["health"] = 20f;
            trait.base_stats["lifespan"] = 25f;
            trait.base_stats["armor"] = 2f;
            trait.base_stats["speed"] = 1f;
            trait.base_stats["stewardship"] = 3f;
            trait.base_stats["diplomacy"] = 3f;
            trait.base_stats["intelligence"] = 4f;
            AssetManager.traits.add(trait);
            trait.unlock();
        }

        private static void RegisterStatuses()
        {
            AddStatus(GuestStatusId, "ui/Icons/iconLoyalty", 120f);
            AddStatus(DebateStatusId, "ui/Icons/traits/iconmingjia", 20f);
            AddStatus(VoyageStatusId, "ui/Icons/iconBoat", 120f);
        }

        private static void AddStatus(string pId, string pIcon, float pDuration)
        {
            if (AssetManager.status.get(pId) != null) return;
            AssetManager.status.add(new StatusAsset
            {
                id = pId,
                duration = pDuration,
                allow_timer_reset = true,
                path_icon = pIcon,
                locale_id = "status_title_" + pId,
                locale_description = "status_description_" + pId,
                sprite_list = Array.Empty<Sprite>()
            });
        }

        private static void RegisterJobs()
        {
            if (!AssetManager.citizen_job_library.has(CitizenJobId))
            {
                AssetManager.citizen_job_library.add(new CitizenJobAsset
                {
                    id = CitizenJobId,
                    common_job = false,
                    ok_for_king = false,
                    ok_for_leader = false,
                    unit_job_default = ActorJobId,
                    debug_option = DebugOption.CitizenJobAttacker,
                    path_icon = "ui/Icons/traits/iconRujia",
                    should_be_assigned = HistoricalSchoolDescentService.IsCanonicalMaster
                });
            }
            if (AssetManager.job_actor.has(ActorJobId)) return;
            ActorJob job = AssetManager.job_actor.add(new ActorJob { id = ActorJobId });
            job.addTask(TravelTaskId);
            job.addTask("wait");
            job.addTask("check_if_stuck_on_small_land");
        }

        private static void RegisterTasks()
        {
            if (AssetManager.tasks_actor.has(TravelTaskId)) return;
            var travel = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = TravelTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1f,
                locale_key = "task_unit_move"
            });
            travel.setIcon("ui/Icons/iconBoat");
            travel.addBeh(new BehHistoricalSchoolTravel());
            travel.addBeh(new BehGoToTileTarget());
            travel.addBeh(new BehHistoricalSchoolArrive());
        }

        private static void RegisterWorldLogs()
        {
            RegisterLog(DescentLogId, Toolbox.color_log_neutral);
            RegisterLog(DeathLogId, Toolbox.color_log_warning);
            RegisterLog(LectureLogId, Toolbox.color_log_neutral);
            RegisterLog(DebateLogId, Toolbox.color_log_neutral);
        }

        private static void RegisterLog(string pId, Color pColor)
        {
            if (AssetManager.world_log_library.get(pId) != null) return;
            AssetManager.world_log_library.add(new WorldLogAsset
            {
                id = pId,
                group = "kings",
                path_icon = "ui/Icons/traits/iconRujia",
                color = pColor,
                text_replacer = (WorldLogMessage pMessage, ref string pText) =>
                {
                    pText = pText.Replace("$master$", pMessage.special1 ?? "")
                        .Replace("$city$", pMessage.special2 ?? "");
                }
            });
        }

        private static void Announce(string pLogId, Actor pActor, City pCity, Kingdom pKingdom)
        {
            if (pActor?.data == null) return;
            try
            {
                WorldLogAsset asset = AssetManager.world_log_library.get(pLogId);
                if (asset == null) return;
                string color = pKingdom?.getColor()?.color_text;
                if (string.IsNullOrEmpty(color)) color = "#FFFFFF";
                var message = new WorldLogMessage(asset,
                    Toolbox.coloredString(pActor.getName(), color), pCity?.data?.name ?? "")
                {
                    unit = pActor,
                    kingdom = pKingdom
                };
                if (pActor.current_tile != null) message.location = pActor.current_tile.pos;
                message.add();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school log failed: " + error.Message);
            }
        }
    }
}
