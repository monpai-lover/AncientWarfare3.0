namespace AncientWarfare3.core.lineage
{
    public static class AllianceConversationRules
    {
        public static bool ShouldRecordJoin(bool creatingNewAlliance)
        {
            return !creatingNewAlliance;
        }

        public static bool ShouldRecordCreation(bool namingCallbacksCompleted,
            bool hasTwoFounders)
        {
            return namingCallbacksCompleted && hasTwoFounders;
        }

        public static string ResolveRecordedName(string finalDisplayName,
            string generatedFallback)
        {
            string finalName = (finalDisplayName ?? "").Trim();
            if (finalName.Length > 0 &&
                !string.Equals(finalName, "name",
                    System.StringComparison.OrdinalIgnoreCase))
                return finalName;
            return (generatedFallback ?? "").Trim();
        }
    }
}
