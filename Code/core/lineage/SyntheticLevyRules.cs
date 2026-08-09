namespace AncientWarfare3.core.lineage
{
    public enum SyntheticLevyDisposition
    {
        Ignore = 0,
        RestoreCivilian = 1,
        RemoveActor = 2,
        PromotePermanent = 3
    }

    public enum SyntheticLevyTask
    {
        Military = 0,
        Food = 1,
        Healing = 2,
        Transport = 3,
        Retreat = 4,
        Formation = 5,
        Social = 6,
        Sleep = 7,
        Singing = 8,
        Laughter = 9,
        CivilianWork = 10,
        Marriage = 11,
        Reproduction = 12,
        Office = 13,
        School = 14
    }

    public static class SyntheticLevyRules
    {
        public static SyntheticLevyDisposition ResolveDemobilization(
            bool synthetic, bool alive, int militaryMerit)
        {
            if (!alive) return SyntheticLevyDisposition.Ignore;
            return synthetic
                ? SyntheticLevyDisposition.RemoveActor
                : SyntheticLevyDisposition.RestoreCivilian;
        }

        public static bool SuppressPersonalHistory(bool synthetic,
            bool promoted)
        {
            return synthetic && !promoted;
        }

        public static bool AllowTask(bool synthetic, SyntheticLevyTask task)
        {
            if (!synthetic) return true;
            return task == SyntheticLevyTask.Military ||
                   task == SyntheticLevyTask.Food ||
                   task == SyntheticLevyTask.Healing ||
                   task == SyntheticLevyTask.Transport ||
                   task == SyntheticLevyTask.Retreat ||
                   task == SyntheticLevyTask.Formation ||
                   task == SyntheticLevyTask.Sleep;
        }

        public static bool ShouldClearSyntheticFields(
            SyntheticLevyDisposition disposition)
        {
            return disposition ==
                   SyntheticLevyDisposition.PromotePermanent;
        }

        public static bool ShouldRemoveActor(
            SyntheticLevyDisposition disposition)
        {
            return disposition == SyntheticLevyDisposition.RemoveActor;
        }
    }
}
