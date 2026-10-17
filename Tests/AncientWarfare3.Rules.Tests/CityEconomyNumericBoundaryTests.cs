using System;
using AncientWarfare3.core.policy;

internal static class CityEconomyNumericBoundaryTests
{
    internal static void Run()
    {
        CityEconomyContribution baseline = Calculate(0f, 1f, 0f);
        Equal(baseline.TaxValue * 0.45f,
            Calculate(float.PositiveInfinity, 1f, 0f).TaxValue,
            "infinite distance uses the minimum distance factor");
        Equal(baseline.TaxValue,
            Calculate(float.NegativeInfinity, 1f, 0f).TaxValue,
            "negative distance cannot exceed the maximum distance factor");
        Equal(0f, Calculate(0f, float.NegativeInfinity, 0f).TaxValue,
            "negative remittance is clamped to zero");
        Equal(baseline.TaxValue,
            Calculate(0f, float.PositiveInfinity, 0f).TaxValue,
            "remittance is capped at one");
        Equal(0f, Calculate(0f, 1f, float.PositiveInfinity).UnrestRisk,
            "unrest has a lower bound");
        Equal(100f, Calculate(0f, 1f, float.NegativeInfinity).UnrestRisk,
            "unrest has an upper bound");
        True(float.IsNaN(Calculate(float.NaN, 1f, 0f).TaxValue),
            "invalid distance remains detectable");
        True(float.IsNaN(Calculate(0f, float.NaN, 0f).TaxValue),
            "invalid remittance remains detectable");
        True(float.IsNaN(Calculate(0f, 1f, float.NaN).UnrestRisk),
            "invalid unrest remains detectable");
        var emptyCity = CityEconomyRules.CalculateContribution(
            CityEconomyRole.CapitalAdmin, int.MinValue, -1, 0, 0f,
            int.MaxValue, false);
        Equal(0f, emptyCity.Manpower, "negative population is treated as zero");
        True(float.IsFinite(emptyCity.TaxValue),
            "zero population and technology cannot divide by zero");
    }

    private static CityEconomyContribution Calculate(float distance,
        float remittance, float unrestReduction)
    {
        return CityEconomyRules.CalculateContribution(
            CityEconomyRole.AgrarianGranary, 100, 2, 10, distance,
            0, false, feudatoryRemittanceMultiplier: remittance,
            unrestReduction: unrestReduction);
    }

    private static void Equal(float expected, float actual, string message)
    {
        True(float.IsFinite(actual) && Math.Abs(expected - actual) <= 0.0001f,
            message + $": expected {expected}, actual {actual}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
