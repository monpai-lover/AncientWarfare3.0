namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolRuntimeMembershipRules
    {
        public static bool ShouldIndex(bool hasActiveMembership,
            bool actorExists, bool actorAlive, bool actorWrecked)
        {
            return hasActiveMembership && actorExists && actorAlive &&
                   !actorWrecked;
        }
    }
}
