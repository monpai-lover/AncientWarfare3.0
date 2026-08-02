using System;

namespace AncientWarfare3.core.lineage
{
    public enum ProvisionalRollbackAction
    {
        None = 0,
        RetryCleanup = 1,
        RetrySeedReturn = 2,
        Finalize = 3
    }

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
        public const int TreatyAnnexationDelayYears = 5;
        public const int MinimumSeedPopulation = 30;
        public const int MaxSeedResidentsInspected = 24;

        public static int EarliestAutonomousYear(int extinctionYear,
            string extinctionCause)
        {
            int delay = string.Equals(extinctionCause, "treaty_annexation",
                StringComparison.Ordinal) ? TreatyAnnexationDelayYears : 0;
            if (extinctionYear > int.MaxValue - delay) return int.MaxValue;
            return extinctionYear + delay;
        }

        public static bool IsAutonomousYearEligible(int currentYear,
            int earliestAutonomousYear)
        {
            return currentYear >= earliestAutonomousYear;
        }

        public static bool ShouldRollbackTreatyAnnexationMarker(
            bool transferCommitted)
        {
            return !transferCommitted;
        }

        public static int InheritedEarliestAutonomousYear(
            int parentEarliestYear, int inheritedFloorYear)
        {
            return Math.Max(parentEarliestYear, inheritedFloorYear);
        }

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

        public static bool ShouldStartAiCampaign(bool chaosPhase, int claimStrength, bool claimantValid,
            bool oldKingdomDead, bool hasEligibleSeed, bool cooldownReady)
        {
            return CanStartAutonomousCampaign(false, chaosPhase, false, claimStrength,
                claimantValid, oldKingdomDead, hasEligibleSeed, cooldownReady);
        }

        public static bool CanStartAutonomousCampaign(bool mandateExists,
            bool chaosPhase, bool playerRequested, int claimStrength, bool claimantValid,
            bool oldKingdomDead, bool hasEligibleSeed, bool cooldownReady,
            bool rebellionTriggered = false)
        {
            return !mandateExists && chaosPhase && claimantValid && oldKingdomDead &&
                   hasEligibleSeed && cooldownReady &&
                   (playerRequested || rebellionTriggered ||
                    claimStrength >= AiMinimumClaimStrength);
        }

        public static bool CanUseSeedCity(bool cityValid, bool oldCore,
            bool peacefulHostCity, bool ownerValid)
        {
            return cityValid && oldCore && !peacefulHostCity && ownerValid;
        }

        public static int MinimumRequiredSupporters(int defenders)
        {
            return Math.Max(6, Math.Max(0, defenders) + 2);
        }

        public static bool HasRequiredSupporters(int supporters, int defenders)
        {
            return supporters >= MinimumRequiredSupporters(defenders);
        }

        public static int SeedResidentsToInspect(int remainingBudget,
            int availableResidents)
        {
            return Math.Min(Math.Max(0, remainingBudget),
                Math.Max(0, availableResidents));
        }

        public static int RemainingSeedResidentInspectionBudget(
            int remainingBudget, int inspectedResidents)
        {
            return Math.Max(0, Math.Max(0, remainingBudget) -
                               Math.Max(0, inspectedResidents));
        }

        public static bool CanFinalizeProvisionalRollback(
            bool seedReturnedToOriginalOwner)
        {
            return seedReturnedToOriginalOwner;
        }

        public static bool CanAdvanceRestorationCampaign(string state)
        {
            return string.Equals(state, "uprising",
                StringComparison.Ordinal);
        }

        public static ProvisionalRollbackAction
            ResolveProvisionalRollbackAction(bool rollbackPending,
                bool physicalCleanupComplete,
                bool seedReturnedToOriginalOwner)
        {
            if (!rollbackPending) return ProvisionalRollbackAction.None;
            if (!physicalCleanupComplete)
                return ProvisionalRollbackAction.RetryCleanup;
            return seedReturnedToOriginalOwner
                ? ProvisionalRollbackAction.Finalize
                : ProvisionalRollbackAction.RetrySeedReturn;
        }

        public static bool IsUprisingPhysicalCleanupComplete(bool actorAlive,
            bool hasArmy, bool isWarrior)
        {
            return !actorAlive || (!hasArmy && !isWarrior);
        }

        public static bool ShouldRetainUprisingCleanupState(
            bool physicalCleanupComplete)
        {
            return !physicalCleanupComplete;
        }

        public static bool CanUseSeedCity(bool cityValid, bool oldCore,
            bool peacefulHostCity, bool ownerValid,
            bool activeOrFrozenCapture, int population, int supporters,
            int defenders)
        {
            return CanUseSeedCity(cityValid, oldCore, peacefulHostCity,
                       ownerValid) && !activeOrFrozenCapture &&
                   population >= MinimumSeedPopulation &&
                   HasRequiredSupporters(supporters, defenders);
        }

        public static bool HasRecoveredCoreThreshold(int controlled, int total)
        {
            return total > 0 && controlled >= 0 && controlled * 100 >= total * 65;
        }

        public static bool CanCompleteCampaign(bool hasRecoveredThreshold,
            bool kingdomAlive, bool hasCity, bool hasActiveWar)
        {
            return hasRecoveredThreshold && kingdomAlive && hasCity &&
                   !hasActiveWar;
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

        public static bool ShouldFoundRestorationCadetBranch(
            bool restorationActive, bool hasLineage, bool hasShi,
            bool isHistoricalFigure, bool isLineageRootFounder,
            bool alreadyFoundedForDestination)
        {
            return restorationActive && hasLineage && hasShi &&
                   !isHistoricalFigure && !isLineageRootFounder &&
                   !alreadyFoundedForDestination;
        }

        public static string ResolveRestoredKingdomName(
            string continuityName, string archiveName, string originalName,
            string boundStateName, string requestStateName)
        {
            string[] candidates =
            {
                continuityName, archiveName, originalName,
                boundStateName, requestStateName
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i] ?? "";
                if (StateNameRules.IsValid(candidate)) return candidate;
            }
            return "";
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
