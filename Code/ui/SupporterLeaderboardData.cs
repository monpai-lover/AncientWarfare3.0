using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AncientWarfare3;

namespace AncientWarfare3.ui
{
    /// <summary>One supporter entry shown by the AW3 thank-you leaderboard.</summary>
    internal sealed class SupporterLeaderboardEntry
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public string Amount { get; set; } = "";
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Reads the updateable supporter list beside the mod files. The built-in
    /// entry keeps the window useful on a fresh install before the optional
    /// CSV has been copied into the Mods directory.
    /// </summary>
    internal static class SupporterLeaderboardData
    {
        public const string FileName = "supporters.csv";

        private static readonly IReadOnlyList<SupporterLeaderboardEntry> BuiltIn =
            new List<SupporterLeaderboardEntry>
            {
                new SupporterLeaderboardEntry
                {
                    Rank = 1,
                    Name = "一米",
                    Amount = "",
                    Date = "",
                    Description = "提供了技术支持和帮助:寻路/大步长调度器"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 2,
                    Name = "刘季",
                    Amount = "80",
                    Date = "2026-08-19"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 3,
                    Name = "Jake",
                    Amount = "20",
                    Date = "2026-08-02"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 4,
                    Name = "Au",
                    Amount = "20",
                    Date = "2026-08-01"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 5,
                    Name = "贰肆",
                    Amount = "15",
                    Date = "2026-08-02"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 6,
                    Name = "米鸡林",
                    Amount = "10",
                    Date = "2026-07-31"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 7,
                    Name = "Beluga",
                    Amount = "15",
                    Date = "2026-08-02"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 8,
                    Name = "妖妖凛",
                    Amount = "25",
                    Date = "2026-07-31"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 9,
                    Name = "Coherence",
                    Amount = "22.90",
                    Date = "2026-08-02"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 10,
                    Name = "未明天逍遥行",
                    Amount = "20.00",
                    Date = "2026-08-02"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 11,
                    Name = "阿良",
                    Amount = "50",
                    Date = "2026-08-04"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 12,
                    Name = "MO",
                    Amount = "10",
                    Date = "2026-08-04"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 13,
                    Name = "Mio",
                    Amount = "20",
                    Date = "2026-08-09"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 14,
                    Name = "华章计",
                    Amount = "50",
                    Date = "2026-08-12"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 15,
                    Name = "张九世",
                    Amount = "50",
                    Date = "2026-08-13"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 18,
                    Name = "阿巴",
                    Amount = "10",
                    Date = "2026-08-19"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 19,
                    Name = "博士",
                    Amount = "",
                    Date = "",
                    Description = "提供了一些建筑和人物贴图"
                },
                new SupporterLeaderboardEntry
                {
                    Rank = 20,
                    Name = "vader",
                    Amount = "",
                    Date = "",
                    Description = "提供了很多的人物和建筑贴图"
                }
            };

        public static IReadOnlyList<SupporterLeaderboardEntry> Read()
        {
            string modFolder = null;
            try
            {
                modFolder = ModClass.Instance?.GetDeclaration()?.FolderPath;
            }
            catch
            {
                // A UI window can be opened while the mod declaration is still
                // being initialized. Use the built-in list in that case.
            }

            if (string.IsNullOrEmpty(modFolder)) return BuiltIn;
            string path = Path.Combine(modFolder, FileName);
            if (!File.Exists(path)) return BuiltIn;

            try
            {
                List<SupporterLeaderboardEntry> entries = Parse(File.ReadAllLines(path));
                return entries.Count == 0 ? BuiltIn : entries;
            }
            catch
            {
                // Malformed external data must never make the in-game UI fail.
                return BuiltIn;
            }
        }

        internal static List<SupporterLeaderboardEntry> Parse(
            IEnumerable<string> pLines)
        {
            var result = new List<SupporterLeaderboardEntry>();
            if (pLines == null) return result;

            bool first = true;
            foreach (string raw in pLines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#",
                        StringComparison.Ordinal)) continue;

                List<string> fields = SplitCsv(line);
                if (first)
                {
                    first = false;
                    if (fields.Count > 0 &&
                        string.Equals(fields[0].Trim(), "rank",
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (fields.Count < 2) continue;
                if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int rank) || rank < 1)
                    rank = result.Count + 1;

                string name = Clean(fields.Count > 1 ? fields[1] : "");
                string amount = Clean(fields.Count > 2 ? fields[2] : "");
                string date = Clean(fields.Count > 3 ? fields[3] : "");
                string description = Clean(fields.Count > 4 ? fields[4] : "");
                if (string.IsNullOrEmpty(name)) name = "刘季";
                if (string.IsNullOrEmpty(amount) && string.IsNullOrEmpty(description))
                    amount = "-";
                if (string.IsNullOrEmpty(date) && string.IsNullOrEmpty(description))
                    date = "-";

                result.Add(new SupporterLeaderboardEntry
                {
                    Rank = rank,
                    Name = name,
                    Amount = amount,
                    Date = date,
                    Description = description
                });
            }

            result.Sort((a, b) =>
            {
                int byRank = a.Rank.CompareTo(b.Rank);
                return byRank != 0
                    ? byRank
                    : string.Compare(a.Name, b.Name,
                        StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < result.Count; i++)
                result[i].Rank = i + 1;
            return result;
        }

        private static string Clean(string pValue)
        {
            string value = (pValue ?? "").Trim();
            return value.Length >= 2 && value[0] == '"' &&
                   value[value.Length - 1] == '"'
                ? value.Substring(1, value.Length - 2).Replace("\"\"", "\"")
                : value;
        }

        private static List<string> SplitCsv(string pLine)
        {
            var values = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < pLine.Length; i++)
            {
                char c = pLine[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < pLine.Length && pLine[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                    continue;
                }

                if (c == ',' && !quoted)
                {
                    values.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            values.Add(current.ToString());
            return values;
        }
    }
}
