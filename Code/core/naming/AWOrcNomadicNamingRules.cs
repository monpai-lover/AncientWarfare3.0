using System.Text;

namespace AncientWarfare3.core.naming
{
    public static class AWOrcNomadicNamingRules
    {
        public const string FamilyStemGeneratorId =
            "orc_nomadic_family_stem";

        public static string ResolveGeneratorId(AWNamingObjectKind pKind)
        {
            return pKind switch
            {
                AWNamingObjectKind.Actor => "orc_nomadic_name",
                AWNamingObjectKind.Alliance => "orc_nomadic_alliance",
                AWNamingObjectKind.City => "orc_nomadic_city",
                AWNamingObjectKind.Clan => "orc_nomadic_clan",
                AWNamingObjectKind.Culture => "orc_nomadic_culture",
                AWNamingObjectKind.Kingdom => "orc_nomadic_kingdom",
                AWNamingObjectKind.Language => "orc_nomadic_language",
                AWNamingObjectKind.Religion => "orc_nomadic_religion",
                AWNamingObjectKind.Subspecies => "orc_nomadic_subspecies",
                _ => string.Empty
            };
        }

        public static string ResolveFallbackGeneratorId(
            AWNamingObjectKind pKind)
        {
            return pKind switch
            {
                AWNamingObjectKind.Actor => "orc_name",
                AWNamingObjectKind.City => "orc_city",
                AWNamingObjectKind.Clan => "orc_clan",
                AWNamingObjectKind.Culture => "orc_culture",
                AWNamingObjectKind.Kingdom => "orc_kingdom",
                _ => string.Empty
            };
        }

        public static string BuildClanTitle(string pClanStem)
        {
            string stem = NormalizeWhitespace(pClanStem);
            return stem.Length == 0 ? string.Empty : stem + "部落";
        }

        private static string NormalizeWhitespace(string pValue)
        {
            string value = (pValue ?? string.Empty).Trim();
            if (value.Length == 0)
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }
    }
}
