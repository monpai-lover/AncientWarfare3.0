using System;
using System.Collections.Generic;

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
            if (pOfficeId == CourtOfficeId.ImperialPhysician) return CourtSchoolId.Medical;
            if (pOfficeId == CourtOfficeId.ImperialAstrologer) return CourtSchoolId.YinYang;

            Dictionary<string, float> scores = BaseScores(pProfile);
            AddRolePreferences(scores, pOfficeId);
            if (scores.ContainsKey(pProfile.ExistingSchool)) scores[pProfile.ExistingSchool] += 8f;

            int start = (int)(Math.Abs(pProfile.ActorId) % Schools.Length);
            string best = Schools[start];
            float bestScore = float.MinValue;
            for (int offset = 0; offset < Schools.Length; offset++)
            {
                string school = Schools[(start + offset) % Schools.Length];
                float score = scores[school];
                if (score <= bestScore) continue;
                best = school;
                bestScore = score;
            }
            return best;
        }

        private static Dictionary<string, float> BaseScores(CourtCandidateProfile p)
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

        private static void AddRolePreferences(Dictionary<string, float> pScores, string pOfficeId)
        {
            switch (pOfficeId ?? "")
            {
                case CourtOfficeId.Marshal:
                case CourtOfficeId.Bingbu:
                    pScores[CourtSchoolId.Military] += 100f;
                    break;
                case CourtOfficeId.Hubu:
                    pScores[CourtSchoolId.Agrarian] += 45f;
                    pScores[CourtSchoolId.Merchant] += 45f;
                    break;
                case CourtOfficeId.Gongbu:
                    pScores[CourtSchoolId.Mohist] += 45f;
                    pScores[CourtSchoolId.Craftsman] += 45f;
                    break;
                case CourtOfficeId.Chancellor:
                case CourtOfficeId.Zhongshu:
                case CourtOfficeId.Shangshu:
                    pScores[CourtSchoolId.Ru] += 35f;
                    pScores[CourtSchoolId.Syncretist] += 20f;
                    break;
                case CourtOfficeId.Censor:
                case CourtOfficeId.Justice:
                case CourtOfficeId.Xingbu:
                    pScores[CourtSchoolId.Legalist] += 40f;
                    break;
                case CourtOfficeId.Libu:
                case CourtOfficeId.Ribu:
                case CourtOfficeId.Erudite:
                    pScores[CourtSchoolId.Ru] += 30f;
                    pScores[CourtSchoolId.Historian] += 25f;
                    break;
                case CourtOfficeId.Menxia:
                    pScores[CourtSchoolId.Logician] += 30f;
                    pScores[CourtSchoolId.Diplomat] += 25f;
                    break;
                case CourtOfficeId.Steward:
                    pScores[CourtSchoolId.Agrarian] += 35f;
                    pScores[CourtSchoolId.Merchant] += 25f;
                    break;
            }
        }
    }
}
