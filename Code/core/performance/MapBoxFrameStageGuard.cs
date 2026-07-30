using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal static class MapBoxFrameStageGuard
    {
        private static readonly HashSet<string> FailedStages =
            new HashSet<string>(StringComparer.Ordinal);

        public static bool Run(string pStage, Action pAction)
        {
            string stage = string.IsNullOrEmpty(pStage)
                ? "unknown"
                : pStage;
            if (FailedStages.Contains(stage) || pAction == null) return false;

            try
            {
                pAction();
                return true;
            }
            catch (Exception error)
            {
                FailedStages.Add(stage);
                ModClass.LogWarning("AW3 MapBox frame stage failed and was " +
                                    "disabled: " + stage + "\n" +
                                    error.ToString());
                return false;
            }
        }

        public static void Reset()
        {
            FailedStages.Clear();
        }
    }
}
