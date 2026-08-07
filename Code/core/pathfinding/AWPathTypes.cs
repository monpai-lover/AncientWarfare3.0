// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    public enum AWPathRequestState
    {
        Pending,
        Streaming,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum AWPathPollKind
    {
        NoRequest,
        Waiting,
        StepReady,
        Completed,
        Failed,
        Cancelled
    }

    public enum AWPathFailureReason
    {
        None,
        InvalidActor,
        InvalidStart,
        InvalidTarget,
        CancelledByNewRequest,
        WorldCleared,
        StepBlocked,
        UnsafeStep,
        PortalUnavailable,
        TransportFailed,
        Timeout,
        GeneratorException,
        Unreachable,
        SearchLimitExceeded
    }

    public enum AWMovementMethod
    {
        Walk,
        Swim,
        Sail,
        Transport
    }

    [Flags]
    public enum AWHazardFlags
    {
        None = 0,
        Block = 1 << 0,
        Lava = 1 << 1,
        Ocean = 1 << 2,
        Fire = 1 << 3,
        TerrainDamage = 1 << 4,
        StaminaDrain = 1 << 5,
        Drowning = 1 << 6,
        LowHealth = 1 << 7,
        Direct = 1 << 8,
        Transport = 1 << 9
    }

    public readonly struct AWTraversalEstimate
    {
        public AWTraversalEstimate(float pTimeSeconds, float pStaminaCost, float pHealthCost,
            float pRiskCost, AWHazardFlags pHazards)
        {
            TimeSeconds = pTimeSeconds;
            StaminaCost = pStaminaCost;
            HealthCost = pHealthCost;
            RiskCost = pRiskCost;
            Hazards = pHazards;
        }

        public float TimeSeconds { get; }
        public float StaminaCost { get; }
        public float HealthCost { get; }
        public float RiskCost { get; }
        public AWHazardFlags Hazards { get; }

        public static AWTraversalEstimate Direct =>
            new AWTraversalEstimate(0f, 0f, 0f, 0f, AWHazardFlags.Direct);
    }

    public readonly struct AWPathStep
    {
        public AWPathStep(int pTileId, AWMovementMethod pMethod,
            AWTraversalEstimate pEstimate = default, long pTransportRequestId = -1L)
        {
            TileId = pTileId;
            Method = pMethod;
            Estimate = pEstimate;
            TransportRequestId = pTransportRequestId;
        }

        public int TileId { get; }
        public AWMovementMethod Method { get; }
        public AWTraversalEstimate Estimate { get; }
        public AWHazardFlags Hazards => Estimate.Hazards;
        public long TransportRequestId { get; }
    }

    public readonly struct AWPathPollResult
    {
        public AWPathPollResult(AWPathPollKind pKind, AWPathStep pStep = default,
            AWPathFailureReason pFailureReason = AWPathFailureReason.None, Exception pError = null)
        {
            Kind = pKind;
            Step = pStep;
            FailureReason = pFailureReason;
            Error = pError;
        }

        public AWPathPollKind Kind { get; }
        public AWPathStep Step { get; }
        public AWPathFailureReason FailureReason { get; }
        public Exception Error { get; }
    }

    public readonly struct AWPathGenerationResult
    {
        public AWPathGenerationResult(bool pSucceeded, bool pReachedTarget,
            int pEndTileId, IReadOnlyList<AWPathStep> pSteps,
            AWPathFailureReason pFailureReason = AWPathFailureReason.None,
            Exception pError = null)
        {
            Succeeded = pSucceeded;
            ReachedTarget = pReachedTarget;
            EndTileId = pEndTileId;
            Steps = pSteps ?? Array.Empty<AWPathStep>();
            FailureReason = pFailureReason;
            Error = pError;
        }

        public bool Succeeded { get; }
        public bool ReachedTarget { get; }
        public int EndTileId { get; }
        public IReadOnlyList<AWPathStep> Steps { get; }
        public AWPathFailureReason FailureReason { get; }
        public Exception Error { get; }

        public static AWPathGenerationResult Success(int pEndTileId,
            bool pReachedTarget, IReadOnlyList<AWPathStep> pSteps)
        {
            return new AWPathGenerationResult(true, pReachedTarget,
                pEndTileId, pSteps);
        }

        public static AWPathGenerationResult Failure(
            AWPathFailureReason pReason, Exception pError = null)
        {
            return new AWPathGenerationResult(false, false, -1,
                Array.Empty<AWPathStep>(), pReason, pError);
        }
    }

    public enum AWPathProcessKind
    {
        Consumed,
        Deferred,
        Abort
    }

    public readonly struct AWPathProcessResult
    {
        private AWPathProcessResult(AWPathProcessKind pKind, AWPathFailureReason pReason)
        {
            Kind = pKind;
            FailureReason = pReason;
        }

        public AWPathProcessKind Kind { get; }
        public AWPathFailureReason FailureReason { get; }

        public static AWPathProcessResult Consumed() =>
            new AWPathProcessResult(AWPathProcessKind.Consumed, AWPathFailureReason.None);

        public static AWPathProcessResult Deferred() =>
            new AWPathProcessResult(AWPathProcessKind.Deferred, AWPathFailureReason.None);

        public static AWPathProcessResult Abort(AWPathFailureReason pReason) =>
            new AWPathProcessResult(AWPathProcessKind.Abort,
                pReason == AWPathFailureReason.None ? AWPathFailureReason.UnsafeStep : pReason);
    }

    public static class AWPathFailureRules
    {
        public static bool IsTerminal(AWPathFailureReason pReason)
        {
            switch (pReason)
            {
                case AWPathFailureReason.InvalidActor:
                case AWPathFailureReason.InvalidStart:
                case AWPathFailureReason.InvalidTarget:
                case AWPathFailureReason.CancelledByNewRequest:
                case AWPathFailureReason.WorldCleared:
                case AWPathFailureReason.Unreachable:
                case AWPathFailureReason.SearchLimitExceeded:
                    return true;
                default:
                    return false;
            }
        }
    }
}
