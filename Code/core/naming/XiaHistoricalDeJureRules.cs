using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.naming
{
    public sealed class XiaHistoricalDeJureProfile
    {
        public string StateId { get; }
        public string StateName { get; }
        public string CommanderyId { get; }
        public string CommanderyName { get; }

        public XiaHistoricalDeJureProfile(string pStateId, string pStateName,
            string pCommanderyId, string pCommanderyName)
        {
            StateId = pStateId ?? string.Empty;
            StateName = pStateName ?? string.Empty;
            CommanderyId = pCommanderyId ?? string.Empty;
            CommanderyName = pCommanderyName ?? string.Empty;
        }
    }

    public static class XiaHistoricalDeJureRules
    {
        public static bool IsHistoricalCountyName(
            XiaHistoricalDeJureCatalog pCatalog, string pName)
        {
            string normalized = Normalize(pName);
            if (pCatalog == null || normalized.Length == 0) return false;
            return pCatalog.States.Where(p => p != null)
                .SelectMany(p => p.Commanderies ??
                    Array.Empty<XiaHistoricalCommanderyDefinition>())
                .Where(p => p != null)
                .Any(p => p.CityNames.Any(cityName =>
                    string.Equals(cityName, normalized,
                        StringComparison.Ordinal)));
        }

        public static string ResolveCountyName(
            XiaHistoricalDeJureCatalog pCatalog, string pChineseCityName,
            string pProjectedCityName, string pLanguage)
        {
            string projected = Normalize(pProjectedCityName);
            if (!AWNamingLanguageRules.IsChinesePresentation(pLanguage))
                return projected;
            string chinese = Normalize(pChineseCityName);
            if (pCatalog != null && chinese.Length > 0 &&
                IsHistoricalCountyName(pCatalog, chinese)) return chinese;
            return projected.Length > 0 ? projected : chinese;
        }

        public static XiaHistoricalDeJureProfile SelectProfile(
            XiaHistoricalDeJureCatalog pCatalog,
            IEnumerable<string> pMemberCityNames, int pStableSelector)
        {
            if (pCatalog == null) return EmptyProfile();
            var names = new HashSet<string>((pMemberCityNames ??
                Array.Empty<string>()).Select(Normalize),
                StringComparer.Ordinal);
            var matches = new List<XiaHistoricalDeJureProfile>();
            foreach (XiaHistoricalStateDefinition state in pCatalog.States)
            {
                foreach (XiaHistoricalCommanderyDefinition commandery in
                         state.Commanderies)
                {
                    if (!commandery.CityNames.Any(names.Contains)) continue;
                    matches.Add(new XiaHistoricalDeJureProfile(state.Id,
                        state.Name, commandery.Id, commandery.Name));
                }
            }
            if (matches.Count == 0) return EmptyProfile();
            int index = pStableSelector == int.MinValue
                ? 0 : (int)((uint)pStableSelector % (uint)matches.Count);
            return matches.OrderBy(p => p.StateId, StringComparer.Ordinal)
                .ThenBy(p => p.CommanderyId, StringComparer.Ordinal)
                .ElementAt(index);
        }

        public static string SelectUnusedCounty(
            XiaHistoricalCommanderyDefinition pCommandery,
            IEnumerable<string> pUsedNames, int pStableSelector)
        {
            if (pCommandery == null) return string.Empty;
            var used = new HashSet<string>((pUsedNames ??
                Array.Empty<string>()).Select(Normalize),
                StringComparer.Ordinal);
            string[] available = pCommandery.CityNames
                .Where(p => !used.Contains(p)).ToArray();
            if (available.Length == 0) return string.Empty;
            int index = pStableSelector == int.MinValue
                ? 0 : (int)((uint)pStableSelector % (uint)available.Length);
            return available[index];
        }

        public static string SelectUnusedCountyFromCatalog(
            XiaHistoricalDeJureCatalog pCatalog, IEnumerable<string> pUsedNames,
            int pStableSelector)
        {
            if (pCatalog == null) return string.Empty;
            var commanderies = pCatalog.States.Where(p => p != null)
                .SelectMany(p => p.Commanderies ??
                    Array.Empty<XiaHistoricalCommanderyDefinition>())
                .Where(p => p != null).OrderBy(p => p.Id,
                    StringComparer.Ordinal).ToArray();
            if (commanderies.Length == 0) return string.Empty;
            int start = pStableSelector == int.MinValue ? 0 :
                (int)((uint)pStableSelector % (uint)commanderies.Length);
            for (int offset = 0; offset < commanderies.Length; offset++)
            {
                int index = (start + offset) % commanderies.Length;
                string candidate = SelectUnusedCounty(commanderies[index],
                    pUsedNames, pStableSelector + offset);
                if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
            }
            return string.Empty;
        }

        public static bool ShouldNameCity(bool pEnabled,
            bool pNameIsGenerated)
        {
            return pEnabled && pNameIsGenerated;
        }

        public static string Normalize(string pName)
        {
            return (pName ?? string.Empty).Trim();
        }

        private static XiaHistoricalDeJureProfile EmptyProfile()
        {
            return new XiaHistoricalDeJureProfile(string.Empty, string.Empty,
                string.Empty, string.Empty);
        }
    }

    public static class XiaHistoricalDeJureCatalogService
    {
        public static XiaHistoricalDeJureCatalog Current { get; private set; } =
            XiaHistoricalDeJureCatalog.Empty();

        public static void Initialize(string pModPath,
            Action<string> pWarning = null)
        {
            string path = System.IO.Path.Combine(pModPath ?? string.Empty,
                "name_generators", "lib", "Xia\u5386\u53f2\u5dde\u90e1.json");
            Current = XiaHistoricalDeJureCatalog.LoadFromFile(path, pWarning);
        }
    }
}
