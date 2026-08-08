using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    public static class XiaizedFamilyBranchTransitionRules
    {
        public static bool CanTransition(NamingProfileId pProfile,
            bool monkey, bool biologicalXia, bool valid)
        {
            if (!valid || monkey || biologicalXia) return false;
            return pProfile == NamingProfileId.Western ||
                   pProfile == NamingProfileId.OrcNomadic;
        }

        public static bool ShouldRegeneratePersonalName(
            bool protectedAuthoredName)
        {
            return !protectedAuthoredName;
        }

        public static bool NeedsSourceLineage(long lineageId, long shiId)
        {
            return lineageId < 0L || shiId < 0L;
        }

        public static string ResolveFamily(string pChineseFamily,
            string pLocalizedFamilyComponent, string pFallback)
        {
            string chinese = NormalizeXiaIdentity(pChineseFamily);
            if (chinese.Length > 0) return chinese;
            string component = NormalizeXiaIdentity(
                pLocalizedFamilyComponent);
            if (component.Length > 0) return component;
            return NormalizeXiaIdentity(pFallback);
        }

        public static string ResolveClan(string pCityChineseName,
            string pFamilyName, string pFallback)
        {
            string city = (pCityChineseName ?? string.Empty).Trim();
            string family = (pFamilyName ?? string.Empty).Trim();
            if (city.Length > 0)
            {
                string initial = city.Substring(0, 1);
                if (NormalizeXiaIdentity(initial).Length > 0 &&
                    !string.Equals(initial, family,
                        System.StringComparison.Ordinal))
                    return initial;
            }
            return NormalizeXiaIdentity(pFallback);
        }

        private static string NormalizeXiaIdentity(string pValue)
        {
            string value = (pValue ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > 4) return string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character < '\u3400' || character > '\u9fff')
                    return string.Empty;
            }
            return value;
        }
    }
}
