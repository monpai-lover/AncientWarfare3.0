using System;
using System.Globalization;

namespace AncientWarfare3.core.policy
{
    public static class ShiLineageMapModeRules
    {
        public const string NeutralHex = "#55585A";
        public const string DesaturatedHex = "#44484C";

        public static string OverviewHex(long pShiId)
        {
            if (pShiId < 0L) return NeutralHex;
            unchecked
            {
                ulong value = (ulong)pShiId + 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                int red = 72 + (int)(value & 0x7F);
                int green = 72 + (int)((value >> 8) & 0x7F);
                int blue = 72 + (int)((value >> 16) & 0x7F);
                return $"#{red:X2}{green:X2}{blue:X2}";
            }
        }

        public static string FocusHex(long pShiId, float pShare)
        {
            float share = Math.Max(0f, Math.Min(1f, pShare));
            if (pShiId < 0L || share <= 0f) return DesaturatedHex;
            return Blend(DesaturatedHex, OverviewHex(pShiId),
                0.30f + share * 0.70f);
        }

        private static string Blend(string pFrom, string pTo, float pAmount)
        {
            Parse(pFrom, out int fromRed, out int fromGreen, out int fromBlue);
            Parse(pTo, out int toRed, out int toGreen, out int toBlue);
            float amount = Math.Max(0f, Math.Min(1f, pAmount));
            int red = (int)Math.Round(fromRed + (toRed - fromRed) * amount);
            int green = (int)Math.Round(fromGreen + (toGreen - fromGreen) * amount);
            int blue = (int)Math.Round(fromBlue + (toBlue - fromBlue) * amount);
            return $"#{red:X2}{green:X2}{blue:X2}";
        }

        private static void Parse(string pHex, out int pRed, out int pGreen,
            out int pBlue)
        {
            string value = (pHex ?? NeutralHex).Trim().TrimStart('#');
            if (value.Length != 6 ||
                !int.TryParse(value.Substring(0, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out pRed) ||
                !int.TryParse(value.Substring(2, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out pGreen) ||
                !int.TryParse(value.Substring(4, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out pBlue))
                pRed = pGreen = pBlue = 85;
        }
    }
}
