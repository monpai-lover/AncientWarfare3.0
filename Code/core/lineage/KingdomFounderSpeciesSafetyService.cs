using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Keeps vanilla Kingdom founder-species presentation safe for old or
    /// partially-created kingdoms. Vanilla passes this field directly to
    /// AssetLibrary.get, which throws when the key is null.
    /// </summary>
    internal static class KingdomFounderSpeciesSafetyService
    {
        private const string FallbackAssetId = "human";
        private static readonly HashSet<long> ReportedFailures =
            new HashSet<long>();

        public static bool TryResolve(Kingdom pKingdom,
            out ActorAsset pAsset)
        {
            pAsset = null;
            if (pKingdom == null) return false;

            try
            {
                string current = pKingdom.data?.original_actor_asset;
                pAsset = FindAsset(current);
                if (pAsset != null)
                {
                    Persist(pKingdom, pAsset.id);
                    return true;
                }

                foreach (string candidate in CandidateIds(pKingdom))
                {
                    pAsset = FindAsset(candidate);
                    if (pAsset == null) continue;
                    Persist(pKingdom, pAsset.id);
                    return true;
                }
            }
            catch (Exception error)
            {
                ReportFailure(pKingdom, error);
            }

            pAsset = GetFallbackAsset();
            return pAsset != null;
        }

        public static void ClearRuntime()
        {
            ReportedFailures.Clear();
        }

        private static IEnumerable<string> CandidateIds(Kingdom pKingdom)
        {
            yield return pKingdom.king?.asset?.id;
            yield return pKingdom.capital?.leader?.asset?.id;
            yield return pKingdom.capital?.data?.original_actor_asset;
            yield return pKingdom.culture?.data?.original_actor_asset;

            if (pKingdom.units == null) yield break;
            foreach (Actor actor in pKingdom.units)
                yield return actor?.asset?.id;
        }

        private static ActorAsset FindAsset(string pAssetId)
        {
            if (KingdomFounderSpeciesSafetyRules.ShouldBypassVanillaLookup(
                    pAssetId)) return null;
            try { return AssetManager.actor_library.get(pAssetId); }
            catch (ArgumentNullException) { return null; }
            catch (Exception) { return null; }
        }

        private static ActorAsset GetFallbackAsset()
        {
            ActorAsset fallback = FindAsset(FallbackAssetId);
            if (fallback != null) return fallback;

            try
            {
                if (AssetManager.actor_library?.list == null) return null;
                foreach (ActorAsset asset in AssetManager.actor_library.list)
                    if (asset != null &&
                        KingdomFounderSpeciesSafetyRules.IsUsableAssetId(
                            asset.id)) return asset;
            }
            catch (Exception) { }
            return null;
        }

        private static void Persist(Kingdom pKingdom, string pAssetId)
        {
            if (pKingdom?.data == null ||
                !KingdomFounderSpeciesSafetyRules.IsUsableAssetId(pAssetId))
                return;
            if (pKingdom.data.original_actor_asset == pAssetId) return;
            pKingdom.data.original_actor_asset = pAssetId;
        }

        private static void ReportFailure(Kingdom pKingdom, Exception pError)
        {
            long id = pKingdom?.data?.id ?? -1L;
            if (id < 0L || !ReportedFailures.Add(id)) return;
            ModClass.LogWarning("Kingdom founder asset repair failed for " +
                id + ": " + pError.Message);
        }
    }
}
