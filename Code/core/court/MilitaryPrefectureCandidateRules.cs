using System;

namespace AncientWarfare3.core.court
{
    public static class MilitaryPrefectureCandidateRules
    {
        public const string BuiltInTemplateId =
            CustomLocalGovernmentPresetRules.MilitaryTemplateId;

        public static bool IsMilitaryTemplate(string pTemplateId)
        {
            return string.Equals(pTemplateId, BuiltInTemplateId,
                StringComparison.Ordinal);
        }

        public static bool IsCandidate(bool pAlive, bool pValid,
            bool pOwnedByKingdom, string pTemplateId)
        {
            return pAlive && pValid && pOwnedByKingdom &&
                   IsMilitaryTemplate(pTemplateId);
        }
    }
}
