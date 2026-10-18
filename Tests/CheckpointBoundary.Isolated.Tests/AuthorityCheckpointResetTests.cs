using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class AuthorityCheckpointResetTests
{
    internal static void Run()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(),
            "Code/core/performance/AWAuthorityCycleService.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        var reset = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Reset");
        string[] cleanupCalls = reset.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression.ToString())
            .Where(call => call != "core.db.LineageArchiveCheckpointService.Shutdown" &&
                call != "ModClass.LogWarning").Distinct().ToArray();
        var stubs = new List<string>();
        foreach (string call in cleanupCalls)
        {
            string[] parts = call.Split('.');
            string type = parts[0];
            string method = parts[parts.Length - 1];
            stubs.Add(parts.Length == 3
                ? "internal sealed class " + type + " { internal static " + type +
                  " Instance = new(); internal void " + method + "() { Trace.Calls.Add(\"" + call + "\"); } }"
                : "internal static class " + type + " { internal static void " + method +
                  "() { Trace.Calls.Add(\"" + call + "\"); } }");
        }
        string source = "using System; using System.Collections.Generic; " +
            string.Join("\n", stubs) +
            " public static class Runtime { private static long _nativeCycleToken; " + reset + " } " +
            Harness.Replace("EXPECTED_CLEANUPS", cleanupCalls.Length.ToString());
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(file => MetadataReference.CreateFromFile(file));
        var compilation = CSharpCompilation.Create("AuthorityCheckpointBoundary",
            new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("Probe").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { throw error.InnerException; }
    }

    private const string Harness = """
        internal static class Trace {
            internal static readonly List<string> Calls = new();
            internal static int Warnings;
        }
        internal static class ModClass {
            internal static void LogWarning(string message) { Trace.Warnings++; }
        }
        namespace core.db {
            internal static class LineageArchiveCheckpointService {
                internal static Exception Failure;
                internal static void Shutdown() {
                    Trace.Calls.Add("checkpoint");
                    if (Failure != null) throw Failure;
                }
            }
        }
        public static class Probe {
            public static void Run() {
                core.db.LineageArchiveCheckpointService.Failure = new TimeoutException("checkpoint I/O stalled");
                Runtime.Reset();
                if (Trace.Calls.Count != EXPECTED_CLEANUPS + 1 || Trace.Warnings != 1 ||
                    Trace.Calls[Trace.Calls.Count - 1] != "checkpoint")
                    throw new InvalidOperationException("checkpoint timeout must not skip runtime cache cleanup and must be reported");
                Trace.Calls.Clear();
                var fatal = new InvalidOperationException("different lifecycle failure");
                core.db.LineageArchiveCheckpointService.Failure = fatal;
                Exception actual = null;
                try { Runtime.Reset(); } catch (Exception error) { actual = error; }
                if (!ReferenceEquals(actual, fatal))
                    throw new InvalidOperationException("runtime reset must not hide unrelated lifecycle failures");
                core.db.LineageArchiveCheckpointService.Failure = null;
                Trace.Calls.Clear();
                Runtime.Reset();
                if (Trace.Calls.Count != EXPECTED_CLEANUPS + 1)
                    throw new InvalidOperationException("runtime reset can retry completed checkpoint cleanup");
            }
        }
        """;
}
