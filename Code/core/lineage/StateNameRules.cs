using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class StateNameRules
    {
        private static readonly string[] ForbiddenSuffixes =
        {
            "共和国", "共和國", "帝国", "帝國", "国", "國", "朝"
        };

        public static bool IsValid(string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            string value = pValue.Trim();
            if (!string.Equals(value, pValue, StringComparison.Ordinal)) return false;
            if (string.Equals(value, "name", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char character in value)
                if (char.IsWhiteSpace(character)) return false;
            if (LooksLikeLocalizationKey(value)) return false;
            foreach (string suffix in ForbiddenSuffixes)
                if (value.EndsWith(suffix, StringComparison.Ordinal)) return false;
            return true;
        }

        public static int StableStartIndex(int pLength, long shiId,
            long founderActorId, long originKingdomId)
        {
            if (pLength <= 0) return -1;
            ulong mixed = Mix(unchecked((ulong)shiId) ^ 0x9E3779B97F4A7C15UL);
            mixed = Mix(mixed ^ unchecked((ulong)founderActorId) ^ 0xBF58476D1CE4E5B9UL);
            mixed = Mix(mixed ^ unchecked((ulong)originKingdomId) ^ 0x94D049BB133111EBUL);
            return (int)(mixed % (ulong)pLength);
        }

        public static string SelectFirstAvailable(IReadOnlyList<string> pPool,
            ISet<string> pActiveNames, long shiId, long founderActorId,
            long originKingdomId)
        {
            if (pPool == null || pPool.Count == 0) return "";
            int start = StableStartIndex(pPool.Count, shiId,
                founderActorId, originKingdomId);
            string firstValid = "";
            for (int offset = 0; offset < pPool.Count; offset++)
            {
                string candidate = pPool[(start + offset) % pPool.Count] ?? "";
                if (!IsValid(candidate)) continue;
                if (firstValid.Length == 0) firstValid = candidate;
                if (pActiveNames == null || !pActiveNames.Contains(candidate)) return candidate;
            }
            return firstValid;
        }

        public static string ResolveBoundName(string pBoundName,
            IReadOnlyList<string> pPool, ISet<string> pActiveNames,
            long shiId, long founderActorId, long originKingdomId)
        {
            string bound = pBoundName ?? "";
            return IsValid(bound)
                ? bound
                : SelectFirstAvailable(pPool, pActiveNames, shiId,
                    founderActorId, originKingdomId);
        }

        public static string ResolveRestorationStateName(string pBoundName,
            string pRequestSnapshot)
        {
            string bound = pBoundName ?? "";
            if (IsValid(bound)) return bound;
            string requested = pRequestSnapshot ?? "";
            return IsValid(requested) ? requested : "";
        }

        public static string ResolvePreferredBoundName(string pBoundName,
            string pPreferredName)
        {
            string preferred = pPreferredName ?? "";
            if (IsValid(preferred)) return preferred;
            string bound = pBoundName ?? "";
            return IsValid(bound) ? bound : "";
        }

        public static string ResolveInitialBoundName(string pBoundName,
            string pPreferredName, string pCurrentKingdomName)
        {
            string preferred = pPreferredName ?? "";
            if (IsValid(preferred)) return preferred;
            string bound = pBoundName ?? "";
            if (IsValid(bound)) return bound;
            string current = pCurrentKingdomName ?? "";
            return IsValid(current) ? current : "";
        }

        public static bool IsSameShiContinuity(long pCurrentShiId, long pNewShiId)
        {
            return pCurrentShiId >= 0 && pCurrentShiId == pNewShiId;
        }

        public static bool IsDynasticContinuity(long pCurrentShiId,
            long pNewShiId, long pCurrentLineageId, long pNewLineageId,
            long pNewOriginKingdomId, long pInheritedKingdomId,
            string pNewSourceType, IReadOnlyList<long> pNewParentShiIds)
        {
            if (IsSameShiContinuity(pCurrentShiId, pNewShiId)) return true;
            if (pCurrentShiId < 0 || pNewShiId < 0 ||
                pCurrentLineageId < 0 ||
                pCurrentLineageId != pNewLineageId ||
                pInheritedKingdomId < 0 ||
                pNewOriginKingdomId != pInheritedKingdomId ||
                !string.Equals(pNewSourceType, "feudatory",
                    StringComparison.OrdinalIgnoreCase)) return false;
            int count = pNewParentShiIds == null
                ? 0
                : pNewParentShiIds.Count;
            for (int i = 0; i < count; i++)
                if (pNewParentShiIds[i] == pCurrentShiId) return true;
            return false;
        }

        public static bool ShouldSkipInitialStateBinding(
            bool hasCurrentDynasty, bool hasHistoricalPreferredName)
        {
            return hasCurrentDynasty && !hasHistoricalPreferredName;
        }

        public static bool ShouldProjectDynasticStateName(
            bool newDynastyCreated, bool isEmpireRank,
            bool changedRulingShi, bool hasExistingBoundStateName)
        {
            return ShouldProjectDynasticStateName(newDynastyCreated,
                isEmpireRank, changedRulingShi, hasExistingBoundStateName,
                isActiveMandate: false);
        }

        public static bool ShouldProjectDynasticStateName(
            bool newDynastyCreated, bool isEmpireRank,
            bool changedRulingShi, bool hasExistingBoundStateName,
            bool isActiveMandate)
        {
            return !isActiveMandate && newDynastyCreated && isEmpireRank &&
                   changedRulingShi && hasExistingBoundStateName;
        }

        private static bool LooksLikeLocalizationKey(string pValue)
        {
            if (pValue.StartsWith("aw_", StringComparison.OrdinalIgnoreCase) ||
                pValue.StartsWith("name_", StringComparison.OrdinalIgnoreCase) ||
                pValue.StartsWith("locale_", StringComparison.OrdinalIgnoreCase)) return true;
            if (pValue.IndexOf('$') >= 0 || pValue.IndexOf('#') >= 0) return true;
            bool hasAsciiLetter = false;
            foreach (char character in pValue)
            {
                if (character >= 'A' && character <= 'Z' ||
                    character >= 'a' && character <= 'z')
                    hasAsciiLetter = true;
            }
            return hasAsciiLetter && pValue.IndexOf('_') >= 0;
        }

        private static ulong Mix(ulong pValue)
        {
            unchecked
            {
                pValue ^= pValue >> 30;
                pValue *= 0xBF58476D1CE4E5B9UL;
                pValue ^= pValue >> 27;
                pValue *= 0x94D049BB133111EBUL;
                return pValue ^ (pValue >> 31);
            }
        }
    }
}
