using System;

namespace AncientWarfare3.core.performance
{
    public static class AWThirdPartySchedulerFaultRules
    {
        private const string KnownFunBoostPostfix =
            "FunBoost.Patches.FamilyReproduction." +
            "FamilyManagerNewFamilyPatch.Postfix";

        public static bool ShouldQuarantine(Exception pError)
        {
            return pError != null && ShouldQuarantine(
                pError.GetType(), pError.ToString());
        }

        internal static bool ShouldQuarantine(Type pExceptionType,
            string pTrace)
        {
            if (pExceptionType != typeof(NullReferenceException) ||
                string.IsNullOrEmpty(pTrace))
            {
                return false;
            }

            string firstFrame = FindFirstStackFrame(pTrace);
            const string framePrefix = "at " + KnownFunBoostPostfix;
            if (!firstFrame.StartsWith(framePrefix,
                    StringComparison.Ordinal))
            {
                return false;
            }

            int boundary = framePrefix.Length;
            return firstFrame.Length == boundary ||
                   firstFrame[boundary] == ' ' ||
                   firstFrame[boundary] == '(';
        }

        private static string FindFirstStackFrame(string pTrace)
        {
            string[] lines = pTrace.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("at ", StringComparison.Ordinal))
                {
                    return line.TrimEnd('\r');
                }
            }

            return string.Empty;
        }
    }
}
