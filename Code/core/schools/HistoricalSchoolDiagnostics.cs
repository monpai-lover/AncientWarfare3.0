using System.Threading;

namespace AncientWarfare3.core.schools
{
    internal enum HistoricalSchoolSnapshotCause
    {
        MapMode,
        Window,
        Consumer
    }

    internal readonly struct HistoricalSchoolDiagnosticSnapshot
    {
        public HistoricalSchoolDiagnosticSnapshot(
            long pYearTokens,
            long pYearEnqueueTicks,
            long pSchedulerFrames,
            long pSchedulerTicks,
            long pSchedulerAllocatedBytes,
            long pIdleFrames,
            long pIdleAllocatedBytes,
            long pSqlBatches,
            long pSqlStatements,
            long pSqlCommitTicks,
            long pSqlRetries,
            long pMapSnapshotRebuilds,
            long pWindowSnapshotRebuilds,
            long pConsumerSnapshotRebuilds,
            long pActiveLectures,
            long pActiveDebates,
            long pActiveTravel,
            long pTaskLeases,
            long pMemberships,
            long pTeachers,
            long pLeaders,
            long pCanonicalMasters,
            long pCacheEntries)
        {
            YearTokens = pYearTokens;
            YearEnqueueTicks = pYearEnqueueTicks;
            SchedulerFrames = pSchedulerFrames;
            SchedulerTicks = pSchedulerTicks;
            SchedulerAllocatedBytes = pSchedulerAllocatedBytes;
            IdleFrames = pIdleFrames;
            IdleAllocatedBytes = pIdleAllocatedBytes;
            SqlBatches = pSqlBatches;
            SqlStatements = pSqlStatements;
            SqlCommitTicks = pSqlCommitTicks;
            SqlRetries = pSqlRetries;
            MapSnapshotRebuilds = pMapSnapshotRebuilds;
            WindowSnapshotRebuilds = pWindowSnapshotRebuilds;
            ConsumerSnapshotRebuilds = pConsumerSnapshotRebuilds;
            ActiveLectures = pActiveLectures;
            ActiveDebates = pActiveDebates;
            ActiveTravel = pActiveTravel;
            TaskLeases = pTaskLeases;
            Memberships = pMemberships;
            Teachers = pTeachers;
            Leaders = pLeaders;
            CanonicalMasters = pCanonicalMasters;
            CacheEntries = pCacheEntries;
        }

        public long YearTokens { get; }
        public long YearEnqueueTicks { get; }
        public long SchedulerFrames { get; }
        public long SchedulerTicks { get; }
        public long SchedulerAllocatedBytes { get; }
        public long IdleFrames { get; }
        public long IdleAllocatedBytes { get; }
        public long SqlBatches { get; }
        public long SqlStatements { get; }
        public long SqlCommitTicks { get; }
        public long SqlRetries { get; }
        public long MapSnapshotRebuilds { get; }
        public long WindowSnapshotRebuilds { get; }
        public long ConsumerSnapshotRebuilds { get; }
        public long ActiveLectures { get; }
        public long ActiveDebates { get; }
        public long ActiveTravel { get; }
        public long TaskLeases { get; }
        public long Memberships { get; }
        public long Teachers { get; }
        public long Leaders { get; }
        public long CanonicalMasters { get; }
        public long CacheEntries { get; }
    }

    internal static class HistoricalSchoolDiagnostics
    {
        private static long _yearTokens;
        private static long _yearEnqueueTicks;
        private static long _schedulerFrames;
        private static long _schedulerTicks;
        private static long _schedulerAllocatedBytes;
        private static long _idleFrames;
        private static long _idleAllocatedBytes;
        private static long _sqlBatches;
        private static long _sqlStatements;
        private static long _sqlCommitTicks;
        private static long _sqlRetries;
        private static long _mapSnapshotRebuilds;
        private static long _windowSnapshotRebuilds;
        private static long _consumerSnapshotRebuilds;
        private static long _activeLectures;
        private static long _activeDebates;
        private static long _activeTravel;
        private static long _taskLeases;
        private static long _memberships;
        private static long _teachers;
        private static long _leaders;
        private static long _canonicalMasters;
        private static long _cacheEntries;

        public static long IdleAllocatedBytes =>
            Interlocked.Read(ref _idleAllocatedBytes);

        public static void RecordYearEnqueue(long pTicks)
        {
            Interlocked.Increment(ref _yearTokens);
            Interlocked.Add(ref _yearEnqueueTicks, pTicks);
        }

        public static void RecordSchedulerFrame(
            long pTicks,
            long pAllocatedBytes,
            bool pIdle)
        {
            Interlocked.Increment(ref _schedulerFrames);
            Interlocked.Add(ref _schedulerTicks, pTicks);
            Interlocked.Add(ref _schedulerAllocatedBytes, pAllocatedBytes);
            if (!pIdle) return;
            Interlocked.Increment(ref _idleFrames);
            Interlocked.Add(ref _idleAllocatedBytes, pAllocatedBytes);
        }

        public static void RecordSqlBatch(
            int pStatements,
            long pCommitTicks,
            bool pRetry)
        {
            Interlocked.Increment(ref _sqlBatches);
            Interlocked.Add(ref _sqlStatements, pStatements);
            Interlocked.Add(ref _sqlCommitTicks, pCommitTicks);
            if (pRetry) Interlocked.Increment(ref _sqlRetries);
        }

        public static void RecordSnapshotRebuild(HistoricalSchoolSnapshotCause pCause)
        {
            switch (pCause)
            {
                case HistoricalSchoolSnapshotCause.MapMode:
                    Interlocked.Increment(ref _mapSnapshotRebuilds);
                    break;
                case HistoricalSchoolSnapshotCause.Window:
                    Interlocked.Increment(ref _windowSnapshotRebuilds);
                    break;
                case HistoricalSchoolSnapshotCause.Consumer:
                    Interlocked.Increment(ref _consumerSnapshotRebuilds);
                    break;
            }
        }

        public static void SetActivityCounts(
            int pLectures,
            int pDebates,
            int pTravel,
            int pTaskLeases)
        {
            Interlocked.Exchange(ref _activeLectures, pLectures);
            Interlocked.Exchange(ref _activeDebates, pDebates);
            Interlocked.Exchange(ref _activeTravel, pTravel);
            Interlocked.Exchange(ref _taskLeases, pTaskLeases);
        }

        public static void SetEcologyCounts(
            int pMemberships,
            int pTeachers,
            int pLeaders,
            int pCanonicalMasters)
        {
            Interlocked.Exchange(ref _memberships, pMemberships);
            Interlocked.Exchange(ref _teachers, pTeachers);
            Interlocked.Exchange(ref _leaders, pLeaders);
            Interlocked.Exchange(ref _canonicalMasters, pCanonicalMasters);
        }

        public static void SetCacheEntries(int pCount)
        {
            Interlocked.Exchange(ref _cacheEntries, pCount);
        }

        public static HistoricalSchoolDiagnosticSnapshot Snapshot()
        {
            return new HistoricalSchoolDiagnosticSnapshot(
                Interlocked.Read(ref _yearTokens),
                Interlocked.Read(ref _yearEnqueueTicks),
                Interlocked.Read(ref _schedulerFrames),
                Interlocked.Read(ref _schedulerTicks),
                Interlocked.Read(ref _schedulerAllocatedBytes),
                Interlocked.Read(ref _idleFrames),
                Interlocked.Read(ref _idleAllocatedBytes),
                Interlocked.Read(ref _sqlBatches),
                Interlocked.Read(ref _sqlStatements),
                Interlocked.Read(ref _sqlCommitTicks),
                Interlocked.Read(ref _sqlRetries),
                Interlocked.Read(ref _mapSnapshotRebuilds),
                Interlocked.Read(ref _windowSnapshotRebuilds),
                Interlocked.Read(ref _consumerSnapshotRebuilds),
                Interlocked.Read(ref _activeLectures),
                Interlocked.Read(ref _activeDebates),
                Interlocked.Read(ref _activeTravel),
                Interlocked.Read(ref _taskLeases),
                Interlocked.Read(ref _memberships),
                Interlocked.Read(ref _teachers),
                Interlocked.Read(ref _leaders),
                Interlocked.Read(ref _canonicalMasters),
                Interlocked.Read(ref _cacheEntries));
        }
    }
}
