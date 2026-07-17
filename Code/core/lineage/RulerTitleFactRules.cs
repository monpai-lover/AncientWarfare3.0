using System;

namespace AncientWarfare3.core.lineage
{
    public static class RulerTitleFactRules
    {
        public const int LowStat = 6;
        public const int HighStat = 12;
        public const int ExcellentStat = 20;

        public static int NormalizeStat(int pTotal, int pExcludedBonus)
        {
            return Math.Max(0, pTotal - Math.Max(0, pExcludedBonus));
        }

        public static bool CanBuildReignSnapshot(long pReignId, long pKingdomId)
        {
            return pReignId >= 0 || pKingdomId >= 0;
        }

        public static RulerTitleDerivedFacts Derive(RulerTitleFacts pFacts)
        {
            if (pFacts == null) return new RulerTitleDerivedFacts();

            bool Has(RulerTraitFlags flag) => (pFacts.Traits & flag) != 0;
            bool graveCrime = Has(RulerTraitFlags.Evil) ||
                              Has(RulerTraitFlags.Psychopath) ||
                              Has(RulerTraitFlags.Bloodlust) ||
                              Has(RulerTraitFlags.Kingslayer) ||
                              Has(RulerTraitFlags.Madness) ||
                              pFacts.AtrocityCount > 0;
            bool populationStable = pFacts.StartPopulation <= 0 ||
                                    (long)pFacts.EndPopulation * 10 >=
                                    (long)pFacts.StartPopulation * 9;

            var result = new RulerTitleDerivedFacts
            {
                CivilScore = pFacts.Stewardship + pFacts.Intelligence + pFacts.Diplomacy,
                MartialScore = pFacts.Warfare * 2 +
                               (pFacts.WarWins - pFacts.WarLosses) * 4 +
                               pFacts.CityDelta * 2,
                GraveCrime = graveCrime
            };
            result.Diligent = Has(RulerTraitFlags.Diligent) ||
                               pFacts.Stewardship >= HighStat || pFacts.MajorReforms >= 2;
            result.Just = (Has(RulerTraitFlags.Honest) || Has(RulerTraitFlags.Just)) &&
                          !Has(RulerTraitFlags.Evil) && !Has(RulerTraitFlags.Deceitful) &&
                          !Has(RulerTraitFlags.Kingslayer) && pFacts.AtrocityCount == 0;
            result.Ambitious = Has(RulerTraitFlags.Ambitious) || pFacts.CityDelta >= 2 ||
                               pFacts.CentralizationRaised;
            result.Compassionate = (Has(RulerTraitFlags.Peaceful) ||
                                    Has(RulerTraitFlags.Pacifist) ||
                                    Has(RulerTraitFlags.Compassionate)) &&
                                   !Has(RulerTraitFlags.Evil) &&
                                   !Has(RulerTraitFlags.Bloodlust) &&
                                   !Has(RulerTraitFlags.Madness);
            result.Generous = (Has(RulerTraitFlags.Generous) ||
                               (!Has(RulerTraitFlags.Greedy) &&
                                pFacts.Diplomacy >= HighStat)) && populationStable;
            result.Patient = Has(RulerTraitFlags.Patient) ||
                             ((Has(RulerTraitFlags.Content) ||
                               Has(RulerTraitFlags.StrongMinded)) &&
                              pFacts.ReignYears >= 10);
            result.Scholar = Has(RulerTraitFlags.Genius) || Has(RulerTraitFlags.Wise) ||
                             pFacts.Intelligence >= HighStat || pFacts.HasSchoolIdentity;
            result.Administrator = pFacts.Stewardship >= HighStat ||
                                   pFacts.MajorReforms >= 1 || pFacts.CentralizationRaised;
            result.Strategist = pFacts.Warfare >= HighStat && pFacts.WarWins >= 2;
            result.Brave = (Has(RulerTraitFlags.Strong) || Has(RulerTraitFlags.Veteran) ||
                            Has(RulerTraitFlags.Tough) || pFacts.Warfare >= HighStat) &&
                           !Has(RulerTraitFlags.FragileHealth) &&
                           !Has(RulerTraitFlags.Pacifist);
            result.FamilyFirst = pFacts.HasBiologicalChildren && pFacts.HasKnownPatriline &&
                                 !pFacts.ForeignLineAdoption;
            result.Healthy = !Has(RulerTraitFlags.Weak) &&
                             !Has(RulerTraitFlags.FragileHealth) &&
                             !Has(RulerTraitFlags.Crippled) && pFacts.Health >= 10;
            result.Frail = Has(RulerTraitFlags.Weak) ||
                           Has(RulerTraitFlags.FragileHealth) ||
                           Has(RulerTraitFlags.Crippled) || pFacts.Health < LowStat;
            result.StableOrder = !pFacts.LostCapital &&
                                 !string.Equals(pFacts.EndReason, "kingdom_fell",
                                     StringComparison.Ordinal) && pFacts.OrderDelta >= 0;
            result.MajorReform = pFacts.MajorReforms >= 2 || pFacts.CentralizationRaised;
            result.GreatConquest = pFacts.WarWins >= 3 && pFacts.CityDelta >= 3 &&
                                    !pFacts.LostCapital;
            result.SmallRealm = pFacts.EndCityCount <= 3;
            return result;
        }
    }
}
