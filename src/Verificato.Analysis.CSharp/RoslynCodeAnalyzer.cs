using System.Xml;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Verificato.Core;

namespace Verificato.Analysis.CSharp;

/// <summary>Never evaluates MSBuild, restores dependencies, loads analyzers or executes source generators.</summary>
public sealed class RoslynCodeAnalyzer : ICodeAnalyzer
{
    public string Id => "verificato.csharp/0.1";
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", ".vs", ".idea", "packages" };
    public async Task<AnalysisResult> AnalyzeAsync(Run run, IObservationSink sink, CancellationToken ct)
    {
        var root = run.Configuration.SourcePath;
        var files = new List<string>();
        var projects = new List<string>();
        Enumerate(root, files, projects, ct);
        if (files.Count == 0) return new(Completeness.Unavailable, "Nie znaleziono plików C# w dozwolonym zakresie.");
        var limitations = new List<string>();
        XDocument? project = null;
        if (projects.Count == 1)
        {
            if (new FileInfo(projects[0]).Length > 1_000_000) throw new InvalidOperationException("Plik projektu przekracza limit 1 MB.");
            using var reader = XmlReader.Create(projects[0], new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            project = XDocument.Load(reader);
        }
        else limitations.Add("Wybrany katalog nie zawiera dokładnie jednego projektu C#. Diagnostyka semantyczna pominięta.");
        var supported = project is not null && IsSupported(project, root, projects[0]);
        if (project is not null && !supported) limitations.Add("Niestandardowy lub nieobsługiwany kontekst projektu; wykonano analizę składni i metryk bez MSBuild.");
        var trees = new List<SyntaxTree>();
        var emittedDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols:
            new[] { "NET", "NET10_0", "NETCOREAPP", "TRACE", "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP1_1_OR_GREATER", "NETCOREAPP2_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP2_2_OR_GREATER", "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER" }
                .Concat(Enumerable.Range(5, 6).Select(x => $"NET{x}_0_OR_GREATER")));
        long bytes = 0, lines = 0, methods = 0, types = 0;
        foreach (var path in files.Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            bytes += info.Length;
            if (info.Length > 2_000_000 || bytes > 50_000_000)
            {
                limitations.Add("Osiągnięto limit rozmiaru źródeł (2 MB/plik, 50 MB/analiza).");
                supported = false;
                continue;
            }
            var modifiedAt = info.LastWriteTimeUtc;
            var text = await File.ReadAllTextAsync(path, ct);
            info.Refresh();
            if (info.LastWriteTimeUtc != modifiedAt)
            {
                supported = false;
                limitations.Add("Wykryto zmianę źródeł w trakcie odczytu; migawka nie jest atomowa.");
            }
            sourceHash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path) + "\0" + text + "\0"));
            var tree = CSharpSyntaxTree.ParseText(text, parseOptions, path, cancellationToken: ct);
            trees.Add(tree);
            lines += tree.GetText(ct).Lines.Count;
            var syntax = await tree.GetRootAsync(ct);
            methods += syntax.DescendantNodes().OfType<BaseMethodDeclarationSyntax>().Count();
            types += syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Count();
            foreach (var diagnostic in tree.GetDiagnostics(ct)) EmitDiagnostic(diagnostic);
            foreach (var block in syntax.DescendantNodes().OfType<CatchClauseSyntax>().Where(x => x.Block.Statements.Count == 0))
            {
                var pos = block.GetLocation().GetLineSpan().StartLinePosition;
                sink.Emit(new(run.Id, "quality.empty-catch", Category.CodeQuality, Severity.Warning,
                    "Pusty blok catch", "Wyjątek jest ignorowany bez obsługi lub udokumentowania intencji.",
                    FilePath: Path.GetRelativePath(root, path), Line: pos.Line + 1, Column: pos.Character + 1));
            }
            foreach (var method in syntax.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                var complexity = Complexity(method);
                if (complexity > 10)
                {
                    var pos = method.GetLocation().GetLineSpan().StartLinePosition;
                    sink.Emit(new(run.Id, "quality.complexity", Category.CodeQuality, Severity.Warning,
                        "Wysoka złożoność metody", $"Złożoność cyklomatyczna {complexity}; próg 10.",
                        FilePath: Path.GetRelativePath(root, path), Line: pos.Line + 1, Column: pos.Character + 1));
                }
            }
        }
        var refs = supported ? FindReferencePack() : [];
        if (supported && refs.Count == 0) { supported = false; limitations.Add("Brak lokalnego targeting packa .NET 10. Zainstaluj .NET 10 SDK; nie wykonano restore."); }
        if (supported)
        {
            var implicitUsings = project!.Descendants("ImplicitUsings").FirstOrDefault()?.Value is "enable" or "true";
            if (implicitUsings)
                trees.Add(CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;", path: "verificato://implicit-usings"));
            var nullable = project!.Descendants("Nullable").FirstOrDefault()?.Value == "enable";
            var output = project.Descendants("OutputType").FirstOrDefault()?.Value;
            var compilation = CSharpCompilation.Create("VerificatoReadOnly", trees, refs,
                new CSharpCompilationOptions(output == "WinExe" ? OutputKind.WindowsApplication : output == "Exe" ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable));
            foreach (var diagnostic in compilation.GetDiagnostics(ct).Where(x => x.Location.SourceTree?.FilePath != "verificato://implicit-usings"))
            {
                EmitDiagnostic(diagnostic);
            }
        }
        EmitMetric("files", "Pliki C#", trees.Count(x => !x.FilePath.StartsWith("verificato:")), "files");
        EmitMetric("loc", "Fizyczne linie kodu", lines, "lines");
        EmitMetric("methods", "Metody i konstruktory", methods, "methods");
        EmitMetric("types", "Typy", types, "types");
        var totalComplexity = trees.Sum(t => t.GetRoot(ct).DescendantNodes().OfType<BaseMethodDeclarationSyntax>().Sum(Complexity));
        EmitMetric("complexity", "Suma złożoności metod", totalComplexity, "score");
        if (!supported && limitations.Count == 0) limitations.Add("Niepełny kontekst kompilacji; dostępna analiza składni, reguł i metryk.");
        limitations.Add("Nie wykonano source generatorów ani analizatorów z repozytorium. Wynik nie zastępuje pełnego builda.");
        var limitation = string.Join(" ", limitations.Distinct());
        sink.Emit(new(run.Id, "analysis.source-fingerprint", Category.System, Severity.Info, "Migawka źródeł · SHA-256",
            Convert.ToHexString(sourceHash.GetHashAndReset()), IsIssue: false));
        sink.Emit(new(run.Id, "analysis.scope", Category.System, Severity.Info, "Zakres analizy C#", limitation, IsIssue: false));
        return new(supported ? Completeness.CompleteWithinScope : Completeness.Partial, limitation);

        void EmitMetric(string name, string title, double value, string unit) => sink.Emit(new(run.Id,
            "code." + name, Category.CodeQuality, Severity.Info, title, "Analiza tylko do odczytu.", Value: value, Unit: unit, IsIssue: false));
        void EmitDiagnostic(Diagnostic diagnostic)
        {
            if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)) return;
            var location = diagnostic.Location.GetLineSpan();
            var key = $"{diagnostic.Id}|{location.Path}|{diagnostic.Location.SourceSpan}|{diagnostic.GetMessage()}";
            if (!emittedDiagnostics.Add(key)) return;
            sink.Emit(new(run.Id, diagnostic.Id, Category.Compilation,
                diagnostic.Severity == DiagnosticSeverity.Error ? Severity.Error : Severity.Warning,
                diagnostic.Id, diagnostic.GetMessage(), FilePath: string.IsNullOrEmpty(location.Path) ? null : Path.GetRelativePath(root, location.Path),
                Line: diagnostic.Location.IsInSource ? location.StartLinePosition.Line + 1 : null,
                Column: diagnostic.Location.IsInSource ? location.StartLinePosition.Character + 1 : null));
        }
    }

    public static int Complexity(BaseMethodDeclarationSyntax method) => 1 + method.DescendantNodes().Count(n =>
        n is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
            or CatchClauseSyntax or ConditionalExpressionSyntax or CaseSwitchLabelSyntax ||
        n is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression);

    private static void Enumerate(string root, List<string> files, List<string> projects, CancellationToken ct, int depth = 0)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 64) throw new InvalidOperationException("Maksymalna głębokość analizy to 64 katalogi.");
        foreach (var file in Directory.EnumerateFiles(root))
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) files.Add(file);
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) projects.Add(file);
            if (files.Count > 10_000) throw new InvalidOperationException("Limit 10 000 plików C# na analizę.");
        }
        foreach (var directory in Directory.EnumerateDirectories(root))
            if (!Excluded.Contains(Path.GetFileName(directory)) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                Enumerate(directory, files, projects, ct, depth + 1);
    }
    private static bool IsSupported(XDocument project, string root, string path)
    {
        var element = project.Root;
        var properties = new HashSet<string>(StringComparer.Ordinal) { "TargetFramework", "ImplicitUsings", "Nullable", "OutputType" };
        // Whitelist instead of guessing MSBuild semantics. Unknown inputs yield syntax-only/Partial.
        if (element?.Name != "Project" || element.Attribute("Sdk")?.Value != "Microsoft.NET.Sdk" ||
            element.Attributes().Any(x => x.Name != "Sdk") ||
            element.Elements().Any(x => x.Name != "PropertyGroup" || x.HasAttributes) ||
            element.Elements().SelectMany(x => x.Elements()).Any(x => !properties.Contains(x.Name.LocalName) || x.HasAttributes || x.HasElements) ||
            properties.Any(name => project.Descendants(name).Count() > 1) ||
            project.Descendants("TargetFramework").FirstOrDefault()?.Value != "net10.0" ||
            project.Descendants("Nullable").Any(x => x.Value is not ("enable" or "disable")) ||
            project.Descendants("ImplicitUsings").Any(x => x.Value is not ("enable" or "disable" or "true" or "false")) ||
            project.Descendants("OutputType").Any(x => x.Value is not ("Exe" or "WinExe" or "Library")) ||
            Path.GetFullPath(Path.GetDirectoryName(path)!) != Path.GetFullPath(root)) return false;
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) || File.Exists(Path.Combine(directory.FullName, "Directory.Build.targets"))) return false;
        return true;
    }
    private static List<MetadataReference> FindReferencePack()
    {
        var roots = new[] { Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"), "/usr/share/dotnet", "/usr/local/share/dotnet" };
        foreach (var root in roots.Where(x => !string.IsNullOrEmpty(x)))
        {
            var packs = Path.Combine(root!, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packs)) continue;
            var pack = Directory.GetDirectories(packs).Where(x => Path.GetFileName(x).StartsWith("10.", StringComparison.Ordinal))
                .OrderDescending().Select(x => Path.Combine(x, "ref", "net10.0")).FirstOrDefault(Directory.Exists);
            if (pack is not null) return Directory.GetFiles(pack, "*.dll").Select(x => (MetadataReference)MetadataReference.CreateFromFile(x)).ToList();
        }
        return [];
    }
}
