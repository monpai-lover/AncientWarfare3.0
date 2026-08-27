using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.county
{
    public enum CountyRenameValidationResult
    {
        Success = 0,
        Empty = 1,
        Duplicate = 2
    }

    public sealed class CountyRenameEntry
    {
        public CountyRenameEntry(long pCountyId, long pRegionId,
            string pName, bool pActive)
        {
            CountyId = pCountyId;
            RegionId = pRegionId;
            Name = pName ?? string.Empty;
            Active = pActive;
        }

        public long CountyId { get; }
        public long RegionId { get; }
        public string Name { get; }
        public bool Active { get; }
    }

    public static class CountyRenameRules
    {
        private const string CountySuffix = "县";

        public static string NormalizeName(string pName)
        {
            string name = (pName ?? string.Empty).Trim();
            if (name.Length == 0) return string.Empty;
            return name.EndsWith(CountySuffix, StringComparison.Ordinal)
                ? name
                : name + CountySuffix;
        }

        public static CountyRenameValidationResult Validate(string pName,
            long pCountyId, long pRegionId,
            IEnumerable<CountyRenameEntry> pEntries,
            out string pNormalizedName)
        {
            pNormalizedName = NormalizeName(pName);
            if (pNormalizedName.Length == 0)
                return CountyRenameValidationResult.Empty;
            foreach (CountyRenameEntry entry in pEntries ??
                     Array.Empty<CountyRenameEntry>())
            {
                if (entry == null || !entry.Active ||
                    entry.CountyId == pCountyId ||
                    entry.RegionId != pRegionId) continue;
                if (string.Equals(NormalizeName(entry.Name), pNormalizedName,
                        StringComparison.Ordinal))
                    return CountyRenameValidationResult.Duplicate;
            }
            return CountyRenameValidationResult.Success;
        }
    }
}
