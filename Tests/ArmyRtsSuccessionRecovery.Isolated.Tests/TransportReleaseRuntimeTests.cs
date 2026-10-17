using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class TransportReleaseRuntimeTests
{
    private static Assembly runtime;
    internal static void Run(string scenario)
    {
        runtime ??= CompileRuntime();
        try { runtime.GetType("TransportReleaseCases").GetMethod(scenario).Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static Assembly CompileRuntime()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
                   "Code/core/lineage/ArmyRtsTransportService.cs"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Production transport source missing.");
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,
            "Code/core/lineage/ArmyRtsTransportService.cs"))).GetRoot();
        string[] names = { "ReleaseArmy", "IsValidMember" };
        var methods = source.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.Text)).ToArray();
        if (methods.Length != names.Length) throw new InvalidOperationException("Transport extraction incomplete.");
        string production = "using System; using System.Collections.Generic; namespace AncientWarfare3.core.lineage { " +
            "internal static partial class ArmyRtsTransportService { " +
            string.Join("\n", methods.Select(m => m.ToFullString())) + " } }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProductionTransportRelease", new[]
        {
            CSharpSyntaxTree.ParseText(production), CSharpSyntaxTree.ParseText(Harness)
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
        public sealed class Data { public long id; }
        public sealed class Kingdom { public Data data = new Data(); }
        public sealed class WorldTile { public Data data = new Data(); }
        public sealed class Army { public Data data = new Data(); public long id => data.id; }
        public sealed class Actor
        {
            public Data data = new Data(); public Army army; public Kingdom kingdom = new Kingdom();
            public WorldTile current_tile = new WorldTile(); public bool Alive = true;
            public bool isAlive() => Alive;
            public bool isRekt() => data == null || !Alive;
        }
        public enum AWPathFailureReason { CancelledByNewRequest }
        public static class AWPathMovementBridge
        {
            public static readonly HashSet<Actor> Cancelled = new HashSet<Actor>();
            public static void Cancel(Actor actor, AWPathFailureReason reason) => Cancelled.Add(actor);
        }
        namespace AncientWarfare3.core.lineage
        {
            internal static class AWArmyMarchService { public static int Cancelled; public static void OnTransportCancelled(Army army) => Cancelled++; }
            internal static class ArmyRtsControllerService { public static int Cancelled; public static void OnTransportCancelled(Army army) => Cancelled++; }
            internal static partial class ArmyRtsTransportService
            {
                private sealed class TransportState
                {
                    public readonly Dictionary<long, Actor> Members = new Dictionary<long, Actor>();
                }
                private static readonly Dictionary<long, TransportState> States = new Dictionary<long, TransportState>();
                public static int Disembarked, BoatsReleased;
                // Native landing/boat APIs are outside the path-ownership boundary under test.
                private static void DisembarkRosterForRelease(TransportState state) => Disembarked++;
                private static void DestroyTemporaryTransportBoats(TransportState state) => BoatsReleased++;
                public static void AddVoyage(Army army, params Actor[] actors)
                {
                    var state = new TransportState();
                    foreach (var actor in actors) state.Members[actor.data.id] = actor;
                    States[army.id] = state;
                }
                public static bool HasVoyage(Army army) => States.ContainsKey(army.id);
                public static void Reset()
                {
                    States.Clear(); Disembarked = BoatsReleased = 0;
                    AWPathMovementBridge.Cancelled.Clear(); AWArmyMarchService.Cancelled = ArmyRtsControllerService.Cancelled = 0;
                }
            }
        }
        public static class TransportReleaseCases
        {
            public static void TransferredMemberKeepsNewPath()
            {
                ArmyRtsTransportService.Reset();
                var oldArmy = new Army { data = new Data { id = 1 } };
                var newArmy = new Army { data = new Data { id = 2 } };
                var transferred = new Actor { data = new Data { id = 10 }, army = oldArmy };
                var retained = new Actor { data = new Data { id = 11 }, army = oldArmy };
                ArmyRtsTransportService.AddVoyage(oldArmy, transferred, retained);
                transferred.army = newArmy;
                ArmyRtsTransportService.AddVoyage(newArmy, transferred);
                ArmyRtsTransportService.ReleaseArmy(oldArmy);
                Check(!AWPathMovementBridge.Cancelled.Contains(transferred), "An old voyage cancelled the transferred actor's new path.");
                Check(AWPathMovementBridge.Cancelled.Contains(retained), "The cancelled voyage retained its actor path.");
                Check(!ArmyRtsTransportService.HasVoyage(oldArmy) && ArmyRtsTransportService.HasVoyage(newArmy), "Release affected the new voyage.");
                Check(ArmyRtsTransportService.Disembarked == 1 && ArmyRtsTransportService.BoatsReleased == 1 &&
                    AWArmyMarchService.Cancelled == 1 && ArmyRtsControllerService.Cancelled == 1, "Ordinary transport cleanup was skipped.");
                ArmyRtsTransportService.ReleaseArmy(newArmy);
                Check(AWPathMovementBridge.Cancelled.Contains(transferred), "Current owner could not later release its path.");
            }
            public static void DetachedAndInvalidActorsAreNotCancelled()
            {
                ArmyRtsTransportService.Reset();
                var army = new Army { data = new Data { id = 1 } };
                var detached = new Actor { data = new Data { id = 10 }, army = army };
                var dead = new Actor { data = new Data { id = 11 }, army = army };
                var disposed = new Actor { data = new Data { id = 12 }, army = army };
                ArmyRtsTransportService.AddVoyage(army, detached, dead, disposed);
                detached.army = null; dead.Alive = false; disposed.data = null;
                ArmyRtsTransportService.ReleaseArmy(army);
                Check(AWPathMovementBridge.Cancelled.Count == 0 && !ArmyRtsTransportService.HasVoyage(army), "Release touched detached/dead actors or retained the voyage.");
            }
            public static void RepeatedReleaseIsIdempotent()
            {
                ArmyRtsTransportService.Reset();
                var army = new Army { data = new Data { id = 1 } };
                var actor = new Actor { data = new Data { id = 10 }, army = army };
                ArmyRtsTransportService.AddVoyage(army, actor);
                ArmyRtsTransportService.ReleaseArmy(army); ArmyRtsTransportService.ReleaseArmy(army);
                Check(AWPathMovementBridge.Cancelled.Count == 1 && ArmyRtsTransportService.BoatsReleased == 1 &&
                    AWArmyMarchService.Cancelled == 1 && ArmyRtsControllerService.Cancelled == 1, "Repeated release changed another lifecycle.");
            }
            private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        }
        """;
}
