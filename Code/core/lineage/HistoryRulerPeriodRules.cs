namespace AncientWarfare3.core.lineage
{
    internal static class HistoryRulerPeriodRules
    {
        public static bool IsRulerTransition(string pEventType)
        {
            return pEventType == KingdomEvent.RULE_CHANGE ||
                   pEventType == KingdomEvent.RULER_CHANGE;
        }

        public static bool IsRegnalPeriod(string pEventType)
        {
            return pEventType == KingdomEvent.RULE_CHANGE;
        }
    }
}
