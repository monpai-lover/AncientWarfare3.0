namespace AncientWarfare3.core.lineage
{
    internal enum MandateIslandExileStage
    {
        None = 0,
        Evaluating = 1,
        Boarding = 2,
        Voyaging = 3,
        Landing = 4,
        Founding = 5,
        WarPending = 6,
        Completed = 7,
        Failed = 8
    }

    internal static class MandateIslandExileRules
    {
        internal static bool CanStart(bool mandate, bool oneCity,
            bool hasPort, bool active)
        {
            return mandate && oneCity && hasPort && !active;
        }

        internal static bool IsActive(MandateIslandExileStage stage)
        {
            return stage != MandateIslandExileStage.None &&
                   stage != MandateIslandExileStage.Completed &&
                   stage != MandateIslandExileStage.Failed;
        }

        internal static bool IsTerminal(MandateIslandExileStage stage)
        {
            return stage == MandateIslandExileStage.Completed ||
                   stage == MandateIslandExileStage.Failed;
        }
    }
}
