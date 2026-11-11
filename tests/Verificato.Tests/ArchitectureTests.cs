using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Verificato.Analysis.CSharp;
using Verificato.Core;

namespace Verificato.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public async Task MapsProjectsToComponentsWithDirectedEvidenceAndContainerScopes()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><ProjectReference Include="../Core/Core.csproj" />
            <ProjectReference Include="../Store/Store.csproj" Condition="'$(Configuration)' == 'Debug'" /></ItemGroup></Project>
            """);
        await Write(directory.Path, "Core/Core.csproj", ProjectXml);
        await Write(directory.Path, "Store/Store.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Core/Core.csproj\" /><PackageReference Include=\"Microsoft.Data.Sqlite\" Version=\"10.0.14\" /></ItemGroup></Project>");
        await Write(directory.Path, "App/Program.cs", "System.Console.WriteLine(\"hello\");");
        await Write(directory.Path, "Core/OrderService.cs", """
            namespace Shop.Core;
            /// <summary>Retrieves orders from the local catalog.</summary>
            public class OrderService
            {
                /// <summary>Find an order by identifier.</summary>
                public string FindOrder(int id) => "order";
                public record Result(string Name);
            }
            """);
        await Write(directory.Path, "Store/Store.cs", "namespace Shop.Store; public class Repository { }");
        await Write(directory.Path, "Tests/Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include=\"../App/App.csproj\" /></ItemGroup></Project>");
        await Write(directory.Path, "Worker/Worker.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
        await Write(directory.Path, "Core/obj/Generated.cs", "public class MustNotAppear {}");
        var before = TreeHash(directory.Path);
        var document = await Analyze(directory);
        Assert.Equal(5, document.Components.Count);
        Assert.Equal(4, document.Relationships.Count);
        var core = document.Components.Single(c => c.Name == "Core");
        var app = document.Components.Single(c => c.Name == "App");
        Assert.Contains("Retrieves orders", core.Description);
        Assert.Equal("XML documentation publicznych typów", core.DescriptionBasis);
        var file = Assert.Single(core.Files);
        Assert.Equal("Core/OrderService.cs", file.Path);
        Assert.Equal(2, file.Types.Count);
        var type = file.Types.Single(t => t.Name == "OrderService");
        Assert.Equal("Shop.Core.OrderService", type.FullName);
        Assert.Contains(type.Members, m => m.Name == "FindOrder" && m.Line == 6 && m.Signature == "string FindOrder(int id)");
        Assert.Equal("Shop.Core", file.Types.Single(t => t.Name == "Result").Namespace);
        Assert.DoesNotContain(core.Namespaces, n => n == "Shop.Core.OrderService");
        var reference = document.Relationships.Single(r => r.SourceId == app.Id && r.TargetId == core.Id);
        Assert.Equal("App/App.csproj", reference.EvidenceFile);
        Assert.True(reference.Line > 0);
        Assert.Contains(document.Relationships, r => r.Condition != null && r.Condition.Contains("Configuration"));
        Assert.True(document.Components.Single(c => c.Name == "Tests").IsTest);
        var container = document.Containers.Single(c => c.Id == app.Id);
        Assert.Equal(3, container.ComponentIds.Count);
        Assert.DoesNotContain(document.Components.Single(c => c.Name == "Tests").Id, container.ComponentIds);
        Assert.DoesNotContain(document.Components.Single(c => c.Name == "Worker").Id, container.ComponentIds);
        Assert.Equal(before, TreeHash(directory.Path));
        Assert.Equal(document.SourceFingerprint, (await Analyze(directory)).SourceFingerprint);
    }
    [Fact]
    public async Task DoesNotEvaluateMsbuildOrFollowOutOfScopeReferences()
    {
        using var directory = new TestDirectory();
        using var outside = new TestDirectory();
        var marker = Path.Combine(directory.Path, "executed.txt");
        await Write(outside.Path, "Outside.csproj", ProjectXml);
        await Write(directory.Path, "App.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
            <ProjectReference Include="{Path.Combine(outside.Path, "Outside.csproj")}" />
            <ProjectReference Include="$(ProjectRoot)/Unknown.csproj" />
            <ProjectReference Include="Missing.csproj" />
            </ItemGroup><Target Name="Dangerous" BeforeTargets="Build"><WriteLinesToFile File="{marker}" Lines="executed" /></Target></Project>
            """);
        await Write(directory.Path, "App.cs", "class App { }");
        var before = TreeHash(directory.Path);
        var document = await Analyze(directory);
        Assert.Single(document.Components);
        Assert.Empty(document.Relationships);
        Assert.Equal(3, document.UnresolvedReferences.Count);
        Assert.Contains(document.UnresolvedReferences, r => r.Reason.Contains("Poza"));
        Assert.Contains(document.UnresolvedReferences, r => r.Reason.Contains("ewaluacji"));
        Assert.False(File.Exists(marker));
        Assert.Equal(before, TreeHash(directory.Path));
    }
    [Fact]
    public async Task HandlesCyclesAndAssignsFilesToNearestProjectOnly()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "Root.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup><ProjectReference Include=\"Nested/Nested.csproj\" /></ItemGroup></Project>");
        await Write(directory.Path, "Root.cs", "class Root {}");
        await Write(directory.Path, "Nested/Nested.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Root.csproj\" /></ItemGroup></Project>");
        await Write(directory.Path, "Nested/Nested.cs", "class Nested {}");
        var document = await Analyze(directory);
        Assert.Single(document.Components.Single(c => c.Name == "Root").Files);
        Assert.Equal("Root.cs", document.Components.Single(c => c.Name == "Root").Files[0].Path);
        Assert.Equal(2, Assert.Single(document.Containers).ComponentIds.Count);
    }
    [Fact]
    public async Task RejectsDtdAndDoesNotInventComponentsForUnsafeXml()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "Unsafe.csproj", "<!DOCTYPE Project [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><Project><Description>&x;</Description></Project>");
        var document = await Analyze(directory);
        Assert.Empty(document.Components);
        Assert.Contains(document.Warnings, w => w.Contains("XML"));
    }
    [Fact]
    public async Task SourcePreviewIsAllowlistedBoundedAndChecksSnapshotHash()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "App.csproj", ProjectXml);
        await Write(directory.Path, "App.cs", "namespace Example;\npublic class App {\n public void Execute() {}\n}");
        var analyzer = new ArchitectureAnalyzer();
        var document = await Analyze(directory);
        var component = Assert.Single(document.Components);
        var file = Assert.Single(component.Files);
        var source = await analyzer.ReadSourceAsync(directory.Project() with { Id = document.ProjectId }, document, file.Id, 3, CancellationToken.None);
        Assert.Contains(source.Lines, l => l.Number == 3 && l.Text.Contains("Execute"));
        Assert.True(source.Lines.Count <= 120);
        var project = directory.Project() with { Id = document.ProjectId };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => analyzer.ReadSourceAsync(project, document, "../outside", 1, CancellationToken.None));
        var xml = await analyzer.ReadSourceAsync(project, document, component.ProjectFileId, 1, CancellationToken.None);
        Assert.Contains(xml.Lines, l => l.Text.Contains("Microsoft.NET.Sdk"));
        await Write(directory.Path, "App.cs", "class Changed {}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => analyzer.ReadSourceAsync(project, document, file.Id, 1, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => analyzer.ReadSourceAsync(project with { SourcePath = Path.GetTempPath() }, document, file.Id, 1, CancellationToken.None));
    }
    [Fact]
    public async Task PersistsSnapshotsAndCascadesTheirDeletionWithTheRun()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "App.csproj", ProjectXml);
        await Write(directory.Path, "App.cs", "class App {}");
        using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        var run = await db.CreateRunAsync(project, RunKind.CodeAnalysis);
        var document = await new ArchitectureAnalyzer().AnalyzeAsync(project, run.Id, CancellationToken.None);
        await db.SaveArchitectureAsync(document);
        Assert.Equal(document.Id, (await db.GetArchitectureAsync(project.Id))!.Id);
        Assert.Equal(document.Id, (await db.GetArchitectureAsync(project.Id, document.Id))!.Id);
        Assert.Null(await db.GetArchitectureAsync(Guid.NewGuid(), document.Id));
        await db.DeleteRunAsync(run.Id);
        Assert.Null(await db.GetArchitectureAsync(project.Id));
    }
    [Fact]
    public async Task MigratesV1WithConsistentBackupAndPreservesExistingProjects()
    {
        using var directory = new TestDirectory();
        Guid id;
        using (var db = await directory.Store()) { var project = directory.Project(); id = project.Id; await db.SaveProjectAsync(project); }
        var dbPath = Path.Combine(directory.Path, "data", "verificato.db");
        await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE ArchitectureSnapshots; PRAGMA user_version=1;"; await command.ExecuteNonQueryAsync();
        }
        using (var upgraded = await directory.Store()) Assert.Equal(id, Assert.Single(await upgraded.GetProjectsAsync()).Id);
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "data"), "verificato.pre-v2-*.db"));
        await using var verify = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        await verify.OpenAsync();
        using var version = verify.CreateCommand(); version.CommandText = "PRAGMA user_version"; Assert.Equal(1L, await version.ExecuteScalarAsync());
        using var count = verify.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM Projects"; Assert.Equal(1L, await count.ExecuteScalarAsync());
    }
    [Fact]
    public async Task DuplicateReferencesOnOneXmlLineHaveUniqueEvidenceIds()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "App/App.csproj", "<Project><ItemGroup><ProjectReference Include=\"../Core/Core.csproj\" /><ProjectReference Include=\"../Core/Core.csproj\" Condition=\"true\" /></ItemGroup></Project>");
        await Write(directory.Path, "Core/Core.csproj", ProjectXml);
        var document = await Analyze(directory);
        Assert.Equal(2, document.Relationships.Count);
        Assert.Equal(2, document.Relationships.Select(r => r.Id).Distinct().Count());
    }
    [Fact]
    public async Task CancellationStopsInventoryWithoutCreatingASnapshot()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "App.csproj", ProjectXml);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ArchitectureAnalyzer().AnalyzeAsync(directory.Project(), Guid.NewGuid(), stop.Token));
    }
    [Fact]
    public async Task RejectsOversizedSnapshotDuringSerializationWithoutSavingIt()
    {
        using var directory = new TestDirectory();
        await Write(directory.Path, "App.csproj", ProjectXml);
        using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        var run = await db.CreateRunAsync(project, RunKind.CodeAnalysis);
        var document = await new ArchitectureAnalyzer().AnalyzeAsync(project, run.Id, CancellationToken.None);
        var large = document with { Warnings = Enumerable.Repeat(new string('x', 1000), 25_001).ToArray() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveArchitectureAsync(large));
        Assert.Null(await db.GetArchitectureAsync(project.Id));
    }
    private const string ProjectXml = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
    private static Task<ArchitectureDocument> Analyze(TestDirectory directory) => new ArchitectureAnalyzer().AnalyzeAsync(directory.Project(), Guid.NewGuid(), CancellationToken.None);
    private static async Task Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, content);
    }
    private static string TreeHash(string root) => string.Join(";", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order()
        .Select(x => Path.GetRelativePath(root, x) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))));
}
