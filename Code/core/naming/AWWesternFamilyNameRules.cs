using System;
using System.Collections.Generic;
using System.Text;

namespace AncientWarfare3.core.naming
{
    public enum WesternNamingTradition
    {
        Von,
        De,
        Van,
        Di
    }

    public static class AWWesternFamilyNameRules
    {
        public static string BuildActor(string given, string familyStem,
            bool noble)
        {
            string normalizedGiven = NormalizeWhitespace(given);
            string family = NormalizeWhitespace(familyStem);
            if (family.Length == 0)
                return normalizedGiven;
            normalizedGiven = NormalizeGivenForFamily(normalizedGiven, family);
            if (normalizedGiven.Length == 0)
                return family;
            return normalizedGiven + " " + family;
        }

        public static string BuildFamilyTitle(
            WesternNamingTradition pTradition, string pOriginCity)
        {
            string stem = BuildFamilyStem(pTradition, pOriginCity);
            return stem.Length == 0 ? string.Empty : stem + "家族";
        }

        public static string BuildFamilyStem(
            WesternNamingTradition pTradition, string pOriginCity)
        {
            string origin = NormalizeWhitespace(pOriginCity);
            if (origin.Length == 0)
                return string.Empty;

            string particle;
            switch (pTradition)
            {
                case WesternNamingTradition.Von:
                    particle = "冯·";
                    break;
                case WesternNamingTradition.De:
                    particle = "德·";
                    break;
                case WesternNamingTradition.Van:
                    particle = "范·";
                    break;
                case WesternNamingTradition.Di:
                    particle = "迪·";
                    break;
                default:
                    return string.Empty;
            }

            return particle + origin;
        }

        public static string ResolveFamilyStem(long seed,
            WesternNamingTradition tradition, string originCity,
            IReadOnlyList<string> dictionaryWords)
        {
            if ((seed & 1L) == 0L && dictionaryWords != null &&
                dictionaryWords.Count > 0)
            {
                ulong stableSeed = seed == long.MinValue
                    ? (ulong)long.MaxValue + 1UL
                    : (ulong)Math.Abs(seed);
                string selected = dictionaryWords[
                    (int)((stableSeed / 2UL) % (ulong)dictionaryWords.Count)];
                if (!string.IsNullOrWhiteSpace(selected))
                    return NormalizeWhitespace(selected);
            }

            return BuildFamilyStem(tradition, originCity);
        }

        private static string NormalizeWhitespace(string pValue)
        {
            string value = (pValue ?? string.Empty).Trim();
            if (value.Length == 0)
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static string NormalizeGivenForFamily(string pGiven,
            string pFamily)
        {
            if (pGiven == pFamily)
                return string.Empty;

            string suffix = " " + pFamily;
            return pGiven.EndsWith(suffix,
                    System.StringComparison.Ordinal)
                ? pGiven.Substring(0, pGiven.Length - suffix.Length)
                : pGiven;
        }
    }
}
