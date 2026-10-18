using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

try
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root != null && !File.Exists(Path.Combine(root.FullName,
               "Code/core/performance/AWCooperativeActorPostRunner.cs"))) root = root.Parent;
    if (root == null) throw new DirectoryNotFoundException("Production source missing.");
    string path = args.Length > 0 ? args[0] : Path.Combine(root.FullName,
        "Code/core/performance/AWCooperativeActorPostRunner.cs");
    var type = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()
        .DescendantNodes().OfType<ClassDeclarationSyntax>()
        .Single(c => c.Identifier.Text == "AWCooperativeActorPostRunner");
    string[] nested = { "SearchWorkItem", "CandidateView", "EnemyPrepareBatchWork" };
    var members = type.Members.OfType<ClassDeclarationSyntax>()
        .Where(c => nested.Contains(c.Identifier.Text)).Select(c => c.ToFullString()).ToList();
    if (members.Count != nested.Length) throw new Exception("Production classes missing.");
    members.Add(type.Members.OfType<EnumDeclarationSyntax>().Single(c =>
        c.Identifier.Text == "EnemyPrepareKind").ToFullString());
    members.Add(type.Members.OfType<MethodDeclarationSyntax>().Single(m =>
        m.Identifier.Text == "CommitEnemySearchClassification").ToFullString());
    members.AddRange(type.Members.OfType<MethodDeclarationSyntax>().Where(m =>
        m.Identifier.Text == "IsLiveEnemySearchActor").Select(m => m.ToFullString()));
    string production = "using System; using System.Collections; using System.Collections.Generic; " +
        "namespace AncientWarfare3.core.performance { internal sealed partial class AWCooperativeActorPostRunner { " +
        string.Join("\n", members) + " } }";
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
    var compilation = CSharpCompilation.Create("EnemySearchLifecycle", new[]
    {
        CSharpSyntaxTree.ParseText(production),
        CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,
            "Tests/ActorEnemySearchLifecycle.Isolated.Tests/RuntimeCases.cs.txt"))),
        CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,
            "Code/core/performance/AWEnemySearchBackoffRules.cs")))
    }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    using var stream = new MemoryStream();
    var result = compilation.Emit(stream);
    if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics));
    var runtime = Assembly.Load(stream.ToArray());
    try { runtime.GetType("AncientWarfare3.core.performance.AWCooperativeActorPostRunner")
        .GetMethod("RunCases").Invoke(null, null); }
    catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
