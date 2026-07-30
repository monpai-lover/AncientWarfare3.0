using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal enum CeremonialTitleEventKind
    {
        Posthumous,
        TempleAndPosthumous,
        Abdication,
        Deposed
    }

    internal static class CeremonialHistoryRules
    {
        public static bool ShouldWriteAccessionBook(bool republic,
            bool hasLivingRuler)
        {
            return !republic && hasLivingRuler;
        }

        public static CeremonialTitleEventKind ResolveTitleEventKind(
            string pTitleKind, string pEndReason, bool hasTemple)
        {
            string titleKind = Normalize(pTitleKind);
            string endReason = Normalize(pEndReason);
            if (titleKind == "deposed")
                return CeremonialTitleEventKind.Deposed;
            if (titleKind == "abdication" || endReason == "abdicated")
                return CeremonialTitleEventKind.Abdication;
            return hasTemple
                ? CeremonialTitleEventKind.TempleAndPosthumous
                : CeremonialTitleEventKind.Posthumous;
        }

        public static string LifeSummaryKey(string pDominant, string pGrade)
        {
            string dimension = Normalize(pDominant) switch
            {
                "civil" => "civil",
                "territory" => "territory",
                "war" => "war",
                "order" => "order",
                "ending" => "ending",
                "balanced" => "balanced",
                _ => "balanced"
            };
            string grade = Normalize(pGrade);
            string tone = grade.StartsWith("praise", StringComparison.Ordinal)
                ? "positive"
                : grade.StartsWith("blame", StringComparison.Ordinal)
                    ? "negative"
                    : "neutral";
            return "aw_hist_edict_life_" + dimension + "_" + tone;
        }

        public static string MeaningKey(char pCharacter)
        {
            return "aw_hist_posthumous_meaning_" +
                   ((int)pCharacter).ToString("x4");
        }

        public static IReadOnlyList<string> MeaningKeys(
            string pPosthumousName)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(pPosthumousName)) return result;
            var seen = new HashSet<char>();
            for (int i = 0; i < pPosthumousName.Length; i++)
            {
                char character = pPosthumousName[i];
                if (char.IsWhiteSpace(character) || !seen.Add(character))
                    continue;
                result.Add(MeaningKey(character));
            }
            return result;
        }

        private static string Normalize(string pValue)
        {
            return (pValue ?? "").Trim().ToLowerInvariant();
        }
    }
}
