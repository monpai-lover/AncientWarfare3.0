using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string root = FindRoot();
            string path = args.Length == 0
                ? Path.Combine(root, "Code/core/performance/AWCooperativeSimulationRunner.cs")
                : args[0];
            var declaration = CSharpSyntaxTree.ParseText(File.ReadAllText(path))
                .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(type => type.Identifier.Text == "AWCooperativeSimulationRunner");
            string[] required =
            {
                "TryBeginActorPresentationOverlap", "TryBeginBuildingPresentationOverlap",
                "FinishPresentationFrame", "EnsureActorReadBoundary", "EnsureBuildingReadBoundary",
                "CompleteActorPresentationWork", "CompleteBuildingPresentationWork",
                "DrainToBoundary", "CompleteActiveCycleAtBoundary", "RestoreNativeParallelism"
            };
            var methods = declaration.Members.OfType<MethodDeclarationSyntax>()
                .Where(method => required.Contains(method.Identifier.Text) ||
                    method.Identifier.Text == "CanDispatchPresentationWork").ToArray();
            if (required.Any(name => !methods.Any(method => method.Identifier.Text == name)))
                throw new InvalidOperationException("Production method extraction is incomplete.");
            var members = methods.Select(method => method.ToFullString()).ToList();
            members.AddRange(declaration.Members.OfType<FieldDeclarationSyntax>().Where(field =>
                field.Declaration.Variables.All(variable =>
                    variable.Identifier.Text.Contains("PresentationOverlap") ||
                    variable.Identifier.Text.EndsWith("PresentationBoundaryReason")))
                .Select(field => field.ToFullString()));
            members.AddRange(declaration.Members.OfType<PropertyDeclarationSyntax>().Where(property =>
                property.Identifier.Text is "Active" or "RequiresControl").Select(property => property.ToFullString()));
            members.Add(declaration.Members.OfType<EnumDeclarationSyntax>().Single(type =>
                type.Identifier.Text == "SimulationStage").ToFullString());
            string production = "using System; using System.Threading; " +
                "namespace AncientWarfare3.core.performance { internal sealed partial class AWCooperativeSimulationRunner { " +
                string.Join("\n", members) + " } }";
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator).Select(file => MetadataReference.CreateFromFile(file));
            var compilation = CSharpCompilation.Create("PresentationLifecycleRuntime", new[]
            {
                CSharpSyntaxTree.ParseText(production),
                CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
                    "Code/core/performance/AWFrameSchedulerRules.cs"))),
                CSharpSyntaxTree.ParseText(Harness)
            }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
            var runtime = Assembly.Load(stream.ToArray());
            try { runtime.GetType("AncientWarfare3.core.performance.AWCooperativeSimulationRunner")
                .GetMethod("RunCases").Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { throw error.InnerException; }
            SchedulerFaultRecoveryCases.Run(root, args.Length > 1 ? args[1] : null);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Code/core/performance/AWCooperativeSimulationRunner.cs")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Cannot find production runner.");
    }

    private const string Harness = """
        using System;
        public sealed class MapBox { public bool Paused; public bool isPaused() => Paused; }
        public static class World { public static MapBox world; }
        public static class Config { public static bool game_loaded = true; }
        public static class SmoothLoader { public static bool Loading; public static bool isLoading() => Loading; }
        public static class AW3MultiplayerReplicaScope { public static bool IsReplicaSession; }
        namespace AncientWarfare3.core.performance {
            internal static class AWPerformanceSettings {
                public static bool EnableActorPresentationSnapshots = true;
                public static bool EnableWorldObjectPresentationSnapshots = true;
                public static AWSimulationMode Mode = AWSimulationMode.Fixed;
            }
            internal static class AWActorPresentationSnapshots { public static bool HasPublishedSnapshot = true; }
            internal static class AWSimulationTickBenchmark {
                public static int Suspends, Resumes;
                public static void Suspend() => Suspends++;
                public static void Resume() => Resumes++;
            }
            internal static class AWSimulationCoordinatorThread {
                internal readonly struct WorkResult {
                    internal long WallTicks => 2;
                    internal long WaitTicks => 1;
                }
            }
            internal sealed class BatchRunner {
                public bool WaitingForPresentationDispatch = true;
                public bool HasParallelPresentationWorkInFlight;
                public bool IsBackgroundWorkCompleted;
                public bool ThrowOnComplete;
                public int Launches, Joins;
                public bool BeginParallelPresentationWork() {
                    Launches++;
                    WaitingForPresentationDispatch = false;
                    HasParallelPresentationWorkInFlight = true;
                    return true;
                }
                public AWSimulationCoordinatorThread.WorkResult CompleteParallelPresentationWork() {
                    Joins++;
                    if (ThrowOnComplete) throw new InvalidOperationException("worker join failed");
                    HasParallelPresentationWorkInFlight = false;
                    return new AWSimulationCoordinatorThread.WorkResult();
                }
            }
            internal sealed class Ownership {
                public int Releases;
                public void Release() => Releases++;
            }
            internal sealed partial class AWCooperativeSimulationRunner {
                private MapBox _world;
                private SimulationStage _stage;
                private readonly BatchRunner _actorRunner = new BatchRunner();
                private readonly BatchRunner _buildingRunner = new BatchRunner();
                private readonly Ownership _resourceOwnership = new Ownership();
                private bool _aborted;
                private int _executedStages;
                private void ExecuteCurrentStage() { _executedStages++; _stage = SimulationStage.Idle; }
                private void Abort() { _aborted = true; _stage = SimulationStage.Idle; }

                private static AWCooperativeSimulationRunner New(bool actor) {
                    World.world = new MapBox();
                    Config.game_loaded = true;
                    SmoothLoader.Loading = false;
                    AW3MultiplayerReplicaScope.IsReplicaSession = false;
                    AWSimulationTickBenchmark.Suspends = AWSimulationTickBenchmark.Resumes = 0;
                    return new AWCooperativeSimulationRunner {
                        _world = World.world, _stage = actor ? SimulationStage.Actors : SimulationStage.Buildings
                    };
                }
                private static void Invalidate(AWCooperativeSimulationRunner runner, string reason) {
                    switch (reason) {
                        case "paused": runner._world.Paused = true; break;
                        case "not-loaded": Config.game_loaded = false; break;
                        case "loading": SmoothLoader.Loading = true; break;
                        case "replica": AW3MultiplayerReplicaScope.IsReplicaSession = true; break;
                        case "changed-world": World.world = new MapBox(); break;
                        case "missing-world": World.world = null; break;
                        case "detached-runner": runner._world = null; break;
                    }
                }
                private static void Restore(AWCooperativeSimulationRunner runner) {
                    runner._world ??= new MapBox();
                    World.world = runner._world;
                    runner._world.Paused = false;
                    Config.game_loaded = true;
                    SmoothLoader.Loading = false;
                    AW3MultiplayerReplicaScope.IsReplicaSession = false;
                }
                public static void RunCases() {
                    int cases = 0;
                    string[] reasons = { "paused", "not-loaded", "loading", "replica", "changed-world", "missing-world", "detached-runner" };
                    foreach (string reason in reasons) {
                        foreach (bool actor in new[] { true, false }) {
                            foreach (bool finish in new[] { true, false }) {
                                DispatchResumesAfterBoundary(reason, actor, finish); cases++;
                            }
                            ReadBoundaryStillJoins(reason, actor); cases++;
                        }
                    }
                    foreach (bool actor in new[] { true, false }) { PausedSaveStillDrains(actor); cases++; }
                    ReplicaSaveAborts(); cases++;
                    SaveJoinFailureStillReleasesResources(); cases++;
                    Console.WriteLine("Presentation dispatch lifecycle: " + cases + " cases passed.");
                }
                private static void DispatchResumesAfterBoundary(string reason, bool actor, bool finish) {
                    var runner = New(actor);
                    var work = actor ? runner._actorRunner : runner._buildingRunner;
                    Invalidate(runner, reason);
                    if (finish) runner.FinishPresentationFrame();
                    else Require(!(actor ? runner.TryBeginActorPresentationOverlap() : runner.TryBeginBuildingPresentationOverlap()),
                        reason + " rejects presentation overlap");
                    Require(work.Launches == 0 && work.WaitingForPresentationDispatch,
                        reason + " must leave queued simulation work untouched during rendering");
                    Restore(runner);
                    if (finish) runner.FinishPresentationFrame();
                    else Require(actor ? runner.TryBeginActorPresentationOverlap() : runner.TryBeginBuildingPresentationOverlap(),
                        "restored world can launch deferred work");
                    Require(work.Launches == 1, "restoring the boundary resumes exactly one dispatch");
                }
                private static void ReadBoundaryStillJoins(string reason, bool actor) {
                    var runner = New(actor);
                    var work = actor ? runner._actorRunner : runner._buildingRunner;
                    work.HasParallelPresentationWorkInFlight = true;
                    Invalidate(runner, reason);
                    Require(actor ? runner.EnsureActorReadBoundary(reason) : runner.EnsureBuildingReadBoundary(reason),
                        "read boundary must join previously dispatched work during " + reason);
                    Require(work.Joins == 1 && work.Launches == 0 && !work.HasParallelPresentationWorkInFlight,
                        "read boundary joins once without dispatching new simulation work");
                    Require(!(actor ? runner.EnsureActorReadBoundary(reason) : runner.EnsureBuildingReadBoundary(reason)),
                        "completed work is not joined twice");
                }
                private static void PausedSaveStillDrains(bool actor) {
                    var runner = New(actor);
                    runner._world.Paused = true;
                    runner.DrainToBoundary();
                    var work = actor ? runner._actorRunner : runner._buildingRunner;
                    Require(!runner.Active && work.Launches == 1 && work.Joins == 1 && runner._executedStages == 1,
                        "explicit save completes deferred work even when presentation cannot dispatch it");
                    Require(runner._resourceOwnership.Releases == 1 && AWSimulationTickBenchmark.Resumes == 1,
                        "save returns native resources and resumes benchmark state");
                }
                private static void ReplicaSaveAborts() {
                    var runner = New(true);
                    AW3MultiplayerReplicaScope.IsReplicaSession = true;
                    runner.DrainToBoundary();
                    Require(runner._aborted && runner._actorRunner.Launches == 0,
                        "replica save cannot execute authoritative simulation work");
                }
                private static void SaveJoinFailureStillReleasesResources() {
                    var runner = New(true);
                    runner._world.Paused = true;
                    runner._actorRunner.ThrowOnComplete = true;
                    bool threw = false;
                    try { runner.DrainToBoundary(); }
                    catch (InvalidOperationException) { threw = true; }
                    Require(threw && runner._aborted && runner._resourceOwnership.Releases == 1 &&
                        AWSimulationTickBenchmark.Resumes == 1,
                        "failed save join propagates and still unwinds ownership and timing state");
                }
                private static void Require(bool value, string message) {
                    if (!value) throw new InvalidOperationException(message);
                }
            }
        }
        """;
}
