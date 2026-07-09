using System;
using AncientWarfare3.core.court;

namespace CourtSystemRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Expect(CourtRules.IsCourtUnlocked(hasPolicySystem: true, hasOfficialCourtTech: true), "official court unlocks with tech");
                Expect(!CourtRules.IsCourtUnlocked(hasPolicySystem: true, hasOfficialCourtTech: false), "official court stays locked without tech");
                Expect(CourtRules.UsePrimitiveCourt(hasPolicySystem: true, hasOfficialCourtTech: false), "primitive court before tech");
                Expect(!CourtRules.UsePrimitiveCourt(hasPolicySystem: false, hasOfficialCourtTech: true), "unsupported kingdoms do not show full court");

                ExpectEqual(1, CourtRules.CityOfficeSlots(population: 12, zoneCount: 3, isCapital: false), "small city slots");
                ExpectEqual(2, CourtRules.CityOfficeSlots(population: 70, zoneCount: 10, isCapital: false), "middle city slots");
                ExpectEqual(3, CourtRules.CityOfficeSlots(population: 130, zoneCount: 20, isCapital: true), "large capital slots");

                Expect(CourtRules.ShouldRefreshCourt(currentYear: 40, lastRefreshYear: 35, intervalYears: 5), "refresh interval reached");
                Expect(!CourtRules.ShouldRefreshCourt(currentYear: 40, lastRefreshYear: 38, intervalYears: 5), "refresh interval not reached");

                ExpectEqual(CourtSchoolId.Legalist, CourtInfluenceRules.DominantSchool("ru=12;fa=20;dao=3;mo=4", CourtSchoolId.Ru), "dominant legalist");
                ExpectEqual(0.625f, CourtInfluenceRules.Concentration(25f, 40f), "concentration");
                Expect(CourtInfluenceRules.ShouldTriggerStrongEvent(yearsDominant: 8, dominantShare: 0.61f, crisis: false, weakKing: false), "long dominance strong event");
                Expect(CourtInfluenceRules.ShouldTriggerStrongEvent(yearsDominant: 2, dominantShare: 0.48f, crisis: true, weakKing: true), "crisis strong event");
                Expect(!CourtInfluenceRules.ShouldTriggerStrongEvent(yearsDominant: 2, dominantShare: 0.48f, crisis: false, weakKing: false), "no strong event");

                Expect(CourtTraitRules.ShouldHoldSchoolTrait(isOfficer: true, alive: true, defected: false), "active officer holds trait");
                Expect(!CourtTraitRules.ShouldHoldSchoolTrait(isOfficer: false, alive: true, defected: false), "non officer loses trait");
                Expect(!CourtTraitRules.ShouldHoldSchoolTrait(isOfficer: true, alive: false, defected: false), "dead officer loses trait");

                Expect(CourtAIRules.ScoreResearch(CourtSchoolId.Legalist, "aw_policy_early_law", atWar: false, mandateExists: false) > 0, "legalist boosts law");
                Expect(CourtAIRules.ScoreResearch(CourtSchoolId.Mohist, "aw_tech_city_defense", atWar: false, mandateExists: false) > 0, "mohist boosts defense");
                Expect(CourtAIRules.ScoreResearch(CourtSchoolId.Military, "aw_tech_chariot_training", atWar: true, mandateExists: false) > CourtAIRules.ScoreResearch(CourtSchoolId.Military, "aw_tech_chariot_training", atWar: false, mandateExists: false), "military values wartime training");
                Expect(CourtAIRules.ScoreDecision(CourtSchoolId.Diplomat, "aw_decision_declare_war", cities: 4, atWar: false, unstable: false) < CourtAIRules.ScoreDecision(CourtSchoolId.Military, "aw_decision_declare_war", cities: 4, atWar: false, unstable: false), "military favors war more than diplomat");

                string encoded = CourtStateCodec.EncodeFactionCache(new[] { CourtSchoolId.Ru, CourtSchoolId.Legalist }, new[] { 4.5f, 8f });
                ExpectEqual("ru=4.5;fa=8", encoded, "encoded faction cache");
                var decoded = CourtStateCodec.DecodeFactionCache(encoded);
                ExpectEqual(2, decoded.Count, "decoded faction count");
                ExpectEqual(8f, decoded[CourtSchoolId.Legalist], "decoded legalist value");
                ExpectEqual("", CourtStateCodec.EncodeFactionCache(new string[0], new float[0]), "empty faction cache");

                ExpectEqual(CourtTraitId.Ru, CourtTraitRules.TraitForSchool(CourtSchoolId.Ru), "ru trait id");
                ExpectEqual(CourtTraitId.Legalist, CourtTraitRules.TraitForSchool(CourtSchoolId.Legalist), "legalist trait id");
                ExpectEqual("", CourtTraitRules.TraitForSchool("unknown"), "unknown trait id");

                Console.WriteLine("Court system rule tests passed.");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
                return 1;
            }
        }

        private static void Expect(bool value, string label)
        {
            if (!value) throw new Exception("Expected true: " + label);
        }

        private static void ExpectEqual<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
                throw new Exception($"Expected {label} {expected}, got {actual}.");
        }
    }
}
