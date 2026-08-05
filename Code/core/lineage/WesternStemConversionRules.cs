using System;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    public static class WesternStemConversionRules
    {
        private static readonly string[] Particles =
            { "德", "冯", "迪", "范" };
        private static readonly string[] FallbackSurnames =
            { "贾", "冯", "范", "迪", "赵", "韩", "梁", "周" };
        private static readonly string[] FallbackShi =
            { "姬", "姜", "姒", "嬴", "妫", "妘", "姚", "芈" };

        public static string ResolveSurname(long pSourceShiId,
            string pRawStem)
        {
            string core = StripLeadingParticle(pRawStem);
            string first = TryFirstCjk(core);
            return first.Length > 0
                ? first
                : StableChoice(FallbackSurnames, pSourceShiId, pRawStem,
                    "western_stem_surname");
        }

        public static string ResolveShi(long pSourceShiId,
            string pRawStem, string pSurname)
        {
            string core = StripLeadingParticle(pRawStem);
            string surname = (pSurname ?? string.Empty).Trim();
            bool skippedSurname = false;
            for (int i = 0; i < core.Length; i++)
            {
                if (!IsCjk(core[i])) continue;
                string candidate = core[i].ToString();
                if (!skippedSurname && candidate == surname)
                {
                    skippedSurname = true;
                    continue;
                }
                if (candidate != surname) return candidate;
            }

            string fallback = StableChoice(FallbackShi, pSourceShiId,
                pRawStem, "western_stem_shi");
            if (fallback == surname)
                fallback = FallbackShi[(Array.IndexOf(FallbackShi, fallback) +
                    1) % FallbackShi.Length];
            return fallback;
        }

        private static string StripLeadingParticle(string pRawStem)
        {
            string value = (pRawStem ?? string.Empty).Trim();
            for (int i = 0; i < Particles.Length; i++)
            {
                string particle = Particles[i];
                if (!value.StartsWith(particle,
                        StringComparison.Ordinal)) continue;
                value = value.Substring(particle.Length).TrimStart(
                    ' ', '\t', '·', '・', '-', '‐', '‑');
                return value;
            }
            return value;
        }

        private static string TryFirstCjk(string pValue)
        {
            for (int i = 0; i < (pValue ?? string.Empty).Length; i++)
                if (IsCjk(pValue[i])) return pValue[i].ToString();
            return string.Empty;
        }

        private static bool IsCjk(char pValue)
        {
            return pValue >= '\u3400' && pValue <= '\u9fff';
        }

        private static string StableChoice(string[] pValues, long pId,
            string pRawStem, string pSalt)
        {
            long seed = AWNamingSeedRules.Combine(pId,
                StableHash(pRawStem), pSalt, 1);
            int index = (int)((ulong)seed % (ulong)pValues.Length);
            return pValues[index];
        }

        private static long StableHash(string pValue)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                foreach (char character in pValue ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }
                return (long)hash;
            }
        }
    }
}
