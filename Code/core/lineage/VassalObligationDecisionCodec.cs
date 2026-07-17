using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class VassalObligationDecisionCodec
    {
        public static bool TryGet(string pData, long pSuzerainId, long pVassalId,
            out bool pDecision)
        {
            pDecision = false;
            string key = PairKey(pSuzerainId, pVassalId);
            foreach (string entry in (pData ?? "").Split(new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = entry.IndexOf('=');
                if (equals <= 0 || !string.Equals(entry.Substring(0, equals), key,
                        StringComparison.Ordinal)) continue;
                string value = entry.Substring(equals + 1);
                if (value != "0" && value != "1") return false;
                pDecision = value == "1";
                return true;
            }
            return false;
        }

        public static string Set(string pData, long pSuzerainId, long pVassalId,
            bool pDecision)
        {
            string key = PairKey(pSuzerainId, pVassalId);
            var entries = new List<string>();
            bool replaced = false;
            foreach (string entry in (pData ?? "").Split(new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = entry.IndexOf('=');
                if (equals > 0 && string.Equals(entry.Substring(0, equals), key,
                        StringComparison.Ordinal))
                {
                    if (!replaced) entries.Add(key + "=" + (pDecision ? "1" : "0"));
                    replaced = true;
                    continue;
                }
                entries.Add(entry);
            }
            if (!replaced) entries.Add(key + "=" + (pDecision ? "1" : "0"));
            return string.Join(";", entries.ToArray());
        }

        public static bool Resolve(string pData, long pWarId, long pSuzerainId,
            long pVassalId, int pEffectiveObligation, out bool pDecision,
            out string pUpdatedData)
        {
            if (TryGet(pData, pSuzerainId, pVassalId, out pDecision))
            {
                pUpdatedData = pData ?? "";
                return true;
            }
            int obligation = pEffectiveObligation < 0 ? 0 : pEffectiveObligation > 100
                ? 100
                : pEffectiveObligation;
            pDecision = StablePercentage(pWarId, pSuzerainId, pVassalId) < obligation;
            pUpdatedData = Set(pData, pSuzerainId, pVassalId, pDecision);
            return true;
        }

        public static int StablePercentage(long pWarId, long pSuzerainId, long pVassalId)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (ulong)pWarId) * 1099511628211UL;
                hash = (hash ^ (ulong)pSuzerainId) * 1099511628211UL;
                hash = (hash ^ (ulong)pVassalId) * 1099511628211UL;
                hash ^= hash >> 32;
                return (int)(hash % 100UL);
            }
        }

        private static string PairKey(long pSuzerainId, long pVassalId)
        {
            return pSuzerainId + ":" + pVassalId;
        }
    }
}
