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
                CustomCourtTemplate snapshot = instance.ResolvedSnapshot;
                List<CustomCourtOffice> offices = (snapshot?.Offices ??
                    new List<CustomCourtOffice>()).Where(item => item != null)
                    .ToList();
                IReadOnlyDictionary<string, int> ranks =
                    CustomCourtHierarchyLayoutRules.BuildRanks(offices,
                        snapshot?.Edges);
                return offices.OrderBy(item => ranks.TryGetValue(item.Id,
                        out int rank) ? rank : int.MaxValue)
                    .ThenBy(item => item.Grade)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => ToDefinition(item, institutionId)).ToList();
            }
            if (builtinProfile == null)
                return Array.Empty<CourtOfficeDefinition>();
            return builtinProfile.OfficeIdsForInstitution(institutionId)
                .Select(builtinProfile.FindOffice).Where(item => item != null)
                .ToList();
        }

        public IReadOnlyList<CourtOfficeDefinition> ResolveLocalGraph(
            string kingdomId, ICourtProfile builtinProfile,
            string institutionId, string localTemplateId)
        {
            CustomCourtInstance instance;
            if (_instances != null && _instances.TryGet(kingdomId,
                    out instance))
            {
                CustomLocalCourtTemplate local = instance.ResolvedSnapshot?
                    .LocalTemplates?.FirstOrDefault(item => item != null &&
                        string.Equals(item.Id, localTemplateId,
                            StringComparison.Ordinal));
                if (local != null)
                {
                    List<CustomCourtOffice> offices = (local.Offices ??
                        new List<CustomCourtOffice>()).Where(item =>
                        item != null && item.Layer == CourtOfficeLayer.City)
                        .ToList();
                    IReadOnlyDictionary<string, int> ranks =
                        CustomCourtHierarchyLayoutRules.BuildRanks(offices,
                            local.Edges);
                    return offices.OrderBy(item => ranks.TryGetValue(item.Id,
                            out int rank) ? rank : int.MaxValue)
                        .ThenBy(item => item.Grade)
                        .ThenBy(item => item.Id, StringComparer.Ordinal)
                        .Select(item => ToDefinition(item, institutionId))
                        .ToList();
                }
            }
            if (builtinProfile == null)
                return Array.Empty<CourtOfficeDefinition>();
            return builtinProfile.OfficeIdsForInstitution(institutionId)
                .Select(builtinProfile.FindOffice).Where(item => item != null &&
                    item.Layer == CourtOfficeLayer.City).ToList();
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
