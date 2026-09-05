using System;
using System.Collections.Generic;

namespace AncientWarfare3.content.figures
{
    internal static class HistoricalFigureCardCuratedBiographies
    {
        private static readonly IReadOnlyDictionary<string, string> ByCardId =
            Build();

        public static int Count => ByCardId.Count;

        public static bool TryGet(string pCardId, out string pBiography)
        {
            return ByCardId.TryGetValue(pCardId ?? "", out pBiography);
        }

        public static string Summary(string pBiography)
        {
            string text = (pBiography ?? "").Trim();
            int ending = text.IndexOf('。');
            return ending >= 0 ? text.Substring(0, ending + 1) : text;
        }

        private static IReadOnlyDictionary<string, string> Build()
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            HistoricalFigureCardBiographiesPreQin.AddTo(entries);
            HistoricalFigureCardBiographiesHan.AddTo(entries);
            HistoricalFigureCardBiographiesThreeSix.AddTo(entries);
            HistoricalFigureCardBiographiesSuiTang.AddTo(entries);
            HistoricalFigureCardBiographiesFiveSong.AddTo(entries);
            HistoricalFigureCardBiographiesYuanMingQing.AddTo(entries);
            return entries;
        }
    }
}
