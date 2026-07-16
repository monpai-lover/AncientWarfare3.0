// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    public readonly struct AWPathRetryDecision
    {
        public AWPathRetryDecision(bool pShouldRetry, int pAttempt, float pDelaySeconds,
            double pDueTime)
        {
            ShouldRetry = pShouldRetry;
            Attempt = pAttempt;
            DelaySeconds = pDelaySeconds;
            DueTime = pDueTime;
        }

        public bool ShouldRetry { get; }
        public int Attempt { get; }
        public float DelaySeconds { get; }
        public double DueTime { get; }
    }

    public sealed class AWPathRecoveryManager
    {
        private readonly Dictionary<long, RecoveryState> _states =
            new Dictionary<long, RecoveryState>();

        public static int RetryLimit(AWPathFailureReason pReason)
        {
            return AWPathLifecycleRules.RetryLimit(pReason);
        }

        public AWPathRetryDecision OnFailure(long pActorId, AWPathFailureReason pReason, double now)
        {
            int limit = RetryLimit(pReason);
            if (pActorId < 0 || limit <= 0)
            {
                _states.Remove(pActorId);
                return default;
            }
            _states.TryGetValue(pActorId, out RecoveryState state);
            if (state.Reason != pReason) state = new RecoveryState(pReason, 0, 0d);
            int attempt = state.Attempt + 1;
            if (attempt > limit)
            {
                _states.Remove(pActorId);
                return new AWPathRetryDecision(false, attempt, 0f, now);
            }
            float delay = AWPathLifecycleRules.RetryDelay(attempt);
            _states[pActorId] = new RecoveryState(pReason, attempt, now + delay);
            return new AWPathRetryDecision(true, attempt, delay, now + delay);
        }

        public bool IsDue(long pActorId, double now)
        {
            return _states.TryGetValue(pActorId, out RecoveryState state) && now >= state.DueTime;
        }

        public void OnProgress(long pActorId) => _states.Remove(pActorId);
        public void Clear(long pActorId) => _states.Remove(pActorId);
        public void Clear() => _states.Clear();

        private readonly struct RecoveryState
        {
            public RecoveryState(AWPathFailureReason pReason, int pAttempt, double pDueTime)
            {
                Reason = pReason;
                Attempt = pAttempt;
                DueTime = pDueTime;
            }

            public AWPathFailureReason Reason { get; }
            public int Attempt { get; }
            public double DueTime { get; }
        }
    }
}
