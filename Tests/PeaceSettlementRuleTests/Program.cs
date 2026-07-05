using System;
using AncientWarfare3.core.lineage;

namespace PeaceSettlementRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            Expect("core", PeaceSettlementAction.TransferCity, "take_core_city", "attackers");
            Expect("claim", PeaceSettlementAction.TransferCity, "press_claim_city", "attackers");
            Expect("vassal", PeaceSettlementAction.ForceVassal, "force_vassal", "attackers");
            Expect("independence", PeaceSettlementAction.ReleaseVassal, "independence", "attackers");
            Expect("restore", PeaceSettlementAction.RestoreKingdom, "restore_kingdom", "attackers");
            Expect("white", PeaceSettlementAction.WhitePeace, "take_core_city", "peace");
            Expect("defender", PeaceSettlementAction.DefenderVictory, "press_claim_city", "defenders");

            Console.WriteLine("Peace settlement rule tests passed.");
            return 0;
        }

        private static void Expect(string label, PeaceSettlementAction expected, string goal, string winner)
        {
            PeaceSettlementAction actual = PeaceSettlementRules.ResolveAction(goal, winner);
            if (actual != expected)
                throw new Exception($"Expected {label} action {expected}, got {actual}.");
        }
    }
}
