namespace AncientWarfare3.core.policy
{
    public static class KingSuccessionPerformanceStage
    {
        public const string DeathCapture =
            "king_succession_death_capture";
        public const string CandidateSnapshot =
            "king_succession_candidate_snapshot";
        public const string DisputeFacts =
            "king_succession_dispute_facts";
        public const string DisputeEnqueue =
            "king_succession_dispute_enqueue";
        public const string CivilLookup =
            "king_civil_service_lookup";
        public const string CivilEnqueue =
            "king_civil_service_enqueue";
    }

    public enum ActorDeathPerformanceStage
    {
        MilitaryIndexes = 0,
        DynasticTitle = 1,
        NobleTitle = 2,
        DeathCause = 3,
        RulerSnapshot = 4,
        HistoricalFigure = 5,
        LineageEligibility = 6,
        LineageArchive = 7,
        KingSuccession = 8,
        FormerRuler = 9,
        PersonHistory = 10,
        BondDeath = 11,
        SchoolDeath = 12,
        RoyalClaim = 13,
        RoyalGuard = 14,
        KingHeirPreparation = 15,
        KingChronicle = 16,
        KingCivilService = 17
    }

    public static class ActorDeathPerformanceRules
    {
        private static readonly string[] StageIds =
        {
            "military_indexes",
            "dynastic_title",
            "noble_title",
            "death_cause",
            "ruler_snapshot",
            "historical_figure",
            "lineage_eligibility",
            "lineage_archive",
            "king_succession",
            "former_ruler",
            "person_history",
            "bond_death",
            "school_death",
            "royal_claim",
            "royal_guard",
            "king_heir_prepare",
            "king_chronicle",
            "king_civil_service"
        };

        public static int StageCount => StageIds.Length;

        public static bool IsValid(ActorDeathPerformanceStage pStage)
        {
            int index = (int)pStage;
            return index >= 0 && index < StageIds.Length;
        }

        public static string Id(ActorDeathPerformanceStage pStage)
        {
            return IsValid(pStage) ? StageIds[(int)pStage] : "unknown";
        }
    }
}
