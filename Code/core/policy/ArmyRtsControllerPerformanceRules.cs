using System;

namespace AncientWarfare3.core.policy
{
    public enum ArmyRtsControllerPerformanceStage
    {
        Unknown = -1,
        Formation = 0,
        JobOwnership = 1,
        TargetFacts = 2,
        Mobilization = 3,
        Route = 4
    }

    public static class ArmyRtsControllerPerformanceRules
    {
        public const int StageCount = 5;

        public static ArmyRtsControllerPerformanceStage StageForId(string pId)
        {
            switch (pId)
            {
                case "formation":
                    return ArmyRtsControllerPerformanceStage.Formation;
                case "job_ownership":
                    return ArmyRtsControllerPerformanceStage.JobOwnership;
                case "target_facts":
                    return ArmyRtsControllerPerformanceStage.TargetFacts;
                case "mobilization":
                    return ArmyRtsControllerPerformanceStage.Mobilization;
                case "route":
                    return ArmyRtsControllerPerformanceStage.Route;
                default:
                    return ArmyRtsControllerPerformanceStage.Unknown;
            }
        }

        public static string Id(ArmyRtsControllerPerformanceStage pStage)
        {
            switch (pStage)
            {
                case ArmyRtsControllerPerformanceStage.Formation:
                    return "formation";
                case ArmyRtsControllerPerformanceStage.JobOwnership:
                    return "job_ownership";
                case ArmyRtsControllerPerformanceStage.TargetFacts:
                    return "target_facts";
                case ArmyRtsControllerPerformanceStage.Mobilization:
                    return "mobilization";
                case ArmyRtsControllerPerformanceStage.Route:
                    return "route";
                default:
                    return "unknown";
            }
        }

        public static bool IsValid(ArmyRtsControllerPerformanceStage pStage)
        {
            int index = (int)pStage;
            return index >= 0 && index < StageCount;
        }
    }
}
