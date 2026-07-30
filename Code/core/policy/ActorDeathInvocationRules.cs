namespace AncientWarfare3.core.policy
{
    public static class ActorDeathInvocationRules
    {
        public static bool ShouldProcess(bool pWasAliveAtEntry,
            bool pIsAliveAfterCall)
        {
            return pWasAliveAtEntry && !pIsAliveAfterCall;
        }
    }
}
