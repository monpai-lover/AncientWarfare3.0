using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public static class CourtTemplateOfficerMigrationRules
    {
        public static Dictionary<string, long> Match(
            IEnumerable<CustomCourtOffice> pSource,
            IEnumerable<CustomCourtOffice> pTarget,
            IReadOnlyDictionary<string, long> pIncumbents)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            var used = new HashSet<long>();
            List<CustomCourtOffice> source = (pSource ??
                Enumerable.Empty<CustomCourtOffice>()).Where(o => o != null)
                .ToList();
            List<CustomCourtOffice> target = (pTarget ??
                Enumerable.Empty<CustomCourtOffice>()).Where(o => o != null)
                .ToList();
            foreach (CustomCourtOffice office in target)
            {
                if (pIncumbents == null || string.IsNullOrEmpty(office.Id))
                    continue;
                if (pIncumbents.TryGetValue(office.Id, out long actor) &&
                    actor >= 0 && used.Add(actor)) result[office.Id] = actor;
            }
            foreach (CustomCourtOffice oldOffice in source)
            {
                if (oldOffice == null || string.IsNullOrEmpty(oldOffice.Id) ||
                    !pIncumbents.TryGetValue(oldOffice.Id, out long actor) ||
                    actor < 0 || used.Contains(actor)) continue;
                CustomCourtOffice match = target.FirstOrDefault(next =>
                    next != null && !result.ContainsKey(next.Id) &&
                    next.Layer == oldOffice.Layer &&
                    next.Grade == oldOffice.Grade &&
                    next.MilitaryCapable == oldOffice.MilitaryCapable);
                if (match != null) { result[match.Id] = actor; used.Add(actor); }
            }
            return result;
        }
    }
}
