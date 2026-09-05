using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.supporters;

namespace AncientWarfare3.ui
{
    internal sealed class SupporterLeaderboardEntry
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public string Amount { get; set; } = "";
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
    }

    internal static class SupporterLeaderboardData
    {
        public const string FileName = SupporterRosterData.FileName;

        public static IReadOnlyList<SupporterLeaderboardEntry> Read()
        {
            return SupporterRosterData.Read().Select(p =>
                new SupporterLeaderboardEntry
                {
                    Rank = p.Rank,
                    Name = p.Name,
                    Amount = string.IsNullOrEmpty(p.Amount) &&
                             string.IsNullOrEmpty(p.Description) ? "-" : p.Amount,
                    Date = string.IsNullOrEmpty(p.Date) &&
                           string.IsNullOrEmpty(p.Description) ? "-" : p.Date,
                    Description = p.Description
                }).ToArray();
        }
    }
}
