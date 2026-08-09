namespace AncientWarfare3.core.lineage
{
    internal static class WartimeMilitaryTaskGate
    {
        public static bool Allows(Actor pActor, string pTaskId)
        {
            if (SyntheticLevyService.IsSynthetic(pActor))
                return SyntheticLevyRules.AllowTaskId(true, pTaskId);
            if (!WartimeMilitaryTaskRules.
                    ShouldEvaluateMilitaryState(pTaskId)) return true;
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
