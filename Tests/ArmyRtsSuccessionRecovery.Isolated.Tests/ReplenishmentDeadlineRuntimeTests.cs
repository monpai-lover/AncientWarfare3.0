using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ReplenishmentDeadlineRuntimeTests
{
    private static Assembly runtime;
    internal static void Run(string scenario)
    {
        runtime ??= CompileRuntime();
        try { runtime.GetType("ReplenishmentDeadlineCases").GetMethod(scenario).Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static Assembly CompileRuntime()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
                   "Code/core/lineage/ArmyReplenishmentOperationService.cs"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Production replenishment sources missing.");
        SyntaxNode Read(string file) => CSharpSyntaxTree.ParseText(File.ReadAllText(
            Path.Combine(root.FullName, "Code/core/lineage", file + ".cs"))).GetRoot();
        string Methods(SyntaxNode source, params string[] names)
        {
            var methods = source.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => names.Contains(m.Identifier.Text)).ToArray();
            if (methods.Length != names.Length) throw new InvalidOperationException("Replenishment extraction incomplete.");
            return string.Join("\n", methods.Select(m => m.ToFullString()));
        }
        var operation = Read("ArmyReplenishmentOperationService");
        string production = "using System; using System.Collections.Generic; namespace AncientWarfare3.core.lineage { " +
            operation.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "ArmyReplenishmentOperationState") +
            " internal static partial class ArmyReplenishmentOperationService { " +
            Methods(operation, "ProcessAuthorityCycle", "ProcessOne", "TakeActiveBatch", "Clear") + " } " +
            " internal static partial class ArmyRtsControllerService { " +
            Methods(Read("ArmyRtsControllerService"), "CanGenerateWartimeReplacements") + " } }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProductionReplenishmentDeadline", new[]
        {
            CSharpSyntaxTree.ParseText(production), Read("ArmyReplenishmentOperationRules").SyntaxTree,
            Read("ArmyMobilizationRules").SyntaxTree, Read("LineageKeys").SyntaxTree,
            CSharpSyntaxTree.ParseText(Harness)
        }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using AncientWarfare3.core.lineage;
        public sealed class Data
        {
            public long id;
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
            public void removeInt(string key) => Values.Remove(key);
            public void removeLong(string key) => Values.Remove(key);
            public void removeString(string key) => Values.Remove(key);
        }
        public sealed class Kingdom { public Data data = new Data(); }
        public sealed class WorldTile { public Data data = new Data(); }
        public sealed class City { public Data data = new Data(); public Kingdom kingdom; public long id => data.id; }
        public sealed class Actor { public Data data = new Data(); public WorldTile current_tile = new WorldTile(); public bool is_inside_boat; }
        public sealed class Army
        {
            public Data data = new Data(); public long id => data.id;
            public Kingdom Kingdom; public Actor Captain = new Actor();
            public int Shortage = 8;
        }
        namespace AncientWarfare3.core.lineage
        {
            internal static class AWPerformanceSettings { public static bool EnableSyntheticMobilization = true; }
            internal static class ArmyRtsRules { public const int DeploymentQuorumPercent = 80; }
            internal static partial class ArmyRtsControllerService
            {
                private static Actor SafeCaptain(Army army) => army?.Captain;
                private static Kingdom SafeKingdom(Army army) => army?.Kingdom;
            }
            internal static class ArmyReplenishmentCompletionService
            {
                public static int Completed;
                public static void Complete(Army army)
                {
                    if (ArmyReplenishmentOperationService.ActiveCount != 0 || army.data.Values.ContainsKey(LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION))
                        throw new InvalidOperationException("Completion published before removing persisted/indexed operation.");
                    Completed++;
                }
            }
            internal static class KingdomWarDirectorService { public static void QueueArmyChanged(Kingdom kingdom) { } }
            internal static class SyntheticMobilizationLedgerService
            {
                public static int Reads;
                public static int AvailableReplacement(long war, long city) { Reads++; return 10; }
            }
            internal static class ArmyConquestReserveService { public static int Get(Army army) => 0; }
            internal static class TemporaryLevyService
            {
                public static int ExhaustionRecords;
                public static void RecordConfirmedReserveExhaustion(Kingdom kingdom, Army army, int shortage) => ExhaustionRecords++;
            }
            internal static partial class ArmyReplenishmentOperationService
            {
                private static readonly SortedSet<long> ActiveArmyIds = new SortedSet<long>();
                private static long _cursorAfterArmyId = -1;
                public static Army CurrentArmy; public static City CurrentCity;
                public static ArmyReplenishmentOperationState State;
                public static double Now;
                public static int RecruitCalls, RecruitLimit = 8;
                public static int ActiveCount => ActiveArmyIds.Count;
                private static Army FindArmy(long id) => CurrentArmy;
                private static City FindCity(long id) => CurrentCity;
                private static Kingdom SafeKingdom(Army army) => army.Kingdom;
                private static long ResolveMissionWarId(Army army, Kingdom kingdom) => 20;
                private static bool TryRead(Army army, out ArmyReplenishmentOperationState state) { state = State; return true; }
                private static bool TryGetLiveShortage(Army army, out int shortage) { shortage = army.Shortage; return true; }
                private static double CurrentWorldTime() => Now;
                private static void DisableSyntheticReplenishment() => ActiveArmyIds.Clear();
                private static int RecruitFormalWarShortage(Army army, Kingdom kingdom, City city, int requested, out bool exhausted, out List<Actor> recruits)
                {
                    RecruitCalls++; exhausted = false; recruits = new List<Actor>();
                    int count = Math.Min(requested, RecruitLimit); army.Shortage -= count; return count;
                }
                private static void Persist(Army army, ArmyReplenishmentOperationState state) { State = state; }
                private static void TeleportSuccessfulRecruits(Army army, List<Actor> recruits) { }
                public static void Reset()
                {
                    ActiveArmyIds.Clear(); _cursorAfterArmyId = -1; RecruitCalls = 0; RecruitLimit = 8;
                    CurrentArmy = new Army { data = new Data { id = 1 }, Kingdom = new Kingdom() };
                    CurrentCity = new City { data = new Data { id = 10 }, kingdom = CurrentArmy.Kingdom };
                    CurrentArmy.data.Values[LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION] = 1;
                    State = new ArmyReplenishmentOperationState { ArmyId = 1, SourceCityId = 10, ApprovedShortage = 8, StartTime = 0, DeadlineTime = 20 };
                    ActiveArmyIds.Add(1);
                    ArmyReplenishmentCompletionService.Completed = TemporaryLevyService.ExhaustionRecords = SyntheticMobilizationLedgerService.Reads = 0;
                }
            }
        }
        public static class ReplenishmentDeadlineCases
        {
            public static void CaptainInBoatExpires() => CheckBlockedDeadline(() => ArmyReplenishmentOperationService.CurrentArmy.Captain.is_inside_boat = true);
            public static void CapturedSourceExpires() => CheckBlockedDeadline(() => ArmyReplenishmentOperationService.CurrentCity.kingdom = new Kingdom());
            public static void DisposedSourceExpires() => CheckBlockedDeadline(() => ArmyReplenishmentOperationService.CurrentCity.data = null);
            public static void MissingSourceExpires() => CheckBlockedDeadline(() => ArmyReplenishmentOperationService.CurrentCity = null);
            private static void CheckBlockedDeadline(Action block)
            {
                ArmyReplenishmentOperationService.Reset(); block();
                ArmyReplenishmentOperationService.Now = 19; ArmyReplenishmentOperationService.ProcessAuthorityCycle();
                Check(ArmyReplenishmentOperationService.ActiveCount == 1 && ArmyReplenishmentCompletionService.Completed == 0,
                    "A temporary replacement gate prematurely completed the operation.");
                ArmyReplenishmentOperationService.Now = 20; ArmyReplenishmentOperationService.ProcessAuthorityCycle();
                Check(ArmyReplenishmentOperationService.ActiveCount == 0 && ArmyReplenishmentCompletionService.Completed == 1,
                    "A replacement gate bypassed the deadline and retained permanent active work.");
                Check(ArmyReplenishmentOperationService.RecruitCalls == 0 && TemporaryLevyService.ExhaustionRecords == 0 && SyntheticMobilizationLedgerService.Reads == 0,
                    "Ineligible generation created troops or falsely confirmed reserve exhaustion.");
                ArmyReplenishmentOperationService.Now = 200; ArmyReplenishmentOperationService.ProcessAuthorityCycle();
                Check(ArmyReplenishmentCompletionService.Completed == 1, "Expired operation repeated completion on later authority cycles.");
            }
            public static void TemporaryGateRecoveryStillRecruits()
            {
                ArmyReplenishmentOperationService.Reset();
                ArmyReplenishmentOperationService.CurrentArmy.Captain.is_inside_boat = true;
                ArmyReplenishmentOperationService.Now = 10; ArmyReplenishmentOperationService.ProcessAuthorityCycle();
                ArmyReplenishmentOperationService.CurrentArmy.Captain.is_inside_boat = false;
                ArmyReplenishmentOperationService.Now = 15; ArmyReplenishmentOperationService.ProcessAuthorityCycle();
                Check(ArmyReplenishmentOperationService.RecruitCalls == 1 && ArmyReplenishmentOperationService.State.EnlistedCount == 8 &&
                    ArmyReplenishmentCompletionService.Completed == 1 && ArmyReplenishmentOperationService.CurrentArmy.Shortage == 0,
                    "A temporary transport gate prevented ordinary successful replenishment after landing.");
            }
            public static void EligiblePartialBatchStillFinishesAtDeadline()
            {
                ArmyReplenishmentOperationService.Reset(); ArmyReplenishmentOperationService.RecruitLimit = 3;
                ArmyReplenishmentOperationService.Now = 20; ArmyReplenishmentOperationService.ProcessAuthorityCycle();
                Check(ArmyReplenishmentOperationService.RecruitCalls == 1 && ArmyReplenishmentOperationService.State.EnlistedCount == 3 &&
                    ArmyReplenishmentOperationService.CurrentArmy.Shortage == 5 && ArmyReplenishmentCompletionService.Completed == 1 &&
                    TemporaryLevyService.ExhaustionRecords == 0, "Deadline changed the final eligible recruitment opportunity.");
            }
            private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        }
        """;
}
