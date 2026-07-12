using System;

namespace AncientWarfare3.core.policy
{
    public static class SchoolInfluenceLabelRules
    {
        public static string Build(string pSchoolName, float pScore, float pShare)
        {
            string name = string.IsNullOrWhiteSpace(pSchoolName) ? "-" : pSchoolName.Trim();
            int score = Math.Max(0, (int)Math.Round(pScore));
            int percent = Math.Max(0, Math.Min(100, (int)Math.Round(pShare * 100f)));
            return name + "  " + score + "  " + percent + "%";
        }
    }

    public static class SchoolActorStandingRules
    {
        public const string Leader = "leader";
        public const string Core = "core";
        public const string Representative = "representative";

        public static string Resolve(int pIndex)
        {
            if (pIndex <= 0) return Leader;
            if (pIndex <= 2) return Core;
            return Representative;
        }
    }
}
