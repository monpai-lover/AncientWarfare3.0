using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct WarParticipantMobilizationBaselines
    {
        public WarParticipantMobilizationBaselines(int pAttackers,
            int pDefenders)
        {
            Attackers = WarParticipantMobilizationBaselineRules.
                NormalizePotential(pAttackers);
            Defenders = WarParticipantMobilizationBaselineRules.
                NormalizePotential(pDefenders);
        }

        public int Attackers { get; }
        public int Defenders { get; }
    }

    internal static class WarParticipantMobilizationBaselineService
    {
        public static WarParticipantMobilizationBaselines
            RegisterExistingParticipants(War pWar)
        {
            if (pWar?.data == null)
                return new WarParticipantMobilizationBaselines(1, 1);
            var seen = new HashSet<long>();
            int attackers = 0;
            int defenders = 0;
            try
            {
                foreach (Kingdom kingdom in pWar.getAttackers())
                    attackers = AddSaturating(attackers,
                        RegisterParticipant(pWar, kingdom, seen));
            }
            catch { }
            try
            {
                foreach (Kingdom kingdom in pWar.getDefenders())
                    defenders = AddSaturating(defenders,
                        RegisterParticipant(pWar, kingdom, seen));
            }
            catch { }
            return new WarParticipantMobilizationBaselines(attackers,
                defenders);
        }

        private static int RegisterParticipant(War pWar, Kingdom pKingdom,
            HashSet<long> pSeen)
        {
            if (pWar?.data == null || pKingdom?.data == null ||
                !pSeen.Add(pKingdom.id)) return 0;
            string key = WarParticipantMobilizationBaselineRules.
                PotentialKey(pKingdom.id);
            pWar.data.get(key, out int recorded, 0);
            int livePotential = recorded > 0
                ? 0
                : WartimeMilitaryPotentialService.CountPotentialWarriors(
                    pKingdom);
            int contribution = WarParticipantMobilizationBaselineRules.
                ResolveContribution(recorded, livePotential);
            if (recorded <= 0) pWar.data.set(key, contribution);
            return contribution;
        }

        private static int AddSaturating(int pLeft, int pRight)
        {
            long total = (long)Math.Max(0, pLeft) + Math.Max(0, pRight);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }
    }
}
