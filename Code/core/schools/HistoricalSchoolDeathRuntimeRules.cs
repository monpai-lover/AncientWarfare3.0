namespace AncientWarfare3.core.schools
{
    public enum HistoricalSchoolMembershipLoadAction
    {
        Ignore,
        KeepActive,
        QueueDeath
    }

    public static class HistoricalSchoolDeathRuntimeRules
    {
        public static HistoricalSchoolMembershipLoadAction ResolveLoadAction(
            bool activeMembership,
            bool actorLookupReady,
            bool actorExists,
            bool actorAlive,
            bool actorWrecked)
        {
            if (!activeMembership) return HistoricalSchoolMembershipLoadAction.Ignore;
            if (!actorLookupReady) return HistoricalSchoolMembershipLoadAction.KeepActive;
            if (!actorExists) return HistoricalSchoolMembershipLoadAction.KeepActive;
            return actorAlive && !actorWrecked
                ? HistoricalSchoolMembershipLoadAction.KeepActive
                : HistoricalSchoolMembershipLoadAction.QueueDeath;
        }

        public static bool ShouldExposeMembership(
            bool activeMembership,
            bool actorExists,
            bool actorAlive,
            bool actorWrecked,
            bool deathPending)
        {
            return activeMembership && actorExists && actorAlive && !actorWrecked &&
                   !deathPending;
        }
    }
}
