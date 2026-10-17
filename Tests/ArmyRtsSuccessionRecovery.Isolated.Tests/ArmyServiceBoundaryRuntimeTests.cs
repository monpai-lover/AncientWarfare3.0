using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ArmyServiceBoundaryRuntimeTests
{
    private static Assembly runtime;

    internal static void Run(string scenario)
    {
        runtime ??= CompileRuntime();
        try { runtime.GetType("ArmyBoundaryCases").GetMethod(scenario).Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static Assembly CompileRuntime()
    {
        string root = FindRoot();
        ClassDeclarationSyntax ReadClass(string path, string name) => CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(root, path))).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == name);
        string ExtractMethods(ClassDeclarationSyntax declaration, params string[] names)
        {
            var methods = declaration.Members.OfType<MethodDeclarationSyntax>()
                .Where(m => names.Contains(m.Identifier.Text)).ToArray();
            if (methods.Length != names.Length) throw new InvalidOperationException("Production method extraction is incomplete.");
            return string.Join("\n", methods.Select(m => m.ToFullString()));
        }
        var march = ReadClass("Code/core/lineage/AWArmyMarchService.cs", "AWArmyMarchService");
        var retreat = ReadClass("Code/core/lineage/ArmyRetreatService.cs", "ArmyRetreatService");
        string production = "using System; using System.Collections.Generic; using AncientWarfare3.core.pathfinding; " +
            "namespace AncientWarfare3.core.lineage { internal static partial class AWArmyMarchService { " +
            ExtractMethods(march, "SubmitStrategicRoute") + " } internal static partial class ArmyRetreatService { " +
            ExtractMethods(retreat, "OnSafeCityObserved", "OnCityControlChanged", "OnCityDestroyed", "RefreshObservedCount") +
            retreat.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "SafeCityIndexState").ToFullString() + " } }";
        var retreatRules = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Code/core/lineage/ArmyRetreatRules.cs"))).GetRoot();
        string indexSource = "using System; using System.Collections.Generic; namespace AncientWarfare3.core.lineage { " +
            string.Join("\n", retreatRules.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c =>
                c.Identifier.Text is "ArmySafeCityIndex" or "ArmySafeCityIdCursor").Select(c => c.ToFullString())) + " }";
        var routeSource = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Code/core/pathfinding/ArmyRouteProvider.cs"))).GetRoot();
        string routeContracts = "using System; namespace AncientWarfare3.core.pathfinding { " +
            string.Join("\n", routeSource.DescendantNodes().OfType<StructDeclarationSyntax>().Where(c =>
                c.Identifier.Text is "ArmyRouteHandle" or "ArmyRouteRequest").Select(c => c.ToFullString())) + " }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProductionArmyBoundaries", new[]
        {
            CSharpSyntaxTree.ParseText(production), CSharpSyntaxTree.ParseText(indexSource),
            CSharpSyntaxTree.ParseText(routeContracts), CSharpSyntaxTree.ParseText(Harness)
        }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Code/core/lineage/AWArmyMarchService.cs"))) return dir.FullName;
        throw new DirectoryNotFoundException("Cannot find production Army source.");
    }

    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using AncientWarfare3.core.lineage;
        using AncientWarfare3.core.pathfinding;
        public sealed class Data { public long id; public int tile_id; }
        public class Entity
        {
            public Data data = new Data();
            // Native NanoObject.id calls CoreSystemObject.getID -> data.id.
            public long id => data.id;
            public bool isRekt() => data == null;
        }
        public sealed class Kingdom : Entity { public List<City> cities = new List<City>(); }
        public sealed class City : Entity { public Kingdom kingdom; }
        public sealed class WorldTile : Entity { }
        public sealed class Actor : Entity { public WorldTile current_tile; }
        public sealed class Army : Entity { public Actor Captain; public Actor getCaptain() => Captain; }
        namespace AncientWarfare3.core.pathfinding
        {
            internal static class ArmyRouteProviderService
            {
                public static int Submissions;
                public static bool Accept = true;
                public static ArmyRouteRequest Last;
                public static ArmyRouteHandle Submit(ArmyRouteRequest request)
                {
                    Submissions++; Last = request;
                    return new ArmyRouteHandle(request.ArmyId, Submissions, Accept, false);
                }
            }
            internal static class AWPathfindingBootstrap { public static CacheState Cache = new CacheState(); }
            internal sealed class CacheState { public int GenerationId = 7; }
        }
        namespace AncientWarfare3.core.lineage
        {
            internal static class ArmyRtsTransportService { public static bool HasActiveVoyage(Army army) => false; }
            internal static partial class AWArmyMarchService
            {
                private sealed class MarchState
                {
                    public MarchState(long army, int target, int generation, bool pUsesProvider, int pProviderStartTileId) { }
                    public bool LandTrailPausedForTransport;
                }
                private static readonly Dictionary<long, MarchState> States = new Dictionary<long, MarchState>();
                public static int CancelledPaths;
                private static void CancelInstalledActorPaths(MarchState state) => CancelledPaths++;
                public static int StateCount => States.Count;
                public static void Reset() { States.Clear(); CancelledPaths = 0; ArmyRouteProviderService.Submissions = 0; ArmyRouteProviderService.Accept = true; }
            }
            internal static partial class ArmyRetreatService
            {
                private static readonly ArmySafeCityIndex SafeCityIndex = new ArmySafeCityIndex();
                private static readonly Dictionary<long, SafeCityIndexState> SafeCityStates = new Dictionary<long, SafeCityIndexState>();
                private static void BeginSafeCityIndexScan(Kingdom kingdom, SafeCityIndexState state) { }
                public static void Reset() { SafeCityIndex.Clear(); SafeCityStates.Clear(); }
                public static bool Contains(long kingdom, long city) => SafeCityIndex.Contains(kingdom, city);
            }
        }
        public static class ArmyBoundaryCases
        {
            private static Army LiveArmy() => new Army { data = new Data { id = 10 },
                Captain = new Actor { current_tile = new WorldTile { data = new Data { tile_id = 20 } } } };
            private static WorldTile Target() => new WorldTile { data = new Data { tile_id = 30 } };
            public static void DisposedArmyRoute()
            {
                AWArmyMarchService.Reset(); var army = LiveArmy(); army.data = null;
                var handle = AWArmyMarchService.SubmitStrategicRoute(army, Target());
                Require(!handle.Accepted && handle.ArmyId == -1 && handle.FailureReason == "invalid_route_input", "disposed army did not produce a normal rejection");
                Require(ArmyRouteProviderService.Submissions == 0 && AWArmyMarchService.StateCount == 0, "disposed army reached the route provider");
            }
            public static void DisposedCaptainRoute()
            {
                AWArmyMarchService.Reset(); var army = LiveArmy(); army.Captain.data = null;
                var handle = AWArmyMarchService.SubmitStrategicRoute(army, Target());
                Require(!handle.Accepted && ArmyRouteProviderService.Submissions == 0, "disposed captain retaining its last tile was assigned a route");
            }
            public static void InvalidRouteTarget()
            {
                AWArmyMarchService.Reset(); var army = LiveArmy();
                var handle = AWArmyMarchService.SubmitStrategicRoute(army, new WorldTile { data = null });
                Require(!handle.Accepted && handle.ArmyId == 10 && ArmyRouteProviderService.Submissions == 0, "invalid target lost the valid army identity or submitted work");
            }
            public static void ValidRouteReplacement()
            {
                AWArmyMarchService.Reset(); var army = LiveArmy(); var target = Target();
                ArmyRouteProviderService.Accept = false;
                Require(!AWArmyMarchService.SubmitStrategicRoute(army, target).Accepted && AWArmyMarchService.StateCount == 0, "provider rejection installed marching state");
                ArmyRouteProviderService.Accept = true;
                Require(AWArmyMarchService.SubmitStrategicRoute(army, target).Accepted, "valid route was rejected");
                AWArmyMarchService.SubmitStrategicRoute(army, target);
                Require(AWArmyMarchService.StateCount == 1 && AWArmyMarchService.CancelledPaths == 1 &&
                    ArmyRouteProviderService.Last.StartTileId == 20 && ArmyRouteProviderService.Last.TargetTileId == 30,
                    "valid route replacement changed path cancellation or endpoints");
            }
            private static (Kingdom, City) IndexedCity()
            {
                ArmyRetreatService.Reset(); var kingdom = new Kingdom { data = new Data { id = 1 } };
                var city = new City { data = new Data { id = 2 }, kingdom = kingdom };
                ArmyRetreatService.OnSafeCityObserved(city, kingdom);
                Require(ArmyRetreatService.Contains(1, 2), "valid city was not initially indexed");
                return (kingdom, city);
            }
            public static void DisposedCityObservation()
            {
                ArmyRetreatService.Reset();
                ArmyRetreatService.OnSafeCityObserved(new City { data = null }, new Kingdom());
                ArmyRetreatService.OnSafeCityObserved(null, null);
                Require(!ArmyRetreatService.Contains(-1, -1), "invalid observation created an index entry");
            }
            public static void DisposedKingdomObservation()
            {
                var (kingdom, city) = IndexedCity(); kingdom.data = null;
                ArmyRetreatService.OnSafeCityObserved(city, kingdom);
                Require(!ArmyRetreatService.Contains(1, 2), "a disposed kingdom retained a safe retreat destination");
            }
            public static void DisposedOwnerControlChange()
            {
                var (kingdom, city) = IndexedCity(); city.kingdom = new Kingdom { data = null };
                ArmyRetreatService.OnCityControlChanged(city, kingdom);
                Require(!ArmyRetreatService.Contains(1, 2), "invalid new owner retained the previous retreat destination");
            }
            public static void DisposedOwnerCityDestruction()
            {
                var (kingdom, city) = IndexedCity(); kingdom.data = null;
                ArmyRetreatService.OnCityDestroyed(city);
                Require(!ArmyRetreatService.Contains(1, 2), "city destruction did not remove the old retreat destination");
            }
            public static void ValidRetreatEvents()
            {
                var (previous, city) = IndexedCity(); var current = new Kingdom { data = new Data { id = 3 } };
                city.kingdom = current; ArmyRetreatService.OnCityControlChanged(city, previous);
                Require(!ArmyRetreatService.Contains(1, 2) && ArmyRetreatService.Contains(3, 2), "valid city transfer did not move the retreat destination");
                ArmyRetreatService.OnCityDestroyed(city);
                Require(!ArmyRetreatService.Contains(3, 2), "valid destruction retained the retreat destination");
            }
            private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        }
        """;
}
