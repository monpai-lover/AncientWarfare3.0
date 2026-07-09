using System.Collections.Generic;
using System.Globalization;

namespace AncientWarfare3.core.court
{
    public static class CourtStateCodec
    {
        public static string EncodeFactionCache(string[] schools, float[] values)
        {
            if (schools == null || values == null || schools.Length == 0 || values.Length == 0) return "";
            var parts = new List<string>();
            int count = schools.Length < values.Length ? schools.Length : values.Length;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(schools[i])) continue;
                if (values[i] <= 0f) continue;
                parts.Add(schools[i] + "=" + values[i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            return string.Join(";", parts.ToArray());
        }

        public static Dictionary<string, float> DecodeFactionCache(string raw)
        {
            return CourtInfluenceRules.Decode(raw);
        }
    }
}
