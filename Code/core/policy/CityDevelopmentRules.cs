using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    public static class CityDevelopmentRules
    {
        public static float CalculateScore(int population, int zoneCount, int buildingCount, float techScore,
            float economyScore, float unrestRisk, bool nonCoreOrOccupied)
        {
            float popScore = Mathf.Clamp01(population / 180f);
            float zoneScore = Mathf.Clamp01(zoneCount / 25f);
            float buildingScore = Mathf.Clamp01(buildingCount / 30f);
            float scaleScore = popScore * 0.55f + zoneScore * 0.25f + buildingScore * 0.20f;

            float score = scaleScore * 0.35f +
                          Mathf.Clamp01(techScore) * 0.30f +
                          Mathf.Clamp01(economyScore) * 0.25f;

            float penalty = Mathf.Clamp01(unrestRisk) * 0.18f;
            if (nonCoreOrOccupied) penalty += 0.12f;
            return Mathf.Clamp01(score - penalty);
        }

        public static string ColorKeyForScore(float pScore)
        {
            float score = Mathf.Clamp01(pScore);
            if (score < 0.10f) return "development_0";
            if (score < 0.22f) return "development_1";
            if (score < 0.34f) return "development_2";
            if (score < 0.45f) return "development_3";
            if (score < 0.57f) return "development_4";
            if (score < 0.69f) return "development_5";
            if (score < 0.81f) return "development_6";
            if (score < 0.92f) return "development_7";
            return "development_8";
        }

        public static string HexForColorKey(string pKey)
        {
            switch (pKey ?? "")
            {
                case "development_0": return "#B3124B";
                case "development_1": return "#C7343A";
                case "development_2": return "#D85B2A";
                case "development_3": return "#E08226";
                case "development_4": return "#D7A928";
                case "development_5": return "#B6B23A";
                case "development_6": return "#7EA648";
                case "development_7": return "#4F8F45";
                case "development_8": return "#226B3A";
                default: return "#242424";
            }
        }

        public static float AverageScore(IEnumerable<float> pScores)
        {
            if (pScores == null) return 0f;
            float total = 0f;
            int count = 0;
            foreach (float score in pScores)
            {
                total += Mathf.Clamp01(score);
                count++;
            }
            return count == 0 ? 0f : total / count;
        }
    }
}
