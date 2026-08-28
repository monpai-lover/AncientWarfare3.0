using System;
using System.Collections.Generic;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsMovementDiagnostic
    {
        private const double RepeatIntervalSeconds = 2d;
        private const int MaximumEntries = 8192;
        private const int MemberSampleModulo = 16;

        private sealed class Entry
        {
            internal string Signature;
            internal double Realtime;
        }

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>();

        internal static bool Enabled =>
            AWPerformanceSettings.ArmyRtsDiagnosticsEnabled;

        // P0 每个 actor 每帧要把 kind 拼进诊断详情十余次。Log 内部虽然一进门
        // 就按开关返回,但实参在调用之前就已经求值完毕 —— 枚举 ToString 加拼接
        // 每采样区间要跑数万次,开关关着也照付。换成常量表后关闭时几乎零成本,
        // 打开时也不再分配。
        internal static string KindDetail(
            ArmyMilitaryMovementPriorityKind pKind)
        {
            return pKind == ArmyMilitaryMovementPriorityKind.RoyalGuard
                ? "kind=RoyalGuard"
                : "kind=RtsMember";
        }

        internal static void Log(string pScope, string pStage,
            Actor pActor, string pDetail = "")
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            long actorId = pActor?.data?.id ?? -1L;
            bool captain = IsCaptain(pActor);
            bool royalGuard = string.Equals(pScope, "guard",
                                  StringComparison.Ordinal) ||
                              Contains(pDetail, "kind=RoyalGuard");
            bool anomaly = Contains(pStage, "exception") ||
                           Contains(pStage, "rejected") ||
                           Contains(pStage, "failed") ||
                           Contains(pStage, "out_of_bounds") ||
                           Contains(pStage, "empty_target") ||
                           Contains(pDetail, "result=False") ||
                           Contains(pDetail, "error=");
            bool schedulerStage = string.Equals(pStage, "p0_chunk_begin",
                StringComparison.Ordinal);
            if (!ArmyRtsP0Rules.ShouldWriteDiagnosticStage(pStage,
                    anomaly)) return;
            if (!schedulerStage &&
                !ArmyRtsP0Rules.ShouldTraceDiagnosticActor(captain,
                    royalGuard, actorId, anomaly, MemberSampleModulo)) return;
            string key = (pScope ?? "unknown") + ":" +
                         (pStage ?? "unknown") + ":" +
                         (schedulerStage ? -1L : actorId);
            double now = Time.realtimeSinceStartupAsDouble;
            // 限流先于状态构造:BuildState 会拼出数百字节的诊断串,而绝大多数
            // 调用最终都被时间窗挡掉。先做纯时间判定,确定要写才付分配代价。
            bool hasPrevious = Entries.TryGetValue(key, out Entry previous);
            if (hasPrevious)
            {
                double elapsedTicks = now - previous.Realtime;
                if (ArmyRtsP0Rules.ShouldRateLimitDiagnostic(anomaly,
                        elapsedTicks, RepeatIntervalSeconds)) return;
            }
            string state = schedulerStage
                ? BuildSchedulerState(pDetail)
                : BuildState(pActor, pDetail);
            if (hasPrevious)
            {
                double elapsed = now - previous.Realtime;
                if (string.Equals(previous.Signature, state,
                        StringComparison.Ordinal) &&
                    elapsed < RepeatIntervalSeconds) return;
            }
            if (Entries.Count >= MaximumEntries) Entries.Clear();
            Entries[key] = new Entry
            {
                Signature = state,
                Realtime = now
            };
            ModClass.LogInfo("[AW3 military P0] scope=" +
                             (pScope ?? "unknown") +
                             " stage=" + (pStage ?? "unknown") +
                             " actor=" + actorId + state);
        }

        private static string BuildSchedulerState(string pDetail)
        {
            return string.IsNullOrWhiteSpace(pDetail)
                ? string.Empty
                : " " + pDetail.Replace('\r', ' ').Replace('\n', ' ');
        }

        private static bool IsCaptain(Actor pActor)
        {
            try
            {
                return pActor?.army?.data != null &&
                       ReferenceEquals(pActor.army.getCaptain(), pActor);
            }
            catch { return false; }
        }

        private static bool Contains(string pValue, string pNeedle)
        {
            return pValue?.IndexOf(pNeedle,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildState(Actor pActor, string pDetail)
        {
            int localCount;
            int localIndex;
            int globalCount;
            bool localFollowing;
            bool moving;
            try
            {
                localCount = pActor?.current_path?.Count ?? 0;
                localIndex = pActor?.current_path_index ?? 0;
                globalCount = pActor?.current_path_global?.Count ?? 0;
                localFollowing = pActor?.isFollowingLocalPath() == true;
                moving = pActor?.is_moving == true;
            }
            catch
            {
                localCount = 0;
                localIndex = 0;
                globalCount = 0;
                localFollowing = false;
                moving = false;
            }
            long threatId = -1L;
            long attackTargetId = -1L;
            float targetDistance = float.NaN;
            float health = float.NaN;
            float targetHealth = float.NaN;
            int aiActionIndex = -1;
            string aiAction = "none";
            string aiJob = "none";
            string aiTask = "none";
            int aiTaskActionCount = -1;
            bool aiTaskInCombat = false;
            float attackTimer = float.NaN;
            bool behaviourSkipped = false;
            try
            {
                aiActionIndex = pActor?.ai?.action_index ?? -1;
                aiAction = pActor?.ai?.action?.id ?? "none";
                aiJob = pActor?.ai?.job?.id ?? "none";
                aiTask = pActor?.ai?.task?.id ?? "none";
                aiTaskActionCount = pActor?.ai?.task?.list?.Count ?? -1;
                aiTaskInCombat = pActor?.ai?.task?.in_combat == true;
                attackTimer = pActor?.attack_timer ?? float.NaN;
                behaviourSkipped = pActor?._beh_skip == true;
                if (pActor?.beh_actor_target?.isActor() == true)
                    threatId = pActor.beh_actor_target.a?.data?.id ?? -1L;
                if (pActor?.has_attack_target == true &&
                    pActor.attack_target?.isActor() == true)
                {
                    Actor target = pActor.attack_target.a;
                    attackTargetId = target?.data?.id ?? -1L;
                    if (target?.data != null)
                    {
                        float dx = target.current_position.x -
                                   pActor.current_position.x;
                        float dy = target.current_position.y -
                                   pActor.current_position.y;
                        targetDistance = (float)Math.Sqrt(dx * dx + dy * dy);
                        targetHealth = target.getHealth();
                    }
                }
                health = pActor?.getHealth() ?? float.NaN;
            }
            catch { }
            int currentX = int.MinValue;
            int currentY = int.MinValue;
            int behaviourX = int.MinValue;
            int behaviourY = int.MinValue;
            int nativeX = int.MinValue;
            int nativeY = int.MinValue;
            int mapWidth = 0;
            int mapHeight = 0;
            float positionX = float.NaN;
            float positionY = float.NaN;
            float nextX = float.NaN;
            float nextY = float.NaN;
            bool inLiquid = false;
            bool tileGround = false;
            bool tileLiquid = false;
            bool tileOcean = false;
            try
            {
                currentX = pActor?.current_tile?.x ?? int.MinValue;
                currentY = pActor?.current_tile?.y ?? int.MinValue;
                behaviourX = pActor?.beh_tile_target?.x ?? int.MinValue;
                behaviourY = pActor?.beh_tile_target?.y ?? int.MinValue;
                nativeX = pActor?.tile_target?.x ?? int.MinValue;
                nativeY = pActor?.tile_target?.y ?? int.MinValue;
                mapWidth = MapBox.width;
                mapHeight = MapBox.height;
                positionX = pActor.current_position.x;
                positionY = pActor.current_position.y;
                nextX = pActor.next_step_position.x;
                nextY = pActor.next_step_position.y;
                inLiquid = pActor.isInLiquid();
                tileGround = pActor.current_tile?.Type?.ground == true;
                tileLiquid = pActor.current_tile?.Type?.liquid == true;
                tileOcean = pActor.current_tile?.Type?.ocean == true;
            }
            catch { }
            int firstPathTile = PathTileId(pActor, 0);
            int lastPathTile = PathTileId(pActor, localCount - 1);
            string detail = string.IsNullOrWhiteSpace(pDetail)
                ? string.Empty
                : " " + pDetail.Replace('\r', ' ').Replace('\n', ' ');
            return " army=" + (pActor?.army?.id ?? -1L) +
                             " task=" + aiTask +
                   " ai_job=" + aiJob +
                   " ai_action=" + aiAction +
                   " ai_action_index=" + aiActionIndex +
                   " task_action_count=" + aiTaskActionCount +
                   " task_in_combat=" + aiTaskInCombat +
                   " beh_skip=" + behaviourSkipped +
                   " attack_timer=" + attackTimer.ToString("0.###") +
                   " current_tile=" +
                   (pActor?.current_tile?.data?.tile_id ?? -1) +
                   " beh_target=" +
                   (pActor?.beh_tile_target?.data?.tile_id ?? -1) +
                   " native_target=" +
                   (pActor?.tile_target?.data?.tile_id ?? -1) +
                   " threat=" + threatId +
                   " attack_target=" + attackTargetId +
                   " target_distance=" +
                   targetDistance.ToString("0.###") +
                   " health=" + health.ToString("0.###") +
                   " target_health=" + targetHealth.ToString("0.###") +
                   " local_path=" + localCount +
                   " local_index=" + localIndex +
                   " local_following=" + localFollowing +
                   " global_path=" + globalCount +
                   " aw_path_owned=" +
                   (pActor != null &&
                    AWPathMovementBridge.HasOwnership(pActor)) +
                   " moving=" + moving +
                   " inside_boat=" +
                   (pActor?.is_inside_boat == true) +
                   " in_liquid=" + inLiquid +
                   " tile_ground=" + tileGround +
                   " tile_liquid=" + tileLiquid +
                   " tile_ocean=" + tileOcean +
                   " current_xy=" + currentX + "," + currentY +
                   " beh_xy=" + behaviourX + "," + behaviourY +
                   " native_xy=" + nativeX + "," + nativeY +
                   " map=" + mapWidth + "x" + mapHeight +
                   " pos=" + positionX.ToString("0.###") + "," +
                       positionY.ToString("0.###") +
                   " next=" + nextX.ToString("0.###") + "," +
                       nextY.ToString("0.###") +
                   " path_first=" + firstPathTile +
                   " path_last=" + lastPathTile + detail;
        }

        internal static void LogOutOfBounds(Actor pActor, string pSource)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled ||
                pActor?.data == null) return;
            float x;
            float y;
            float nextX;
            float nextY;
            bool moving;
            try
            {
                x = pActor.current_position.x;
                y = pActor.current_position.y;
                nextX = pActor.next_step_position.x;
                nextY = pActor.next_step_position.y;
                moving = pActor.is_moving;
            }
            catch { return; }
            float width = MapBox.width;
            float height = MapBox.height;
            bool currentOutside = float.IsNaN(x) || float.IsNaN(y) ||
                x < 0f || y < 0f || x > width || y > height;
            bool nextTargetEmpty = !AWPathLifecycleRules.
                IsValidMovementTarget(nextX, nextY);
            bool nextOutside = !nextTargetEmpty &&
                !AWPathLifecycleRules.IsInsideMap(nextX, nextY,
                    width, height);
            if (!AWPathLifecycleRules.ShouldReportMovementAnomaly(
                    currentOutside, nextOutside, nextTargetEmpty, moving))
                return;
            string stage = currentOutside || nextOutside
                ? "out_of_bounds"
                : "empty_target_while_moving";
            Log("movement", stage, pActor,
                "source=" + (pSource ?? "unknown") +
                " current_outside=" + currentOutside +
                " next_outside=" + nextOutside +
                " moving=" + moving +
                " current=" + x.ToString("0.###") + "," +
                    y.ToString("0.###") +
                " next=" + nextX.ToString("0.###") + "," +
                    nextY.ToString("0.###") +
                " map=" + width + "x" + height);
        }

        private static int PathTileId(Actor pActor, int pIndex)
        {
            if (pActor?.current_path == null || pIndex < 0) return -1;
            try
            {
                if (pIndex >= pActor.current_path.Count) return -1;
                return pActor.current_path[pIndex]?.data?.tile_id ?? -1;
            }
            catch { return -1; }
        }
    }
}
