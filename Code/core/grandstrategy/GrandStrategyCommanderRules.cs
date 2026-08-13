using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.grandstrategy
{
    public static class GrandStrategyCommanderRules
    {
        public static GrandStrategyCommanderAssignment SelectSuccessor(
            IReadOnlyList<GrandStrategyCommanderAssignment> assignments,
            long unavailableActorId)
        {
            GrandStrategyCommanderAssignment best = null;
            if (assignments == null) return null;
            for (int i = 0; i < assignments.Count; i++)
            {
                GrandStrategyCommanderAssignment candidate = assignments[i];
                if (candidate == null || !candidate.Eligible ||
                    candidate.ActorId == unavailableActorId) continue;
                if (best == null || Rank(candidate.Position) < Rank(best.Position) ||
                    Rank(candidate.Position) == Rank(best.Position) &&
                    candidate.Prowess > best.Prowess ||
                    Rank(candidate.Position) == Rank(best.Position) &&
                    candidate.Prowess == best.Prowess &&
                    candidate.ActorId < best.ActorId)
                    best = candidate;
            }
            return best;
        }

        public static GrandStrategyCommanderOutcome ResolveRisk(int roll,
            bool routed, int prowess, double lossesPercent)
        {
            int value = Math.Max(0, Math.Min(10, roll));
            int lossBand = (int)Math.Max(0, Math.Min(100,
                lossesPercent * 100.0));
            int score = value + (routed ? 4 : 0) +
                Math.Max(0, lossBand - 50) / 10 - Math.Max(0, prowess) / 4;
            if (score >= 17) return GrandStrategyCommanderOutcome.Killed;
            if (score >= 12) return GrandStrategyCommanderOutcome.Captured;
            if (score >= 6) return GrandStrategyCommanderOutcome.SeverelyWounded;
            if (score >= 3) return GrandStrategyCommanderOutcome.Wounded;
            return GrandStrategyCommanderOutcome.Safe;
        }

        private static int Rank(GrandStrategyCommanderPosition position)
        {
            return position switch
            {
                GrandStrategyCommanderPosition.Vanguard => 1,
                GrandStrategyCommanderPosition.LeftWing => 2,
                GrandStrategyCommanderPosition.RightWing => 3,
                GrandStrategyCommanderPosition.RearGuard => 4,
                GrandStrategyCommanderPosition.SiegeOfficer => 5,
                _ => 0
            };
        }
    }
}
