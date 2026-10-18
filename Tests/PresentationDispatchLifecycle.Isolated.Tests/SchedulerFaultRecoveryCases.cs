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
            "FinalizeFailedMapBoxUpdate",
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
        string[] faultFields = { "_faulted", "_faultHasNullReference", "_faultMessage" };
        var governorMembers = governor.Members.OfType<MethodDeclarationSyntax>().Where(method =>
                method.Identifier.Text is "CanRun" or "MarkFault" or "ResetFault" or
                    "ContainsNullReferenceException")
            .Select(method => method.ToFullString()).ToList();
        governorMembers.AddRange(governor.Members.OfType<FieldDeclarationSyntax>().Where(field =>
                field.Declaration.Variables.All(variable => faultFields.Contains(variable.Identifier.Text)))
            .Select(field => field.ToFullString()));
        governorMembers.AddRange(governor.Members.OfType<PropertyDeclarationSyntax>().Where(property =>
                property.Identifier.Text is "Faulted" or "FaultHasNullReference" or "FaultMessage")
            .Select(property => property.ToFullString()));
        string productionGovernor = "using System; using System.Collections.Generic; namespace AncientWarfare3.core.performance { " +
            "internal static partial class AWFramePriorityGovernor { " + string.Join("\n",
                governorMembers) + " } }";
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
                public Exception FrameException;
                public Action AfterAbort;
                public int Completed, Aborts, Releases;
                public bool RequiresControl => AWPerformanceSettings.EnableFramePriorityScheduler || Active;
                public void RunFrame(MapBox map, bool allow) {
                    if (Config.paused || !AWFramePriorityGovernor.CanRun("test.phase")) return;
                    if (ThrowOnFrame) { ThrowOnFrame = false; throw FrameException ?? new InvalidOperationException("injected frame failure"); }
                    Completed++;
                }
                public void Abort() {
                    Aborts++;
                    if (ThrowOnAbort) throw new InvalidOperationException("injected abort failure");
                    Active = false;
                    AfterAbort?.Invoke();
                }
                public void ReleaseControl() => Releases++;
                public void FinishPresentationFrame() {}
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
            internal static class AWEmergencyNullReferenceRepairService {
                public static int Repairs;
                public static Action OnRepair;
                public static MapBox RepairedWorld;
                public static string Repair(MapBox world) {
                    Repairs++;
                    RepairedWorld = world;
                    OnRepair?.Invoke();
                    return "repaired=1 skipped=0";
                }
            }
        }
        namespace AncientWarfare3.patch {
            internal static partial class AW_FramePrioritySchedulerPatch {
                private struct MapBoxUpdateScope {}
                private static void CloseHostMeasurement(ref MapBoxUpdateScope scope) {}
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
                        ("simulation-phase reentry cannot abort its own work", PhaseDoesNotRecover),
                        ("null-reference faults repair only after worker teardown", RepairNullReferenceAfterTeardown),
                        ("wrapped aggregate null-reference faults recover from the hotkey", RepairWrappedAggregateAfterHotkey),
                        ("non-null-reference failures do not mutate world references", NonNullReferenceDoesNotRepair),
                        ("programmatic unpause cannot trigger reference repair", NoAutomaticRepair),
                        ("failed teardown preserves the repair requirement for retry", FailedTeardownBeforeRepair),
                        ("failed reference repair stays paused and remains retryable", FailedRepair),
                        ("a world switch during teardown prevents repair", WorldChangesDuringTeardown),
                        ("direct and wrapped null-reference exceptions are classified", NullReferenceClassification),
                        ("all aggregate branches are checked for null references", AggregateClassification),
                        ("unrelated failures and unknown errors are not classified as null references", NonNullReferenceClassification),
                        ("explicit repair requirement survives a different cleanup exception", PreserveRepairRequirement),
                        ("later unrelated errors preserve pending reference repair", LaterFaultPreservesRepairRequirement),
                        ("later fault handling preserves reference repair until explicit resume", LaterFaultHandlerPreservesRepairRequirement),
                        ("map update finalization preserves reference repair until explicit resume", FinalizerPreservesRepairRequirement),
                        ("reset clears the fault classification", ResetClearsClassification)
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
                    Runner.FrameException = null; Runner.AfterAbort = null;
                    ResetSchedulerState(false, true);
                    Runner.Completed = Runner.Aborts = Runner.Releases = 0;
                    AWAuthorityCycleService.Resets = 0;
                    Config.paused = false; Config.game_loaded = true; SmoothLoader.Loading = false;
                    AW3MultiplayerReplicaScope.IsReplicaSession = false; AWWorldInitializationGate.Pending = false;
                    AWFramePriorityGovernor.IsExecutingSimulationPhase = false;
                    AWPerformanceSettings.EnableFramePriorityScheduler = true;
                    World.world = new MapBox(); ModClass.Errors.Clear();
                    AWEmergencyNullReferenceRepairService.Repairs = 0;
                    AWEmergencyNullReferenceRepairService.OnRepair = null;
                    AWEmergencyNullReferenceRepairService.RepairedWorld = null;
                }
                private static void InjectFrameFailure(Exception error = null) {
                    Runner.FrameException = error;
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
                private static void RepairNullReferenceAfterTeardown() {
                    InjectFrameFailure(new NullReferenceException("expired actor"));
                    int previousAborts = Runner.Aborts;
                    bool teardownFinished = false;
                    Runner.AfterAbort = () => {
                        Require(Config.paused && AWFramePriorityGovernor.Faulted,
                            "teardown must retain both pause and the fault latch");
                        teardownFinished = true;
                    };
                    AWEmergencyNullReferenceRepairService.OnRepair = () => {
                        Require(teardownFinished && Runner.Aborts == previousAborts + 1,
                            "reference repair must follow a successful worker join");
                        Require(Config.paused && AWFramePriorityGovernor.Faulted,
                            "repair cannot admit simulation before it completes");
                        Require(!AWFramePriorityGovernor.CanRun("repair"),
                            "the governor must reject work while references are being repaired");
                    };
                    MapBox failedWorld = World.world;
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 1 &&
                        ReferenceEquals(AWEmergencyNullReferenceRepairService.RepairedWorld, failedWorld),
                        "explicit resume must repair the failed world exactly once");
                    Require(!Config.paused && !AWFramePriorityGovernor.Faulted &&
                        !AWFramePriorityGovernor.FaultHasNullReference,
                        "completed repair must clear the fault and its classification");
                    TakeOverMainSimulation(World.world, out _);
                    Require(Runner.Completed == 1, "the repaired simulation must advance");
                }
                private static void RepairWrappedAggregateAfterHotkey() {
                    InjectFrameFailure(new InvalidOperationException("worker boundary",
                        new AggregateException(new Exception("unrelated worker"),
                            new InvalidOperationException("job", new NullReferenceException("expired target")))));
                    UserTogglePause(true);
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 1 &&
                        !Config.paused && !AWFramePriorityGovernor.Faulted,
                        "the keyboard resume must repair null references in any aggregate branch");
                }
                private static void NonNullReferenceDoesNotRepair() {
                    InjectFrameFailure(new AggregateException(new InvalidOperationException("job"),
                        new ArgumentNullException("requiredInput")));
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 0 && !Config.paused &&
                        !AWFramePriorityGovernor.Faulted,
                        "argument validation and unrelated failures must only use ordinary safe teardown");
                }
                private static void NoAutomaticRepair() {
                    InjectFrameFailure(new NullReferenceException("expired target"));
                    Config.paused = false;
                    TakeOverMainSimulation(World.world, out _);
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 0 && Runner.Completed == 0 &&
                        AWFramePriorityGovernor.Faulted && AWFramePriorityGovernor.FaultHasNullReference,
                        "programmatic unpause must neither repair nor clear a null-reference fault");
                }
                private static void FailedTeardownBeforeRepair() {
                    InjectFrameFailure(new NullReferenceException("expired target"));
                    Runner.ThrowOnAbort = true;
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 0 && Config.paused &&
                        AWFramePriorityGovernor.Faulted && AWFramePriorityGovernor.FaultHasNullReference,
                        "failed worker join must skip repair and preserve its original requirement");
                    Runner.ThrowOnAbort = false;
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 1 && !Config.paused &&
                        !AWFramePriorityGovernor.Faulted,
                        "a later safe explicit retry must still perform the pending reference repair");
                }
                private static void FailedRepair() {
                    InjectFrameFailure(new NullReferenceException("expired target"));
                    AWEmergencyNullReferenceRepairService.OnRepair = () => {
                        Require(Config.paused && AWFramePriorityGovernor.Faulted,
                            "repair starts while simulation is blocked");
                        throw new InvalidOperationException("injected repair failure");
                    };
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 1 && Config.paused &&
                        AWFramePriorityGovernor.Faulted && AWFramePriorityGovernor.FaultHasNullReference,
                        "failed repair must retain pause and null-reference classification");
                    Require(Runner.Completed == 0 && AWFramePriorityGovernor.FaultMessage.Contains("injected repair failure"),
                        "failed repair must remain diagnosable and cannot run simulation");
                    AWEmergencyNullReferenceRepairService.OnRepair = null;
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 2 && !Config.paused &&
                        !AWFramePriorityGovernor.Faulted,
                        "a later explicit retry must attempt repair again before resuming");
                }
                private static void WorldChangesDuringTeardown() {
                    InjectFrameFailure(new NullReferenceException("expired target"));
                    Runner.AfterAbort = () => World.world = new MapBox();
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 0 && Config.paused &&
                        AWFramePriorityGovernor.Faulted && AWFramePriorityGovernor.FaultHasNullReference,
                        "teardown changing the world must not repair or resume a different world");
                }
                private static void NullReferenceClassification() {
                    foreach (Exception error in new Exception[] {
                        new NullReferenceException("direct"),
                        new InvalidOperationException("outer", new NullReferenceException("inner")),
                        new NullReferenceException("outer null", new InvalidOperationException("inner non-null")),
                        new EqualException(new EqualException(new NullReferenceException("reference identity")))
                    }) {
                        AWFramePriorityGovernor.ResetFault();
                        AWFramePriorityGovernor.MarkFault(error);
                        Require(AWFramePriorityGovernor.FaultHasNullReference,
                            "every null-reference node must classify the complete exception graph");
                    }
                    Exception deep = new NullReferenceException("deep inner");
                    for (int index = 0; index < 512; index++) deep = new Exception("wrapper", deep);
                    AWFramePriorityGovernor.ResetFault();
                    AWFramePriorityGovernor.MarkFault(deep);
                    Require(AWFramePriorityGovernor.FaultHasNullReference, "deep wrappers must not be truncated");
                }
                private static void AggregateClassification() {
                    var nullReference = new NullReferenceException("worker");
                    foreach (Exception error in new Exception[] {
                        new AggregateException(new Exception("first"), nullReference),
                        new AggregateException(nullReference, new Exception("last")),
                        new Exception("outer", new AggregateException(new Exception("first"),
                            new AggregateException(new Exception("other"),
                                new Exception("wrapper", nullReference)))),
                        new AggregateException(new Exception("shared", nullReference), nullReference)
                    }) {
                        AWFramePriorityGovernor.ResetFault();
                        AWFramePriorityGovernor.MarkFault(error);
                        Require(AWFramePriorityGovernor.FaultHasNullReference,
                            "all aggregate branches and wrapped inner exceptions must be inspected");
                    }
                }
                private static void NonNullReferenceClassification() {
                    foreach (Exception error in new Exception[] {
                        null,
                        new ArgumentNullException("requiredInput"),
                        new Exception("NullReferenceException text is not an exception type"),
                        new AggregateException(new Exception("one"),
                            new InvalidOperationException("two", new ArgumentNullException("input")))
                    }) {
                        AWFramePriorityGovernor.MarkFault(error);
                        Require(AWFramePriorityGovernor.Faulted && !AWFramePriorityGovernor.FaultHasNullReference,
                            "unrelated faults must not create a null-reference repair requirement");
                    }
                }
                private static void PreserveRepairRequirement() {
                    AWFramePriorityGovernor.MarkFault(new InvalidOperationException("cleanup failed"),
                        pRequireNullReferenceRepair: true);
                    Require(AWFramePriorityGovernor.Faulted && AWFramePriorityGovernor.FaultHasNullReference,
                        "a recovery failure must preserve the original repair requirement");
                }
                private static void LaterFaultPreservesRepairRequirement() {
                    AWFramePriorityGovernor.MarkFault(new NullReferenceException("expired target"));
                    AWFramePriorityGovernor.MarkFault(new InvalidOperationException("later failure"));
                    Require(AWFramePriorityGovernor.FaultHasNullReference &&
                        AWFramePriorityGovernor.FaultMessage.Contains("later failure"),
                        "later diagnostics must not discard pending null-reference repair");
                    AWFramePriorityGovernor.MarkFault(null);
                    Require(AWFramePriorityGovernor.FaultHasNullReference,
                        "an unknown later failure must not discard pending null-reference repair");
                }
                private static void LaterFaultHandlerPreservesRepairRequirement() {
                    InjectFrameFailure(new NullReferenceException("expired target"));
                    HandleBackgroundSimulationFault(new InvalidOperationException("later failure"));
                    Require(Config.paused && AWFramePriorityGovernor.Faulted &&
                        AWFramePriorityGovernor.FaultHasNullReference,
                        "later handler teardown must not discard pending repair before explicit resume");
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 1 && !Config.paused &&
                        !AWFramePriorityGovernor.Faulted,
                        "explicit resume must repair after intervening unrelated failures");
                }
                private static void FinalizerPreservesRepairRequirement() {
                    InjectFrameFailure(new NullReferenceException("expired target"));
                    var scope = new MapBoxUpdateScope();
                    FinalizeFailedMapBoxUpdate(new InvalidOperationException("later map failure"), ref scope);
                    Require(Config.paused && AWFramePriorityGovernor.Faulted &&
                        AWFramePriorityGovernor.FaultHasNullReference,
                        "map finalizer teardown must retain pending repair until explicit resume");
                    UserTogglePause();
                    Require(AWEmergencyNullReferenceRepairService.Repairs == 1 && !Config.paused &&
                        !AWFramePriorityGovernor.Faulted,
                        "explicit resume must repair after a later map update exception");
                }
                private static void ResetClearsClassification() {
                    AWFramePriorityGovernor.MarkFault(new NullReferenceException("expired"));
                    AWFramePriorityGovernor.ResetFault();
                    Require(!AWFramePriorityGovernor.Faulted && !AWFramePriorityGovernor.FaultHasNullReference &&
                        AWFramePriorityGovernor.FaultMessage == "",
                        "fault reset must clear diagnostics and repair eligibility together");
                }
                private sealed class EqualException : Exception {
                    internal EqualException(Exception inner) : base("equal", inner) {}
                    public override bool Equals(object other) => other is Exception;
                    public override int GetHashCode() => 0;
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
