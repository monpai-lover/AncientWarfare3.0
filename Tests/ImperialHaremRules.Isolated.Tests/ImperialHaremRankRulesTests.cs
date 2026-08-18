using AncientWarfare3.core.lineage;

internal static class ImperialHaremRankRulesTests
{
    internal static void Run()
    {
        Equal("empress", RulerHouseholdRankRules.SeatCode(0),
            "first imperial seat");
        Equal("consort_kang", RulerHouseholdRankRules.SeatCode(9),
            "last imperial seat");
        Equal(9,
            RulerHouseholdRules.ConsortCapacity(
                RulerHouseholdRealmTier.Empire),
            "imperial active consort capacity");
        True(RulerHouseholdRules.IsCandidateClassEligible(
                RulerHouseholdCandidateClass.Commoner,
                RulerHouseholdKind.Consort),
            "qualified commoner consort");
        False(RulerHouseholdRules.IsCandidateClassEligible(
                RulerHouseholdCandidateClass.Commoner,
                RulerHouseholdKind.PrincipalWife),
            "commoner is not principal wife by default");
        True(RulerHouseholdRankRules.KeepsSeatAfterAge(36),
            "age does not revoke rank");
    }

    private static void True(bool pValue, string pMessage)
    {
        if (!pValue) throw new InvalidOperationException(pMessage);
    }

    private static void False(bool pValue, string pMessage)
    {
        True(!pValue, pMessage);
    }

    private static void Equal<T>(T pExpected, T pActual, string pMessage)
    {
        if (!EqualityComparer<T>.Default.Equals(pExpected, pActual))
        {
            throw new InvalidOperationException(
                $"{pMessage}: expected {pExpected}, got {pActual}");
        }
    }
}
