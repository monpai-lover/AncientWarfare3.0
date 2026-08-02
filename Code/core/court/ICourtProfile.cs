using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public interface ICourtProfile
    {
        CourtProfileId Id { get; }
        string DefaultInstitutionId { get; }
        IReadOnlyList<CourtOfficeDefinition> Offices { get; }
        CourtOfficeDefinition FindOffice(string officeId);
        IReadOnlyList<string> OfficeIdsForInstitution(string institutionId);
        string ResolveInstitution(bool officeSystemUnlocked,
            bool electiveAdopted, bool feudalAdopted,
            bool royalDirectAdopted);
    }
}
