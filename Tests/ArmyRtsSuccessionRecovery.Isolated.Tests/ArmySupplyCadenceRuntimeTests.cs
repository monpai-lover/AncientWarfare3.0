using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class ArmySupplyCadenceRuntimeTests
{
    private static Assembly runtime;
    internal static void Run(string scenario)
    {
        runtime ??= CompileRuntime();
        try { runtime.GetType("ArmySupplyCadenceCases").GetMethod(scenario).Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }
    private static Assembly CompileRuntime()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
                   "Code/core/lineage/ArmyRtsAbstractSupplyService.cs"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Production supply source missing.");
        var sources = new[] { "ArmyRtsAbstractSupplyService", "ArmyRtsAbstractSupplyRules" }
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,
                "Code/core/lineage", file + ".cs")))).Append(CSharpSyntaxTree.ParseText(Harness));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProductionArmySupplyCadence", sources,
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using AncientWarfare3.core.lineage;
        public sealed class Data { public long id; public string favorite_food = "food"; }
        public sealed class Kingdom { }
        public sealed class ResourceAsset { public string id = "food"; }
        public sealed class City
        {
            public Data data = new Data(); public Kingdom kingdom;
            public int Eaten;
            public bool isRekt() => false;
            public bool hasSuitableFood(object subspecies) => true;
            public ResourceAsset getFoodItem(object subspecies, string favorite) => new ResourceAsset();
            public void eatFoodItem(string id) => Eaten++;
        }
        public sealed class Army { public City Anchor; }
        public sealed class Actor
        {
            public Data data = new Data(); public Army army; public Kingdom kingdom;
            public object subspecies; public bool Hungry; public int Consumed;
            public bool isAlive() => data != null;
            public bool isRekt() => data == null;
            public bool needsFood() => true;
            public bool isHungry() => Hungry;
            public void consumeFoodResource(ResourceAsset food) => Consumed++;
        }
        namespace UnityEngine { public static class Time { public static double realtimeSinceStartupAsDouble; } }
        namespace AncientWarfare3.core.lineage
        {
            internal static class ArmyRtsControllerService
            {
                public static int OwnerQueries;
                public static bool HasActiveMilitaryP0Owner(Actor actor) { OwnerQueries++; return true; }
            }
            internal static class RoyalGuardService { public static bool IsRoyalGuard(Actor actor) => false; }
            internal static class AWArmyService { public static City FindAnchorCity(Army army) => army?.Anchor; }
        }
        public static class ArmySupplyCadenceCases
        {
            public static void LargeRosterKeepsCooldown()
            {
                Reset();
                var actors = new List<Actor>();
                for (int i = 0; i < 16385; i++) actors.Add(NewActor(i));
                foreach (var actor in actors) ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor);
                Check(ArmyRtsControllerService.OwnerQueries == actors.Count, "Initial roster did not receive one evaluation each.");
                ArmyRtsControllerService.OwnerQueries = 0;
                UnityEngine.Time.realtimeSinceStartupAsDouble = 0.1;
                foreach (var actor in actors) ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor);
                Check(ArmyRtsControllerService.OwnerQueries == 0,
                    "Cooldown evicted live roster entries: repeated owner queries=" + ArmyRtsControllerService.OwnerQueries);
                UnityEngine.Time.realtimeSinceStartupAsDouble = 1;
                foreach (var actor in actors) ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor);
                Check(ArmyRtsControllerService.OwnerQueries == actors.Count, "Overflow roster members starved after the cooldown interval.");
            }
            public static void HungryActorConsumesOncePerInterval()
            {
                Reset(); var actor = NewActor(1); actor.Hungry = true;
                Check(ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor), "Initial ration was not consumed.");
                UnityEngine.Time.realtimeSinceStartupAsDouble = 0.5;
                Check(!ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor) && actor.Consumed == 1,
                    "Same interval consumed a duplicate ration.");
                UnityEngine.Time.realtimeSinceStartupAsDouble = 1;
                Check(ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor) && actor.Consumed == 2 && actor.army.Anchor.Eaten == 2,
                    "Expired interval did not resume actual food consumption.");
            }
            public static void RuntimeResetClearsCooldown()
            {
                Reset(); var actor = NewActor(1);
                ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor);
                ArmyRtsAbstractSupplyService.ClearRuntime();
                ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor);
                Check(ArmyRtsControllerService.OwnerQueries == 2, "World reset retained the previous cooldown.");
            }
            public static void CacheDoesNotRetainRemovedActors()
            {
                Reset(); WeakReference reference = ObserveTemporaryActor();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Check(!reference.IsAlive, "The cooldown cache retained an otherwise unreachable actor.");
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static WeakReference ObserveTemporaryActor()
            {
                var actor = NewActor(1); ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor);
                return new WeakReference(actor);
            }
            public static void InvalidActorsAreIgnored()
            {
                Reset(); var actor = NewActor(1); actor.data = null;
                Check(!ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(null) &&
                    !ArmyRtsAbstractSupplyService.TryConsumeHomeRationScheduled(actor) && ArmyRtsControllerService.OwnerQueries == 0,
                    "Invalid actors entered the scheduled supply query.");
            }
            private static Actor NewActor(long id)
            {
                var kingdom = new Kingdom();
                return new Actor { data = new Data { id = id }, kingdom = kingdom,
                    army = new Army { Anchor = new City { kingdom = kingdom } } };
            }
            private static void Reset()
            {
                ArmyRtsAbstractSupplyService.ClearRuntime(); ArmyRtsControllerService.OwnerQueries = 0;
                UnityEngine.Time.realtimeSinceStartupAsDouble = 0;
            }
            private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        }
        """;
}
