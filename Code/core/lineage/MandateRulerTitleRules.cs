using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class MandateRulerTitleRules
    {
        private static readonly string[] CommonTempleNames =
        {
            "\u6210\u5b97", "\u4ec1\u5b97", "\u5ba3\u5b97", "\u7a46\u5b97", "\u82f1\u5b97",
            "\u5baa\u5b97", "\u5b5d\u5b97", "\u6b66\u5b97", "\u5b81\u5b97", "\u7406\u5b97"
        };

        public static string SelectTempleName(bool founder, bool lowOrigin, bool refounder,
            int conquestScore, int reformScore, int reignIndex)
        {
            if (founder && refounder) return "\u4e16\u7956";
            if (founder && lowOrigin) return "\u9ad8\u7956";
            if (founder && conquestScore >= 70) return "\u592a\u7956";
            if (founder) return "\u9ad8\u7956";
            if (reignIndex == 2 && reformScore >= 60) return "\u592a\u5b97";
            if (conquestScore >= 75) return "\u70c8\u7956";

            // Shizong is reserved for rare restoration/reform rulers, not the default high-reform result.
            if (reignIndex >= 3 && reformScore >= 90 && conquestScore >= 30) return "\u4e16\u5b97";
            return "";
        }

        public static string EnsureUniqueTempleName(string pCandidate, IEnumerable<string> pUsed, int pReignIndex)
        {
            if (string.IsNullOrEmpty(pCandidate)) return "";
            var used = new HashSet<string>(pUsed ?? new string[0]);
            if (!used.Contains(pCandidate)) return pCandidate;

            int start = PositiveIndex(pReignIndex - 2, CommonTempleNames.Length);
            for (int i = 0; i < CommonTempleNames.Length; i++)
            {
                string next = CommonTempleNames[(start + i) % CommonTempleNames.Length];
                if (!used.Contains(next)) return next;
            }
            return "";
        }

        public static string BuildFullTitle(string pTemple, string pDoublePosthumous)
        {
            string pair = pDoublePosthumous ?? "";
            string title = pair + "\u7687\u5e1d";
            return string.IsNullOrEmpty(pTemple) ? title : pTemple + " " + title;
        }

        public static string SelectDoublePosthumousTitle(int civil, int war, int order, int disaster)
        {
            if (disaster >= 60)
                return MandateRulerTitleDefs.NegativePairs[PositiveIndex(disaster, MandateRulerTitleDefs.NegativePairs.Length)];

            int score = civil + war + order;
            return MandateRulerTitleDefs.PositivePairs[PositiveIndex(score, MandateRulerTitleDefs.PositivePairs.Length)];
        }

        public static string EnsureUniqueDoublePosthumousTitle(string pCandidate,
            IEnumerable<string> pUsed, bool pNegative, int pReignIndex)
        {
            if (string.IsNullOrEmpty(pCandidate)) return "";
            string[] pool = pNegative
                ? MandateRulerTitleDefs.NegativePairs
                : MandateRulerTitleDefs.PositivePairs;
            if (pool.Length == 0) return pCandidate;

            var used = new HashSet<string>();
            string previous = "";
            if (pUsed != null)
            {
                foreach (string title in pUsed)
                {
                    if (string.IsNullOrEmpty(title)) continue;
                    used.Add(title);
                    previous = title;
                }
            }

            if (!used.Contains(pCandidate)) return pCandidate;

            int candidateIndex = IndexOf(pool, pCandidate);
            int start = candidateIndex >= 0
                ? PositiveIndex(candidateIndex + 1, pool.Length)
                : PositiveIndex(pReignIndex, pool.Length);
            for (int i = 0; i < pool.Length; i++)
            {
                string next = pool[(start + i) % pool.Length];
                if (!used.Contains(next)) return next;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                string next = pool[(start + i) % pool.Length];
                if (next != previous) return next;
            }

            return pCandidate;
        }

        private static int PositiveIndex(int pValue, int pLength)
        {
            if (pLength <= 0) return 0;
            int idx = pValue % pLength;
            return idx < 0 ? idx + pLength : idx;
        }

        private static int IndexOf(string[] pPool, string pValue)
        {
            for (int i = 0; i < pPool.Length; i++)
                if (pPool[i] == pValue)
                    return i;
            return -1;
        }
    }
}
