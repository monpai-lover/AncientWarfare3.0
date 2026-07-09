using System.Text;

namespace AncientWarfare3.core.lineage
{
    public static class FamilyTreeLabelLayoutRules
    {
        public const int MaxNodeNameHalfUnits = 16;
        private const string Ellipsis = "...";

        public static string BuildNodeNameLabel(string pRelationLabel, string pDisplayName,
            string pSexSuffix, string pSelfLabel = null)
        {
            string relation = NormalizeSpaces(pRelationLabel);
            string self = NormalizeSpaces(pSelfLabel);
            string name = NormalizeSpaces(pDisplayName);
            string sex = pSexSuffix ?? "";

            bool showRelation = !string.IsNullOrEmpty(relation) &&
                                (string.IsNullOrEmpty(self) || relation != self);
            string prefix = showRelation ? relation + " " : "";
            string raw = prefix + name + sex;
            if (FitsNodeNameLine(raw)) return raw;

            int fixedUnits = CountHalfUnits(prefix) + CountHalfUnits(sex);
            int nameUnits = MaxNodeNameHalfUnits - fixedUnits;
            if (nameUnits > CountHalfUnits(Ellipsis))
            {
                string compactName = CompactToHalfUnits(name, nameUnits);
                string compact = prefix + compactName + sex;
                if (FitsNodeNameLine(compact)) return compact;
            }

            string compactPrefix = showRelation
                ? CompactToHalfUnits(relation, 4) + " "
                : "";
            int compactNameUnits = MaxNodeNameHalfUnits - CountHalfUnits(compactPrefix) - CountHalfUnits(sex);
            string fallbackName = CompactToHalfUnits(name, compactNameUnits);
            string fallback = compactPrefix + fallbackName + sex;
            return FitsNodeNameLine(fallback)
                ? fallback
                : CompactToHalfUnits(fallback, MaxNodeNameHalfUnits);
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
