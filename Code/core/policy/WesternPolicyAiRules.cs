using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public readonly struct WesternPolicyCandidate
    {
        public WesternPolicyCandidate(string id, bool profileEligible,
            bool available, bool playerLocked, int layoutOrder)
        {
            Id = id ?? string.Empty;
            ProfileEligible = profileEligible;
            Available = available;
            PlayerLocked = playerLocked;
            LayoutOrder = Math.Max(0, layoutOrder);
        }

        public string Id { get; }
        public bool ProfileEligible { get; }
        public bool Available { get; }
        public bool PlayerLocked { get; }
        public int LayoutOrder { get; }
    }

    public readonly struct WesternPolicyNeedFacts
    {
        public WesternPolicyNeedFacts(float foodSecurity = 1f,
            float equipmentQuality = 1f, bool atWar = false,
            bool borderThreat = false, float treasuryRatio = 1f,
            int courtVacancies = 0, int cityCount = 1,
            int royalAuthority = 50, int nobleOpposition = 0,
            float slaveShare = 0f, string dominantSchool = "")
        {
            FoodSecurity = Clamp01(foodSecurity);
            EquipmentQuality = Clamp01(equipmentQuality);
            AtWar = atWar;
            BorderThreat = borderThreat;
            TreasuryRatio = Clamp01(treasuryRatio);
            CourtVacancies = Math.Max(0, courtVacancies);
            CityCount = Math.Max(0, cityCount);
            RoyalAuthority = Clamp100(royalAuthority);
            NobleOpposition = Clamp100(nobleOpposition);
            SlaveShare = Clamp01(slaveShare);
            DominantSchool = dominantSchool ?? string.Empty;
        }

        public float FoodSecurity { get; }
        public float EquipmentQuality { get; }
        public bool AtWar { get; }
        public bool BorderThreat { get; }
        public float TreasuryRatio { get; }
        public int CourtVacancies { get; }
        public int CityCount { get; }
        public int RoyalAuthority { get; }
        public int NobleOpposition { get; }
        public float SlaveShare { get; }
        public string DominantSchool { get; }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }

        private static int Clamp100(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }
    }

    public static class WesternPolicyAiRules
    {
        public static string SelectBest(
            IEnumerable<WesternPolicyCandidate> candidates,
            WesternPolicyNeedFacts facts)
        {
            string bestId = null;
            int bestScore = int.MinValue;
            int bestOrder = int.MaxValue;

            foreach (WesternPolicyCandidate candidate in
                     candidates ?? Array.Empty<WesternPolicyCandidate>())
            {
                if (!candidate.ProfileEligible || !candidate.Available ||
                    candidate.PlayerLocked ||
                    string.IsNullOrWhiteSpace(candidate.Id)) continue;

                int score = Score(candidate.Id, facts);
                if (score < bestScore ||
                    score == bestScore &&
                    candidate.LayoutOrder > bestOrder ||
                    score == bestScore &&
                    candidate.LayoutOrder == bestOrder &&
                    string.CompareOrdinal(candidate.Id, bestId) >= 0)
                    continue;

                bestId = candidate.Id;
                bestScore = score;
                bestOrder = candidate.LayoutOrder;
            }

            return bestId;
        }

        public static int Score(string id, WesternPolicyNeedFacts facts)
        {
            float foodNeed = 1f - facts.FoodSecurity;
            float equipmentNeed = 1f - facts.EquipmentQuality;
            float treasuryNeed = 1f - facts.TreasuryRatio;
            int authorityNeed = 100 - facts.RoyalAuthority;
            int largeRealmNeed = Math.Max(0, facts.CityCount - 3);

            switch (id)
            {
                case "aw_west_tech_irrigation":
                    return Round(foodNeed * 1200f) +
                           SchoolBonus(facts.DominantSchool, "nong", 80);
                case "aw_tech_granary_accounting":
                    return Round(foodNeed * 850f);
                case "aw_tech_well_field_survey":
                    return Round(foodNeed * 650f) + largeRealmNeed * 15;
                case "aw_west_tech_iron_casting":
                    return Round(equipmentNeed * 1200f) +
                           SchoolBonus(facts.DominantSchool, "craftsman", 80);
                case "aw_tech_bronze_casting":
                    return Round(equipmentNeed * 850f);
                case "aw_tech_pottery_casting":
                    return Round(equipmentNeed * 600f);
                case "aw_tech_city_defense":
                    return (facts.AtWar ? 450 : 0) +
                           (facts.BorderThreat ? 650 : 0) +
                           SchoolBonus(facts.DominantSchool, "bing", 80);
                case "aw_west_tech_coin_minting":
                    return Round(treasuryNeed * 900f) +
                           SchoolBonus(facts.DominantSchool, "merchant", 80);
                case "aw_west_tech_tax_office":
                    return Round(treasuryNeed * 1200f);
                case "aw_west_tech_landlord_tax":
                    return Round(treasuryNeed * 750f) -
                           facts.NobleOpposition * 2;
                case "aw_west_tech_office_system":
                    return facts.CourtVacancies * 120 +
                           SchoolBonus(facts.DominantSchool, "fa", 70);
                case "aw_west_tech_elective_offices":
                    return facts.CourtVacancies * 75 +
                           facts.NobleOpposition * 3;
                case "aw_west_tech_enfeoffment_study":
                    return largeRealmNeed * 90;
                case "aw_west_tech_ritual_order":
                    return authorityNeed * 12 +
                           SchoolBonus(facts.DominantSchool, "ru", 80);
                case "aw_west_tech_feudal_retainers":
                    return largeRealmNeed * 55 +
                           (facts.AtWar ? 260 : 0);
                case "aw_west_tech_royal_domain":
                    return authorityNeed * 8 + largeRealmNeed * 35 -
                           facts.NobleOpposition * 2;

                case "aw_policy_household_registry":
                    return 160 + facts.CourtVacancies * 8;
                case "aw_policy_start_slavery":
                    return facts.AtWar && facts.SlaveShare >= 0.20f
                        ? 700 + Round(facts.SlaveShare * 1000f)
                        : -500;
                case "aw_policy_corvee_labor":
                    return largeRealmNeed * 30 +
                           Round(treasuryNeed * 180f);
                case "aw_policy_control_slaves":
                    return facts.SlaveShare >= 0.15f
                        ? Round(facts.SlaveShare * 700f)
                        : -250;
                case "aw_policy_slave_army":
                    return facts.AtWar && facts.SlaveShare >= 0.20f
                        ? 450 + Round(facts.SlaveShare * 900f)
                        : -400;
                case "aw_west_policy_landlord_taxation":
                    return Round(treasuryNeed * 650f) -
                           facts.NobleOpposition;
                case "aw_west_policy_noble_council":
                    return facts.NobleOpposition * 12 +
                           authorityNeed * 2;
                case "aw_west_policy_elective_offices":
                    return facts.CourtVacancies * 65 +
                           facts.NobleOpposition * 5;
                case "aw_west_policy_feudal_retainers":
                    return largeRealmNeed * 60 +
                           facts.NobleOpposition * 3 +
                           (facts.AtWar ? 160 : 0);
                case "aw_west_policy_royal_direct_rule":
                    return authorityNeed * 9 + largeRealmNeed * 35 -
                           facts.NobleOpposition * 5;
                default:
                    return 0;
            }
        }

        private static int SchoolBonus(string actual, string expected,
            int bonus)
        {
            return string.Equals(actual, expected,
                StringComparison.Ordinal) ? bonus : 0;
        }

        private static int Round(float value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
