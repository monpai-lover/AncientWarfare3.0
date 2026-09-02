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
        // StateGovernment 原本是一个阶段,里面串了四个互不相干的子服务。实测
        // annual_state_government:73.824/2/68.088 —— 单次 68ms,是整个权威周期
        // 里最大的单一尖峰(同区间 worst_frame_ms 的 90%+ 都归它)。
        //
        // 阶段是这套调度的最小不可分割单位:RunStage 跑完一个阶段就重新入队,
        // 让出这一帧。所以阶段切得越细,单帧峰值越低 —— 拆开这四个子服务不改
        // 任何行为(它们本来就是顺序执行、互不依赖),只是让调度器有机会在中间
        // 让出。总工作量不变,峰值除以四。
        StateGovernmentExam = 11,
        StateGovernmentCareer = 12,
        StateGovernmentMinisterial = 13,
        StateGovernmentTribute = 14,
        StateRealm = 15,
        StrategyMandate = 16,
        StrategyDiplomacy = 17,
        StrategyMilitary = 18,
        Complete = 19
    }

    public static class KingdomAnnualWorkRules
    {
        public const int StageCount = 19;

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
