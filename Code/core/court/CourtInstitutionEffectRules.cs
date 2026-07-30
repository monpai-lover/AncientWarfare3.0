using System;

namespace AncientWarfare3.core.court
{
    public readonly struct CourtInstitutionEffects
    {
        public CourtInstitutionEffects(int pVassalSoftCap,
            int pFeudatoryMaintenanceLoyaltyBonus,
            float pUnrestReduction, float pManpowerMultiplier,
            float pWarriorSlotMultiplier,
            int pDirectVassalAutonomyCapReduction,
            int pCrossCultureOpinionBonus,
            float pDomesticTechSpreadMultiplier,
            float pPolicyOutputMultiplier, float pTechOutputMultiplier,
            float pTaxMultiplier, int pDirectVassalTributeRateBonus)
        {
            VassalSoftCap = pVassalSoftCap;
            FeudatoryMaintenanceLoyaltyBonus =
                pFeudatoryMaintenanceLoyaltyBonus;
            UnrestReduction = pUnrestReduction;
            ManpowerMultiplier = pManpowerMultiplier;
            WarriorSlotMultiplier = pWarriorSlotMultiplier;
            DirectVassalAutonomyCapReduction =
                pDirectVassalAutonomyCapReduction;
            CrossCultureOpinionBonus = pCrossCultureOpinionBonus;
            DomesticTechSpreadMultiplier = pDomesticTechSpreadMultiplier;
            PolicyOutputMultiplier = pPolicyOutputMultiplier;
            TechOutputMultiplier = pTechOutputMultiplier;
            TaxMultiplier = pTaxMultiplier;
            DirectVassalTributeRateBonus =
                pDirectVassalTributeRateBonus;
        }

        public int VassalSoftCap { get; }
        public int FeudatoryMaintenanceLoyaltyBonus { get; }
        public float UnrestReduction { get; }
        public float ManpowerMultiplier { get; }
        public float WarriorSlotMultiplier { get; }
        public int DirectVassalAutonomyCapReduction { get; }
        public int CrossCultureOpinionBonus { get; }
        public float DomesticTechSpreadMultiplier { get; }
        public float PolicyOutputMultiplier { get; }
        public float TechOutputMultiplier { get; }
        public float TaxMultiplier { get; }
        public int DirectVassalTributeRateBonus { get; }
    }

    public static class CourtInstitutionEffectRules
    {
        public const int BaseVassalSoftCap = 6;

        private static readonly CourtInstitutionEffects Neutral =
            new CourtInstitutionEffects(BaseVassalSoftCap, 0, 0f, 1f,
                1f, 0, 0, 1f, 1f, 1f, 1f, 0);

        public static CourtInstitutionEffects Resolve(string pInstitution,
            bool eligibleXiaRealm)
        {
            if (!eligibleXiaRealm) return Neutral;
            switch (pInstitution ?? "")
            {
                case CourtInstitutionId.Han:
                    return new CourtInstitutionEffects(BaseVassalSoftCap,
                        0, 4f, 1.15f, 1.10f, 10, 0, 1f, 1f, 1f, 1f,
                        0);
                case CourtInstitutionId.Tang:
                    return new CourtInstitutionEffects(BaseVassalSoftCap,
                        0, 0f, 1f, 1.12f, 0, 10, 1.10f, 1f, 1f, 1f,
                        0);
                case CourtInstitutionId.Song:
                    return new CourtInstitutionEffects(BaseVassalSoftCap,
                        0, 0f, 1f, 0.90f, 0, 0, 1.20f, 1.15f,
                        1.20f, 1.10f, 10);
                default:
                    return new CourtInstitutionEffects(8, 1, 0f, 1f,
                        1f, 0, 0, 1f, 1f, 1f, 1f, 0);
            }
        }

        public static int CrossCultureOpinion(int pBonus,
            long pMainCultureId, long pTargetCultureId)
        {
            if (pBonus <= 0 || pMainCultureId < 0 ||
                pTargetCultureId < 0 || pMainCultureId == pTargetCultureId)
                return 0;
            return pBonus;
        }

        public static int ApplyWarriorSlotMultiplier(int pBaseSlots,
            float pMultiplier)
        {
            int slots = Math.Max(0, pBaseSlots);
            if (slots == 0 || float.IsNaN(pMultiplier) || pMultiplier <= 0f)
                return 0;
            int adjusted = (int)Math.Round(slots * pMultiplier,
                MidpointRounding.AwayFromZero);
            return Math.Max(1, adjusted);
        }
    }
}
