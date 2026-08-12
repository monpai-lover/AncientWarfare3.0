using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum CityArmyPriority
    {
        Recapture = 0,
        Frontline = 1,
        War = 2,
        Reserve = 3
    }

    public readonly struct CityArmyReinforcementRequest
    {
        public CityArmyReinforcementRequest(long pArmyId, int pLiving,
            int pDesiredTarget, CityArmyPriority pPriority)
        {
            ArmyId = pArmyId;
            Living = Math.Max(0, pLiving);
            DesiredTarget = Math.Max(Living, pDesiredTarget);
            Priority = pPriority;
        }

        public long ArmyId { get; }
        public int Living { get; }
        public int DesiredTarget { get; }
        public CityArmyPriority Priority { get; }
    }

    public readonly struct CityArmyReinforcementAllocation
    {
        public CityArmyReinforcementAllocation(long pArmyId,
            int pApprovedTarget)
        {
            ArmyId = pArmyId;
            ApprovedTarget = Math.Max(0, pApprovedTarget);
        }

        public long ArmyId { get; }
        public int ApprovedTarget { get; }
    }

    public static class CityArmyReinforcementRules
    {
        public static int CityCapacity(int pPopulation,
            int pEffectiveWarriorSlots)
        {
            _ = pPopulation;
            return Math.Max(0, pEffectiveWarriorSlots);
        }

        public static int Shortage(int pLiving, int pApprovedTarget)
        {
            return Math.Max(0, Math.Max(0, pApprovedTarget) -
                               Math.Max(0, pLiving));
        }

        public static CityArmyReinforcementAllocation[] Allocate(
            int pCityCapacity,
            IReadOnlyList<CityArmyReinforcementRequest> pRequests)
        {
            if (pRequests == null || pRequests.Count == 0)
                return Array.Empty<CityArmyReinforcementAllocation>();

            var ordered = new List<CityArmyReinforcementRequest>(
                pRequests.Count);
            long existing = 0L;
            for (int i = 0; i < pRequests.Count; i++)
            {
                CityArmyReinforcementRequest request = pRequests[i];
                if (request.ArmyId < 0L) continue;
                ordered.Add(request);
                existing += request.Living;
            }

            ordered.Sort(CompareRequests);
            int available = (int)Math.Max(0L,
                (long)Math.Max(0, pCityCapacity) - existing);
            var result = new CityArmyReinforcementAllocation[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                CityArmyReinforcementRequest request = ordered[i];
                int granted = Math.Min(available, Shortage(request.Living,
                    request.DesiredTarget));
                available -= granted;
                result[i] = new CityArmyReinforcementAllocation(request.ArmyId,
                    request.Living + granted);
            }
            return result;
        }

        private static int CompareRequests(
            CityArmyReinforcementRequest pLeft,
            CityArmyReinforcementRequest pRight)
        {
            int priority = pLeft.Priority.CompareTo(pRight.Priority);
            return priority != 0 ? priority : pLeft.ArmyId.CompareTo(pRight.ArmyId);
        }
    }
}
