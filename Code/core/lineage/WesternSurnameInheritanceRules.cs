using System;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     Pure selection rules for the lightweight Western birth path.
    ///     This path carries family identity without admitting an actor into
    ///     the full noble lineage model.
    /// </summary>
    public static class WesternSurnameInheritanceRules
    {
        public static string ResolveSurname(string pFamilyName,
            string pChineseFamilyName)
        {
            string family = Normalize(pFamilyName);
            return family.Length > 0 ? family : Normalize(pChineseFamilyName);
        }

        public static int SelectSourceSlot(bool parent1Male,
            string parent1Surname, bool parent2Male, string parent2Surname)
        {
            bool parent1Valid = Normalize(parent1Surname).Length > 0;
            bool parent2Valid = Normalize(parent2Surname).Length > 0;

            if (parent1Male && parent1Valid) return 1;
            if (parent2Male && parent2Valid) return 2;
            if (parent1Valid) return 1;
            if (parent2Valid) return 2;
            return -1;
        }

        private static string Normalize(string pValue)
        {
            return (pValue ?? string.Empty).Trim();
        }
    }
}
