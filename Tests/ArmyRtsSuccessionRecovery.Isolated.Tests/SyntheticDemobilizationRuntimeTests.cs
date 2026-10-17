using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SyntheticDemobilizationRuntimeTests
{
    private static Assembly runtime;

    internal static void Run(string scenario)
    {
        runtime ??= CompileRuntime();
        try { runtime.GetType("SyntheticCleanupCases").GetMethod(scenario).Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static Assembly CompileRuntime()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
                   "Code/core/lineage/SyntheticMobilizationLedgerService.cs"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Production synthetic sources missing.");
        SyntaxNode Read(string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(
            Path.Combine(root.FullName, "Code/core/lineage", name + ".cs"))).GetRoot();
        string Methods(SyntaxNode source, params string[] names)
        {
            var methods = source.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => names.Contains(m.Identifier.Text)).ToArray();
            foreach (string name in names)
                if (!methods.Any(m => m.Identifier.Text == name))
                    throw new InvalidOperationException("Production method missing: " + name);
            return string.Join("\n", methods.Select(m => m.ToFullString()));
        }
        var ledger = Read("SyntheticMobilizationLedgerService");
        var levy = Read("SyntheticLevyService");
        string production = "using System; using System.Collections.Generic; namespace AncientWarfare3.core.lineage { " +
            ledger.DescendantNodes().OfType<EnumDeclarationSyntax>().Single(e => e.Identifier.Text == "SyntheticMobilizationPhase") +
            ledger.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "SyntheticMobilizationRecord") +
            " internal static partial class SyntheticMobilizationLedgerService { " +
            Methods(ledger, "ProcessDemobilization", "MatchesRecord", "OnSyntheticRemoved", "Key") + " } " +
            "internal static partial class SyntheticLevyService { " +
            Methods(levy, "IsSynthetic", "TryReadFlags", "RemoveWithoutPersonalHistory", "ReleaseLiveLedgerOnce", "FindSourceCity") + " } }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProductionSyntheticCleanup", new[]
        {
            CSharpSyntaxTree.ParseText(production), Read("SyntheticMobilizationRules").SyntaxTree,
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
            private readonly Dictionary<string, object> values = new Dictionary<string, object>();
            public void get<T>(string key, out T value, T fallback) => value = values.TryGetValue(key, out var found) ? (T)found : fallback;
            public void set<T>(string key, T value) => values[key] = value;
        }
        public sealed class City { public Data data = new Data(); public long id => data.id; }
        public sealed class Actor
        {
            public Data data = new Data(); public object army; public City city; public bool Removed;
            public void removeFromArmy() => army = null;
            public void setArmy(object value) => army = value;
        }
        public sealed class CityManager { public City Value; public City get(long id) => Value != null && Value.id == id ? Value : null; }
        public sealed class World { public static World world = new World(); public CityManager cities = new CityManager(); }
        public static class ActionLibrary
        {
            public static void removeUnit(Actor actor)
            {
                if (!SyntheticLevySpawnScope.Active) throw new InvalidOperationException("Personal history suppression missing.");
                actor.Removed = true;
            }
        }
        public sealed class SyntheticLevySpawnScope : IDisposable
        {
            public static bool Active;
            public static SyntheticLevySpawnScope Open() { Active = true; return new SyntheticLevySpawnScope(); }
            public void Dispose() { Active = false; }
        }
        namespace AncientWarfare3.core.lineage
        {
            internal static class LineageKeys
            {
                public const string SYNTHETIC_LEVY = "synthetic", SYNTHETIC_LEVY_PROMOTED = "promoted",
                    SYNTHETIC_LEVY_EMERGENCY_ID = "war", SYNTHETIC_LEVY_SOURCE_CITY_ID = "city",
                    SYNTHETIC_LEVY_LEDGER_RELEASED = "released";
            }
            internal static class CityReservePoolService { public static void OnSyntheticLedgerChanged(long city, long kingdom) { } }
            internal static partial class SyntheticLevyService { }
            internal static partial class SyntheticMobilizationLedgerService
            {
                private static readonly List<long> ActorBatch = new List<long>();
                private static readonly Dictionary<string, SyntheticMobilizationRecord> Records = new Dictionary<string, SyntheticMobilizationRecord>();
                public static readonly Dictionary<long, Actor> Actors = new Dictionary<long, Actor>();
                public static int Visits, Queued, CityLive;
                private static Actor ResolveActor(long id) { Visits++; return Actors.TryGetValue(id, out var actor) ? actor : null; }
                private static void AddCitySynthetic(long id, int delta) => CityLive += delta;
                private static void EnqueueRecord(string key) => Queued++;
                public static void Reset() { Records.Clear(); Actors.Clear(); Visits = Queued = CityLive = 0; }
                public static void Process(SyntheticMobilizationRecord record)
                {
                    Records[Key(record.WarId, record.CityId)] = record;
                    ProcessDemobilization(record, Key(record.WarId, record.CityId));
                }
            }
        }
        public static class SyntheticCleanupCases
        {
            private static SyntheticMobilizationRecord Setup()
            {
                SyntheticMobilizationLedgerService.Reset();
                World.world.cities.Value = new City { data = new Data { id = 10 } };
                return new SyntheticMobilizationRecord { WarId = 20, CityId = 10, Phase = SyntheticMobilizationPhase.Demobilizing };
            }
            private static Actor Add(SyntheticMobilizationRecord record, long id, bool synthetic = true, bool promoted = false, long war = 20, long city = 10)
            {
                var actor = new Actor { data = new Data { id = id } };
                actor.data.set("synthetic", synthetic); actor.data.set("promoted", promoted);
                actor.data.set("war", war); actor.data.set("city", city);
                SyntheticMobilizationLedgerService.Actors[id] = actor;
                record.ActorIds.Add(id); record.LiveSynthetic++; SyntheticMobilizationLedgerService.CityLive++;
                return actor;
            }
            public static void NoArmyOrArrivalRequired()
            {
                var record = Setup(); var actor = Add(record, 1);
                SyntheticMobilizationLedgerService.Process(record);
                Check(actor.Removed && record.ActorIds.Count == 0 && record.LiveSynthetic == 0 &&
                    SyntheticMobilizationLedgerService.CityLive == 0 && record.Phase == SyntheticMobilizationPhase.Complete,
                    "An army-less synthetic actor without arrival state did not finish cleanup.");
            }
            public static void BoundedBatches()
            {
                var record = Setup(); for (int i = 1; i <= 20; i++) Add(record, i);
                SyntheticMobilizationLedgerService.Process(record);
                Check(SyntheticMobilizationLedgerService.Visits == 16 && record.ActorIds.Count == 4 &&
                    record.LiveSynthetic == 4 && SyntheticMobilizationLedgerService.Queued == 1,
                    "First cleanup batch exceeded its bound or lost remaining work.");
                SyntheticMobilizationLedgerService.Process(record);
                Check(SyntheticMobilizationLedgerService.Visits == 20 && record.ActorIds.Count == 0 &&
                    record.Phase == SyntheticMobilizationPhase.Complete, "Remaining cleanup failed to complete.");
            }
            public static void PermanentAndUnrelatedActorsSurvive()
            {
                var record = Setup(); var ordinary = Add(record, 1, synthetic: false);
                var promoted = Add(record, 2, promoted: true); var otherWar = Add(record, 3, war: 21);
                var otherCity = Add(record, 4, city: 11); var disposed = Add(record, 5); disposed.data = null;
                record.ActorIds.Add(6); record.LiveSynthetic++; SyntheticMobilizationLedgerService.CityLive++;
                var generated = Add(record, 7);
                SyntheticMobilizationLedgerService.Process(record);
                Check(!ordinary.Removed && !promoted.Removed && !otherWar.Removed && !otherCity.Removed &&
                    !disposed.Removed && generated.Removed && record.ActorIds.Count == 0 && record.LiveSynthetic == 0 &&
                    SyntheticMobilizationLedgerService.CityLive == 0 && record.Phase == SyntheticMobilizationPhase.Complete,
                    "Cleanup removed a permanent/unrelated actor or retained a stale record.");
            }
            private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        }
        """;
}
