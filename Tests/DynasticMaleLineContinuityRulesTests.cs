using System;
using AncientWarfare3.core.lineage;

public static class DynasticMaleLineContinuityRulesTests
{
    public static int Main()
    {
        try
        {
            Equal(true, DynasticMaleLineContinuityRules.IsEligibleRole(
                    isKing: false, isRegisteredHeir: false,
                    isFeudatoryPrince: false,
                    isFeudatorySuccessor: false,
                    holdsActiveMaleTitle: true,
                    isExpectedMaleTitleSuccessor: false),
                "active male title holder is protected");
            Equal(true, DynasticMaleLineContinuityRules.IsEligibleRole(
                    false, true, false, false, false, false),
                "registered male heir is protected");
            Equal(true, DynasticMaleLineContinuityRules.IsEligibleRole(
                    false, false, false, false, false, true),
                "expected personal-title successor is protected");
            Equal(false, DynasticMaleLineContinuityRules.IsEligibleRole(
                    false, false, false, false, false, false),
                "ordinary noble identity alone is not protected");

            Equal(true, DynasticMaleLineContinuityRules
                    .ShouldBypassPersonalOffspringLimit(
                        eligibleRole: true, alive: true, adult: true,
                        breedingAge: true, canProduceBabies: true,
                        hasLivingSon: false),
                "eligible no-son line bypasses personal cap");
            Equal(false, DynasticMaleLineContinuityRules
                    .ShouldBypassPersonalOffspringLimit(
                        true, true, true, true, true,
                        hasLivingSon: true),
                "living son immediately restores vanilla cap");
            Equal(false, DynasticMaleLineContinuityRules
                    .ShouldBypassPersonalOffspringLimit(
                        eligibleRole: false, true, true, true, true,
                        false),
                "ordinary actor cannot bypass personal cap");
            Equal(false, DynasticMaleLineContinuityRules
                    .ShouldBypassPersonalOffspringLimit(
                        true, true, true, true,
                        canProduceBabies: false, false),
                "infertility is never bypassed");

            Equal(true, DynasticMaleLineContinuityRules
                    .HasPersonalOffspringRoom(
                        vanillaRoom: false, continuationBypass: true),
                "continuation bypass opens only the personal cap");
            Equal(false, DynasticMaleLineContinuityRules
                    .HasPersonalOffspringRoom(false, false),
                "closed vanilla cap remains closed without continuation");

            Console.WriteLine(
                "Dynastic male-line continuity rule tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                                                expected + ", got " + actual);
    }
}
