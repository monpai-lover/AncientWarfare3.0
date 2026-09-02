using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     WAL 检查点的后台驱动。
    ///
    ///     成因:<c>wal_autocheckpoint</c> 让检查点在**提交那一刻**发生,而学派
    ///     写缓冲的提交跑在权威帧的主线程上。实测开坛讲学时:
    ///       school_writes=commit:266.969/3/266.43   ← 单次提交 266.43ms
    ///       worst_frame_ms=297.524
    ///     而同一局里绝大多数提交只有 0.2~0.4ms —— 双峰分布,慢的那一档就是
    ///     顺带做了检查点的那几次。
    ///
    ///     检查点本身省不掉(不做 WAL 就无限涨),但它是纯 I/O,没有任何理由
    ///     占用主线程。WAL 模式允许多连接并发,且 PASSIVE 检查点不阻塞写者
    ///     (拿不到就少做一点,不会等),所以这里用**自己的连接 + 后台任务**跑,
    ///     主线程的提交从此只付事务本身的钱。
    ///
    ///     与存档的关系:<see cref="LineageArchivePragmaService.CheckpointForSave"/>
    ///     仍在主线程做(存档本来就是停顿点,且要求确定性)。两者共用
    ///     <see cref="Gate"/> 串行化,存档最多等一个在途检查点。
    ///
    ///     兜底:即使本服务完全不工作,WAL 也只会涨到「两次存档之间的写入量」,
    ///     存档时的 CheckpointForSave 会清掉。
    /// </summary>
    internal static class LineageArchiveCheckpointService
    {
        /// <summary>两次后台检查点之间的最小间隔。</summary>
        private const double MinimumIntervalSeconds = 2.0d;

        /// <summary>连续失败到这个次数就不再重试,留给存档时的主线程检查点。</summary>
        private const int MaximumConsecutiveFailures = 3;

        internal static readonly object Gate = new object();

        private static SQLiteConnection _connection;
        private static string _connectionPath = string.Empty;
        private static Task _running;
        private static long _lastCompletedAt;
        private static int _consecutiveFailures;
        private static long _checkpoints;
        private static long _checkpointTicks;
        private static long _maxCheckpointTicks;
        private static long _skipped;
        private static bool _disabled;

        internal static string GetDiagnostics()
        {
            long count = Interlocked.Read(ref _checkpoints);
            return string.Format(CultureInfo.InvariantCulture,
                "{0}/{1:0.###}/{2:0.###}(count/total_ms/max_ms) skipped={3}" +
                " disabled={4}",
                count,
                Interlocked.Read(ref _checkpointTicks) * 1000.0 /
                    Stopwatch.Frequency,
                Interlocked.Read(ref _maxCheckpointTicks) * 1000.0 /
                    Stopwatch.Frequency,
                Interlocked.Read(ref _skipped),
                _disabled);
        }

        /// <summary>
        ///     权威周期每帧调用。自身节流,绝大多数调用是几十纳秒的早退。
        ///     不做任何 I/O,只决定要不要派一个后台任务。
        /// </summary>
        internal static void RequestIfDue()
        {
            if (_disabled) return;
            // 先查时钟再查文件:这个方法每个权威周期都被调用(实测约 50Hz),
            // 而 FileInfo 是一次系统调用。节流放在最前面,绝大多数调用就只是
            // 一次时间戳相减。
            if (!IntervalElapsed())
            {
                Interlocked.Increment(ref _skipped);
                return;
            }

            Task running = Volatile.Read(ref _running);
            if (running != null && !running.IsCompleted) return;

            string path = ResolvePath();
            if (string.IsNullOrEmpty(path)) return;
            if (WalBytes(path) <= 0L)
            {
                // WAL 是空的,没什么可搬。推一下时钟,免得空转时每帧都 stat。
                Interlocked.Exchange(ref _lastCompletedAt,
                    Stopwatch.GetTimestamp());
                return;
            }

            Volatile.Write(ref _running, Task.Run(() => RunCheckpoint(path)));
        }

        private static bool IntervalElapsed()
        {
            long last = Interlocked.Read(ref _lastCompletedAt);
            if (last == 0L) return true;
            double elapsed = (Stopwatch.GetTimestamp() - last) /
                (double)Stopwatch.Frequency;
            return elapsed >= MinimumIntervalSeconds;
        }

        private static void RunCheckpoint(string pPath)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                lock (Gate)
                {
                    SQLiteConnection connection = EnsureConnection(pPath);
                    if (connection == null) return;
                    using SQLiteCommand command = connection.CreateCommand();
                    command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                    command.ExecuteNonQuery();
                }

                long elapsed = Stopwatch.GetTimestamp() - started;
                Interlocked.Increment(ref _checkpoints);
                Interlocked.Add(ref _checkpointTicks, elapsed);
                RecordMax(elapsed);
                Interlocked.Exchange(ref _lastCompletedAt,
                    Stopwatch.GetTimestamp());
                _consecutiveFailures = 0;
            }
            catch (Exception error)
            {
                Interlocked.Exchange(ref _lastCompletedAt,
                    Stopwatch.GetTimestamp());
                CloseConnection();
                if (++_consecutiveFailures < MaximumConsecutiveFailures)
                    return;
                // 连续失败说明这条路走不通(文件被占、权限、库损坏)。停掉,
                // 让 wal 由存档时的主线程检查点收拾 —— 那条路一直都在。
                _disabled = true;
                ModClass.LogWarning(
                    "[AW3] 后台 WAL 检查点连续失败 " + _consecutiveFailures +
                    " 次,已停用,改由存档时的主线程检查点兜底: " +
                    error.Message);
            }
        }

        private static void RecordMax(long pElapsed)
        {
            long observed = Interlocked.Read(ref _maxCheckpointTicks);
            while (pElapsed > observed)
            {
                long previous = Interlocked.CompareExchange(
                    ref _maxCheckpointTicks, pElapsed, observed);
                if (previous == observed) return;
                observed = previous;
            }
        }

        private static SQLiteConnection EnsureConnection(string pPath)
        {
            if (_connection != null &&
                string.Equals(_connectionPath, pPath, StringComparison.Ordinal))
                return _connection;

            CloseConnection();
            if (!File.Exists(pPath)) return null;
            var connection = new SQLiteConnection("data source=" + pPath);
            connection.Open();
            using (SQLiteCommand command = connection.CreateCommand())
            {
                // 只设等待时间:journal_mode / synchronous / cache_size 都是主
                // 连接的事,这条连接唯一的工作就是搬 WAL。
                command.CommandText = "PRAGMA busy_timeout=2500;";
                command.ExecuteNonQuery();
            }

            _connection = connection;
            _connectionPath = pPath;
            return _connection;
        }

        private static void CloseConnection()
        {
            SQLiteConnection connection = _connection;
            _connection = null;
            _connectionPath = string.Empty;
            if (connection == null) return;
            try { connection.Close(); } catch { }
            try { connection.Dispose(); } catch { }
        }

        private static string ResolvePath()
        {
            try
            {
                if (LineageArchiveManager.Instance?.InitializeSuccessful != true)
                    return string.Empty;
                return LineageArchiveManager.RuntimeDbPath ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static long WalBytes(string pPath)
        {
            try
            {
                var info = new FileInfo(pPath + "-wal");
                return info.Exists ? info.Length : 0L;
            }
            catch { return 0L; }
        }

        /// <summary>
        ///     世界切换 / 退出时调用。等在途检查点结束再放连接,免得把正在
        ///     搬运的 WAL 连接抽掉。
        /// </summary>
        internal static void Shutdown()
        {
            Task running = Volatile.Read(ref _running);
            if (running != null)
            {
                try { running.Wait(TimeSpan.FromSeconds(2)); }
                catch { }
            }

            Volatile.Write(ref _running, null);
            lock (Gate) { CloseConnection(); }
            Interlocked.Exchange(ref _lastCompletedAt, 0L);
            _consecutiveFailures = 0;
            _disabled = false;
        }
    }
}
