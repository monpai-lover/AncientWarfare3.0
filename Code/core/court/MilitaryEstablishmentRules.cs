using System;

namespace AncientWarfare3.core.court
{
    public static class MilitaryEstablishmentRules
    {
        public static int GeneralTarget(int strategicArmies,
            int militaryPrefectures, bool capitalDefence, int independentFronts)
        {
            int result = NonNegative(strategicArmies) +
                         NonNegative(militaryPrefectures) +
                         NonNegative(independentFronts);
            return result + (capitalDefence ? 1 : 0);
        }

        public static int LocalTarget(int garrisonCities,
            int uncommandedLocalArmies, int deputySlots,
            int frontierBonusCities)
        {
            return NonNegative(garrisonCities) +
                   NonNegative(uncommandedLocalArmies) +
                   NonNegative(deputySlots) +
                   NonNegative(frontierBonusCities);
        }

        public static int Vacancies(int target, int qualifiedActive,
            int lockedAppointments)
        {
            return Math.Max(0, NonNegative(target) -
                NonNegative(qualifiedActive) - NonNegative(lockedAppointments));
        }

        private static int NonNegative(int value)
        {
            return Math.Max(0, value);
        }
    }
}
