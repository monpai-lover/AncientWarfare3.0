using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct CentralizationEffects
    {
        public CentralizationEffects(float pTaxMultiplier, float pManpowerMultiplier,
            float pUnrestReduction, int pTributeRateBonus,
            int pMilitaryObligationBonus, int pAutonomyCap,
            bool pBlocksInternalVassalWar, bool pIncludesVassalBorderArmies)
        {
            TaxMultiplier = pTaxMultiplier;
            ManpowerMultiplier = pManpowerMultiplier;
            UnrestReduction = pUnrestReduction;
            TributeRateBonus = pTributeRateBonus;
            MilitaryObligationBonus = pMilitaryObligationBonus;
            AutonomyCap = pAutonomyCap;
            BlocksInternalVassalWar = pBlocksInternalVassalWar;
            IncludesVassalBorderArmies = pIncludesVassalBorderArmies;
        }

        public float TaxMultiplier { get; }
        public float ManpowerMultiplier { get; }
        public float UnrestReduction { get; }
        public int TributeRateBonus { get; }
        public int MilitaryObligationBonus { get; }
        public int AutonomyCap { get; }
        public bool BlocksInternalVassalWar { get; }
        public bool IncludesVassalBorderArmies { get; }
    }

    public static class CentralizationRules
    {
        public const int MaximumLevel = 3;

        public static int NormalizeLevel(int pValue)
        {
            return Math.Max(0, Math.Min(MaximumLevel, pValue));
        }

        public static int EffectiveLevel(int pNominalLevel, MandatePhase pPhase)
        {
            return Math.Min(NormalizeLevel(pNominalLevel),
                MandatePhaseRules.MaxCentralization(pPhase));
        }

        public static int ReformCost(int pTargetLevel)
        {
            return pTargetLevel switch
            {
                1 => 45,
                2 => 75,
                3 => 110,
                _ => 0
            };
        }

        public static int ReformCooldownYears(int pTargetLevel)
        {
            return pTargetLevel switch
            {
                1 => 10,
                2 => 20,
                3 => 30,
                _ => 0
            };
        }

        public static string RequiredTechId(int pTargetLevel)
        {
            return pTargetLevel switch
            {
                2 => "aw_tech_official_court",
                3 => "aw_tech_three_departments",
                _ => ""
            };
        }

        public static bool ShouldApplyChaosDowngrade(MandatePhase phase,
            int phaseSinceYear, int lastHandledEpoch)
        {
            return phase == MandatePhase.Chaos &&
                   phaseSinceYear != lastHandledEpoch;
        }

        public static CentralizationEffects Effects(int pEffectiveLevel)
        {
            return NormalizeLevel(pEffectiveLevel) switch
            {
                1 => new CentralizationEffects(1f, 1f, 0f, 5, 0, 85,
                    false, false),
                2 => new CentralizationEffects(1.05f, 1.05f, 3f, 10, 10, 70,
                    true, false),
                3 => new CentralizationEffects(1.10f, 1.10f, 6f, 15, 20, 50,
                    true, true),
                _ => new CentralizationEffects(1f, 1f, 0f, 0, 0, 100,
                    false, false)
            };
        }
    }
}
