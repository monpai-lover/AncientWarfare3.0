using System.Text;

namespace AncientWarfare3.core.lineage
{
    public static class FamilyTreeLabelLayoutRules
    {
        public const float NodeWidth = 70f;
        public const float LabelWidth = 70f;
        public const float NameLabelHeight = 14f;
        public const float SocialTitleHeight = 18f;
        public const float CompactSocialTitleHeight = 9f;
        public const int MaxNodeNameHalfUnits = 16;
        public const int MaxNodeNameLines = 2;
        public const int MaxSocialTitleLineHalfUnits = 16;
        public const int MaxSocialTitleLines = 2;
        public const string DisplayEllipsis = "\u2026";
        private const string Ellipsis = "...";

        public static string ResolvePrimaryDisplayName(bool pIsAlive,
            string pDisplayName, string pRitualAppellation)
        {
            return NormalizeSpaces(pDisplayName);
        }

        public static string BuildIdentityTitleBlock(string pRitualAppellation,
            string pSocialTitle, int pMaxLines = MaxSocialTitleLines)
        {
            int maxLines = pMaxLines > MaxSocialTitleLines
                ? MaxSocialTitleLines
                : pMaxLines;
            if (maxLines <= 0) return "";

            string ritual = NormalizeSpaces(pRitualAppellation);
            string social = RemoveDuplicateRole(pSocialTitle, ritual);
            if (string.IsNullOrEmpty(ritual))
                return BuildSocialTitleLabel(social, maxLines);

            string ritualLine = EllipsizeLine(ritual,
                MaxSocialTitleLineHalfUnits);
            if (maxLines == 1 || string.IsNullOrEmpty(social))
                return ritualLine;

            string socialLine = BuildSocialTitleLabel(social, 1);
            return string.IsNullOrEmpty(socialLine)
                ? ritualLine
                : ritualLine + "\n" + socialLine;
        }

        public static string BuildNodeNameLabel(string pRelationLabel, string pDisplayName,
            string pSexSuffix, string pSelfLabel = null)
        {
            string relation = NormalizeSpaces(pRelationLabel);
            string self = NormalizeSpaces(pSelfLabel);
            string name = NormalizeSpaces(pDisplayName);
            string sex = pSexSuffix ?? "";

            bool showRelation = !string.IsNullOrEmpty(relation) &&
                                (string.IsNullOrEmpty(self) || relation != self);
            string nameAndSex = name + sex;
            if (!showRelation) return nameAndSex;

            string withRelation = relation + " " + nameAndSex;
            return CountHalfUnits(withRelation) <=
                   MaxNodeNameHalfUnits * MaxNodeNameLines
                ? withRelation
                : nameAndSex;
        }

        public static string BuildSocialTitleLabel(string pFullTitle,
            int pMaxLines = MaxSocialTitleLines)
        {
            string title = NormalizeSpaces(pFullTitle);
            if (string.IsNullOrEmpty(title) || pMaxLines <= 0) return "";

            int maxLines = pMaxLines > MaxSocialTitleLines
                ? MaxSocialTitleLines
                : pMaxLines;
            if (CountHalfUnits(title) <= MaxSocialTitleLineHalfUnits)
                return title;

            string[] roles = SplitTitleRoles(title);
            if (roles.Length > 1)
                return PackCompactRoles(roles, maxLines);
            return BuildEllipsizedTextBlock(title, maxLines);
        }

        private static string PackCompactRoles(string[] pRoles, int pMaxLines)
        {
            const string separator = " \u00b7 ";
            var lines = new System.Collections.Generic.List<string>(pMaxLines);
            string current = "";
            for (int i = 0; i < pRoles.Length; i++)
            {
                string role = CompactRole(pRoles[i]);
                if (string.IsNullOrEmpty(role)) continue;
                string candidate = string.IsNullOrEmpty(current)
                    ? role
                    : current + separator + role;
                if (CountHalfUnits(candidate) <= MaxSocialTitleLineHalfUnits)
                {
                    current = candidate;
                    continue;
                }

                if (string.IsNullOrEmpty(current))
                {
                    current = EllipsizeLine(role,
                        MaxSocialTitleLineHalfUnits);
                    continue;
                }
                if (lines.Count + 1 >= pMaxLines)
                {
                    lines.Add(AppendOmissionMarker(current));
                    return string.Join("\n", lines.ToArray());
                }
                lines.Add(current);
                current = CountHalfUnits(role) <= MaxSocialTitleLineHalfUnits
                    ? role
                    : EllipsizeLine(role, MaxSocialTitleLineHalfUnits);
            }

            if (!string.IsNullOrEmpty(current)) lines.Add(current);
            return string.Join("\n", lines.ToArray());
        }

        private static string[] SplitTitleRoles(string pTitle)
        {
            string[] raw = pTitle.Split(new[] { '\u00b7' },
                System.StringSplitOptions.RemoveEmptyEntries);
            var roles = new System.Collections.Generic.List<string>(raw.Length);
            foreach (string item in raw)
            {
                string role = NormalizeSpaces(item);
                if (!string.IsNullOrEmpty(role)) roles.Add(role);
            }
            return roles.ToArray();
        }

        private static string RemoveDuplicateRole(string pTitle,
            string pExcludedRole)
        {
            string title = NormalizeSpaces(pTitle);
            string excluded = NormalizeSpaces(pExcludedRole);
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(excluded))
                return title;

            string[] roles = SplitTitleRoles(title);
            var kept = new System.Collections.Generic.List<string>(roles.Length);
            foreach (string role in roles)
                if (!string.Equals(role, excluded,
                        System.StringComparison.Ordinal))
                    kept.Add(role);
            return string.Join(" \u00b7 ", kept.ToArray());
        }

        private static string CompactRole(string pRole)
        {
            string role = NormalizeSpaces(pRole);
            int lastSpace = role.LastIndexOf(' ');
            if (lastSpace > 0 && lastSpace + 1 < role.Length)
            {
                string office = role.Substring(lastSpace + 1).Trim();
                if (!string.IsNullOrEmpty(office)) return office;
            }
            return CountHalfUnits(role) <= MaxSocialTitleLineHalfUnits
                ? role
                : EllipsizeLine(role, MaxSocialTitleLineHalfUnits);
        }

        private static string BuildEllipsizedTextBlock(string pTitle,
            int pMaxLines)
        {
            string remaining = pTitle;

            var lines = new System.Collections.Generic.List<string>(pMaxLines);
            for (int lineIndex = 0; lineIndex < pMaxLines && remaining.Length > 0;
                 lineIndex++)
            {
                bool lastLine = lineIndex == pMaxLines - 1;
                int budget = MaxSocialTitleLineHalfUnits;
                if (lastLine && CountHalfUnits(remaining) > budget)
                    budget -= CountHalfUnits(DisplayEllipsis);

                int consumed;
                string line = TakeNaturalLine(remaining, budget, out consumed);
                if (string.IsNullOrEmpty(line))
                {
                    line = TakeHalfUnits(remaining, budget).Trim();
                    consumed = line.Length;
                }
                remaining = TrimLeadingTitleSeparator(
                    consumed >= remaining.Length
                        ? ""
                        : remaining.Substring(consumed));

                if (lastLine && remaining.Length > 0)
                    line = TrimTrailingTitleSeparator(line) + DisplayEllipsis;
                else
                    line = TrimTrailingTitleSeparator(line);

                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            return string.Join("\n", lines.ToArray());
        }

        private static string AppendOmissionMarker(string pLine)
        {
            string line = TrimTrailingTitleSeparator(pLine);
            if (CountHalfUnits(line) + CountHalfUnits(DisplayEllipsis) <=
                MaxSocialTitleLineHalfUnits)
                return line + DisplayEllipsis;

            int delimiter = line.LastIndexOf(" \u00b7 ",
                System.StringComparison.Ordinal);
            if (delimiter > 0)
            {
                string withoutLastRole = line.Substring(0, delimiter).TrimEnd();
                if (CountHalfUnits(withoutLastRole) +
                    CountHalfUnits(DisplayEllipsis) <=
                    MaxSocialTitleLineHalfUnits)
                    return withoutLastRole + DisplayEllipsis;
            }
            return EllipsizeLine(line, MaxSocialTitleLineHalfUnits);
        }

        private static string EllipsizeLine(string pText, int pMaxHalfUnits)
        {
            string text = NormalizeSpaces(pText);
            int ellipsisUnits = CountHalfUnits(DisplayEllipsis);
            if (CountHalfUnits(text) <= pMaxHalfUnits) return text;
            string head = TakeHalfUnits(text,
                pMaxHalfUnits - ellipsisUnits).TrimEnd();
            return TrimTrailingTitleSeparator(head) + DisplayEllipsis;
        }

        public static bool FitsNodeNameLine(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return true;
            return pText.IndexOf('\n') < 0 && pText.IndexOf('\r') < 0 &&
                   CountHalfUnits(pText) <= MaxNodeNameHalfUnits;
        }

        public static string CompactToHalfUnits(string pText, int pMaxHalfUnits)
        {
            string text = NormalizeSpaces(pText);
            if (string.IsNullOrEmpty(text) || pMaxHalfUnits <= 0) return "";
            if (CountHalfUnits(text) <= pMaxHalfUnits) return text;

            int ellipsisUnits = CountHalfUnits(Ellipsis);
            if (pMaxHalfUnits <= ellipsisUnits)
                return TakeHalfUnits(text, pMaxHalfUnits);

            string head = TakeHalfUnits(text, pMaxHalfUnits - ellipsisUnits).TrimEnd();
            return string.IsNullOrEmpty(head) ? TakeHalfUnits(text, pMaxHalfUnits) : head + Ellipsis;
        }

        public static int CountHalfUnits(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return 0;
            int total = 0;
            foreach (char c in pText)
                total += VisualHalfUnits(c);
            return total;
        }

        private static string TakeHalfUnits(string pText, int pMaxHalfUnits)
        {
            if (string.IsNullOrEmpty(pText) || pMaxHalfUnits <= 0) return "";
            var sb = new StringBuilder();
            int used = 0;
            foreach (char c in pText)
            {
                int units = VisualHalfUnits(c);
                if (used + units > pMaxHalfUnits) break;
                sb.Append(c);
                used += units;
            }
            return sb.ToString();
        }

        private static string TakeNaturalLine(string pText, int pMaxHalfUnits,
            out int pConsumed)
        {
            pConsumed = 0;
            if (string.IsNullOrEmpty(pText) || pMaxHalfUnits <= 0) return "";

            int used = 0;
            int lastBreak = -1;
            for (int i = 0; i < pText.Length; i++)
            {
                int units = VisualHalfUnits(pText[i]);
                if (used + units > pMaxHalfUnits) break;
                used += units;
                pConsumed = i + 1;
                if (pText[i] == ' ' || pText[i] == '\u00b7')
                    lastBreak = i + 1;
            }

            if (pConsumed < pText.Length && lastBreak > 0)
                pConsumed = lastBreak;
            return pText.Substring(0, pConsumed).Trim();
        }

        private static string TrimLeadingTitleSeparator(string pText)
        {
            return (pText ?? "").TrimStart(' ', '\u00b7');
        }

        private static string TrimTrailingTitleSeparator(string pText)
        {
            return (pText ?? "").TrimEnd(' ', '\u00b7');
        }

        private static int VisualHalfUnits(char pChar)
        {
            if (pChar == '\r' || pChar == '\n') return 0;
            return pChar <= 0x007f ? 1 : 2;
        }

        private static string NormalizeSpaces(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return "";
            var sb = new StringBuilder(pText.Length);
            bool pendingSpace = false;
            foreach (char c in pText)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
