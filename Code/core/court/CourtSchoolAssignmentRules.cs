using System;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public readonly struct CourtCandidateProfile
    {
        public readonly long ActorId;
        public readonly float Stewardship;
        public readonly float Diplomacy;
        public readonly float Warfare;
        public readonly float Intelligence;
        public readonly string ExistingSchool;
        public readonly bool HasAuthoritativeMembership;

        public CourtCandidateProfile(long actorId, float stewardship, float diplomacy,
            float warfare, float intelligence, string existingSchool,
            bool hasAuthoritativeMembership = false)
        {
            ActorId = actorId;
            Stewardship = stewardship;
            Diplomacy = diplomacy;
            Warfare = warfare;
            Intelligence = intelligence;
            ExistingSchool = existingSchool ?? "";
            HasAuthoritativeMembership = hasAuthoritativeMembership;
        }
    }

    public static class CourtSchoolAssignmentRules
    {
        public static string[] AllSchools() =>
            Array.ConvertAll(CourtSchoolRegistry.All.ToArray(), p => p.Id);

        public static string ResolveSchool(string pOfficeId, CourtCandidateProfile pProfile)
        {
            return CourtSchoolIdentityRules.Resolve(
                new CourtSchoolIdentityProfile(pProfile.ExistingSchool,
                    pProfile.HasAuthoritativeMembership));
        }

        public static float CompatibilityBonus(string pOfficeId, string pSchoolId)
        {
            switch (pOfficeId ?? "")
            {
                case CourtOfficeId.ImperialPhysician:
                    return pSchoolId == CourtSchoolId.Medical ? 12f : 0f;
                case CourtOfficeId.ImperialAstrologer:
                    return pSchoolId == CourtSchoolId.YinYang ? 12f : 0f;
                case CourtOfficeId.Marshal:
                case CourtOfficeId.Bingbu:
                    return pSchoolId == CourtSchoolId.Military ? 8f : 0f;
                case CourtOfficeId.Hubu:
                    return pSchoolId == CourtSchoolId.Agrarian || pSchoolId == CourtSchoolId.Merchant ? 6f : 0f;
                case CourtOfficeId.Gongbu:
                    return pSchoolId == CourtSchoolId.Mohist || pSchoolId == CourtSchoolId.Craftsman ? 6f : 0f;
                default:
                    return 0f;
            }
        }
    }
}
