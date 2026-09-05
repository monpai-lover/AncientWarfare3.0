using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AncientWarfare3.content.supporters
{
    public sealed class SupporterRosterEntry
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public string Amount { get; set; } = "";
        public string Date { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public static class SupporterRosterData
    {
        public const string FileName = "supporters.csv";

        public static IReadOnlyList<SupporterRosterEntry> Read()
        {
            string path = ResolveFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return Array.Empty<SupporterRosterEntry>();
            try
            {
                return Parse(File.ReadAllLines(path));
            }
            catch
            {
                return Array.Empty<SupporterRosterEntry>();
            }
        }

        public static List<SupporterRosterEntry> Parse(
            IEnumerable<string> pLines)
        {
            var result = new List<SupporterRosterEntry>();
            if (pLines == null) return result;

            bool firstDataLine = true;
            foreach (string raw in pLines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#",
                        StringComparison.Ordinal)) continue;

                List<string> fields = SplitCsv(line);
                if (firstDataLine)
                {
                    firstDataLine = false;
                    if (fields.Count > 0 && string.Equals(fields[0].Trim(),
                            "rank", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (fields.Count < 2) continue;
                string name = Clean(fields[1]);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int rank) || rank < 1)
                    rank = result.Count + 1;

                result.Add(new SupporterRosterEntry
                {
                    Rank = rank,
                    Name = name,
                    Amount = Clean(fields.Count > 2 ? fields[2] : ""),
                    Date = Clean(fields.Count > 3 ? fields[3] : ""),
                    Description = Clean(fields.Count > 4 ? fields[4] : "")
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
            for (int i = 0; i < result.Count; i++) result[i].Rank = i + 1;
            return result;
        }

        private static string ResolveFilePath()
        {
#if !AW3_RULES_TESTS
            try
            {
                string folder = ModClass.Instance?.GetDeclaration()?.FolderPath;
                if (!string.IsNullOrWhiteSpace(folder))
                    return Path.Combine(folder, FileName);
            }
            catch
            {
                return "";
            }
#else
            string current = Environment.CurrentDirectory;
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(current); i++)
            {
                string candidate = Path.Combine(current, FileName);
                if (File.Exists(candidate)) return candidate;
                current = Directory.GetParent(current)?.FullName;
            }
#endif
            return "";
        }

        private static string Clean(string pValue)
        {
            return (pValue ?? "").Trim();
        }

        private static List<string> SplitCsv(string pLine)
        {
            var values = new List<string>();
            var current = new StringBuilder();
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
