using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AncientWarfare3.core.performance
{
    internal static class AWPresentationInterpolator
    {
        private static ConditionalWeakTable<Actor, ActorPresentationState>
            _states = new ConditionalWeakTable<Actor, ActorPresentationState>();
        private static int _preparedFrame = -1;
        private static float _presentationDelta = 1f / 60f;
        private static float _presentationTimeScale = 1f;
        private static float _presentationClock;

        public static void PrepareFrame()
        {
            _preparedFrame = Time.frameCount;
            _presentationDelta = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            WorldTimeScaleAsset timeScale = Config.time_scale_asset;
            _presentationTimeScale =
                Mathf.Max(0f, timeScale?.multiplier ?? 1f) *
                Mathf.Max(1, timeScale?.ticks ?? 1);
            _presentationClock = Time.unscaledTime;
        }

        public static void Apply(Actor pActor, ref Vector3 pResult)
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler ||
                !AWPerformanceSettings.EnablePresentationSmoothing ||
                pActor == null ||
                (!pActor.is_visible && !SelectedUnit.isSelected(pActor) &&
                 !ReferenceEquals(pActor,
                     ControllableUnit.getControllableUnit())))
                return;

            Vector2 target = pActor.current_position;
            if (!IsFinite(target)) return;

            ActorPresentationState state = _states.GetValue(pActor,
                _ => new ActorPresentationState());
            Vector2 presented;
            lock (state)
            {
                if (!state.Initialized)
                {
                    state.Initialized = true;
                    state.Presented = target;
                    state.Authoritative = target;
                    state.AuthoritativeChangedAt = _presentationClock;
                    state.LastFrame = _preparedFrame;
                }
                else
                {
                    Vector2 authoritativeDelta =
                        target - state.Authoritative;
                    if (authoritativeDelta.sqrMagnitude > 64f)
                    {
                        state.Presented = target;
                        state.AuthoritySampleCount = 0;
                        state.EstimatedAuthorityInterval = 0.25f;
                        state.AuthoritativeChangedAt = _presentationClock;
                    }
                    else if (authoritativeDelta.sqrMagnitude > 0.000001f)
                    {
                        float interval = _presentationClock -
                                         state.AuthoritativeChangedAt;
                        if (interval >= 0.005f && interval <= 2f)
                        {
                            state.EstimatedAuthorityInterval =
                                state.AuthoritySampleCount == 0
                                    ? interval
                                    : Mathf.Lerp(
                                        state.EstimatedAuthorityInterval,
                                        interval, 0.25f);
                            state.AuthoritySampleCount++;
                        }
                        state.AuthoritativeChangedAt = _presentationClock;
                    }

                    state.Authoritative = target;
                    if (state.LastFrame != _preparedFrame)
                    {
                        state.LastFrame = _preparedFrame;
                        bool controlled = ReferenceEquals(pActor,
                            ControllableUnit.getControllableUnit());
                        bool selected = SelectedUnit.isSelected(pActor);
                        Vector2 movementTarget = pActor.next_step_position;
                        bool canPredictMovement = pActor.is_moving &&
                            IsFinite(movementTarget) &&
                            (movementTarget - target).sqrMagnitude > 0.0001f;

                        if (canPredictMovement)
                        {
                            float emphasis = controlled
                                ? 1.25f
                                : selected ? 1.1f : 1f;
                            float baseSpeed = Mathf.Max(0.4f,
                                pActor._current_combined_movement_speed) *
                                              emphasis;
                            float speed = baseSpeed *
                                          _presentationTimeScale;
                            if (state.AuthoritySampleCount > 0)
                            {
                                float elapsedSinceAuthority =
                                    _presentationClock -
                                    state.AuthoritativeChangedAt;
                                float remainingInterval = Mathf.Max(
                                    _presentationDelta,
                                    state.EstimatedAuthorityInterval -
                                    elapsedSinceAuthority);
                                float cadenceSpeed = Vector2.Distance(
                                    state.Presented, movementTarget) /
                                                     remainingInterval;
                                speed = Mathf.Min(speed,
                                    Mathf.Max(baseSpeed, cadenceSpeed));
                            }

                            state.Presented = Vector2.MoveTowards(
                                state.Presented, movementTarget,
                                speed * _presentationDelta);
                        }
                        else
                        {
                            float responsiveness = controlled
                                ? 45f
                                : selected ? 30f : 18f;
                            float alpha = 1f - Mathf.Exp(
                                -responsiveness * _presentationDelta);
                            state.Presented = Vector2.LerpUnclamped(
                                state.Presented, target, alpha);
                            if ((state.Presented - target).sqrMagnitude <
                                0.0001f)
                                state.Presented = target;
                        }
                    }
                }

                presented = state.Presented;
            }

            Vector2 shake = pActor.shake_offset;
            Vector2 jump = pActor.move_jump_offset;
            pActor.current_shadow_position.Set(presented.x + shake.x,
                presented.y + shake.y);
            pActor.cur_transform_position.Set(
                presented.x + jump.x + shake.x,
                presented.y + jump.y + shake.y + pActor.position_height,
                pActor.position_height);
            pResult = pActor.cur_transform_position;
        }

        public static void Reset()
        {
            _states =
                new ConditionalWeakTable<Actor, ActorPresentationState>();
            _preparedFrame = -1;
            _presentationDelta = 1f / 60f;
            _presentationTimeScale = 1f;
            _presentationClock = 0f;
        }

        private static bool IsFinite(Vector2 pValue)
        {
            return !float.IsNaN(pValue.x) &&
                   !float.IsInfinity(pValue.x) &&
                   !float.IsNaN(pValue.y) &&
                   !float.IsInfinity(pValue.y);
        }

        private sealed class ActorPresentationState
        {
            public bool Initialized;
            public Vector2 Presented;
            public Vector2 Authoritative;
            public float AuthoritativeChangedAt;
            public float EstimatedAuthorityInterval = 0.25f;
            public int AuthoritySampleCount;
            public int LastFrame;
        }
    }
}
