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

        public static bool AllowTaskId(bool synthetic, string taskOrJobId)
        {
            if (!synthetic) return true;
            if (string.IsNullOrEmpty(taskOrJobId)) return false;
            string id = taskOrJobId.ToLowerInvariant();
            if (id.StartsWith("aw_army_rts_",
                    System.StringComparison.Ordinal) ||
                id.StartsWith("aw_war_deployment",
                    System.StringComparison.Ordinal)) return true;
            if (ContainsAny(id, "sleep", "rest"))
                return AllowTask(true, SyntheticLevyTask.Sleep);
            if (ContainsAny(id, "food", "eat", "hunger"))
                return AllowTask(true, SyntheticLevyTask.Food);
            if (ContainsAny(id, "heal", "cure", "hospital"))
                return AllowTask(true, SyntheticLevyTask.Healing);
            if (ContainsAny(id, "embark", "sail", "landing", "pickup",
                    "transport", "boat", "ship"))
                return AllowTask(true, SyntheticLevyTask.Transport);
            if (ContainsAny(id, "retreat", "flee"))
                return AllowTask(true, SyntheticLevyTask.Retreat);
            if (ContainsAny(id, "formation", "rally", "regroup", "hold"))
                return AllowTask(true, SyntheticLevyTask.Formation);
            if (ContainsAny(id, "attack", "assault", "pursue", "fight",
                    "battle", "warrior", "military", "march", "deploy",
                    "mission"))
                return AllowTask(true, SyntheticLevyTask.Military);
            return false;
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (source.Contains(values[i])) return true;
            return false;
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
