using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal static class LocalChiefOfficeResolver
    {
        internal static IReadOnlyList<string> ResolveOrderedSeats(
            Kingdom pKingdom, City pCity, int pCapacity)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom || pCapacity <= 0)
                return Array.Empty<string>();
            IReadOnlyList<string> custom = ResolveCustomOfficeIds(
                pKingdom, pCity);
            if (custom.Count > 0)
                return custom.Take(pCapacity).ToArray();
            string builtInChief = CourtService.ResolveBuiltInCityOffice(
                pKingdom, pCity);
            return Enumerable.Range(0, pCapacity)
                .Select(slot => LocalCourtOfficeRules.OfficeForSlot(
                    slot, builtInChief))
                .Where(id => !string.IsNullOrEmpty(id))
                .ToArray();
        }

        internal static string ResolveChiefOffice(Kingdom pKingdom,
            City pCity)
        {
            if (CustomCourtRuntime.TryGetLocalTemplate(pKingdom, pCity,
                    out CustomLocalCourtTemplate local) && local != null)
            {
                CustomLocalCourtTemplateRules.EnsureChiefOfficeId(local);
                string chief = CustomLocalCourtTemplateRules.
                    ResolveChiefOfficeId(local);
                if (!string.IsNullOrEmpty(chief)) return chief;
            }
            return CourtService.ResolveBuiltInCityOffice(pKingdom, pCity) ??
                   string.Empty;
        }

        private static IReadOnlyList<string> ResolveCustomOfficeIds(
            Kingdom pKingdom, City pCity)
        {
            if (!CustomCourtRuntime.TryGetLocalTemplate(pKingdom, pCity,
                    out CustomLocalCourtTemplate local) || local == null)
                return Array.Empty<string>();
            CustomLocalCourtTemplateRules.EnsureChiefOfficeId(local);
            List<CustomCourtOffice> offices = (local.Offices ??
                    new List<CustomCourtOffice>()).Where(office =>
                    office != null && office.Layer == CourtOfficeLayer.City &&
                    !string.IsNullOrEmpty(office.Id)).ToList();
            IReadOnlyDictionary<string, int> ranks =
                CustomCourtHierarchyLayoutRules.BuildRanks(offices,
                    local.Edges);
            string chiefOfficeId = CustomLocalCourtTemplateRules.
                ResolveChiefOfficeId(local);
            IEnumerable<CustomCourtOffice> ordered = offices.OrderBy(office =>
                    string.Equals(office.Id, chiefOfficeId,
                        StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(office => ranks.TryGetValue(office.Id,
                        out int rank) ? rank : int.MaxValue)
                .ThenBy(office => office.Grade)
                .ThenBy(office => office.Id, StringComparer.Ordinal);
            return ordered
                .SelectMany(office => Enumerable.Repeat(office.Id,
                    Math.Max(1, office.Slots)))
                .ToArray();
        }
    }
}
