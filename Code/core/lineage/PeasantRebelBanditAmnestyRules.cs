using System;

namespace AncientWarfare3.core.lineage
{
    internal enum BanditAmnestyAiDecision
    {
        Suppression = 0,
        Amnesty = 1
    }

    public static class PeasantRebelBanditAmnestyRules
    {
        internal static BanditAmnestyAiDecision ResolveAiDecision(
            int banditStrength, int originStrength)
        {
            long bandit = Math.Max(0, banditStrength);
            long origin = Math.Max(0, originStrength);
            if (bandit <= 0) return BanditAmnestyAiDecision.Suppression;
            if (origin <= 0 || bandit * 2L > origin * 3L)
                return BanditAmnestyAiDecision.Amnesty;
            return BanditAmnestyAiDecision.Suppression;
        }

        public static bool CanAccept(bool bandit, bool strongholdActive,
            bool originValid, bool offeringIsOrigin, bool authoritative,
            bool applying)
        {
            return bandit && strongholdActive && originValid &&
                   offeringIsOrigin && authoritative && !applying;
        }

        public static bool ShouldEndWars(bool accepted)
        {
            return accepted;
        }

        public static string ResolveSettlementClass(bool accepted)
        {
            return accepted ? "default" : "";
        }

        public static string ResolveFailureKey(bool bandit,
            bool strongholdActive, bool originValid,
            bool offeringIsOrigin)
        {
            if (!bandit || !strongholdActive) return "not_bandit_stronghold";
            if (!originValid) return "origin_missing";
            if (!offeringIsOrigin) return "only_origin_may_amnesty";
            return "amnesty_unavailable";
        }

        public static bool CanSelectOffice(bool officeExists,
            bool officeVacant, bool leaderEligible)
        {
            return officeExists && officeVacant && leaderEligible;
        }

        public static bool IncludesOffice(BanditAmnestyRewardKind pKind)
        {
            return pKind == BanditAmnestyRewardKind.Office ||
                   pKind == BanditAmnestyRewardKind.OfficeAndFief;
        }

        public static bool IncludesFief(BanditAmnestyRewardKind pKind)
        {
            return pKind == BanditAmnestyRewardKind.Fief ||
                   pKind == BanditAmnestyRewardKind.OfficeAndFief;
        }

        public static bool IsRewardRecipientNaturalized(
            bool leaderBelongsToOrigin, bool leaderHasCity,
            bool leaderCityBelongsToOrigin)
        {
            return leaderBelongsToOrigin &&
                   (!leaderHasCity || leaderCityBelongsToOrigin);
        }

        public static bool CanAdvance(BanditAmnestySettlementPhase current,
            BanditAmnestySettlementPhase next)
        {
            return current == BanditAmnestySettlementPhase.Prepared &&
                       next == BanditAmnestySettlementPhase.
                           TerritorialSettlement ||
                   current == BanditAmnestySettlementPhase.
                       TerritorialSettlement &&
                       next == BanditAmnestySettlementPhase.RewardPending ||
                   current == BanditAmnestySettlementPhase.RewardPending &&
                       next == BanditAmnestySettlementPhase.Completed;
        }
    }
}
