using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public readonly struct KingdomPolicyEffects
    {
        public int ExtraWorkshopAttempts { get; init; }
        public int EquipmentQualityBonus { get; init; }
        public float TaxMultiplier { get; init; }
        public float StorageMultiplier { get; init; }
        public bool OrganizedFamineTransfers { get; init; }
        public float FarmOutputMultiplier { get; init; }
        public float FamineResilience { get; init; }
        public float GarrisonMultiplier { get; init; }
        public float OccupationResistance { get; init; }
        public int ZoneTechnologyTier { get; init; }
        public int LegitimacyBonus { get; init; }
        public int SameCultureOpinion { get; init; }
        public int SuccessionStability { get; init; }
        public bool GovernorAdministrationUnlocked { get; init; }
        public bool VassalAdministrationUnlocked { get; init; }
        public bool WesternCourtUnlocked { get; init; }
        public bool ElectiveTermsUnlocked { get; init; }
        public bool FeudalRetainersUnlocked { get; init; }
        public bool RoyalAppointmentsUnlocked { get; init; }
        public bool CentralizationUnlocked { get; init; }
        public float AdministrationMultiplier { get; init; }
        public int NobleOpinion { get; init; }
        public int VassalOpinion { get; init; }

        public static KingdomPolicyEffects Neutral => new KingdomPolicyEffects
        {
            TaxMultiplier = 1f,
            StorageMultiplier = 1f,
            FarmOutputMultiplier = 1f,
            GarrisonMultiplier = 1f,
            AdministrationMultiplier = 1f
        };
    }

    public static class KingdomPolicyEffectRules
    {
        public const int MaximumWorkshopAttempts = 3;
        public const int MaximumEquipmentQualityBonus = 3;
        public const float MaximumTaxMultiplier = 1.55f;
        public const float MaximumAdministrationMultiplier = 1.25f;

        public static KingdomPolicyEffects Resolve(
            KingdomPolicyProfileId pProfile,
            IEnumerable<string> pCompletedNodeIds,
            string pGovernmentState)
        {
            if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    pProfile))
                return KingdomPolicyEffects.Neutral;

            var completed = new HashSet<string>(StringComparer.Ordinal);
            if (pCompletedNodeIds != null)
            {
                foreach (string id in pCompletedNodeIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        completed.Add(id.Trim());
                }
            }

            int workshop = 0;
            int equipment = 0;
            float tax = 1f;
            float storage = 1f;
            bool famineTransfers = false;
            float farm = 1f;
            float famineResilience = 0f;
            float garrison = 1f;
            float occupationResistance = 0f;
            int zoneTier = 0;
            int legitimacy = 0;
            int sameCultureOpinion = 0;
            int successionStability = 0;
            bool governorAdministration = false;
            bool vassalAdministration = false;
            bool westernCourt = false;
            bool electiveTerms = false;
            bool feudalRetainers = false;
            bool royalAppointments = false;
            bool centralization = false;
            float administration = 1f;
            int nobleOpinion = 0;
            int vassalOpinion = 0;

            if (completed.Contains("aw_tech_writing"))
                administration += 0.03f;
            if (completed.Contains("aw_tech_pottery_casting"))
                workshop++;
            if (completed.Contains("aw_tech_bronze_casting"))
            {
                workshop++;
                equipment++;
            }
            if (completed.Contains("aw_tech_granary_accounting"))
            {
                storage += 0.25f;
                famineTransfers = true;
            }
            if (completed.Contains("aw_tech_city_defense"))
            {
                garrison += 0.15f;
                occupationResistance += 0.15f;
            }
            if (completed.Contains("aw_tech_well_field_survey"))
            {
                zoneTier++;
                farm += 0.05f;
            }

            if (pProfile == KingdomPolicyProfileId.WesternGeneral)
            {
                if (completed.Contains("aw_west_tech_iron_casting"))
                {
                    workshop++;
                    equipment += 2;
                }
                if (completed.Contains("aw_west_tech_coin_minting"))
                    tax += 0.08f;
                if (completed.Contains("aw_west_tech_irrigation"))
                {
                    farm += 0.18f;
                    famineResilience += 0.20f;
                }
                if (completed.Contains("aw_west_tech_enfeoffment_study"))
                {
                    governorAdministration = true;
                    vassalAdministration = true;
                }
                if (completed.Contains("aw_west_tech_tax_office"))
                    tax += 0.07f;
                if (completed.Contains("aw_west_tech_landlord_tax"))
                {
                    tax += 0.20f;
                    nobleOpinion -= 10;
                }
                if (completed.Contains("aw_west_tech_office_system"))
                {
                    westernCourt = true;
                    administration += 0.10f;
                }
                if (completed.Contains("aw_west_tech_elective_offices"))
                {
                    electiveTerms = true;
                    administration += 0.03f;
                }
                if (completed.Contains("aw_west_tech_ritual_order"))
                {
                    legitimacy += 10;
                    sameCultureOpinion += 5;
                    successionStability += 10;
                }
                if (completed.Contains("aw_west_tech_feudal_retainers"))
                {
                    feudalRetainers = true;
                    garrison += 0.10f;
                }

                if (completed.Contains("aw_west_policy_landlord_taxation"))
                {
                    tax += 0.12f;
                    nobleOpinion -= 5;
                }
                if (completed.Contains("aw_west_policy_noble_council"))
                {
                    nobleOpinion += 10;
                    administration += 0.05f;
                }

                bool directRule = string.Equals(pGovernmentState,
                    "western_royal_direct", StringComparison.Ordinal) &&
                    completed.Contains("aw_west_tech_royal_domain") &&
                    completed.Contains("aw_west_policy_royal_direct_rule");
                if (directRule)
                {
                    royalAppointments = true;
                    centralization = true;
                    tax += 0.08f;
                    administration += 0.15f;
                    nobleOpinion -= 20;
                    vassalOpinion -= 10;
                }
            }

            return new KingdomPolicyEffects
            {
                ExtraWorkshopAttempts = Math.Min(
                    MaximumWorkshopAttempts, workshop),
                EquipmentQualityBonus = Math.Min(
                    MaximumEquipmentQualityBonus, equipment),
                TaxMultiplier = Math.Min(MaximumTaxMultiplier, tax),
                StorageMultiplier = Math.Min(1.5f, storage),
                OrganizedFamineTransfers = famineTransfers,
                FarmOutputMultiplier = Math.Min(1.4f, farm),
                FamineResilience = Math.Min(0.4f, famineResilience),
                GarrisonMultiplier = Math.Min(1.35f, garrison),
                OccupationResistance = Math.Min(0.35f,
                    occupationResistance),
                ZoneTechnologyTier = Math.Min(5, zoneTier),
                LegitimacyBonus = Math.Min(20, legitimacy),
                SameCultureOpinion = Math.Min(10, sameCultureOpinion),
                SuccessionStability = Math.Min(20, successionStability),
                GovernorAdministrationUnlocked = governorAdministration,
                VassalAdministrationUnlocked = vassalAdministration,
                WesternCourtUnlocked = westernCourt,
                ElectiveTermsUnlocked = electiveTerms,
                FeudalRetainersUnlocked = feudalRetainers,
                RoyalAppointmentsUnlocked = royalAppointments,
                CentralizationUnlocked = centralization,
                AdministrationMultiplier = Math.Min(
                    MaximumAdministrationMultiplier, administration),
                NobleOpinion = Math.Max(-40, Math.Min(20, nobleOpinion)),
                VassalOpinion = Math.Max(-30, Math.Min(10, vassalOpinion))
            };
        }
    }
}
