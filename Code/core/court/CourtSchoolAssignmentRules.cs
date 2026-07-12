using System;

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

        public CourtCandidateProfile(long actorId, float stewardship, float diplomacy,
            float warfare, float intelligence, string existingSchool)
        {
            ActorId = actorId;
            Stewardship = stewardship;
            Diplomacy = diplomacy;
            Warfare = warfare;
            Intelligence = intelligence;
            ExistingSchool = existingSchool ?? "";
        }
    }

    public static class CourtSchoolAssignmentRules
    {
        private static readonly string[] Schools =
        {
            CourtSchoolId.Ru, CourtSchoolId.Legalist, CourtSchoolId.Dao, CourtSchoolId.Mohist,
            CourtSchoolId.Military, CourtSchoolId.Diplomat, CourtSchoolId.Agrarian,
            CourtSchoolId.YinYang, CourtSchoolId.Logician, CourtSchoolId.Medical,
            CourtSchoolId.Syncretist, CourtSchoolId.Merchant, CourtSchoolId.Craftsman,
            CourtSchoolId.Historian
        };

        public static string[] AllSchools() => (string[])Schools.Clone();

        public static string ResolveSchool(string pOfficeId, CourtCandidateProfile pProfile)
        {
            return CourtSchoolIdentityRules.Resolve(new CourtSchoolIdentityProfile(
                pProfile.ActorId, pProfile.Stewardship, pProfile.Diplomacy,
                pProfile.Warfare, pProfile.Intelligence, pProfile.ExistingSchool, "", ""));
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
