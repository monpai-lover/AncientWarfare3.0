using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public static class PeasantRebelOutlawNameRules
    {
        public const string LibraryId = "土匪名根";

        private static readonly string[] RouteSuffixes =
            { "义军", "贼" };

        public static string NormalizeRoot(string value)
        {
            string root = (value ?? "").Trim();
            bool changed;
            do
            {
                changed = false;
                foreach (string suffix in RouteSuffixes)
                {
                    if (!root.EndsWith(suffix,
                            StringComparison.Ordinal)) continue;
                    root = root.Substring(0,
                        root.Length - suffix.Length).Trim();
                    changed = true;
                }
            } while (changed && root.Length > 0);
            return root;
        }

        public static bool IsValidLibraryRoot(string value,
            IReadOnlyList<string> roots)
        {
            string root = NormalizeRoot(value);
            if (!ContainsHan(root) || roots == null) return false;
            return roots.Any(candidate => string.Equals(
                NormalizeRoot(candidate), root, StringComparison.Ordinal));
        }

        public static string SelectRoot(IReadOnlyList<string> roots,
            long seed)
        {
            string[] valid = (roots ?? Array.Empty<string>())
                .Select(NormalizeRoot)
                .Where(ContainsHan)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (valid.Length == 0) return "";
            ulong mixed = Mix(unchecked((ulong)seed));
            return valid[(int)(mixed % (ulong)valid.Length)];
        }

        public static string ResolveRoot(string stored,
            IReadOnlyList<string> roots, long seed)
        {
            return IsValidLibraryRoot(stored, roots)
                ? NormalizeRoot(stored)
                : SelectRoot(roots, seed);
        }

        public static string ComposeName(string root, string route)
        {
            return NormalizeRoot(root) +
                (route == PeasantRebelRouteIds.Bandit ? "贼" : "义军");
        }

        private static bool ContainsHan(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char character in value)
            {
                if (character >= '\u3400' && character <= '\u4DBF' ||
                    character >= '\u4E00' && character <= '\u9FFF')
                    return true;
            }
            return false;
        }

        // SplitMix64 gives stable distribution without runtime RNG state.
        private static ulong Mix(ulong value)
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ value >> 30) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ value >> 27) * 0x94D049BB133111EBUL;
            return value ^ value >> 31;
        }
    }
}
