using System;

namespace AncientWarfare3.core.pathfinding
{
    public enum AWPathfindingMode
    {
        Aw3,
        Vanilla
    }

    public static class AWPathfindingRuntimeModeRules
    {
        public const string EnvironmentVariable = "AW3_PATHFINDING";

        public static AWPathfindingMode Parse(string pValue)
        {
            string value = pValue?.Trim();
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "0", StringComparison.Ordinal) ||
                string.Equals(value, "vanilla", StringComparison.OrdinalIgnoreCase))
                return AWPathfindingMode.Vanilla;

            return AWPathfindingMode.Aw3;
        }

        public static string LogName(AWPathfindingMode pMode)
        {
            return pMode == AWPathfindingMode.Vanilla ? "vanilla" : "aw3";
        }
    }

    internal static class AWPathfindingRuntimeMode
    {
        private static readonly AWPathfindingMode StartupMode =
            AWPathfindingRuntimeModeRules.Parse(
                Environment.GetEnvironmentVariable(
                    AWPathfindingRuntimeModeRules.EnvironmentVariable));

        public static bool IsAw3 => StartupMode == AWPathfindingMode.Aw3;
        public static string LogName =>
            AWPathfindingRuntimeModeRules.LogName(StartupMode);
    }
}
