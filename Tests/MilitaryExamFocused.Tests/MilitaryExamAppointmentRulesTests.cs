using System;
using AncientWarfare3.core.court;

internal static class MilitaryExamAppointmentRulesTests
{
    public static void Run()
    {
        AssertEqual("general", MilitaryExamAppointmentRules.ResolveTier(true, true, true, true), "palace pass is general");
        AssertEqual("local", MilitaryExamAppointmentRules.ResolveTier(true, false, true, true), "palace failure remains local");
        AssertEqual("local", MilitaryExamAppointmentRules.ResolveTier(true, false, false, false), "tribute highest stage is local");
        AssertEqual("none", MilitaryExamAppointmentRules.ResolveTier(false, true, true, true), "palace requires local pass");
        Assert(MilitaryExamAppointmentRules.CanAppoint(true, 74.9f), "age below 75 can be appointed");
        Assert(!MilitaryExamAppointmentRules.CanAppoint(true, 75f), "age 75 cannot be appointed");
        Assert(!MilitaryExamAppointmentRules.CanAppoint(false, 40f), "dead candidate cannot be appointed");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }
}
