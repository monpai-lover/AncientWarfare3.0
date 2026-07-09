using System;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

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
                Expect(CourtRules.ShouldUseSingleYearRoster(CourtRules.CentralOfficeCount), "multi-office court refresh uses one yearly roster snapshot");
                Expect(!CourtRules.ShouldUseSingleYearRoster(1), "single-office court refresh can skip roster snapshot overhead");

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

                ExpectEqual(CaptiveTreatmentAction.SettleAsNobleDependent,
                    CaptiveTreatmentRules.Decide(CourtSchoolId.Ru, wasKing: true, wasLeader: false,
                        captorAtWar: true, hostilePowerRatio: 1.5f),
                    "ru court settles captured rulers");
                ExpectEqual(CaptiveTreatmentAction.ExecuteCaptive,
                    CaptiveTreatmentRules.Decide(CourtSchoolId.Legalist, wasKing: true, wasLeader: false,
                        captorAtWar: true, hostilePowerRatio: 1.2f),
                    "legalist court executes hostile captured kings");
                ExpectEqual(CaptiveTreatmentAction.ExecuteCaptive,
                    CaptiveTreatmentRules.Decide(CourtSchoolId.Military, wasKing: false, wasLeader: true,
                        captorAtWar: true, hostilePowerRatio: 1.0f),
                    "military court executes wartime captured leaders");
                ExpectEqual(CaptiveTreatmentAction.SettleAsNobleDependent,
                    CaptiveTreatmentRules.Decide(CourtSchoolId.Mohist, wasKing: true, wasLeader: true,
                        captorAtWar: true, hostilePowerRatio: 3.0f),
                    "mohist court avoids executing important captives");
                ExpectEqual(CaptiveTreatmentAction.KeepAsSlave,
                    CaptiveTreatmentRules.Decide(CourtSchoolId.Legalist, wasKing: false, wasLeader: false,
                        captorAtWar: true, hostilePowerRatio: 1.0f),
                    "ordinary captives stay in normal slavery flow");

                Expect(CourtRules.CanHoldOffice(alive: true, sameKingdom: true, slave: false, madness: false), "valid office holder");
                Expect(!CourtRules.CanHoldOffice(alive: true, sameKingdom: false, slave: false, madness: false), "foreign holder rejected");
                Expect(!CourtRules.CanHoldOffice(alive: true, sameKingdom: true, slave: true, madness: false), "slave holder rejected");
                Expect(!CourtRules.CanHoldOffice(alive: true, sameKingdom: true, slave: false, madness: true), "madness holder rejected");

                int baseWar = CourtAIRules.ScoreDecision(CourtSchoolId.None, "aw_decision_declare_war", cities: 5, atWar: false, unstable: false);
                int militaryWar = CourtAIRules.ScoreDecision(CourtSchoolId.Military, "aw_decision_declare_war", cities: 5, atWar: false, unstable: false);
                int daoWar = CourtAIRules.ScoreDecision(CourtSchoolId.Dao, "aw_decision_declare_war", cities: 5, atWar: false, unstable: false);
                Expect(militaryWar > baseWar, "military court raises war decision");
                Expect(daoWar < baseWar, "dao court lowers war decision");

                ExpectEqual(54f, CourtUiRules.KingdomMiddleHeight, "court middle row height");
                ExpectEqual(114f, CourtUiRules.CourtButtonWidth, "court wide button width");
                ExpectEqual(16f, CourtUiRules.CourtButtonHeight, "court wide button height");

                Expect(CourtBureauRules.ShouldRefreshCityBureau(currentYear: 60, lastRefreshYear: 55,
                        hasOfficialCourt: true),
                    "official court refreshes city bureau on interval");
                Expect(!CourtBureauRules.ShouldRefreshCityBureau(currentYear: 60, lastRefreshYear: 58,
                        hasOfficialCourt: true),
                    "city bureau refresh respects interval");
                Expect(!CourtBureauRules.ShouldRefreshCityBureau(currentYear: 60, lastRefreshYear: 40,
                        hasOfficialCourt: false),
                    "primitive court skips city bureau refresh");
                ExpectEqual(70f, CourtBureauRules.BureauEfficiency(officeSlots: 3, filledSlots: 2),
                    "partial city bureau efficiency");
                ExpectEqual(100f, CourtBureauRules.BureauEfficiency(officeSlots: 2, filledSlots: 3),
                    "overfilled city bureau clamps efficiency");
                ExpectEqual(CourtOfficeId.Governor,
                    CourtBureauRules.CityOfficeForSlot(0, pIsCapital: false),
                    "first city office is governor");
                ExpectEqual(CourtOfficeId.GranaryOfficer,
                    CourtBureauRules.CityOfficeForSlot(1, pIsCapital: false),
                    "second city office is granary officer");
                ExpectEqual(CourtOfficeId.Constable,
                    CourtBureauRules.CityOfficeForSlot(2, pIsCapital: false),
                    "third city office is constable");
                ExpectEqual(CourtSchoolId.Agrarian,
                    CourtBureauRules.PreferredSchoolForCityOffice(CourtOfficeId.GranaryOfficer),
                    "granary officer prefers agrarian school");
                ExpectEqual(2, CourtBureauRules.FilledSlots(officeSlots: 3, courtEfficiency: 70f),
                    "filled bureau slots scale with court efficiency");
                ExpectEqual(3, CourtBureauRules.FilledSlots(officeSlots: 3, courtEfficiency: 100f),
                    "efficient court staffs every bureau slot");
                ExpectEqual(0, CourtBureauRules.FilledSlots(officeSlots: 3, courtEfficiency: 0f),
                    "collapsed court staffs no bureau slot");

                Expect(CourtOfficerRecordRules.ShouldInsertNewActiveRecord(
                        hasActiveRecord: false, sameKingdom: false, sameOffice: false, sameLayer: false),
                    "missing officer record creates active row");
                Expect(!CourtOfficerRecordRules.ShouldInsertNewActiveRecord(
                        hasActiveRecord: true, sameKingdom: true, sameOffice: true, sameLayer: true),
                    "same active officer row is updated not duplicated");
                Expect(CourtOfficerRecordRules.ShouldCloseActiveRecord(hasActiveRecord: true),
                    "active officer row closes on dismissal");
                ExpectEqual(1, CourtOfficerRecordRules.ActiveFlag(true), "active flag");
                ExpectEqual(0, CourtOfficerRecordRules.ActiveFlag(false), "inactive flag");

                Expect(CourtEventRules.ShouldFireStrongEvent(currentYear: 80, lastStrongEventYear: 60,
                        yearsDominant: 8, dominantShare: 0.61f, crisis: false, weakKing: false),
                    "strong court event fires after cooldown");
                Expect(!CourtEventRules.ShouldFireStrongEvent(currentYear: 80, lastStrongEventYear: 74,
                        yearsDominant: 8, dominantShare: 0.61f, crisis: false, weakKing: false),
                    "strong court event blocked by cooldown");
                ExpectEqual(75, CourtEventRules.NextDominantSinceYear(currentYear: 80,
                        previousDominant: CourtSchoolId.Ru, dominant: CourtSchoolId.Ru,
                        previousSinceYear: 75),
                    "same dominant school keeps since year");
                ExpectEqual(80, CourtEventRules.NextDominantSinceYear(currentYear: 80,
                        previousDominant: CourtSchoolId.Ru, dominant: CourtSchoolId.Legalist,
                        previousSinceYear: 75),
                    "new dominant school resets since year");

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
