using System;
using AncientWarfare3.core.lineage;

namespace RoyalGuardActionRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectNoFollowMove("no_target", pHasTarget: false, pTargetIsCurrentTile: false);
            ExpectNoFollowMove("already_at_target", pHasTarget: true, pTargetIsCurrentTile: true);
            ExpectFollowMove("new_target", pHasTarget: true, pTargetIsCurrentTile: false);

            ExpectNoThreatSearch("peaceful_idle", pHasEnemyWar: false, pKingOrGuardUnderAttack: false);
            ExpectThreatSearch("wartime", pHasEnemyWar: true, pKingOrGuardUnderAttack: false);
            ExpectThreatSearch("direct_attack", pHasEnemyWar: false, pKingOrGuardUnderAttack: true);

            if (RoyalGuardActionRules.WaitAfterNoThreat(2f, 5f) != 2f)
                throw new Exception("Expected royal guard no-threat wait low bound.");
            if (RoyalGuardActionRules.WaitAfterFollowIdle(3f, 6f) != 3f)
                throw new Exception("Expected royal guard follow-idle wait low bound.");

            Console.WriteLine("Royal guard action rule tests passed.");
            return 0;
        }

        private static void ExpectNoFollowMove(string pLabel, bool pHasTarget, bool pTargetIsCurrentTile)
        {
            if (RoyalGuardActionRules.ShouldIssueFollowMove(pHasTarget, pTargetIsCurrentTile))
                throw new Exception($"Expected follow move '{pLabel}' to be blocked.");
        }

        private static void ExpectFollowMove(string pLabel, bool pHasTarget, bool pTargetIsCurrentTile)
        {
            if (!RoyalGuardActionRules.ShouldIssueFollowMove(pHasTarget, pTargetIsCurrentTile))
                throw new Exception($"Expected follow move '{pLabel}' to be allowed.");
        }

        private static void ExpectNoThreatSearch(string pLabel, bool pHasEnemyWar, bool pKingOrGuardUnderAttack)
        {
            if (RoyalGuardActionRules.ShouldSearchThreats(pHasEnemyWar, pKingOrGuardUnderAttack))
                throw new Exception($"Expected threat search '{pLabel}' to be blocked.");
        }

        private static void ExpectThreatSearch(string pLabel, bool pHasEnemyWar, bool pKingOrGuardUnderAttack)
        {
            if (!RoyalGuardActionRules.ShouldSearchThreats(pHasEnemyWar, pKingOrGuardUnderAttack))
                throw new Exception($"Expected threat search '{pLabel}' to be allowed.");
        }
    }
}
