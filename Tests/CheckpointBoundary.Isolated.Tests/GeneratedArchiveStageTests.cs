using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class GeneratedArchiveStageTests
{
    internal static void Run()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(),
            "Code/core/multiplayer/AW3RuntimeRestorePipeline.cs");
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        string[] types = { "AW3RestoreStage", "AW3RestoreResult", "AW3RuntimeRestoreStageRunner" };
        string definitions = string.Join("\n", tree.DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Where(type => types.Contains(type.Identifier.Text))
            .Select(type => type.ToFullString()));
        var generated = tree.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "TryInitializeGeneratedWorld");
        var stage = generated.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Single(expression => expression.Type.ToString() == "AW3RestoreStage" &&
                expression.ArgumentList.Arguments[0].Expression.ToString() == "\"archive_create\"");
        string source = "using System; using System.Collections.Generic; " + definitions + "\n" +
            "internal static class StageFactory { internal static AW3RestoreStage Create() => " + stage + "; }\n" + Harness;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(file => MetadataReference.CreateFromFile(file));
        var compilation = CSharpCompilation.Create("GeneratedArchiveBoundary",
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
        internal sealed class LineageArchiveManager {
            internal static readonly LineageArchiveManager Instance = new();
            internal bool FailCreate;
            internal bool IsOperational;
            // CreateDataBase catches shutdown/native failures and returns with
            // IsOperational=false while the previous connection can still exist.
            internal void CreateDataBase() { IsOperational = !FailCreate; }
        }
        public static class Probe {
            public static void Run() {
                foreach (bool strict in new[] { false, true }) {
                    int projections = 0;
                    LineageArchiveManager.Instance.FailCreate = true;
                    var result = AW3RuntimeRestoreStageRunner.Run(new[] {
                        StageFactory.Create(), new AW3RestoreStage("projection", () => projections++)
                    }, strict);
                    if (result.Success || result.FailedStage != "archive_create" || projections != 0)
                        throw new InvalidOperationException("failed archive creation must stop generated-world projections even in normal restore mode");
                    LineageArchiveManager.Instance.FailCreate = false;
                    result = AW3RuntimeRestoreStageRunner.Run(new[] {
                        StageFactory.Create(), new AW3RestoreStage("projection", () => projections++)
                    }, strict);
                    if (!result.Success || projections != 1)
                        throw new InvalidOperationException("a successful retry must proceed with generated-world projections");
                }
            }
        }
        """;
}
