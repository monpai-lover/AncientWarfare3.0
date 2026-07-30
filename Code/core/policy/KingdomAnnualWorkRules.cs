using System;

namespace AncientWarfare3.core.policy
{
    public enum KingdomAnnualWorkStage
    {
        Succession = 0,
        RoyalAsylum = 1,
        WarMobilization = 2,
        Policy = 3,
        CourtSupport = 4,
        CourtAuxiliary = 5,
        ConferredPosthumous = 6,
        DiplomaticMarriage = 7,
        NobleRemarriage = 8,
        DiplomaticOperation = 9,
        StateEconomy = 10,
        StateGovernment = 11,
        StateRealm = 12,
        StrategyMandate = 13,
        StrategyDiplomacy = 14,
        StrategyMilitary = 15,
        Complete = 16
    }

    public static class KingdomAnnualWorkRules
    {
        public const int StageCount = 16;

        public static KingdomAnnualWorkStage NextStage(
            KingdomAnnualWorkStage pStage)
        {
            int next = Math.Min((int)KingdomAnnualWorkStage.Complete,
                (int)pStage + 1);
            return (KingdomAnnualWorkStage)next;
        }

        public static bool ShouldAcceptSchedule(int pendingYear,
            int requestedYear)
        {
            return requestedYear >= 0 && requestedYear > pendingYear;
        }

        public static int MergeYear(int pendingYear, int requestedYear)
        {
            return Math.Max(pendingYear, requestedYear);
        }

        public static string CoalescingKey(long pKingdomId)
        {
            return "kingdom_annual:" + pKingdomId;
        }
    }
}
