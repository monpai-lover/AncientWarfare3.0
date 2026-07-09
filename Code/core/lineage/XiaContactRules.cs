using System;

namespace AncientWarfare3.core.lineage
{
    public static class XiaContactRules
    {
        public const int LevelKnownXia = 1;
        public const int LevelAdoptCustoms = 2;
        public const float PolicyUnlockProgress = 100f;

        public const float BorderGain = 4f;
        public const float DiplomaticGain = 5f;
        public const float VassalGain = 9f;
        public const float OccupiedXiaCityGain = 8f;
        public const float MixedChildGain = 2f;
        public const float OfficialContactGain = 6f;

        private const int MaxOccupiedCitiesPerYear = 5;
        private const int MaxMixedChildEventsPerYear = 8;

        public static float CalculateYearlyGain(bool pBordersXia, bool pDiplomaticContact, bool pVassalContact,
            int pOccupiedXiaCityCount, int pMixedChildEvents, bool pOfficialContact = false)
        {
            float gain = 0f;
            if (pBordersXia) gain += BorderGain;
            if (pDiplomaticContact) gain += DiplomaticGain;
            if (pVassalContact) gain += VassalGain;
            if (pOfficialContact) gain += OfficialContactGain;
            gain += Math.Max(0, Math.Min(MaxOccupiedCitiesPerYear, pOccupiedXiaCityCount)) * OccupiedXiaCityGain;
            gain += Math.Max(0, Math.Min(MaxMixedChildEventsPerYear, pMixedChildEvents)) * MixedChildGain;
            return gain;
        }

        public static string BuildSourceMask(bool pBordersXia, bool pDiplomaticContact, bool pVassalContact,
            int pOccupiedXiaCityCount, int pMixedChildEvents, bool pOfficialContact = false)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (pBordersXia) parts.Add("border");
            if (pDiplomaticContact) parts.Add("diplomacy");
            if (pVassalContact) parts.Add("vassal");
            if (pOccupiedXiaCityCount > 0) parts.Add("occupation");
            if (pMixedChildEvents > 0) parts.Add("mixed");
            if (pOfficialContact) parts.Add("official");
            return string.Join(";", parts.ToArray());
        }

        public static string PrimaryReason(string pSourceMask)
        {
            if (ContainsSource(pSourceMask, "occupation")) return "xia_occupation_contact";
            if (ContainsSource(pSourceMask, "official")) return "xia_official_contact";
            if (ContainsSource(pSourceMask, "mixed")) return "xia_mixed_child";
            if (ContainsSource(pSourceMask, "vassal")) return "xia_vassal_contact";
            if (ContainsSource(pSourceMask, "diplomacy")) return "xia_diplomatic_contact";
            if (ContainsSource(pSourceMask, "border")) return "xia_border_contact";
            return "xia_contact";
        }

        private static bool ContainsSource(string pSourceMask, string pSource)
        {
            if (string.IsNullOrEmpty(pSourceMask) || string.IsNullOrEmpty(pSource)) return false;
            string[] parts = pSourceMask.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
                if (part == pSource)
                    return true;
            return false;
        }

        public static int LevelForProgress(float pProgress)
        {
            if (pProgress <= 0f) return 0;
            return pProgress >= PolicyUnlockProgress ? LevelAdoptCustoms : LevelKnownXia;
        }
    }
}
