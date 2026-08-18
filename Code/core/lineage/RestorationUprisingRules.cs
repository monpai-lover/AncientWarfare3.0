using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct RestorationSeedScore
    {
        public RestorationSeedScore(long cityId, bool originalCapital,
            bool claimantPresent, int distanceSquared, float resentment,
            int population, int defenders)
        {
            CityId = cityId;
            long value = originalCapital ? 1_000_000_000L : 0L;
            if (claimantPresent) value += 100_000_000L;
            value += (long)Math.Round(Math.Max(0f, Math.Min(100f, resentment)) * 10_000f);
            value += Math.Max(0, Math.Min(10_000, population)) * 100L;
            value -= Math.Max(0, Math.Min(10_000, defenders)) * 25_000L;
            value -= Math.Max(0, Math.Min(1_000_000, distanceSquared));
            Value = value;
        }

        public long CityId { get; }
        public long Value { get; }
    }

    public static class RestorationUprisingRules
    {
        public const int MaxWorkItemsPerCampaignYear = 4;
        public const int MaxCandidatesPerWorkItem = 24;
        public const int MaxRecruitsPerWorkItem = 6;
        public const int MaxCandidatesPerCampaignYear = 96;
        public const int MaxRecruitsPerCampaignYear = 24;
        public const int MaxActiveRecruitsPerCampaign = 96;
        public const int DemobilizationBatchSize = 8;
        public const float MaximumEnlistmentAge = 65f;

        public static bool ShouldRunRecruitmentWorkItem(bool campaignActive,
            int completedWorkItems, int scannedCandidates, int recruitedActors)
        {
            return campaignActive &&
                   completedWorkItems < MaxWorkItemsPerCampaignYear &&
                   scannedCandidates < MaxCandidatesPerCampaignYear &&
                   recruitedActors < MaxRecruitsPerCampaignYear;
        }

        public static bool CanEnlist(bool originalEligible, bool protectedIdentity,
            bool male, bool adult, float age, bool alreadyWarrior)
        {
            return originalEligible && !protectedIdentity && male && adult &&
                   age < MaximumEnlistmentAge && !alreadyWarrior;
        }

        public static bool ShouldCountInitialSupporter(long candidateActorId,
            long claimantActorId)
        {
            return candidateActorId >= 0 &&
                   candidateActorId != claimantActorId;
        }

        public static int CompareSeeds(RestorationSeedScore left,
            RestorationSeedScore right)
        {
            int value = right.Value.CompareTo(left.Value);
            return value != 0 ? value : left.CityId.CompareTo(right.CityId);
        }

        public static bool ShouldDemobilizeActor(bool marked, bool living,
            bool sameKingdom, bool protectedIdentity)
        {
            return marked && living && sameKingdom && !protectedIdentity;
        }
    }
}
