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

        True(LocalOfficialCandidateRules.CanEnter(
                alive: true, adult: true, slave: false,
                alreadyOfficial: false, king: false, heir: false,
                examinationEnabled: true, qualification: "juren",
                participatedAndFailedHigherStage: false),
            "local-stage pass enters the local pool");
        True(LocalOfficialCandidateRules.CanEnter(
                alive: true, adult: true, slave: false,
                alreadyOfficial: false, king: false, heir: false,
                examinationEnabled: true, qualification: "none",
                participatedAndFailedHigherStage: true),
            "higher-stage non-finalist remains locally employable");
        False(LocalOfficialCandidateRules.CanEnter(
                alive: true, adult: true, slave: false,
                alreadyOfficial: false, king: false, heir: true,
                examinationEnabled: true, qualification: "jinshi",
                participatedAndFailedHigherStage: false),
            "an heir cannot enter a local office");
        Equal(25, LocalOfficialCandidateRules.HometownBonus,
            "hometown bonus is explicit");
        True(LocalOfficialCandidateRules.Score(60, 50,
                 sameNativeCity: true) >
             LocalOfficialCandidateRules.Score(90, 50,
                 sameNativeCity: false),
            "qualified same-native-city recommendation is material");
        False(LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                "juren", participatedAndFailedHigherStage: false,
                allowLocalLowerQualification: false),
            "central appointments do not accept local-stage credentials");
        True(LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                "juren", participatedAndFailedHigherStage: false,
                allowLocalLowerQualification: true),
            "the explicit local path accepts a local-stage credential");
        True(LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                "none", participatedAndFailedHigherStage: true,
                allowLocalLowerQualification: true),
            "the explicit local path accepts a higher-stage non-finalist");
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
            throw new InvalidOperationException(
                $"{pMessage}: expected {pExpected}, got {pActual}");
    }
}
