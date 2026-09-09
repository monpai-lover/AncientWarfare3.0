using System;
using AncientWarfare3.core.court;

internal static class ExamCycleRulesTests
{
    public static void Run()
    {
        Equal(2031, ExamCycleRules.FirstOpeningYear(2030),
            "the first joint sitting opens in the next year");
        Equal(true, ExamCycleRules.IsCycleYear(2034, 2028),
            "a three-year cycle year is recognized");
        Equal(false, ExamCycleRules.IsCycleYear(2033, 2028),
            "an off-cycle year is rejected");
        Equal("imperial_exam", ExamCycleRules.ResolveMode(
                hasMandate: true, hasEmpireTitle: false),
            "mandate realms use imperial mode");
        Equal("tribute_exam", ExamCycleRules.ResolveMode(
                hasMandate: false, hasEmpireTitle: false),
            "ordinary realms use tribute mode");
        Equal("42:2034", ExamCycleRules.IdempotencyKey(42L, 2034),
            "kingdom and cycle year form one idempotency key");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(
                $"{message}: expected {expected}, got {actual}");
    }
}
