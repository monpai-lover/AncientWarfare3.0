using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWSimulationCoordinatorThread
    {
        internal static AWSimulationCoordinatorThread Instance { get; } =
            new AWSimulationCoordinatorThread();

        private readonly object _gate = new object();
        private readonly AutoResetEvent _workReady =
            new AutoResetEvent(false);
        private readonly ManualResetEventSlim _workCompleted =
            new ManualResetEventSlim(true);
        private readonly Thread _thread;

        private Action _operation;
        private ExceptionDispatchInfo _operationException;
        private string _operationName;
        private int _activeGeneration;
        private int _nextGeneration;
        private bool _operationActive;
        private long _operationStartedAt;
        private long _operationCompletedAt;
        private long _operationWaitTicks;
        private long _completedOperations;
        private long _completedWallTicks;
        private long _completedWaitTicks;

        private AWSimulationCoordinatorThread()
        {
            _thread = new Thread(CoordinatorLoop)
            {
                IsBackground = true,
                Name = "AW3 Simulation Coordinator",
                Priority = ThreadPriority.Normal
            };
            _thread.Start();
        }

        internal WorkTicket Begin(string pName, Action pAction)
        {
            if (string.IsNullOrEmpty(pName))
                throw new ArgumentException(
                    "Background simulation work needs a diagnostic name.",
                    nameof(pName));
            if (pAction == null)
                throw new ArgumentNullException(nameof(pAction));

            WorkTicket ticket;
            lock (_gate)
            {
                if (_operationActive)
                    throw new InvalidOperationException(
                        "Simulation coordinator still owns work: " +
                        _operationName);

                _activeGeneration = unchecked(++_nextGeneration);
                if (_activeGeneration == 0)
                    _activeGeneration = unchecked(++_nextGeneration);

                _operation = pAction;
                _operationException = null;
                _operationName = pName;
                _operationActive = true;
                _operationStartedAt = Stopwatch.GetTimestamp();
                _operationCompletedAt = 0L;
                _operationWaitTicks = 0L;
                _workCompleted.Reset();
                ticket = new WorkTicket(_activeGeneration);
            }

            _workReady.Set();
            return ticket;
        }

        internal bool IsCompleted(WorkTicket pTicket)
        {
            ValidateActiveTicket(pTicket);
            return _workCompleted.IsSet;
        }

        internal void Wait(WorkTicket pTicket)
        {
            ValidateActiveTicket(pTicket);
            if (_workCompleted.IsSet) return;

            long startedAt = Stopwatch.GetTimestamp();
            int idleSpins = 0;
            while (!_workCompleted.IsSet)
            {
                if (AWSimulationWorkerPool.Instance
                    .TryAssistActiveOperation())
                {
                    idleSpins = 0;
                    continue;
                }

                if (idleSpins++ < 64)
                    Thread.SpinWait(64);
                else
                {
                    Thread.Yield();
                    idleSpins = 0;
                }
            }

            Interlocked.Add(ref _operationWaitTicks,
                Stopwatch.GetTimestamp() - startedAt);
        }

        internal bool TryWait(WorkTicket pTicket,
            double pMaximumMilliseconds)
        {
            ValidateActiveTicket(pTicket);
            if (_workCompleted.IsSet) return true;
            if (pMaximumMilliseconds <= 0d) return false;

            long startedAt = Stopwatch.GetTimestamp();
            long maximumTicks = Math.Max(1L,
                (long)(pMaximumMilliseconds * Stopwatch.Frequency / 1000d));
            long deadline = startedAt + maximumTicks;
            int idleSpins = 0;
            while (!_workCompleted.IsSet)
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    Interlocked.Add(ref _operationWaitTicks,
                        Stopwatch.GetTimestamp() - startedAt);
                    return false;
                }

                if (AWSimulationWorkerPool.Instance
                    .TryAssistActiveOperationUntil(deadline))
                    idleSpins = 0;
                else if (idleSpins++ < 64)
                    Thread.SpinWait(64);
                else
                {
                    Thread.Yield();
                    idleSpins = 0;
                }
            }

            Interlocked.Add(ref _operationWaitTicks,
                Stopwatch.GetTimestamp() - startedAt);
            return true;
        }

        internal WorkResult Complete(WorkTicket pTicket)
        {
            ValidateActiveTicket(pTicket);
            if (!_workCompleted.IsSet)
                throw new InvalidOperationException(
                    "Background simulation work has not completed.");

            WorkResult result;
            ExceptionDispatchInfo exception;
            lock (_gate)
            {
                ValidateActiveTicketLocked(pTicket);
                result = new WorkResult(_operationName,
                    _operationStartedAt, _operationCompletedAt,
                    Math.Max(0L,
                        Interlocked.Read(ref _operationWaitTicks)));
                exception = _operationException;
                _operation = null;
                _operationException = null;
                _operationName = null;
                _operationActive = false;
                _activeGeneration = 0;
                _operationStartedAt = 0L;
                _operationCompletedAt = 0L;
                _operationWaitTicks = 0L;
            }

            Interlocked.Increment(ref _completedOperations);
            Interlocked.Add(ref _completedWallTicks, result.WallTicks);
            Interlocked.Add(ref _completedWaitTicks, result.WaitTicks);
            exception?.Throw();
            return result;
        }

        internal string GetDiagnostics()
        {
            bool active;
            string activeName;
            lock (_gate)
            {
                active = _operationActive;
                activeName = _operationName;
            }

            return string.Format(CultureInfo.InvariantCulture,
                "ops={0} wall={1:0.0}ms wait={2:0.0}ms active={3} name={4}",
                Interlocked.Read(ref _completedOperations),
                TicksToMilliseconds(
                    Interlocked.Read(ref _completedWallTicks)),
                TicksToMilliseconds(
                    Interlocked.Read(ref _completedWaitTicks)),
                active ? 1 : 0, activeName ?? "none");
        }

        private static double TicksToMilliseconds(long pTicks)
        {
            return Math.Max(0L, pTicks) * 1000d / Stopwatch.Frequency;
        }

        internal void WaitAndDiscard(WorkTicket pTicket)
        {
            if (!pTicket.IsValid) return;
            while (!TryWait(pTicket, 1000d))
                AncientWarfare3.ModClass.LogInfo(
                    "AW3 simulation coordinator teardown is still waiting for " +
                    "ticket " + pTicket.Generation + ".");
            try
            {
                Complete(pTicket);
            }
            catch (Exception error)
            {
                AncientWarfare3.ModClass.LogInfo(
                    "AW3 simulation coordinator teardown discarded an " +
                    "operation error: " + error);
            }
        }

        private void CoordinatorLoop()
        {
            while (true)
            {
                _workReady.WaitOne();
                Action action;
                int generation;
                lock (_gate)
                {
                    action = _operation;
                    generation = _activeGeneration;
                }

                try
                {
                    action();
                }
                catch (Exception error)
                {
                    lock (_gate)
                    {
                        if (_operationActive &&
                            generation == _activeGeneration &&
                            _operationException == null)
                            _operationException =
                                ExceptionDispatchInfo.Capture(error);
                    }
                }
                finally
                {
                    lock (_gate)
                    {
                        if (_operationActive &&
                            generation == _activeGeneration)
                        {
                            _operationCompletedAt = Stopwatch.GetTimestamp();
                            _workCompleted.Set();
                        }
                    }
                }
            }
        }

        private void ValidateActiveTicket(WorkTicket pTicket)
        {
            lock (_gate)
                ValidateActiveTicketLocked(pTicket);
        }

        private void ValidateActiveTicketLocked(WorkTicket pTicket)
        {
            if (!pTicket.IsValid || !_operationActive ||
                pTicket.Generation != _activeGeneration)
                throw new InvalidOperationException(
                    "Simulation coordinator ticket is no longer active.");
        }

        internal readonly struct WorkTicket
        {
            internal WorkTicket(int pGeneration)
            {
                Generation = pGeneration;
            }

            internal int Generation { get; }
            internal bool IsValid => Generation != 0;
        }

        internal readonly struct WorkResult
        {
            internal WorkResult(string pName, long pStartedAt,
                long pCompletedAt, long pWaitTicks)
            {
                Name = pName;
                StartedAt = pStartedAt;
                CompletedAt = pCompletedAt;
                WaitTicks = pWaitTicks;
            }

            internal string Name { get; }
            internal long StartedAt { get; }
            internal long CompletedAt { get; }
            internal long WaitTicks { get; }
            internal long WallTicks => Math.Max(0L,
                CompletedAt - StartedAt);
        }
    }
}
