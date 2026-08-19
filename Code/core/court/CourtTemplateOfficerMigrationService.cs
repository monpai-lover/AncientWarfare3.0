using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal static class CourtTemplateOfficerMigrationService
    {
        public static bool TryMigrateLocal(Kingdom pKingdom, City pCity,
            CustomLocalCourtTemplate pSource,
            CustomLocalCourtTemplate pTarget)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom || pTarget == null) return false;
            Dictionary<string, long> incumbents = CourtService
                .GetActiveOfficers(pKingdom, 96)
                .Where(row => row != null && row.city_id == pCity.id &&
                    row.layer == CourtOfficeLayer.City && row.actor_id >= 0)
                .ToDictionary(row => row.office_id, row => row.actor_id,
                    System.StringComparer.Ordinal);
            Dictionary<string, long> assignments =
                CourtTemplateOfficerMigrationRules.Match(pSource?.Offices,
                    pTarget.Offices, incumbents);
            foreach (KeyValuePair<string, long> assignment in assignments)
            {
                Actor actor = World.world?.units?.get(assignment.Value);
                if (actor?.data == null) continue;
                if (incumbents.TryGetValue(assignment.Key,
                        out long existing) && existing == assignment.Value)
                    continue;
                CourtService.TryAssignLocalOfficer(actor, pKingdom, pCity,
                    assignment.Key, true);
            }
            return true;
        }

        public static bool TryMigrateCentral(Kingdom pKingdom,
            CustomCourtTemplate pSource, CustomCourtTemplate pTarget)
        {
            if (pKingdom?.data == null || pTarget == null) return false;
            Dictionary<string, long> incumbents = CourtService
                .GetActiveOfficers(pKingdom, 96)
                .Where(row => row != null && row.layer == CourtOfficeLayer.Central &&
                    row.actor_id >= 0)
                .ToDictionary(row => row.office_id, row => row.actor_id,
                    System.StringComparer.Ordinal);
            Dictionary<string, long> assignments =
                CourtTemplateOfficerMigrationRules.Match(pSource?.Offices,
                    pTarget.Offices, incumbents);
            foreach (KeyValuePair<string, long> assignment in assignments)
            {
                if (incumbents.TryGetValue(assignment.Key,
                        out long existing) && existing == assignment.Value)
                    continue;
                CourtService.TryManualAppointment(pKingdom.id,
                    assignment.Key, assignment.Value);
            }
            return true;
        }
    }
}
