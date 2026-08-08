using System;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace AncientWarfare3.core.performance;

internal static class AWPresentationInterpolator
{
    private static ConditionalWeakTable<Actor, AWActorPresentationState> states = new();
    private static int preparedFrame = -1;
    private static float presentationDelta = 1f / 60f;
    private static float presentationTimeScale = 1f;
    private static float presentationClock;

    public static void PrepareFrame()
    {
        preparedFrame = Time.frameCount;
        presentationDelta = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
        presentationTimeScale = World.world?.isPaused() == true
            ? 0f
            : AWWorldTimeRateTracker.HasActualSpeed
                ? Mathf.Max(0f, AWWorldTimeRateTracker.ActualSpeed)
                : AWWorldTimeRateTracker.GetRequestedSpeed();
        presentationClock = Time.unscaledTime;

    }

    public static bool TryApply(Actor actor, out Vector3 result)
    {
        result = default;
        if (!AWPerformanceSettings.EnableActorPresentationSnapshots ||
            !AWPerformanceSettings.EnableFramePriorityScheduler ||
            actor == null ||
            !AWActorPresentationRenderer.TryGetPreparedSample(
                actor,
                out AWActorPresentationSample sample))
        {
            return false;
        }

        long actorId = sample.Handle.ActorId;
        bool controlled = AWActorPresentationRenderer.IsControlled(actorId);
        bool selected = AWActorPresentationRenderer.IsSelected(actorId);
        if (!TryResolve(
                in sample,
                selected,
                controlled,
                out result,
                out _))
        {
            result = default;
            return false;
        }

        return true;
    }

    internal static bool TryResolve(
        in AWActorPresentationSample sample,
        bool selected,
        bool controlled,
        out Vector3 transformPosition,
        out Vector2 shadowPosition)
    {
        return TryResolve(
            in sample,
            selected,
            controlled,
            out transformPosition,
            out shadowPosition,
            out _);
    }

    internal static bool TryResolve(
        in AWActorPresentationSample sample,
        bool selected,
        bool controlled,
        out Vector3 transformPosition,
        out Vector2 shadowPosition,
        out bool requiresContinuousUpdate)
    {
        Vector2 target = sample.Position;
        if (!IsFinite(target))
        {
            transformPosition = default;
            shadowPosition = default;
            requiresContinuousUpdate = false;
            return false;
        }

        Vector2 presented = target;
        requiresContinuousUpdate = false;
        if (AWPerformanceSettings.EnablePresentationSmoothing)
        {
            presented = ResolveSmoothedPosition(
                in sample,
                target,
                selected,
                controlled,
                out requiresContinuousUpdate);
        }

        Vector2 shake = sample.ShakeOffset;
        Vector2 jump = sample.JumpOffset;
        shadowPosition = default;
        shadowPosition.Set(presented.x + shake.x, presented.y + shake.y);
        transformPosition = default;
        transformPosition.Set(
            presented.x + jump.x + shake.x,
            presented.y + jump.y + shake.y + sample.PositionHeight,
            sample.PositionHeight);
        return true;
    }

    private static Vector2 ResolveSmoothedPosition(
        in AWActorPresentationSample sample,
        Vector2 target,
        bool selected,
        bool controlled,
        out bool requiresContinuousUpdate)
    {
        Actor actor = sample.ActorReference;
        if (actor == null)
        {
            requiresContinuousUpdate = false;
            return target;
        }
        AWActorPresentationState state = states.GetValue(actor, CreateState);

        if (IsWorldPaused())
        {
            state.Initialized = true;
            state.Presented = target;
            state.Authoritative = target;
            state.AuthoritySampleCount = 0;
            state.EstimatedAuthorityInterval = 0.25f;
            state.AuthoritativeChangedAt = presentationClock;
            state.LastFrame = preparedFrame;
            requiresContinuousUpdate = false;
            return target;
        }

        Vector2 presented;
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.Presented = target;
            state.Authoritative = target;
            state.AuthoritativeChangedAt = presentationClock;
            state.LastFrame = preparedFrame;
        }
        else
        {
            Vector2 authoritativeDelta = target - state.Authoritative;
            if (authoritativeDelta.sqrMagnitude > 64f)
            {
                state.Presented = target;
                state.AuthoritySampleCount = 0;
                state.EstimatedAuthorityInterval = 0.25f;
                state.AuthoritativeChangedAt = presentationClock;
            }
            else if (authoritativeDelta.sqrMagnitude > 0.000001f)
            {
                float interval = presentationClock - state.AuthoritativeChangedAt;
                if (interval is >= 0.005f and <= 2f)
                {
                    state.EstimatedAuthorityInterval = state.AuthoritySampleCount == 0
                        ? interval
                        : Mathf.Lerp(state.EstimatedAuthorityInterval, interval, 0.25f);
                    state.AuthoritySampleCount++;
                }

                state.AuthoritativeChangedAt = presentationClock;
            }

            state.Authoritative = target;
            if (state.LastFrame != preparedFrame)
            {
                state.LastFrame = preparedFrame;
                Vector2 movementTarget = sample.NextStepPosition;
                bool canPredictMovement =
                    sample.HasFlag(AWActorPresentationFlags.Moving) &&
                    IsFinite(movementTarget) &&
                    (movementTarget - target).sqrMagnitude > 0.0001f;

                if (canPredictMovement)
                {
                    float emphasis = controlled ? 1.25f : selected ? 1.1f : 1f;
                    float baseSpeed = Mathf.Max(0.4f, sample.MovementSpeed) * emphasis;
                    float speed = baseSpeed * presentationTimeScale;
                    if (state.AuthoritySampleCount > 0)
                    {
                        float elapsedSinceAuthority = presentationClock - state.AuthoritativeChangedAt;
                        float remainingInterval = Mathf.Max(
                            presentationDelta,
                            state.EstimatedAuthorityInterval - elapsedSinceAuthority);
                        float cadenceSpeed = Vector2.Distance(state.Presented, movementTarget) /
                                             remainingInterval;
                        speed = Mathf.Min(speed, Mathf.Max(baseSpeed, cadenceSpeed));
                    }

                    state.Presented = Vector2.MoveTowards(
                        state.Presented,
                        movementTarget,
                        speed * presentationDelta);
                }
                else
                {
                    float responsiveness = controlled ? 45f : selected ? 30f : 18f;
                    float alpha = 1f - Mathf.Exp(-responsiveness * presentationDelta);
                    state.Presented = Vector2.LerpUnclamped(state.Presented, target, alpha);
                    if ((state.Presented - target).sqrMagnitude < 0.0001f)
                    {
                        state.Presented = target;
                    }
                }
            }
        }

        presented = state.Presented;
        Vector2 nextStep = sample.NextStepPosition;
        bool predictingMovement =
            sample.HasFlag(AWActorPresentationFlags.Moving) &&
            IsFinite(nextStep) &&
            (nextStep - target).sqrMagnitude > 0.0001f;
        Vector2 settledTarget =
            predictingMovement ? nextStep : target;
        requiresContinuousUpdate =
            (presented - settledTarget).sqrMagnitude > 0.0001f;
        return presented;
    }

    public static void Reset()
    {
        states = new ConditionalWeakTable<Actor, AWActorPresentationState>();
        preparedFrame = -1;
        presentationDelta = 1f / 60f;
        presentationTimeScale = 1f;
        presentationClock = 0f;
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x) &&
               !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) &&
               !float.IsInfinity(value.y);
    }

    private static bool IsWorldPaused()
    {
        return Config.paused ||
               (World.world != null && World.world.isPaused());
    }

    private static AWActorPresentationState CreateState(Actor _)
    {
        return new AWActorPresentationState();
    }

    private sealed class AWActorPresentationState
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
