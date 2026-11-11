using System.Security.Cryptography;
using Verificato.Analysis.CSharp;
using Verificato.Core;

namespace Verificato.Tests;

public sealed class AnalysisTests
{
    [Fact]
    public async Task ReportsSyntaxAndEmptyCatchWithoutWritingToSource()
    {
        using var directory = new TestDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Sample.cs"), "class Test { void Work() { try { } catch { } int x = ; } }");
        var before = HashFiles(directory.Path);
        var sink = new CollectingSink();
        var result = await new RoslynCodeAnalyzer().AnalyzeAsync(CreateRun(directory.Project()), sink, CancellationToken.None);
        Assert.Equal(Completeness.Partial, result.Completeness);
        Assert.Contains(sink.Items, x => x.Category == Category.Compilation && x.Severity == Severity.Error && x.Line == 1 && x.FilePath == "Sample.cs");
        Assert.Contains(sink.Items, x => x.RuleId == "quality.empty-catch");
        Assert.Contains(sink.Items, x => x.RuleId == "code.files" && x.Value == 1);
        Assert.Equal(before, HashFiles(directory.Path));
    }
    [Fact]
    public async Task NeverExecutesCustomMsbuildTargetsAndIgnoresBuildDirectories()
    {
        using var directory = new TestDirectory();
        var marker = Path.Combine(directory.Path, "must-not-exist.txt");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Test.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <Target Name="Dangerous" BeforeTargets="Build"><WriteLinesToFile File="{marker}" Lines="executed" /></Target></Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Test.cs"), "class Test { }");
        Directory.CreateDirectory(Path.Combine(directory.Path, "obj"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "obj", "Generated.cs"), "this is invalid source");
        var before = HashFiles(directory.Path);
        var sink = new CollectingSink();
        var result = await new RoslynCodeAnalyzer().AnalyzeAsync(CreateRun(directory.Project()), sink, CancellationToken.None);
        Assert.Equal(Completeness.Partial, result.Completeness);
        Assert.False(File.Exists(marker));
        Assert.DoesNotContain(sink.Items, x => x.Category == Category.Compilation);
        Assert.Equal(before, HashFiles(directory.Path));
    }
    [Fact]
    public async Task SupportsSemanticDiagnosticsWhenLocalNet10ReferencePackExists()
    {
        using var directory = new TestDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Test.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Test.cs"), "class Test { int Number = \"not an int\"; }");
        var sink = new CollectingSink();
        var result = await new RoslynCodeAnalyzer().AnalyzeAsync(CreateRun(directory.Project()), sink, CancellationToken.None);
        if (result.Completeness == Completeness.Partial)
            Assert.Contains("targeting pack", result.Limitation);
        else Assert.Contains(sink.Items, x => x.RuleId == "CS0029" && x.FilePath == "Test.cs" && x.Line == 1);
    }
    private static Run CreateRun(Project project) => new(Guid.NewGuid(), project.Id, RunKind.CodeAnalysis, RunState.Running,
        RunOutcome.Inconclusive, Completeness.CompleteWithinScope, DateTimeOffset.UtcNow, null, project);
    private static string HashFiles(string root) => string.Join(";", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order()
        .Select(x => Path.GetRelativePath(root, x) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))));
}
