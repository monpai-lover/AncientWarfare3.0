using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public enum CustomCourtOfficeMigration
    {
        KeepIncumbent = 0,
        PreserveLegacy = 1,
        Vacate = 2
    }

    public static class CustomCourtApplicationRules
    {
        public static CustomCourtOfficeMigration ResolveMigration(
            bool officeExists, bool incumbentCompatible, bool hasIncumbent)
        {
            if (officeExists && incumbentCompatible)
                return CustomCourtOfficeMigration.KeepIncumbent;
            if (officeExists)
                return CustomCourtOfficeMigration.PreserveLegacy;
            return CustomCourtOfficeMigration.Vacate;
        }

        public static bool CanApply(bool snapshotValid,
            bool expectedRevisionMatches, bool actorUpdatesSucceeded)
        {
            return snapshotValid && expectedRevisionMatches &&
                actorUpdatesSucceeded;
        }

        public static bool ContainsOffice(CustomCourtTemplate template,
            string officeId)
        {
            if (template?.Offices == null || string.IsNullOrEmpty(officeId))
                return false;
            foreach (CustomCourtOffice office in template.Offices)
                if (office != null && string.Equals(office.Id, officeId,
                    StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
