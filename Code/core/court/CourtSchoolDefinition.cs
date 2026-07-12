using System;

namespace AncientWarfare3.core.court
{
    public readonly struct CourtSchoolDirection
    {
        public readonly float Livelihood;
        public readonly float War;
        public readonly float Aggression;
        public readonly float Peace;
        public readonly float Order;
        public readonly float Commerce;
        public readonly float Technology;

        public CourtSchoolDirection(float livelihood, float war, float aggression, float peace,
            float order, float commerce, float technology)
        {
            Livelihood = livelihood;
            War = war;
            Aggression = aggression;
            Peace = peace;
            Order = order;
            Commerce = commerce;
            Technology = technology;
        }

        public bool IsBounded => InRange(Livelihood) && InRange(War) && InRange(Aggression) &&
                                 InRange(Peace) && InRange(Order) && InRange(Commerce) &&
                                 InRange(Technology);

        private static bool InRange(float pValue)
        {
            return pValue >= 0f && pValue <= 1f && !float.IsNaN(pValue) && !float.IsInfinity(pValue);
        }
    }

    public sealed class CourtSchoolDefinition
    {
        public string Id { get; }
        public string TraitId { get; }
        public string NameKey { get; }
        public string DescriptionKey { get; }
        public string IconPath { get; }
        public string ColorHex { get; }
        public CourtSchoolDirection Direction { get; }
        public string[] CompatibleOffices { get; }

        public CourtSchoolDefinition(string id, string traitId, string iconPath, string colorHex,
            CourtSchoolDirection direction, params string[] compatibleOffices)
        {
            Id = id ?? "";
            TraitId = traitId ?? "";
            NameKey = "aw_court_school_" + Id;
            DescriptionKey = NameKey + "_description";
            IconPath = iconPath ?? "";
            ColorHex = colorHex ?? "";
            Direction = direction;
            CompatibleOffices = compatibleOffices ?? Array.Empty<string>();
        }

        public bool IsComplete => !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(TraitId) &&
                                  !string.IsNullOrEmpty(NameKey) && !string.IsNullOrEmpty(DescriptionKey) &&
                                  !string.IsNullOrEmpty(IconPath) && IsColor(ColorHex) &&
                                  Direction.IsBounded;

        private static bool IsColor(string pValue)
        {
            if (string.IsNullOrEmpty(pValue) || pValue[0] != '#' ||
                (pValue.Length != 7 && pValue.Length != 9)) return false;
            for (int i = 1; i < pValue.Length; i++)
                if (!Uri.IsHexDigit(pValue[i])) return false;
            return true;
        }
    }
}
