using System;

namespace AncientWarfare3.core.lineage
{
    public static class RoyalRestorationRules
    {
        public const int MaxClaimGeneration = 3;
        public const int MaxAnnualCandidates = 8;
        public const int MaxAnnualStarts = 1;
        public const int MaxCampaignsPerYear = 4;
        public const int MaxCoreCandidates = 16;
        public const int MaxInitialDescendants = 128;
        public const int AiMinimumClaimStrength = 85;
        public const int MinimumInheritedClaimStrength = 40;

        public static bool CanInheritClaim(int parentGeneration, bool fatherHasClaim,
            bool childMale, bool childValid)
        {
            return fatherHasClaim && childMale && childValid &&
                   parentGeneration >= 0 && parentGeneration < MaxClaimGeneration;
        }

        public static int NextGeneration(int parentGeneration)
        {
            return parentGeneration >= 0 && parentGeneration < MaxClaimGeneration
                ? parentGeneration + 1
                : -1;
        }

        public static int InheritedClaimStrength(int anchorStrength, int generation)
        {
            if (generation < 1 || generation > MaxClaimGeneration) return 0;
            return Math.Max(MinimumInheritedClaimStrength, anchorStrength - generation * 15);
        }

        public static int InheritFromParentStrength(int parentStrength)
        {
            return Math.Max(MinimumInheritedClaimStrength, parentStrength - 15);
        }

        public static int ResolveAgnaticGeneration(long anchorActorId, long candidateActorId,
            Func<long, long> pFatherOf)
        {
            if (anchorActorId < 0 || candidateActorId < 0 || pFatherOf == null) return -1;
            long current = candidateActorId;
            for (int generation = 0; generation <= MaxClaimGeneration; generation++)
            {
                if (current == anchorActorId) return generation;
                current = pFatherOf(current);
                if (current < 0) return -1;
            }
            return -1;
        }

        public static bool ShouldCreateClaim(bool activeDuplicate, bool claimantEligible,
            int generation)
        {
            return !activeDuplicate && claimantEligible &&
                   generation >= 0 && generation <= MaxClaimGeneration;
        }

        public static string InheritedRestorationState(string pParentState)
        {
            return pParentState == "campaign" || pParentState == "suspended"
                ? "suspended"
                : "dormant";
        }

        public static bool ShouldStartAiCampaign(int claimStrength, bool claimantValid,
            bool oldKingdomDead, bool hasEligibleSeed, bool cooldownReady)
        {
            return claimStrength >= AiMinimumClaimStrength && claimantValid &&
                   oldKingdomDead && hasEligibleSeed && cooldownReady;
        }

        public static bool CanUseSeedCity(bool cityValid, bool oldCore,
            bool peacefulHostCity, bool ownerValid)
        {
            return cityValid && oldCore && !peacefulHostCity && ownerValid;
        }

        public static bool HasRecoveredCoreThreshold(int controlled, int total)
        {
            return total > 0 && controlled >= 0 && controlled * 100 >= total * 65;
        }

        public static bool CanLeaseOriginalKingdomId(long originalKingdomId,
            bool liveKingdomExists, bool archiveMarkedDead)
        {
            return originalKingdomId >= 0 && !liveKingdomExists && archiveMarkedDead;
        }

        public static bool ShouldSuppressNewKingdomEffects(bool restorationCreationActive,
            long requestedKingdomId, long actualKingdomId)
        {
            return restorationCreationActive && requestedKingdomId >= 0 &&
                   requestedKingdomId == actualKingdomId;
        }
    }

    public sealed class RestorationKingdomIdLease : IDisposable
    {
        public RestorationKingdomIdLease(long pKingdomId)
        {
            KingdomId = pKingdomId;
        }

        public long KingdomId { get; }
        public bool Consumed { get; private set; }
        public bool Disposed { get; private set; }

        public bool TryConsume(string pType, out long pKingdomId)
        {
            pKingdomId = -1L;
            if (Disposed || Consumed || pType != "kingdom" || KingdomId < 0) return false;
            Consumed = true;
            pKingdomId = KingdomId;
            return true;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
