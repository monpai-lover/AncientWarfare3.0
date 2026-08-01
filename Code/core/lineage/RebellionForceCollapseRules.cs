namespace AncientWarfare3.core.lineage
{
    public static class RebellionForceCollapseRules
    {
        public static bool ShouldCollapse(bool pWarValid,
            bool pWarActive, bool pAuthoritativeRebellion,
            bool pRebelParticipantValid, bool pWarriorFactsReadable,
            int pRebelWarriors, int pRebelReserves)
        {
            return pWarValid && pWarActive &&
                   pAuthoritativeRebellion && pRebelParticipantValid &&
                   pWarriorFactsReadable && pRebelWarriors == 0 &&
                   pRebelReserves == 0;
        }
    }
}
