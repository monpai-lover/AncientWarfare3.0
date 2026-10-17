using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class TemporaryLevyRequestRuntimeTests
{
    private static Assembly runtime;

    internal static void Run(bool diagnostics)
    {
        runtime ??= CompileProductionRequest();
        try
        {
            runtime.GetType("LevyRequestHarness").GetMethod("Run").Invoke(null, new object[] { diagnostics });
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static Assembly CompileProductionRequest()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "Code/core/lineage/TemporaryLevyService.cs"));
        var declaration = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "TemporaryLevyService");
        // Compile the exact production overloads and plan representation. Only
        // engine access and downstream work scheduling are replaced below.
        var methods = declaration.Members.OfType<MethodDeclarationSyntax>().Where(m =>
            m.Identifier.Text is "RequestOffensiveRecovery" or "HasPendingOffensiveRecovery" or "PendingDemand").ToArray();
        if (methods.Count(m => m.Identifier.Text == "RequestOffensiveRecovery") != 5)
            throw new InvalidOperationException("The production request overload set changed; update this execution harness.");
        var plan = declaration.Members.OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "CasualtyReinforcementPlan");
        string production = "using System; using System.Collections.Generic; using AncientWarfare3.core.performance; " +
            "namespace AncientWarfare3.core.lineage { internal static partial class TemporaryLevyService { " +
            plan.ToFullString() + string.Join("\n", methods.Select(m => m.ToFullString())) + " } }";

        // The queue's demand arithmetic also comes from production source.
        string rulesSource = File.ReadAllText(Path.Combine(root, "Code/core/lineage/TemporaryLevyRules.cs"));
        var rules = CSharpSyntaxTree.ParseText(rulesSource).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "TemporaryLevyRules");
        string[] selectedRules = { "AddReplenishmentDemand", "MergeDirectedReplenishmentDemand",
            "ShouldResetCasualtyReinforcementProgress", "ShouldForceEmergencyRecoverySlots" };
        string ruleSubset = "namespace AncientWarfare3.core.lineage { public static class TemporaryLevyRules { " +
            string.Join("\n", rules.Members.Where(m => m is FieldDeclarationSyntax f && f.Modifiers.Any(SyntaxKind.ConstKeyword) ||
                m is MethodDeclarationSyntax method && selectedRules.Contains(method.Identifier.Text)).Select(m => m.ToFullString())) + " } }";
        string diagnostics = File.ReadAllText(Path.Combine(root, "Code/core/lineage/TemporaryLevyDiagnosticRules.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProductionLevyRequestRuntime", new[]
        {
            CSharpSyntaxTree.ParseText(production), CSharpSyntaxTree.ParseText(ruleSubset),
            CSharpSyntaxTree.ParseText(diagnostics), CSharpSyntaxTree.ParseText(HarnessSource)
        }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Code/core/lineage/TemporaryLevyService.cs")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Cannot find production levy source.");
    }

    private const string HarnessSource = """
        using System;
        using System.Collections.Generic;
        using AncientWarfare3.core.lineage;
        public sealed class Data { public long id; }
        public class Entity
        {
            public Data data = new Data();
            // Match the game DLL: NanoObject.id -> CoreSystemObject.getID -> data.id.
            public long id => data.id;
            public bool Dead;
            public bool isRekt() => data == null || Dead;
        }
        public sealed class Kingdom : Entity { }
        public sealed class Army : Entity { }
        public sealed class City : Entity { public Kingdom kingdom; }
        namespace AncientWarfare3
        {
            internal static class ModClass
            {
                public static readonly List<string> Logs = new List<string>();
                public static void LogInfo(string message) => Logs.Add(message);
            }
        }
        namespace AncientWarfare3.core.performance
        {
            internal static class AWPerformanceSettings { public static bool ArmyRtsDiagnosticsEnabled; }
        }
        namespace AncientWarfare3.core.lineage
        {
            internal static class MilitaryEmergencyService
            {
                public static bool Active = true;
                public static bool HasAny(Kingdom kingdom) => Active;
            }
            internal static class AutonomousRestorationService
            {
                public static bool Active;
                public static bool IsActiveCampaignKingdom(Kingdom kingdom) => Active;
            }
            internal static partial class TemporaryLevyService
            {
                private static readonly Dictionary<long, CasualtyReinforcementPlan> CasualtyReinforcementPlans =
                    new Dictionary<long, CasualtyReinforcementPlan>();
                private static readonly TemporaryLevyDiagnosticSampler DiagnosticSampler = new TemporaryLevyDiagnosticSampler(1, 100);
                public static int Scheduled;
                private static void ScheduleCasualtyReinforcement(CasualtyReinforcementPlan plan) => Scheduled++;
                private static double CurrentRealtime() => 10;
                public static void Reset()
                {
                    CasualtyReinforcementPlans.Clear(); DiagnosticSampler.Clear(); Scheduled = 0;
                    MilitaryEmergencyService.Active = true; AutonomousRestorationService.Active = false;
                    AncientWarfare3.ModClass.Logs.Clear();
                }
                public static int Demand => CasualtyReinforcementPlans.Count == 0 ? 0 :
                    PendingDemand(CasualtyReinforcementPlans[1]);
                public static int GeneralDemand => CasualtyReinforcementPlans[1].PendingDemand;
                public static long PreferredCity => CasualtyReinforcementPlans[1].PreferredCityId;
            }
        }
        public static class LevyRequestHarness
        {
            public static void Run(bool diagnostics)
            {
                AncientWarfare3.core.performance.AWPerformanceSettings.ArmyRtsDiagnosticsEnabled = diagnostics;
                TemporaryLevyService.Reset();
                var disposedKingdom = new Kingdom { data = null };
                Require(!TemporaryLevyService.RequestOffensiveRecovery(disposedKingdom, null, 6, true),
                    "disposed kingdom must reject recovery");
                Require(TemporaryLevyService.Scheduled == 0 && TemporaryLevyService.Demand == 0,
                    "disposed kingdom created recruitment demand");

                TemporaryLevyService.Reset();
                var kingdom = new Kingdom { data = new Data { id = 1 } };
                var city = new City { data = new Data { id = 2 }, kingdom = kingdom };
                TemporaryLevyService.RequestOffensiveRecovery(kingdom, city, 6, new Army { data = null });
                Require(TemporaryLevyService.Scheduled == 0 && TemporaryLevyService.Demand == 0,
                    "disposed directed target became unrelated general recruitment demand");

                TemporaryLevyService.Reset();
                TemporaryLevyService.RequestOffensiveRecovery(kingdom, city, 6,
                    new Army { data = new Data { id = 3 }, Dead = true });
                Require(TemporaryLevyService.Scheduled == 0 && TemporaryLevyService.Demand == 0,
                    "dead directed target scheduled recruitment");

                TemporaryLevyService.Reset();
                Require(TemporaryLevyService.RequestOffensiveRecovery(kingdom, new City { data = null }, 6, true),
                    "a disposed optional preferred city blocked otherwise valid general recovery");
                Require(TemporaryLevyService.Scheduled == 1 && TemporaryLevyService.Demand == 6 &&
                    TemporaryLevyService.PreferredCity == -1,
                    "disposed preference must be ignored without retaining its id");

                TemporaryLevyService.Reset();
                MilitaryEmergencyService.Active = false;
                Require(!TemporaryLevyService.RequestOffensiveRecovery(kingdom, new City { data = null }, 6, true),
                    "missing emergency state must reject recovery");
                Require(TemporaryLevyService.Scheduled == 0 && TemporaryLevyService.Demand == 0,
                    "rejected request mutated recruitment demand");

                TemporaryLevyService.Reset();
                AutonomousRestorationService.Active = true;
                Require(!TemporaryLevyService.RequestOffensiveRecovery(kingdom, city, 6, true),
                    "restoration campaign must retain its recruitment ownership");
                Require(TemporaryLevyService.Scheduled == 0 && TemporaryLevyService.Demand == 0,
                    "restoration rejection mutated recruitment demand");

                TemporaryLevyService.Reset();
                TemporaryLevyService.RequestOffensiveRecovery(kingdom, city, 6, new Army { data = new Data { id = 3 } });
                Require(TemporaryLevyService.Scheduled == 1 && TemporaryLevyService.Demand == 6 &&
                    TemporaryLevyService.GeneralDemand == 0 && TemporaryLevyService.PreferredCity == 2,
                    "valid directed recovery changed demand ownership or preferred city");
                Require(AncientWarfare3.ModClass.Logs.Count == (diagnostics ? 1 : 0),
                    "the diagnostics setting did not control production logging");
            }
            private static void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }
        }
        """;
}
