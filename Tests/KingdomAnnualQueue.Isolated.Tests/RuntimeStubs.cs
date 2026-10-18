// Annual scheduling, stage progression, deferred coalescing and retry are real production code.
// Native world lookup and the independent annual subsystems are controllable boundaries.
using System;
using System.Collections.Generic;
namespace AncientWarfare3.core.court { internal sealed class NamespaceMarker { } }
namespace AncientWarfare3.core.performance { internal sealed class NamespaceMarker { } }
namespace UnityEngine { internal static class Time { public static int frameCount; } }
internal sealed class Kingdom
{
    public object data = new();
    public long Id;
    public long id => Id;
    public bool isRekt() => data == null;
    public bool isNeutral() => false;
}
internal sealed class KingdomBook
{
    public readonly Dictionary<long, Kingdom> Values = new();
    public Kingdom get(long id) => Values.TryGetValue(id, out Kingdom kingdom) ? kingdom : null;
}
internal sealed class MapBox { public KingdomBook kingdoms = new(); }
internal static class World { public static MapBox world = new(); }
internal static class Date { public static int Year = 100; public static int getCurrentYear() => Year; }
internal static class ModClass { public static List<string> Warnings = new(); public static void LogWarning(string message) => Warnings.Add(message); }
internal static class Boundary
{
    public static readonly Dictionary<(long, string), int> Counts = new();
    public static string FaultService = "";
    public static long FaultKingdom = -1;
    public static int FailuresLeft;
    public static Action<string, Kingdom> DuringCall;
    public static int Calls(long id, string service) => Counts.TryGetValue((id, service), out int count) ? count : 0;
    public static void Call(string service, Kingdom kingdom)
    {
        Counts[(kingdom.id, service)] = Calls(kingdom.id, service) + 1;
        DuringCall?.Invoke(service, kingdom);
        if (service == FaultService && kingdom.id == FaultKingdom && FailuresLeft-- > 0)
            throw new InvalidOperationException("injected annual subsystem failure");
    }
}
internal static class RuntimePerformanceDiagnostic
{
    public static long BeginScope() => 0;
    public static long BeginContinuousScope() => 0;
    public static long BeginDeferredItemScope() => 0;
    public static void EndAnnualStage(string id, long start) { }
    public static void EndDetail(string id, long start) { }
    public static void EndDeferredItem(string id, long start) { }
}
internal static class KingdomAnnualStepDiagnostics { public static long Mark() => 0; public static void Account(string id, long start) { } }
internal static class RecentFeatureBenchmark { public static long Begin() => 0; public static void End(int id, long start) { } }
internal static class UpdateAgeBenchmark { public static long Begin() => 0; public static void End(int id, long start) { } public static void Flush() { } }
internal static class AWPerformanceSettings { public static bool EnableDiplomacyAi => true; }
internal static class DiplomacyAiRules { public static bool ShouldRun(bool enabled) => enabled; }
internal static class UpdateAgeBenchmarkRules { public const int KingdomCityEconomyIndex = 0; public const int KingdomCityTechIndex = 1; public const int KingdomCivilServiceExamIndex = 2; public const int KingdomFeudatoryIndex = 3; public const int KingdomForeignOccupationIndex = 4; public const int KingdomGeneralIndex = 5; public const int KingdomMandateDecisionIndex = 6; public const int KingdomMandateIndex = 7; public const int KingdomMandateRebelIndex = 8; public const int KingdomMinisterialPowerIndex = 9; public const int KingdomOfficialCareerIndex = 10; public const int KingdomPolicyIndex = 11; public const int KingdomVassalAiIndex = 12; public const int KingdomVassalTributeIndex = 13; public const int KingdomWarAiIndex = 14; public const int KingdomWarPlotIndex = 15; public const int KingdomWarTerritoryIndex = 16; public const int KingdomXiaizationIndex = 17; }
internal static class RecentFeatureBenchmarkRules { public const int CivilServiceExamAnnualIndex = 0; public const int KingdomCourtAuxiliaryIndex = 1; public const int KingdomCourtSupportIndex = 2; public const int KingdomDiplomacyIndex = 3; public const int KingdomDiplomaticMarriageIndex = 4; public const int KingdomDiplomaticOperationIndex = 5; public const int KingdomFeudatoryIndex = 6; public const int KingdomHeirIndex = 7; public const int KingdomRoyalAsylumIndex = 8; }
internal static class AsyncKingdomStrategyService
{
    public static void ClearRuntime() { }
    public static void ScheduleDiplomacy(Kingdom kingdom, int year) => Boundary.Call("AsyncKingdomStrategyService.ScheduleDiplomacy", kingdom);
    public static void ScheduleWar(Kingdom kingdom, int year) => Boundary.Call("AsyncKingdomStrategyService.ScheduleWar", kingdom);
}
internal static class BanditGreatUprisingService
{
    public static void ClearRuntime() { }
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("BanditGreatUprisingService.OnKingdomYear", kingdom);
}
internal static class CityBureauAnnualWorkService
{
    public static void ClearRuntime() { }
}
internal static class CityEconomyService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("CityEconomyService.OnKingdomYear", kingdom);
}
internal static class CitySchoolSnapshotService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("CitySchoolSnapshotService.OnKingdomYear", kingdom);
}
internal static class CityTechService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("CityTechService.OnKingdomYear", kingdom);
}
internal static class ConferredPosthumousTitleService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("ConferredPosthumousTitleService.OnKingdomYear", kingdom);
}
internal static class CorruptionService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("CorruptionService.OnKingdomYear", kingdom);
}
internal static class CourtAuxiliaryLawService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("CourtAuxiliaryLawService.OnKingdomYear", kingdom);
}
internal static class CourtDirectionService
{
    public static void RecalculateIfDirty(Kingdom kingdom) => Boundary.Call("CourtDirectionService.RecalculateIfDirty", kingdom);
}
internal static class CourtMeritRewardService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("CourtMeritRewardService.OnKingdomYear", kingdom);
}
internal static class CourtPeaceService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("CourtPeaceService.OnKingdomYear", kingdom);
}
internal static class DiplomaticCoalitionService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("DiplomaticCoalitionService.OnKingdomYear", kingdom);
}
internal static class DiplomaticMarriageService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("DiplomaticMarriageService.OnKingdomYear", kingdom);
}
internal static class DiplomaticOperationService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("DiplomaticOperationService.OnKingdomYear", kingdom);
}
internal static class DynasticMaleLineContinuityService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("DynasticMaleLineContinuityService.OnKingdomYear", kingdom);
}
internal static class ExamCycleService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("ExamCycleService.OnKingdomYear", kingdom);
}
internal static class FeudatoryService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("FeudatoryService.OnKingdomYear", kingdom);
}
internal static class ForeignOccupationService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("ForeignOccupationService.OnKingdomYear", kingdom);
}
internal static class GeneralService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("GeneralService.OnKingdomYear", kingdom);
}
internal static class HeirService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("HeirService.OnKingdomYear", kingdom);
}
internal static class KingdomFoodReliefService
{
    public static void ClearRuntime() { }
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("KingdomFoodReliefService.OnKingdomYear", kingdom);
}
internal static class KingdomPolicyService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("KingdomPolicyService.OnKingdomYear", kingdom);
}
internal static class KingdomWarDirectorService
{
    public static void Schedule(Kingdom kingdom) => Boundary.Call("KingdomWarDirectorService.Schedule", kingdom);
}
internal static class MandateDecisionService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("MandateDecisionService.OnKingdomYear", kingdom);
}
internal static class MandateIslandExileService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("MandateIslandExileService.OnKingdomYear", kingdom);
}
internal static class MandateRebelService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("MandateRebelService.OnKingdomYear", kingdom);
}
internal static class MandateService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("MandateService.OnKingdomYear", kingdom);
}
internal static class MassUprisingClusterService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("MassUprisingClusterService.OnKingdomYear", kingdom);
}
internal static class MilitaryGovernorateAiService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("MilitaryGovernorateAiService.OnKingdomYear", kingdom);
}
internal static class MinisterialPowerService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("MinisterialPowerService.OnKingdomYear", kingdom);
}
internal static class NobleRankService
{
    public static void RetryPendingDeathSuccessionOne() { }
}
internal static class NobleRemarriageService
{
    public static void ClearRuntimeState() { }
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("NobleRemarriageService.OnKingdomYear", kingdom);
}
internal static class OfficialCareerStateService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("OfficialCareerStateService.OnKingdomYear", kingdom);
}
internal static class OfficialMilitaryRetirementService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("OfficialMilitaryRetirementService.OnKingdomYear", kingdom);
}
internal static class PeasantRebelBanditIslandMigrationService
{
    public static void ClearRuntime() { }
}
internal static class PeasantRebelBanditSpawnService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("PeasantRebelBanditSpawnService.OnKingdomYear", kingdom);
}
internal static class ReverseXiaizationService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("ReverseXiaizationService.OnKingdomYear", kingdom);
}
internal static class RitualDiplomacyOpinionService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("RitualDiplomacyOpinionService.OnKingdomYear", kingdom);
}
internal static class RoyalAsylumService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("RoyalAsylumService.OnKingdomYear", kingdom);
}
internal static class RoyalMedicalCareService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("RoyalMedicalCareService.OnKingdomYear", kingdom);
}
internal static class RulerHouseholdService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("RulerHouseholdService.OnKingdomYear", kingdom);
}
internal static class RumpCourtSplitService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("RumpCourtSplitService.OnKingdomYear", kingdom);
}
internal static class VassalAIService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("VassalAIService.OnKingdomYear", kingdom);
}
internal static class VassalService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("VassalService.OnKingdomYear", kingdom);
    public static void SettleAnnualTribute(Kingdom kingdom) => Boundary.Call("VassalService.SettleAnnualTribute", kingdom);
}
internal static class WarPlotRedirectService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("WarPlotRedirectService.OnKingdomYear", kingdom);
}
internal static class WarTerritoryService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("WarTerritoryService.OnKingdomYear", kingdom);
}
internal static class XiaContactService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("XiaContactService.OnKingdomYear", kingdom);
}
internal static class XiaizationService
{
    public static void OnKingdomYear(Kingdom kingdom) => Boundary.Call("XiaizationService.OnKingdomYear", kingdom);
}
