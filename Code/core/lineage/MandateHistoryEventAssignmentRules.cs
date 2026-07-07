namespace AncientWarfare3.core.lineage
{
    public static class MandateHistoryEventAssignmentRules
    {
        public static bool ShouldPreferActorReign(string pEventType, long pActorId)
        {
            if (pActorId < 0) return false;
            return pEventType == "mandate_ruler_title";
        }
    }
}
