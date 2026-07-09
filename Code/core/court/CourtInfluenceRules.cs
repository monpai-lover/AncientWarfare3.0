using System;
using System.Collections.Generic;
using System.Globalization;

namespace AncientWarfare3.core.court
{
    public static class CourtInfluenceRules
    {
        public static float InfluenceWeight(string layer, bool importantActor, int merit)
        {
            float baseWeight = layer switch
            {
                CourtOfficeLayer.Central => 6f,
                CourtOfficeLayer.Censor => 4.5f,
                CourtOfficeLayer.City => 2.5f,
                CourtOfficeLayer.Military => 3.5f,
                CourtOfficeLayer.Primitive => 1.5f,
                _ => 1f
            };
            if (importantActor) baseWeight += 1.5f;
            if (merit > 0) baseWeight += Math.Min(2f, merit / 25f);
            return baseWeight;
        }

        public static float Concentration(float dominantInfluence, float totalInfluence)
        {
            if (totalInfluence <= 0f || dominantInfluence <= 0f) return 0f;
            return (float)Math.Round(dominantInfluence / totalInfluence, 3);
        }

        public static bool ShouldTriggerStrongEvent(int yearsDominant, float dominantShare, bool crisis, bool weakKing)
        {
            if (dominantShare >= 0.60f && yearsDominant >= 6) return true;
            return crisis && weakKing && dominantShare >= 0.45f;
        }

        public static string DominantSchool(string encoded, string fallback)
        {
            string result = string.IsNullOrEmpty(fallback) ? CourtSchoolId.None : fallback;
            float best = -1f;
            foreach (KeyValuePair<string, float> pair in Decode(encoded))
            {
                if (pair.Value <= best) continue;
                best = pair.Value;
                result = pair.Key;
            }
            return result;
        }

        public static Dictionary<string, float> Decode(string encoded)
        {
            var result = new Dictionary<string, float>();
            if (string.IsNullOrEmpty(encoded)) return result;
            string[] parts = encoded.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                int idx = part.IndexOf('=');
                if (idx <= 0 || idx >= part.Length - 1) continue;
                string key = part.Substring(0, idx);
                string raw = part.Substring(idx + 1);
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) continue;
                result[key] = value;
            }
            return result;
        }
    }
}
