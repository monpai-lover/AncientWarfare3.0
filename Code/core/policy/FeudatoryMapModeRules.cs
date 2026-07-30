using System;

namespace AncientWarfare3.core.policy
{
    public static class FeudatoryMapModeRules
    {
        private static readonly string[] Palette =
        {
            "#D94B4B", "#2FA7A0", "#D6A326", "#4E9A51",
            "#B45CB5", "#3D7FC4", "#D56A3A", "#6D78C8",
            "#A6A832", "#C64F7A", "#3188A8", "#7B9D3E"
        };

        private const int MinimumDistanceSquared = 9000;

        public static bool ShouldRender(bool pModeActive,
            bool pHasFeudatorySnapshot)
        {
            return pModeActive && pHasFeudatorySnapshot;
        }

        public static string BuildCityLabel(string pFeudatoryName,
            string pCityName)
        {
            string feudatory = (pFeudatoryName ?? "").Trim();
            string city = (pCityName ?? "").Trim();
            if (feudatory.Length == 0) feudatory = city;
            if (feudatory.Length == 0) return "";
            feudatory = feudatory.TrimEnd('\u85E9') + "\u85E9";
            return city.Length == 0 ? feudatory : feudatory + "-" + city;
        }

        public static bool ShouldReplaceNameplateAnchor(
            long pCurrentCityId, bool pCurrentIsSeat,
            bool pCurrentCenterVisible, int pCurrentZoneId,
            long pCandidateCityId, bool pCandidateIsSeat,
            bool pCandidateCenterVisible, int pCandidateZoneId)
        {
            if (pCandidateCityId < 0) return false;
            if (pCurrentCityId < 0) return true;
            if (pCurrentIsSeat != pCandidateIsSeat)
                return pCandidateIsSeat;
            if (pCurrentCenterVisible != pCandidateCenterVisible)
                return pCandidateCenterVisible;
            if (pCurrentCityId != pCandidateCityId)
                return pCandidateCityId < pCurrentCityId;
            return pCandidateZoneId < pCurrentZoneId;
        }

        public static string ColorHex(long pParentKingdomId,
            long pFeudatoryId, string pParentColorHex)
        {
            ParseHex(pParentColorHex, out int parentRed, out int parentGreen,
                out int parentBlue);
            var eligible = new int[Palette.Length];
            int eligibleCount = 0;
            for (int index = 0; index < Palette.Length; index++)
            {
                string candidate = Palette[index];
                ParseHex(candidate, out int red, out int green, out int blue);
                int distance = Square(red - parentRed) +
                               Square(green - parentGreen) +
                               Square(blue - parentBlue);
                if (distance >= MinimumDistanceSquared)
                    eligible[eligibleCount++] = index;
            }
            if (eligibleCount == 0) return Palette[0];

            int parentOffset = StableParentOffset(pParentKingdomId,
                eligibleCount);
            long feudatoryOffset = pFeudatoryId % eligibleCount;
            if (feudatoryOffset < 0) feudatoryOffset += eligibleCount;
            int selected = (parentOffset + (int)feudatoryOffset) % eligibleCount;
            return Palette[eligible[selected]];
        }

        private static int StableParentOffset(long pParentKingdomId,
            int pCount)
        {
            unchecked
            {
                ulong hash = (ulong)pParentKingdomId * 11400714819323198485UL;
                hash ^= hash >> 29;
                return (int)(hash % (ulong)pCount);
            }
        }

        private static void ParseHex(string pHex, out int pRed,
            out int pGreen, out int pBlue)
        {
            string value = (pHex ?? "").Trim().TrimStart('#');
            if (value.Length == 6 &&
                int.TryParse(value.Substring(0, 2),
                    System.Globalization.NumberStyles.HexNumber, null,
                    out pRed) &&
                int.TryParse(value.Substring(2, 2),
                    System.Globalization.NumberStyles.HexNumber, null,
                    out pGreen) &&
                int.TryParse(value.Substring(4, 2),
                    System.Globalization.NumberStyles.HexNumber, null,
                    out pBlue))
                return;
            pRed = 102;
            pGreen = 102;
            pBlue = 102;
        }

        private static int Square(int pValue)
        {
            return pValue * pValue;
        }
    }
}
