using System;

namespace AncientWarfare3.core.lineage
{
    public static class WartimeMilitaryTaskRules
    {
        public static bool AllowsTask(bool pWartimeMilitary,
            string pTaskId)
        {
            return !pWartimeMilitary || !IsCivilianTask(pTaskId);
        }

        public static bool ShouldEvaluateMilitaryState(string pTaskId)
        {
            return IsCivilianTask(pTaskId);
        }

        public static bool IsCivilianTask(string pTaskId)
        {
            if (string.IsNullOrEmpty(pTaskId)) return false;
            if (pTaskId.StartsWith("socialize_",
                    StringComparison.Ordinal)) return true;
            switch (pTaskId)
            {
                case "happy_laughing":
                case "singing":
                case "swearing":
                case "crying":
                case "reflection":
                case "madness_random_emotion":
                case "decide_where_to_sleep":
                case "sleep_inside":
                case "sleep_outside":
                case "poop_inside":
                case "poop_outside":
                    return true;
                default:
                    return false;
            }
        }
    }
}
