using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public readonly struct CourtSchoolIdentityProfile
    {
        public readonly long ActorId;
        public readonly float Stewardship;
        public readonly float Diplomacy;
        public readonly float Warfare;
        public readonly float Intelligence;
        public readonly string ExistingSchool;
        public readonly string ParentSchool;
        public readonly string CitySchool;

        public CourtSchoolIdentityProfile(long actorId, float stewardship, float diplomacy,
            float warfare, float intelligence, string existingSchool, string parentSchool,
            string citySchool)
        {
            ActorId = actorId;
            Stewardship = stewardship;
            Diplomacy = diplomacy;
            Warfare = warfare;
            Intelligence = intelligence;
            ExistingSchool = existingSchool ?? "";
            ParentSchool = parentSchool ?? "";
            CitySchool = citySchool ?? "";
        }
    }

    public static class CourtSchoolIdentityRules
    {
        public static string Resolve(CourtSchoolIdentityProfile pProfile)
        {
            string[] schools = CourtSchoolAssignmentRules.AllSchools();
            var valid = new HashSet<string>(schools, StringComparer.Ordinal);
            if (valid.Contains(pProfile.ExistingSchool)) return pProfile.ExistingSchool;

            float strongestSignal = Math.Max(Math.Max(pProfile.Stewardship, pProfile.Diplomacy),
                Math.Max(pProfile.Warfare, pProfile.Intelligence));
            if (strongestSignal < 5f && !valid.Contains(pProfile.ParentSchool) &&
                !valid.Contains(pProfile.CitySchool)) return CourtSchoolId.None;

            Dictionary<string, float> scores = BaseScores(pProfile);
            if (valid.Contains(pProfile.ParentSchool)) scores[pProfile.ParentSchool] += 10f;
            if (valid.Contains(pProfile.CitySchool)) scores[pProfile.CitySchool] += 6f;

            string best = CourtSchoolId.None;
            float bestScore = float.MinValue;
            foreach (string school in schools)
            {
                float score = scores[school] + StableJitter(pProfile.ActorId, school);
                if (score <= bestScore) continue;
                best = school;
                bestScore = score;
            }
            return best;
        }

        private static Dictionary<string, float> BaseScores(CourtSchoolIdentityProfile p)
        {
            return new Dictionary<string, float>
            {
                [CourtSchoolId.Ru] = p.Stewardship * 0.7f + p.Diplomacy * 0.5f + p.Intelligence * 0.3f,
                [CourtSchoolId.Legalist] = p.Stewardship * 0.7f + p.Warfare * 0.3f + p.Intelligence * 0.4f,
                [CourtSchoolId.Dao] = p.Intelligence * 0.7f + p.Diplomacy * 0.4f - p.Warfare * 0.2f,
                [CourtSchoolId.Mohist] = p.Intelligence * 0.7f + p.Stewardship * 0.6f + p.Warfare * 0.2f,
                [CourtSchoolId.Military] = p.Warfare * 1.2f + p.Intelligence * 0.3f,
                [CourtSchoolId.Diplomat] = p.Diplomacy * 1.2f + p.Intelligence * 0.3f,
                [CourtSchoolId.Agrarian] = p.Stewardship * 1.1f + p.Intelligence * 0.2f,
                [CourtSchoolId.YinYang] = p.Intelligence + p.Diplomacy * 0.3f,
                [CourtSchoolId.Logician] = p.Intelligence * 0.9f + p.Diplomacy * 0.6f,
                [CourtSchoolId.Medical] = p.Intelligence * 0.9f + p.Stewardship * 0.8f,
                [CourtSchoolId.Syncretist] = (p.Stewardship + p.Diplomacy + p.Warfare + p.Intelligence) * 0.35f,
                [CourtSchoolId.Merchant] = p.Stewardship * 0.7f + p.Diplomacy * 0.8f,
                [CourtSchoolId.Craftsman] = p.Stewardship * 0.7f + p.Intelligence * 0.8f,
                [CourtSchoolId.Historian] = p.Intelligence * 0.8f + p.Diplomacy * 0.5f + p.Stewardship * 0.3f
            };
        }

        private static float StableJitter(long pActorId, string pSchool)
        {
            unchecked
            {
                long hash = pActorId * 397L;
                foreach (char c in pSchool ?? "") hash = hash * 31L + c;
                return (Math.Abs(hash) % 100L) / 100f;
            }
        }
    }
}
