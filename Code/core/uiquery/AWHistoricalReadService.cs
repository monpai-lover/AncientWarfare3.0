using System;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.uiquery
{
    internal static class AWHistoricalReadService
    {
        private static readonly object Gate = new object();
        private static readonly AWHistoricalReadWorker Worker =
            new AWHistoricalReadWorker();
        private static bool _worldStarted;

        public static bool Ready
        {
            get { lock (Gate) return _worldStarted && Worker.Accepting; }
        }

        public static int PendingCount => Worker.PendingCount;
        public static int PendingDrainCount => Worker.PendingCount +
                                               Worker.PendingCompletionCount;
        public static bool WorkerAlive => Worker.WorkerAlive;
        public static bool ConnectionOpen => Worker.ConnectionOpen;

        public static AWAsyncDiagnosticsSnapshot SnapshotDiagnostics()
        {
            return Worker.SnapshotDiagnostics();
        }

        public static void StartWorld(long pWorldGeneration)
        {
            if (!ClearWorld(TimeSpan.FromSeconds(2), out string clearError))
                throw new InvalidOperationException(
                    "Cannot start historical reader: " + clearError);
            if (!AWAsyncRuntime.UiEnabled && !AWAsyncRuntime.ShadowEnabled)
                return;
            if (LineageArchiveManager.Instance.OperatingDB == null) return;
            try
            {
                Worker.StartWorld(LineageArchiveManager.RuntimeDbPath,
                    LineageArchiveManager.RuntimeDatabaseEpoch,
                    pWorldGeneration);
                lock (Gate) _worldStarted = true;
            }
            catch (Exception error)
            {
                lock (Gate) _worldStarted = false;
                ModClass.LogWarning(
                    "Historical read worker start failed: " + error.Message);
                throw;
            }
        }

        public static bool TrySchedule(AWHistoricalReadRequest pRequest)
        {
            return TrySchedule(pRequest, out _);
        }

        public static bool TrySchedule(AWHistoricalReadRequest pRequest,
            out long pRequestId)
        {
            pRequestId = -1L;
            if (!AWAsyncRuntime.UiEnabled && !AWAsyncRuntime.ShadowEnabled)
                return false;
            lock (Gate)
                if (!_worldStarted) return false;
            return Worker.TrySchedule(pRequest, out pRequestId);
        }

        public static bool ReleaseRequest(long pRequestId, string pKey)
        {
            return Worker.Cancel(pRequestId, pKey);
        }

        public static void DrainMainThread(int pMaximumCompletions)
        {
            Worker.DrainMainThread(pMaximumCompletions);
        }

        public static void DrainMainThread(double pMilliseconds,
            int pMaximumCompletions)
        {
            Worker.DrainMainThread(pMilliseconds, pMaximumCompletions);
        }

        public static bool TryEnterSaveBarrier(TimeSpan pTimeout,
            out string pError)
        {
            lock (Gate)
                if (!_worldStarted)
                {
                    pError = string.Empty;
                    return true;
                }
            return Worker.TryEnterSaveBarrier(pTimeout, out pError);
        }

        public static void ExitSaveBarrier()
        {
            lock (Gate)
                if (!_worldStarted) return;
            Worker.ExitSaveBarrier();
        }

        public static bool ClearWorld(TimeSpan pTimeout, out string pError)
        {
            if (!Worker.ClearWorld(pTimeout, out pError)) return false;
            lock (Gate) _worldStarted = false;
            pError = string.Empty;
            return true;
        }

        public static void ClearWorld(TimeSpan pTimeout)
        {
            if (ClearWorld(pTimeout, out string error)) return;
            throw new InvalidOperationException(error);
        }

        public static bool TryShutdown(TimeSpan pTimeout, out string pError)
        {
            if (!Worker.TryShutdown(pTimeout, out pError)) return false;
            lock (Gate) _worldStarted = false;
            pError = string.Empty;
            return true;
        }

        public static void Shutdown(TimeSpan pTimeout)
        {
            if (TryShutdown(pTimeout, out string error)) return;
            throw new InvalidOperationException(error);
        }
    }
}
