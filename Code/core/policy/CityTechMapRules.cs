using UnityEngine;

namespace AncientWarfare3.core.policy
{
    public static class CityTechMapRules
    {
        private const float LATE_WORLD_RELATIVE_MIN = 0.55f;
        private const float LATE_WORLD_RELATIVE_RANGE = 0.08f;

        public static float CalculateDevelopmentScore(float adoptedScore, int totalTechCount)
        {
            if (totalTechCount <= 0) return 0f;
            return Mathf.Clamp01(adoptedScore / totalTechCount);
        }

        public static float CalculateVisibleScore(float pRawScore, float pMinScore, float pMaxScore)
        {
            float raw = Mathf.Clamp01(pRawScore);
            float min = Mathf.Clamp01(Mathf.Min(pMinScore, pMaxScore));
            float max = Mathf.Clamp01(Mathf.Max(pMinScore, pMaxScore));
            float range = max - min;

            if (min >= LATE_WORLD_RELATIVE_MIN && range >= LATE_WORLD_RELATIVE_RANGE)
                return Mathf.Clamp01((raw - min) / range);

            return raw;
        }

        public static string ColorKeyForScore(float pScore)
        {
            float score = Mathf.Clamp01(pScore);
            if (score < 0.03f) return "tech_0";
            if (score < 0.15f) return "tech_1";
            if (score < 0.28f) return "tech_2";
            if (score < 0.40f) return "tech_3";
            if (score < 0.52f) return "tech_4";
            if (score < 0.64f) return "tech_5";
            if (score < 0.76f) return "tech_6";
            if (score < 0.88f) return "tech_7";
            return "tech_8";
        }

        public static string HexForColorKey(string pKey)
        {
            switch (pKey)
            {
                case "tech_0": return "#B3124B";
                case "tech_1": return "#C7343A";
                case "tech_2": return "#D85B2A";
                case "tech_3": return "#E08226";
                case "tech_4": return "#D7A928";
                case "tech_5": return "#B6B23A";
                case "tech_6": return "#7EA648";
                case "tech_7": return "#4F8F45";
                case "tech_8": return "#226B3A";
                default: return "#777777";
            }
        }
    }
}
