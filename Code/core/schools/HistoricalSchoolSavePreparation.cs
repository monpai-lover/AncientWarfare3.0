using System;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolSavePreparationResult
    {
        public bool DescentsResolved { get; internal set; }
        public bool DeathsResolved { get; internal set; }
        public bool NobleDeathsResolved { get; internal set; }
        public bool PriorWritesResolved { get; internal set; }
        public bool ActivitiesResolved { get; internal set; }
        public bool WritesResolved { get; internal set; }
        public bool DeathArchivesResolved { get; internal set; }
        public bool AsyncWritesResolved { get; internal set; }
        public bool RuntimeStateAttempted { get; internal set; }
        public bool RuntimeStateResolved { get; internal set; }

        public bool AllResolved => DescentsResolved && DeathsResolved &&
            NobleDeathsResolved && PriorWritesResolved &&
            ActivitiesResolved && WritesResolved &&
            DeathArchivesResolved && AsyncWritesResolved &&
            RuntimeStateResolved;
    }

    public static class HistoricalSchoolSavePreparation
    {
        public static HistoricalSchoolSavePreparationResult Run(
            Func<bool> pFlushDescents,
            Func<bool> pFlushDeaths,
            Func<bool> pFlushNobleDeaths,
            Func<bool> pFlushPriorWrites,
            Func<bool> pFlushActivities,
            Func<bool> pFlushWrites,
            Func<bool> pFlushDeathArchives,
            Func<bool> pFlushAsyncWrites,
            Func<bool> pFlushRuntimeState)
        {
            var result = new HistoricalSchoolSavePreparationResult
            {
                DescentsResolved = Invoke(pFlushDescents),
                DeathsResolved = Invoke(pFlushDeaths),
                NobleDeathsResolved = Invoke(pFlushNobleDeaths),
                PriorWritesResolved = Invoke(pFlushPriorWrites),
                ActivitiesResolved = Invoke(pFlushActivities),
                WritesResolved = Invoke(pFlushWrites),
                DeathArchivesResolved = Invoke(pFlushDeathArchives),
                AsyncWritesResolved = Invoke(pFlushAsyncWrites)
            };
            if (!AllPrerequisitesResolved(result)) return result;

            result.RuntimeStateAttempted = true;
            result.RuntimeStateResolved = Invoke(pFlushRuntimeState);
            return result;
        }

        private static bool AllPrerequisitesResolved(
            HistoricalSchoolSavePreparationResult pResult)
        {
            return pResult.DescentsResolved && pResult.DeathsResolved &&
                pResult.NobleDeathsResolved && pResult.PriorWritesResolved &&
                pResult.ActivitiesResolved && pResult.WritesResolved &&
                pResult.DeathArchivesResolved &&
                pResult.AsyncWritesResolved;
        }

        private static bool Invoke(Func<bool> pStage)
        {
            if (pStage == null) return false;
            try { return pStage(); }
            catch { return false; }
        }
    }
}
