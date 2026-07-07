namespace AncientWarfare3.core.lineage
{
    public static class WarDecisionTargetOrderRules
    {
        public static int SortOrder(string pGoalOrProjectType)
        {
            switch (pGoalOrProjectType ?? "")
            {
                case "fabricate_core":
                    return 0;
                case "take_mandate":
                    return 5;
                case "take_core_city":
                    return 10;
                case "press_claim_city":
                    return 20;
                case "fabricate_weak_claim":
                    return 24;
                case "fabricate_strong_claim":
                    return 25;
                case "restore_kingdom":
                    return 30;
                case "force_vassal":
                    return 40;
                case "independence":
                    return 50;
                case "no_cb":
                case "no_cb_punitive":
                    return 90;
                default:
                    return 80;
            }
        }
    }
}
