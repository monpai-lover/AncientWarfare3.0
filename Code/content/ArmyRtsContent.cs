using System.Collections.Generic;
using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.presentation;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class ArmyRtsContent
    {
        public const string CaptainJobId = "aw_army_rts_captain";
        public const string HoldJobId = "aw_army_rts_front_hold";
        public const string FollowerJobId = "aw_army_rts_follower";
        public const string VanillaFollowerJobId =
            "aw_army_rts_vanilla_follower";
        public const string RetreatCaptainJobId =
            "aw_army_rts_retreat_captain";
        public const string RetreatFollowerJobId =
            "aw_army_rts_retreat_follower";
        public const string MissionTaskId = "aw_army_rts_mission";
        public const string RallyTaskId = "aw_army_rts_rally";
        public const string ReplenishTaskId = "aw_army_rts_replenish";
        public const string MarchTaskId = "aw_army_rts_march";
        public const string DeployTaskId = "aw_army_rts_deploy";
        public const string AssaultTaskId = "aw_army_rts_assault";
        public const string PursueTaskId = "aw_army_rts_pursue";
        public const string RetreatTaskId = "aw_army_rts_retreat";
        public const string RegroupTaskId = "aw_army_rts_regroup";
        public const string AwaitingPickupTaskId =
            "aw_army_rts_transport_awaiting_pickup";
        public const string EmbarkingTaskId =
            "aw_army_rts_transport_embarking";
        public const string SailingTaskId =
            "aw_army_rts_transport_sailing";
        public const string LandingTaskId =
            "aw_army_rts_transport_landing";
        public const string HoldMissionTaskId = "aw_army_rts_hold";
        public const string CaptainCombatTaskId =
            "aw_army_rts_captain_combat";
        public const string CaptainSiegeAdvanceTaskId =
            "aw_army_rts_captain_siege_advance";
        public const string SiegeCombatTaskId =
            "aw_army_rts_siege_combat";
        public const string MemberCombatJobId = "aw_army_rts_member_combat";
        public const string MemberCombatTaskId =
            "aw_army_rts_member_combat";
        public const string HoldTaskId = "aw_army_rts_front_hold";
        public const string FormationTaskId = "aw_army_rts_formation";
        public const string MobilizationSpeedStatusId =
            "aw_army_rts_mobilizing";
        private const float MobilizationSpeedBonus = 3.5f;
        private const float MobilizationStatusDuration = 20f;
        private static readonly string[] VanillaStrategicDecisionIds =
        {
            "warrior_army_captain_idle_walking_city",
            "warrior_army_captain_waiting",
            "warrior_army_leader_move_random",
            "warrior_army_leader_move_to_attack_target",
            "warrior_army_follow_leader",
            "warrior_random_move",
            "check_warrior_transport"
        };
        private static readonly Dictionary<string, DecisionAsset>
            InstalledDecisionAssets = new Dictionary<string, DecisionAsset>();
        private static readonly Dictionary<string, DecisionAction>
            InstalledDecisionActions = new Dictionary<string, DecisionAction>();

        public static void Init()
        {
            RegisterMobilizationStatus();
            ArmyRtsAttackSpeechBubbleService.RegisterAsset();
            InterceptVanillaStrategicDecisions();
            if (!AssetManager.job_actor.has(CaptainJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = CaptainJobId
                });
                AddCaptainTasks(job);
            }
            if (!AssetManager.job_actor.has(FollowerJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = FollowerJobId
                });
                job.addTask(FormationTaskId);
            }
            if (!AssetManager.job_actor.has(VanillaFollowerJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = VanillaFollowerJobId
                });
                job.addTask("warrior_army_follow_leader");
            }
            if (!AssetManager.job_actor.has(MemberCombatJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = MemberCombatJobId
                });
                job.addTask(MemberCombatTaskId);
                job.addTask(SiegeCombatTaskId);
            }
            if (!AssetManager.job_actor.has(HoldJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = HoldJobId
                });
                job.addTask(HoldTaskId);
            }
            if (!AssetManager.job_actor.has(RetreatCaptainJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = RetreatCaptainJobId
                });
                job.addTask(RetreatTaskId);
            }
            if (!AssetManager.job_actor.has(RetreatFollowerJobId))
            {
                ActorJob job = AssetManager.job_actor.add(new ActorJob
                {
                    id = RetreatFollowerJobId
                });
                job.addTask("warrior_army_follow_leader");
            }

            RegisterCaptainTask(MissionTaskId,
                "task_unit_aw_army_rts_mission", "ui/Icons/iconWar");
            RegisterCaptainTask(RallyTaskId,
                "task_unit_aw_army_rts_rally", "ui/Icons/iconLoyalty");
            RegisterCaptainTask(ReplenishTaskId,
                "task_unit_aw_army_rts_replenish", "ui/Icons/iconWar");
            RegisterCaptainTask(MarchTaskId,
                "task_unit_aw_army_rts_march",
                "ui/Icons/iconArrowAttackTarget");
            RegisterCaptainTask(DeployTaskId,
                "task_unit_aw_army_rts_deploy",
                "ui/Icons/iconArrowAttackTarget");
            RegisterCaptainTask(AssaultTaskId,
                "task_unit_aw_army_rts_assault", "ui/Icons/iconWar");
            RegisterCaptainTask(PursueTaskId,
                "task_unit_aw_army_rts_pursue",
                "ui/Icons/iconArrowAttackTarget");
            RegisterRetreatTask();
            RegisterCaptainTask(RegroupTaskId,
                "task_unit_aw_army_rts_regroup", "ui/Icons/iconLoyalty");
            RegisterCaptainTask(AwaitingPickupTaskId,
                "task_unit_aw_army_rts_transport_awaiting_pickup",
                "ui/Icons/iconLoyalty");
            RegisterCaptainTask(EmbarkingTaskId,
                "task_unit_aw_army_rts_transport_embarking",
                "ui/Icons/iconLoyalty");
            RegisterCaptainTask(SailingTaskId,
                "task_unit_aw_army_rts_transport_sailing",
                "ui/Icons/iconArrowAttackTarget");
            RegisterCaptainTask(LandingTaskId,
                "task_unit_aw_army_rts_transport_landing",
                "ui/Icons/iconArrowAttackTarget");
            RegisterCaptainTask(HoldMissionTaskId,
                "task_unit_aw_army_rts_front_hold", "ui/Icons/iconLoyalty");
            RegisterCaptainCombatTask();
            RegisterCaptainSiegeAdvanceTask();
            RegisterSiegeCombatTask();
            RegisterMemberCombatTask();

            if (!AssetManager.tasks_actor.has(FormationTaskId))
            {
                var formation = AssetManager.tasks_actor.add(
                    new BehaviourTaskActor
                    {
                        id = FormationTaskId,
                        cancellable_by_reproduction = false,
                        cancellable_by_socialize = false,
                        speed_multiplier = 1f,
                        locale_key = "task_unit_aw_army_rts_formation"
                });
                formation.setIcon("ui/Icons/iconLoyalty");
                formation.addBeh(new BehArmyRtsFormation());
                formation.addBeh(new BehGoToTileTarget
                {
                    limit_pathfinding_regions = 0
                });
                formation.addBeh(new BehRandomWait(0.1f, 0.3f));
            }

            if (!AssetManager.tasks_actor.has(HoldTaskId))
            {
                var hold = AssetManager.tasks_actor.add(
                    new BehaviourTaskActor
                    {
                        id = HoldTaskId,
                        cancellable_by_reproduction = false,
                        cancellable_by_socialize = false,
                        speed_multiplier = 1f,
                        locale_key = "task_unit_aw_army_rts_front_hold"
                    });
                hold.setIcon("ui/Icons/iconLoyalty");
                hold.addBeh(new BehArmyRtsFrontHold());
                hold.addBeh(new BehRandomWait(0.15f, 0.3f));
            }
        }

        internal static string ResolveCaptainTaskId(ArmyRtsState pState,
            ArmyRtsTransportPhase pTransportPhase)
        {
            switch (pTransportPhase)
            {
                case ArmyRtsTransportPhase.AwaitingPickup:
                    return AwaitingPickupTaskId;
                case ArmyRtsTransportPhase.Embarking:
                    return EmbarkingTaskId;
                case ArmyRtsTransportPhase.Sailing:
                    return SailingTaskId;
                case ArmyRtsTransportPhase.Landing:
                    return LandingTaskId;
            }
            switch (pState)
            {
                case ArmyRtsState.Rally: return RallyTaskId;
                case ArmyRtsState.Replenish: return ReplenishTaskId;
                case ArmyRtsState.March: return MarchTaskId;
                case ArmyRtsState.Deploy: return DeployTaskId;
                case ArmyRtsState.Assault: return AssaultTaskId;
                case ArmyRtsState.Pursue: return PursueTaskId;
                case ArmyRtsState.Retreat: return RetreatTaskId;
                case ArmyRtsState.Regroup: return RegroupTaskId;
                case ArmyRtsState.Hold: return HoldMissionTaskId;
                default: return MissionTaskId;
            }
        }

        private static void AddCaptainTasks(ActorJob pJob)
        {
            pJob.addTask(MissionTaskId);
            pJob.addTask(RallyTaskId);
            pJob.addTask(ReplenishTaskId);
            pJob.addTask(MarchTaskId);
            pJob.addTask(DeployTaskId);
            pJob.addTask(AssaultTaskId);
            pJob.addTask(PursueTaskId);
            pJob.addTask(RetreatTaskId);
            pJob.addTask(RegroupTaskId);
            pJob.addTask(AwaitingPickupTaskId);
            pJob.addTask(EmbarkingTaskId);
            pJob.addTask(SailingTaskId);
            pJob.addTask(LandingTaskId);
            pJob.addTask(HoldMissionTaskId);
            pJob.addTask(CaptainCombatTaskId);
            pJob.addTask(CaptainSiegeAdvanceTaskId);
            pJob.addTask(SiegeCombatTaskId);
        }

        private static void RegisterCaptainCombatTask()
        {
            if (AssetManager.tasks_actor.has(CaptainCombatTaskId)) return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = CaptainCombatTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                in_combat = true,
                speed_multiplier = 1f,
                locale_key = "task_unit_aw_army_rts_captain_combat"
            });
            task.setIcon("ui/Icons/iconWar");
            task.addBeh(new BehArmyRtsCaptainCombat());
            task.addBeh(new BehGoToActorTarget(
                GoToActorTargetType.RaycastWithAttackRange,
                false, true, true));
            task.addBeh(new BehArmyRtsCaptainAttack());
            task.addBeh(new BehRestartTask());
        }

        private static void RegisterMemberCombatTask()
        {
            if (AssetManager.tasks_actor.has(MemberCombatTaskId)) return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = MemberCombatTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                in_combat = true,
                speed_multiplier = 1f,
                locale_key = "task_unit_aw_army_rts_member_combat"
            });
            task.setIcon("ui/Icons/iconWar");
            task.addBeh(new BehArmyRtsMemberCombat());
            task.addBeh(new BehGoToActorTarget(
                GoToActorTargetType.RaycastWithAttackRange,
                false, true, true));
            task.addBeh(new BehArmyRtsCaptainAttack());
            task.addBeh(new BehRestartTask());
        }

        private static void RegisterSiegeCombatTask()
        {
            if (AssetManager.tasks_actor.has(SiegeCombatTaskId)) return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = SiegeCombatTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                in_combat = true,
                speed_multiplier = 1f,
                locale_key = "task_unit_aw_army_rts_siege_combat"
            });
            task.setIcon("ui/Icons/iconWar");
            task.addBeh(new BehArmyRtsSiegeCombat());
            task.addBeh(new BehGoToActorTarget(
                GoToActorTargetType.RaycastWithAttackRange,
                false, true, true));
            task.addBeh(new BehArmyRtsCaptainAttack());
            task.addBeh(new BehRestartTask());
        }

        private static void RegisterCaptainSiegeAdvanceTask()
        {
            if (AssetManager.tasks_actor.has(CaptainSiegeAdvanceTaskId))
                return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = CaptainSiegeAdvanceTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                in_combat = true,
                speed_multiplier = 1f,
                locale_key = "task_unit_aw_army_rts_captain_siege_advance"
            });
            task.setIcon("ui/Icons/iconWar");
            task.addBeh(new BehArmyRtsSiegeAdvance());
            task.addBeh(new BehGoToTileTarget
            {
                limit_pathfinding_regions = 0
            });
            task.addBeh(new BehRandomWait(0.1f, 0.25f));
        }

        private static void RegisterCaptainTask(string pId, string pLocaleKey,
            string pIcon)
        {
            if (AssetManager.tasks_actor.has(pId)) return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = pId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1f,
                locale_key = pLocaleKey
            });
            task.setIcon(pIcon);
            task.addBeh(new BehArmyRtsMission());
            task.addBeh(new BehGoToTileTarget
            {
                limit_pathfinding_regions = 0
            });
            task.addBeh(new BehRandomWait(0.1f, 0.25f));
        }

        private static void RegisterRetreatTask()
        {
            if (AssetManager.tasks_actor.has(RetreatTaskId)) return;
            var task = AssetManager.tasks_actor.add(new BehaviourTaskActor
            {
                id = RetreatTaskId,
                cancellable_by_reproduction = false,
                cancellable_by_socialize = false,
                speed_multiplier = 1.15f,
                locale_key = "task_unit_aw_army_rts_retreat"
            });
            task.setIcon("ui/Icons/iconLoyalty");
            task.addBeh(new BehArmyRtsRetreatTarget());
            task.addBeh(new BehGoToTileTarget
            {
                limit_pathfinding_regions = 6
            });
            task.addBeh(new BehWarriorCaptainWait());
            task.addBeh(new BehRestartTask());
        }

        private static void RegisterMobilizationStatus()
        {
            if (AssetManager.status.has(MobilizationSpeedStatusId)) return;
            var status = new StatusAsset
            {
                id = MobilizationSpeedStatusId,
                duration = MobilizationStatusDuration,
                allow_timer_reset = true,
                path_icon = "ui/Icons/iconArrowAttackTarget",
                locale_id = "status_title_aw_army_rts_mobilizing",
                locale_description =
                    "status_description_aw_army_rts_mobilizing"
            };
            status.base_stats["speed"] = MobilizationSpeedBonus;
            AssetManager.status.add(status);
        }

        private static void InterceptVanillaStrategicDecisions()
        {
            for (int i = 0; i < VanillaStrategicDecisionIds.Length; i++)
            {
                string decisionId = VanillaStrategicDecisionIds[i];
                DecisionAsset decision = AssetManager.decisions_library
                    .get(decisionId);
                if (decision == null)
                {
                    ModClass.LogWarning(
                        "[Army RTS] Missing vanilla decision asset: " +
                        decisionId);
                    continue;
                }
                if (InstalledDecisionAssets.TryGetValue(decisionId,
                        out DecisionAsset installedAsset) &&
                    InstalledDecisionActions.TryGetValue(decisionId,
                        out DecisionAction installedAction) &&
                    ReferenceEquals(installedAsset, decision) &&
                    decision.action_check_launch == installedAction)
                    continue;

                DecisionAction upstream = decision.action_check_launch;
                decision.action_check_launch = pActor =>
                    ArmyRtsRuntimeModeRules.ShouldAllowVanillaStrategicDecision(
                        ArmyRtsRuntimeMode.Current, decisionId,
                        ArmyRtsControllerService.OwnsLiveActor(pActor)) &&
                    (upstream == null || upstream(pActor));
                InstalledDecisionAssets[decisionId] = decision;
                InstalledDecisionActions[decisionId] =
                    decision.action_check_launch;
            }
        }
    }
}
