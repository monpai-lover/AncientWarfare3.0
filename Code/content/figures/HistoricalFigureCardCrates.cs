using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.content.figures
{
    /// <summary>
    /// A CS2-style case backed by one historical period. CardCount is derived
    /// from the catalogue so the number shown in the UI cannot drift.
    /// </summary>
    public sealed class HistoricalFigureCardCrate
    {
        internal HistoricalFigureCardCrate(string pId, string pDisplayName,
            string pDescription, int pStartYear, int pEndYear)
        {
            Id = pId ?? "";
            DisplayName = pDisplayName ?? "";
            Description = pDescription ?? "";
            NameKey = "aw_historical_figure_cards_crate_" + Id + "_name";
            DescriptionKey = "aw_historical_figure_cards_crate_" + Id + "_description";
            StartYear = pStartYear;
            EndYear = pEndYear;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string NameKey { get; }
        public string DescriptionKey { get; }
        public int StartYear { get; }
        public int EndYear { get; }
        public string ImagePath => "ui/historical_cards/crates/" + Id;

        public int CardCount => HistoricalFigureCardCatalog.GetCards(Id).Count;

        public int CardCountFor(HistoricalFigureCardRole pRole)
        {
            return HistoricalFigureCardCatalog.GetCards(Id, pRole).Count;
        }

        public bool ContainsYear(int pYear)
        {
            return pYear >= StartYear && pYear <= EndYear;
        }
    }

    /// <summary>Stable period buckets used by both the crate list and draws.</summary>
    public static class HistoricalFigureCardCrates
    {
        private const int MinimumYear = -1000000;
        private const int MaximumYear = 1000000;

        public static readonly IReadOnlyList<HistoricalFigureCardCrate> All =
            new[]
            {
                new HistoricalFigureCardCrate("pre_qin_qin", "先秦·秦",
                    "从周代到秦帝国的历史人物", MinimumYear, -207),
                new HistoricalFigureCardCrate("han", "汉",
                    "西汉、新朝与东汉的历史人物", -206, 219),
                new HistoricalFigureCardCrate("three_six_dynasties",
                    "三国·两晋·南北朝", "分裂时代的历史人物", 220, 580),
                new HistoricalFigureCardCrate("sui_tang", "隋唐",
                    "隋帝国与唐帝国的历史人物", 581, 906),
                new HistoricalFigureCardCrate("five_song", "五代·宋·辽·金",
                    "五代十国至宋辽金的历史人物", 907, 1279),
                new HistoricalFigureCardCrate("yuan_ming_qing", "元·明·清",
                    "元、明、清三代的历史人物", 1280, MaximumYear),
                new HistoricalFigureCardCrate("supporters", "赞助者",
                    "赞助，你也可以进入游戏", 1, 0)
            };

        public static HistoricalFigureCardCrate Get(string pCrateId)
        {
            if (string.IsNullOrWhiteSpace(pCrateId)) return null;
            return All.FirstOrDefault(p => string.Equals(p.Id, pCrateId,
                StringComparison.Ordinal));
        }

        public static HistoricalFigureCardCrate ForYear(int pYear)
        {
            return All.FirstOrDefault(p => p.ContainsYear(pYear));
        }
    }
}
