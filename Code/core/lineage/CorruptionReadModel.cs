namespace AncientWarfare3.core.lineage
{
    internal sealed class CorruptionCountrySnapshot
    {
        public int Score;
        public CorruptionSeverity Severity;
        public int LastYear;
        public int HighStreakYears;
        public int VeryHighStreakYears;
        public float CentralPressure;
        public float FiscalPressure;
        public int AverageCityScore;
        public int HighestCityScore;
        public long HighestCityId = -1L;
        public bool CleanupActive;
    }

    internal sealed class CorruptionCitySnapshot
    {
        public int Score;
        public CorruptionSeverity Severity;
        public int LastYear;
        public int HighStreakYears;
        public float TaxPressure;
        public float OfficialPressure;
        public float OrderPressure;
        public float FoodPressure;
    }
}
