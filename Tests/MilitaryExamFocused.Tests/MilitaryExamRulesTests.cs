using System;
using AncientWarfare3.core.court;

internal static class MilitaryExamRulesTests
{
    public static void Run()
    {
        Equal(false, MilitaryExamRules.IsEligibleCandidate(
                alive: true, adult: true, age: 75f, isKing: false,
                isHeir: false, isPrince: false, isSlave: false,
                retired: false),
            "age 75 is not eligible for the military exam");
        Equal(true, MilitaryExamRules.IsEligibleCandidate(
                alive: true, adult: true, age: 74.9f, isKing: false,
                isHeir: false, isPrince: false, isSlave: false,
                retired: false),
            "age below 75 remains eligible");
        Equal(false, MilitaryExamRules.IsEligibleCandidate(
                alive: true, adult: true, age: 30f, isKing: false,
                isHeir: false, isPrince: false, isSlave: false,
                retired: true),
            "retired actors cannot enter the military exam");

        Equal(100, MilitaryExamRules.Score(100, 100, 100, 100, 100, 100),
            "perfect military profile scores 100");
        Equal(0, MilitaryExamRules.Score(-10, -1, -3, -4, -5, -6),
            "negative ability values clamp to zero");
        Equal(35, MilitaryExamRules.Score(100, 0, 0, 0, 0, 0),
            "warfare contributes 35 percent");
        Equal(10, MilitaryExamRules.Score(0, 0, 0, 0, 0, 100),
            "military merit contributes 10 percent");

        Equal("wuxiucai", MilitaryExamRules.QualificationForStage(
                MilitaryExamStage.Local, passed: true,
                imperialMode: true),
            "local pass grants wuxiucai");
        Equal("wujinshi", MilitaryExamRules.QualificationForStage(
                MilitaryExamStage.Palace, passed: true,
                imperialMode: true),
            "palace pass grants wujinshi in imperial mode");
        Equal("wujuren", MilitaryExamRules.QualificationForStage(
                MilitaryExamStage.Palace, passed: true,
                imperialMode: false),
            "tribute mode has no palace wujinshi qualification");

        Equal(3, MilitaryExamRules.CandidateReserveTarget(
                generalVacancies: 2, localVacancies: 3,
                hasMilitaryOrganization: true, minimumReserve: 3),
            "reserve target is half of combined vacancies rounded up");
        Equal(0, MilitaryExamRules.CandidateReserveTarget(
                generalVacancies: 2, localVacancies: 3,
                hasMilitaryOrganization: false, minimumReserve: 3),
            "no military organization creates no reserve candidates");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(
                $"{message}: expected {expected}, got {actual}");
    }
}
