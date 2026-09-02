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
        private readonly AWSimulationWorkerDispatchGate _dispatchGate;
        private readonly Thread[] _workers;
        private readonly AutoResetEvent[] _workerSignals;

        private Action<int> _operationAction;
        private ExceptionDispatchInfo _operationException;
        private int _activeGeneration;
        private int _nextGeneration;
        /// <summary>
        ///     高 32 位 = 代次,低 32 位 = 已发放到的索引,同样打包进一个 64 位字。
        ///
        ///     裸 int 游标的问题:认领是 <c>Interlocked.Increment</c>,**先消费再
        ///     判断**。一个上一代的参与者只要在「循环条件通过」和「自增」之间被
        ///     调度出去,等它回来时下一个操作可能已经开始 —— 它那一次自增就吃掉了
        ///     **新操作**的一个索引,然后因为代次/endIndex 对不上直接 break,不执行。
        ///     那一项从此没人做,而账面上又确实被发放过。这就是玩家日志里
        ///     「7/36,nextIndex 已越过 endIndex」的来源。
        ///
        ///     改成带代次的 CAS 之后,跨代认领会直接失败、一个索引都不会被消费。
        /// </summary>
        private long _cursorState;
        private int _endIndex;
        /// <summary>
        ///     高 32 位 = 代次,低 32 位 = 剩余参与者数,打包进一个 64 位字。
        ///
        ///     原本是一个裸的 int `_remainingParticipants`,加入方(两个 assist
        ///     入口)先读 <see cref="_activeGeneration"/> 判断代次、再单独 CAS 这个
        ///     计数 —— **两步不原子**。于是一次 assist 可以「按 A 代做的判断」把
        ///     +1 落到 B 代的计数上,而事后的 SignalParticipantCompleted 带代次
        ///     守卫、发现不是 A 代就跳过不减,B 代的账从此永远凑不齐 0。
        ///
        ///     打包之后代次进了被比较的那个字,跨代的加入/退出会被 CAS 直接否决,
        ///     账不可能串代。
        ///
        ///     还顺带立起一条不变量:**只要还有已登记的参与者,计数就不为 0,
        ///     完成标记就不会置位,Complete 就跑不了,代次也就不会前进** ——
        ///     所以参与者在自己的执行循环里读到的 _nextIndex/_endIndex/
        ///     _operationAction 必然还属于自己那一代,不会被下一个操作换掉。
        /// </summary>
        private long _participantState;
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
            _dispatchGate = new AWSimulationWorkerDispatchGate(workerCount);
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
                CursorIndex(Volatile.Read(ref _cursorState)) >=
                Volatile.Read(ref _endIndex) - 1)
                return false;

            if (!TryJoinParticipant(generation)) return false;

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
                    int stoppedAtIndex = CursorIndex(
                        Volatile.Read(ref _cursorState));
                    int endIndex = Volatile.Read(ref _endIndex);
                    int stopRequested = Volatile.Read(ref _stopRequested);
                    int generation = Volatile.Read(ref _activeGeneration);
                    int remainingParticipants = ParticipantCount(
                        Volatile.Read(ref _participantState));
                    int completionMarked =
                        Volatile.Read(ref _completionMarked);
                    exception = ExceptionDispatchInfo.Capture(
                        new InvalidOperationException(
                            "Simulation worker did not execute all scheduled work: " +
                            result.ExecutedItems + "/" +
                            result.ScheduledItems +
                            " (nextIndex=" + stoppedAtIndex +
                            ", endIndex=" + endIndex +
                            ", stopRequested=" + stopRequested +
                            ", generation=" + generation +
                            ", remainingParticipants=" +
                            remainingParticipants +
                            ", completionMarked=" + completionMarked +
                            ", workers=" + result.WorkerSlots + ")"));
                }
                // 拆除按发布的逆序:先撤代次(让所有外部线程再也进不来),
                // 才允许清载荷。反过来写的话,任何还拿着当前代次在跑的线程
                // 都可能读到已经被清成 null 的 _operationAction。
                Volatile.Write(ref _activeGeneration, 0);
                Volatile.Write(ref _participantState, 0L);
                Volatile.Write(ref _cursorState, 0L);
                _endIndex = 0;
                _operationAction = null;
                _operationException = null;
                _operationActive = false;
                _operationAsynchronous = false;
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
                CursorIndex(Volatile.Read(ref _cursorState)) >=
                Volatile.Read(ref _endIndex) - 1)
                return false;

            if (!TryJoinParticipant(generation)) return false;

            Interlocked.Exchange(ref _assistantJoined, 1);
            long busyStartedAt = Stopwatch.GetTimestamp();
            try
            {
                while (Volatile.Read(ref _stopRequested) == 0 &&
                       generation == Volatile.Read(ref _activeGeneration) &&
                       Stopwatch.GetTimestamp() < pDeadline)
                {
                    if (!TryClaimIndex(generation, out int index)) break;
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

                int generation = unchecked(++_nextGeneration);
                if (generation == 0)
                    generation = unchecked(++_nextGeneration);

                // 全部初始化必须发生在**发布代次之前**。
                //
                // 两个 assist 入口不进 _operationLock,它们只凭
                // _activeGeneration + _completionMarked 就加入并开始执行。所以
                // 一旦代次先于计数清零被发布,就存在这样一段窗口:
                //
                //   主线程: _activeGeneration = G2; _completionMarked = 0;
                //           ——被调度出去——
                //   协助线程: 看到 G2、看到未完成 → 加入 → 认领 → 动作真的跑完
                //           → Interlocked.Increment(ref _executedItems) × N
                //   主线程: _executedItems = 0;      ← N 次自增被整片抹掉
                //
                // 结果是活全干了、账被清空,Complete 拿到 0/N 抛「未执行完」。
                // 压力台上「动作实际被调用 6 次 / 应为 6 次」却仍然报错,以及
                // 部分抹除的 7/8,都是这个窗口的形状。
                _operationActive = true;
                _operationAsynchronous = pAsynchronous;
                _operationAction = pAction;
                _operationException = null;
                _completionMarked = 0;
                _stopRequested = 0;
                _executedItems = 0;
                _participantBusyTicks = 0L;
                _mainWaitTicks = 0L;
                _assistantJoined = 0;
                _endIndex = pExclusiveEndIndex;
                _itemCount = pExclusiveEndIndex - pStartIndex;
                _workerSlots = pBackgroundWorkers;
                _operationStartedAt = Stopwatch.GetTimestamp();
                _operationCompletedAt = 0L;
                _operationCompleted.Reset();

                // 这两个字自带代次,外部线程读到旧代次会被 CAS 直接否决,
                // 所以先于 _activeGeneration 写是安全的。
                Volatile.Write(ref _cursorState,
                    PackCursor(generation, pStartIndex - 1));
                Volatile.Write(ref _participantState,
                    PackParticipants(generation,
                        pBackgroundWorkers + (pAsynchronous ? 0 : 1)));
                // 唯一的发布点。release 写保证上面所有初始化对任何做
                // Volatile.Read(ref _activeGeneration) 的线程都已经可见。
                Volatile.Write(ref _activeGeneration, generation);
                ticket = new WorkTicket(generation);
            }

            for (int i = 0; i < pBackgroundWorkers; i++)
            {
                _dispatchGate.Assign(i, ticket.Generation);
                _workerSignals[i].Set();
            }
            return ticket;
        }

        private void WorkerLoop(object pState)
        {
            int workerIndex = (int)pState;
            AutoResetEvent signal = _workerSignals[workerIndex];
            while (true)
            {
                signal.WaitOne();
                int generation = _dispatchGate.Consume(workerIndex);
                if (generation == 0) continue;
                try
                {
                    if (generation == Volatile.Read(ref _activeGeneration))
                        ExecuteItems(generation);
                }
                finally
                {
                    // 无论有没有真的执行,都必须把自己从参与者里摘掉:
                    // StartOperation 在派发时就已经把这个 worker 计进去了,不摘
                    // 就永远凑不齐 0,Wait 会挂死。代次对不上时打包 CAS 会自动
                    // 否决,不会误伤别的操作,所以这里无条件调用是安全的。
                    SignalParticipantCompleted(generation);
                }
            }
        }

        private void ExecuteItems(int pGeneration)
        {
            if (pGeneration != Volatile.Read(ref _activeGeneration)) return;

            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                // 每轮都复查代次:入口查一次不够。带期限的 assist 会在期限到点
                // 时提前离场,离场后 Complete 可能已经把 _nextIndex/_endIndex 复位
                // 给下一个操作 —— 这时若还有落在别代的参与者继续 Interlocked
                // 自增游标,它消费掉的就是**别人**的索引,而且因为 _endIndex 已归 0
                // 会直接 break 不执行。账面上那一项就此永久丢失,正是玩家日志里
                // 「7/36 而 nextIndex 已越过 endIndex」的形状。
                while (Volatile.Read(ref _stopRequested) == 0 &&
                       pGeneration == Volatile.Read(ref _activeGeneration))
                {
                    if (!TryClaimIndex(pGeneration, out int index)) break;
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
            while (true)
            {
                long state = Volatile.Read(ref _participantState);
                if (ParticipantGeneration(state) != pGeneration) return;
                int count = ParticipantCount(state);
                if (count <= 0) return;
                long next = PackParticipants(pGeneration, count - 1);
                if (Interlocked.CompareExchange(ref _participantState, next,
                        state) != state)
                    continue;
                if (count == 1) MarkOperationCompleted(pGeneration);
                return;
            }
        }

        /// <summary>
        ///     以参与者身份加入正在进行的操作。代次与计数在同一个 CAS 里比较,
        ///     所以「判断的那一代」和「加到的那一代」必然是同一代。
        /// </summary>
        private bool TryJoinParticipant(int pGeneration)
        {
            while (true)
            {
                if (Volatile.Read(ref _completionMarked) != 0) return false;
                long state = Volatile.Read(ref _participantState);
                if (ParticipantGeneration(state) != pGeneration) return false;
                int count = ParticipantCount(state);
                // 计数已经归零表示操作正在收尾,这时候再挤进去只会拖住它。
                if (count <= 0) return false;
                long next = PackParticipants(pGeneration, count + 1);
                if (Interlocked.CompareExchange(ref _participantState, next,
                        state) == state)
                    return true;
            }
        }

        private static long PackParticipants(int pGeneration, int pCount)
        {
            return ((long)pGeneration << 32) | (uint)pCount;
        }

        /// <summary>
        ///     认领下一个索引。代次进 CAS,所以跨代认领会失败且**不消费**任何索引。
        /// </summary>
        private bool TryClaimIndex(int pGeneration, out int pIndex)
        {
            pIndex = 0;
            while (true)
            {
                long state = Volatile.Read(ref _cursorState);
                if (CursorGeneration(state) != pGeneration) return false;
                int next = CursorIndex(state) + 1;
                if (next >= Volatile.Read(ref _endIndex)) return false;
                if (Interlocked.CompareExchange(ref _cursorState,
                        PackCursor(pGeneration, next), state) != state)
                    continue;
                pIndex = next;
                return true;
            }
        }

        private static long PackCursor(int pGeneration, int pIndex)
        {
            return ((long)pGeneration << 32) | (uint)pIndex;
        }

        private static int CursorGeneration(long pState)
        {
            return (int)(pState >> 32);
        }

        private static int CursorIndex(long pState)
        {
            return (int)(pState & 0xFFFFFFFFL);
        }

        private static int ParticipantGeneration(long pState)
        {
            return (int)(pState >> 32);
        }

        private static int ParticipantCount(long pState)
        {
            return (int)(pState & 0xFFFFFFFFL);
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
