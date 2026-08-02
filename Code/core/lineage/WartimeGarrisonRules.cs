using System;

namespace AncientWarfare3.core.lineage
{
    public enum WartimeGarrisonDuty
    {
        Patrol,
        Defend
    }

    public static class WartimeGarrisonRules
    {
        public const int BaseTarget = 4;
        public const int PriorityTarget = 8;
        public const int EmergencyTarget = 12;
        public const int MaxCitiesPerWorkItem = 1;
        public const int MaxCandidatesScannedPerWorkItem = 24;
        public const int MaxRecruitsPerWorkItem = 4;
        public const int DemobilizationBatchSize = 8;
        public const int MaxThreatCandidatesPerProbe = 24;
        public const int MaxThreatProbeAdjacentChunks = 8;
        public const float MaximumEnlistmentAge = 65f;
        public const double ThreatProbeActorCooldownSeconds = 0.5d;
        public const double ThreatProbeCityCooldownSeconds = 0.15d;
        public const double BoundaryRefreshRetrySeconds = 8d;

        public static int TargetSize(bool atWar, bool capital,
            bool foreignBorder, bool underAttack)
        {
            if (!atWar) return 0;
            if (underAttack) return EmergencyTarget;
            return capital || foreignBorder ? PriorityTarget : BaseTarget;
        }

        public static int ScaleTarget(int pBaseTarget,
            float pGarrisonMultiplier)
        {
            if (pBaseTarget <= 0) return 0;
            float multiplier = Math.Max(1f,
                Math.Min(2f, pGarrisonMultiplier));
            return (int)Math.Ceiling(pBaseTarget * multiplier);
        }

        public static bool CanEnlist(bool originalEligible,
            bool protectedIdentity, bool localCitizen, bool civilian,
            float age)
        {
            return originalEligible && !protectedIdentity && localCitizen &&
                   civilian && age >= 0f && age < MaximumEnlistmentAge;
        }

        public static bool ShouldBlockOffensiveAssignment(
            bool activeGarrison)
        {
            return activeGarrison;
        }

        public static WartimeGarrisonDuty SelectDuty(
            bool cityHasDangerZone, bool cityIsGettingCaptured,
            bool hasDirectThreat, bool hasNearbyThreat,
            bool cityFrozenControlledByEnemy = false)
        {
            if (cityIsGettingCaptured || cityFrozenControlledByEnemy ||
                hasDirectThreat || hasNearbyThreat)
                return WartimeGarrisonDuty.Defend;
            return WartimeGarrisonDuty.Patrol;
        }

        public static bool ShouldPatrol(bool cityHasDangerZone,
            bool cityIsGettingCaptured, bool hasDirectThreat,
            bool hasNearbyThreat,
            bool cityFrozenControlledByEnemy = false)
        {
            return SelectDuty(cityHasDangerZone, cityIsGettingCaptured,
                hasDirectThreat, hasNearbyThreat,
                cityFrozenControlledByEnemy) ==
                   WartimeGarrisonDuty.Patrol;
        }

        public static bool ShouldRunThreatProbe(double now,
            double actorNextAllowed, double cityNextAllowed)
        {
            return now >= actorNextAllowed && now >= cityNextAllowed;
        }

        public static bool CanInspectThreatCandidate(int inspected)
        {
            return inspected >= 0 &&
                   inspected < MaxThreatCandidatesPerProbe;
        }

        public static int ThreatProbeChunkSlotCount(int adjacentChunkCount)
        {
            return 1 + Math.Min(MaxThreatProbeAdjacentChunks,
                Math.Max(0, adjacentChunkCount));
        }

        public static int NormalizeThreatProbeCursor(int currentCursor,
            int entryCount)
        {
            if (currentCursor < 0 || entryCount <= 0) return 0;
            int cursor = currentCursor % entryCount;
            return cursor < 0 ? cursor + entryCount : cursor;
        }

        public static int AdvanceThreatProbeUnitCursor(int currentCursor,
            int inspectedCount, int unitCount)
        {
            if (unitCount <= 0) return 0;
            int cursor = NormalizeThreatProbeCursor(currentCursor,
                unitCount);
            int inspected = Math.Min(MaxThreatCandidatesPerProbe,
                Math.Max(0, inspectedCount));
            long next = (long)cursor + inspected;
            return next >= unitCount ? 0 : (int)next;
        }

        public static bool ShouldAdvanceThreatProbeChunk(int currentCursor,
            int inspectedCount, int unitCount)
        {
            if (unitCount <= 0) return true;
            int cursor = NormalizeThreatProbeCursor(currentCursor,
                unitCount);
            int inspected = Math.Min(MaxThreatCandidatesPerProbe,
                Math.Max(0, inspectedCount));
            return (long)cursor + inspected >= unitCount;
        }

        public static bool ShouldRetryBoundaryRefresh(double now,
            double nextAllowed)
        {
            return now >= nextAllowed;
        }

        public static bool CanUsePatrolCandidate(bool exists,
            bool ground, bool liquid, bool ocean, bool lava, bool block)
        {
            return exists && ground && !liquid && !ocean && !lava &&
                   !block;
        }

        public static int RecruitmentNeed(int current, int target)
        {
            return Math.Min(MaxRecruitsPerWorkItem,
                Math.Max(0, target - Math.Max(0, current)));
        }

        public static int PatrolStartIndex(long actorId, int visit,
            int candidateCount)
        {
            if (candidateCount <= 0) return 0;
            long foldedActorId = actorId ^ actorId >> 32;
            long value = foldedActorId + (long)Math.Max(0, visit) * 7L;
            int result = (int)(value % candidateCount);
            return result < 0 ? result + candidateCount : result;
        }
    }
}
