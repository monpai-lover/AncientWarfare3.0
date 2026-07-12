namespace AncientWarfare3.core.court
{
    public readonly struct CourtSchoolIdentityProfile
    {
        public CourtSchoolIdentityProfile(string pExistingSchool,
            bool pHasAuthoritativeMembership)
        {
            ExistingSchool = pExistingSchool ?? "";
            HasAuthoritativeMembership = pHasAuthoritativeMembership;
        }

        public string ExistingSchool { get; }
        public bool HasAuthoritativeMembership { get; }
    }

    public static class CourtSchoolIdentityRules
    {
        public static string Resolve(CourtSchoolIdentityProfile pProfile)
        {
            return !pProfile.HasAuthoritativeMembership ||
                   CourtSchoolRegistry.Find(pProfile.ExistingSchool) == null
                ? CourtSchoolId.None
                : pProfile.ExistingSchool;
        }
    }
}
