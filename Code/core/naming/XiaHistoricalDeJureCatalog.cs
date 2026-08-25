using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AncientWarfare3.core.naming
{
    public sealed class XiaHistoricalCommanderyDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> CityNames { get; }

        public XiaHistoricalCommanderyDefinition(string pId, string pName,
            IEnumerable<string> pCityNames)
        {
            Id = Normalize(pId);
            Name = Normalize(pName);
            CityNames = DistinctNames(pCityNames);
        }

        private static string Normalize(string pValue)
        {
            return (pValue ?? string.Empty).Trim();
        }

        private static IReadOnlyList<string> DistinctNames(
            IEnumerable<string> pNames)
        {
            return (pNames ?? Array.Empty<string>())
                .Select(Normalize)
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    public sealed class XiaHistoricalStateDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<XiaHistoricalCommanderyDefinition> Commanderies { get; }

        public XiaHistoricalStateDefinition(string pId, string pName,
            IEnumerable<XiaHistoricalCommanderyDefinition> pCommanderies)
        {
            Id = (pId ?? string.Empty).Trim();
            Name = (pName ?? string.Empty).Trim();
            Commanderies = (pCommanderies ?? Array.Empty<
                XiaHistoricalCommanderyDefinition>())
                .Where(p => p != null && p.Id.Length > 0)
                .GroupBy(p => p.Id, StringComparer.Ordinal)
                .Select(p => p.First())
                .ToArray();
        }
    }

    public sealed class XiaHistoricalDeJureCatalog
    {
        private readonly IReadOnlyList<XiaHistoricalStateDefinition> _states;

        public XiaHistoricalDeJureCatalog(
            IEnumerable<XiaHistoricalStateDefinition> pStates)
        {
            _states = (pStates ?? Array.Empty<XiaHistoricalStateDefinition>())
                .Where(p => p != null && p.Id.Length > 0)
                .GroupBy(p => p.Id, StringComparer.Ordinal)
                .Select(p => p.First())
                .ToArray();
        }

        public IReadOnlyList<XiaHistoricalStateDefinition> States => _states;

        public XiaHistoricalCommanderyDefinition GetCommandery(string pId)
        {
            return _states.SelectMany(p => p.Commanderies)
                .FirstOrDefault(p => string.Equals(p.Id, pId,
                    StringComparison.Ordinal));
        }

        public XiaHistoricalStateDefinition GetState(string pId)
        {
            return _states.FirstOrDefault(p => string.Equals(p.Id, pId,
                StringComparison.Ordinal));
        }

        public static XiaHistoricalDeJureCatalog LoadFromFile(string pPath,
            Action<string> pWarning = null)
        {
            if (string.IsNullOrWhiteSpace(pPath) || !File.Exists(pPath))
                return new XiaHistoricalDeJureCatalog(Array.Empty<
                    XiaHistoricalStateDefinition>());
            try
            {
                var document = JsonConvert.DeserializeObject<CatalogDocument>(
                    File.ReadAllText(pPath));
                return new XiaHistoricalDeJureCatalog(document?.states?.Select(
                    pState => new XiaHistoricalStateDefinition(pState.id,
                        pState.name, pState.commanderies?.Select(pCommandery =>
                            new XiaHistoricalCommanderyDefinition(
                                pCommandery.id, pCommandery.name,
                                pCommandery.cities)))));
            }
            catch (Exception pError)
            {
                pWarning?.Invoke("AW3 historical de jure catalog load failed: " +
                    pError.Message);
                return new XiaHistoricalDeJureCatalog(Array.Empty<
                    XiaHistoricalStateDefinition>());
            }
        }

        public static XiaHistoricalDeJureCatalog Empty()
        {
            return new XiaHistoricalDeJureCatalog(Array.Empty<
                XiaHistoricalStateDefinition>());
        }

        private sealed class CatalogDocument
        {
            public int schemaVersion { get; set; }
            public List<StateDocument> states { get; set; }
        }

        private sealed class StateDocument
        {
            public string id { get; set; }
            public string name { get; set; }
            public List<CommanderyDocument> commanderies { get; set; }
        }

        private sealed class CommanderyDocument
        {
            public string id { get; set; }
            public string name { get; set; }
            public List<string> cities { get; set; }
        }
    }
}
