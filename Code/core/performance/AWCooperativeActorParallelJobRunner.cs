using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWCooperativeActorParallelJobRunner :
        IAWCooperativeBatchParallelJobRunner<BatchActors, Actor>
    {
        private const string PrepareJobId = "prepare";
        private const string UpdateTimersJobId = "update_timers";
        private const string UpdateVisibilityJobId = "update_visibility";
        private const int TimerRangeSize = 128;

        private TimerRange[] _timerRanges = Array.Empty<TimerRange>();
        private float _activeElapsed;
        private bool _activePaused;

        private static int lastVisibilityFrame = -1;
        private static long _timerBatches;
        private static long _timerActors;
        private static long _timerRangesProcessed;
        private static long _prepareJobsSkipped;
        private static long _visibilityJobsSkipped;
        private static long _visibilityFrames;
        private static long _visibilityActors;

        public bool TrySkipAllBatches(Job<Actor> pJob, int pBatchCount,
            float pElapsed)
        {
            if (pJob == null) return false;
            if (string.Equals(pJob.id, PrepareJobId,
                    StringComparison.Ordinal))
            {
                Interlocked.Add(ref _prepareJobsSkipped, pBatchCount);
                return true;
            }

            if (!string.Equals(pJob.id, UpdateVisibilityJobId,
                    StringComparison.Ordinal) ||
                !AWPerformanceSettings.EnableFramePriorityScheduler)
                return false;

            Interlocked.Add(ref _visibilityJobsSkipped, pBatchCount);
            return true;
        }

        public bool TryRunGroup(IReadOnlyList<BatchActors> pBatches,
            int pJobIndex, int[] pActiveBatchIndices,
            int pActiveBatchCount, float pElapsed,
            ParallelOptions pParallelOptions)
        {
            if (pActiveBatchCount <= 0 || pParallelOptions == null)
                return false;

            BatchActors firstBatch = pBatches[pActiveBatchIndices[0]];
            if (!string.Equals(firstBatch.jobs_parallel[pJobIndex].id,
                    UpdateTimersJobId, StringComparison.Ordinal))
                return false;

            int rangeCount = 0;
            int actorCount = 0;
            EnsureTimerRangeCapacity(pActiveBatchCount * 2);
            for (int i = 0; i < pActiveBatchCount; i++)
            {
                BatchActors batch = pBatches[pActiveBatchIndices[i]];
                ObjectContainer<Actor> container =
                    batch.jobs_parallel[pJobIndex].container;
                if (container == null ||
                    (container.Count <= 0 && !container.isDirtyContainer()))
                {
                    batch._array = Array.Empty<Actor>();
                    batch._count = 0;
                    continue;
                }

                container.checkAddRemove();
                Actor[] actors = container.getFastSimpleArray() ??
                                 Array.Empty<Actor>();
                int count = container.Count;
                batch._array = actors;
                batch._count = count;
                actorCount += count;
                int batchRangeCount =
                    (count + TimerRangeSize - 1) / TimerRangeSize;
                EnsureTimerRangeCapacity(rangeCount + batchRangeCount);
                for (int start = 0; start < count;
                     start += TimerRangeSize)
                    _timerRanges[rangeCount++] = new TimerRange(actors,
                        start, Math.Min(count, start + TimerRangeSize));
            }

            _activeElapsed = pElapsed;
            _activePaused = World.world != null && World.world.isPaused();
            try
            {
                AWSimulationWorkerPool.Instance.RunIndexed(0, rangeCount,
                    RunTimerRange);
            }
            finally
            {
                _activeElapsed = 0f;
                _activePaused = false;
            }

            Interlocked.Add(ref _timerBatches, pActiveBatchCount);
            Interlocked.Add(ref _timerActors, actorCount);
            Interlocked.Add(ref _timerRangesProcessed, rangeCount);
            return true;
        }

        public bool TryRun(BatchActors pBatch, Job<Actor> pJob,
            float pElapsed)
        {
            return false;
        }

        internal static void RefreshFrameVisibility()
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler ||
                !Config.game_loaded ||
                SmoothLoader.isLoading() ||
                lastVisibilityFrame == UnityEngine.Time.frameCount)
                return;

            ActorManager manager = World.world?.units;
            if (manager == null) return;

            lastVisibilityFrame = UnityEngine.Time.frameCount;
            manager.checkContainer();
            manager.prepareArray();
            Actor[] actors = manager.getSimpleArray();
            int count = manager.Count;
            bool renderGameplay = MapBox.isRenderGameplay();
            int updated = 0;
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                ActorAsset asset = actor.asset;
                if (!asset.has_sprite_renderer) continue;

                if (actor.isInMagnet() || actor.isInsideSomething())
                    actor.is_visible = false;
                else if (renderGameplay)
                    actor.is_visible = actor.current_tile.zone.visible;
                else
                    actor.is_visible = asset.visible_on_minimap;
                updated++;
            }

            Interlocked.Increment(ref _visibilityFrames);
            Interlocked.Add(ref _visibilityActors, updated);
        }

        internal static string GetDiagnostics()
        {
            return "timer_batches=" + Interlocked.Read(ref _timerBatches) +
                   " timer_actors=" + Interlocked.Read(ref _timerActors) +
                   " timer_ranges=" +
                   Interlocked.Read(ref _timerRangesProcessed) +
                   " prepare_skipped=" +
                   Interlocked.Read(ref _prepareJobsSkipped) +
                   " visibility_skipped=" +
                   Interlocked.Read(ref _visibilityJobsSkipped) +
                   " visibility_frames=" +
                   Interlocked.Read(ref _visibilityFrames) +
                   " visibility_actors=" +
                   Interlocked.Read(ref _visibilityActors);
        }

        private void RunTimerRange(int pRangeIndex)
        {
            TimerRange range = _timerRanges[pRangeIndex];
            for (int i = range.Start; i < range.End; i++)
                RunTimerActor(range.Actors[i]);
        }

        private void RunTimerActor(Actor pActor)
        {
            pActor._update_done = false;
            pActor._beh_skip = false;
            float elapsed = _activeElapsed;
            if (pActor.timer_jump_animation > 0f)
                pActor.timer_jump_animation -= elapsed;

            if (pActor.dirty_current_tile ||
                (pActor._next_step_tile != null &&
                 (float)Toolbox.SquaredDistTile(pActor.current_tile,
                     pActor._next_step_tile) > 4f))
                pActor.findCurrentTile();

            bool alive = pActor.isAlive();
            pActor._is_in_liquid = pActor.current_tile.is_liquid &&
                pActor.move_jump_offset.y == 0f &&
                pActor.position_height <= 0f && alive;
            if (pActor.asset.update_z && pActor.position_height != 0f)
                pActor.updateFall();
            if (pActor.attackedBy != null &&
                !pActor.attackedBy.isAlive())
                pActor.attackedBy = null;
            if (pActor.is_inside_boat) return;

            if (NeedsFlipUpdate(pActor))
                pActor.updateFlipRotation(elapsed);
            if (pActor.under_forces)
            {
                float multiplier = Math.Max(0f,
                    elapsed / AWFrameSchedulerRules.FixedSimulationStepSeconds);
                for (int i = 0; (float)i < multiplier; i++)
                    pActor.updateVelocity();
            }
            if (_activePaused || !alive) return;

            if (pActor.rotation_cooldown > 0f ||
                pActor.is_unconscious || pActor.target_angle.z != 0f)
                pActor.updateRotations(elapsed);
            if (pActor.attack_timer >= 0f)
                pActor.attack_timer -= elapsed;
            if (MayUpdateWalkJump(pActor))
                pActor.updateWalkJump(
                    AWFrameSchedulerRules.FixedSimulationStepSeconds);
            if (pActor._timeout_targets >= 0f)
                pActor._timeout_targets -=
                    AWFrameSchedulerRules.FixedSimulationStepSeconds;
            if (pActor.timer_action >= 0f)
                pActor.timer_action -= elapsed;
            if (pActor.isAllowedToLookForEnemies())
                pActor.targets_to_ignore_timer.update(elapsed);
            if (pActor.actor_scale != pActor.target_scale)
                pActor.updateChangeScale(elapsed);
            if (!pActor.is_immovable && pActor.is_moving)
            {
                if (pActor._precalc_movement_speed_skips > 0)
                    pActor._precalc_movement_speed_skips--;
                else
                    pActor.precalcMovementSpeed();
            }
        }

        private void EnsureTimerRangeCapacity(int pCapacity)
        {
            if (_timerRanges.Length >= pCapacity) return;
            Array.Resize(ref _timerRanges, Math.Max(
                AWPerformanceSettings.SimulationBatchSize, pCapacity));
        }

        private static bool NeedsFlipUpdate(Actor pActor)
        {
            if (!pActor.asset.can_flip) return false;
            float settledAngle = pActor.flip ? 180f : 0f;
            return pActor.flip_angle != settledAngle ||
                   pActor.target_angle.y != pActor.flip_angle;
        }

        private static bool MayUpdateWalkJump(Actor pActor)
        {
            if ((!pActor.is_visible && pActor.move_jump_offset.y == 0f) ||
                pActor.position_height > 0f ||
                pActor.asset.disable_jump_animation)
                return false;
            return pActor.is_moving ||
                   pActor.move_jump_offset.y != 0f ||
                   pActor._jump_time != 0f;
        }

        private readonly struct TimerRange
        {
            internal TimerRange(Actor[] pActors, int pStart, int pEnd)
            {
                Actors = pActors;
                Start = pStart;
                End = pEnd;
            }

            internal Actor[] Actors { get; }
            internal int Start { get; }
            internal int End { get; }
        }
    }
}
