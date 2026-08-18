using System;
using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;
using ai.behaviours;
using NeoModLoader.General;
using UnityEngine;

namespace AncientWarfare3.content.schools
{
    internal static class HistoricalSchoolContent
    {
        public const string MasterTraitId = "aw_historical_school_master";
        public const string MasterTraitLocaleId = "trait_aw_historical_school_master";
        public const string MasterTraitDescriptionLocaleId =
            "trait_aw_historical_school_master_info";
        public const string GuestStatusId = "aw_school_guest";
        public const string DebateStatusId = "aw_school_debate";
        public const string VoyageStatusId = "aw_school_voyage";
        public const string CitizenJobId = "aw_historical_school_scholar";
        public const string ActorJobId = "aw_historical_school_job";
        public const string IdleRoamTaskId = "aw_historical_school_idle_roam";
        public const string TravelTaskId = "aw_historical_school_travel";
        public const string EducationTravelTaskId =
            "aw_historical_school_education_travel";
        public const string LectureTaskId = "aw_historical_school_lecture";
        public const string DebateTravelTaskId = "aw_historical_school_debate_travel";
        public const string DebateTaskId = "aw_historical_school_debate";
        public const string DebateReceivingTaskId =
            "aw_historical_school_debate_receiving";
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
            HistoricalSchoolAcademyService.Init();
        }

        public static void AnnounceDescent(Actor pActor, City pCity, string pSchoolId)
        {
            Announce(LogIdForSchool(DescentLogId, pSchoolId), pActor, pCity,
                LogKingdom(pActor));
        }

        public static void AnnounceDeath(Actor pActor, City pCity, string pSchoolId)
        {
            Announce(LogIdForSchool(DeathLogId, pSchoolId), pActor, pCity,
                LogKingdom(pActor));
        }

        public static void AnnounceLecture(Actor pActor, City pCity, string pSchoolId)
        {
            Announce(LogIdForSchool(LectureLogId, pSchoolId), pActor, pCity,
                LogKingdom(pActor));
        }

        public static void AnnounceDebate(Actor pFirst, Actor pSecond, City pCity,
            SchoolDebateOutcome pOutcome)
        {
            // The world-log asset currently has one actor and one city replacement slot.
            // The complete pair/result remains in the persisted school event and history.
            Announce(DebateLogId, pFirst, pCity, LogKingdom(pFirst));
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
            ActorTrait trait = AssetManager.traits.get(MasterTraitId);
            if (trait == null)
            {
                trait = new ActorTrait
                {
                    id = MasterTraitId,
                    path_icon = "ui/Icons/iconXias",
                    rate_birth = 0,
                    rate_inherit = 0,
                    needs_to_be_explored = false,
                    unlocked_with_achievement = false,
                    group_id = XiaTraitGroups.AW2,
                    special_locale_id = MasterTraitLocaleId,
                    special_locale_description = MasterTraitDescriptionLocaleId,
                    has_description_2 = false
                };
                // AssetManager initializes base_stats when the trait is registered.
                AssetManager.traits.add(trait);
            }
            else
            {
                // Hot reloads can retain an older asset, so repair its explicit locale route.
                trait.special_locale_id = MasterTraitLocaleId;
                trait.special_locale_description = MasterTraitDescriptionLocaleId;
                trait.has_description_2 = false;
            }
            trait.unlock();
            trait.base_stats["health"] = 20f;
            trait.base_stats["lifespan"] = 25f;
            trait.base_stats["armor"] = 2f;
            trait.base_stats["speed"] = 1f;
            trait.base_stats["stewardship"] = 3f;
            trait.base_stats["diplomacy"] = 3f;
            trait.base_stats["intelligence"] = 4f;
            EnsureMasterTraitLocalization();
        }

        private static void EnsureMasterTraitLocalization()
        {
            if (LocalizedTextManager.instance == null) return;
            if (LocalizedTextManager.stringExists(MasterTraitLocaleId) &&
                LocalizedTextManager.stringExists(MasterTraitDescriptionLocaleId)) return;
            try
            {
                // NML normally applies Locales before OnModLoad. Reapply once when another
                // mod or a hot reload left the game's active locale dictionary stale.
                LM.ApplyLocale(false);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical master locale reapply failed: " +
                                    error.Message);
            }
            if (!LocalizedTextManager.stringExists(MasterTraitLocaleId) ||
                !LocalizedTextManager.stringExists(MasterTraitDescriptionLocaleId))
                ModClass.LogWarning("Historical master trait locale missing: " +
                                    MasterTraitLocaleId + " / " +
                                    MasterTraitDescriptionLocaleId);
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
            job.addTask(IdleRoamTaskId);
            job.addTask("wait");
            job.addTask("check_if_stuck_on_small_land");
        }

        private static void RegisterTasks()
        {
            if (!AssetManager.tasks_actor.has(IdleRoamTaskId))
            {
                var idleRoam = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = IdleRoamTaskId,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    speed_multiplier = 1f,
                    locale_key = "task_unit_walk"
                });
                idleRoam.setIcon("ui/Icons/iconKnowledge");
                idleRoam.addBeh(new BehHistoricalSchoolIdleRoam());
                idleRoam.addBeh(new BehGoToTileTarget());
                idleRoam.addBeh(new BehRandomWait(3f, 6f));
            }

            if (!AssetManager.tasks_actor.has(TravelTaskId))
            {
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

            if (!AssetManager.tasks_actor.has(EducationTravelTaskId))
            {
                var educationTravel = AssetManager.tasks_actor.add(
                    new BehaviourTaskActor
                    {
                        id = EducationTravelTaskId,
                        cancellable_by_reproduction = false,
                        cancellable_by_socialize = false,
                        speed_multiplier = 1f,
                        locale_key = "task_unit_move"
                    });
                educationTravel.setIcon("ui/Icons/iconKnowledge");
                educationTravel.addBeh(
                    new BehHistoricalSchoolEducationTravel());
                educationTravel.addBeh(new BehGoToTileTarget());
                educationTravel.addBeh(
                    new BehHistoricalSchoolEducationArrive());
            }

            if (!AssetManager.tasks_actor.has(LectureTaskId))
            {
                var lecture = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = LectureTaskId,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    speed_multiplier = 1f,
                    locale_key = "task_unit_aw_historical_school_lecture"
                });
                lecture.setIcon("ui/Icons/iconKnowledge");
                lecture.addBeh(new BehHistoricalSchoolPrepareLecture());
                lecture.addBeh(new BehGoToBuildingTarget());
                lecture.addBeh(new BehStayInBuildingTarget(4f, 7f));
                lecture.addBeh(new BehHistoricalSchoolCompleteLecture());
            }

            if (!AssetManager.tasks_actor.has(DebateTravelTaskId))
            {
                var travel = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = DebateTravelTaskId,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    speed_multiplier = 1f,
                    locale_key = "task_unit_aw_historical_school_debate_travel"
                });
                travel.setIcon("ui/Icons/traits/iconmingjia");
                travel.addBeh(new BehHistoricalSchoolPrepareDebate());
                travel.addBeh(new BehGoToBuildingTarget());
                travel.addBeh(new BehStayInBuildingTarget(0f, 0f));
                travel.addBeh(new BehHistoricalSchoolBeginDebate());
            }

            if (!AssetManager.tasks_actor.has(DebateTaskId))
            {
                var debate = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = DebateTaskId,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    locale_key = "task_unit_aw_historical_school_debate"
                });
                debate.setIcon("ui/Icons/traits/iconmingjia");
                debate.addBeh(new BehRandomWait(4f, 7f));
                debate.addBeh(new BehHistoricalSchoolCompleteDebate());
            }

            if (!AssetManager.tasks_actor.has(DebateReceivingTaskId))
            {
                var receiving = AssetManager.tasks_actor.add(new BehaviourTaskActor
                {
                    id = DebateReceivingTaskId,
                    cancellable_by_reproduction = false,
                    cancellable_by_socialize = false,
                    speed_multiplier = 1f,
                    locale_key = "task_unit_aw_historical_school_debate_receiving"
                });
                receiving.setIcon("ui/Icons/traits/iconmingjia");
                receiving.addBeh(new BehHistoricalSchoolPrepareDebate());
                receiving.addBeh(new BehGoToBuildingTarget());
                receiving.addBeh(new BehStayInBuildingTarget(4f, 7f));
                receiving.addBeh(new BehHistoricalSchoolCompleteDebate());
            }
        }

        private static void RegisterWorldLogs()
        {
            RegisterLog(DescentLogId, Toolbox.color_log_neutral);
            RegisterLog(DeathLogId, Toolbox.color_log_warning);
            RegisterLog(LectureLogId, Toolbox.color_log_neutral,
                "ui/Icons/iconKnowledge", LectureLogId);
            foreach (CourtSchoolDefinition definition in CourtSchoolRegistry.All)
            {
                RegisterLog(LogIdForSchool(DescentLogId, definition.Id),
                    Toolbox.color_log_neutral, definition.IconPath, DescentLogId);
                RegisterLog(LogIdForSchool(DeathLogId, definition.Id),
                    Toolbox.color_log_warning, definition.IconPath, DeathLogId);
                RegisterLog(LogIdForSchool(LectureLogId, definition.Id),
                    Toolbox.color_log_neutral, definition.IconPath, LectureLogId);
            }
            RegisterLog(DebateLogId, Toolbox.color_log_neutral);
        }

        private static string LogIdForSchool(string pBaseLogId, string pSchoolId)
        {
            return CourtSchoolRegistry.Find(pSchoolId) == null
                ? pBaseLogId
                : pBaseLogId + "__" + pSchoolId;
        }

        private static void RegisterLog(string pId, Color pColor,
            string pIcon = "ui/Icons/traits/iconRujia", string pLocaleId = null)
        {
            if (AssetManager.world_log_library.get(pId) != null) return;
            AssetManager.world_log_library.add(new WorldLogAsset
            {
                id = pId,
                locale_id = pLocaleId ?? pId,
                group = "kings",
                path_icon = pIcon,
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
