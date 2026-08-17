using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public sealed class CourtDefinitionResolver
    {
        private readonly CustomCourtInstanceService _instances;

        public CourtDefinitionResolver(CustomCourtInstanceService instances)
        {
            _instances = instances;
        }

        public CourtOfficeDefinition Resolve(string kingdomId,
            ICourtProfile builtinProfile, string institutionId, string officeId)
        {
            CustomCourtInstance instance;
            if (_instances != null && _instances.TryGet(kingdomId, out instance))
            {
                CustomCourtOffice custom = instance.ResolvedSnapshot?.Offices?
                    .FirstOrDefault(item => item != null &&
                        string.Equals(item.Id, officeId,
                            StringComparison.Ordinal));
                if (custom != null)
                    return ToDefinition(custom, institutionId);
            }
            CourtOfficeDefinition builtin = builtinProfile?.FindOffice(officeId);
            if (builtin == null || string.IsNullOrEmpty(institutionId) ||
                builtin.AvailableIn(institutionId))
                return builtin;
            return null;
        }

        public IReadOnlyList<CourtOfficeDefinition> ResolveGraph(
            string kingdomId, ICourtProfile builtinProfile,
            string institutionId)
        {
            CustomCourtInstance instance;
            if (_instances != null && _instances.TryGet(kingdomId, out instance))
            {
                return (instance.ResolvedSnapshot?.Offices ??
                    new List<CustomCourtOffice>()).Where(item => item != null)
                    .OrderBy(item => item.Layout?.Lane ?? 0)
                    .ThenBy(item => item.Layout?.Y ?? 0f)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => ToDefinition(item, institutionId)).ToList();
            }
            if (builtinProfile == null)
                return Array.Empty<CourtOfficeDefinition>();
            return builtinProfile.OfficeIdsForInstitution(institutionId)
                .Select(builtinProfile.FindOffice).Where(item => item != null)
                .ToList();
        }

        private static CourtOfficeDefinition ToDefinition(
            CustomCourtOffice office, string institutionId)
        {
            return new CourtOfficeDefinition(office.Id, office.Layer,
                office.Grade, office.PreferredSchoolId,
                "aw_custom_court_office_" + office.Id,
                office.MilitaryCapable, institutionId ?? string.Empty);
        }
    }
}
