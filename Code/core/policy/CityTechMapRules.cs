using UnityEngine;

namespace AncientWarfare3.core.policy
{
    public static class CityTechMapRules
    {
        public static float CalculateDevelopmentScore(float adoptedScore, int totalTechCount)
        {
            if (totalTechCount <= 0) return 0f;
            return Mathf.Clamp01(adoptedScore / totalTechCount);
        }

        public static string ColorKeyForScore(float pScore)
        {
            float score = Mathf.Clamp01(pScore);
            if (score < 0.05f) return "tech_0";
            if (score < 0.22f) return "tech_1";
            if (score < 0.45f) return "tech_2";
            if (score < 0.70f) return "tech_3";
            if (score < 0.95f) return "tech_4";
            return "tech_5";
        }

        public static string HexForColorKey(string pKey)
        {
            switch (pKey)
            {
                case "tech_0": return "#B33A2E";
                case "tech_1": return "#C96B2C";
                case "tech_2": return "#C9A42C";
                case "tech_3": return "#9CBF45";
                case "tech_4": return "#74A84A";
                case "tech_5": return "#2F9B57";
                default: return "#777777";
            }
        }
    }
}
