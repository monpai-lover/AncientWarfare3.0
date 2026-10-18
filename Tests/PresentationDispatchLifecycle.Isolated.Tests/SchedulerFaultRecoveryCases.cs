using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SchedulerFaultRecoveryCases
{
    internal static void Run(string root, string patchOverride)
    {
        string patchPath = patchOverride ?? Path.Combine(root,
            "Code/patch/AW_FramePrioritySchedulerPatch.cs");
        var patch = ReadClass(patchPath, "AW_FramePrioritySchedulerPatch");
        string[] methods = {
            "TakeOverMainSimulation", "HandleBackgroundSimulationFault",
            "ResetSchedulerState", "RunCleanup", "EnsureSimulationTimeBound",
            "CaptureUserPauseState", "ResumeSchedulerAfterUserUnpause",
            "CaptureUserHotkeyPauseState", "ResumeSchedulerAfterUserHotkeyUnpause"
        };
        string[] fields = { "_schedulerLifecycleOwned", "_reportedThirdPartySchedulerFault",
            "_pendingAutoSave", "_pendingAutoSaveSkipDelete", "_pendingAutoSaveForce",
            "_bypassAutoSaveDeferral" };
        var members = patch.Members.OfType<MethodDeclarationSyntax>()
            .Where(method => methods.Contains(method.Identifier.Text))
            .Select(method => method.WithAttributeLists(default).ToFullString()).ToList();
        if (methods.Take(methods.Length - 4).Any(name => !patch.Members
                .OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.Text == name)))
            throw new InvalidOperationException("Scheduler recovery extraction is incomplete.");
        members.AddRange(patch.Members.OfType<FieldDeclarationSyntax>().Where(field =>
                field.Declaration.Variables.All(variable => fields.Contains(variable.Identifier.Text)))
            .Select(field => field.ToFullString()));
        string productionPatch = "using System; using AncientWarfare3.core.performance; " +
            "namespace AncientWarfare3.patch { internal static partial class AW_FramePrioritySchedulerPatch { " +
            string.Join("\n", members) + " } }";

        var governor = ReadClass(Path.Combine(root,
            "Code/core/performance/AWFramePriorityGovernor.cs"), "AWFramePriorityGovernor");
        string productionGovernor = "using System; namespace AncientWarfare3.core.performance { " +
            "internal static partial class AWFramePriorityGovernor { " + string.Join("\n",
                governor.Members.OfType<MethodDeclarationSyntax>().Where(method =>
                    method.Identifier.Text is "CanRun" or "MarkFault" or "ResetFault")
                    .Select(method => method.ToFullString())) + " } }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(file => MetadataReference.CreateFromFile(file));
        var compilation = CSharpCompilation.Create("SchedulerFaultRecoveryRuntime", new[] {
            CSharpSyntaxTree.ParseText(productionPatch),
            CSharpSyntaxTree.ParseText(productionGovernor),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
                "Code/core/performance/AWThirdPartySchedulerFaultRules.cs"))),
            CSharpSyntaxTree.ParseText(Harness)
        }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        var runtime = Assembly.Load(stream.ToArray());
        try { runtime.GetType("AncientWarfare3.patch.AW_FramePrioritySchedulerPatch")
            .GetMethod("RunCases").Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { throw error.InnerException; }
    }

    private static ClassDeclarationSyntax ReadClass(string path, string name) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.Text == name);

    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using System.Reflection;
        using AncientWarfare3.core.performance;
        public sealed class MapBox { public object map_stats = new object(); }
        public static class World { public static MapBox world = new MapBox(); }
        public static class Config { public static bool paused; public static bool game_loaded = true; }
        public static class SmoothLoader { public static bool Loading; public static bool isLoading() => Loading; }
        public static class AW3MultiplayerReplicaScope { public static bool IsReplicaSession; }
        public static class AWWorldInitializationGate { public static bool Pending; public static bool IsPending(MapBox map) => Pending; }
        namespace AncientWarfare3 {
            internal static class ModClass {
                internal static readonly List<string> Errors = new List<string>();
                public static void LogWarning(string text) {}
                public static void LogInfo(string text) {}
                public static void LogError(string text) => Errors.Add(text);
            }
        }
        namespace AncientWarfare3.core.performance {
            internal enum AWSimulationDomain { Vanilla, Aw3Authority }
            internal static class AWPerformanceSettings {
                public static bool EnableFramePriorityScheduler = true;
                public const double MinimumSliceMilliseconds = 0.15;
                public const double RenderReserveMilliseconds = 2;
            }
            internal static partial class AWFramePriorityGovernor {
                private static bool _faulted;
                private static string _faultMessage = "";
                public static bool Faulted => _faulted;
                public static string FaultMessage => _faultMessage;
                public static bool IsExecutingSimulationPhase;
                private static readonly Dictionary<string, double> PhaseEstimates = new();
                private static long _frameStartedAt;
                private static double _simulationCpuMilliseconds;
                private static double _frameBudgetMilliseconds = 10;
                public static double EffectiveTargetRenderFps => 60;
                private static void BeginFrame() {}
                private static double ElapsedMilliseconds(long value) => 0;
                private static bool CanUseStarvationSlice(AWSimulationDomain domain) => true;
            }
            internal sealed class AWCooperativeSimulationRunner {
                public static readonly AWCooperativeSimulationRunner Instance = new();
                public bool Active, ThrowOnFrame, ThrowOnAbort;
                public int Completed, Aborts, Releases;
                public bool RequiresControl => AWPerformanceSettings.EnableFramePriorityScheduler || Active;
                public void RunFrame(MapBox map, bool allow) {
                    if (Config.paused || !AWFramePriorityGovernor.CanRun("test.phase")) return;
                    if (ThrowOnFrame) { ThrowOnFrame = false; throw new InvalidOperationException("injected frame failure"); }
                    Completed++;
                }
                public void Abort() {
                    Aborts++;
                    if (ThrowOnAbort) throw new InvalidOperationException("injected abort failure");
                    Active = false;
                }
                public void ReleaseControl() => Releases++;
            }
            internal static class AWSimulationTime {
                public static bool IsBound;
                public static void BindWorld(MapBox map) => IsBound = true;
                public static void UnbindWorld() => IsBound = false;
                public static void CancelTick() {}
            }
            internal static class AWAuthorityCycleService { public static int Resets; public static void Reset() => Resets++; }
            internal static class AWPresentationCommandQueue { public static void Clear() {} }
            internal static class AWActorPresentationSnapshots { public static void Reset() {} }
            internal static class AWActorPresentationRenderer { public static void Reset() {} }
            internal static class AWWorldObjectPresentationRenderer { public static void Reset() {} }
            internal static class AWActorTransientPresentationFrame { public static void Reset() {} }
            internal static class AWPresentationInterpolator { public static void Reset() {} }
            internal static class AWCursorPresentationLifecycle { public static void Reset() {} }
            internal static class AWStatusPresentationAnimationClock { public static void SetSnapshotMode(bool value) {} }
        }
        namespace AncientWarfare3.patch {
            internal static partial class AW_FramePrioritySchedulerPatch {
                private static AWCooperativeSimulationRunner Runner => AWCooperativeSimulationRunner.Instance;
                public static void RunCases() {
                    var cases = new (string, Action)[] {
                        ("one-shot simulation failure recovers after explicit resume", RecoverAfterUserResume),
                        ("pause hotkey also recovers a failed simulation", RecoverAfterHotkeyResume),
                        ("native-mode fault does not strand a later scheduler switch", NativeFaultRecovery),
                        ("automatic unpause leaves the fault latched", NoAutomaticRecovery),
                        ("failed teardown keeps the game paused until a later successful retry", FailedTeardown),
                        ("ordinary pause controls do not reset a healthy cycle", HealthyPause),
                        ("world loading defers fault recovery", LoadingDoesNotRecover),
                        ("missing world defers fault recovery", MissingWorldDoesNotRecover),
                        ("unloaded game defers fault recovery", UnloadedDoesNotRecover),
                        ("replica sessions cannot recover local simulation", ReplicaDoesNotRecover),
                        ("world initialization defers fault recovery", InitializationDoesNotRecover),
                        ("simulation-phase reentry cannot abort its own work", PhaseDoesNotRecover)
                    };
                    int failures = 0;
                    foreach (var test in cases) {
                        ResetCase();
                        try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
                        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Item1 + ": " + error.Message); }
                    }
                    if (failures > 0) throw new InvalidOperationException(failures + " scheduler fault recovery cases failed.");
                }
                private static void ResetCase() {
                    Runner.ThrowOnAbort = false; Runner.ThrowOnFrame = false; Runner.Active = false;
                    ResetSchedulerState(false, true);
                    Runner.Completed = Runner.Aborts = Runner.Releases = 0;
                    AWAuthorityCycleService.Resets = 0;
                    Config.paused = false; Config.game_loaded = true; SmoothLoader.Loading = false;
                    AW3MultiplayerReplicaScope.IsReplicaSession = false; AWWorldInitializationGate.Pending = false;
                    AWFramePriorityGovernor.IsExecutingSimulationPhase = false;
                    AWPerformanceSettings.EnableFramePriorityScheduler = true;
                    World.world = new MapBox(); ModClass.Errors.Clear();
                }
                private static void InjectFrameFailure() {
                    Runner.ThrowOnFrame = true;
                    TakeOverMainSimulation(World.world, out _);
                    Require(Config.paused && AWFramePriorityGovernor.Faulted, "first failure must pause and latch diagnostics");
                    Require(ModClass.Errors.Count == 1, "first failure must be logged");
                }
                private static void UserTogglePause(bool hotkey = false) {
                    bool wasPaused = Config.paused;
                    object[] state = { wasPaused };
                    typeof(AW_FramePrioritySchedulerPatch).GetMethod(hotkey ? "CaptureUserHotkeyPauseState" : "CaptureUserPauseState",
                        BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, state);
                    // The native pause control has changed Config.paused before its postfix.
                    Config.paused = !Config.paused;
                    typeof(AW_FramePrioritySchedulerPatch).GetMethod(hotkey ? "ResumeSchedulerAfterUserHotkeyUnpause" : "ResumeSchedulerAfterUserUnpause",
                        BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, state);
                }
                private static void RecoverAfterUserResume() {
                    InjectFrameFailure();
                    UserTogglePause();
                    Require(!Config.paused && !AWFramePriorityGovernor.Faulted, "explicit resume must clear a safely cleaned-up fault");
                    TakeOverMainSimulation(World.world, out _);
                    Require(Runner.Completed == 1, "next simulation frame must advance after one-shot failure");
                }
                private static void NoAutomaticRecovery() {
                    InjectFrameFailure();
                    Config.paused = false;
                    TakeOverMainSimulation(World.world, out _);
                    Require(AWFramePriorityGovernor.Faulted && Runner.Completed == 0,
                        "programmatic unpause must not automatically retry a faulted frame");
                }
                private static void RecoverAfterHotkeyResume() {
                    InjectFrameFailure(); UserTogglePause(true);
                    TakeOverMainSimulation(World.world, out _);
                    Require(!Config.paused && !AWFramePriorityGovernor.Faulted && Runner.Completed == 1,
                        "native keyboard action must share safe recovery, including rebound pause keys");
                }
                private static void NativeFaultRecovery() {
                    AWPerformanceSettings.EnableFramePriorityScheduler = false;
                    AWFramePriorityGovernor.MarkFault(new InvalidOperationException("native authority failure"));
                    Config.paused = true;
                    UserTogglePause();
                    AWPerformanceSettings.EnableFramePriorityScheduler = true;
                    TakeOverMainSimulation(World.world, out _);
                    Require(!AWFramePriorityGovernor.Faulted && Runner.Completed == 1,
                        "explicit native recovery must not leave a stale fault for a later scheduler switch");
                }
                private static void FailedTeardown() {
                    InjectFrameFailure(); Runner.ThrowOnAbort = true;
                    UserTogglePause();
                    Require(Config.paused && AWFramePriorityGovernor.Faulted, "cleanup failure must remain paused and faulted");
                    Require(Runner.Completed == 0, "failed cleanup must not admit simulation");
                    Runner.ThrowOnAbort = false; UserTogglePause();
                    TakeOverMainSimulation(World.world, out _);
                    Require(!Config.paused && Runner.Completed == 1, "later safe explicit retry can recover");
                }
                private static void HealthyPause() {
                    Runner.Active = true;
                    UserTogglePause(); UserTogglePause();
                    Require(!Config.paused && Runner.Active && Runner.Aborts == 0, "healthy pause/resume must preserve the active cycle");
                }
                private static void LoadingDoesNotRecover() {
                    CheckBlockedRecovery(() => SmoothLoader.Loading = true);
                }
                private static void MissingWorldDoesNotRecover() {
                    CheckBlockedRecovery(() => World.world = null);
                }
                private static void UnloadedDoesNotRecover() {
                    CheckBlockedRecovery(() => Config.game_loaded = false);
                }
                private static void ReplicaDoesNotRecover() {
                    CheckBlockedRecovery(() => AW3MultiplayerReplicaScope.IsReplicaSession = true);
                }
                private static void InitializationDoesNotRecover() {
                    CheckBlockedRecovery(() => AWWorldInitializationGate.Pending = true);
                }
                private static void PhaseDoesNotRecover() {
                    CheckBlockedRecovery(() => AWFramePriorityGovernor.IsExecutingSimulationPhase = true);
                }
                private static void CheckBlockedRecovery(Action block) {
                    InjectFrameFailure(); block();
                    int aborts = Runner.Aborts;
                    UserTogglePause();
                    Require(Config.paused && AWFramePriorityGovernor.Faulted,
                        "unsafe boundary must preserve both pause and fault state");
                    Require(Runner.Completed == 0 && Runner.Aborts == aborts,
                        "unsafe boundary cannot admit simulation or abort reentrant work");
                }
                private static void Require(bool value, string reason) {
                    if (!value) throw new InvalidOperationException(reason);
                }
            }
        }
        """;
}
