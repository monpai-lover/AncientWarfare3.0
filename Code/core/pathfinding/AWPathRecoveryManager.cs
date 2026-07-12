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
            switch (pReason)
            {
                case AWPathFailureReason.StepBlocked:
                case AWPathFailureReason.UnsafeStep:
                    return 4;
                case AWPathFailureReason.PortalUnavailable:
                case AWPathFailureReason.TransportFailed:
                case AWPathFailureReason.Timeout:
                    return 2;
                case AWPathFailureReason.GeneratorException:
                    return 1;
                default:
                    return 0;
            }
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
            int attempt = state.Attempt + 1;
            if (attempt > limit)
            {
                _states.Remove(pActorId);
                return new AWPathRetryDecision(false, attempt, 0f, now);
            }
            float delay = Delay(attempt);
            _states[pActorId] = new RecoveryState(attempt, now + delay);
            return new AWPathRetryDecision(true, attempt, delay, now + delay);
        }

        public bool IsDue(long pActorId, double now)
        {
            return _states.TryGetValue(pActorId, out RecoveryState state) && now >= state.DueTime;
        }

        public void OnProgress(long pActorId) => _states.Remove(pActorId);
        public void Clear(long pActorId) => _states.Remove(pActorId);
        public void Clear() => _states.Clear();

        private static float Delay(int pAttempt)
        {
            switch (pAttempt)
            {
                case 1: return 0.1f;
                case 2: return 0.25f;
                case 3: return 0.5f;
                default: return 1f;
            }
        }

        private readonly struct RecoveryState
        {
            public RecoveryState(int pAttempt, double pDueTime)
            {
                Attempt = pAttempt;
                DueTime = pDueTime;
            }

            public int Attempt { get; }
            public double DueTime { get; }
        }
    }
}
