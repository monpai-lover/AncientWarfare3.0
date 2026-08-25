// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public sealed class AWPathStream
    {
        private readonly ConcurrentQueue<AWPathStep> _steps = new ConcurrentQueue<AWPathStep>();
        private int _status;
        private int _pendingCount;
        private AWPathFailureReason _failureReason;
        private Exception _error;

        public int Count => Volatile.Read(ref _pendingCount);
        public bool HasPendingSteps => !_steps.IsEmpty;
        public bool IsFinalized => Volatile.Read(ref _status) != 0;
        public bool IsFinished => IsFinalized && !HasPendingSteps;

        public AWPathRequestState State
        {
            get
            {
                switch (Volatile.Read(ref _status))
                {
                    case 0:
                        return HasPendingSteps
                            ? AWPathRequestState.Streaming
                            : AWPathRequestState.Pending;
                    case 1: return AWPathRequestState.Succeeded;
                    case 2: return AWPathRequestState.Cancelled;
                    case 3: return AWPathRequestState.Failed;
                    default: return AWPathRequestState.Failed;
                }
            }
        }

        public AWPathFailureReason FailureReason => _failureReason;
        public Exception Error => _error;

        public bool AddStep(AWPathStep pStep)
        {
            if (pStep.TileId < 0 || IsFinalized) return false;
            Interlocked.Increment(ref _pendingCount);
            _steps.Enqueue(pStep);
            return true;
        }

        public bool TryPeek(out AWPathStep pStep)
        {
            return _steps.TryPeek(out pStep);
        }

        // Cultiway's actor-facing inspection API returns a snapshot without
        // consuming ownership of any step.  Keep this separate from TryTake
        // so diagnostics and UI callers cannot advance an active route.
        public List<AWPathStep> TryViewAll()
        {
            return new List<AWPathStep>(_steps.ToArray());
        }

        public bool TryTake(out AWPathStep pStep)
        {
            if (!_steps.TryDequeue(out pStep)) return false;
            Interlocked.Decrement(ref _pendingCount);
            return true;
        }

        public void Complete()
        {
            Interlocked.CompareExchange(ref _status, 1, 0);
        }

        public void Cancel(AWPathFailureReason pReason = AWPathFailureReason.CancelledByNewRequest)
        {
            if (IsFinalized) return;
            _failureReason = pReason;
            Interlocked.CompareExchange(ref _status, 2, 0);
        }

        public void Fail(AWPathFailureReason pReason, Exception pError)
        {
            if (IsFinalized) return;
            _failureReason = pReason == AWPathFailureReason.None
                ? AWPathFailureReason.GeneratorException
                : pReason;
            _error = pError;
            Interlocked.CompareExchange(ref _status, 3, 0);
        }

        public void Fail(Exception pError)
        {
            Fail(AWPathFailureReason.GeneratorException, pError);
        }

        public void EnsureCompleted()
        {
            if (!IsFinalized) Complete();
        }

        public AWPathPollResult Poll()
        {
            if (_steps.TryPeek(out AWPathStep step))
                return new AWPathPollResult(AWPathPollKind.StepReady, step);

            switch (State)
            {
                case AWPathRequestState.Pending:
                case AWPathRequestState.Streaming:
                    return new AWPathPollResult(AWPathPollKind.Waiting);
                case AWPathRequestState.Succeeded:
                    return new AWPathPollResult(AWPathPollKind.Completed);
                case AWPathRequestState.Cancelled:
                    return new AWPathPollResult(AWPathPollKind.Cancelled,
                        pFailureReason: _failureReason, pError: _error);
                case AWPathRequestState.Failed:
                    return new AWPathPollResult(AWPathPollKind.Failed,
                        pFailureReason: _failureReason, pError: _error);
                default:
                    return new AWPathPollResult(AWPathPollKind.NoRequest);
            }
        }
    }
}
