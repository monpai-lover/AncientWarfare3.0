using System;

namespace AncientWarfare3.core.policy
{
    public enum ActorBatchPerformanceStage
    {
        Unknown = -1,
        ParallelChecks = 0,
        Visibility = 1,
        Stats = 2,
        Nutrition = 3,
        LifeEvents = 4,
        InsideAndChildren = 5,
        SpriteAnimation = 6,
        TileAndStatus = 7,
        ForceAndTargets = 8,
        EnemySearch = 9,
        TaskVerifier = 10,
        PathMovement = 11,
        Decision = 12,
        NaturalDeath = 13,
        Ai = 14,
        SmoothMovement = 15,
        Effects = 16,
        DeathCheck = 17
    }

    public static class ActorBatchPerformanceRules
    {
        public const int StageCount = 18;

        public static ActorBatchPerformanceStage StageForMethod(
            string pMethodName)
        {
            switch (pMethodName)
            {
                case "updateParallelChecks":
                    return ActorBatchPerformanceStage.ParallelChecks;
                case "updateVisibility":
                    return ActorBatchPerformanceStage.Visibility;
                case "updateStats":
                    return ActorBatchPerformanceStage.Stats;
                case "updateNutritionDecay":
                    return ActorBatchPerformanceStage.Nutrition;
                case "updateEventsBecomeAdult":
                case "updateEventsEggHatched":
                case "updateActionLanded":
                    return ActorBatchPerformanceStage.LifeEvents;
                case "u1_checkInside":
                case "u2_updateChildren":
                    return ActorBatchPerformanceStage.InsideAndChildren;
                case "u3_spriteAnimation":
                    return ActorBatchPerformanceStage.SpriteAnimation;
                case "u4_deadCheck":
                case "u5_curTileAction":
                case "u5_checkTileDeath":
                case "u6_checkFrozen":
                case "u7_checkAugmentationEffects":
                case "u8_checkUpdateTimers":
                    return ActorBatchPerformanceStage.TileAndStatus;
                case "b1_checkUnderForce":
                case "b2_checkCurrentEnemyTarget":
                    return ActorBatchPerformanceStage.ForceAndTargets;
                case "b3_findEnemyTarget":
                    return ActorBatchPerformanceStage.EnemySearch;
                case "b4_checkTaskVerifier":
                    return ActorBatchPerformanceStage.TaskVerifier;
                case "b5_checkPathMovement":
                    return ActorBatchPerformanceStage.PathMovement;
                case "b6_0_updateDecision":
                    return ActorBatchPerformanceStage.Decision;
                case "b55_updateNaturalDeaths":
                    return ActorBatchPerformanceStage.NaturalDeath;
                case "b6_updateAI":
                    return ActorBatchPerformanceStage.Ai;
                case "u10_checkSmoothMovement":
                    return ActorBatchPerformanceStage.SmoothMovement;
                case "updateShake":
                case "updateHovering":
                case "updatePollinating":
                    return ActorBatchPerformanceStage.Effects;
                case "updateDeathCheck":
                    return ActorBatchPerformanceStage.DeathCheck;
                default:
                    return ActorBatchPerformanceStage.Unknown;
            }
        }

        public static bool IsValid(ActorBatchPerformanceStage pStage)
        {
            int index = (int)pStage;
            return index >= 0 && index < StageCount;
        }

        public static string Id(ActorBatchPerformanceStage pStage)
        {
            switch (pStage)
            {
                case ActorBatchPerformanceStage.ParallelChecks:
                    return "parallel_checks";
                case ActorBatchPerformanceStage.Visibility: return "visibility";
                case ActorBatchPerformanceStage.Stats: return "stats";
                case ActorBatchPerformanceStage.Nutrition: return "nutrition";
                case ActorBatchPerformanceStage.LifeEvents: return "life_events";
                case ActorBatchPerformanceStage.InsideAndChildren:
                    return "inside_children";
                case ActorBatchPerformanceStage.SpriteAnimation:
                    return "sprite_animation";
                case ActorBatchPerformanceStage.TileAndStatus:
                    return "tile_status";
                case ActorBatchPerformanceStage.ForceAndTargets:
                    return "force_targets";
                case ActorBatchPerformanceStage.EnemySearch:
                    return "enemy_search";
                case ActorBatchPerformanceStage.TaskVerifier:
                    return "task_verifier";
                case ActorBatchPerformanceStage.PathMovement:
                    return "path_movement";
                case ActorBatchPerformanceStage.Decision: return "decision";
                case ActorBatchPerformanceStage.NaturalDeath:
                    return "natural_death";
                case ActorBatchPerformanceStage.Ai: return "ai";
                case ActorBatchPerformanceStage.SmoothMovement:
                    return "smooth_movement";
                case ActorBatchPerformanceStage.Effects: return "effects";
                case ActorBatchPerformanceStage.DeathCheck:
                    return "death_check";
                default: return "unknown";
            }
        }
    }
}
