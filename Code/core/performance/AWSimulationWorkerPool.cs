using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal sealed class AWSimulationWorkerPool
    {
        internal static AWSimulationWorkerPool Instance { get; } =
            new AWSimulationWorkerPool();

        private readonly ManualResetEventSlim _operationCompleted =
            new ManualResetEventSlim(true);
        private readonly object _operationLock = new object();
        private readonly Thread[] _workers;
        private readonly AutoResetEvent[] _workerSignals;

        private Action<int> _operationAction;
        private ExceptionDispatchInfo _operationException;
        private int _activeGeneration;
        private int _nextGeneration;
        private int _nextIndex;
        private int _endIndex;
        private int _remainingParticipants;
        private int _completionMarked;
        private int _stopRequested;
        private int _executedItems;
        private int _itemCount;
        private int _workerSlots;
        private int _assistantJoined;
        private long _operationStartedAt;
        private long _operationCompletedAt;
        private long _participantBusyTicks;
        private long _mainWaitTicks;
        private bool _operationActive;
        private bool _operationAsynchronous;

        private long _completedOperations;
        private long _completedAsynchronousOperations;
        private long _completedItems;
        private long _completedWallTicks;
        private long _completedParticipantBusyTicks;
        private long _completedMainWaitTicks;
        private long _completedParticipantSlots;
        private long _completedParticipantCapacityTicks;
        private long _completedAssistedOperations;

        private AWSimulationWorkerPool()
        {
            int workerCount = Math.Max(0,
                AWPerformanceSettings.ForegroundParallelism - 1);
            _workers = new Thread[workerCount];
            _workerSignals = new AutoResetEvent[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                _workerSignals[i] = new AutoResetEvent(false);
                Thread worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "AW3 Simulation Worker " + (i + 1),
                    Priority = ThreadPriority.Normal
                };
                _workers[i] = worker;
                worker.Start(i);
            }
        }

        internal WorkResult RunIndexed(int pStartIndex,
            int pExclusiveEndIndex, Action<int> pAction)
        {
            ValidateRange(pStartIndex, pExclusiveEndIndex, pAction);
            int count = pExclusiveEndIndex - pStartIndex;
            int backgroundWorkers = Math.Min(_workers.Length,
                Math.Max(0, count - 1));
            WorkTicket ticket = StartOperation(pStartIndex,
                pExclusiveEndIndex, pAction, backgroundWorkers,
                pAsynchronous: false);

            if (count > 0) ExecuteItems(ticket.Generation);
            SignalParticipantCompleted(ticket.Generation);
            Wait(ticket);
            return Complete(ticket);
        }

        internal WorkTicket BeginIndexed(int pStartIndex,
            int pExclusiveEndIndex, Action<int> pAction)
        {
            ValidateRange(pStartIndex, pExclusiveEndIndex, pAction);
            int count = pExclusiveEndIndex - pStartIndex;
            int backgroundWorkers = Math.Min(_workers.Length, count);
            WorkTicket ticket = StartOperation(pStartIndex,
                pExclusiveEndIndex, pAction, backgroundWorkers,
                pAsynchronous: backgroundWorkers > 0);

            if (count > 0 && backgroundWorkers == 0)
                ExecuteItems(ticket.Generation);
            if (backgroundWorkers == 0)
                SignalParticipantCompleted(ticket.Generation);
            return ticket;
        }

        internal bool IsCompleted(WorkTicket pTicket)
        {
            ValidateActiveTicket(pTicket);
            return _operationCompleted.IsSet;
        }

        internal bool TryAssistActiveOperation()
        {
            int generation = Volatile.Read(ref _activeGeneration);
            if (generation == 0 ||
                Volatile.Read(ref _completionMarked) != 0 ||
                Volatile.Read(ref _nextIndex) >=
                Volatile.Read(ref _endIndex) - 1)
                return false;

            while (true)
            {
                int participants =
                    Volatile.Read(ref _remainingParticipants);
                if (participants <= 0 ||
                    generation != Volatile.Read(ref _activeGeneration) ||
                    Volatile.Read(ref _completionMarked) != 0)
                    return false;
                if (Interlocked.CompareExchange(
                        ref _remainingParticipants, participants + 1,
                        participants) == participants)
                    break;
            }

            Interlocked.Exchange(ref _assistantJoined, 1);
            try
            {
                ExecuteItems(generation);
            }
            finally
            {
                SignalParticipantCompleted(generation);
            }
            return true;
        }

        internal void Wait(WorkTicket pTicket)
        {
            ValidateActiveTicket(pTicket);
            if (_operationCompleted.IsSet) return;
            long startedAt = Stopwatch.GetTimestamp();
            _operationCompleted.Wait();
            Interlocked.Add(ref _mainWaitTicks,
                Stopwatch.GetTimestamp() - startedAt);
        }

        internal bool TryWait(WorkTicket pTicket,
            double pMaximumMilliseconds)
        {
            ValidateActiveTicket(pTicket);
            if (_operationCompleted.IsSet) return true;
            if (pMaximumMilliseconds <= 0d) return false;

            long startedAt = Stopwatch.GetTimestamp();
            long maximumTicks = Math.Max(1L,
                (long)(pMaximumMilliseconds * Stopwatch.Frequency / 1000d));
            long deadline = startedAt + maximumTicks;
            int idleSpins = 0;
            while (!_operationCompleted.IsSet)
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    Interlocked.Add(ref _mainWaitTicks,
                        Stopwatch.GetTimestamp() - startedAt);
                    return false;
                }

                if (TryAssistActiveOperationUntil(deadline))
                    idleSpins = 0;
                else if (idleSpins++ < 64)
                    Thread.SpinWait(64);
                else
                {
                    Thread.Yield();
                    idleSpins = 0;
                }
            }

            Interlocked.Add(ref _mainWaitTicks,
                Stopwatch.GetTimestamp() - startedAt);
            return true;
        }

        internal WorkResult Complete(WorkTicket pTicket)
        {
            ValidateActiveTicket(pTicket);
            if (!_operationCompleted.IsSet)
                throw new InvalidOperationException(
                    "Simulation worker work has not completed.");

            WorkResult result;
            ExceptionDispatchInfo exception;
            lock (_operationLock)
            {
                ValidateActiveTicketLocked(pTicket);
                long completedAt = Volatile.Read(ref _operationCompletedAt);
                result = new WorkResult(_itemCount,
                    Volatile.Read(ref _executedItems),
                    _operationStartedAt,
                    completedAt,
                    Math.Max(0L,
                        Interlocked.Read(ref _participantBusyTicks)),
                    Math.Max(0L,
                        Interlocked.Read(ref _mainWaitTicks)), _workerSlots,
                    _operationAsynchronous,
                    Volatile.Read(ref _assistantJoined) != 0);
                exception = _operationException;
                if (exception == null &&
                    result.ExecutedItems != result.ScheduledItems)
                {
                    // 无捕获异常却仍有未执行项：通常意味着某个 worker 线程在原版非线程安全代码里
                    // 静默撕裂（如并发写普通 Dictionary）。附带索引游标与停止标记，便于定位停在何处。
                    int stoppedAtIndex = Volatile.Read(ref _nextIndex);
                    int endIndex = Volatile.Read(ref _endIndex);
                    int stopRequested = Volatile.Read(ref _stopRequested);
                    exception = ExceptionDispatchInfo.Capture(
                        new InvalidOperationException(
                            "Simulation worker did not execute all scheduled work: " +
                            result.ExecutedItems + "/" +
                            result.ScheduledItems +
                            " (nextIndex=" + stoppedAtIndex +
                            ", endIndex=" + endIndex +
                            ", stopRequested=" + stopRequested +
                            ", workers=" + result.WorkerSlots + ")"));
                }
                _operationAction = null;
                _operationException = null;
                _operationActive = false;
                _operationAsynchronous = false;
                _activeGeneration = 0;
                _nextIndex = 0;
                _endIndex = 0;
                _remainingParticipants = 0;
                _itemCount = 0;
                _workerSlots = 0;
                _assistantJoined = 0;
            }

            RecordCompletedOperation(result);
            exception?.Throw();
            return result;
        }

        internal void WaitAndDiscard(WorkTicket pTicket)
        {
            if (!pTicket.IsValid) return;
            while (!TryWait(pTicket, 1000d))
                AncientWarfare3.ModClass.LogInfo(
                    "AW3 simulation worker teardown is still waiting for ticket " +
                    pTicket.Generation + ".");
            try
            {
                Complete(pTicket);
            }
            catch (Exception error)
            {
                AncientWarfare3.ModClass.LogInfo(
                    "AW3 simulation worker teardown discarded an operation error: " +
                    error);
            }
        }

        internal string GetDiagnostics()
        {
            long operations = Interlocked.Read(ref _completedOperations);
            long wallTicks = Interlocked.Read(ref _completedWallTicks);
            long busyTicks =
                Interlocked.Read(ref _completedParticipantBusyTicks);
            long waitTicks = Interlocked.Read(ref _completedMainWaitTicks);
            long slots = Interlocked.Read(ref _completedParticipantSlots);
            long capacityTicks =
                Interlocked.Read(ref _completedParticipantCapacityTicks);
            double wallSeconds = wallTicks / (double)Stopwatch.Frequency;
            double busySeconds = busyTicks / (double)Stopwatch.Frequency;
            double waitSeconds = waitTicks / (double)Stopwatch.Frequency;
            double utilization = capacityTicks <= 0L
                ? 0d
                : busyTicks * 100d / capacityTicks;
            bool active;
            lock (_operationLock) active = _operationActive;
            return string.Format(CultureInfo.InvariantCulture,
                "ops={0}(async={1},assist={9}) items={2} wall={3:0.0}ms busy={4:0.0}ms wait={5:0.0}ms slots={6:0.00} util={7:0.0}% active={8}",
                operations,
                Interlocked.Read(ref _completedAsynchronousOperations),
                Interlocked.Read(ref _completedItems), wallSeconds * 1000d,
                busySeconds * 1000d, waitSeconds * 1000d,
                operations <= 0L ? 0d : slots / (double)operations,
                utilization, active,
                Interlocked.Read(ref _completedAssistedOperations));
        }

        internal bool TryAssistActiveOperationUntil(long pDeadline)
        {
            int generation = Volatile.Read(ref _activeGeneration);
            if (generation == 0 ||
                Volatile.Read(ref _completionMarked) != 0 ||
                Volatile.Read(ref _nextIndex) >=
                Volatile.Read(ref _endIndex) - 1)
                return false;

            while (true)
            {
                int participants =
                    Volatile.Read(ref _remainingParticipants);
                if (participants <= 0 ||
                    generation != Volatile.Read(ref _activeGeneration) ||
                    Volatile.Read(ref _completionMarked) != 0)
                    return false;
                if (Interlocked.CompareExchange(
                        ref _remainingParticipants, participants + 1,
                        participants) == participants)
                    break;
            }

            Interlocked.Exchange(ref _assistantJoined, 1);
            long busyStartedAt = Stopwatch.GetTimestamp();
            try
            {
                while (Volatile.Read(ref _stopRequested) == 0 &&
                       Stopwatch.GetTimestamp() < pDeadline)
                {
                    int index = Interlocked.Increment(ref _nextIndex);
                    if (index >= Volatile.Read(ref _endIndex)) break;
                    try
                    {
                        _operationAction(index);
                        Interlocked.Increment(ref _executedItems);
                    }
                    catch (Exception error)
                    {
                        Interlocked.CompareExchange(ref _operationException,
                            ExceptionDispatchInfo.Capture(error), null);
                        Volatile.Write(ref _stopRequested, 1);
                        break;
                    }
                }
            }
            finally
            {
                Interlocked.Add(ref _participantBusyTicks,
                    Stopwatch.GetTimestamp() - busyStartedAt);
                SignalParticipantCompleted(generation);
            }
            return true;
        }

        private WorkTicket StartOperation(int pStartIndex,
            int pExclusiveEndIndex, Action<int> pAction,
            int pBackgroundWorkers, bool pAsynchronous)
        {
            WorkTicket ticket;
            lock (_operationLock)
            {
                if (_operationActive)
                    throw new InvalidOperationException(
                        "Simulation worker pool still owns an operation.");
                _operationActive = true;
                _operationAsynchronous = pAsynchronous;
                _activeGeneration = unchecked(++_nextGeneration);
                if (_activeGeneration == 0)
                    _activeGeneration = unchecked(++_nextGeneration);
                _operationAction = pAction;
                _operationException = null;
                _nextIndex = pStartIndex - 1;
                _endIndex = pExclusiveEndIndex;
                _itemCount = pExclusiveEndIndex - pStartIndex;
                _workerSlots = pBackgroundWorkers;
                _remainingParticipants = pBackgroundWorkers +
                    (pAsynchronous ? 0 : 1);
                _completionMarked = 0;
                _stopRequested = 0;
                _executedItems = 0;
                _participantBusyTicks = 0L;
                _mainWaitTicks = 0L;
                _assistantJoined = 0;
                _operationStartedAt = Stopwatch.GetTimestamp();
                _operationCompletedAt = 0L;
                _operationCompleted.Reset();
                ticket = new WorkTicket(_activeGeneration);
            }

            for (int i = 0; i < pBackgroundWorkers; i++)
                _workerSignals[i].Set();
            return ticket;
        }

        private void WorkerLoop(object pState)
        {
            int workerIndex = (int)pState;
            AutoResetEvent signal = _workerSignals[workerIndex];
            while (true)
            {
                signal.WaitOne();
                int generation = Volatile.Read(ref _activeGeneration);
                if (generation == 0) continue;
                ExecuteItems(generation);
                SignalParticipantCompleted(generation);
            }
        }

        private void ExecuteItems(int pGeneration)
        {
            if (pGeneration != Volatile.Read(ref _activeGeneration)) return;

            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                while (Volatile.Read(ref _stopRequested) == 0)
                {
                    int index = Interlocked.Increment(ref _nextIndex);
                    if (index >= Volatile.Read(ref _endIndex)) break;
                    try
                    {
                        _operationAction(index);
                        Interlocked.Increment(ref _executedItems);
                    }
                    catch (Exception error)
                    {
                        Interlocked.CompareExchange(ref _operationException,
                            ExceptionDispatchInfo.Capture(error), null);
                        Volatile.Write(ref _stopRequested, 1);
                        break;
                    }
                }
            }
            finally
            {
                Interlocked.Add(ref _participantBusyTicks,
                    Stopwatch.GetTimestamp() - startedAt);
            }
        }

        private void MarkOperationCompleted(int pGeneration)
        {
            if (pGeneration != Volatile.Read(ref _activeGeneration) ||
                Interlocked.CompareExchange(ref _completionMarked, 1, 0) != 0)
                return;
            Volatile.Write(ref _operationCompletedAt,
                Stopwatch.GetTimestamp());
            _operationCompleted.Set();
        }

        private void SignalParticipantCompleted(int pGeneration)
        {
            if (pGeneration == Volatile.Read(ref _activeGeneration) &&
                Interlocked.Decrement(ref _remainingParticipants) == 0)
                MarkOperationCompleted(pGeneration);
        }

        private void RecordCompletedOperation(WorkResult pResult)
        {
            Interlocked.Increment(ref _completedOperations);
            if (pResult.RanAsynchronously)
                Interlocked.Increment(
                    ref _completedAsynchronousOperations);
            if (pResult.Assisted)
                Interlocked.Increment(ref _completedAssistedOperations);
            Interlocked.Add(ref _completedItems, pResult.ExecutedItems);
            Interlocked.Add(ref _completedWallTicks, pResult.WallTicks);
            Interlocked.Add(ref _completedParticipantBusyTicks,
                pResult.ParticipantBusyTicks);
            Interlocked.Add(ref _completedMainWaitTicks,
                pResult.MainWaitTicks);
            Interlocked.Add(ref _completedParticipantSlots,
                pResult.ParticipantSlots);
            Interlocked.Add(ref _completedParticipantCapacityTicks,
                pResult.WallTicks * pResult.ParticipantSlots);
        }

        private void ValidateActiveTicket(WorkTicket pTicket)
        {
            lock (_operationLock) ValidateActiveTicketLocked(pTicket);
        }

        private void ValidateActiveTicketLocked(WorkTicket pTicket)
        {
            if (!pTicket.IsValid || !_operationActive ||
                pTicket.Generation != _activeGeneration)
                throw new InvalidOperationException(
                    "Simulation worker ticket is no longer active.");
        }

        private static void ValidateRange(int pStartIndex,
            int pExclusiveEndIndex, Action<int> pAction)
        {
            if (pStartIndex < 0 || pExclusiveEndIndex < pStartIndex)
                throw new ArgumentOutOfRangeException(nameof(pStartIndex));
            if (pAction == null) throw new ArgumentNullException(nameof(pAction));
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
            internal WorkResult(int pScheduledItems, int pExecutedItems,
                long pStartedAt, long pCompletedAt,
                long pParticipantBusyTicks, long pMainWaitTicks,
                int pWorkerSlots, bool pRanAsynchronously,
                bool pAssisted)
            {
                ScheduledItems = pScheduledItems;
                ExecutedItems = pExecutedItems;
                StartedAt = pStartedAt;
                CompletedAt = pCompletedAt;
                WallTicks = Math.Max(0L, pCompletedAt - pStartedAt);
                WallSeconds = WallTicks / (double)Stopwatch.Frequency;
                ParticipantBusyTicks = pParticipantBusyTicks;
                ParticipantBusySeconds = ParticipantBusyTicks /
                    (double)Stopwatch.Frequency;
                MainWaitTicks = pMainWaitTicks;
                MainWaitSeconds = MainWaitTicks /
                    (double)Stopwatch.Frequency;
                WorkerSlots = pWorkerSlots;
                RanAsynchronously = pRanAsynchronously;
                Assisted = pAssisted;
                ParticipantSlots = pWorkerSlots +
                    (pRanAsynchronously ? 0 : 1) + (pAssisted ? 1 : 0);
            }

            internal int ScheduledItems { get; }
            internal int ExecutedItems { get; }
            internal long StartedAt { get; }
            internal long CompletedAt { get; }
            internal long WallTicks { get; }
            internal double WallSeconds { get; }
            internal long ParticipantBusyTicks { get; }
            internal double ParticipantBusySeconds { get; }
            internal long MainWaitTicks { get; }
            internal double MainWaitSeconds { get; }
            internal int WorkerSlots { get; }
            internal bool RanAsynchronously { get; }
            internal bool Assisted { get; }
            internal int ParticipantSlots { get; }
        }
    }
}
