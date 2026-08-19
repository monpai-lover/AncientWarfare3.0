using System;

namespace AncientWarfare3.core.performance
{
    public static class FramePrioritySchedulerConfirmationRules
    {
        public static bool RequiresConfirmation(bool currentValue,
            bool requestedValue)
        {
            return !currentValue && requestedValue;
        }

        public static bool IsAccepted(string pInput)
        {
            return string.Equals(pInput, "yes", StringComparison.Ordinal);
        }
    }
}
