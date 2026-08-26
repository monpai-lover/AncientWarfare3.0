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
            return pCatalog.States.Where(p => p != null).Any(state =>
                state.CountyNames.Any(countyName => string.Equals(countyName,
                    normalized, StringComparison.Ordinal)) ||
                state.Commanderies.Where(p => p != null).Any(commandery =>
                    commandery.CityNames.Any(cityName => string.Equals(
                        cityName, normalized, StringComparison.Ordinal))));
        }

        public static bool IsHistoricalCityName(
            XiaHistoricalDeJureCatalog pCatalog, string pName)
        {
            string normalized = Normalize(pName);
            if (pCatalog == null || normalized.Length == 0) return false;
            return IsHistoricalCountyName(pCatalog, normalized) ||
                pCatalog.States.Where(p => p != null).SelectMany(state =>
                    state.Commanderies ?? Array.Empty<
                        XiaHistoricalCommanderyDefinition>()).Any(
                    commandery => commandery != null &&
                        (string.Equals(commandery.Id, normalized,
                             StringComparison.Ordinal) ||
                         string.Equals(commandery.Name, normalized,
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
            if (commanderies.Length > 0)
            {
                int start = StableIndex(pStableSelector,
                    commanderies.Length);
                for (int offset = 0; offset < commanderies.Length; offset++)
                {
                    int index = (start + offset) % commanderies.Length;
                    string candidate = SelectUnusedCounty(
                        commanderies[index], pUsedNames,
                        pStableSelector + offset);
                    if (!string.IsNullOrWhiteSpace(candidate))
                        return candidate;
                }
            }
            foreach (XiaHistoricalStateDefinition state in pCatalog.States
                         .Where(p => p != null)
                         .OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                string candidate = SelectUnusedName(state.CountyNames,
                    pUsedNames, pStableSelector);
                if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
            }
            return string.Empty;
        }

        public static string SelectUnusedCountyFromState(
            XiaHistoricalDeJureCatalog pCatalog, string pStateId,
            IEnumerable<string> pUsedNames, int pStableSelector)
        {
            XiaHistoricalStateDefinition state = pCatalog?.GetState(
                Normalize(pStateId));
            return state == null ? string.Empty : SelectUnusedName(
                state.CountyNames, pUsedNames, pStableSelector);
        }

        public static string SelectHistoricalCityName(
            XiaHistoricalDeJureCatalog pCatalog, string pStateId,
            IEnumerable<string> pUsedNames, int pStableSelector)
        {
            XiaHistoricalStateDefinition state = pCatalog?.GetState(
                Normalize(pStateId));
            if (state == null) return string.Empty;
            string commanderyName = SelectUnusedName(state.Commanderies
                    .Where(p => p != null).Select(p => p.Name), pUsedNames,
                pStableSelector);
            if (!string.IsNullOrWhiteSpace(commanderyName))
                return commanderyName;
            string countyName = SelectUnusedName(state.CountyNames,
                pUsedNames, pStableSelector);
            if (!string.IsNullOrWhiteSpace(countyName)) return countyName;
            return SelectUnusedName(state.Commanderies.Where(p => p != null)
                    .SelectMany(p => p.CityNames ?? Array.Empty<string>()),
                pUsedNames, pStableSelector);
        }

        public static XiaHistoricalCommanderyDefinition SelectCityCommandery(
            XiaHistoricalDeJureCatalog pCatalog, string pStateId,
            string pPersistedCommanderyId, string pPreferredCommanderyId,
            string pCityName, IEnumerable<string> pUsedCommanderyIds,
            int pStableSelector)
        {
            if (pCatalog == null) return null;
            XiaHistoricalStateDefinition state = pCatalog.GetState(
                Normalize(pStateId));
            XiaHistoricalCommanderyDefinition[] candidates = (state?
                .Commanderies ?? Array.Empty<
                    XiaHistoricalCommanderyDefinition>())
                .Where(p => p != null).ToArray();
            if (candidates.Length == 0) return null;

            var used = new HashSet<string>((pUsedCommanderyIds ??
                Array.Empty<string>()).Select(Normalize),
                StringComparer.Ordinal);
            XiaHistoricalCommanderyDefinition persisted = candidates.
                FirstOrDefault(p => string.Equals(p.Id,
                    Normalize(pPersistedCommanderyId),
                    StringComparison.Ordinal));
            if (persisted != null && !used.Contains(persisted.Id))
                return persisted;
            XiaHistoricalCommanderyDefinition preferred = candidates.
                FirstOrDefault(p => string.Equals(p.Id,
                    Normalize(pPreferredCommanderyId),
                    StringComparison.Ordinal));
            if (preferred != null && !used.Contains(preferred.Id))
                return preferred;

            string cityName = Normalize(pCityName);
            XiaHistoricalCommanderyDefinition[] matching = candidates.
                Where(p => !used.Contains(p.Id) && p.CityNames.Any(name =>
                    string.Equals(Normalize(name), cityName,
                        StringComparison.Ordinal)))
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            if (matching.Length > 0)
                return matching[StableIndex(pStableSelector,
                    matching.Length)];

            XiaHistoricalCommanderyDefinition[] available = candidates.
                Where(p => !used.Contains(p.Id))
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            if (available.Length > 0)
                return available[StableIndex(pStableSelector,
                    available.Length)];
            return persisted ?? preferred ?? candidates
                .OrderBy(p => p.Id, StringComparer.Ordinal).First();
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

        private static int StableIndex(int pSelector, int pCount)
        {
            if (pCount <= 0) return 0;
            return pSelector == int.MinValue
                ? 0
                : (int)((uint)pSelector % (uint)pCount);
        }

        private static string SelectUnusedName(IEnumerable<string> pNames,
            IEnumerable<string> pUsedNames, int pStableSelector)
        {
            var used = new HashSet<string>((pUsedNames ??
                Array.Empty<string>()).Select(Normalize),
                StringComparer.Ordinal);
            string[] available = (pNames ?? Array.Empty<string>())
                .Select(Normalize).Where(p => p.Length > 0 &&
                    !used.Contains(p)).Distinct(StringComparer.Ordinal)
                .ToArray();
            return available.Length == 0 ? string.Empty :
                available[StableIndex(pStableSelector, available.Length)];
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
