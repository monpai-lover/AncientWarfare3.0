// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    [Flags]
    public enum AWPathTileFlags : ushort
    {
        None = 0,
        Exists = 1 << 0,
        HasType = 1 << 1,
        Block = 1 << 2,
        Lava = 1 << 3,
        Ocean = 1 << 4,
        Liquid = 1 << 5,
        DamageUnits = 1 << 6,
        Fire = 1 << 7
    }

    public static class AWPathTileFlagsExtensions
    {
        public const AWPathTileFlags RuntimeRelevant =
            AWPathTileFlags.HasType | AWPathTileFlags.Block |
            AWPathTileFlags.Lava | AWPathTileFlags.Ocean |
            AWPathTileFlags.Liquid | AWPathTileFlags.DamageUnits |
            AWPathTileFlags.Fire;

        public static AWPathTileFlags FromSnapshot(AWTileTraversalSnapshot pTile)
        {
            AWPathTileFlags flags = pTile.Exists ? AWPathTileFlags.Exists : AWPathTileFlags.None;
            if (pTile.HasType) flags |= AWPathTileFlags.HasType;
            if (pTile.Block) flags |= AWPathTileFlags.Block;
            if (pTile.Lava) flags |= AWPathTileFlags.Lava;
            if (pTile.Ocean) flags |= AWPathTileFlags.Ocean;
            if (pTile.Liquid) flags |= AWPathTileFlags.Liquid;
            if (pTile.DamageUnits) flags |= AWPathTileFlags.DamageUnits;
            if (pTile.Fire) flags |= AWPathTileFlags.Fire;
            return flags;
        }

    }

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
        StaleTraversal,
        WorldCleared,
        StepBlocked,
        UnsafeStep,
        PortalUnavailable,
        TransportFailed,
        Timeout,
        GeneratorException,
        Unreachable,
        SearchLimitExceeded,
        ActorDead
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
            AWTraversalEstimate pEstimate = default, long pTransportRequestId = -1L,
            AWPathTileFlags pPlannedTileFlags = AWPathTileFlags.None,
            long pEntryPortalId = -1L, long pExitPortalId = -1L,
            int pEntryLandTileId = -1, int pPickupSeaTileId = -1,
            int pDestinationSeaTileId = -1, int pLandingLandTileId = -1)
        {
            TileId = pTileId;
            Method = pMethod;
            Estimate = pEstimate;
            TransportRequestId = pTransportRequestId;
            PlannedTileFlags = pPlannedTileFlags;
            EntryPortalId = pEntryPortalId;
            ExitPortalId = pExitPortalId;
            EntryLandTileId = pEntryLandTileId;
            PickupSeaTileId = pPickupSeaTileId;
            DestinationSeaTileId = pDestinationSeaTileId;
            LandingLandTileId = pLandingLandTileId;
        }

        public int TileId { get; }
        public AWMovementMethod Method { get; }
        public AWTraversalEstimate Estimate { get; }
        public AWHazardFlags Hazards => Estimate.Hazards;
        public long TransportRequestId { get; }
        public AWPathTileFlags PlannedTileFlags { get; }
        public long EntryPortalId { get; }
        public long ExitPortalId { get; }
        public int EntryLandTileId { get; }
        public int PickupSeaTileId { get; }
        public int DestinationSeaTileId { get; }
        public int LandingLandTileId { get; }
    }

    // Immutable transport metadata captured while a request is created.
    // Workers and movement code can carry it without rescanning live docks.
    internal readonly struct AWTransportRouteSnapshot
    {
        internal AWTransportRouteSnapshot(long pEntryPortalId,
            long pExitPortalId, int pEntryLandTileId, int pPickupSeaTileId,
            int pDestinationSeaTileId, int pLandingLandTileId,
            float pEstimatedRouteTiles)
        {
            EntryPortalId = pEntryPortalId;
            ExitPortalId = pExitPortalId;
            EntryLandTileId = pEntryLandTileId;
            PickupSeaTileId = pPickupSeaTileId;
            DestinationSeaTileId = pDestinationSeaTileId;
            LandingLandTileId = pLandingLandTileId;
            EstimatedRouteTiles = pEstimatedRouteTiles;
        }

        internal long EntryPortalId { get; }
        internal long ExitPortalId { get; }
        internal int EntryLandTileId { get; }
        internal int PickupSeaTileId { get; }
        internal int DestinationSeaTileId { get; }
        internal int LandingLandTileId { get; }
        internal float EstimatedRouteTiles { get; }
        internal bool IsValid => EntryLandTileId >= 0 &&
                                  PickupSeaTileId >= 0 &&
                                  DestinationSeaTileId >= 0 &&
                                  LandingLandTileId >= 0 &&
                                  !float.IsNaN(EstimatedRouteTiles) &&
                                  !float.IsInfinity(EstimatedRouteTiles);

        internal static AWTransportRouteSnapshot FromRoute(
            AWDockRouteCandidate pRoute)
        {
            return new AWTransportRouteSnapshot(
                pRoute.Entry.Id, pRoute.Exit.Id,
                pRoute.Entry.LandTileId, pRoute.Entry.OceanTileId,
                pRoute.Exit.OceanTileId, pRoute.Exit.LandTileId,
                pRoute.EstimatedRouteTiles);
        }
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
            Exception pError = null, int pExpandedNodes = 0,
            AWPathGenerationKind pKind = AWPathGenerationKind.Search,
            float pEndStamina = float.NaN, float pEndHealth = float.NaN)
        {
            Succeeded = pSucceeded;
            ReachedTarget = pReachedTarget;
            EndTileId = pEndTileId;
            Steps = pSteps ?? Array.Empty<AWPathStep>();
            FailureReason = pFailureReason;
            Error = pError;
            ExpandedNodes = Math.Max(0, pExpandedNodes);
            Kind = pKind;
            EndStamina = pEndStamina;
            EndHealth = pEndHealth;
        }

        public bool Succeeded { get; }
        public bool ReachedTarget { get; }
        public int EndTileId { get; }
        public IReadOnlyList<AWPathStep> Steps { get; }
        public AWPathFailureReason FailureReason { get; }
        public Exception Error { get; }
        public int ExpandedNodes { get; }
        public AWPathGenerationKind Kind { get; }
        public float EndStamina { get; }
        public float EndHealth { get; }

        public static AWPathGenerationResult Success(int pEndTileId,
            bool pReachedTarget, IReadOnlyList<AWPathStep> pSteps)
        {
            return new AWPathGenerationResult(true, pReachedTarget,
                pEndTileId, pSteps);
        }

        public static AWPathGenerationResult Success(int pEndTileId,
            bool pReachedTarget, IReadOnlyList<AWPathStep> pSteps,
            int pExpandedNodes, AWPathGenerationKind pKind,
            float pEndStamina, float pEndHealth)
        {
            return new AWPathGenerationResult(true, pReachedTarget,
                pEndTileId, pSteps, AWPathFailureReason.None, null,
                pExpandedNodes, pKind, pEndStamina, pEndHealth);
        }

        public static AWPathGenerationResult Failure(
            AWPathFailureReason pReason, Exception pError = null)
        {
            return new AWPathGenerationResult(false, false, -1,
                Array.Empty<AWPathStep>(), pReason, pError);
        }

        public static AWPathGenerationResult Failure(
            AWPathFailureReason pReason, Exception pError,
            int pExpandedNodes)
        {
            return new AWPathGenerationResult(false, false, -1,
                Array.Empty<AWPathStep>(), pReason, pError,
                pExpandedNodes, AWPathGenerationKind.Search);
        }
    }

    public enum AWPathSubmissionKind
    {
        Rejected,
        Reused,
        Created,
        Replaced
    }

    public readonly struct AWPathSubmissionResult
    {
        public AWPathSubmissionResult(AWPathSubmissionKind pKind,
            AWPathFailureReason pFailureReason = AWPathFailureReason.None,
            long pSubmissionToken = 0L)
        {
            Kind = pKind;
            FailureReason = pFailureReason;
            SubmissionToken = pSubmissionToken;
        }

        public AWPathSubmissionKind Kind { get; }
        public AWPathFailureReason FailureReason { get; }
        public long SubmissionToken { get; }
        public bool Accepted => Kind != AWPathSubmissionKind.Rejected;
    }

    public enum AWPathGenerationKind
    {
        Search,
        StraightLine,
        RegionCorridor,
        Portal,
        Transport
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
                case AWPathFailureReason.StaleTraversal:
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
