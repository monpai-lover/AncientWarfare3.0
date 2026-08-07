using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public sealed class WesternCourtProfile : ICourtProfile
    {
        private static readonly string[] BaseInstitutions =
        {
            CourtInstitutionId.WesternBureaucratic,
            CourtInstitutionId.WesternFeudalBureaucratic
        };

        private static readonly string[] AdvancedInstitutions =
        {
            CourtInstitutionId.WesternFeudalBureaucratic
        };

        private static readonly CourtOfficeDefinition[] Definitions =
        {
            Office(CourtOfficeId.WestExecutive, CourtOfficeLayer.Central, 10,
                CourtSchoolId.Diplomat, false, BaseInstitutions),
            Office(CourtOfficeId.WestSenateElder, CourtOfficeLayer.Central, 20,
                CourtSchoolId.Historian, false, BaseInstitutions),
            Office(CourtOfficeId.WestHighPriest, CourtOfficeLayer.Central, 20,
                CourtSchoolId.Ru, false, BaseInstitutions),
            Office(CourtOfficeId.WestFieldGeneral, CourtOfficeLayer.Military,
                20, CourtSchoolId.Military, true, BaseInstitutions),
            Office(CourtOfficeId.WestMayor, CourtOfficeLayer.City, 30,
                CourtSchoolId.Agrarian, false, BaseInstitutions),
            Office(CourtOfficeId.WestHighJustice, CourtOfficeLayer.Central, 10,
                CourtSchoolId.Legalist, false, AdvancedInstitutions),
            Office(CourtOfficeId.WestTreasurer, CourtOfficeLayer.Central, 10,
                CourtSchoolId.Merchant, false, AdvancedInstitutions),
            Office(CourtOfficeId.WestPalaceSteward, CourtOfficeLayer.Central,
                20, CourtSchoolId.Agrarian, false, AdvancedInstitutions),
            Office(CourtOfficeId.WestRoyalChamberlain,
                CourtOfficeLayer.Central, 20, CourtSchoolId.Diplomat, false,
                AdvancedInstitutions),
            Office(CourtOfficeId.WestMarshal, CourtOfficeLayer.Military, 10,
                CourtSchoolId.Military, true, AdvancedInstitutions),
            Office(CourtOfficeId.WestSecretary, CourtOfficeLayer.Central, 20,
                CourtSchoolId.Historian, false, AdvancedInstitutions),
            Office(CourtOfficeId.WestCount, CourtOfficeLayer.City, 20,
                CourtSchoolId.Agrarian, false, AdvancedInstitutions)
        };

        private static readonly Dictionary<string, CourtOfficeDefinition> ById =
            Definitions.ToDictionary(p => p.Id, StringComparer.Ordinal);

        public CourtProfileId Id => CourtProfileId.Western;
        public string DefaultInstitutionId =>
            CourtInstitutionId.WesternPrimitive;
        public IReadOnlyList<CourtOfficeDefinition> Offices => Definitions;

        public CourtOfficeDefinition FindOffice(string officeId)
        {
            return !string.IsNullOrEmpty(officeId) &&
                   ById.TryGetValue(officeId, out CourtOfficeDefinition value)
                ? value
                : null;
        }

        public IReadOnlyList<string> OfficeIdsForInstitution(
            string institutionId)
        {
            return Definitions.Where(p => p.AvailableIn(institutionId))
                .Select(p => p.Id).ToArray();
        }

        public string ResolveInstitution(bool officeSystemUnlocked,
            bool advancedOfficeSystemUnlocked)
        {
            if (!officeSystemUnlocked)
                return CourtInstitutionId.WesternPrimitive;
            return advancedOfficeSystemUnlocked
                ? CourtInstitutionId.WesternFeudalBureaucratic
                : CourtInstitutionId.WesternBureaucratic;
        }

        private static CourtOfficeDefinition Office(string id, string layer,
            int grade, string school, bool military,
            params string[] institutions)
        {
            return new CourtOfficeDefinition(id, layer, grade, school,
                "aw_court_office_" + id, military, institutions);
        }
    }
}
