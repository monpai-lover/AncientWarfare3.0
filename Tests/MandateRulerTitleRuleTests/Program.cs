using System;
using AncientWarfare3.core.lineage;

namespace MandateRulerTitleRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
            ExpectTemple("founder", "\u592a\u7956", founder: true, lowOrigin: false, refounder: false,
                conquestScore: 80, reformScore: 20, reignIndex: 1);
            ExpectTemple("low_origin", "\u9ad8\u7956", founder: true, lowOrigin: true, refounder: false,
                conquestScore: 55, reformScore: 20, reignIndex: 1);
            ExpectTemple("refounder", "\u9ad8\u7956", founder: true, lowOrigin: false, refounder: true,
                conquestScore: 55, reformScore: 20, reignIndex: 1);
            ExpectTemple("second_reformer", "\u592a\u5b97", founder: false, lowOrigin: false, refounder: false,
                conquestScore: 20, reformScore: 80, reignIndex: 2);
            ExpectTemple("later_reformer_not_spammed_as_shizong", "\u5ba3\u5b97", founder: false, lowOrigin: false,
                refounder: false, conquestScore: 20, reformScore: 80, reignIndex: 4);
            ExpectTemple("later_great_reformer_can_be_shizong", "\u4e16\u5b97", founder: false, lowOrigin: false,
                refounder: false, conquestScore: 35, reformScore: 95, reignIndex: 5);
            ExpectTemple("later_conqueror_is_wuzong_not_liezu", "\u6b66\u5b97", founder: false,
                lowOrigin: false, refounder: false, conquestScore: 85, reformScore: 20, reignIndex: 6);
            ExpectMandateReignIndex(1, pKingdomReignIndex: 4, pPriorMandateTitles: 0);
            ExpectMandateReignIndex(2, pKingdomReignIndex: 5, pPriorMandateTitles: 1);
            ExpectMandateFounder(true, pKingActorId: 3376, pPeriodFounderActorId: 3376, pMandateReignIndex: 1);
            ExpectMandateFounder(false, pKingActorId: 7536, pPeriodFounderActorId: 3376, pMandateReignIndex: 2);
            ExpectTemple("mandate_founder_mid_kingdom_reign", "\u9ad8\u7956",
                MandateRulerTitleRules.IsMandateFounderReign(3376, 3376,
                    MandateRulerTitleRules.ResolveMandateReignIndex(4, 0)),
                lowOrigin: false, refounder: false, conquestScore: 36, reformScore: 20, reignIndex: 1);
            ExpectTemple("mandate_second_reign_uses_mandate_index", "\u592a\u5b97",
                MandateRulerTitleRules.IsMandateFounderReign(7536, 3376,
                    MandateRulerTitleRules.ResolveMandateReignIndex(5, 1)),
                lowOrigin: false, refounder: false, conquestScore: 20, reformScore: 80, reignIndex: 2);
            ExpectUniqueTemple("\u4e16\u5b97", "\u4e16\u5b97", Array.Empty<string>(), 5);
            ExpectUniqueTemple("\u7a46\u5b97", "\u4e16\u5b97", new[] { "\u4e16\u5b97" }, 5);
            ExpectTempleValidity("founder_taizu", true, "\u592a\u7956", true);
            ExpectTempleValidity("founder_gaozu", true, "\u9ad8\u7956", true);
            ExpectTempleValidity("founder_shizu_invalid", true, "\u4e16\u7956", false);
            ExpectTempleValidity("later_shizong", false, "\u4e16\u5b97", true);
            ExpectTempleValidity("later_taizong", false, "\u592a\u5b97", true);
            ExpectTempleValidity("later_liezu_invalid", false, "\u70c8\u7956", false);
            ExpectTempleValidity("later_empty_invalid", false, "", false);
            ExpectUniqueDoublePosthumous("\u7aef\u9756", "\u61ff\u70c8", new[] { "\u61ff\u70c8" },
                pNegative: false, pReignIndex: 4);
            ExpectUniqueDoublePosthumous("\u8c2c\u60d1", "\u623e\u8650", new[] { "\u623e\u8650" },
                pNegative: true, pReignIndex: 4);
            ExpectFullTitle("\u9ad8\u7956 \u6587\u6b66\u7687\u5e1d", "\u9ad8\u7956", "\u6587\u6b66");
            ExpectFullTitle("\u6587\u6b66\u7687\u5e1d", "", "\u6587\u6b66");

            string pair = MandateRulerTitleRules.SelectDoublePosthumousTitle(civil: 70, war: 65,
                order: 40, disaster: 0);
            if (pair.Length != 2) throw new Exception("Expected two-character mandate posthumous title.");

            string bad = MandateRulerTitleRules.SelectDoublePosthumousTitle(civil: 0, war: 0,
                order: 0, disaster: 80);
            if (bad.Length != 2 || bad == pair) throw new Exception("Expected distinct negative title pair.");

            ExpectCoreStatus("non_core", "none", isLegalCore: false, hasMandate: true,
                hasOwner: true, ownerIsMandate: true, ownerRootSuzerainIsMandate: false);
            ExpectCoreStatus("controlled", "controlled", isLegalCore: true, hasMandate: true,
                hasOwner: true, ownerIsMandate: true, ownerRootSuzerainIsMandate: false);
            ExpectCoreStatus("vassal", "vassal", isLegalCore: true, hasMandate: true,
                hasOwner: true, ownerIsMandate: false, ownerRootSuzerainIsMandate: true);
            ExpectCoreStatus("lost", "lost", isLegalCore: true, hasMandate: true,
                hasOwner: true, ownerIsMandate: false, ownerRootSuzerainIsMandate: false);
            ExpectCoreStatus("orphan", "orphan", isLegalCore: true, hasMandate: false,
                hasOwner: true, ownerIsMandate: false, ownerRootSuzerainIsMandate: false);

            ExpectCoreSync(true, pIsActiveMandateKingdom: true, pCoreAlreadyLegal: false);
            ExpectCoreSync(false, pIsActiveMandateKingdom: false, pCoreAlreadyLegal: false);
            ExpectCoreSync(false, pIsActiveMandateKingdom: true, pCoreAlreadyLegal: true);

            ExpectNameplateSuffix("mandate_emperor", "\u671d", title: 4, isMandate: true);
            ExpectNameplateSuffix("normal_emperor", "\u5e1d\u56fd", title: 4, isMandate: false);
            ExpectNameplateSuffix("normal_king", "\u738b\u56fd", title: 3, isMandate: true);

            ExpectMandateHistoryAssignmentRules();
            ExpectMandateDeclarationRules();

            Console.WriteLine("Mandate ruler title rule tests passed.");
            return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
                return 1;
            }
        }

        private static void ExpectTemple(string label, string expected, bool founder, bool lowOrigin,
            bool refounder, int conquestScore, int reformScore, int reignIndex)
        {
            string actual = MandateRulerTitleRules.SelectTempleName(founder, lowOrigin, refounder,
                conquestScore, reformScore, reignIndex);
            if (actual != expected)
                throw new Exception($"Expected {label} temple {expected}, got {actual}.");
        }

        private static void ExpectUniqueTemple(string expected, string candidate, string[] used, int reignIndex)
        {
            string actual = MandateRulerTitleRules.EnsureUniqueTempleName(candidate, used, reignIndex);
            if (actual != expected)
                throw new Exception($"Expected unique temple {expected}, got {actual}.");
        }

        private static void ExpectTempleValidity(string label, bool founder, string temple, bool expected)
        {
            bool actual = MandateRulerTitleRules.IsTempleNameValidForMandatePosition(temple, founder);
            if (actual != expected)
                throw new Exception($"Expected temple validity {label} {expected}, got {actual}.");
        }

        private static void ExpectMandateReignIndex(int expected, int pKingdomReignIndex, int pPriorMandateTitles)
        {
            int actual = MandateRulerTitleRules.ResolveMandateReignIndex(pKingdomReignIndex, pPriorMandateTitles);
            if (actual != expected)
                throw new Exception($"Expected mandate reign index {expected}, got {actual}.");
        }

        private static void ExpectMandateFounder(bool expected, long pKingActorId, long pPeriodFounderActorId,
            int pMandateReignIndex)
        {
            bool actual = MandateRulerTitleRules.IsMandateFounderReign(pKingActorId, pPeriodFounderActorId,
                pMandateReignIndex);
            if (actual != expected)
                throw new Exception($"Expected mandate founder {expected}, got {actual}.");
        }

        private static void ExpectFullTitle(string expected, string temple, string pair)
        {
            string actual = MandateRulerTitleRules.BuildFullTitle(temple, pair);
            if (actual != expected)
                throw new Exception($"Expected full title {expected}, got {actual}.");
        }

        private static void ExpectUniqueDoublePosthumous(string expected, string candidate, string[] used,
            bool pNegative, int pReignIndex)
        {
            string actual = MandateRulerTitleRules.EnsureUniqueDoublePosthumousTitle(candidate, used,
                pNegative, pReignIndex);
            if (actual != expected)
                throw new Exception($"Expected unique double posthumous {expected}, got {actual}.");
        }

        private static void ExpectCoreStatus(string label, string expected, bool isLegalCore, bool hasMandate,
            bool hasOwner, bool ownerIsMandate, bool ownerRootSuzerainIsMandate)
        {
            string actual = MandateCoreMapRules.SelectCoreStatus(isLegalCore, hasMandate, hasOwner,
                ownerIsMandate, ownerRootSuzerainIsMandate);
            if (actual != expected)
                throw new Exception($"Expected {label} core status {expected}, got {actual}.");
        }

        private static void ExpectCoreSync(bool expected, bool pIsActiveMandateKingdom, bool pCoreAlreadyLegal)
        {
            bool actual = MandateCoreMapRules.ShouldAddNewKingdomCoreToMandateLegalCore(
                pIsActiveMandateKingdom, pCoreAlreadyLegal);
            if (actual != expected)
                throw new Exception($"Expected legal core sync {expected}, got {actual}.");
        }

        private static void ExpectNameplateSuffix(string label, string expected, int title, bool isMandate)
        {
            string actual = KingdomTitleDisplayRules.GetNameplateTitleSuffix(title, isMandate);
            if (actual != expected)
                throw new Exception($"Expected {label} nameplate suffix {expected}, got {actual}.");
        }

        private static void ExpectMandateHistoryAssignmentRules()
        {
            if (!MandateHistoryEventAssignmentRules.ShouldPreferActorReign("mandate_ruler_title", 12))
                throw new Exception("Mandate ruler title events should be assigned by target actor first.");

            if (MandateHistoryEventAssignmentRules.ShouldPreferActorReign("mandate_year_name", 12))
                throw new Exception("General mandate events should still be assigned by event time.");

            if (MandateHistoryEventAssignmentRules.ShouldPreferActorReign("mandate_ruler_title", -1))
                throw new Exception("Actor assignment should require a valid actor id.");
        }

        private static void ExpectMandateDeclarationRules()
        {
            if (!MandateDeclarationRules.HasEnoughRealmToDeclare(
                    pCityCount: 4,
                    pTitle: 0,
                    pHistoricalFigureKing: true,
                    pMinimumCities: 4,
                    pKingTitleValue: 3))
                throw new Exception("Historical figure kings should bypass title but still need enough realm.");

            if (MandateDeclarationRules.HasEnoughRealmToDeclare(
                    pCityCount: 1,
                    pTitle: 0,
                    pHistoricalFigureKing: true,
                    pMinimumCities: 4,
                    pKingTitleValue: 3))
                throw new Exception("Historical figure kings must not declare mandate while tiny.");

            if (!MandateDeclarationRules.HasEnoughRealmToDeclare(
                    pCityCount: 2,
                    pTitle: 3,
                    pHistoricalFigureKing: false,
                    pMinimumCities: 4,
                    pKingTitleValue: 3))
                throw new Exception("Normal kings with king title should satisfy the title gate.");

            if (!MandateDeclarationRules.NeedsLegalCoreControl(
                    pPreviousCoreCount: 5,
                    pPreviousMandateActive: false))
                throw new Exception("Former mandate legal core should require restoration control.");

            if (!MandateDeclarationRules.HasEnoughLegalCoreControl(0.65f, 0.65f) ||
                MandateDeclarationRules.HasEnoughLegalCoreControl(0.64f, 0.65f))
                throw new Exception("AW2 restoration threshold should be 65% legal core control.");
        }
    }
}
