using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public sealed class CourtOfficeDefinition
    {
        private readonly HashSet<string> _institutions;

        public CourtOfficeDefinition(string id, string layer, int grade,
            string preferredSchoolId, string localizationKey,
            bool militaryCapable, params string[] institutions)
        {
            Id = id ?? string.Empty;
            Layer = layer ?? string.Empty;
            Grade = Math.Max(0, grade);
            PreferredSchoolId = preferredSchoolId ?? string.Empty;
            LocalizationKey = localizationKey ?? string.Empty;
            MilitaryCapable = militaryCapable;
            _institutions = new HashSet<string>(
                institutions ?? Array.Empty<string>(),
                StringComparer.Ordinal);
        }

        public string Id { get; }
        public string Layer { get; }
        public int Grade { get; }
        public string PreferredSchoolId { get; }
        public string LocalizationKey { get; }
        public bool MilitaryCapable { get; }
        public IReadOnlyCollection<string> Institutions => _institutions;

        public bool AvailableIn(string institutionId)
        {
            return !string.IsNullOrEmpty(institutionId) &&
                   _institutions.Contains(institutionId);
        }
    }
}
