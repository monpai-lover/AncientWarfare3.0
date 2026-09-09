namespace AncientWarfare3.core.court
{
    public static class MilitaryExamAppointmentRules
    {
        public static string ResolveTier(bool localPassed, bool palacePassed,
            bool imperialMode, bool palaceAvailable)
        {
            if (!localPassed) return "none";
            if (palaceAvailable && imperialMode && palacePassed) return "general";
            return "local";
        }

        public static bool CanAppoint(bool alive, float age)
        {
            return alive && MilitaryExamRules.IsServiceAgeAllowed(age);
        }
    }
}
