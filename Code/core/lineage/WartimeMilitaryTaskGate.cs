namespace AncientWarfare3.core.lineage
{
    internal static class WartimeMilitaryTaskGate
    {
        public static bool Allows(Actor pActor, string pTaskId)
        {
            try
            {
                return WartimeMilitaryTaskRules.AllowsTask(
                    ActiveMilitaryLifecycleService.
                        IsWartimeMilitaryActor(pActor), pTaskId);
            }
            catch
            {
                return true;
            }
        }
    }
}
