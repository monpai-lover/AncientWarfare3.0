// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Concurrent;

namespace AncientWarfare3.core.pathfinding
{
    public sealed class AWPathStream
    {
        private readonly ConcurrentQueue<AWPathStep> _steps = new ConcurrentQueue<AWPathStep>();
        private readonly object _stateGate = new object();
        private AWPathRequestState _state = AWPathRequestState.Pending;
        private AWPathFailureReason _failureReason;
        private Exception _error;

        public AWPathRequestState State
        {
            get
            {
                lock (_stateGate) return _state;
            }
        }

        public AWPathFailureReason FailureReason
        {
            get
            {
                lock (_stateGate) return _failureReason;
            }
        }

        public Exception Error
        {
            get
            {
                lock (_stateGate) return _error;
            }
        }

        public int Count => _steps.Count;

        public bool AddStep(AWPathStep pStep)
        {
            lock (_stateGate)
            {
                if (IsTerminal(_state)) return false;
                _steps.Enqueue(pStep);
                _state = AWPathRequestState.Streaming;
                return true;
            }
        }

        public bool TryPeek(out AWPathStep pStep)
        {
            return _steps.TryPeek(out pStep);
        }

        public bool TryTake(out AWPathStep pStep)
        {
            return _steps.TryDequeue(out pStep);
        }

        public void Complete()
        {
            TryFinish(AWPathRequestState.Completed, AWPathFailureReason.None, null);
        }

        public void Cancel(AWPathFailureReason pReason = AWPathFailureReason.CancelledByNewRequest)
        {
            TryFinish(AWPathRequestState.Cancelled, pReason, null);
        }

        public void Fail(AWPathFailureReason pReason, Exception pError)
        {
            TryFinish(AWPathRequestState.Failed,
                pReason == AWPathFailureReason.None ? AWPathFailureReason.GeneratorException : pReason,
                pError);
        }

        public void Fail(Exception pError)
        {
            Fail(AWPathFailureReason.GeneratorException, pError);
        }

        public AWPathPollResult Poll()
        {
            if (_steps.TryPeek(out AWPathStep step))
                return new AWPathPollResult(AWPathPollKind.StepReady, step);

            lock (_stateGate)
            {
                switch (_state)
                {
                    case AWPathRequestState.Pending:
                    case AWPathRequestState.Streaming:
                        return new AWPathPollResult(AWPathPollKind.Waiting);
                    case AWPathRequestState.Completed:
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

        private bool TryFinish(AWPathRequestState pState, AWPathFailureReason pReason,
            Exception pError)
        {
            lock (_stateGate)
            {
                if (IsTerminal(_state)) return false;
                _failureReason = pReason;
                _error = pError;
                _state = pState;
                return true;
            }
        }

        private static bool IsTerminal(AWPathRequestState pState)
        {
            return pState == AWPathRequestState.Completed ||
                   pState == AWPathRequestState.Failed ||
                   pState == AWPathRequestState.Cancelled;
        }
    }
}
