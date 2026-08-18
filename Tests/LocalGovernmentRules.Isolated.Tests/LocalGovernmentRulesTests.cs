using AncientWarfare3.core.court;

internal static class LocalGovernmentRulesTests
{
    internal static void Run()
    {
        Equal(CourtOfficeId.Governor,
            LocalCourtOfficeRules.OfficeForSlot(0, CourtProfileId.Xia),
            "Xia city leader is the root office");
        Equal(CourtOfficeId.WestMayor,
            LocalCourtOfficeRules.OfficeForSlot(0, CourtProfileId.Western),
            "western city leader uses the mayor office");
        Equal(CourtOfficeId.GranaryOfficer,
            LocalCourtOfficeRules.OfficeForSlot(1, CourtProfileId.Xia),
            "second city slot is granary administration");
        Equal(CourtOfficeId.Constable,
            LocalCourtOfficeRules.OfficeForSlot(2, CourtProfileId.Xia),
            "third city slot is local constable");

        for (long actorId = 1; actorId <= 32; actorId++)
        {
            int term = LocalOfficialTermRules.TermLength(
                ability: 20, merit: 80, age: 35,
                actorId, appointmentYear: 100);
            True(LocalOfficialTermRules.IsValidTermLength(term),
                "local terms are always ten to fifteen years");
        }

        True(OfficialCirculationRules.IsRotatingCityOffice(
                CourtOfficeId.GranaryOfficer,
                xiaCirculationUnlocked: false),
            "all local offices circulate regardless of central law");
        True(OfficialCirculationRules.IsRotatingCityOffice(
                CourtOfficeId.Constable,
                xiaCirculationUnlocked: false),
            "local constables also circulate");
    }

    private static void True(bool pValue, string pMessage)
    {
        if (!pValue) throw new InvalidOperationException(pMessage);
    }

    private static void Equal<T>(T pExpected, T pActual, string pMessage)
    {
        if (!EqualityComparer<T>.Default.Equals(pExpected, pActual))
            throw new InvalidOperationException(
                $"{pMessage}: expected {pExpected}, got {pActual}");
    }
}
