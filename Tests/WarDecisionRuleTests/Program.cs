using System;
using AncientWarfare3.core.lineage;

namespace WarDecisionRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectAllianceWarGate();
            Console.WriteLine("War decision rule tests passed.");
            return 0;
        }

        private static void ExpectAllianceWarGate()
        {
            if (WarAllianceRules.CanStartWar(pSameAlliance: true, pSystemWar: false,
                    pIndependenceWar: false, out string reason))
                throw new Exception("Same-alliance normal wars should be blocked.");
            if (reason != "same_alliance")
                throw new Exception("Same-alliance war block should report same_alliance.");

            if (!WarAllianceRules.CanStartWar(pSameAlliance: false, pSystemWar: false,
                    pIndependenceWar: false, out _))
                throw new Exception("Different alliances should not be blocked by the alliance gate.");

            if (!WarAllianceRules.CanStartWar(pSameAlliance: true, pSystemWar: true,
                    pIndependenceWar: false, out _))
                throw new Exception("System rebellion wars should bypass the alliance gate.");

            if (!WarAllianceRules.CanStartWar(pSameAlliance: true, pSystemWar: false,
                    pIndependenceWar: true, out _))
                throw new Exception("Independence wars should bypass the alliance gate.");
        }
    }
}
