using System;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.policy
{
    public static class SchoolMapModeRules
    {
        public const string NeutralHex = "#55585A";
        public const string DesaturatedHex = "#44484C";

        public static string OverviewHex(string pDominantSchoolId)
        {
            return CourtSchoolRegistry.Find(pDominantSchoolId)?.ColorHex ?? NeutralHex;
        }

        public static string FocusHex(string pSchoolId, float pShare)
        {
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(pSchoolId);
            float share = Clamp01(pShare);
            if (definition == null || share <= 0f) return DesaturatedHex;
            return Blend(DesaturatedHex, definition.ColorHex, 0.30f + share * 0.70f);
        }

        private static string Blend(string pFrom, string pTo, float pAmount)
        {
            Parse(pFrom, out int fromR, out int fromG, out int fromB);
            Parse(pTo, out int toR, out int toG, out int toB);
            float amount = Clamp01(pAmount);
            int r = (int)Math.Round(fromR + (toR - fromR) * amount);
            int g = (int)Math.Round(fromG + (toG - fromG) * amount);
            int b = (int)Math.Round(fromB + (toB - fromB) * amount);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static void Parse(string pHex, out int pR, out int pG, out int pB)
        {
            string hex = (pHex ?? NeutralHex).TrimStart('#');
            if (hex.Length < 6)
            {
                pR = pG = pB = 85;
                return;
            }
            pR = Convert.ToInt32(hex.Substring(0, 2), 16);
            pG = Convert.ToInt32(hex.Substring(2, 2), 16);
            pB = Convert.ToInt32(hex.Substring(4, 2), 16);
        }

        private static float Clamp01(float pValue)
        {
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }
}
