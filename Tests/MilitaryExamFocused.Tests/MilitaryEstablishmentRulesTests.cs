using System;
using AncientWarfare3.core.court;

internal static class MilitaryEstablishmentRulesTests
{
    public static void Run()
    {
        Equal(7, MilitaryEstablishmentRules.GeneralTarget(
                strategicArmies: 2, militaryPrefectures: 1,
                capitalDefence: true, independentFronts: 3),
            "general target combines strategic commands, prefectures, capital and fronts");
        Equal(6, MilitaryEstablishmentRules.LocalTarget(
                garrisonCities: 3, uncommandedLocalArmies: 2,
                deputySlots: 1, frontierBonusCities: 0),
            "local target combines garrisons, uncommanded armies and deputies");
        Equal(3, MilitaryEstablishmentRules.Vacancies(
                target: 5, qualifiedActive: 1, lockedAppointments: 1),
            "vacancies subtract qualified and locked officers");
        Equal(0, MilitaryEstablishmentRules.Vacancies(
                target: 1, qualifiedActive: 3, lockedAppointments: 2),
            "vacancies never become negative");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(
                $"{message}: expected {expected}, got {actual}");
    }
}
